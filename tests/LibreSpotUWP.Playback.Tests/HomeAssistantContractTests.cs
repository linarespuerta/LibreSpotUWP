using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LibreSpotUWP.Models;
using LibreSpotUWP.Services.HomeAssistant;
using Newtonsoft.Json.Linq;
using SpotifyAPI.Web;

// WP4: the C# half of Home Assistant contract v1. The shared fixtures in contracts/lumia_hifi/v1
// are the golden output of the mapper and the input of the parser, and the Python suite validates
// the very same files against the JSON Schemas, so the three implementations cannot drift apart.
internal static class HomeAssistantContractTests
{
    public static readonly Action[] All =
    {
        StateMapperMatchesTheGoldenFixtures,
        MapperCoversEveryPlaybackState,
        VolumeConversionIsExactAtTheEdgesAndRoundTrips,
        RepeatSessionAndOutputMappings,
        MissingMetadataProducesNullsNotPlaceholders,
        TextIsTrimmedTruncatedAndSurrogateSafe,
        ArtworkAndContentIdAreSanitized,
        MediaStateAdapterPrefersMetadataAndFallsBackToTrack,
        DjNarrationNeverPutsNarrationTextOnTheWire,
        SerializedStateContainsOnlyContractKeysAndNoPrivateData,
        OutgoingControlFramesMatchTheFixtures,
        ParserAcceptsEveryValidCommandFixture,
        ParserRejectsEveryInvalidCommandFixture,
        ParserClassifiesProtocolErrors,
        ParserIsStrictAboutTypesAndSyntax,
        ParserTreatsPeerErrorsAndWrongDirectionFramesCorrectly,
        ThrottleSendsFirstChangeImmediatelyAndCoalescesBursts,
        ThrottleFlushResetAndClockEdgeCases,
        ThrottleLimitsSustainedChangesToTheConfiguredRate,
        ThrottleEarlyTimerReArmsInsteadOfDroppingTheSnapshot,
        ParserNeverThrowsOnHugeIntegers,
        ParserAgreesWithPythonOnRuntimeOnlyFixtures,
        ParserRejectsEveryMalformedFixture,
        StrictJsonAcceptsStandardJson,
        ArtworkFallsThroughToTheFirstHttpsCandidate,
        HelloCleansTheDeviceNameAndRejectsBadIdentity,
        TruncatedTextNeverEndsInWhitespace,
        AppProjectCompilesEveryHomeAssistantContractFile
    };

    private static readonly string ContractDirectory = FindContractDirectory();

    // -- mapper ---------------------------------------------------------------------------------

    private static void StateMapperMatchesTheGoldenFixtures()
    {
        var playing = new HaStateInput
        {
            PlaybackState = LibrespotPlaybackState.Playing,
            Title = "Blue in Green", Artist = "Miles Davis", Album = "Kind of Blue",
            ArtworkUrl = "https://i.scdn.co/image/ab67616d00001e02example",
            ContentId = "spotify:track:0aBcDeFgHiJkLmNoPqRsTu",
            DurationMs = 337000, PositionMs = 61250, Volume = 32768,
            IsSessionConnected = true
        };
        var pausedRemote = new HaStateInput
        {
            PlaybackState = LibrespotPlaybackState.Paused,
            Title = "So What", Artist = "Miles Davis", Album = "Kind of Blue",
            ContentId = "spotify:track:1AbCdEfGhIjKlMnOpQrStU",
            DurationMs = 562000, Volume = 65535, Shuffle = true, RepeatMode = 1,
            IsSessionConnected = true, IsRemote = true
        };
        var buffering = new HaStateInput
        {
            PlaybackState = LibrespotPlaybackState.Loading,
            Title = "Freddie Freeloader", Artist = "Miles Davis", Album = "Kind of Blue",
            Volume = 8061, RepeatMode = 2, IsSessionConnected = true, IsRecovering = true
        };
        var idle = new HaStateInput { PlaybackState = LibrespotPlaybackState.Stopped };

        AssertGolden("state_playing.json", HaStateMapper.Map(playing, 42));
        AssertGolden("state_paused_remote.json", HaStateMapper.Map(pausedRemote, 43));
        AssertGolden("state_buffering.json", HaStateMapper.Map(buffering, 44));
        AssertGolden("state_idle_offline.json", HaStateMapper.Map(idle, 0));
    }

    private static void AssertGolden(string fixture, HaStateFrame frame)
    {
        var expected = JObject.Parse(ReadFixture("valid", fixture));
        var actual = JObject.Parse(HaFrames.State(frame));
        Assert(JsonEquivalent(expected, actual), fixture + " differs from the mapper output:\n" + actual + "\n" + expected);
    }

    private static void MapperCoversEveryPlaybackState()
    {
        var expected = new Dictionary<LibrespotPlaybackState, string>
        {
            { LibrespotPlaybackState.Playing, "playing" },
            { LibrespotPlaybackState.Paused, "paused" },
            { LibrespotPlaybackState.Loading, "buffering" },
            { LibrespotPlaybackState.Stopped, "idle" }
        };
        foreach (LibrespotPlaybackState state in Enum.GetValues(typeof(LibrespotPlaybackState)))
        {
            Assert(expected.ContainsKey(state), "new playback state needs a wire mapping: " + state);
            Assert(HaStateMapper.MapPlayback(state) == expected[state], "wrong mapping for " + state);
        }
        Assert(HaStateMapper.MapPlayback((LibrespotPlaybackState)99) == "idle", "unknown states read as idle");
    }

