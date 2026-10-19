using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LibreSpotUWP.Services
{
    // A background deferral must cover native work AND the actual stored reply,
    // not just receipt/enqueue of a WebSocket frame.
    internal sealed class ControlWorkTracker
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, TaskCompletionSource<bool>> _replies = new Dictionary<string, TaskCompletionSource<bool>>();
        private readonly TaskCompletionSource<bool> _registration = Signal();
        private TaskCompletionSource<bool> _idle = Signal();
        private int _work;
        private bool _closed;
        private long _startupVersion;
        private bool _startupReady;
        private bool _platformPlaying;
        private bool _pcmDelivered;
        private TaskCompletionSource<bool> _startup;
        public ControlWorkTracker() { _idle.SetResult(true); }
        private static TaskCompletionSource<bool> Signal() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Begin()
        {
            lock (_sync) { if (!_closed && _work++ == 0) _idle = Signal(); }
        }
        public void End()
        {
            lock (_sync) { if (_work > 0 && --_work == 0) _idle.TrySetResult(true); }
        }
        public void RegistrationCompleted() { lock (_sync) _registration.TrySetResult(true); }
        public void BeginStartup(long version)
        {
            lock (_sync)
            {
                if (_closed) return;
                _startupVersion = version;
                _startupReady = false;
                _platformPlaying = false;
                _pcmDelivered = false;
                if (_startup == null) _startup = Signal();
            }
        }
        public void PlaybackReady(long version, bool platformPlaying)
        {
            lock (_sync)
            {
                if (_closed || _startup == null || version != _startupVersion) return;
                _startupReady = true;
                _platformPlaying = platformPlaying;
            }
        }
        public void PlatformPlaybackChanged(bool playing)
        {
            lock (_sync)
            {
                _platformPlaying = playing;
                if (playing && _startupReady && _pcmDelivered) CancelStartup();
            }
        }
        public void PcmDelivered(long version)
        {
            lock (_sync)
            {
                // Require a sample after accepted generation/pre-roll readiness;
                // PCM left over from the old track cannot finish a new load.
                if (!_startupReady || version != _startupVersion) return;
                _pcmDelivered = true;
                if (_platformPlaying) CancelStartup();
            }
        }
        public void CancelStartup()
        {
            lock (_sync)
            {
                _startup?.TrySetResult(true);
                _startup = null;
                _startupReady = false;
            }
        }
        public bool TrackReply(string key)
        {
            lock (_sync)
            {
                if (_closed) return false;
                if (_replies.ContainsKey(key)) return true;
                if (_replies.Count >= 32) return false;
                _replies.Add(key, Signal());
                return true;
            }
        }
        public void ReplyStored(string key)
        {
            lock (_sync)
            {
                if (!_replies.TryGetValue(key, out var pending)) return;
                _replies.Remove(key);
                pending.TrySetResult(true);
            }
        }
        public bool IsIdle { get { lock (_sync) return _closed || (_work == 0 && _replies.Count == 0 && _startup == null && _registration.Task.IsCompleted); } }
        public Task Pending { get { lock (_sync) return Task.WhenAll(_replies.Values.Select(x => x.Task).Concat(new[] { _idle.Task, _registration.Task, _startup?.Task ?? Task.CompletedTask })); } }
        public void Close()
        {
            lock (_sync)
            {
                _closed = true;
                CancelStartup();
                _idle.TrySetResult(false);
                _registration.TrySetResult(false);
                foreach (var reply in _replies.Values) reply.TrySetResult(false);
                _replies.Clear();
            }
        }
    }
}
