# Goal 5: stability and appliance operation

Date: 2026-10-18. Status: reopened for loss of remote control after the controller phone locks. Earlier 1.0.15.0 acceptance and closure are retained as history, superseded by the report below.

## Reopened: controller lock loses remote control (2026-10-18)

Priority: required for the next delivery, ahead of visual polish. User report on the current 1.0.15.0 baseline: transfer from the controller phone to the Lumia succeeds and music plays; locking the controller phone loses the control connection. The Lumia continues playing, but Pause on the controller no longer affects it. The receiver appears to operate independently. This is a new failed functional acceptance scenario, not evidence that autonomous audio playback is the desired result.

The phone being locked here is the Spotify controller, not necessarily the Lumia. Lumia lock state, controller OS/Spotify version, owner versus guest session, time to failure, selected target after unlocking, and recovery behavior have not yet been established. Do not attribute this to Lumia suspension, mDNS, controller power management or a confirmed native-session bug without a correlated trace. Continuous audio alone does not establish a healthy Connect control channel.

Required behavior: household members control the receiver wirelessly from their own Spotify accounts, including after controller lock/background/unlock. Pause/Play, Next/Previous, seek, volume and supported Spotify queue edits must continue to affect the Lumia, or recover through the controller alone after a real connection interruption. Do not require the user to touch the Lumia, build its queue locally, reauthenticate on it or keep the controller screen on. Do not stop valid audio merely because a controller becomes inactive; distinguish an intentionally disconnected controller from a receiver control-channel failure.

Investigation and next implementation slice (R0):

1. Reproduce with the Lumia unlocked first to isolate controller lock, then repeat with the Lumia locked. Record relative timestamps for transfer, controller lock, first failed command and unlock/reopen. Test lock-screen controls and controls from the reopened Spotify app separately. Record whether Spotify still selects the Lumia or silently targets the controller itself, and whether reselecting the Lumia restores control without receiver interaction.
2. Repeat owner and second Premium account separately. Preserve playback ownership throughout; an owner-authenticated UI or library must not replace the guest's Connect session. Test another same-account controller to distinguish a single-controller symptom from receiver-wide control loss.
3. Correlate redacted device session-generation changes, session disconnect/reconnect, native Spirc task lifetime, activation/deactivation, incoming transport commands, playback-request/audio generations, producer/renderer progress and outgoing state notifications. Add only missing bounded events. Do not log raw protocol payloads, account identifiers or credentials. Record commands at reception and application to locate where control stops.
4. Inspect `../librespot-upstream/src/runner.rs` session/Spirc creation and task completion handling, account credential changes, receiver activation and state publication. A usable audio producer must not hide a dead control task; verify whether control processing exits while decoded/buffered audio continues. Preserve the fresh-Session account-switch invariant.
5. Inspect `MediaService.OnSessionStateChanged`, producer recovery, `OnEndOfTrack`, online queue recovery and `ContinueDownloadedPlaybackAfterOnlineFailureAsync`. Determine whether a recovery or fallback replaces the active session, activates an unintended target or starts local playback detached from the controller queue. Follow native command/state propagation before changing managed timers or UI.
6. Choose the smallest evidence-backed correction. If receiver control transport is lost, recover the correct session/control task with bounded retries and current intent/account/session checks; never fall back to owner credentials for guest playback. If the receiver receives commands but ignores them, correct command/generation/state reconciliation instead. If only the controller is failing, document verified controller behavior and supported controller-only recovery; do not claim an app-side fix without evidence.
7. Add behavioral regressions at the actual faulty boundary: control-task termination while audio advances, stale completion after account/target change, control recovery during Pause/transfer and queue ownership as applicable. A static source audit or audio-continuity test alone cannot close this issue. Rebuild Rust only if the verified correction changes native source; otherwise reuse the known-good DLL.

Acceptance: owner and guest each complete repeated controller lock/unlock cycles and 5/15/60-minute lock/background intervals. After each interval, commands issued from supported lock-screen controls and from reopened Spotify affect the selected Lumia and converge to the same playback state/position. Queue edits and track changes from the controller remain authoritative where Spotify supports them. Repeat with Lumia screen on and off; record exceptions separately. Intentional Pause stays paused and recovery never switches accounts, resurrects old tracks or requires touching the receiver. Any automatic recovery timeout must be bounded and measured; do not hide a broken channel behind a Playing badge. Record app/native revisions and redacted evidence before declaring R0 fixed.

Scheduling: R0 is the first functional slice in [HIFI_UI_PLAN.md](HIFI_UI_PLAN.md). Layout planning may proceed independently, but the next combined candidate cannot be accepted on visual appearance or uninterrupted audio while this control regression remains unresolved. No code fix or new APPX is claimed by this documentation update.

### R0 source investigation checkpoint

Current checkout reviewed after the user asked whether the cause had actually been investigated. Confirmed source behavior, rather than a confirmed reproduction of the lock trigger:

- Audio and control are separate paths: `../librespot-upstream/src/runner.rs: Runner::run` creates a Player and a separate Spirc task; `connect/src/spirc.rs: SpircTask::run` receives remote commands through Dealer subscriptions. Continued PCM does not prove the Dealer WebSocket or command loop is usable. There is no receiver-side event that directly knows the controller phone's screen was locked.
- Concrete terminal-error gap: `core/src/dealer/mod.rs: run` retries errors from `connect`, but uses `?` on the URL/token/endpoint-resolution result from `get_url`. A transient error at that point terminates the background Dealer task instead of following its connection retry branch. `core/src/dealer/manager.rs: get_url` can fail during endpoint resolution or token acquisition. `Dealer` retains the task handle and shared subscription/request senders; the inspected API has no task-completion health signal consumed by Spirc. Consequently Dealer-task exit need not close those streams or invalidate the session, so Spirc may remain alive without receiving remote commands.
- The runner does already reconnect when the entire Spirc future completes. Therefore “no native reconnect exists” would be incorrect. That completion-based recovery does not cover a terminated internal Dealer task while Spirc remains running.
- Managed recovery is also incomplete for this case: `LibrespotRingBufferPlayer.CheckProducerHealth` samples PCM write/read progress; `ProducerHealthMonitor.Evaluate` returns without recovery while buffered bytes exceed the low-water threshold, and healthy PCM with a connected session is not treated as failure. No independent control-channel health input is present. A healthy audio producer can therefore mask control failure indefinitely if native disconnect is not reported.
- Native code already has WebSocket ping/pong checks, a five-second Connect-state publication heartbeat and a two-second timeout around `notify` publication. Do not propose “add a heartbeat” as if none exists. These are not proof of command reception: HTTP state publication and audio can succeed while Dealer command reception fails. Other awaited operations in the serial Spirc loop, including initial/reconnected device registration and volume-state notification, also require a timeout/liveness audit before concluding that loop execution is healthy.

Most defensible diagnosis so far: the source contains a reproducible-in-principle control-task failure path plus a control-health detection gap consistent with autonomous audio and lost commands. There is no correlated device trace proving that controller lock caused a Dealer URL/token error, a blocked Spirc operation, or a controller-side target change in the reported run. Do not present the terminal-error gap as that run's proven root cause, or claim that all transient Dealer disconnects fail (ordinary connect failures already retry).

Refined implementation order: write a native regression with injected `get_url` failure after a simulated WebSocket loss, verify that transient failures retry/cancel cleanly and that terminal task completion propagates to session/control health; test a continuing PCM producer concurrently. Then audit/block-test serial Spirc awaits and preserve same-account/guest credentials during control recovery. Validate controller-selected target and incoming-command timestamps on the Lumia to establish the actual lock trigger. Pin the candidate's native revision/hash when implementing; this source inspection alone does not establish which branch executed inside the installed DLL. No runtime change or newly executed native test is claimed here.

## Closure and handoff (2026-10-18)

The user installed the candidate and reported that it works, then requested closure of this workstream and a move to Goal 2. Record this as user-reported functional acceptance of the 1.0.15.0 delivery, not completion of every physical qualification scenario. The package, source revision `6e5ae32`, artifact hashes and rollback details are recorded below. The working baseline for the visual milestone is 1.0.15.0; retain 1.0.14.0 as the existing rollback package.

Delivered: managed lifecycle/recovery hardening, account-safe recovery, snapshot ordering, renderer monitoring, diagnostics, lock-screen command ownership and pre-roll continuation checks; portable tests and ARM packaging passed. This closure was subsequently superseded by the controller-lock report above; R0 is now scheduled for the next delivery.

Residual qualification is explicitly deferred: locked idle discovery/socket-broker feasibility, the complete lock-cycle and network/account/restart matrix, eight-hour playback, overnight idle, and measured power/thermal/resource budgets. The brief user confirmation does not establish which of these passed. Reopen the corresponding section if a symptom recurs or these operating guarantees become an implementation priority. UI work must preserve the accepted playback path and include lock-screen regression checks.

Next workstream: [HIFI_UI_PLAN.md](HIFI_UI_PLAN.md), covering Goal 2 and optional persistent Now Playing presentation. Goal 5's full qualification definition remains below as a reference rather than a claim of completed testing.

## Scope and baseline

Cover all of Goal 5 in `../../prompt.md`: network recovery, Spotify session recovery, background audio, authentication preservation, restart behavior, CPU/memory efficiency, battery/thermal behavior, actionable errors, safe diagnostics, and optional appliance operation. Audio-format optimization, UI redesign, and Home Assistant remain separate goals.

Baseline: Lumia ARM32 package 1.0.14.0 (built from the stability implementation). Two-account discovery, transfer, and playback were physically confirmed; see [DISCOVERY_AUDIT.md](DISCOVERY_AUDIT.md). This does not establish long-running stability. The user reports that the first screen lock pauses playback, after which pressing Play from the lock screen makes later locks work normally. The user also reports that the device is not discoverable while locked, even when LibreSpotUWP was open before locking. No correlated lock-time device log was collected during this planning session; both causes remain hypotheses until the device trace is captured.

Preserve the known-good APPX and data before each installation. Preserve package identity, authentication boundaries, and the native fresh-Session account-switch fix. Use small Conventional Commits and independently testable milestones. Do not deploy anything to the homelab for this work.

## Source findings and remaining uncertainty

Paths below are relative to the app repository unless prefixed with `../librespot-upstream/`. Method names are navigation anchors; line numbers may change.