    private static void VolumeConversionIsExactAtTheEdgesAndRoundTrips()
    {
        Assert(HaStateMapper.VolumeToWire(0) == 0.0, "0 maps to 0");
        Assert(HaStateMapper.VolumeToWire(65535) == 1.0, "65535 maps to 1");
        Assert(HaStateMapper.VolumeToWire(32768) == 0.5, "mid volume");
        Assert(HaStateMapper.VolumeToWire(8061) == 0.123, "three decimals");
        Assert(HaStateMapper.VolumeFromWire(0) == 0, "wire 0");
        Assert(HaStateMapper.VolumeFromWire(1) == 65535, "wire 1");
        Assert(HaStateMapper.VolumeFromWire(0.35) == 22937, "0.35 rounds to nearest");
        Assert(HaStateMapper.VolumeFromWire(1.0000001) == 65535, "values above 1 saturate defensively");
        Assert(HaStateMapper.VolumeFromWire(-0.5) == 0, "negative values saturate defensively");
        for (var raw = 0; raw <= 65535; raw += 7)
        {
            var wire = HaStateMapper.VolumeToWire((ushort)raw);
            Assert(wire >= 0 && wire <= 1, "volume stays inside the unit interval");
            var back = HaStateMapper.VolumeFromWire(wire);
            // Three decimals bound the error to half a step: about 33 of 65535.
            Assert(Math.Abs(back - raw) <= 33, "round trip drifted at " + raw);
        }
    }

    private static void RepeatSessionAndOutputMappings()
    {
        Assert(HaStateMapper.MapRepeat(0) == "off" && HaStateMapper.MapRepeat(1) == "all" && HaStateMapper.MapRepeat(2) == "one", "repeat names");
        Assert(HaStateMapper.MapRepeat(-1) == "off" && HaStateMapper.MapRepeat(7) == "off", "unknown repeat modes read as off");

        Func<bool, bool, bool, string> session = (connected, offline, recovering) =>
            HaStateMapper.MapSession(new HaStateInput { IsSessionConnected = connected, IsOffline = offline, IsRecovering = recovering });
        Assert(session(true, false, false) == "connected", "connected");
        Assert(session(true, true, true) == "offline", "offline wins over recovering");
        Assert(session(true, false, true) == "recovering", "recovering");
        Assert(session(false, false, false) == "offline", "not connected reads as offline");
        Assert(session(false, false, true) == "recovering", "recovering wins over not connected");

        var local = JObject.Parse(HaFrames.State(HaStateMapper.Map(new HaStateInput(), 1)));
        var remote = JObject.Parse(HaFrames.State(HaStateMapper.Map(new HaStateInput { IsRemote = true }, 1)));
        Assert((string)local["output"] == "local" && (string)remote["output"] == "remote", "output follows IsRemote");
    }

    private static void MissingMetadataProducesNullsNotPlaceholders()
    {
        var frame = JObject.Parse(HaFrames.State(HaStateInputFactory_FromEmptyState()));
        foreach (var key in new[] { "title", "artist", "album", "artwork_url", "content_id" })
            Assert(frame[key].Type == JTokenType.Null, key + " must be null without a track (no 'Unknown Track' placeholder)");
        Assert((string)frame["state"] == "idle", "no playback reads as idle");
    }

    private static HaStateFrame HaStateInputFactory_FromEmptyState()
    {
        return HaStateMapper.Map(HaStateInputFactory.FromMediaState(new MediaState()), 0);
    }

    private static void TextIsTrimmedTruncatedAndSurrogateSafe()
    {
        Assert(HaStateMapper.CleanText(null) == null, "null stays null");
        Assert(HaStateMapper.CleanText("   \t ") == null, "blank becomes null");
        Assert(HaStateMapper.CleanText("  Song  ") == "Song", "trimmed");
        var longText = new string('a', 1000);
        Assert(HaStateMapper.CleanText(longText).Length == HaContract.MaxTextLength, "truncated to the contract limit");
        // The emoji is a surrogate pair that straddles the cut point; it must be dropped, not split.
        var straddling = new string('a', HaContract.MaxTextLength - 1) + "\U0001F3B5" + "tail";
        var cut = HaStateMapper.CleanText(straddling);
        Assert(cut.Length == HaContract.MaxTextLength - 1, "a split surrogate pair is removed");
        Assert(!char.IsHighSurrogate(cut[cut.Length - 1]), "no dangling high surrogate");
        // And the result must be encodable as strict UTF-8, which the transport requires.
        new UTF8Encoding(false, true).GetBytes(cut);
    }

    private static void ArtworkAndContentIdAreSanitized()
    {
        Assert(HaStateMapper.CleanArtwork("https://i.scdn.co/image/abc") == "https://i.scdn.co/image/abc", "https artwork passes");
        foreach (var rejected in new[]
        {
            null, "", " ", "http://i.scdn.co/image/abc", "ms-appdata:///local/art.jpg",
            "C:\\Users\\x\\art.jpg", "file:///art.jpg", "/relative/path.jpg", "https://exa mple.com/a",
            "https://example.com/a\nb", "https://example.com/" + new string('a', 600)
        })
            Assert(HaStateMapper.CleanArtwork(rejected) == null, "artwork must be rejected: " + rejected);

        Assert(HaStateMapper.CleanContentId("spotify:track:0aBcDeFgHiJkLmNoPqRsTu") != null, "track id");
        Assert(HaStateMapper.CleanContentId("spotify:episode:0aBcDeFgHiJkLmNoPqRsTu") != null, "episode id");
        foreach (var rejected in new[]
        {
            null, "", "spotify:local:Artist:Album:Title:123", "spotify:playlist:0aBcDeFgHiJkLmNoPqRsTu",
            "spotify:track:short", "spotify:track:0aBcDeFgHiJkLmNoPqRsTuX", "https://open.spotify.com/track/x"
        })
            Assert(HaStateMapper.CleanContentId(rejected) == null, "content id must be rejected: " + rejected);
    }

