# Test plan

The local/desktop boundary is documented in [LOCAL_TESTING.md](LOCAL_TESTING.md). Desktop results must not be reported as Lumia hardware acceptance.

## Automated and static checks

- Restore/build `Release|ARM` on the pinned Windows toolchain.
- Verify C# `LibrespotConfig` layout against the exact Rust FFI definition for ARM32, including Boolean size, enum widths, pointer alignment and callback lifetime.
- Run existing playback tests and smoke scripts only after dependencies are restored; record commands and outcomes.
- Inspect APPX contents, manifest capabilities, DLL machine type, signature, and absence of secrets.
- Stability change coverage: `dotnet run --project tests/LibreSpotUWP.Playback.Tests/LibreSpotUWP.Playback.Tests.csproj` includes guest/owner recovery authorization, snapshot ownership, renderer-stall classification, lifecycle/network wiring, and diagnostic redaction. Record the actual result for this change below; a passing portable test does not replace a UWP ARM build.

## Stability implementation validation (2026-10-18)

User acceptance update: after installing 1.0.15.0, the user reported that it works and requested closure of the current stability delivery. This is a functional acceptance report; individual lock-cycle, locked-idle discovery, outage and long-run matrix results were not supplied. Retain those as deferred qualification, as recorded in `STABILITY_PLAN.md`. The next UI work uses 1.0.15.0 as its accepted baseline.

APPX review follow-up (1.0.15.0): added current-intent/session/PCM checks around playback readiness so a late pre-roll completion cannot publish an obsolete Playing state. The full portable suite passed, including the expanded lock-screen wiring regression; the Login Helper audit remains skipped. Signed ARM package validation is recorded in `STABILITY_PLAN.md` after generation. Hardware acceptance remains pending.

Package generation completed successfully with `Release|ARM`, .NET Native, packaging enabled and the existing development certificate. MakeAppx extraction, manifest ARM/version/identity checks, cryptographic signature verification and embedded native DLL hash comparison passed. SignTool's trust-chain check reports the existing untrusted development root. Package hash and rollback details are in `STABILITY_PLAN.md`; no deployment was performed.

Lock-screen follow-up: Play/Pause commands now use handled command-manager events with deferrals instead of the duplicate ButtonPressed route. Accepted playback arms its background sponsor before awaiting PCM pre-roll. The full portable suite, including `LockScreenCommandsHaveOnePlaybackOwner`, passed (exit 0); the new test audits source wiring and ordering, not OS behavior. The managed `Release|ARM` build with packaging disabled passed (exit 0), with existing compiler warnings. The Login Helper audit remains skipped. No APPX was produced and first-lock/locked-discovery behavior remains unverified on the Lumia. See `STABILITY_PLAN.md` for the remaining native socket/network ownership investigation.

Review follow-up: fixed self-invalidating reconnect retries, out-of-order snapshot writes, renderer continuation after Pause/Stop, and recovery remaining blocked after a new Connect session. Added executable regression tests: `FailedReconnectAllowsTheNextOwnedGeneration`, `OldSnapshotCannotOverwriteForcedPause`, `PauseDuringRendererDisposalPreventsContinuation`, and `NewConnectSessionRearmsRecovery`. The full portable suite and managed `Release|ARM` build passed again (exit 0). The Login Helper audit remains skipped because its checkout is absent; Lumia acceptance remains pending.

- `dotnet run --project tests/LibreSpotUWP.Playback.Tests/LibreSpotUWP.Playback.Tests.csproj`: passed all registered portable playback tests, including the new recovery, snapshot ownership, renderer stall, lifecycle and redaction cases. The Login Helper source audit printed `SKIP` because the sibling `LibreSpotUWPLoginHelper` checkout is absent.
- `MSBuild.exe LibreSpotUWP.sln /t:Build /p:Configuration=Release /p:Platform=ARM /p:AppxPackage=false /p:GenerateAppxPackageOnBuild=false /m /v:quiet`: passed (exit 0); compiles the UWP ARM managed app. No APPX was generated or signed. MSBuild emitted existing XAML/C# warnings, including warnings for legacy background-access enum values retained for compatibility.
- `git diff --check`: passed after implementation.
- Both project files parse as XML.
- APPX package inspection: not run because package generation was disabled.
- Lumia lock/unlock, multi-account, Wi-Fi, restart, and soak matrix: pending physical-device execution. No hardware result is inferred from local checks.

Diagnostic export entry point: `LogService.ExportRedactedDiagnosticsAsync()` writes `LibreSpotUWP-diagnostics.txt` under app local storage after waiting up to two seconds for queued log entries. It applies the logger's current token/user/path redaction rules; inspect the export before sharing it.

## Lumia test matrix

| Scenario | Evidence required |
| --- | --- |
| Baseline login/playback | audio through 3.5 mm, metadata, redacted log |
| Discovery disabled | no unintended advertisement/listener |
| Discovery enabled | avahi record, TXT data, reachable HTTP handshake |
| Family transfer | independent second Premium account discovers, transfers and controls device |
| Recovery | Wi-Fi loss/rejoin, session reconnect, app restart, resumed playback behavior |
| UI | artwork/metadata transition, controls, dimming, burn-in movement, sustained performance |
| Audio | selected format/backend, effects state, glitches/underruns over extended playback |
| HA prototype | entity state/control latency, reconnect, unauthorized request rejection |

Each test records device OS/build, app and Rust commit, package hash, network topology, setting state, pass/fail, and redacted logs. Failed discovery must identify whether mDNS, TCP, HTTP, authentication, transfer, or audio failed. Do not mark physical acceptance criteria as passed from desktop-only testing.
# Current execution gate (2026-10-12)

The ARM32 package is now buildable and signed with a local development certificate. The next required test is physical Lumia deployment:

1. Back up the installed v1.0.5 package/data and record the rollback path.
2. Transfer `LibreSpotUWP_Development.cer` to the Lumia and install/trust it if Device Portal requests it.
3. Sideload `LibreSpotUWP_1.0.6.0_ARM.appx` through Windows Device Portal. **Completed:** the Lumia reports version 1.0.6.0, launches, and retains the existing login/session.
4. Build and sideload the 1.0.7.0 discovery MVP. Keep the new toggle off for the first launch, then enable it in Settings and restart the app.
5. From Linux, run `avahi-browse -rt _spotify-connect._tcp` and record whether the Lumia advertises a reachable endpoint.

This package is a baseline deployment artifact; it does not yet claim cross-account discovery. That claim requires the M1 discovery change and the two-account hardware test below.

