using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibreSpotUWP.Models;
using LibreSpotUWP.Services.HomeAssistant;
using Newtonsoft.Json.Linq;

// WP5: the WinRT-free half of the Home Assistant link (session protocol, command dispatch, retry
// policy and settings validation). The WinRT transport only moves bytes and timers around this code.
internal static class HomeAssistantLinkTests
{
    public static readonly Action[] All =
    {
        SessionStartsWithHelloThenAnImmediateSnapshot,
        EveryCommandIsExecutedAckedAndFollowedBySnapshot,
        FailedAndSlowCommandsAreAckedWithAReason,
        CommandsRunOneAtATimeInArrivalOrder,
        StateChangesAreThrottledAndFlushedLater,
        PositionTicksAreNotSentButRealChangesAre,
        BadFramesFromHomeAssistantGetTheContractReply,
        IdleCoversInFlightCommandsForBackgroundWakes,
        ClosedSessionSendsNothing,
        StateReadFailureDoesNotBreakTheSession,
        CloseCodesMapToRetryDecisions,
        BackoffGrowsCapsAndJitters,
        UrlsAreNormalizedToTheLinkEndpoint,
        SecretsAreValidatedWithoutEchoingThem,
        EveryStatusHasAUserFacingDescription,
        AppWiringIsOptInAndCompleteAcrossThemes
    };

    // -- fakes ----------------------------------------------------------------------------------

    private sealed class FakeControl : IHaPlaybackControl
    {
        public readonly List<string> Calls = new List<string>();
        public Func<string, Task> Behaviour = _ => Task.CompletedTask;

        private Task Record(string call) { lock (Calls) Calls.Add(call); return Behaviour(call); }
        public Task PlayAsync() { return Record("play"); }
        public Task PauseAsync() { return Record("pause"); }
        public void Next() { Record("next").GetAwaiter().GetResult(); }
        public void Previous() { Record("previous").GetAwaiter().GetResult(); }
        public void Seek(uint positionMs) { Record("seek:" + positionMs).GetAwaiter().GetResult(); }
        public Task SetVolumeAsync(ushort volume) { return Record("volume:" + volume); }
        public Task SetShuffleAsync(bool enabled) { return Record("shuffle:" + enabled); }
        public Task SetRepeatAsync(int mode) { return Record("repeat:" + mode); }
    }

    private sealed class Harness
    {
        public readonly List<JObject> Sent = new List<JObject>();
        public readonly List<TimeSpan> Scheduled = new List<TimeSpan>();
        public readonly List<string> Logs = new List<string>();
        public readonly FakeControl Control = new FakeControl();
        public DateTime Now = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        public HaStateInput State = new HaStateInput { PlaybackState = LibrespotPlaybackState.Playing, Title = "Song", IsSessionConnected = true, Volume = 32768 };
        public readonly HaLinkSession Session;

        public Harness(TimeSpan? commandTimeout = null)
        {
            Session = new HaLinkSession(new HaLinkSessionOptions
            {
                DeviceId = "3f2b8c1e-6a4d-4e0b-9d57-1c2a7b9e4f10",
                DeviceName = "Marantz Sala",
                AppVersion = "1.0.35.0",
                Send = text => { lock (Sent) Sent.Add(JObject.Parse(text)); return Task.CompletedTask; },
                CurrentState = () => State,
                Control = Control,
                UtcNow = () => Now,
                ScheduleFlush = wait => Scheduled.Add(wait),
                Log = message => { lock (Logs) Logs.Add(message); },
                CommandTimeout = commandTimeout ?? TimeSpan.FromSeconds(4)
            });
        }

        public string[] Types() { lock (Sent) return Sent.Select(f => (string)f["type"]).ToArray(); }
        public void Clear() { lock (Sent) Sent.Clear(); }
        public HaSessionAction Frame(string json) { return Session.OnFrameAsync(json).GetAwaiter().GetResult(); }
    }

    private static string Command(string id, string name, string extra = "")
    {
        return "{\"v\":1,\"type\":\"command\",\"id\":\"" + id + "\",\"name\":\"" + name + "\"" + extra + "}";
    }

    // -- session --------------------------------------------------------------------------------