    private static void MediaStateAdapterPrefersMetadataAndFallsBackToTrack()
    {
        var track = new LibrespotTrackInfo
        {
            Uri = "spotify:track:0aBcDeFgHiJkLmNoPqRsTu", Name = "Track name", Artist = "Track artist",
            Album = "Track album", CoverUrl = "https://i.scdn.co/image/fromtrack"
        };
        var metadata = new FullTrack
        {
            Name = "Metadata name", Uri = "spotify:track:1AbCdEfGhIjKlMnOpQrStU",
            Artists = new List<SimpleArtist> { new SimpleArtist { Name = "Metadata artist" }, new SimpleArtist { Name = "Second" } },
            Album = new SimpleAlbum { Name = "Metadata album" }
        };
        var state = new MediaState
        {
            PlaybackState = LibrespotPlaybackState.Playing, Track = track, Metadata = metadata,
            DurationMs = 1000, PositionMs = 200, Volume = 65535, IsSessionConnected = true
        };

        var input = HaStateInputFactory.FromMediaState(state);
        Assert(input.Title == "Metadata name" && input.Artist == "Metadata artist" && input.Album == "Metadata album", "metadata wins");
        Assert(input.ContentId == track.Uri, "the native track uri identifies the current track");
        Assert(input.ArtworkUrl == track.CoverUrl, "cover falls back to the track cover");

        state.Metadata = null;
        input = HaStateInputFactory.FromMediaState(state);
        Assert(input.Title == "Track name" && input.Artist == "Track artist" && input.Album == "Track album", "track fallback");

        state.Track = null;
        state.Metadata = new FullTrack { Name = "Only metadata", Artists = new List<SimpleArtist>() };
        input = HaStateInputFactory.FromMediaState(state);
        Assert(input.Title == "Only metadata" && input.Artist == null && input.Album == null, "empty artist list is tolerated");
    }

    private static void DjNarrationNeverPutsNarrationTextOnTheWire()
    {
        var state = new MediaState
        {
            PlaybackState = LibrespotPlaybackState.Playing,
            IsNarrationActive = true, NarrationDurationMs = 30000, DurationMs = 200000,
            NarrationText = "Hey SECRET-NAME, here is a track you played last Tuesday",
            Metadata = new FullTrack { Name = "Next song", Artists = new List<SimpleArtist> { new SimpleArtist { Name = "Someone" } } }
        };
        var json = HaFrames.State(HaStateMapper.Map(HaStateInputFactory.FromMediaState(state), 1));
        var frame = JObject.Parse(json);
        Assert((string)frame["title"] == "Spotify DJ" && (string)frame["artist"] == "Spotify", "narration shows a neutral label");
        Assert((uint)frame["duration_ms"] == 30000, "narration duration is used while it plays");
        Assert(!json.Contains("SECRET-NAME") && !json.Contains("Tuesday") && !json.Contains("Next song"), "nothing from the narration or the next song leaks");
    }

    private static void SerializedStateContainsOnlyContractKeysAndNoPrivateData()
    {
        var state = new MediaState
        {
            PlaybackState = LibrespotPlaybackState.Playing,
            Metadata = new FullTrack { Name = "Song", Artists = new List<SimpleArtist> { new SimpleArtist { Name = "Artist" } }, Album = new SimpleAlbum { Name = "Album" } },
            Track = new LibrespotTrackInfo { Uri = "spotify:track:0aBcDeFgHiJkLmNoPqRsTu", PlayRequestId = 987654321, AudioGeneration = 555, SessionGeneration = 777 },
            ContextUri = "spotify:playlist:PRIVATE-CONTEXT-URI", ContextName = "PRIVATE-CONTEXT-NAME",
            StatusMessage = "PRIVATE-STATUS", SpotifyConnectDeviceId = "PRIVATE-DEVICE-ID",
            SpotifyConnectDeviceName = "PRIVATE-DEVICE-NAME", NextDjSetUid = "PRIVATE-DJ-UID",
            IsSessionConnected = true
        };
        var json = HaFrames.State(HaStateMapper.Map(HaStateInputFactory.FromMediaState(state), 9));
        var keys = JObject.Parse(json).Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var allowed = HaContract.StateKeys.OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert(keys.SequenceEqual(allowed), "state keys are exactly the contract keys: " + string.Join(",", keys));
        Assert(!json.Contains("PRIVATE"), "context, status and device fields must never be serialized");
        Assert(!json.Contains("987654321") && !json.Contains("555") && !json.Contains("777"), "internal generation counters must not leak");
        Assert(Encoding.UTF8.GetByteCount(json) < HaContract.MaxFrameBytes, "a state frame fits comfortably in the frame limit");

        // Worst case: every text field at its limit still fits.
        var worst = new HaStateInput
        {
            Title = new string('t', 5000), Artist = new string('a', 5000), Album = new string('b', 5000),
            ArtworkUrl = "https://example.com/" + new string('c', 480), ContentId = "spotify:episode:0aBcDeFgHiJkLmNoPqRsTu",
            DurationMs = uint.MaxValue, PositionMs = uint.MaxValue
        };
        var worstJson = HaFrames.State(HaStateMapper.Map(worst, uint.MaxValue));
        Assert(Encoding.UTF8.GetByteCount(worstJson) < HaContract.MaxFrameBytes, "worst case state frame stays under the limit");
        Assert((uint)JObject.Parse(worstJson)["duration_ms"] == HaContract.MaxMilliseconds, "durations are clamped to the contract maximum");
    }

    // -- outgoing frames ------------------------------------------------------------------------

