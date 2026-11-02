using System.Collections.Generic;

namespace LibreSpotUWP.Services.HomeAssistant
{
    // Wire contract v1 between the app and the Home Assistant integration.
    // Authoritative definition: contracts/lumia_hifi/v1 (JSON Schema and shared fixtures).
    // Nothing in this folder may reference WinRT types so it stays testable on a build host.
    internal static class HaContract
    {
        public const int Version = 1;
        public const int MaxFrameBytes = 16384;
        public const int MaxTextLength = 256;
        public const int MaxDeviceNameLength = 64;
        public const string DefaultDeviceName = "Lumia HiFi";
        public const int MaxArtworkUrlLength = 512;
        public const uint MaxMilliseconds = 86400000;
        public const ushort MaxVolume = 65535;

        public static readonly string[] CommandNames =
        {
            "play", "pause", "next", "previous", "seek", "volume_set", "shuffle_set", "repeat_set"
        };

        // Every key the state frame may contain. Nothing outside this list is ever serialized.
        public static readonly string[] StateKeys =
        {
            "v", "type", "seq", "state", "title", "artist", "album", "artwork_url", "content_id",
            "duration_ms", "position_ms", "volume", "shuffle", "repeat", "session", "output"
        };

        public static readonly IReadOnlyList<string> RepeatNames = new[] { "off", "all", "one" };
    }
}