    private static void SessionStartsWithHelloThenAnImmediateSnapshot()
    {
        var h = new Harness();
        h.Session.StartAsync().GetAwaiter().GetResult();
        Assert(h.Types().SequenceEqual(new[] { "hello", "state" }), "hello first, then a snapshot: " + string.Join(",", h.Types()));
        var hello = h.Sent[0];
        Assert((string)hello["device_name"] == "Marantz Sala" && (string)hello["app_version"] == "1.0.35.0", "hello identity");
        Assert(hello["commands"].Select(t => (string)t).SequenceEqual(HaContract.CommandNames), "every command is announced");
        Assert((uint)h.Sent[1]["seq"] == 0 && (string)h.Sent[1]["state"] == "playing", "first snapshot has seq 0");
        Assert(h.Scheduled.Count == 0, "the first snapshot is not throttled");
    }

    private static void EveryCommandIsExecutedAckedAndFollowedBySnapshot()
    {
        var h = new Harness();
        h.Session.StartAsync().GetAwaiter().GetResult();
        h.Clear();
        var commands = new[]
        {
            Tuple.Create(Command("1", "play"), "play"),
            Tuple.Create(Command("2", "pause"), "pause"),
            Tuple.Create(Command("3", "next"), "next"),
            Tuple.Create(Command("4", "previous"), "previous"),
            Tuple.Create(Command("5", "seek", ",\"position_ms\":90000"), "seek:90000"),
            Tuple.Create(Command("6", "volume_set", ",\"volume\":0.35"), "volume:22937"),
            Tuple.Create(Command("7", "shuffle_set", ",\"shuffle\":true"), "shuffle:True"),
            Tuple.Create(Command("8", "repeat_set", ",\"repeat\":\"one\""), "repeat:2")
        };
        foreach (var command in commands)
            Assert(!h.Frame(command.Item1).Close, "a command never closes the link");
        Assert(h.Control.Calls.SequenceEqual(commands.Select(c => c.Item2)), "each command reaches playback once: " + string.Join(",", h.Control.Calls));
        var types = h.Types();
        Assert(types.Length == 16, "an ack and a snapshot per command");
        for (var i = 0; i < commands.Length; i++)
        {
            Assert(types[2 * i] == "ack" && types[2 * i + 1] == "state", "ack precedes its snapshot");
            Assert((string)h.Sent[2 * i]["id"] == (i + 1).ToString() && (bool)h.Sent[2 * i]["ok"], "ack carries the command id");
        }
        var seqs = h.Sent.Where(f => (string)f["type"] == "state").Select(f => (uint)f["seq"]).ToArray();
        Assert(seqs.SequenceEqual(Enumerable.Range(1, 8).Select(i => (uint)i)), "seq is monotonic across the connection");
    }

    private static void FailedAndSlowCommandsAreAckedWithAReason()
    {
        var h = new Harness(TimeSpan.FromMilliseconds(50));
        h.Control.Behaviour = call =>
        {
            if (call == "pause") throw new InvalidOperationException("boom");
            if (call == "play") return Task.FromException(new InvalidOperationException("async boom"));
            if (call == "next") return Task.CompletedTask;
            if (call.StartsWith("volume", StringComparison.Ordinal)) return Task.Delay(2000);
            return Task.CompletedTask;
        };
        h.Frame(Command("a", "pause"));
        h.Frame(Command("b", "play"));
        h.Frame(Command("c", "volume_set", ",\"volume\":1"));
        var acks = h.Sent.Where(f => (string)f["type"] == "ack").ToArray();
        Assert((string)acks[0]["error"] == "failed" && !(bool)acks[0]["ok"], "a synchronous exception is 'failed'");
        Assert((string)acks[1]["error"] == "failed", "a faulted task is 'failed'");
        Assert((string)acks[2]["error"] == "busy", "a command slower than the timeout is 'busy'");
        Assert(h.Sent.Count(f => (string)f["type"] == "state") == 3, "a snapshot follows even a failed command");
        Assert(h.Logs.Count >= 3 && h.Logs.All(l => !l.Contains("boom")), "failures are logged by HRESULT, not by message text");
    }

