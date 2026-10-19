using LibreSpotUWP.Interop;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace LibreSpotUWP.Services
{
    // The actual authenticated Dealer socket is owned by WinRT, allowing its
    // traffic to wake a network-requesting task even while playback is paused.
    internal static class WinRtDealerTransport
    {
        private const string TaskPrefix = "LibreSpotUWP.Dealer.";
        private const int MaximumFrameBytes = 2 * 1024 * 1024;
        private static readonly ConcurrentDictionary<ulong, Channel> Channels = new ConcurrentDictionary<ulong, Channel>();
        // Root delegates for the complete native lifetime, including reconnects.
        private static readonly Librespot.DealerOpenCallback OpenDelegate = Open;
        private static readonly Librespot.DealerSendCallback SendDelegate = Send;
        private static readonly Librespot.DealerCloseCallback CloseDelegate = Close;
        private static readonly Librespot.DealerActivityCallback ActivityDelegate = Activity;
        private static Task<BackgroundAccessStatus> _access;
        private static int _installed;
        private static long _latestOpened;
        public static event Action ConnectionLost;

        public static void Install()
        {
            if (Interlocked.Exchange(ref _installed, 1) != 0) return;
            try
            {
                foreach (var task in BackgroundTaskRegistration.AllTasks.Values)
                    if (task.Name.StartsWith(TaskPrefix, StringComparison.Ordinal)) task.Unregister(true);
                _access = BackgroundExecutionManager.RequestAccessAsync().AsTask();
                Librespot.librespot_dealer_register_transport(OpenDelegate, SendDelegate, CloseDelegate, ActivityDelegate);
                LogService.Info("WinRT Dealer transport installed; control tasks request network independently of PCM playback.");
            }
            catch { Interlocked.Exchange(ref _installed, 0); throw; }
        }

        public static bool OwnsTask(string name) => name != null && name.StartsWith(TaskPrefix, StringComparison.Ordinal);

        public static async Task HandleWakeAsync(CancellationToken cancellation)
        {
            await Task.WhenAll(Channels.Values.ToArray().Select(channel => channel.DrainAsync(cancellation))).ConfigureAwait(false);
        }

        private static bool Open(ulong id, IntPtr url)
        {
            try
            {
                var endpoint = new Uri(Marshal.PtrToStringAnsi(url));
                if (endpoint.Scheme != "wss" || Channels.Count >= 4) return false;
                var channel = new Channel(id);
                if (!Channels.TryAdd(id, channel)) { channel.Dispose(); return false; }
                Interlocked.Exchange(ref _latestOpened, unchecked((long)id));
                _ = Task.Run(() => channel.OpenAsync(endpoint));
                return true;
            }
            catch (Exception ex) { LogService.Warn($"Dealer open callback failed: HRESULT=0x{ex.HResult:X8}."); return false; }
        }

        private static bool Send(ulong id, IntPtr data, UIntPtr length)
        {
            try
            {
                var count = checked((int)length.ToUInt64());
                if (count < 0 || count > MaximumFrameBytes || !Channels.TryGetValue(id, out var channel)) return false;
                var bytes = new byte[count];
                Marshal.Copy(data, bytes, 0, count);
                return channel.Send(bytes);
            }
            catch (Exception ex) { LogService.Warn($"Dealer send callback failed: HRESULT=0x{ex.HResult:X8}."); return false; }
        }

        private static void Close(ulong id)
        {
            try
            {
                if (Channels.TryRemove(id, out var channel))
                {
                    Push(id, 0);
                    channel.Dispose();
                }
            }
            catch (Exception ex) { LogService.Warn($"Dealer close callback failed: HRESULT=0x{ex.HResult:X8}."); }
        }

        private static void Activity(ulong id, uint kind, bool active)
        {
            try { if (Channels.TryGetValue(id, out var channel)) channel.Activity(kind, active); }
            catch (Exception ex) { LogService.Warn($"Dealer activity callback failed: HRESULT=0x{ex.HResult:X8}."); }
        }

        private static bool Push(ulong id, uint kind, byte[] data = null)
        {
            if (data == null) return Librespot.librespot_dealer_push_frame(id, kind, IntPtr.Zero, UIntPtr.Zero);
            var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
            try { return Librespot.librespot_dealer_push_frame(id, kind, pinned.AddrOfPinnedObject(), (UIntPtr)(uint)data.Length); }
            finally { pinned.Free(); }
        }

        private sealed class Channel : IDisposable
        {
            private readonly ulong _id;
            private readonly MessageWebSocket _socket = new MessageWebSocket();
            private ControlChannelTrigger _trigger;
            private BackgroundTaskRegistration _pushTask;
            private BackgroundTaskRegistration _keepAliveTask;
            private readonly CancellationTokenSource _closed = new CancellationTokenSource();
            private readonly SemaphoreSlim _writes = new SemaphoreSlim(1, 1);
            private readonly object _resources = new object();
            private readonly ControlWorkTracker _work = new ControlWorkTracker();
            private int _queuedWrites;
            private int _disposed;

            public Channel(ulong id)
            {
                _id = id;
                _socket.Control.MessageType = SocketMessageType.Utf8;
                _socket.MessageReceived += Received;
                _socket.Closed += (_, args) => Fail($"closed-code-{args.Code}");
            }

            public async Task OpenAsync(Uri endpoint)
            {
                try
                {
                    var access = await _access.ConfigureAwait(false);
                    if (access == BackgroundAccessStatus.DeniedBySystemPolicy || access == BackgroundAccessStatus.DeniedByUser)
                        throw new UnauthorizedAccessException("Background network access is disabled.");
                    _closed.Token.ThrowIfCancellationRequested();
                    var name = TaskPrefix + _id;
                    lock (_resources)
                    {
                        _closed.Token.ThrowIfCancellationRequested();
                        _trigger = new ControlChannelTrigger(name, 1, ControlChannelTriggerResourceType.RequestHardwareSlot);
                        var push = new BackgroundTaskBuilder { Name = name + ".Push", IsNetworkRequested = true };
                        push.SetTrigger(_trigger.PushNotificationTrigger);
                        _pushTask = push.Register();
                        var keepAlive = new BackgroundTaskBuilder
                        {
                            Name = name + ".KeepAlive", IsNetworkRequested = true,
                            TaskEntryPoint = "Windows.Networking.Sockets.WebSocketKeepAlive"
                        };
                        keepAlive.SetTrigger(_trigger.KeepAliveTrigger);
                        _keepAliveTask = keepAlive.Register();
                        _trigger.UsingTransport(_socket);
                    }
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_closed.Token))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(15));
                        await _socket.ConnectAsync(endpoint).AsTask(timeout.Token).ConfigureAwait(false);
                    }
                    ControlChannelTriggerStatus status;
                    lock (_resources)
                    {
                        _closed.Token.ThrowIfCancellationRequested();
                        status = _trigger.WaitForPushEnabled();
                    }
                    if (status != ControlChannelTriggerStatus.HardwareSlotAllocated && status != ControlChannelTriggerStatus.SoftwareSlotAllocated)
                        throw new InvalidOperationException("No control-channel slot was allocated.");
                    if (Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Mobile" &&
                        status != ControlChannelTriggerStatus.HardwareSlotAllocated)
                    {
                        LogService.Warn($"Dealer Mobile standby slot was not allocated; status={status}.");
                        throw new InvalidOperationException("Mobile standby requires a hardware control-channel slot.");
                    }
                    LogService.Info($"Dealer control channel ready; channel={_id}, slot={status}.");
                    if (!Push(_id, 1)) Fail("native-open-rejected");
                }
                catch (Exception ex) { Fail($"open-HRESULT-0x{ex.HResult:X8}"); }
            }

            private void Received(MessageWebSocket sender, MessageWebSocketMessageReceivedEventArgs args)
            {
                try
                {
                    if (Volatile.Read(ref _disposed) != 0) return;
                    using (var reader = args.GetDataReader())
                    {
                        if (reader.UnconsumedBufferLength > MaximumFrameBytes) { Fail("oversized-frame"); return; }
                        var bytes = new byte[reader.UnconsumedBufferLength];
                        reader.ReadBytes(bytes);
                        if (args.MessageType != SocketMessageType.Utf8) return;
                        var message = JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes, 0, bytes.Length));
                        var key = (string)message["key"];
                        if ((string)message["type"] == "request" && key != null)
                        {
                            if (!_work.TrackReply(key)) { Fail("reply-backlog"); return; }
                        }
                        if (!Push(_id, 2, bytes)) Fail("native-frame-rejected");
                    }
                }
                catch (Exception ex) { Fail($"receive-HRESULT-0x{ex.HResult:X8}"); }
            }

            public void Activity(uint kind, bool active)
            {
                if (active) _work.Begin();
                else
                {
                    _work.End();
                    if (kind == 3) _work.RegistrationCompleted();
                }
            }

            public bool Send(byte[] data)
            {
                if (Volatile.Read(ref _disposed) != 0) return false;
                if (Interlocked.Increment(ref _queuedWrites) > 32)
                {
                    Interlocked.Decrement(ref _queuedWrites);
                    Fail("write-backlog"); return false;
                }
                Activity(0, true);
                _ = WriteAsync(data);
                return true;
            }

            private async Task WriteAsync(byte[] data)
            {
                bool acquired = false;
                try
                {
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_closed.Token))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(10));
                        await _writes.WaitAsync(timeout.Token).ConfigureAwait(false);
                        acquired = true;
                        using (var writer = new DataWriter(_socket.OutputStream))
                        {
                            try
                            {
                                writer.WriteBytes(data);
                                await writer.StoreAsync().AsTask(timeout.Token).ConfigureAwait(false);
                            }
                            finally { writer.DetachStream(); }
                        }
                        var reply = JObject.Parse(System.Text.Encoding.UTF8.GetString(data, 0, data.Length));
                        var key = (string)reply["key"];
                        if (key != null) _work.ReplyStored(key);
                    }
                }
                catch (Exception ex) { Fail($"write-HRESULT-0x{ex.HResult:X8}"); }
                finally
                {
                    if (acquired) _writes.Release();
                    Interlocked.Decrement(ref _queuedWrites);
                    Activity(0, false);
                }
            }

            public async Task DrainAsync(CancellationToken cancellation)
            {
                try
                {
                    using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _closed.Token))
                    {
                        deadline.CancelAfter(TimeSpan.FromSeconds(20));
                        var cancelled = Task.Delay(Timeout.Infinite, deadline.Token);
                        while (!deadline.IsCancellationRequested)
                        {
                            var work = _work.Pending;
                            if (await Task.WhenAny(work, cancelled).ConfigureAwait(false) != work)
                                throw new OperationCanceledException();
                            await work.ConfigureAwait(false);
                            if (_work.IsIdle) { deadline.Cancel(); return; }
                        }
                        throw new OperationCanceledException();
                    }
                }
                catch (OperationCanceledException) { if (Volatile.Read(ref _disposed) == 0) Fail("background-work-cancelled"); }
            }

            private void Fail(string reason)
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                LogService.Warn($"Dealer control channel unavailable; channel={_id}, reason={reason}. Native session will retry with its current account.");
                if (Interlocked.Read(ref _latestOpened) == unchecked((long)_id))
                {
                    try { ConnectionLost?.Invoke(); }
                    catch (Exception ex) { LogService.Warn($"Dealer health notification failed: HRESULT=0x{ex.HResult:X8}."); }
                }
                Close(_id);
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                _closed.Cancel();
                _work.Close();
                lock (_resources)
                {
                    _socket.MessageReceived -= Received;
                    _socket.Dispose();
                    _pushTask?.Unregister(true);
                    _keepAliveTask?.Unregister(true);
                    _trigger?.Dispose();
                }
                // Queued writes/native work may still complete; their semaphore
                // and token are collected after those continuations release them.
            }
        }
    }
}