    private static void OutgoingControlFramesMatchTheFixtures()
    {
        var hello = HaFrames.Hello("3f2b8c1e-6a4d-4e0b-9d57-1c2a7b9e4f10", "Marantz Sala", "1.0.34.0", HaContract.CommandNames);
        Assert(JsonEquivalent(JObject.Parse(hello), JObject.Parse(ReadFixture("valid", "hello.json"))), "hello fixture");
        Assert(JsonEquivalent(JObject.Parse(HaFrames.Ack("cmd-1", true)), JObject.Parse(ReadFixture("valid", "ack_ok.json"))), "ack ok fixture");
        Assert(JsonEquivalent(JObject.Parse(HaFrames.Ack("cmd-2", false, "busy")), JObject.Parse(ReadFixture("valid", "ack_failed.json"))), "ack failed fixture");
        Assert(JsonEquivalent(JObject.Parse(HaFrames.Error("unknown_type")), JObject.Parse(ReadFixture("valid", "error_unknown_type.json"))), "error fixture");
        Assert((string)JObject.Parse(HaFrames.Ack("x", false))["error"] == "failed", "a failed ack always carries an error");
        Assert(JObject.Parse(HaFrames.Ack("x", true))["error"] == null, "a successful ack never carries an error");
        Assert(!hello.Contains("\n") && !hello.Contains(": ") && !hello.Contains(", "), "frames are compact");
    }

    // -- parser ---------------------------------------------------------------------------------

    private static void ParserAcceptsEveryValidCommandFixture()
    {
        var count = 0;
        foreach (var file in FixtureFiles("valid", "command_"))
        {
            var result = HaCommandParser.Parse(File.ReadAllText(file));
            Assert(result.Status == HaParseStatus.Command, Path.GetFileName(file) + " must parse: " + result.Status);
            Assert(result.ErrorCodeToSend == null, "no error is sent for a valid command");
            count++;
        }
        Assert(count == HaContract.CommandNames.Length, "one valid fixture per contract command");

        Func<string, HaCommand> parse = name => HaCommandParser.Parse(ReadFixture("valid", name)).Command;
        Assert(parse("command_play.json").Kind == HaCommandKind.Play && parse("command_play.json").Id == "c1", "play");
        Assert(parse("command_pause.json").Kind == HaCommandKind.Pause, "pause");
        Assert(parse("command_next.json").Kind == HaCommandKind.Next, "next");
        Assert(parse("command_previous.json").Kind == HaCommandKind.Previous, "previous");
        Assert(parse("command_seek.json").Kind == HaCommandKind.Seek && parse("command_seek.json").PositionMs == 90000, "seek");
        Assert(parse("command_volume_set.json").Kind == HaCommandKind.VolumeSet && parse("command_volume_set.json").Volume == 22937, "volume_set 0.35");
        Assert(parse("command_shuffle_set.json").Kind == HaCommandKind.ShuffleSet && parse("command_shuffle_set.json").Shuffle, "shuffle_set");
        Assert(parse("command_repeat_set.json").Kind == HaCommandKind.RepeatSet && parse("command_repeat_set.json").RepeatMode == 1, "repeat_set all = native 1");

        Assert(CommandNameCoverage(), "every contract command name has a parser case");
    }

    private static bool CommandNameCoverage()
    {
        // Each contract command, minimally valid, must parse; the parser cannot lag the contract.
        var samples = new Dictionary<string, string>
        {
            { "play", "" }, { "pause", "" }, { "next", "" }, { "previous", "" },
            { "seek", ",\"position_ms\":0" }, { "volume_set", ",\"volume\":0" },
            { "shuffle_set", ",\"shuffle\":false" }, { "repeat_set", ",\"repeat\":\"off\"" }
        };
        foreach (var name in HaContract.CommandNames)
        {
            string extra;
            if (!samples.TryGetValue(name, out extra)) return false;
            var json = "{\"v\":1,\"type\":\"command\",\"id\":\"i\",\"name\":\"" + name + "\"" + extra + "}";
            if (HaCommandParser.Parse(json).Status != HaParseStatus.Command) return false;
        }
        return true;
    }

    private static void ParserRejectsEveryInvalidCommandFixture()
    {
        var count = 0;
        foreach (var file in FixtureFiles("invalid", "command__"))
        {
            var name = Path.GetFileName(file);
            var result = HaCommandParser.Parse(File.ReadAllText(file));
            Assert(result.Command == null, name + " must not yield a command");
            var expected = name.Contains("version") ? HaParseStatus.UnsupportedVersion : HaParseStatus.InvalidFrame;
            Assert(result.Status == expected, name + " expected " + expected + " but was " + result.Status);
            count++;
        }
        Assert(count >= 12, "the invalid command fixtures are present");
    }

