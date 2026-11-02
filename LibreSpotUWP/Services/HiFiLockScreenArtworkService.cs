using LibreSpotUWP.Helpers;
using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System.UserProfile;

namespace LibreSpotUWP.Services
{
    /// <summary>Publishes one still per track to the OS lock-screen wallpaper, when opted in.</summary>
    internal static class HiFiLockScreenArtworkService
    {
        private const int MaximumInputBytes = 3 * 1024 * 1024;
        private const int MaximumWallpaperBytes = 2 * 1024 * 1024;
        private const string FolderName = "HiFiLockScreenArtwork";
        private static readonly object Sync = new object();
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        private static CancellationTokenSource _pending;
        private static string _scheduledKey;

        internal static void Schedule(string trackUri, string artworkUri)
        {
            // This is called on the media-state path: optional wallpaper work must never
            // interrupt Connect control or audio, even if the OS personalization API fails.
            try
            {
                if (string.IsNullOrWhiteSpace(trackUri) ||
                    !UserSettings.HiFiLockScreenArtworkEnabled ||
                    !UserProfilePersonalizationSettings.IsSupported()) return;

                string key = trackUri + "|" + (artworkUri ?? string.Empty);
                CancellationToken token;
                lock (Sync)
                {
                    if (string.Equals(key, _scheduledKey, StringComparison.Ordinal)) return;
                    _pending?.Cancel();
                    _pending = new CancellationTokenSource();
                    _scheduledKey = key;
                    token = _pending.Token;
                }
                _ = Task.Run(() => PublishAsync(key, artworkUri, token));
            }
            catch (Exception ex)
            {
                LogService.Telemetry("hifi-lock-screen-schedule-error",
                    "Could not schedule optional lock-screen artwork: " + ex.GetType().Name, warning: true);
            }
        }

        internal static void Disable()
        {
            lock (Sync)
            {
                _pending?.Cancel();
                _scheduledKey = null;
            }
        }

        private static async Task PublishAsync(string key, string artworkUri, CancellationToken token)
        {
            bool entered = false;
            StorageFile outputFile = null;
            bool published = false;
            try
            {
                await Gate.WaitAsync(token).ConfigureAwait(false);
                entered = true;
                token.ThrowIfCancellationRequested();

                byte[] cover = null;
                int coverWidth = 0;
                int coverHeight = 0;
                if (ImageUriHelper.TryCreateImageUri(artworkUri, out Uri uri))
                {
                    byte[] input = await ReadArtworkAsync(uri, token).ConfigureAwait(false);
                    using (var stream = new InMemoryRandomAccessStream())
                    {
                        await stream.WriteAsync(input.AsBuffer());
                        stream.Seek(0);
                        var decoder = await BitmapDecoder.CreateAsync(stream);
                        double scale = Math.Min((double)HiFiLockScreenArtworkComposer.ArtworkSize / decoder.PixelWidth,
                            (double)HiFiLockScreenArtworkComposer.ArtworkSize / decoder.PixelHeight);
                        coverWidth = Math.Max(1, (int)Math.Round(decoder.PixelWidth * scale));
                        coverHeight = Math.Max(1, (int)Math.Round(decoder.PixelHeight * scale));
                        var transform = new BitmapTransform
                        {
                            ScaledWidth = (uint)coverWidth,
                            ScaledHeight = (uint)coverHeight,
                            InterpolationMode = BitmapInterpolationMode.Fant
                        };
                        var provider = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,
                            BitmapAlphaMode.Ignore, transform, ExifOrientationMode.RespectExifOrientation,
                            ColorManagementMode.DoNotColorManage);
                        cover = provider.DetachPixelData();
                    }
                }

                token.ThrowIfCancellationRequested();
                byte[] wallpaper = await Task.Run(() =>
                    HiFiLockScreenArtworkComposer.Compose(cover, coverWidth, coverHeight, token), token)
                    .ConfigureAwait(false);
                var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                    FolderName, CreationCollisionOption.OpenIfExists);
                outputFile = await folder.CreateFileAsync("hifi-" + Guid.NewGuid().ToString("N") + ".jpg",
                    CreationCollisionOption.FailIfExists);
                using (var output = await outputFile.OpenAsync(FileAccessMode.ReadWrite))
                {
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output);
                    encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                        HiFiLockScreenArtworkComposer.Width, HiFiLockScreenArtworkComposer.Height,
                        96, 96, wallpaper);
                    await encoder.FlushAsync();
                }

                token.ThrowIfCancellationRequested();
                if ((await outputFile.GetBasicPropertiesAsync()).Size >= MaximumWallpaperBytes)
                    throw new InvalidOperationException("Generated lock-screen image exceeds the Mobile size limit.");
                if (!UserSettings.HiFiLockScreenArtworkEnabled)
                    return;

                published = await UserProfilePersonalizationSettings.Current
                    .TrySetLockScreenImageAsync(outputFile);
                if (!published)
                    throw new InvalidOperationException("Windows rejected the lock-screen image.");
                await PruneOldFilesAsync(folder, outputFile).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogService.Telemetry("hifi-lock-screen-artwork-error",
                    "Lock-screen artwork update failed: " + ex.GetType().Name, warning: true);
                lock (Sync)
                    if (string.Equals(key, _scheduledKey, StringComparison.Ordinal)) _scheduledKey = null;
            }
            finally
            {
                if (!published && outputFile != null)
                {
                    try { await outputFile.DeleteAsync(); } catch { }
                }
                if (entered) Gate.Release();
            }
        }

        private static async Task<byte[]> ReadArtworkAsync(Uri uri, CancellationToken token)
        {
            if (uri.Scheme == "ms-appdata")
            {
                var file = await StorageFile.GetFileFromApplicationUriAsync(uri);
                if ((await file.GetBasicPropertiesAsync()).Size > MaximumInputBytes)
                    throw new InvalidOperationException("Album artwork exceeds the input size limit.");
                return File.ReadAllBytes(file.Path);
            }
            if (uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("Unsupported album-art URI scheme.");

            using (var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaximumInputBytes)
                    throw new InvalidOperationException("Album artwork exceeds the input size limit.");
                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var memory = new MemoryStream())
                {
                    var buffer = new byte[16384];
                    int read;
                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length, token)
                        .ConfigureAwait(false)) > 0)
                    {
                        if (memory.Length + read > MaximumInputBytes)
                            throw new InvalidOperationException("Album artwork exceeds the input size limit.");
                        memory.Write(buffer, 0, read);
                    }
                    return memory.ToArray();
                }
            }
        }

        private static async Task PruneOldFilesAsync(StorageFolder folder, StorageFile current)
        {
            var files = await folder.GetFilesAsync();
            foreach (var file in files)
            {
                if (file.Name == current.Name || !file.Name.StartsWith("hifi-", StringComparison.Ordinal))
                    continue;
                try { await file.DeleteAsync(); } catch { }
            }
        }
    }
}