    private static void CommandsRunOneAtATimeInArrivalOrder()
    {
        var h = new Harness();
        var running = 0;
        var maxRunning = 0;
        h.Control.Behaviour = async call =>
        {
            var now = Interlocked.Increment(ref running);
            lock (h) maxRunning = Math.Max(maxRunning, now);
            await Task.Delay(20).ConfigureAwait(false);
            Interlocked.Decrement(ref running);
        };
        var frames = Enumerable.Range(0, 6).Select(i => h.Session.OnFrameAsync(Command("c" + i, i % 2 == 0 ? "pause" : "play"))).ToArray();
        Task.WaitAll(frames);
        Assert(maxRunning == 1, "commands never overlap: " + maxRunning);
        var ackIds = h.Sent.Where(f => (string)f["type"] == "ack").Select(f => (string)f["id"]).ToArray();
        Assert(ackIds.SequenceEqual(Enumerable.Range(0, 6).Select(i => "c" + i)), "acks keep arrival order: " + string.Join(",", ackIds));
    }

    private static void StateChangesAreThrottledAndFlushedLater()
    {
        var h = new Harness();
        h.Session.StartAsync().GetAwaiter().GetResult(); // t0: snapshot sent
        h.Clear();
        h.Now = h.Now.AddMilliseconds(10);
        h.Session.OnStateChangedAsync().GetAwaiter().GetResult();
        h.Now = h.Now.AddMilliseconds(10);
        h.State.Title = "Latest";
        h.Session.OnStateChangedAsync().GetAwaiter().GetResult();
        Assert(h.Sent.Count == 0, "changes inside the interval are held");
        Assert(h.Scheduled.Count == 2 && h.Scheduled[0] == TimeSpan.FromMilliseconds(240), "a flush is scheduled for the remaining interval");

        h.Now = h.Now.AddMilliseconds(200); // 220 ms: an early timer
        h.Session.OnFlushTimerAsync().GetAwaiter().GetResult();
        Assert(h.Sent.Count == 0 && h.Scheduled.Last() == TimeSpan.FromMilliseconds(30), "an early timer is re-armed");

        h.Now = h.Now.AddMilliseconds(30);
        h.Session.OnFlushTimerAsync().GetAwaiter().GetResult();
        Assert(h.Sent.Count == 1 && (string)h.Sent[0]["title"] == "Latest", "one snapshot carries the latest state");
        h.Session.OnFlushTimerAsync().GetAwaiter().GetResult();
        Assert(h.Sent.Count == 1, "a second timer finds nothing pending");

        // A command reply counts as a send: the next change waits a full interval again.
        h.Frame(Command("x", "pause"));
        h.Clear();
        h.Session.OnStateChangedAsync().GetAwaiter().GetResult();
        Assert(h.Sent.Count == 0, "the snapshot after a command restarts the interval");
    }

    private static void PositionTicksAreNotSentButRealChangesAre()
    {
        var h = new Harness();
        h.State.PositionMs = 10000;
        h.State.DurationMs = 300000;
        h.Session.StartAsync().GetAwaiter().GetResult();
        h.Clear();

        // 20 s of MediaService's 500 ms position ticks while playing: nothing to send.
        for (var i = 1; i <= 40; i++)
        {
            h.Now = h.Now.AddMilliseconds(500);
            h.State.PositionMs = (uint)(10000 + i * 500);
            h.Session.OnStateChangedAsync().GetAwaiter().GetResult();
            h.Session.OnFlushTimerAsync().GetAwaiter().GetResult();
        }
        Assert(h.Sent.Count == 0, "position ticks that match extrapolation are not sent: " + h.Sent.Count);

        // A seek (position jumps away from the extrapolation) is sent.
        h.Now = h.Now.AddMilliseconds(500);
        h.State.PositionMs = 200000;
        h.Session.OnStateChangedAsync().GetAwaiter().GetResult();
        Assert(h.Sent.Count == 1 && (uint)h.Sent[0]["position_ms"] == 200000, "a seek is sent");

        // A pause is sent; afterwards a static position is not.
        h.Now = h.Now.AddSeconds(1);
        h.State.PlaybackState = LibrespotPlaybackState.Paused;
        h.Session.OnStateChangedAsync().GetAwaiter().GetResult();
        Assert(h.Sent.Count == 2 && (string)h.Sent[1]["state"] == "paused", "a pause is sent");
        h.Now = h.Now.AddSeconds(30);
        h.Session.OnStateChangedAsync().GetAwaiter().GetResult();
        Assert(h.Sent.Count == 2, "a paused, unchanged position is not resent");

        // Any other field change is sent even inside the tolerance.
        h.Now = h.Now.AddSeconds(1);
        h.State.Volume = 1000;
        h.Session.OnStateChangedAsync().GetAwaiter().GetResult();
        Assert(h.Sent.Count == 3, "a volume change is sent");
        var seqs = h.Sent.Select(f => (uint)f["seq"]).ToArray();
        Assert(seqs.SequenceEqual(new uint[] { 1, 2, 3 }), "skipped snapshots consume no seq: " + string.Join(",", seqs));

        // Commands always produce a snapshot, even if nothing changed.
        h.Clear();
        h.Frame(Command("c", "pause"));
        Assert(h.Types().SequenceEqual(new[] { "ack", "state" }), "the snapshot after a command is never skipped");

        Assert(!HaLinkSession.OnlyPositionAdvanced(null, new HaStateFrame(), TimeSpan.Zero), "no previous frame means send");
    }

