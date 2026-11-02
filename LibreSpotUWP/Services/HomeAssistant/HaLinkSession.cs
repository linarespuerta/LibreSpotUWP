using System;
using System.Threading;
using System.Threading.Tasks;

namespace LibreSpotUWP.Services.HomeAssistant
{
    // What the transport should do after a frame.
    internal struct HaSessionAction
    {
        public static readonly HaSessionAction Continue = new HaSessionAction();
        public bool Close { get; private set; }
        public ushort CloseCode { get; private set; }
        public static HaSessionAction CloseWith(ushort code) { return new HaSessionAction { Close = true, CloseCode = code }; }
    }

    internal sealed class HaLinkSessionOptions
    {
        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public string AppVersion { get; set; }
        public Func<string, Task> Send { get; set; }
        public Func<HaStateInput> CurrentState { get; set; }
        public IHaPlaybackControl Control { get; set; }
        public Func<DateTime> UtcNow { get; set; }
        // Ask the transport to call OnFlushTimerAsync after the delay.
        public Action<TimeSpan> ScheduleFlush { get; set; }
        public Action<string> Log { get; set; }
        public TimeSpan CommandTimeout { get; set; } = HaCommandDispatcher.DefaultTimeout;
        public TimeSpan ThrottleInterval { get; set; } = HaStateThrottle.DefaultInterval;
    }

    // Contract v1 for one connection, independent of the socket API: hello, throttled state snapshots,
    // serialized command execution with ack-then-state, and error replies. The WinRT transport owns the
    // socket and timer and feeds this class; tests feed it directly.
    internal sealed class HaLinkSession
    {
        public const ushort CloseUnsupportedVersion = 4002;
        public const ushort CloseTooLarge = 4004;

        private readonly HaLinkSessionOptions _options;
        private readonly HaStateThrottle _throttle;
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _commandGate = new SemaphoreSlim(1, 1);
        private readonly object _workSync = new object();
        private TaskCompletionSource<bool> _idle = Completed();
        private int _work;
        private int _closed;
        private uint _seq;
        private HaStateFrame _lastSent;
        private DateTime _lastSentAt;

        // MediaService publishes a state change every ~500 ms just for the position. Home Assistant
        // extrapolates the position of a playing media player itself, so a snapshot whose only change is
        // a position consistent with that extrapolation is not sent.
        public static readonly TimeSpan PositionTolerance = TimeSpan.FromSeconds(2);

        public HaLinkSession(HaLinkSessionOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.Send == null || options.CurrentState == null || options.Control == null)
                throw new ArgumentException("Send, CurrentState and Control are required.", nameof(options));
            _options = options;
            if (_options.UtcNow == null) _options.UtcNow = () => DateTime.UtcNow;
            _throttle = new HaStateThrottle(options.ThrottleInterval);
        }

        // Completes when no frame, command or state send is in progress (used by background wakes).
        public Task Idle { get { lock (_workSync) return _idle.Task; } }

        public bool IsClosed { get { return Volatile.Read(ref _closed) != 0; } }

        public void Close() { Interlocked.Exchange(ref _closed, 1); }

        // Hello, then the first snapshot immediately.
        public async Task StartAsync()
        {
            using (Track())
            {
                _throttle.Reset();
                await SendAsync(HaFrames.Hello(_options.DeviceId, _options.DeviceName, _options.AppVersion, HaContract.CommandNames)).ConfigureAwait(false);
                await SendStateAsync(force: true).ConfigureAwait(false);
            }
        }

        // Call on every playback state change.
        public async Task OnStateChangedAsync()
        {
            if (IsClosed) return;
            using (Track())
            {
                TimeSpan wait;
                if (_throttle.Request(_options.UtcNow(), out wait)) await SendStateAsync(force: false).ConfigureAwait(false);
                else _options.ScheduleFlush?.Invoke(wait);
            }
        }

        public async Task OnFlushTimerAsync()
        {
            if (IsClosed) return;
            using (Track())
            {
                TimeSpan wait;
                if (_throttle.TryFlush(_options.UtcNow(), out wait)) await SendStateAsync(force: false).ConfigureAwait(false);
                else if (wait > TimeSpan.Zero) _options.ScheduleFlush?.Invoke(wait);
            }
        }

