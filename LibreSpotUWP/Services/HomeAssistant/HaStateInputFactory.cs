using System.Linq;
using LibreSpotUWP.Models;

namespace LibreSpotUWP.Services.HomeAssistant
{
    // Adapter from the app's MediaState to the plain mapper input. Deliberately reads only the
    // playback fields below: device ids, context names/URIs, status messages and account data
    // never leave this method.
    internal static class HaStateInputFactory
    {
        public const string DjTitle = "Spotify DJ";
        public const string DjArtist = "Spotify";

        public static HaStateInput FromMediaState(MediaState state)
        {
            var input = new HaStateInput
            {
                PlaybackState = state.PlaybackState,
                ArtworkUrl = FirstUsableArtwork(state),
                ContentId = state.Track?.Uri ?? state.Metadata?.Uri,
                DurationMs = state.DisplayDurationMs,
                PositionMs = state.PositionMs,
                Volume = state.Volume,
                Shuffle = state.Shuffle,
                RepeatMode = state.RepeatMode,
                IsSessionConnected = state.IsSessionConnected,
                IsOffline = state.IsOffline,
                IsRecovering = state.IsRecoveringOnlinePlayback,
                IsRemote = state.IsSpotifyConnectRemote
            };

            if (state.IsNarrationActive)
            {
                // The narration text is generated commentary that can address the listener; keep it off the wire.
                input.Title = DjTitle;
                input.Artist = DjArtist;
                return input;
            }

            input.Title = state.Metadata?.Name ?? state.Track?.Name;
            input.Artist = state.Metadata?.Artists?.FirstOrDefault()?.Name ?? state.Track?.Artist;
            input.Album = state.Metadata?.Album?.Name ?? state.Track?.Album;
            return input;
        }

        // Same order as the rest of the app (MediaControllerBar.GetArtworkUri), but a candidate Home
        // Assistant cannot fetch (a local ms-appdata cache path, plain http) falls through to the next.
        private static string FirstUsableArtwork(MediaState state)
        {
            var candidates = new[]
            {
                state.ArtworkUri,
                state.Metadata?.Album?.Images?.FirstOrDefault()?.Url,
                state.Track?.CoverUrl
            };
            foreach (var candidate in candidates)
            {
                var usable = HaStateMapper.CleanArtwork(candidate);
                if (usable != null) return usable;
            }
            return null;
        }
    }
}