    private static void BadFramesFromHomeAssistantGetTheContractReply()
    {
        var h = new Harness();
        Func<string, string> errorFor = json =>
        {
            h.Clear();
            var action = h.Frame(json);
            Assert(!action.Close, "recoverable: " + json);
            return h.Sent.Count == 1 && (string)h.Sent[0]["type"] == "error" ? (string)h.Sent[0]["code"] : "none";
        };
        Assert(errorFor("not json") == "invalid_frame", "syntax error");
        Assert(errorFor("{\"v\":1,\"type\":\"reboot\"}") == "unknown_type", "unknown type");
        Assert(errorFor(Command("1", "volume_set", ",\"volume\":2")) == "invalid_frame", "invalid command");
        Assert(errorFor("{\"v\":1,\"type\":\"ack\",\"id\":\"1\",\"ok\":true}") == "invalid_frame", "wrong direction");
        Assert(h.Control.Calls.Count == 0, "no invalid frame reaches playback");

        h.Clear();
        Assert(h.Frame("{\"v\":1,\"type\":\"error\",\"code\":\"invalid_frame\"}").Close == false && h.Sent.Count == 0, "a peer error is logged, never answered (no ping-pong)");

        h.Clear();
        var version = h.Frame("{\"v\":2,\"type\":\"command\",\"id\":\"1\",\"name\":\"play\"}");
        Assert(version.Close && version.CloseCode == 4002 && h.Sent.Count == 0, "unsupported version closes with 4002");

        h.Clear();
        var large = h.Frame(new string(' ', HaContract.MaxFrameBytes + 1));
        Assert(large.Close && large.CloseCode == 4004 && (string)h.Sent.Single()["code"] == "too_large", "oversized frame gets too_large then 4004");

        h.Clear();
        h.Session.OnBinaryFrameAsync().GetAwaiter().GetResult();
        Assert((string)h.Sent.Single()["code"] == "invalid_frame", "binary frames are invalid");
    }

    private static void IdleCoversInFlightCommandsForBackgroundWakes()
    {
        var h = new Harness();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Control.Behaviour = _ => release.Task;
        Assert(h.Session.Idle.IsCompleted, "idle with nothing to do");
        var frame = h.Session.OnFrameAsync(Command("w", "pause"));
        Assert(!h.Session.Idle.IsCompleted, "a running command keeps the wake alive");
        release.SetResult(true);
        frame.GetAwaiter().GetResult();
        Assert(h.Session.Idle.Wait(1000), "idle again once the ack and snapshot are sent");
        Assert(h.Types().SequenceEqual(new[] { "ack", "state" }), "the reply was sent before idle");
    }

    private static void ClosedSessionSendsNothing()
    {
        var h = new Harness();
        h.Session.Close();
        h.Session.StartAsync().GetAwaiter().GetResult();
        h.Session.OnStateChangedAsync().GetAwaiter().GetResult();
        h.Frame(Command("1", "pause"));
        Assert(h.Sent.Count == 0 && h.Control.Calls.Count == 0, "a closed session neither sends nor executes");
    }