| Area | Confirmed source evidence | Implication / investigation |
| --- | --- | --- |
| Background capability | `LibreSpotUWP/Package.appxmanifest` declares `backgroundMediaPlayback` and LAN capabilities | Adding the same capability again cannot fix the issue. Validate actual execution and network retention on Mobile. |
| Two audio paths | `Services/MediaService.cs: InitializeCoreAsync` creates a MediaPlayer playing `CreateSilentMediaSource`; audible PCM uses AudioGraph or a native backend | Trace both players and SMTC together. Do not assume a silent player guarantees native networking or renderer survival. |
| Platform media source | `CreateSilentMediaSource` supplies local silent samples, declares 24 hours, and does not report the remote stream's buffered range | Test network reservation and long-duration behavior. Investigate supported integration before changing buffering semantics; do not fabricate buffering information. |
| Extended execution | `Helpers/UwpBackgroundExecutionManager.cs` requests `ExtendedExecutionReason.Unspecified`; `Session_Revoked` only logs and sets a flag | This is revocable and is not a Mobile screen-off guarantee. Access requests/session disposal are not serialized; exception cleanup can leave `_session` assigned. Add explicit ownership and state tests. |
| Lifecycle | `App.xaml.cs: OnSuspending/OnResuming`; `MediaService: PrepareForSuspendingAsync/ResumeAfterSuspendingAsync` preserve active local audio through `BackgroundPlaybackPolicy` | Existing policy is useful. Setting `_suspended = 0` does not prevent OS suspension. Resume may return early after active playback and does not perform an end-to-end health reconciliation. |
| First lock pause | `MediaService` starts the silent `MediaPlayer` and requests extended execution from asynchronous session/discovery callbacks; `OnSuspending` can call `_mediaPlayer.Play()` only after the OS has begun the background transition | A first-lock race can leave the SMTC/background-media session unarmed. The observed recovery after pressing lock-screen Play is consistent with the first SMTC command completing that activation. Capture lifecycle, `MediaPlayer` state, SMTC status, and extended-execution result before changing transport behavior. |
| Locked discoverability | Discovery keep-alive uses `ExtendedExecutionSession` and the native Spotify Connect listener; `Package.appxmanifest` has `backgroundMediaPlayback`, but no socket-broker/background-task handoff | Background audio capability does not by itself guarantee an idle mDNS listener in Connected Standby. Determine whether active playback keeps the process/listener alive; if not, evaluate a supported socket/background-task design or explicitly document that idle discovery requires the screen on. Never claim that a longer timeout or a second listener fixes this without device evidence. |
| Suspend work | `OnSuspending` awaits discovery keep-alive before media preparation and live tiles, under one try block | A denied/failed/slow auxiliary operation can delay or skip essential preparation. Use the actual suspension deadline and independent bounded cleanup. |
| Timers | `MediaService` position, volume debounce, and Connect polling use DispatcherTimer; AudioGraph transition/producer monitoring uses `System.Threading.Timer` | Identify which timer drives correctness. Transport recovery and queue progress must not require UI ticks; thread timers also cannot run after OS suspension. |
| Producer watchdog | `LibrespotRingBufferPlayer.CheckProducerHealth`, `ProducerHealthMonitor`, `RecoverProducerAsync` already detect stalls and limit attempts to three | Reuse these mechanisms. A healthy/full producer with a stopped renderer is a different fault and can escape the low-buffer producer check. |
| Measurement limits | `ProducerHealthMonitor.ObserveProducer` infers successful source reads from PCM progress | Label this as inferred; it is not an independent native network-read measurement. |
| Recovery credentials | `RecoverProducerAsync` and `HandleNetworkStatusChangedCoreAsync` fetch `_auth` and `_playbackAuth` connection material | Risk: reconnecting a guest's active playback with the owner's saved credentials. Must validate session ownership before every retry. |
| Transient accounts | `OnPlaybackCredentialsAvailable` intentionally does not persist another controller's credentials | Correct isolation behavior must survive recovery and restart. Guest recovery requires in-memory native/session material or an explicit request to reconnect. |
| Native session | `../librespot-upstream/src/runner.rs` creates a fresh Session for credential attempts after the cross-account fix | Preserve this invariant; map native reconnect versus managed instance recreation before changing either. |
| Concurrent recovery | Producer recovery, network recovery, queue continuation, and offline substitution have different gates/timers | Trace lock ordering, cancellation, and precedence. Verify a late recovery cannot override Pause, Stop, account switch, or transfer away. |
| Audio faults | `LibrespotRingBufferPlayer` uses `AudioRenderCategory.Media` and logs callback gaps, buffer fill, silence, graph identity | Check graph unrecoverable/device errors and native backend equivalents. Measure consumption as well as production. |
| Persistence | `RestorePlaybackSnapshotAsync`, `PersistPlaybackSnapshot`, and resume preferences already exist | Audit atomicity, account ownership, stale commands, position freshness, and termination without Suspending. |
| Diagnostics | `Services/LogService.cs` has bounded queues, batching, rate limiting, and redaction | Extend existing diagnostics rather than add a second logger. Verify crash retention, bounded shutdown flush, and secret coverage. |
| Tests | `tests/LibreSpotUWP.Playback.Tests` covers background policy, stalls, gates, transitions, queues and stale generations | Pure tests cannot establish actual screen-off/network behavior. The current host reports no .NET SDK via `dotnet --list-sdks`; restore test execution before implementation acceptance. |

