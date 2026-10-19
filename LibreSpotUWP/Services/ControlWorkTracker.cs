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
        public bool IsIdle { get { lock (_sync) return _closed || (_work == 0 && _replies.Count == 0 && _registration.Task.IsCompleted); } }
        public Task Pending { get { lock (_sync) return Task.WhenAll(_replies.Values.Select(x => x.Task).Concat(new[] { _idle.Task, _registration.Task })); } }
        public void Close()
        {
            lock (_sync)
            {
                _closed = true;
                _idle.TrySetResult(false);
                _registration.TrySetResult(false);
                foreach (var reply in _replies.Values) reply.TrySetResult(false);
                _replies.Clear();
            }
        }
    }
}