    private static void StateReadFailureDoesNotBreakTheSession()
    {
        var h = new Harness();
        var options = new HaLinkSessionOptions
        {
            DeviceId = "3f2b8c1e-6a4d-4e0b-9d57-1c2a7b9e4f10", DeviceName = "x", AppVersion = "1.0.0.0",
            Send = text => { h.Sent.Add(JObject.Parse(text)); return Task.CompletedTask; },
            CurrentState = () => { throw new InvalidOperationException("no state"); },
            Control = h.Control, Log = m => h.Logs.Add(m)
        };
        var session = new HaLinkSession(options);
        session.StartAsync().GetAwaiter().GetResult();
        Assert(h.Types().SequenceEqual(new[] { "hello" }), "hello still goes out");
        session.OnFrameAsync(Command("1", "pause")).GetAwaiter().GetResult();
        Assert(h.Types().SequenceEqual(new[] { "hello", "ack" }) && h.Logs.Any(), "the command is acked and the failure logged");
        AssertThrows<ArgumentException>(() => new HaLinkSession(new HaLinkSessionOptions()), "required options");
    }

    // -- policy ---------------------------------------------------------------------------------

    private static void CloseCodesMapToRetryDecisions()
    {
        var expected = new Dictionary<int, Tuple<HaLinkFailure, bool, HaLinkStatus>>
        {
            { 4001, Tuple.Create(HaLinkFailure.Replaced, true, HaLinkStatus.Waiting) },
            { 4002, Tuple.Create(HaLinkFailure.UnsupportedVersion, false, HaLinkStatus.Rejected) },
            { 4003, Tuple.Create(HaLinkFailure.HelloRequired, false, HaLinkStatus.Rejected) },
            { 4004, Tuple.Create(HaLinkFailure.TooLarge, true, HaLinkStatus.Waiting) },
            { 4005, Tuple.Create(HaLinkFailure.InvalidIdentity, false, HaLinkStatus.Rejected) },
            { 1000, Tuple.Create(HaLinkFailure.ClosedByServer, true, HaLinkStatus.Waiting) },
            { 1001, Tuple.Create(HaLinkFailure.ClosedByServer, true, HaLinkStatus.Waiting) },
            { 1009, Tuple.Create(HaLinkFailure.ClosedByServer, true, HaLinkStatus.Waiting) }
        };
        foreach (var pair in expected)
        {
            var failure = HaLinkPolicy.ClassifyClose(pair.Key);
            Assert(failure == pair.Value.Item1, pair.Key + " classified as " + failure);
            Assert(HaLinkPolicy.ShouldRetry(failure) == pair.Value.Item2, pair.Key + " retry decision");
            Assert(HaLinkPolicy.StatusAfter(failure) == pair.Value.Item3, pair.Key + " status");
        }
        Assert(!HaLinkPolicy.ShouldRetry(HaLinkFailure.Unauthorized) && HaLinkPolicy.StatusAfter(HaLinkFailure.Unauthorized) == HaLinkStatus.AuthFailed, "a refused secret stops retries");
        Assert(HaLinkPolicy.ShouldRetry(HaLinkFailure.Network), "network failures retry");
    }

