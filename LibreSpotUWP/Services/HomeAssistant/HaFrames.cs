using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LibreSpotUWP.Services.HomeAssistant
{
    // Builds the frames the app sends. Every frame is assembled key by key from an allow-list
    // so no field can reach the wire by accident.
    internal static class HaFrames
    {
        private static readonly Regex DeviceIdPattern =
            new Regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.CultureInvariant);
        private static readonly Regex AppVersionPattern =
            new Regex("^[0-9]+\\.[0-9]+\\.[0-9]+\\.[0-9]+$", RegexOptions.CultureInvariant);

        // The user-typed device name is cleaned so a bad name can never get the hello rejected.
        // Device id and app version come from the app itself; a bad value is a programming error.
        public static string Hello(string deviceId, string deviceName, string appVersion, IEnumerable<string> commands)
        {
            if (deviceId == null || !DeviceIdPattern.IsMatch(deviceId))
                throw new ArgumentException("device id must be a lower-case GUID", nameof(deviceId));
            if (appVersion == null || !AppVersionPattern.IsMatch(appVersion))
                throw new ArgumentException("app version must be four numeric parts", nameof(appVersion));

            var list = new JArray();
            foreach (var command in (commands ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal))
                if (Array.IndexOf(HaContract.CommandNames, command) >= 0) list.Add(command);

            return Serialize(new JObject
            {
                ["v"] = HaContract.Version,
                ["type"] = "hello",
                ["device_id"] = deviceId,
                ["device_name"] = CleanDeviceName(deviceName),
                ["app_version"] = appVersion,
                ["commands"] = list
            });
        }

        public static string CleanDeviceName(string name)
        {
            return HaStateMapper.CleanText(name, HaContract.MaxDeviceNameLength) ?? HaContract.DefaultDeviceName;
        }

        public static string State(HaStateFrame frame)
        {
            return Serialize(ToJson(frame));
        }

        public static JObject ToJson(HaStateFrame frame)
        {
            return new JObject
            {
                ["v"] = HaContract.Version,
                ["type"] = "state",
                ["seq"] = frame.Seq,
                ["state"] = frame.State,
                ["title"] = frame.Title,
                ["artist"] = frame.Artist,
                ["album"] = frame.Album,
                ["artwork_url"] = frame.ArtworkUrl,
                ["content_id"] = frame.ContentId,
                ["duration_ms"] = frame.DurationMs,
                ["position_ms"] = frame.PositionMs,
                ["volume"] = frame.Volume,
                ["shuffle"] = frame.Shuffle,
                ["repeat"] = frame.Repeat,
                ["session"] = frame.Session,
                ["output"] = frame.Output
            };
        }

        public static string Ack(string id, bool ok, string error = null)
        {
            var frame = new JObject { ["v"] = HaContract.Version, ["type"] = "ack", ["id"] = id, ["ok"] = ok };
            if (!ok) frame["error"] = error ?? "failed";
            return Serialize(frame);
        }

        public static string Error(string code)
        {
            return Serialize(new JObject { ["v"] = HaContract.Version, ["type"] = "error", ["code"] = code });
        }

        private static string Serialize(JObject frame)
        {
            return frame.ToString(Formatting.None);
        }
    }
}
