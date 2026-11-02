using LibreSpotUWP.Helpers;
using LibreSpotUWP.Interfaces;
using LibreSpotUWP.Models;
using LibreSpotUWP.Services.HomeAssistant;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Background;
using Windows.Networking.Connectivity;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.Web;

namespace LibreSpotUWP.Services
{
    internal sealed class HomeAssistantLinkStatusInfo
    {
        public HaLinkStatus Status { get; set; }
        public HaLinkFailure Failure { get; set; }
        // What the platform granted for standby wake-ups (the WP0 question), e.g. "hardware slot".
        public string Standby { get; set; }

        public string Text
        {
            get
            {
                var text = HaLinkPolicy.Describe(Status, Failure);
                return Status == HaLinkStatus.Connected && !string.IsNullOrEmpty(Standby)
                    ? text + " Standby wake-up: " + Standby + "."
                    : text;
            }
        }
    }

    // The Lumia's side of the Home Assistant link: an outbound WebSocket that the phone dials and keeps
    // open, owned by a ControlChannelTrigger like the Spotify Dealer channel, so that a command from
    // Home Assistant can wake the app in standby. Protocol logic lives in HaLinkSession (tested off-device);
    // this class only owns the socket, the trigger, timers and reconnection. With the setting off it
    // creates no socket, trigger or background task.
    internal sealed class HomeAssistantLinkService
    {
        public const string SecretKey = "HomeAssistantLinkSecret";
        private const string TaskPrefix = "LibreSpotUWP.HaLink.";
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan WakeBudget = TimeSpan.FromSeconds(20);
        private const uint TransportMaxBytes = 1048576;

        private static HomeAssistantLinkService _current;

        private readonly IMediaService _media;
        private readonly ISecureStorage _secrets;
        private readonly SemaphoreSlim _lifecycle = new SemaphoreSlim(1, 1);
        private readonly object _sync = new object();
        private readonly Random _random = new Random();
        private CancellationTokenSource _run;
        private Task _loop;
        private Connection _connection;
        private TaskCompletionSource<bool> _retryNow = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private HomeAssistantLinkStatusInfo _status = new HomeAssistantLinkStatusInfo { Status = HaLinkStatus.Disabled };
        private bool _networkHooked;

        public HomeAssistantLinkService(IMediaService media, ISecureStorage secrets)
        {
            _media = media ?? throw new ArgumentNullException(nameof(media));
            _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            _current = this;
        }

        public event EventHandler<HomeAssistantLinkStatusInfo> StatusChanged;

        public HomeAssistantLinkStatusInfo Status { get { lock (_sync) return _status; } }

        public static bool OwnsTask(string name) => name != null && name.StartsWith(TaskPrefix, StringComparison.Ordinal);

        // Removes trigger tasks left behind by a previous process. Only this link's own prefix is touched.
        public static void CleanupStaleTasks()
        {
            foreach (var task in BackgroundTaskRegistration.AllTasks.Values.ToArray())
            {
                if (!OwnsTask(task.Name)) continue;
                try { task.Unregister(true); }
                catch (Exception ex) { LogService.Warn($"Home Assistant link task cleanup failed: HRESULT=0x{ex.HResult:X8}."); }
            }
        }

        // Called from App.OnBackgroundActivated for a push from Home Assistant while in standby: keep the
        // deferral until the frame that woke us has been handled and its ack/state frames are written.
        public static async Task HandleWakeAsync(CancellationToken cancellation)
        {
            var connection = _current?.CurrentConnection;
            if (connection == null) return;
            using (var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                budget.CancelAfter(WakeBudget);
                try { await connection.DrainAsync(budget.Token).ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    LogService.Warn($"Home Assistant link wake ended before the work finished; systemCancelled={cancellation.IsCancellationRequested}.");
                }
            }
        }

        public async Task<bool> HasSecretAsync() => !string.IsNullOrEmpty(await _secrets.LoadAsync(SecretKey).ConfigureAwait(false));

        public Task SaveSecretAsync(string secret) => _secrets.SaveAsync(SecretKey, secret);