    private static void BackoffGrowsCapsAndJitters()
    {
        Assert(HaLinkPolicy.RetryDelay(0, 0.5) == TimeSpan.FromSeconds(1), "first retry after 1 s");
        Assert(HaLinkPolicy.RetryDelay(1, 0.5) == TimeSpan.FromSeconds(2) && HaLinkPolicy.RetryDelay(3, 0.5) == TimeSpan.FromSeconds(8), "doubles");
        Assert(HaLinkPolicy.RetryDelay(30, 0.5) == TimeSpan.FromMinutes(5) && HaLinkPolicy.RetryDelay(int.MaxValue, 0.5) == TimeSpan.FromMinutes(5), "caps at 5 min without overflow");
        Assert(HaLinkPolicy.RetryDelay(0, 0) == TimeSpan.FromSeconds(0.8) && HaLinkPolicy.RetryDelay(0, 0.999999) < TimeSpan.FromSeconds(1.21), "+/-20 % jitter");
        Assert(HaLinkPolicy.RetryDelay(-3, 0.5) == TimeSpan.FromSeconds(1) && HaLinkPolicy.RetryDelay(0, double.NaN) == TimeSpan.FromSeconds(1) && HaLinkPolicy.RetryDelay(0, 7) == TimeSpan.FromSeconds(1), "bad inputs are tolerated");
        Assert(HaLinkPolicy.RetryDelay(0, 0.5, HaLinkFailure.Replaced) == TimeSpan.FromMinutes(1), "a replaced link waits at least a minute");
        Assert(HaLinkPolicy.RetryDelay(30, 0.999999) <= TimeSpan.FromMinutes(6), "jitter cannot push far past the cap");
    }

    private static void UrlsAreNormalizedToTheLinkEndpoint()
    {
        var cases = new Dictionary<string, string>
        {
            { "ws://homeassistant.local:8123/api/lumia_hifi/v1/link", "ws://homeassistant.local:8123/api/lumia_hifi/v1/link" },
            { "  http://homeassistant.local:8123  ", "ws://homeassistant.local:8123/api/lumia_hifi/v1/link" },
            { "http://homeassistant.local:8123/", "ws://homeassistant.local:8123/api/lumia_hifi/v1/link" },
            { "https://ha.example:8443", "wss://ha.example:8443/api/lumia_hifi/v1/link" },
            { "WSS://HA.example/api/lumia_hifi/v1/link/", "wss://ha.example/api/lumia_hifi/v1/link" },
            { "homeassistant.local:8123", "ws://homeassistant.local:8123/api/lumia_hifi/v1/link" },
            { "ws://[fd00::1]:8123", "ws://[fd00::1]:8123/api/lumia_hifi/v1/link" }
        };
        foreach (var pair in cases)
        {
            string normalized, error;
            Assert(HaLinkPolicy.TryNormalizeUrl(pair.Key, out normalized, out error) && normalized == pair.Value,
                "'" + pair.Key + "' -> '" + normalized + "' (" + error + ")");
        }
        foreach (var bad in new[] { null, "", "   ", "ftp://ha:21", "ws://user:pass@ha:8123", "ws://ha:8123/?token=x", "ws://ha:8123/#x", "ws://ha:8123/api/other", "ws://", "not a url at all" })
        {
            string normalized, error;
            Assert(!HaLinkPolicy.TryNormalizeUrl(bad, out normalized, out error) && normalized == null && !string.IsNullOrEmpty(error), "must be rejected: " + bad);
        }
    }

    private static void SecretsAreValidatedWithoutEchoingThem()
    {
        const string secret = "kA9-zQ_3xY7wP2mN8vB4cR6tL1sD5fG0hJ_qWeRtYuI";
        string normalized, error;
        Assert(HaLinkPolicy.TryNormalizeSecret("  " + secret + "\n", out normalized, out error) && normalized == secret, "pasted secrets are trimmed");
        foreach (var bad in new[] { null, "", "short", secret.Substring(0, 10) + " " + secret.Substring(10), secret + "é", new string('a', 201) })
        {
            Assert(!HaLinkPolicy.TryNormalizeSecret(bad, out normalized, out error) && normalized == null, "must be rejected: " + (bad == null ? "null" : bad.Length + " chars"));
            Assert(bad == null || bad.Length < 5 || !error.Contains(bad), "the error never echoes the secret");
        }
    }

    private static void EveryStatusHasAUserFacingDescription()
    {
        foreach (HaLinkStatus status in Enum.GetValues(typeof(HaLinkStatus)))
            foreach (HaLinkFailure failure in Enum.GetValues(typeof(HaLinkFailure)))
            {
                var text = HaLinkPolicy.Describe(status, failure);
                Assert(!string.IsNullOrWhiteSpace(text) && text.EndsWith(".", StringComparison.Ordinal), status + "/" + failure + " needs a sentence");
            }
    }

    // -- static audit of the WinRT wiring (cannot run off-device) ---------------------------------