## Platform research

Primary Microsoft references checked on 2026-10-18:

- [Background audio requirements](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/background-audio): background playback requires capability, appropriate SMTC integration, and compliance with background memory limits. AudioGraph needs explicit SMTC integration. Streaming through MediaStreamSource has specific network/buffering semantics.
- [Extended execution](https://learn.microsoft.com/en-us/windows/uwp/launch-resume/run-minimized-with-extended-execution): execution can be revoked; only one session request can be active; test without a debugger. Specifically on Windows 10 Mobile, Unspecified execution lasts while the screen is on and stops at Connected Standby. Desktop power/time behavior must not be applied to the Lumia.
- [Microsoft background playback sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-universal-samples/backgroundmediaplayback/): evaluate the supported single-process media model and background networking requirements against the installed Mobile build.
- [Network communications in the background](https://learn.microsoft.com/en-us/windows/uwp/networking/network-communications-in-the-background): Connected Standby may require a background task with `IsNetworkRequested`, or socket-broker ownership via `SocketActivityTrigger`/`ControlChannelTrigger`; this is distinct from merely requesting extended execution.

These facts establish platform constraints, not the cause of this particular interruption. Prefer supported background media execution. Do not promise an always-reachable idle receiver when the OS suspends the app. Do not add fake audio, unrestricted capabilities, or a replacement architecture without evidence that the existing media path cannot meet acceptance.

## Intended runtime contract

Track these dimensions independently: foreground/background/suspending/resumed; playback requested/paused/stopped; local/remote target; current account/session generation; network availability; producer progress; renderer progress; background permission state. A single Boolean such as `_suspended` or `IsConnected` cannot describe overall health.

1. User Pause/Stop/transfer away cancels automatic recovery. New track/account/session invalidates old work.
2. A guest session never falls back silently to owner credentials. UI account and active playback account remain separate.
3. Network loss waits for usable connectivity; retries are bounded and serialized, with cancellation and backoff. A disconnected session is not proof of missing Wi-Fi, and a Windows internet flag is not proof Spotify is reachable.
4. A renderer fault restarts the renderer only when justified; a producer/session fault recovers the producer/session. Metadata failures do not stop valid audio.
5. Supported screen-off playback must continue without UI dispatch, while idle discovery limitations are reported accurately.
6. Restart preserves legitimate owner authentication and preferences. Restoring a snapshot does not reactivate a guest using owner credentials or start unexpected audio.

## Delivery milestones

### S0 — Establish a measurable baseline (small; first implementation session)

Objective: reproduce failures and make later changes attributable.

Changes: add lightweight correlated events for app background transitions, extended execution request/result/revocation, MediaPlayer state/failure, SMTC state, audio backend, graph/session/track generations, monotonic timestamps, producer/consumer progress, and recovery reason/attempt/outcome. Record process launch identity to distinguish suspension from termination. Sample memory usage/limit and existing audio-health counters at low frequency. Add a bounded diagnostic export using existing redaction and logging.

Dependencies: inspect current source/history and build preflight; enable the existing net8 test runner with an appropriate SDK if still absent. Do not retarget the UWP app. Record OS build, current power/background settings, active backend and connection conditions through device testing.

Tests: logger redaction fixtures; bounded event-rate/queue behavior; baseline physical matrix below. Collect controller and device timestamps without storing credentials or private account IDs.

Acceptance: each reproduced interruption can be classified as process stop, background revocation, producer stall, renderer stall, explicit pause/transfer, or unresolved with a precise missing signal. Healthy runs have low-rate telemetry.

Risks: logging changes timing or exposes secrets. Rollback: disable added telemetry or restore the baseline package. Do not log raw Zeroconf requests, credentials, tokens, audio keys, or connection material.

### S1 — Correct screen-off and lifecycle behavior (medium; highest user priority)

Objective: continuous audible playback under the Lumia's supported background media model.

Changes: audit SMTC ownership/command handling, silent MediaPlayer and audible renderer state together; arm the MediaPlayer/SMTC background session when local playback becomes active, not for the first time inside `OnSuspending`; record whether the first lock occurred before extended execution was allowed. Add EnteredBackground/LeavingBackground handling with low-cost state recording and memory release. Serialize extended-execution requests and make revoke/dispose/error paths idempotent, with unsubscribe/cleanup on all paths. Reconcile current health on foreground return even if app-level `_suspended` was never set. Keep suspension work deadline-bounded; media preparation must not depend on live tile or keep-alive success.

Decision gate: use S0 evidence to choose the smallest supported correction to media/SMTC/network lifetime. First validate the existing AudioGraph path against Microsoft's requirements. Only if device evidence proves it necessary, prototype feeding actual decoded PCM through a platform media source as an alternative to the silent-player arrangement. Treat that as a separate renderer change with format, seek, buffering and continuity regression tests; avoid an immediate rewrite or legacy dual-process migration.

Tests: lifecycle policy transitions and permission denied/revoked/racing requests; physical lock/unlock immediately after starting playback and again after a confirmed keep-alive; Home, track boundary, SMTC buttons, owner/guest, battery/charger, debugger detached. Verify the first lock does not issue an unintended pause and that lock-screen Play/Pause remains authoritative.

Acceptance: 30 lock/unlock cycles without unintended pause, a two-hour screen-off playlist with track changes, working lock-screen controls, and no recovery triggered by intentional pause. Idle discoverability is tested separately and any OS-imposed limitation is documented.

Dependencies: S0. Risks: double transport commands, additional audio work, Mobile policy differences. Rollback: previous package; isolate any alternative renderer behind a setting until accepted.

### S2 — Account-safe, coordinated recovery (medium)

Objective: restore interrupted sessions without account takeover, duplicate playback or retry storms.

Changes: map native/managed reconnect ownership, including the native fresh-Session invariant. Introduce a minimal recovery context carrying account/session/track/intent generations and cancellation. Prefer one arbitration point over additional parallel watchdogs. Select authorization by active playback ownership: use transient material only in memory for guests where supported; otherwise stop safely and request a new Connect transfer. Never serialize guest material. Recheck current intent before/after every awaited reconnect/load/seek. Use monotonic timeout measurements. Audit lock order and lifetime of `_playbackGate`, `_ringPlayerGate`, `_networkStatusGate`, and RecoveryOperationGate.

Tests: owner and guest reconnect, guest material unavailable, owner token expired, duplicate disconnect events, account switch during reconnect, stale callback, Pause/Stop/remote transfer during backoff/load, dispose during recovery. Verify retries cannot use old track/account state.

Acceptance: one active recovery per playback intent; no silent owner fallback; no autoplay after Pause/Stop; bounded attempts and a clear recoverable error; guest-to-owner-to-guest transfers still play.

Dependencies: S0, coordinate with S1. Risks: auth regression or deadlock. Rollback: revert the recovery change while retaining baseline account-switch behavior.

### S3 — Network loss and discovery recovery (medium)

Objective: recover across Wi-Fi/internet changes while advertising only usable endpoints.

Changes: separate LAN reachability, internet indication, Spotify transport state and discovery readiness. Debounce/coalesce network events; defer retries while offline and re-arm on genuine return. Audit the early return after clearing connectivity backoff to ensure subsequent handling is triggered. Re-evaluate usable interface/address on Wi-Fi changes and recreate advertisement/listener only when needed. Never hardcode IP or the dynamic port. Prevent repeated errors from exhausting recovery while the network is unavailable. Trace the locked-device case separately: active playback discoverability and idle receiver discoverability are different contracts. Inspect the native listener's socket ownership and determine whether it can be transferred to a supported socket-broker/background-task path with `IsNetworkRequested`; do not assume `ExtendedExecutionReason.Unspecified` survives Connected Standby on Windows 10 Mobile.

Tests: Wi-Fi off/on, internet unavailable while LAN remains up, DNS failure/restoration, repeated network notifications, address change, Spotify endpoint unavailable, connected-but-no-internet. Verify native discovery implementation before selecting rebind/recreate behavior.

Acceptance: after connectivity is confirmed restored, a healthy active session resumes or exposes a specific retry action within a proposed 30-second target; no wrong-account recovery. A current avahi record, TCP connection and getInfo response match the actual endpoint while the screen is on. For screen-off operation, either maintain a verified discovery record/TCP/getInfo path during active playback and idle periods, or expose/document the Windows Mobile limitation and require the screen to be on for a new Connect transfer. Idle/screen-off cases record platform limits separately.

Dependencies: S2 and usable network evidence. Risks: disconnecting good playback or stale advertisements. Rollback: restore prior network handling and discovery preference.

### S4 — Renderer, queue and command robustness (medium)

Objective: audible output and queue continuation remain correct independently of UI activity.

Changes: distinguish producer stalls from stopped consumer/graph and output-device errors. Reuse audio-health counters; add bounded renderer recovery only for confirmed faults. Audit track-end, preload, queue continuation, local cache substitution and Connect event ordering. Keep authoritative command and timing paths independent of DispatcherTimer. Inventory native backend support and either add equivalent health reporting or explicitly scope recovery to the supported backend.

Tests: healthy producer/stalled consumer, underrun then refill, delayed preload, next/previous/seek bursts, headphone removal/reinsert, default device change, end of queue, cached fallback, UI navigation while playing and guest-controlled track changes.

Acceptance: no indefinite Loading, no duplicate next/seek, one live renderer after recovery, bounded silence recovery, no stale track resurrection, and no transport work waiting for UI ticks.

Dependencies: S0/S2. Risks: clicks, duplicate graphs or queue corruption. Rollback: previous backend/renderer behavior and package.

### S5 — Restart, persistence and error recovery (small to medium)

Objective: predictable startup after graceful close, OS termination and failed authentication.

Changes: audit snapshot write atomicity/schema/ownership, bounded asynchronous persistence and stale-state rejection. Persist durable preferences/owner authorization through established secure storage only. Define startup behavior explicitly: restore presentation; resume only under the existing user preference and valid ownership. Do not require Suspending to run before preserving important state. Distinguish expired token, invalid authorization, no network and renderer errors in user-facing actions.

Tests: terminate during write/load, malformed/old snapshot, expired owner authorization, restart after guest playback, repeated starts, offline launch, storage failure, resume preference off/on.

Acceptance: valid owner login/preferences survive; bad snapshots do not crash startup; no guest credential persistence or wrong-account autoplay; a user can retry without reinstalling.

Dependencies: S2 ownership contract. Risks: session/data loss. Rollback: preserve old schema compatibility and avoid destructive migration; restore baseline package/data as available.

### S6 — Resource, power and long-run qualification (medium; elapsed soak time required)

Objective: sustained operation with bounded resource usage and clear operating limits.

Changes: stop background-only UI polling/artwork/tile work when unnecessary; bound image/cache/log retention and release views under memory pressure. Confirm no repeated graph/session creation or retained callbacks. Profile charging/screen-on/screen-off and battery cases. Keep persistent Now Playing optional; screen dimming is not a substitute for background correctness. Document supported battery/background settings and restart expectations. Do not promise automatic launch after reboot unless verified supported.

Tests: initial two-hour qualification, then eight-hour screen-off playback and overnight idle; repeat account transfers and network recovery during soak. Record process memory vs OS limit, CPU, graph count, log volume, audio gaps and available power/thermal observations. Do not infer temperature from CPU usage or battery drain alone.

Acceptance: no crash, unrecovered stall or authentication loss; one renderer per active pipeline; no sustained memory growth across repeated cycles; memory remains below current OS limit. Establish numeric CPU/memory budgets from S0 measurements before optimizing. Report charging heat and battery behavior as observations with test conditions.

Dependencies: S1–S5. Risks: resource optimization changes timing; long tests cannot be replaced by desktop tests. Rollback: revert individual optimizations; keep the previous accepted package.

## Physical acceptance matrix and evidence

| Scenario | Minimum test | Required evidence |
| --- | --- | --- |
| Lock/background | 30 cycles plus two-hour playlist; charger and battery | Audible continuity, lifecycle/permission/media/PCM timeline; no debugger |
| First lock activation | Start local playback, lock within 1–2 seconds; repeat after keep-alive is logged as allowed | No unintended pause; `MediaPlayer`/SMTC/extended-execution ordering; lock-screen Play/Pause result |
| Family sessions | Owner → guest → owner; guest loss/recovery | Actual audio and controller transfer, ownership generations, no saved guest secret |
| Wi-Fi recovery | Three short outages and one five-minute outage | Network return, bounded retries, audio or clear action; proposed 30-second recovery target |
| Internet/DNS | LAN preserved while internet or DNS unavailable | No false LAN failure; valid rediscovery and retry on return |
| Pause/transfer | Pause, Stop and transfer away during every recovery phase | No unexpected restart or stale command |
| Audio path | Track boundaries, seeks, output reconnect | Producer and consumer progress, graph count, underruns and audible result |
| Restart | Graceful exit, forced termination, offline launch | Login/preferences, snapshot validity, correct ownership and resume policy |
| Idle receiver | Screen off at 5/15/60 minutes, then Connect attempt | mDNS/TCP/getInfo and controller result; distinguish OS suspension from listener defect |
| Locked discoverability | With playback active and with playback idle, lock the Lumia and attempt Connect from a second Premium account at 5/15/60 minutes | Avahi record presence, advertised IP/port, TCP/getInfo, controller result, lifecycle/permission state; classify as supported, recoverable, or OS-imposed limitation |
| Long run | Eight-hour playback plus overnight idle | Resource trend, logs, no crash or unresolved audio stall |

For each run record OS/app/native revisions, APPX/DLL hashes, backend, power/background settings, duration and result. Store only redacted evidence outside Git when it contains private LAN/account/device information. Hardware acceptance is user-assisted unless direct device access is available.

## Execution order and handoff

1. Read this plan, AGENTS.md, DISCOVERY_AUDIT.md and BUILD_GUIDE.md; inspect current diff/history and preserve unrelated work. Confirm installed package version and baseline artifact.
2. Complete S0 and run existing desktop tests. Example from repository root: `dotnet run --project tests/LibreSpotUWP.Playback.Tests/LibreSpotUWP.Playback.Tests.csproj`. An absent SDK is a test prerequisite, not a reason to rebuild Rust or change UWP target.
3. Deliver S1 and S2 as small changes, collecting device evidence after each. Then S3–S5; qualify with S6. Some investigation can overlap, but merge only independently tested behavior.
4. Reuse the validated ARM DLL for managed-only changes. Rebuild Rust only when native source/ABI/features change. Run one incremental ARM build/package per device candidate after local checks; inspect manifest/version, signature, architecture, exports if relevant and artifact hashes. Keep one active build and follow the recorded linker/Cargo preflight.
5. Commit code/tests/docs with Conventional Commits. Update TEST_PLAN.md with actual results, ROADMAP.md with milestone status, and this plan with decisions. Existing TEST_PLAN.md deployment instructions reference older versions; treat them as historical until refreshed.

Suggested first implementation slice: `feat(diagnostics): correlate lifecycle and playback health`, then `fix(background): ...` based on a reproduced device trace. Do not bundle a speculative renderer rewrite with account recovery.

## Definition of done

Goal 5 is complete only when the physical matrix passes, documented platform limitations have explicit behavior, tests/build checks pass, and diagnostics/rollback instructions are usable. A successful compile, a single successful song, or the absence of errors in a desktop run is insufficient. Any untested battery/idle/soak scenario stays explicitly pending.

## Implementation checkpoint (2026-10-18)

Review corrections: reconnect retry state now follows the single generation increment made by each instance recreation, including failed attempts. Snapshot writes carry monotonic revisions and discard obsolete workers. Renderer recovery rechecks cancellation and current playback intent after old-renderer disposal and again before issuing Seek. An authoritative connected-session event clears the recovery block; stale and disconnected events do not. Four behavioral regression tests and the full portable suite passed, together with the managed ARM build; physical qualification is still pending.

## New device observations to resolve in the next session (2026-10-18)

### Correction checkpoint: lock-screen transport and activation ordering

Implemented after source review:

- `MediaPlaybackCommandManager` was enabled while `ButtonPressed` independently dispatched Play/Pause to librespot. The automatic manager could therefore change only the silent sponsor's state, independently of the audible renderer. Play/Pause now use the command manager's received events, mark the command handled, hold a deferral across the real transport operation, and log failures. The duplicate ButtonPressed Play/Pause route was removed; shuffle/repeat integration is retained. This follows Microsoft's [command manager guidance](https://learn.microsoft.com/en-us/uwp/api/windows.media.playback.mediaplaybackcommandmanager.isenabled).
- Accepted native playback now starts the background MediaPlayer and publishes SMTC Playing before awaiting PCM pre-roll. Synchronously rejected stale requests do not arm it. Existing final publication after pre-roll remains in place. This reduces the first-lock activation window; it does not prove that the observed device pause is fixed.
- The portable suite includes a source-wiring regression for command ownership, deferrals, handler cleanup, and sponsor activation before pre-roll. Existing transition tests exercise stale native requests and cancelled transitions. These tests cannot simulate Windows lock-screen policy.

Locked discovery remains open, not fixed by this checkpoint. Native `../librespot-upstream/discovery/src/server.rs` binds a Rust `std::net::TcpListener` and converts it to a Tokio listener. This is not a WinRT `StreamSocketListener` with socket-broker ownership. Adding a manifest task or `IsNetworkRequested` alone cannot hand that existing listener to the broker. Multicast advertisement and TCP credential exchange both need an execution/ownership design; fixing only advertisement would leave controllers stuck connecting.

There is also a separate active-stream networking gap: the sponsor supplies locally generated silence, while librespot fetches the real stream. Microsoft [documents](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/background-audio) that network-aware media sources retain networking while fetching remote content, and that MediaStreamSource relies on accurate `SetBufferedRange` reporting. Reporting an invented buffered range for the silent source would not describe the real stream. If lock-time traces show networking is removed despite an active sponsor, implement a separately tested real-PCM MediaStreamSource path with truthful buffering, rather than patching silence metadata.

Next acceptance step: package the managed correction using the existing native DLL, then capture immediate-first-lock and delayed-lock playback, intentional lock-screen Play/Pause, and locked active/idle TCP/getInfo plus discovery from a second account. If active playback survives but idle reception does not, evaluate the WinRT socket-broker port as a separate native/managed milestone using the decision tree below. No product decision is needed for the implemented corrections. An always-reachable locked idle receiver remains an unmet goal requiring platform feasibility evidence; no always-on screen behavior is enabled implicitly.

Validation: portable suite passed, including the new wiring regression; sibling Login Helper audit remains skipped because its checkout is absent. Managed ARM build results are recorded in `TEST_PLAN.md`. Lumia verification and a new APPX remain pending.

Review follow-up for package 1.0.15.0: a playback-ready task can complete after a concurrent Pause, transfer, disposal, session replacement, or PCM transition replacement. Previously its continuation could publish Playing and restart the sponsor without checking current ownership/intent. `CanPublishNativePlayback` now checks disposal, requested playback, local target, session generation and pending/active PCM identity both before requesting playback and after the pre-roll await. The wiring regression verifies the completion guard and its required conditions. The portable suite passed again; physical lock-screen behavior and locked discovery remain pending. Package 1.0.14.0 is retained for rollback; the native DLL is reused unchanged.

Package 1.0.15.0 generated successfully: `LibreSpotUWP/AppPackages/LibreSpotUWP_1.0.15.0_ARM_Test/LibreSpotUWP_1.0.15.0_ARM.appx`. Release ARM/.NET Native build and MakeAppx extraction passed. Manifest identity/publisher are unchanged, architecture is `arm`, and the PKCS#7 signature verifies cryptographically against the same development signer (`D12D4AD769E2DE1F8F0C82E58DD6533165658455`). SignTool `/pa` reports the expected untrusted self-signed development root on this host; this is not a trusted production signature. APPX SHA-256: `DB397AFF75F042C38D40355017F8F0BE0528D556404D841266C5664D55CAD312`. Embedded native DLL SHA-256: `F785F226D82D04EEA705161211EAC2915FD42382FF8B24B47900220CC44526D7`, matching both the source DLL and rollback package. ARM dependencies are included beside the APPX. Installation and physical acceptance are pending; validate About 1.0.15.0, immediate lock, intentional Pause/Play, guest transfer, then locked active/idle discovery separately.

### A. First screen lock pauses once

Observed behavior: the first transition to the lock screen pauses playback. Pressing Play from the lock screen restores playback, and subsequent screen-off periods behave normally.

Working hypothesis: the first lock races the asynchronous establishment of the single-process background-media/SMTC session and the `ExtendedExecutionSession`. `OnSuspending` currently reinforces `_mediaPlayer.Play()` after the transition has started, which may be too late to prevent the OS from treating the session as inactive. This is not yet proven to be an explicit Spotify pause or a native producer failure.

Investigation:

1. Add a monotonic launch/playback correlation to the device trace and log `EnteredBackground`, `Suspending`, `MediaPlayer.PlaybackStateChanged`, `SystemMediaTransportControls.PlaybackStatus`, `SessionStateChanged`, and extended-execution request/allow/revoke results.
2. Compare an immediate lock (within 1–2 seconds of starting playback) with a lock after the keep-alive request has completed successfully.
3. Confirm whether `_mediaPlayer.Play()` and SMTC status are already `Playing` before the first lock; do not infer this from librespot's playback state alone.
4. If the race is confirmed, arm the background media session at the local-playback activation boundary, make the first transition idempotent, and keep `OnSuspending` as a bounded reconciliation rather than the first activation point. Add a pure ordering test and repeat the physical first-lock matrix.

### B. Discoverability disappears while locked

Observed behavior: with LibreSpotUWP open, the Lumia is discoverable while unlocked but disappears from Spotify Connect after the screen is locked. Unlocking restores discoverability. This must be tested both while audio is playing and while the receiver is idle.

What the platform evidence says: the `backgroundMediaPlayback` capability is intended to keep active media playback alive and requires SMTC integration; it does not promise an indefinitely running arbitrary network listener. Microsoft documents that network work in Connected Standby may require a background task with `IsNetworkRequested` or socket-broker ownership (`SocketActivityTrigger`/`ControlChannelTrigger`). `ExtendedExecutionReason.Unspecified` is not equivalent to an always-on mDNS service on Windows 10 Mobile.

Implementation decision tree for the next session:

1. First verify whether the native librespot Zeroconf listener remains bound and receives packets while locked during active playback. Record the advertised address/port and compare local TCP/getInfo with the Spotify controller result.
2. If active playback keeps the process alive but idle discovery does not, define two explicit contracts: “discoverable during active background playback” and “idle receiver.” Do not change audio behavior to preserve idle discovery without evidence.
3. If idle discovery is a hard requirement, inspect whether the listener can be handed to a supported Windows socket-broker/background-task mechanism on this Lumia build. A task-based design must request network with `IsNetworkRequested`, use the documented trigger/ownership lifecycle, and pass packet handling back safely; it must not create a second unmanaged mDNS listener.
4. If Windows 10 Mobile cannot support the required multicast listener in Connected Standby, document the limitation in the UI/diagnostics and require the user to unlock/start playback before a new Connect transfer. Do not add a private capability, hardcoded port, or periodic polling workaround.

Acceptance for this investigation is evidence-backed classification, not merely a successful unlocked test: the plan must state whether locked active playback and locked idle discoverability are supported, conditionally supported, or OS-limited, with an artifact hash, OS build, lifecycle trace, Avahi result, TCP/getInfo result, and controller result.

Changes now in the working tree:

- S0 groundwork: log entries carry a process-launch correlation ID; background transitions record app memory usage/limit and memory pressure; MediaPlayer state/failure events are logged; the existing bounded logger can export its already-redacted log to `LibreSpotUWP-diagnostics.txt` in app local storage. The export waits at most two seconds for queued entries. The secret redactor is isolated for direct tests.
- S1: suspension tasks run under one four-second deadline, media preparation runs before optional discovery/tile work, and an individual failure no longer prevents later cleanup. Extended execution requests are serialized; failed, stopped, and revoked sessions are detached and disposed. Foreground return restores the background media sponsor when playback intent still requires it and always reconciles connectivity.
- S2: automatic producer/network recovery checks the active playback account before using saved owner authorization. Unknown or guest sessions fail closed in producer recovery; known guest sessions are not replaced on network/auth changes. Session-generation checks cancel stale recovery. Guest credentials remain transient. Playback snapshots now record only the owner account identity (no guest ID or credential); legacy, guest, and account-mismatched snapshots do not autoplay. Forced snapshot writes now complete synchronously before suspension/explicit state transitions.
- S3: network notifications are coalesced for 250 ms, and clearing the connectivity backoff no longer returns before reconciliation.
- S4: the ring-buffer backend distinguishes a stopped renderer from a stalled producer when PCM is buffered and the consumer read position has not advanced. Renderer recovery recreates only the audio backend and seeks to the saved position; Spotify session recovery remains a separate path.
- S5: snapshot ownership and owner-only resume are implemented; the existing snapshot schema remains backward-readable, while old snapshots without ownership are intentionally not auto-resumed.
- S6: memory samples and existing bounded audio/queue diagnostics provide initial soak evidence. Resource budgets and all long-run results still require measurement on the Lumia.

Automated coverage added for owner/guest recovery authorization, snapshot resume ownership, producer-versus-consumer stall classification, lifecycle/recovery wiring, and log secret redaction. The portable playback suite passed; its Login Helper source audit was skipped because that sibling checkout is absent. The managed UWP `Release|ARM` solution build passed without creating an APPX. Windows 10 Mobile lock/unlock, account transfer, Wi-Fi recovery, APPX installation, and soak acceptance remain unverified until exercised on the Lumia. No native Rust change or APPX deployment is part of this code slice. See `TEST_PLAN.md` for exact commands and results.
