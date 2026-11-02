using System;
using System.Globalization;
using System.Text.RegularExpressions;
using LibreSpotUWP.Models;

namespace LibreSpotUWP.Services.HomeAssistant
{
    // Everything the mapper needs, as plain values. HaStateInputFactory fills it from MediaState.
    internal sealed class HaStateInput
    {
        public LibrespotPlaybackState PlaybackState { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string ArtworkUrl { get; set; }
        public string ContentId { get; set; }
        public uint DurationMs { get; set; }
        public uint PositionMs { get; set; }
        public ushort Volume { get; set; }
        public bool Shuffle { get; set; }
        public int RepeatMode { get; set; }
        public bool IsSessionConnected { get; set; }
        public bool IsOffline { get; set; }
        public bool IsRecovering { get; set; }
        public bool IsRemote { get; set; }
    }

    internal sealed class HaStateFrame
    {
        public uint Seq { get; set; }
        public string State { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string ArtworkUrl { get; set; }
        public string ContentId { get; set; }
        public uint DurationMs { get; set; }
        public uint PositionMs { get; set; }
        public double Volume { get; set; }
        public bool Shuffle { get; set; }
        public string Repeat { get; set; }
        public string Session { get; set; }
        public string Output { get; set; }
    }

    internal static class HaStateMapper
    {
        private static readonly Regex ContentIdPattern =
            new Regex("^spotify:(track|episode):[A-Za-z0-9]{22}$", RegexOptions.CultureInvariant);

        public static HaStateFrame Map(HaStateInput input, uint seq)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return new HaStateFrame
            {
                Seq = seq,
                State = MapPlayback(input.PlaybackState),
                Title = CleanText(input.Title),
                Artist = CleanText(input.Artist),
                Album = CleanText(input.Album),
                ArtworkUrl = CleanArtwork(input.ArtworkUrl),
                ContentId = CleanContentId(input.ContentId),
                DurationMs = Math.Min(input.DurationMs, HaContract.MaxMilliseconds),
                PositionMs = Math.Min(input.PositionMs, HaContract.MaxMilliseconds),
                Volume = VolumeToWire(input.Volume),
                Shuffle = input.Shuffle,
                Repeat = MapRepeat(input.RepeatMode),
                Session = MapSession(input),
                Output = input.IsRemote ? "remote" : "local"
            };
        }

        public static string MapPlayback(LibrespotPlaybackState state)
        {
            switch (state)
            {
                case LibrespotPlaybackState.Playing: return "playing";
                case LibrespotPlaybackState.Paused: return "paused";
                case LibrespotPlaybackState.Loading: return "buffering";
                default: return "idle";
            }
        }

        public static string MapRepeat(int mode)
        {
            return mode == 1 ? "all" : mode == 2 ? "one" : "off";
        }

        // Offline wins over recovering; a session that is not connected also reads as offline.
        public static string MapSession(HaStateInput input)
        {
            if (input.IsOffline) return "offline";
            if (input.IsRecovering) return "recovering";
            return input.IsSessionConnected ? "connected" : "offline";
        }

        public static double VolumeToWire(ushort volume)
        {
            return Math.Round(volume / (double)HaContract.MaxVolume, 3, MidpointRounding.AwayFromZero);
        }

        public static ushort VolumeFromWire(double volume)
        {
            var scaled = Math.Round(volume * HaContract.MaxVolume, MidpointRounding.AwayFromZero);
            if (scaled < 0) return 0;
            if (scaled > HaContract.MaxVolume) return HaContract.MaxVolume;
            return (ushort)scaled;
        }

        // Trim, collapse empty to null, and cut at the contract limit without splitting a surrogate pair.
        public static string CleanText(string value)
        {
            return CleanText(value, HaContract.MaxTextLength);
        }

        public static string CleanText(string value, int maximumLength)
        {
            if (value == null || maximumLength <= 0) return null;
            var text = value.Trim();
            if (text.Length > maximumLength)
            {
                var length = maximumLength;
                if (char.IsHighSurrogate(text[length - 1])) length--;
                // The cut can land just after whitespace; the result is trimmed again.
                text = text.Substring(0, length).TrimEnd();
            }
            return text.Length == 0 ? null : text;
        }

        // Only https artwork the Home Assistant frontend can fetch itself; local cache paths become null.
        public static string CleanArtwork(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > HaContract.MaxArtworkUrlLength) return null;
            foreach (var character in value)
                if (char.IsWhiteSpace(character) || char.IsControl(character)) return null;
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri)) return null;
            return string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? value : null;
        }

        public static string CleanContentId(string value)
        {
            return value != null && ContentIdPattern.IsMatch(value) ? value : null;
        }
    }
}
