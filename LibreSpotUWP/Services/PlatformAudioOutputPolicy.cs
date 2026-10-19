using System;
using System.Threading.Tasks;

namespace LibreSpotUWP.Services
{
    internal static class PlatformAudioOutputPolicy
    {
        public static async Task ConfigureAsync<T>(bool isWindowsMobile, string requestedDeviceId,
            Func<string, Task<T>> resolve, Action<T> assign) where T : class
        {
            if (isWindowsMobile)
            {
                // The measured Lumia failure occurs inside MediaPlayer.AudioDevice's
                // setter. Leave both enumeration and routing to Windows on Mobile;
                // even setting null is not safe on the tested build.
                if (!string.IsNullOrWhiteSpace(requestedDeviceId))
                    throw new NotSupportedException(
                        "Windows streaming on Windows Mobile requires System default audio output.");
                return;
            }

            // Preserve desktop explicit-device and reset-to-default behavior.
            // Mobile must never invoke either delegate, including with null.
            assign(await resolve(requestedDeviceId).ConfigureAwait(false));
        }
    }
}