        public async Task<HaSessionAction> OnFrameAsync(string raw)
        {
            if (IsClosed) return HaSessionAction.Continue;
            using (Track())
            {
                var result = HaCommandParser.Parse(raw);
                switch (result.Status)
                {
                    case HaParseStatus.Command:
                        await ExecuteAsync(result.Command).ConfigureAwait(false);
                        return HaSessionAction.Continue;
                    case HaParseStatus.PeerError:
                        _options.Log?.Invoke("Home Assistant reported a protocol error: " + result.PeerErrorCode + ".");
                        return HaSessionAction.Continue;
                    case HaParseStatus.UnsupportedVersion:
                        _options.Log?.Invoke("Home Assistant sent an unsupported protocol version; closing the link.");
                        return HaSessionAction.CloseWith(CloseUnsupportedVersion);
                    case HaParseStatus.TooLarge:
                        await SendAsync(HaFrames.Error("too_large")).ConfigureAwait(false);
                        return HaSessionAction.CloseWith(CloseTooLarge);
                    default:
                        _options.Log?.Invoke("Rejected a frame from Home Assistant: " + result.ErrorCodeToSend + ".");
                        await SendAsync(HaFrames.Error(result.ErrorCodeToSend)).ConfigureAwait(false);
                        return HaSessionAction.Continue;
                }
            }
        }

        // Binary frames are never part of the contract.
        public async Task OnBinaryFrameAsync()
        {
            if (IsClosed) return;
            using (Track())
                await SendAsync(HaFrames.Error("invalid_frame")).ConfigureAwait(false);
        }

        private async Task ExecuteAsync(HaCommand command)
        {
            // One command at a time, in arrival order, like taps on the lock-screen controls.
            await _commandGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var error = await HaCommandDispatcher.ExecuteAsync(_options.Control, command, _options.CommandTimeout, _options.Log).ConfigureAwait(false);
                await SendAsync(HaFrames.Ack(command.Id, error == null, error)).ConfigureAwait(false);
                // The contract promises a snapshot after every command, outside the throttle.
                await SendStateAsync(force: true).ConfigureAwait(false);
            }
            finally { _commandGate.Release(); }
        }

        private async Task SendStateAsync(bool force)
        {
            HaStateInput input;
            try { input = _options.CurrentState(); }
            catch (Exception ex)
            {
                _options.Log?.Invoke("Unable to read playback state for Home Assistant: HRESULT=0x" + ex.HResult.ToString("X8") + ".");
                return;
            }
            if (input == null) return;
            await _sendGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (IsClosed) return;
                var now = _options.UtcNow();
                var frame = HaStateMapper.Map(input, _seq);
                if (!force && OnlyPositionAdvanced(_lastSent, frame, now - _lastSentAt)) return;
                _seq = unchecked(_seq + 1);
                _lastSent = frame;
                _lastSentAt = now;
                _throttle.MarkSent(now);
                await _options.Send(HaFrames.State(frame)).ConfigureAwait(false);
            }
            finally { _sendGate.Release(); }
        }

        // True when nothing but the position changed, and the position is where Home Assistant's own
        // extrapolation (playing: last position + elapsed; otherwise: unchanged) already puts it.
        public static bool OnlyPositionAdvanced(HaStateFrame previous, HaStateFrame current, TimeSpan elapsed)
        {
            if (previous == null || current == null) return false;
            if (previous.State != current.State || previous.Title != current.Title || previous.Artist != current.Artist ||
                previous.Album != current.Album || previous.ArtworkUrl != current.ArtworkUrl || previous.ContentId != current.ContentId ||
                previous.DurationMs != current.DurationMs || previous.Volume != current.Volume || previous.Shuffle != current.Shuffle ||
                previous.Repeat != current.Repeat || previous.Session != current.Session || previous.Output != current.Output)
                return false;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            var expected = (double)previous.PositionMs + (current.State == "playing" ? elapsed.TotalMilliseconds : 0);
            return Math.Abs(current.PositionMs - expected) <= PositionTolerance.TotalMilliseconds;
        }

        private async Task SendAsync(string frame)
        {
            await _sendGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (IsClosed) return;
                await _options.Send(frame).ConfigureAwait(false);
            }
            finally { _sendGate.Release(); }
        }

        private WorkScope Track()
        {
            lock (_workSync)
            {
                if (_work++ == 0) _idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            return new WorkScope(this);
        }

        private void EndWork()
        {
            lock (_workSync)
            {
                if (_work > 0 && --_work == 0) _idle.TrySetResult(true);
            }
        }

        private static TaskCompletionSource<bool> Completed()
        {
            var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.SetResult(true);
            return source;
        }

        private struct WorkScope : IDisposable
        {
            private readonly HaLinkSession _owner;
            public WorkScope(HaLinkSession owner) { _owner = owner; }
            public void Dispose() { _owner?.EndWork(); }
        }
    }
}
