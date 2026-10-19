using System;

namespace LibreSpotUWP.Services
{
    // Stream timestamps describe delivered PCM, not the current track position.
    // Keeping an integer frame count prevents rounding drift across many samples.
    internal sealed class PcmStreamTimeline
    {
        private readonly int _sampleRate;
        private readonly object _sync = new object();
        private long _framesDelivered;
        public int FrameBytes { get; }
        public long FramesDelivered { get { lock (_sync) return _framesDelivered; } }
        public TimeSpan Position { get { lock (_sync) return TimeSpan.FromTicks(_framesDelivered * TimeSpan.TicksPerSecond / _sampleRate); } }

        public PcmStreamTimeline(int sampleRate, int channels, int bitsPerSample)
        {
            if (sampleRate <= 0 || channels <= 0 ||
                (bitsPerSample != 16 && bitsPerSample != 24 && bitsPerSample != 32))
                throw new ArgumentOutOfRangeException(nameof(bitsPerSample), "Unsupported PCM layout.");
            _sampleRate = sampleRate;
            FrameBytes = checked(channels * (bitsPerSample / 8));
        }

        public int AlignedReadLength(ulong read, ulong write, int capacity, ulong boundary, int maximum)
        {
            if (capacity <= 0 || maximum <= 0 || write < read)
                return 0;
            var available = write - read;
            if (available > (ulong)capacity)
                throw new InvalidOperationException("PCM producer overran its ring consumer.");
            if (boundary != ulong.MaxValue)
                available = Math.Min(available, boundary > read ? boundary - read : 0);
            var bytes = (int)Math.Min(available, (ulong)maximum);
            return bytes - bytes % FrameBytes;
        }

        public TimeSpan Advance(int bytes)
        {
            if (bytes <= 0 || bytes % FrameBytes != 0)
                throw new ArgumentOutOfRangeException(nameof(bytes));
            lock (_sync)
            {
                var start = Position;
                _framesDelivered = checked(_framesDelivered + bytes / FrameBytes);
                return Position - start;
            }
        }

        public TimeSpan BufferedEnd(int bytes)
        {
            if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
            lock (_sync) return TimeSpan.FromTicks((_framesDelivered + bytes / FrameBytes) * TimeSpan.TicksPerSecond / _sampleRate);
        }
    }
}
