using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.MediaProperties;

namespace LibreSpotUWP.Services
{
    // One source, one ring consumer. Never manufacture silence to satisfy a
    // request: starvation is reported as buffering while the producer recovers.
    internal sealed class PlatformPcmSource : IDisposable
    {
        private readonly Func<int, byte[]> _read;
        private readonly Func<int> _available;
        private readonly PcmStreamTimeline _timeline;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly SemaphoreSlim _samples = new SemaphoreSlim(1, 1);
        private readonly object _enabledLock = new object();
        private TaskCompletionSource<bool> _enabled = NewSignal();
        private readonly int _sampleBytes;
        private readonly Task _cancelled;
        private int _disposed;
        private int _pendingRequests;
        private int _discontinuous = 1;
        public MediaStreamSource Stream { get; }
        public MediaSource Source { get; }

        public PlatformPcmSource(AudioEncodingProperties properties, Func<int, byte[]> read, Func<int> available)
        {
            _read = read;
            _available = available;
            _cancelled = Task.Delay(Timeout.Infinite, _lifetime.Token);
            _timeline = new PcmStreamTimeline((int)properties.SampleRate, (int)properties.ChannelCount, (int)properties.BitsPerSample);
            _sampleBytes = Math.Max(1, (int)properties.SampleRate / 50) * _timeline.FrameBytes;
            Stream = new MediaStreamSource(new AudioStreamDescriptor(properties))
            {
                CanSeek = false,
                Duration = TimeSpan.Zero,
                BufferTime = TimeSpan.FromMilliseconds(40)
            };
            Stream.Starting += OnStarting;
            Stream.SampleRequested += OnSampleRequested;
            Source = MediaSource.CreateFromMediaStreamSource(Stream);
        }

        private static TaskCompletionSource<bool> NewSignal() =>
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public void SetEnabled(bool enabled)
        {
            lock (_enabledLock)
            {
                if (enabled) _enabled.TrySetResult(true);
                else if (_enabled.Task.IsCompleted) _enabled = NewSignal();
            }
        }

        public void MarkDiscontinuity() => Interlocked.Exchange(ref _discontinuous, 1);

        private void OnStarting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args)
        {
            // A resumed live source continues its own monotonic PCM timeline.
            args.Request.SetActualStartPosition(_timeline.Position);
        }

        private async void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
        {
            var deferral = args.Request.GetDeferral();
            bool acquired = false;
            // MF normally serializes audio requests. Bound ownership even if a
            // faulty/replaced source submits additional overlapping requests.
            if (Interlocked.Increment(ref _pendingRequests) > 2)
            {
                Interlocked.Decrement(ref _pendingRequests);
                args.Request.Sample = null;
                deferral.Complete();
                return;
            }
            try
            {
                await _samples.WaitAsync(_lifetime.Token).ConfigureAwait(false);
                acquired = true;
                while (!_lifetime.IsCancellationRequested)
                {
                    Task enabled;
                    lock (_enabledLock) enabled = _enabled.Task;
                    if (await Task.WhenAny(enabled, _cancelled).ConfigureAwait(false) != enabled)
                        break;
                    _lifetime.Token.ThrowIfCancellationRequested();
                    var data = _read(_sampleBytes);
                    if (data != null && data.Length != 0)
                    {
                        var start = _timeline.Position;
                        var duration = _timeline.Advance(data.Length);
                        var sample = MediaStreamSample.CreateFromBuffer(data.AsBuffer(), start);
                        sample.Duration = duration;
                        sample.Discontinuous = Interlocked.Exchange(ref _discontinuous, 0) != 0;
                        // This range comes from audible PCM actually buffered,
                        // never from the old synthetic 24-hour silent source.
                        sender.SetBufferedRange(start, _timeline.BufferedEnd(_available()));
                        args.Request.Sample = sample;
                        return;
                    }
                    args.Request.ReportSampleProgress(0);
                    await Task.Delay(20, _lifetime.Token).ConfigureAwait(false);
                }
                args.Request.Sample = null;
            }
            catch (OperationCanceledException) { args.Request.Sample = null; }
            catch (Exception ex)
            {
                LogService.Warn($"Platform PCM sample failed: {ex.Message}");
                sender.NotifyError(MediaStreamSourceErrorStatus.Other);
            }
            finally
            {
                if (acquired) _samples.Release();
                Interlocked.Decrement(ref _pendingRequests);
                deferral.Complete();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Stream.Starting -= OnStarting;
            Stream.SampleRequested -= OnSampleRequested;
            _lifetime.Cancel();
            // Pending MF deferrals retain this object until they finish. Do not
            // dispose their semaphore/token underneath an awaited continuation.
        }
    }
}