    private static void ParserClassifiesProtocolErrors()
    {
        Assert(HaCommandParser.Parse("{\"v\":1,\"type\":\"reboot\"}").Status == HaParseStatus.UnknownType, "unknown type");
        Assert(HaCommandParser.Parse("{\"v\":1,\"type\":\"reboot\"}").ErrorCodeToSend == "unknown_type", "unknown type is answered with unknown_type");
        Assert(HaCommandParser.Parse("{\"v\":2,\"type\":\"command\",\"id\":\"c\",\"name\":\"play\"}").Status == HaParseStatus.UnsupportedVersion, "version 2");
        Assert(HaCommandParser.Parse("{\"type\":\"command\",\"id\":\"c\",\"name\":\"play\"}").Status == HaParseStatus.UnsupportedVersion, "missing version");
        Assert(HaCommandParser.Parse("{\"v\":true,\"type\":\"command\",\"id\":\"c\",\"name\":\"play\"}").Status == HaParseStatus.UnsupportedVersion, "boolean version");
        Assert(HaCommandParser.Parse("{\"v\":1.0,\"type\":\"command\",\"id\":\"c\",\"name\":\"play\"}").Status == HaParseStatus.UnsupportedVersion, "float version");
        Assert(HaCommandParser.Parse("{\"v\":1}").Status == HaParseStatus.UnknownType, "missing type");
        Assert(HaCommandParser.Parse("{\"v\":1,\"type\":5}").Status == HaParseStatus.UnknownType, "non-string type");

        var padding = new string('x', HaContract.MaxFrameBytes);
        var oversized = HaCommandParser.Parse("{\"v\":1,\"type\":\"command\",\"id\":\"" + padding + "\",\"name\":\"play\"}");
        Assert(oversized.Status == HaParseStatus.TooLarge && oversized.ErrorCodeToSend == "too_large", "oversized");
        // The limit counts UTF-8 bytes, not characters.
        var multibyte = new string('\u00e9', HaContract.MaxFrameBytes / 2 + 1);
        Assert(HaCommandParser.Parse("\"" + multibyte + "\"").Status == HaParseStatus.TooLarge, "bytes, not characters");
    }

    private static void ParserIsStrictAboutTypesAndSyntax()
    {
        foreach (var garbage in new[] { null, "", "not json", "[]", "null", "42", "\"text\"", "{", "{\"v\":1,\"type\":\"command\"} trailing", "{} {}" })
            Assert(HaCommandParser.Parse(garbage).Status == HaParseStatus.InvalidFrame || HaCommandParser.Parse(garbage).Status == HaParseStatus.UnsupportedVersion,
                "garbage must not become a command: " + garbage);
        Assert(HaCommandParser.Parse("not json").Status == HaParseStatus.InvalidFrame, "syntax error is an invalid frame");
        Assert(HaCommandParser.Parse("[]").Status == HaParseStatus.InvalidFrame, "non-object is an invalid frame");

        Func<string, HaParseStatus> volume = v => HaCommandParser.Parse("{\"v\":1,\"type\":\"command\",\"id\":\"c\",\"name\":\"volume_set\",\"volume\":" + v + "}").Status;
        Assert(volume("0") == HaParseStatus.Command && volume("1") == HaParseStatus.Command && volume("0.5") == HaParseStatus.Command, "valid volumes (integer and float JSON numbers)");
        foreach (var bad in new[] { "true", "\"0.5\"", "null", "NaN", "Infinity", "-Infinity", "-0.1", "1.0001", "[0.5]" })
            Assert(volume(bad) == HaParseStatus.InvalidFrame, "volume " + bad + " must be rejected");

        Func<string, HaParseStatus> seek = v => HaCommandParser.Parse("{\"v\":1,\"type\":\"command\",\"id\":\"c\",\"name\":\"seek\",\"position_ms\":" + v + "}").Status;
        Assert(seek("0") == HaParseStatus.Command && seek("86400000") == HaParseStatus.Command, "valid positions");
        foreach (var bad in new[] { "-1", "86400001", "1.5", "1e3", "true", "\"5\"", "99999999999999999999999" })
            Assert(seek(bad) == HaParseStatus.InvalidFrame, "position " + bad + " must be rejected");

        Assert(HaCommandParser.Parse("{\"v\":1,\"type\":\"command\",\"id\":\"c\",\"id\":\"d\",\"name\":\"play\"}").Status == HaParseStatus.InvalidFrame, "duplicate keys are ambiguous and rejected");
        Assert(HaCommandParser.Parse("{\"v\":1,\"type\":\"command\",\"id\":\"" + new string('i', 65) + "\",\"name\":\"play\"}").Status == HaParseStatus.InvalidFrame, "id longer than 64");
        Assert(HaCommandParser.Parse("{\"v\":1,\"type\":\"command\",\"id\":\"" + new string('i', 64) + "\",\"name\":\"play\"}").Status == HaParseStatus.Command, "id of exactly 64");
        Assert(HaCommandParser.Parse("{\"v\":1,\"type\":\"command\",\"id\":7,\"name\":\"play\"}").Status == HaParseStatus.InvalidFrame, "numeric id");

        var deep = new StringBuilder("{\"v\":1,\"type\":\"command\",\"id\":\"c\",\"name\":\"play\",\"x\":");
        for (var i = 0; i < 200; i++) deep.Append('[');
        for (var i = 0; i < 200; i++) deep.Append(']');
        deep.Append('}');
        Assert(HaCommandParser.Parse(deep.ToString()).Status == HaParseStatus.InvalidFrame, "deep nesting is rejected without exhausting the stack");
    }

    private static void ParserTreatsPeerErrorsAndWrongDirectionFramesCorrectly()
    {
        var peer = HaCommandParser.Parse(ReadFixture("valid", "error_unknown_type.json"));
        Assert(peer.Status == HaParseStatus.PeerError && peer.PeerErrorCode == "unknown_type" && peer.ErrorCodeToSend == null, "error frame from Home Assistant");
        Assert(HaCommandParser.Parse(ReadFixture("invalid", "error__bad_code.json")).Status == HaParseStatus.InvalidFrame, "bad error code");
        Assert(HaCommandParser.Parse("{\"v\":1,\"type\":\"error\",\"code\":\"too_large\",\"extra\":1}").Status == HaParseStatus.InvalidFrame, "extra field on error");
        foreach (var fixture in new[] { "hello.json", "state_playing.json", "ack_ok.json" })
            Assert(HaCommandParser.Parse(ReadFixture("valid", fixture)).Status == HaParseStatus.InvalidFrame,
                fixture + " is a Lumia -> HA frame and invalid from Home Assistant");
    }

