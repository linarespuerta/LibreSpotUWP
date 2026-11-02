using System;
using System.Threading;

namespace LibreSpotUWP.Services
{
    /// <summary>Builds one still wallpaper; the system lock screen does not host app animations.</summary>
    internal static class HiFiLockScreenArtworkComposer
    {
        internal const int Width = 720;
        internal const int Height = 1280;
        internal const int ArtworkSize = 560;
        internal const int ArtworkLeft = (Width - ArtworkSize) / 2;
        internal const int ArtworkTop = 280;

        // Source pixels are BGRA8, already decoded to fit within ArtworkSize.
        internal static byte[] Compose(byte[] artwork, int artWidth, int artHeight, CancellationToken cancellationToken)
        {
            if ((artwork == null && (artWidth != 0 || artHeight != 0)) ||
                (artwork != null && (artWidth < 1 || artHeight < 1 ||
                artWidth > ArtworkSize || artHeight > ArtworkSize ||
                artwork.Length != artWidth * artHeight * 4)))
                throw new ArgumentException("Invalid decoded album artwork.");

            // A muted glow borrows the sleeve's dominant color without storing artwork
            // beyond this one bounded composition or competing with the system clock.
            int tintB = 46, tintG = 35, tintR = 25;
            if (artwork != null)
            {
                long blue = 0, green = 0, red = 0, samples = 0;
                int stepX = Math.Max(1, artWidth / 32);
                int stepY = Math.Max(1, artHeight / 32);
                for (int y = 0; y < artHeight; y += stepY)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int x = 0; x < artWidth; x += stepX)
                    {
                        int source = (y * artWidth + x) * 4;
                        blue += artwork[source];
                        green += artwork[source + 1];
                        red += artwork[source + 2];
                        samples++;
                    }
                }
                tintB = (int)(blue / samples);
                tintG = (int)(green / samples);
                tintR = (int)(red / samples);
            }

            var pixels = new byte[Width * Height * 4];
            for (int y = 0; y < Height; y++)
            {
                if ((y & 31) == 0) cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < Width; x++)
                {
                    int offset = (y * Width + x) * 4;
                    int dxGlow = x - Width / 2;
                    int dyGlow = y - 545;
                    int glow = Math.Max(0, 100 - (dxGlow * dxGlow / 2600 + dyGlow * dyGlow / 4200));
                    int shade = 5 + Math.Min(7, Math.Abs(x - Width / 2) / 65);
                    pixels[offset] = (byte)(shade + tintB * glow / 650);
                    pixels[offset + 1] = (byte)(shade + tintG * glow / 650);
                    pixels[offset + 2] = (byte)(shade + tintR * glow / 650);
                    pixels[offset + 3] = 255;
                }
            }

            // The full cover is centered and sits above the phone's lower clock/controls.
            for (int y = ArtworkTop; y < ArtworkTop + ArtworkSize; y++)
            {
                int offset = (y * Width + ArtworkLeft) * 4;
                for (int x = 0; x < ArtworkSize; x++)
                {
                    pixels[offset++] = 19;
                    pixels[offset++] = 19;
                    pixels[offset++] = 19;
                    pixels[offset++] = 255;
                }
            }
            int left = ArtworkLeft + (ArtworkSize - artWidth) / 2;
            int top = ArtworkTop + (ArtworkSize - artHeight) / 2;
            for (int y = 0; y < artHeight; y++)
            {
                if ((y & 31) == 0) cancellationToken.ThrowIfCancellationRequested();
                Buffer.BlockCopy(artwork, y * artWidth * 4, pixels,
                    ((top + y) * Width + left) * 4, artWidth * 4);
            }

            for (int y = ArtworkTop - 2; y < ArtworkTop + ArtworkSize + 2; y++)
            {
                for (int x = ArtworkLeft - 2; x < ArtworkLeft + ArtworkSize + 2; x++)
                {
                    if (x >= ArtworkLeft && x < ArtworkLeft + ArtworkSize &&
                        y >= ArtworkTop && y < ArtworkTop + ArtworkSize)
                        continue;
                    int offset = (y * Width + x) * 4;
                    pixels[offset] = 91;
                    pixels[offset + 1] = 166;
                    pixels[offset + 2] = 215;
                    pixels[offset + 3] = 255;
                }
            }

            return pixels;
        }
    }
}