    private static void AppWiringIsOptInAndCompleteAcrossThemes()
    {
        var root = FindRepositoryRoot();
        Func<string, string> read = relative => System.IO.File.ReadAllText(System.IO.Path.Combine(root, "LibreSpotUWP", relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));

        var project = read("LibreSpotUWP.csproj");
        foreach (var file in new[] { "Services\\HomeAssistantLinkService.cs", "Helpers\\HomeAssistantSettingsController.cs", "Services\\HomeAssistant\\HaLinkSession.cs", "Services\\HomeAssistant\\HaLinkPolicy.cs", "Services\\HomeAssistant\\HaCommandDispatcher.cs" })
            Assert(project.Contains("<Compile Include=\"" + file + "\" />"), "the app project must compile " + file);

        var settings = read("Helpers/UserSettings.cs");
        Assert(settings.Contains("GetBoolSetting(HomeAssistantLinkEnabledKey, defaultValue: false)"), "the link is off by default");

        var service = read("Services/HomeAssistantLinkService.cs");
        var apply = service.Substring(service.IndexOf("public async Task ApplySettingsAsync()", StringComparison.Ordinal));
        Assert(apply.IndexOf("UserSettings.HomeAssistantLinkEnabled", StringComparison.Ordinal) < apply.IndexOf("Task.Run(", StringComparison.Ordinal), "nothing starts before the opt-in flag is checked");
        Assert(service.IndexOf("new MessageWebSocket", StringComparison.Ordinal) > service.IndexOf("private sealed class Connection", StringComparison.Ordinal), "sockets exist only inside a started connection");
        Assert(service.Contains("SetRequestHeader(\"Authorization\", \"Bearer \" + _secret)"), "the secret travels only in the Authorization header");
        Assert(!System.Text.RegularExpressions.Regex.IsMatch(service, "Log[A-Za-z.]*\\([^;]*_secret"), "the secret is never logged");
        Assert(!service.Contains("_endpoint}") && !service.Contains("{url}") && !service.Contains("{_endpoint"), "the full address is never logged");
        Assert(service.Contains("LibreSpotUWP.HaLink.") && service.Contains("ControlChannelTriggerResourceType.RequestHardwareSlot"), "standby wake-ups use their own control channel");
        Assert(service.Contains("ha-link-standby-slot"), "the granted standby slot is recorded (WP0 evidence)");

        var app = read("App.xaml.cs");
        var activated = app.Substring(app.IndexOf("protected override async void OnBackgroundActivated", StringComparison.Ordinal));
        Assert(activated.IndexOf("HomeAssistantLinkService.OwnsTask", StringComparison.Ordinal) < activated.IndexOf("WinRtDealerTransport.OwnsTask", StringComparison.Ordinal), "Home Assistant wakes are routed before the Dealer early return");
        Assert(app.Contains("await StartHomeAssistantLinkAsync();") && app.Contains("HomeAssistantLink?.RetrySoon();"), "the link starts with the services and retries on resume");

        foreach (var theme in new[] { "Win10_1507", "Win10_1709", "Win11" })
        {
            var xaml = read("Views/" + theme + "/SettingsPage_" + theme + ".xaml");
            var code = read("Views/" + theme + "/SettingsPage_" + theme + ".xaml.cs");
            foreach (var control in new[] { "HomeAssistantLinkToggle", "HomeAssistantUrlTextBox", "HomeAssistantSecretBox", "HomeAssistantSaveButton", "HomeAssistantStatusText" })
            {
                Assert(xaml.Contains("x:Name=\"" + control + "\""), theme + " Settings must contain " + control);
                Assert(code.Contains(control), theme + " Settings must wire " + control);
            }
            Assert(code.Contains("_homeAssistant.Load();") && code.Contains("_homeAssistant?.Unload()"), theme + " Settings loads and releases the section");
        }

        var manifest = read("Package.appxmanifest");
        Assert(!manifest.Contains("Version=\"1.0.34.0\""), "a changed package needs a new version (1.0.34.0 is installed)");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "LibreSpotUWP", "LibreSpotUWP.csproj")))
            directory = directory.Parent;
        if (directory == null) throw new InvalidOperationException("repository root not found");
        return directory.FullName;
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