    // -- throttle -------------------------------------------------------------------------------

    private static void ThrottleSendsFirstChangeImmediatelyAndCoalescesBursts()
    {
        var throttle = new HaStateThrottle();
        var t0 = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        TimeSpan wait;
        Assert(throttle.Request(t0, out wait) && wait == TimeSpan.Zero, "first change goes out at once");
        Assert(!throttle.HasPending, "nothing pending after an immediate send");

        Assert(!throttle.Request(t0.AddMilliseconds(10), out wait), "a change 10 ms later is held");
        Assert(wait == TimeSpan.FromMilliseconds(240), "wait is the remaining interval");
        Assert(throttle.HasPending, "a held change is pending");
        Assert(!throttle.Request(t0.AddMilliseconds(100), out wait) && wait == TimeSpan.FromMilliseconds(150), "further changes coalesce into the same pending send");
        Assert(!throttle.Request(t0.AddMilliseconds(200), out wait) && wait == TimeSpan.FromMilliseconds(50), "wait keeps shrinking");

        Assert(!throttle.TryFlush(t0.AddMilliseconds(249), out wait) && wait == TimeSpan.FromMilliseconds(1), "an early flush reports the remaining wait");
        Assert(throttle.HasPending, "an early flush keeps the snapshot pending");
        Assert(throttle.TryFlush(t0.AddMilliseconds(250), out wait), "flushed exactly at the interval");
        Assert(!throttle.HasPending && !throttle.TryFlush(t0.AddMilliseconds(260), out wait) && wait == TimeSpan.Zero, "one flush per burst; nothing left to re-arm");
        Assert(!throttle.Request(t0.AddMilliseconds(300), out wait) && wait == TimeSpan.FromMilliseconds(200), "interval restarts from the flush");
    }

    private static void ThrottleFlushResetAndClockEdgeCases()
    {
        var t0 = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        TimeSpan wait;

        var throttle = new HaStateThrottle();
        Assert(!throttle.TryFlush(t0, out wait) && wait == TimeSpan.Zero, "flush with nothing pending does nothing");
        throttle.Request(t0, out wait);
        throttle.Request(t0.AddMilliseconds(5), out wait);
        throttle.MarkSent(t0.AddMilliseconds(6));
        Assert(!throttle.HasPending, "a send for another reason satisfies the pending one");
        Assert(!throttle.Request(t0.AddMilliseconds(7), out wait) && wait == TimeSpan.FromMilliseconds(249), "and restarts the interval");

        throttle.Reset();
        Assert(throttle.Request(t0.AddMilliseconds(8), out wait), "after a reconnect the first snapshot is immediate");

        var backwards = new HaStateThrottle();
        backwards.Request(t0, out wait);
        Assert(backwards.Request(t0.AddSeconds(-30), out wait), "a clock that moved backwards never blocks sending");

        var disabled = new HaStateThrottle(TimeSpan.Zero);
        Assert(disabled.Request(t0, out wait) && disabled.Request(t0, out wait), "a zero interval never holds anything");
        AssertThrows<ArgumentOutOfRangeException>(() => new HaStateThrottle(TimeSpan.FromSeconds(-1)), "negative interval");
    }

    private static void ThrottleLimitsSustainedChangesToTheConfiguredRate()
    {
        var throttle = new HaStateThrottle();
        var start = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        var sends = 0;
        var timerAt = (DateTime?)null;
        // A change every 10 ms for 5 s (500 changes) with a timer that fires as requested.
        for (var ms = 0; ms <= 5000; ms += 10)
        {
            var now = start.AddMilliseconds(ms);
            if (timerAt != null && now >= timerAt.Value)
            {
                TimeSpan flushWait;
                if (throttle.TryFlush(now, out flushWait)) sends++;
                timerAt = flushWait > TimeSpan.Zero ? now + flushWait : (DateTime?)null;
            }
            TimeSpan wait;
            if (throttle.Request(now, out wait)) sends++;
            else if (timerAt == null) timerAt = now + wait;
        }
        // 5 s at 250 ms is 20 intervals; allow one extra for the boundary.
        Assert(sends >= 19 && sends <= 21, "expected about 20 sends in 5 s but saw " + sends);
    }

    // -- review fixes ---------------------------------------------------------------------------

    private static void ThrottleEarlyTimerReArmsInsteadOfDroppingTheSnapshot()
    {
        // Change at t0 is sent; change at t0+10 ms is held for 240 ms. The timer fires 15 ms early
        // (Windows clock granularity). The pending snapshot must still go out, and no later change
        // may be needed to release it.
        var throttle = new HaStateThrottle();
        var t0 = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        TimeSpan wait;
        throttle.Request(t0, out wait);
        Assert(!throttle.Request(t0.AddMilliseconds(10), out wait), "second change is held");
        var firedAt = t0.AddMilliseconds(10) + wait - TimeSpan.FromMilliseconds(15);
        Assert(!throttle.TryFlush(firedAt, out wait), "too early");
        Assert(wait == TimeSpan.FromMilliseconds(15), "the remaining wait lets the caller re-arm: " + wait);
        Assert(throttle.TryFlush(firedAt + wait, out wait), "the re-armed timer releases the snapshot");
        Assert(!throttle.HasPending, "nothing is left behind");
    }