        // (Re)reads the settings and starts or stops the link accordingly. Safe to call repeatedly.
        public async Task ApplySettingsAsync()
        {
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                await StopCoreAsync().ConfigureAwait(false);
                if (!UserSettings.HomeAssistantLinkEnabled)
                {
                    SetStatus(HaLinkStatus.Disabled, HaLinkFailure.None, null);
                    return;
                }

                string url, secret, error;
                var storedSecret = await _secrets.LoadAsync(SecretKey).ConfigureAwait(false);
                if (!HaLinkPolicy.TryNormalizeUrl(UserSettings.HomeAssistantLinkUrl, out url, out error) ||
                    !HaLinkPolicy.TryNormalizeSecret(storedSecret, out secret, out error))
                {
                    SetStatus(HaLinkStatus.NotConfigured, HaLinkFailure.None, null);
                    return;
                }

                var run = new CancellationTokenSource();
                _run = run;
                HookNetwork(true);
                _loop = Task.Run(() => RunAsync(new Uri(url), secret, run.Token));
                LogService.Info($"Home Assistant link enabled; scheme={new Uri(url).Scheme}.");
            }
            finally { _lifecycle.Release(); }
        }

        public async Task StopAsync()
        {
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                await StopCoreAsync().ConfigureAwait(false);
                SetStatus(HaLinkStatus.Disabled, HaLinkFailure.None, null);
            }
            finally { _lifecycle.Release(); }
        }

        // Network came back or the app resumed: skip the rest of a backoff wait.
        public void RetrySoon()
        {
            TaskCompletionSource<bool> signal;
            lock (_sync)
            {
                signal = _retryNow;
                _retryNow = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            signal.TrySetResult(true);
        }

        private Connection CurrentConnection { get { lock (_sync) return _connection; } }

        private async Task StopCoreAsync()
        {
            var run = _run;
            var loop = _loop;
            _run = null;
            _loop = null;
            HookNetwork(false);
            if (run == null) return;
            run.Cancel();
            CurrentConnection?.Abort();
            if (loop != null)
            {
                try { await Task.WhenAny(loop, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false); }
                catch (Exception ex) { LogService.Warn($"Home Assistant link stop failed: HRESULT=0x{ex.HResult:X8}."); }
            }
            run.Dispose();
            CleanupStaleTasks();
        }

        private async Task RunAsync(Uri endpoint, string secret, CancellationToken token)
        {
            var attempt = 0;
            while (!token.IsCancellationRequested)
            {
                SetStatus(HaLinkStatus.Connecting, HaLinkFailure.None, null);
                var connection = new Connection(this, endpoint, secret, token);
                lock (_sync) _connection = connection;
                HaLinkFailure failure;
                try { failure = await connection.RunAsync().ConfigureAwait(false); }
                catch (Exception ex)
                {
                    LogService.Warn($"Home Assistant link failed unexpectedly: HRESULT=0x{ex.HResult:X8}.");
                    failure = HaLinkFailure.Network;
                }
                finally
                {
                    lock (_sync) if (_connection == connection) _connection = null;
                    connection.Dispose();
                }

                if (token.IsCancellationRequested) return;
                if (connection.WasConnected) attempt = 0;
                LogService.Info($"Home Assistant link closed; reason={failure}, wasConnected={connection.WasConnected}.");
                if (!HaLinkPolicy.ShouldRetry(failure))
                {
                    SetStatus(HaLinkPolicy.StatusAfter(failure), failure, null);
                    return;
                }

                SetStatus(HaLinkStatus.Waiting, failure, null);
                var delay = HaLinkPolicy.RetryDelay(attempt++, _random.NextDouble(), failure);
                Task retryNow;
                lock (_sync) retryNow = _retryNow.Task;
                try { await Task.WhenAny(Task.Delay(delay, token), retryNow).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        private void SetStatus(HaLinkStatus status, HaLinkFailure failure, string standby)
        {
            var info = new HomeAssistantLinkStatusInfo { Status = status, Failure = failure, Standby = standby };
            lock (_sync) _status = info;
            try { StatusChanged?.Invoke(this, info); }
            catch (Exception ex) { LogService.Warn($"Home Assistant link status listener failed: HRESULT=0x{ex.HResult:X8}."); }
        }

        private void HookNetwork(bool attach)
        {
            if (attach == _networkHooked) return;
            if (attach) NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
            else NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
            _networkHooked = attach;
        }

        private void OnNetworkStatusChanged(object sender)
        {
            if (Status.Status == HaLinkStatus.Waiting) RetrySoon();
        }

        private static string AppVersion()
        {
            var version = Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }

        private sealed class Connection : IDisposable
        {
            private readonly HomeAssistantLinkService _owner;
            private readonly Uri _endpoint;
            private readonly string _secret;
            private readonly CancellationToken _token;
            private readonly MessageWebSocket _socket = new MessageWebSocket();
            private readonly SemaphoreSlim _writes = new SemaphoreSlim(1, 1);
            private readonly TaskCompletionSource<HaLinkFailure> _done = new TaskCompletionSource<HaLinkFailure>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly object _chainSync = new object();
            private Task _chain = Task.CompletedTask;
            private ControlChannelTrigger _trigger;
            private BackgroundTaskRegistration _pushTask;
            private BackgroundTaskRegistration _keepAliveTask;
            private HaLinkSession _session;
            private string _standby = "not requested";
            private int _flushScheduled;
            private int _disposed;

            public Connection(HomeAssistantLinkService owner, Uri endpoint, string secret, CancellationToken token)
            {
                _owner = owner;
                _endpoint = endpoint;
                _secret = secret;
                _token = token;
            }

            public bool WasConnected { get; private set; }

            public async Task<HaLinkFailure> RunAsync()
            {
                _socket.Control.MessageType = SocketMessageType.Utf8;
                _socket.SetRequestHeader("Authorization", "Bearer " + _secret);
                _socket.MessageReceived += Received;
                _socket.Closed += Closed;

                await AttachControlChannelAsync().ConfigureAwait(false);

                try
                {
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_token))
                    {
                        timeout.CancelAfter(ConnectTimeout);
                        await _socket.ConnectAsync(_endpoint).AsTask(timeout.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (_token.IsCancellationRequested) { return HaLinkFailure.Network; }
                catch (Exception ex)
                {
                    var web = WebSocketError.GetStatus(ex.HResult);
                    LogService.Info($"Home Assistant link connect failed; status={web}, HRESULT=0x{ex.HResult:X8}.");
                    return web == WebErrorStatus.Unauthorized || web == WebErrorStatus.Forbidden
                        ? HaLinkFailure.Unauthorized
                        : HaLinkFailure.Network;
                }

                if (_trigger != null)
                {
                    try
                    {
                        var slot = _trigger.WaitForPushEnabled();
                        _standby = slot == ControlChannelTriggerStatus.HardwareSlotAllocated ? "hardware slot"
                            : slot == ControlChannelTriggerStatus.SoftwareSlotAllocated ? "software slot (foreground only on Mobile)"
                            : "not granted (" + slot + ")";
                    }
                    catch (Exception ex) { _standby = $"not granted (HRESULT 0x{ex.HResult:X8})"; }
                }
                // WP0 evidence: whether Windows 10 Mobile grants this second control channel a hardware slot.
                LogService.Telemetry("ha-link-standby-slot", $"Home Assistant link standby wake-up: {_standby}.");

                var media = _owner._media;
                _session = new HaLinkSession(new HaLinkSessionOptions
                {
                    DeviceId = UserSettings.HomeAssistantDeviceId,
                    DeviceName = Environment.MachineName,
                    AppVersion = AppVersion(),
                    Send = SendAsync,
                    CurrentState = () => HaStateInputFactory.FromMediaState(media.Current ?? new MediaState()),
                    Control = new MediaPlaybackControl(media),
                    ScheduleFlush = ScheduleFlush,
                    Log = message => LogService.Info(message)
                });
                media.MediaStateChanged += OnMediaStateChanged;
                await _session.StartAsync().ConfigureAwait(false);
                if (_done.Task.IsCompleted) return await _done.Task.ConfigureAwait(false);

                WasConnected = true;
                _owner.SetStatus(HaLinkStatus.Connected, HaLinkFailure.None, _standby);
                using (_token.Register(() => _done.TrySetResult(HaLinkFailure.None)))
                    return await _done.Task.ConfigureAwait(false);
            }

            private async Task AttachControlChannelAsync()
            {
                var stage = "background-access";
                try
                {
                    var access = await BackgroundExecutionManager.RequestAccessAsync().AsTask().ConfigureAwait(false);
                    if (access == BackgroundAccessStatus.DeniedBySystemPolicy || access == BackgroundAccessStatus.DeniedByUser)
                    {
                        _standby = "background access denied";
                        return;
                    }
                    var name = TaskPrefix + Guid.NewGuid().ToString("N");
                    stage = "trigger-create";
                    _trigger = new ControlChannelTrigger(name, DealerChannelPolicy.KeepAliveIntervalMinutes, ControlChannelTriggerResourceType.RequestHardwareSlot);
                    stage = "push-register";
                    var push = new BackgroundTaskBuilder { Name = name + ".Push", IsNetworkRequested = true };
                    push.SetTrigger(_trigger.PushNotificationTrigger);
                    _pushTask = push.Register();
                    stage = "keepalive-register";
                    var keepAlive = new BackgroundTaskBuilder
                    {
                        Name = name + ".KeepAlive",
                        IsNetworkRequested = true,
                        TaskEntryPoint = "Windows.Networking.Sockets.WebSocketKeepAlive"
                    };
                    keepAlive.SetTrigger(_trigger.KeepAliveTrigger);
                    _keepAliveTask = keepAlive.Register();
                    stage = "transport-associate";
                    _trigger.UsingTransport(_socket);
                }
                catch (Exception ex)
                {
                    // Without a control channel the link still works while the app runs; it just cannot
                    // wake the app in standby. Record why, and continue without it.
                    _standby = $"unavailable ({stage}, HRESULT 0x{ex.HResult:X8})";
                    ReleaseControlChannel();
                }
            }

            private void OnMediaStateChanged(object sender, MediaState state)
            {
                var session = _session;
                if (session == null) return;
                Observe(session.OnStateChangedAsync(), "state");
            }

            private void Received(MessageWebSocket sender, MessageWebSocketMessageReceivedEventArgs args)
            {
                try
                {
                    if (Volatile.Read(ref _disposed) != 0 || _session == null) return;
                    string text = null;
                    var binary = args.MessageType != SocketMessageType.Utf8;
                    using (var reader = args.GetDataReader())
                    {
                        var length = reader.UnconsumedBufferLength;
                        if (length > TransportMaxBytes) { Close(1009, HaLinkFailure.TooLarge); return; }
                        if (!binary)
                        {
                            var bytes = new byte[length];
                            reader.ReadBytes(bytes);
                            text = System.Text.Encoding.UTF8.GetString(bytes, 0, bytes.Length);
                        }
                    }
                    // Frames are handled strictly in arrival order.
                    lock (_chainSync)
                        _chain = _chain.ContinueWith(_ => HandleAsync(text, binary), TaskScheduler.Default).Unwrap();
                }
                catch (Exception ex)
                {
                    LogService.Info($"Home Assistant link receive failed: HRESULT=0x{ex.HResult:X8}.");
                    _done.TrySetResult(HaLinkFailure.Network);
                }
            }

            private async Task HandleAsync(string text, bool binary)
            {
                try
                {
                    var session = _session;
                    if (session == null) return;
                    if (binary)
                    {
                        await session.OnBinaryFrameAsync().ConfigureAwait(false);
                        return;
                    }
                    var action = await session.OnFrameAsync(text).ConfigureAwait(false);
                    if (action.Close)
                        Close(action.CloseCode, action.CloseCode == HaLinkSession.CloseUnsupportedVersion ? HaLinkFailure.UnsupportedVersion : HaLinkFailure.TooLarge);
                }
                catch (Exception ex) { LogService.Warn($"Home Assistant frame handling failed: HRESULT=0x{ex.HResult:X8}."); }
            }

            private void Closed(IWebSocket sender, WebSocketClosedEventArgs args)
            {
                _done.TrySetResult(HaLinkPolicy.ClassifyClose(args.Code));
            }

            private async Task SendAsync(string frame)
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                var acquired = false;
                try
                {
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_token))
                    {
                        timeout.CancelAfter(WriteTimeout);
                        await _writes.WaitAsync(timeout.Token).ConfigureAwait(false);
                        acquired = true;
                        using (var writer = new DataWriter(_socket.OutputStream) { UnicodeEncoding = UnicodeEncoding.Utf8 })
                        {
                            try
                            {
                                writer.WriteString(frame);
                                await writer.StoreAsync().AsTask(timeout.Token).ConfigureAwait(false);
                            }
                            finally { writer.DetachStream(); }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // A failed write means the link is gone; the reconnect loop takes over.
                    LogService.Info($"Home Assistant link write failed: HRESULT=0x{ex.HResult:X8}.");
                    _done.TrySetResult(HaLinkFailure.Network);
                }
                finally { if (acquired) _writes.Release(); }
            }

            private void ScheduleFlush(TimeSpan wait)
            {
                if (Interlocked.CompareExchange(ref _flushScheduled, 1, 0) != 0) return;
                Observe(FlushLaterAsync(wait), "flush");
            }

            private async Task FlushLaterAsync(TimeSpan wait)
            {
                try { await Task.Delay(wait, _token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                // Clear before flushing so a flush that is still early can re-arm the timer.
                Interlocked.Exchange(ref _flushScheduled, 0);
                var session = _session;
                if (session != null) await session.OnFlushTimerAsync().ConfigureAwait(false);
            }

            public async Task DrainAsync(CancellationToken cancellation)
            {
                // The push that woke us may still be in flight; give MessageReceived a moment to queue it.
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellation).ConfigureAwait(false);
                while (true)
                {
                    Task chain;
                    lock (_chainSync) chain = _chain;
                    var session = _session;
                    var idle = session == null ? Task.CompletedTask : session.Idle;
                    var all = Task.WhenAll(chain, idle);
                    if (await Task.WhenAny(all, Task.Delay(Timeout.Infinite, cancellation)).ConfigureAwait(false) != all)
                        throw new OperationCanceledException(cancellation);
                    lock (_chainSync) if (chain == _chain && (session == null || session.Idle.IsCompleted)) return;
                }
            }

            private void Close(ushort code, HaLinkFailure failure)
            {
                try { _socket.Close(code, string.Empty); }
                catch (Exception ex) { LogService.Info($"Home Assistant link close failed: HRESULT=0x{ex.HResult:X8}."); }
                _done.TrySetResult(failure);
            }

            public void Abort()
            {
                _done.TrySetResult(HaLinkFailure.None);
            }

            private static void Observe(Task task, string what)
            {
                task.ContinueWith(
                    t => LogService.Warn($"Home Assistant link {what} work failed: HRESULT=0x{t.Exception.InnerException?.HResult:X8}."),
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            }

            private void ReleaseControlChannel()
            {
                Cleanup(() => _pushTask?.Unregister(true));
                Cleanup(() => _keepAliveTask?.Unregister(true));
                Cleanup(() => _trigger?.Dispose());
                _pushTask = null;
                _keepAliveTask = null;
                _trigger = null;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                _session?.Close();
                _owner._media.MediaStateChanged -= OnMediaStateChanged;
                _socket.MessageReceived -= Received;
                _socket.Closed -= Closed;
                Cleanup(() => _socket.Close(1000, string.Empty));
                Cleanup(() => _socket.Dispose());
                ReleaseControlChannel();
            }

            private static void Cleanup(Action release)
            {
                try { release(); }
                catch (Exception ex) { LogService.Info($"Home Assistant link cleanup: HRESULT=0x{ex.HResult:X8}."); }
            }
        }

        // The same IMediaService calls the lock-screen controls use; no new playback path.
        private sealed class MediaPlaybackControl : IHaPlaybackControl
        {
            private readonly IMediaService _media;
            public MediaPlaybackControl(IMediaService media) { _media = media; }
            public Task PlayAsync() => _media.ResumeAsync();
            public Task PauseAsync() => _media.PauseAsync();
            public void Next() => _media.Next();
            public void Previous() => _media.Previous();
            public void Seek(uint positionMs) => _media.Seek(positionMs);
            public Task SetVolumeAsync(ushort volume) => _media.SetVolumeAsync(volume);
            public Task SetShuffleAsync(bool enabled) => _media.SetShuffleAsync(enabled);
            public Task SetRepeatAsync(int mode) => _media.SetRepeatAsync(mode);
        }
    }
}
