using System;
using System.Diagnostics;
using System.Threading;

namespace LibreSpotUWP.Services
{
    // Counters only on the audio path; no file I/O, endpoint or media metadata.
    internal sealed class PcmDeliveryDiagnostics
    {
        private long _requests;
        private long _samples;
        private long _bytes;
        private long _lastSample;
        public void Requested() => Interlocked.Increment(ref _requests);
        public void Delivered(int bytes)
        {
            Interlocked.Increment(ref _samples);
            Interlocked.Add(ref _bytes, bytes);
            Interlocked.Exchange(ref _lastSample, Stopwatch.GetTimestamp());
        }
        public string Snapshot()
        {
            var last = Interlocked.Read(ref _lastSample);
            var age = last == 0 ? -1 : (Stopwatch.GetTimestamp() - last) * 1000 / Stopwatch.Frequency;
            return $"requests={Interlocked.Read(ref _requests)}, samples={Interlocked.Read(ref _samples)}, bytes={Interlocked.Read(ref _bytes)}, lastSampleAgeMs={age}";
        }
    }
}