    private static void ParserNeverThrowsOnHugeIntegers()
    {
        foreach (var json in new[]
        {
            "{\"v\":99999999999999999999,\"type\":\"command\",\"id\":\"c\",\"name\":\"play\"}",
            "{\"v\":-99999999999999999999,\"type\":\"command\",\"id\":\"c\",\"name\":\"play\"}",
            "{\"v\":1,\"type\":\"command\",\"id\":\"c\",\"name\":\"seek\",\"position_ms\":99999999999999999999}",
            "{\"v\":1,\"type\":\"command\",\"id\":\"c\",\"name\":\"volume_set\",\"volume\":99999999999999999999}",
            "{\"v\":1,\"type\":\"command\",\"id\":\"c\",\"name\":\"seek\",\"position_ms\":9223372036854775807}"
        })
        {
            HaParseResult result;
            try { result = HaCommandParser.Parse(json); }
            catch (Exception ex) { throw new InvalidOperationException("Parse threw " + ex.GetType().Name + " for " + json); }
            Assert(result.Command == null, "a huge integer never yields a command: " + json);
            Assert(result.Status == (json.Contains("\"v\":1,") ? HaParseStatus.InvalidFrame : HaParseStatus.UnsupportedVersion), "status for " + json + ": " + result.Status);
        }
    }

    private static void ParserAgreesWithPythonOnRuntimeOnlyFixtures()
    {
        var expected = ReadExpected("runtime_only");
        Assert(expected.Count >= 4, "runtime_only fixtures are present");
        foreach (var pair in expected)
        {
            var result = HaCommandParser.Parse(ReadFixture("runtime_only", pair.Key));
            Assert(result.Status == ToStatus(pair.Value), pair.Key + " expected " + pair.Value + " but was " + result.Status);
        }
    }

    private static void ParserRejectsEveryMalformedFixture()
    {
        var expected = ReadExpected("malformed");
        Assert(expected.Count >= 15, "malformed fixtures are present");
        foreach (var pair in expected)
        {
            var result = HaCommandParser.Parse(ReadFixture("malformed", pair.Key));
            Assert(result.Status == ToStatus(pair.Value), pair.Key + " expected " + pair.Value + " but was " + result.Status);
        }
    }

    private static void StrictJsonAcceptsStandardJson()
    {
        foreach (var json in new[]
        {
            "{}", "[]", " {\"a\" : [1, -2, 3.5, 1e3, 2E-2, 0, -0.0, true, false, null] } ", "\"text\"", "0",
            "{\"esc\":\"\\\" \\\\ \\/ \\b \\f \\n \\r \\t \\u00e9 \\uD83C\\uDFB5\"}",
            "{\"unicode\":\"\u00e9\U0001F3B5\"}", "\r\n\t{\"a\":{\"b\":{}}}\r\n"
        })
            Assert(HaStrictJson.IsValid(json, 8), "standard JSON must be accepted: " + json);
        foreach (var json in new[] { null, "", " ", "-", "1.", ".5", "01", "1e", "+1", "{\"a\"}", "[1,]", "{,}", "tru", "nul", "\"open" })
            Assert(!HaStrictJson.IsValid(json, 8), "must be rejected: " + json);
        Assert(HaStrictJson.IsValid("[[[]]]", 3) && !HaStrictJson.IsValid("[[[[]]]]", 3), "depth limit");
        foreach (var file in FixtureFiles("valid", ""))
            Assert(HaStrictJson.IsValid(File.ReadAllText(file), 8), "every valid fixture is strict JSON: " + Path.GetFileName(file));
    }

    private static void ArtworkFallsThroughToTheFirstHttpsCandidate()
    {
        var state = new MediaState
        {
            ArtworkUri = "ms-appdata:///local/OfflineCatalog/art.jpg",
            Metadata = new FullTrack { Name = "x", Album = new SimpleAlbum { Name = "a", Images = new List<Image> { new Image { Url = "https://i.scdn.co/image/album" } } } },
            Track = new LibrespotTrackInfo { CoverUrl = "https://i.scdn.co/image/track" }
        };
        Assert(HaStateInputFactory.FromMediaState(state).ArtworkUrl == "https://i.scdn.co/image/album", "a local cache path falls through to the album image");

        state.Metadata.Album.Images = new List<Image>();
        Assert(HaStateInputFactory.FromMediaState(state).ArtworkUrl == "https://i.scdn.co/image/track", "then to the track cover");

        state.ArtworkUri = "https://i.scdn.co/image/resolved";
        Assert(HaStateInputFactory.FromMediaState(state).ArtworkUrl == "https://i.scdn.co/image/resolved", "a usable resolved artwork still wins");

        state.ArtworkUri = "http://insecure/art.jpg";
        state.Track.CoverUrl = "ms-appdata:///local/x.jpg";
        Assert(HaStateInputFactory.FromMediaState(state).ArtworkUrl == null, "no usable candidate means null");

        state.IsNarrationActive = true;
        state.ArtworkUri = "https://i.scdn.co/image/dj";
        Assert(HaStateInputFactory.FromMediaState(state).ArtworkUrl == "https://i.scdn.co/image/dj", "narration keeps the artwork");
    }

