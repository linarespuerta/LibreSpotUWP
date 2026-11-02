using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LibreSpotUWP.Services.HomeAssistant
{
    internal enum HaCommandKind { Play, Pause, Next, Previous, Seek, VolumeSet, ShuffleSet, RepeatSet }

    internal enum HaParseStatus
    {
        // A valid command. Command is set.
        Command,
        // A valid error frame from Home Assistant. PeerErrorCode is set.
        PeerError,
        InvalidFrame,
        UnknownType,
        // Close the connection (code 4002).
        UnsupportedVersion,
        TooLarge
    }

    internal sealed class HaCommand
    {
        public string Id { get; set; }
        public HaCommandKind Kind { get; set; }
        public uint PositionMs { get; set; }
        public ushort Volume { get; set; }
        public bool Shuffle { get; set; }
        // 0 = off, 1 = context, 2 = track (the app's native repeat values).
        public int RepeatMode { get; set; }
    }

    internal sealed class HaParseResult
    {
        public HaParseStatus Status { get; set; }
        public HaCommand Command { get; set; }
        public string PeerErrorCode { get; set; }

        // Wire error code to answer with, or null when the frame was fine.
        public string ErrorCodeToSend
        {
            get
            {
                switch (Status)
                {
                    case HaParseStatus.InvalidFrame: return "invalid_frame";
                    case HaParseStatus.UnknownType: return "unknown_type";
                    case HaParseStatus.TooLarge: return "too_large";
                    default: return null;
                }
            }
        }
    }

    // Strict validator for frames received from Home Assistant. It mirrors the JSON Schemas and the
    // Python validator, including the rules the schemas cannot express (integers written as integers,
    // strict JSON syntax, no duplicate names); the shared fixtures in contracts/lumia_hifi/v1 keep the
    // three in agreement.
    internal static class HaCommandParser
    {
        private const int MaxDepth = 8;

        public static HaParseResult Parse(string raw)
        {
            if (raw == null) return Fail(HaParseStatus.InvalidFrame);
            if (Encoding.UTF8.GetByteCount(raw) > HaContract.MaxFrameBytes) return Fail(HaParseStatus.TooLarge);

            if (!HaStrictJson.IsValid(raw, MaxDepth)) return Fail(HaParseStatus.InvalidFrame);

            JObject frame;
            try
            {
                using (var text = new StringReader(raw))
                using (var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None, MaxDepth = MaxDepth })
                {
                    var token = JToken.ReadFrom(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (reader.Read()) return Fail(HaParseStatus.InvalidFrame); // trailing content
                    frame = token as JObject;
                }
            }
            catch (JsonException) { return Fail(HaParseStatus.InvalidFrame); }
            if (frame == null) return Fail(HaParseStatus.InvalidFrame);

            // The version must be the integer literal 1. 1.0, true, a string or an integer too large for
            // a long (Newtonsoft stores it as a BigInteger) are all unsupported, never an exception.
            JToken version;
            if (!frame.TryGetValue("v", out version) || !IsVersion(version)) return Fail(HaParseStatus.UnsupportedVersion);

            JToken type;
            if (!frame.TryGetValue("type", out type) || type.Type != JTokenType.String) return Fail(HaParseStatus.UnknownType);
            switch ((string)type)
            {
                case "command": return ParseCommand(frame);
                case "error": return ParseError(frame);
                case "hello":
                case "state":
                case "ack":
                    return Fail(HaParseStatus.InvalidFrame); // valid frame types, wrong direction
                default: return Fail(HaParseStatus.UnknownType);
            }
        }

        private static bool IsVersion(JToken token)
        {
            var value = token as JValue;
            return value != null && value.Type == JTokenType.Integer && value.Value is long && (long)value.Value == HaContract.Version;
        }

        private static HaParseResult ParseError(JObject frame)
        {
            if (!HasOnly(frame, "v", "type", "code")) return Fail(HaParseStatus.InvalidFrame);
            string code;
            if (!TryString(frame, "code", 1, 32, out code)) return Fail(HaParseStatus.InvalidFrame);
            if (code != "unknown_type" && code != "invalid_frame" && code != "too_large") return Fail(HaParseStatus.InvalidFrame);
            return new HaParseResult { Status = HaParseStatus.PeerError, PeerErrorCode = code };
        }

        private static HaParseResult ParseCommand(JObject frame)
        {
            string id, name;
            if (!TryString(frame, "id", 1, 64, out id) || !TryString(frame, "name", 1, 32, out name))
                return Fail(HaParseStatus.InvalidFrame);

            var command = new HaCommand { Id = id };
            switch (name)
            {
                case "play": command.Kind = HaCommandKind.Play; return Finish(frame, command);
                case "pause": command.Kind = HaCommandKind.Pause; return Finish(frame, command);
                case "next": command.Kind = HaCommandKind.Next; return Finish(frame, command);
                case "previous": command.Kind = HaCommandKind.Previous; return Finish(frame, command);
                case "seek":
                {
                    long position;
                    if (!TryInteger(frame, "position_ms", 0, HaContract.MaxMilliseconds, out position)) return Fail(HaParseStatus.InvalidFrame);
                    command.Kind = HaCommandKind.Seek;
                    command.PositionMs = (uint)position;
                    return Finish(frame, command, "position_ms");
                }
                case "volume_set":
                {
                    double volume;
                    if (!TryUnitNumber(frame, "volume", out volume)) return Fail(HaParseStatus.InvalidFrame);
                    command.Kind = HaCommandKind.VolumeSet;
                    command.Volume = HaStateMapper.VolumeFromWire(volume);
                    return Finish(frame, command, "volume");
                }
                case "shuffle_set":
                {
                    JToken flag;
                    if (!frame.TryGetValue("shuffle", out flag) || flag.Type != JTokenType.Boolean) return Fail(HaParseStatus.InvalidFrame);
                    command.Kind = HaCommandKind.ShuffleSet;
                    command.Shuffle = (bool)flag;
                    return Finish(frame, command, "shuffle");
                }
                case "repeat_set":
                {
                    string repeat;
                    if (!TryString(frame, "repeat", 1, 8, out repeat)) return Fail(HaParseStatus.InvalidFrame);
                    var index = Array.IndexOf(new[] { "off", "all", "one" }, repeat);
                    if (index < 0) return Fail(HaParseStatus.InvalidFrame);
                    command.Kind = HaCommandKind.RepeatSet;
                    command.RepeatMode = index;
                    return Finish(frame, command, "repeat");
                }
                default:
                    return Fail(HaParseStatus.InvalidFrame);
            }
        }

        private static HaParseResult Finish(JObject frame, HaCommand command, string extraKey = null)
        {
            var allowed = extraKey == null
                ? new[] { "v", "type", "id", "name" }
                : new[] { "v", "type", "id", "name", extraKey };
            if (!HasOnly(frame, allowed)) return Fail(HaParseStatus.InvalidFrame);
            return new HaParseResult { Status = HaParseStatus.Command, Command = command };
        }

        private static bool HasOnly(JObject frame, params string[] allowed)
        {
            foreach (var property in frame.Properties())
                if (Array.IndexOf(allowed, property.Name) < 0) return false;
            return true;
        }

        private static bool TryString(JObject frame, string key, int minimum, int maximum, out string value)
        {
            value = null;
            JToken token;
            if (!frame.TryGetValue(key, out token) || token.Type != JTokenType.String) return false;
            var text = (string)token;
            if (text.Length < minimum || text.Length > maximum) return false;
            value = text;
            return true;
        }

        private static bool TryInteger(JObject frame, string key, long minimum, long maximum, out long value)
        {
            value = 0;
            JToken token;
            if (!frame.TryGetValue(key, out token) || token.Type != JTokenType.Integer) return false;
            var raw = ((JValue)token).Value;
            if (!(raw is long)) return false; // BigInteger: out of range for every contract field
            value = (long)raw;
            return value >= minimum && value <= maximum;
        }

        private static bool TryUnitNumber(JObject frame, string key, out double value)
        {
            value = 0;
            JToken token;
            if (!frame.TryGetValue(key, out token)) return false;
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) return false;
            value = (double)token;
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= 1;
        }

        private static HaParseResult Fail(HaParseStatus status)
        {
            return new HaParseResult { Status = status };
        }
    }
}
