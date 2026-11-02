using System;

namespace LibreSpotUWP.Services.HomeAssistant
{
    // Coalesces bursts of state changes into at most one send per interval. The caller owns the timer
    // and the clock, so the logic is deterministic:
    //   on every change:   if (throttle.Request(now, out wait)) SendNow(); else StartTimer(wait);
    //   when the timer fires: if (throttle.TryFlush(now, out wait)) SendNow(); else if (wait > 0) StartTimer(wait);
    // Timers can fire early (DateTime.UtcNow has ~15.6 ms resolution on Windows), so a flush that is
    // still too early reports the remaining wait instead of silently dropping the pending snapshot.
    // A send always carries the latest full snapshot, so dropping intermediate changes loses nothing.
    internal sealed class HaStateThrottle
    {
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(250);

        private readonly object _gate = new object();
        private readonly TimeSpan _interval;
        private DateTime? _lastSent;
        private bool _pending;

        public HaStateThrottle(TimeSpan? interval = null)
        {
            _interval = interval ?? DefaultInterval;
            if (_interval < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        }

        public bool HasPending { get { lock (_gate) return _pending; } }

        // True: send now. False: a send is pending and should be flushed after `wait`.
        public bool Request(DateTime nowUtc, out TimeSpan wait)
        {
            lock (_gate)
            {
                var remaining = Remaining(nowUtc);
                if (remaining <= TimeSpan.Zero)
                {
                    _lastSent = nowUtc;
                    _pending = false;
                    wait = TimeSpan.Zero;
                    return true;
                }
                _pending = true;
                wait = remaining;
                return false;
            }
        }

        // True: send the pending snapshot now. False with wait > 0: still too early, re-arm the timer.
        // False with wait == 0: nothing is pending.
        public bool TryFlush(DateTime nowUtc, out TimeSpan wait)
        {
            lock (_gate)
            {
                wait = TimeSpan.Zero;
                if (!_pending) return false;
                var remaining = Remaining(nowUtc);
                if (remaining > TimeSpan.Zero)
                {
                    wait = remaining;
                    return false;
                }
                _lastSent = nowUtc;
                _pending = false;
                return true;
            }
        }

        // A snapshot was sent for another reason (hello, command reply): count it, drop what is pending.
        public void MarkSent(DateTime nowUtc)
        {
            lock (_gate)
            {
                _lastSent = nowUtc;
                _pending = false;
            }
        }

        // New connection: the first snapshot goes out immediately.
        public void Reset()
        {
            lock (_gate)
            {
                _lastSent = null;
                _pending = false;
            }
        }

        // A clock that moved backwards never blocks sending.
        private TimeSpan Remaining(DateTime nowUtc)
        {
            if (_lastSent == null) return TimeSpan.Zero;
            var elapsed = nowUtc - _lastSent.Value;
            if (elapsed < TimeSpan.Zero) return TimeSpan.Zero;
            return _interval - elapsed;
        }
    }
}