    private static void HelloCleansTheDeviceNameAndRejectsBadIdentity()
    {
        const string id = "3f2b8c1e-6a4d-4e0b-9d57-1c2a7b9e4f10";
        Func<string, string> name = n => (string)JObject.Parse(HaFrames.Hello(id, n, "1.0.34.0", HaContract.CommandNames))["device_name"];
        Assert(name("  Marantz Sala  ") == "Marantz Sala", "trimmed");
        Assert(name(null) == HaContract.DefaultDeviceName && name("   ") == HaContract.DefaultDeviceName, "empty names get the default");
        Assert(name(new string('n', 200)).Length == HaContract.MaxDeviceNameLength, "long names are cut to the contract limit");
        Assert(name(new string('n', 63) + " tail") == new string('n', 63), "and never end in whitespace");

        var commands = JObject.Parse(HaFrames.Hello(id, "x", "1.0.34.0", new[] { "play", "play", "reboot", "pause" }))["commands"].Select(t => (string)t).ToArray();
        Assert(commands.SequenceEqual(new[] { "play", "pause" }), "duplicate and unknown commands are dropped: " + string.Join(",", commands));
        Assert(JObject.Parse(HaFrames.Hello(id, "x", "1.0.34.0", null))["commands"].Count() == 0, "null command list is empty");

        AssertThrows<ArgumentException>(() => HaFrames.Hello("not-a-guid", "x", "1.0.34.0", HaContract.CommandNames), "bad device id");
        AssertThrows<ArgumentException>(() => HaFrames.Hello(id.ToUpperInvariant(), "x", "1.0.34.0", HaContract.CommandNames), "upper-case device id");
        AssertThrows<ArgumentException>(() => HaFrames.Hello(id, "x", "1.0.34", HaContract.CommandNames), "three-part version");
        AssertThrows<ArgumentException>(() => HaFrames.Hello(id, "x", null, HaContract.CommandNames), "missing version");
    }

    private static void TruncatedTextNeverEndsInWhitespace()
    {
        var spaced = new string('a', HaContract.MaxTextLength - 1) + " " + new string('b', 50);
        var cut = HaStateMapper.CleanText(spaced);
        Assert(cut == new string('a', HaContract.MaxTextLength - 1), "a cut just after a space is trimmed again");
        Assert(HaStateMapper.CleanText(new string(' ', 10) + new string('c', 300)).Length == HaContract.MaxTextLength, "leading whitespace does not count");
        Assert(HaStateMapper.CleanText("x", 0) == null, "a zero limit yields null rather than an empty string");
    }

    private static Dictionary<string, string> ReadExpected(string kind)
    {
        var expected = JObject.Parse(ReadFixture(kind, "expected.json"));
        var files = Directory.GetFiles(Path.Combine(ContractDirectory, "fixtures", kind)).Select(Path.GetFileName).Where(n => n != "expected.json").OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var listed = expected.Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert(files.SequenceEqual(listed), kind + "/expected.json is out of date");
        return expected.Properties().ToDictionary(p => p.Name, p => (string)p.Value);
    }

    private static HaParseStatus ToStatus(string code)
    {
        switch (code)
        {
            case "unsupported_version": return HaParseStatus.UnsupportedVersion;
            case "invalid_frame": return HaParseStatus.InvalidFrame;
            case "unknown_type": return HaParseStatus.UnknownType;
            case "too_large": return HaParseStatus.TooLarge;
            default: throw new InvalidOperationException("unknown expected code " + code);
        }
    }

    // -- project wiring -------------------------------------------------------------------------

    private static void AppProjectCompilesEveryHomeAssistantContractFile()
    {
        var root = Path.GetFullPath(Path.Combine(ContractDirectory, "..", "..", ".."));
        var project = File.ReadAllText(Path.Combine(root, "LibreSpotUWP", "LibreSpotUWP.csproj"));
        var folder = Path.Combine(root, "LibreSpotUWP", "Services", "HomeAssistant");
        var files = Directory.GetFiles(folder, "*.cs");
        Assert(files.Length >= 6, "contract sources exist");
        foreach (var file in files)
            Assert(project.Contains("Services\\HomeAssistant\\" + Path.GetFileName(file)),
                "LibreSpotUWP.csproj must compile " + Path.GetFileName(file) + " (old-style projects list files explicitly)");

        var testProject = File.ReadAllText(Path.Combine(root, "tests", "LibreSpotUWP.Playback.Tests", "LibreSpotUWP.Playback.Tests.csproj"));
        foreach (var file in files)
            Assert(testProject.Contains("HomeAssistant\\" + Path.GetFileName(file)), "the host tests must link " + Path.GetFileName(file));

        // Contract code must stay free of WinRT so it can be tested off-device.
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            Assert(!source.Contains("using Windows."), Path.GetFileName(file) + " must not reference WinRT");
        }
    }

    // -- helpers --------------------------------------------------------------------------------

    // JSON has one number type: 1 and 1.0 are the same value, which Newtonsoft's DeepEquals distinguishes.
    private static bool JsonEquivalent(JToken left, JToken right)
    {
        if (left == null || right == null) return left == right;
        var numeric = new[] { JTokenType.Integer, JTokenType.Float };
        if (numeric.Contains(left.Type) && numeric.Contains(right.Type)) return (double)left == (double)right;
        if (left.Type != right.Type) return false;
        if (left is JObject leftObject)
        {
            var rightObject = (JObject)right;
            if (leftObject.Count != rightObject.Count) return false;
            foreach (var property in leftObject.Properties())
                if (!JsonEquivalent(property.Value, rightObject[property.Name])) return false;
            return true;
        }
        if (left is JArray leftArray)
        {
            var rightArray = (JArray)right;
            return leftArray.Count == rightArray.Count && leftArray.Zip(rightArray, JsonEquivalent).All(equal => equal);
        }
        return JToken.DeepEquals(left, right);
    }

    private static string FindContractDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "contracts", "lumia_hifi", "v1");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("contracts/lumia_hifi/v1 was not found above " + AppContext.BaseDirectory);
    }

    private static string ReadFixture(string kind, string name)
    {
        return File.ReadAllText(Path.Combine(ContractDirectory, "fixtures", kind, name));
    }

    private static IEnumerable<string> FixtureFiles(string kind, string prefix)
    {
        return Directory.GetFiles(Path.Combine(ContractDirectory, "fixtures", kind), prefix + "*.json").OrderBy(f => f, StringComparer.Ordinal);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertThrows<TException>(Action action, string message) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException("Expected " + typeof(TException).Name + ": " + message);
    }
}
