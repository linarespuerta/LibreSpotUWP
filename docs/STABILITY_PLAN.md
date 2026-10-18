# Goal 5: stability and appliance operation

Date: 2026-10-18. Status: implementation underway. The checkpoint at the end records delivered code and outstanding acceptance evidence.

## Scope and baseline

Cover all of Goal 5 in `../../prompt.md`: network recovery, Spotify session recovery, background audio, authentication preservation, restart behavior, CPU/memory efficiency, battery/thermal behavior, actionable errors, safe diagnostics, and optional appliance operation. Audio-format optimization, UI redesign, and Home Assistant remain separate goals.

Baseline: Lumia ARM32 package 1.0.13.0. Two-account discovery, transfer, and playback were physically confirmed; see [DISCOVERY_AUDIT.md](DISCOVERY_AUDIT.md). This does not establish long-running stability. The user reports intermittent interruption after locking the phone. No correlated lock-time device log was collected during this planning session; the exact cause remains unconfirmed.

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

Changes: audit SMTC ownership/command handling, silent MediaPlayer and audible renderer state together; add EnteredBackground/LeavingBackground handling with low-cost state recording and memory release. Serialize extended-execution requests and make revoke/dispose/error paths idempotent, with unsubscribe/cleanup on all paths. Reconcile current health on foreground return even if app-level `_suspended` was never set. Keep suspension work deadline-bounded; media preparation must not depend on live tile or keep-alive success.

Decision gate: use S0 evidence to choose the smallest supported correction to media/SMTC/network lifetime. First validate the existing AudioGraph path against Microsoft's requirements. Only if device evidence proves it necessary, prototype feeding actual decoded PCM through a platform media source as an alternative to the silent-player arrangement. Treat that as a separate renderer change with format, seek, buffering and continuity regression tests; avoid an immediate rewrite or legacy dual-process migration.

Tests: lifecycle policy transitions and permission denied/revoked/racing requests; physical lock/unlock, Home, track boundary, SMTC buttons, owner/guest, battery/charger, debugger detached.

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

Changes: separate LAN reachability, internet indication, Spotify transport state and discovery readiness. Debounce/coalesce network events; defer retries while offline and re-arm on genuine return. Audit the early return after clearing connectivity backoff to ensure subsequent handling is triggered. Re-evaluate usable interface/address on Wi-Fi changes and recreate advertisement/listener only when needed. Never hardcode IP or the dynamic port. Prevent repeated errors from exhausting recovery while the network is unavailable.

Tests: Wi-Fi off/on, internet unavailable while LAN remains up, DNS failure/restoration, repeated network notifications, address change, Spotify endpoint unavailable, connected-but-no-internet. Verify native discovery implementation before selecting rebind/recreate behavior.

Acceptance: after connectivity is confirmed restored, a healthy active session resumes or exposes a specific retry action within a proposed 30-second target; no wrong-account recovery. A current avahi record, TCP connection and getInfo response match the actual endpoint. Idle/screen-off cases record platform limits separately.

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
| Family sessions | Owner → guest → owner; guest loss/recovery | Actual audio and controller transfer, ownership generations, no saved guest secret |
| Wi-Fi recovery | Three short outages and one five-minute outage | Network return, bounded retries, audio or clear action; proposed 30-second recovery target |
| Internet/DNS | LAN preserved while internet or DNS unavailable | No false LAN failure; valid rediscovery and retry on return |
| Pause/transfer | Pause, Stop and transfer away during every recovery phase | No unexpected restart or stale command |
| Audio path | Track boundaries, seeks, output reconnect | Producer and consumer progress, graph count, underruns and audible result |
| Restart | Graceful exit, forced termination, offline launch | Login/preferences, snapshot validity, correct ownership and resume policy |
| Idle receiver | Screen off at 5/15/60 minutes, then Connect attempt | mDNS/TCP/getInfo and controller result; distinguish OS suspension from listener defect |
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

Changes now in the working tree:

- S0 groundwork: log entries carry a process-launch correlation ID; background transitions record app memory usage/limit and memory pressure; MediaPlayer state/failure events are logged; the existing bounded logger can export its already-redacted log to `LibreSpotUWP-diagnostics.txt` in app local storage. The export waits at most two seconds for queued entries. The secret redactor is isolated for direct tests.
- S1: suspension tasks run under one four-second deadline, media preparation runs before optional discovery/tile work, and an individual failure no longer prevents later cleanup. Extended execution requests are serialized; failed, stopped, and revoked sessions are detached and disposed. Foreground return restores the background media sponsor when playback intent still requires it and always reconciles connectivity.
- S2: automatic producer/network recovery checks the active playback account before using saved owner authorization. Unknown or guest sessions fail closed in producer recovery; known guest sessions are not replaced on network/auth changes. Session-generation checks cancel stale recovery. Guest credentials remain transient. Playback snapshots now record only the owner account identity (no guest ID or credential); legacy, guest, and account-mismatched snapshots do not autoplay. Forced snapshot writes now complete synchronously before suspension/explicit state transitions.
- S3: network notifications are coalesced for 250 ms, and clearing the connectivity backoff no longer returns before reconciliation.
- S4: the ring-buffer backend distinguishes a stopped renderer from a stalled producer when PCM is buffered and the consumer read position has not advanced. Renderer recovery recreates only the audio backend and seeks to the saved position; Spotify session recovery remains a separate path.
- S5: snapshot ownership and owner-only resume are implemented; the existing snapshot schema remains backward-readable, while old snapshots without ownership are intentionally not auto-resumed.
- S6: memory samples and existing bounded audio/queue diagnostics provide initial soak evidence. Resource budgets and all long-run results still require measurement on the Lumia.

Automated coverage added for owner/guest recovery authorization, snapshot resume ownership, producer-versus-consumer stall classification, lifecycle/recovery wiring, and log secret redaction. The portable playback suite passed; its Login Helper source audit was skipped because that sibling checkout is absent. The managed UWP `Release|ARM` solution build passed without creating an APPX. Windows 10 Mobile lock/unlock, account transfer, Wi-Fi recovery, APPX installation, and soak acceptance remain unverified until exercised on the Lumia. No native Rust change or APPX deployment is part of this code slice. See `TEST_PLAN.md` for exact commands and results.
