# Test plan

## Backend-switch diagnostic instrumentation

- Generated artifact: `LibreSpotUWP/AppPackages/LibreSpotUWP_1.0.19.0_ARM_Test/LibreSpotUWP_1.0.19.0_ARM.appx` (11,326,675 bytes). SHA-256: `8C9F2D62E41E42A6A188A18D42964D4EFE68F72FAB13F2FF8A18D762F1C4DBF4`.
- ARM Release/.NET Native build: PASS. Manifest version/identity/ARM/keepalive, cryptographic signature with the existing development signer, and MakeAppx extraction: PASS. The signer remains self-signed development, not public production trust.
- Generated XAML check: PASS (43). Packaged executable/PRI mapping: PASS (48, all Settings variants). The native executable contains the dedicated `audio-backend-startup.txt` marker. This does not substitute for reading the file after an actual device run.
- Embedded librespot DLL unchanged (`43598DCC36B4D2914C8305A6C8AD83106F3E06B9F4CBDAB1D0B1319FA06F059C`); no Rust rebuild. 1.0.18.0 APPX retained unchanged (`8CA4F7CCC1596CEAFCFF204C6C1A5617676C2E3537F0AA13452E8D36503B52AD`). Temporary signing PFX removed after build.

- `BackendStartupCheckpointsAreDurableBoundedAndSecretFree`: PASS. Tests the production trace class: immediate file visibility without a logging worker/delay, type/HRESULT without exception message leakage, rejection of untrusted stage strings, 64 KiB bound, retained previous-launch checkpoint, and contained storage failures. Source wiring checks require markers before WinRT stream/source creation and prohibit checkpoint writes in PCM sample handling.
- Full portable playback suite: PASS with the diagnostic changes. This is instrumentation only; it does not establish a crash fix, successful PlatformMedia startup or bit-perfect output.
- Physical 1.0.18.0: Settings/About accepted by the user; PlatformMedia selection terminates the app. Crash dump capture is enabled but no dump was enumerated. Root cause remains open pending the dedicated flushed checkpoint file from the diagnostic candidate.
- Not deployed by the agent. Next: install 1.0.19.0, select Windows streaming once, read `TempState/audio-backend-startup.txt` using the version-specific package identity in Device Portal, and correlate the latest launch's final stage with the last completed operation. Keep device traces/dumps outside Git. Do not claim this diagnostic package fixes the crash or verifies Hi-Fi fidelity.

## Verified Settings resource-root repair: 1.0.18.0

- Cause confirmed by device exception plus package comparison: 1.0.17.0's native executable used `ms-appx:///LibreSpotUWP/Views/...Settings...xaml`; PRI resources existed only under `Files/Views/...Settings...xbf`. The accepted 1.0.15.0 executable used the correct `/Views/...` root. Exact earlier cache-contaminating invocation remains unproven.
- Generated ARM Release `obj`/`bin` directories moved to a unique temporary backup after validating their resolved paths. Issued APPX files preserved. Built from the directory containing `LibreSpotUWP.csproj`; no Rust rebuild, credential change or backend default change.
- `Test-GeneratedXamlUris.ps1 -SelfTest`: PASS (three correct Settings paths accepted; three duplicated-prefix paths rejected). The verifier also rejected the actual old generated Settings URI before the cache was moved.
- Actual 1.0.17.0 package negative regression: PASS (the verifier rejects exactly the three invalid Settings resource URIs). Clean generated outputs: PASS (43 application XAML URIs). The project invokes this validation after `MarkupCompilePass1`, before native compilation/packaging.
- Portable playback suite: PASS. ARM Release/.NET Native signed build: PASS. Existing compiler warnings remain; no build errors.
- **Packaged** native executable versus PRI: PASS (48 unique XAML URIs, explicitly including all three correct Settings paths). This checks the artifact rather than assuming a successful compiler run produces correct runtime resource URIs.
- Artifact: `LibreSpotUWP/AppPackages/LibreSpotUWP_1.0.18.0_ARM_Test/LibreSpotUWP_1.0.18.0_ARM.appx` (11,324,849 bytes). SHA-256: `8CA4F7CCC1596CEAFCFF204C6C1A5617676C2E3537F0AA13452E8D36503B52AD`.
- Version/identity/ARM/keepalive manifest: PASS. Embedded native DLL matches unchanged corrected build `43598DCC36B4D2914C8305A6C8AD83106F3E06B9F4CBDAB1D0B1319FA06F059C`. PKCS#7 signature and existing development signer verified; MakeAppx unpack: PASS. Temporary signing PFX removed after build.
- 1.0.15.0 rollback and 1.0.17.0 diagnostic packages retained with their original hashes. Not deployed by the agent. Device gate: Settings opens, About reports 1.0.18.0, backend selection is accessible; then repeat PlatformMedia locked-control/audio acceptance. Artifact mapping checks are not physical acceptance.

## 1.0.17.0 build checks (superseded: physical Settings regression)

Physical follow-up found `XamlParseException` before Settings initialization. The packaged native executable requests a duplicated `LibreSpotUWP/` resource prefix that is absent from the PRI. The successful checks below did not cover this runtime resource mapping and must not be read as physical acceptance. See BUILD_GUIDE.md for the exact comparison and preventive build guard.

- Release ARM/.NET Native build and signing: PASS. Source: `a2e6ca2` plus the manifest-only version increment to 1.0.17.0; native HEAD `f51b24e`.
- Artifact: `LibreSpotUWP/AppPackages/LibreSpotUWP_1.0.17.0_ARM_Test/LibreSpotUWP_1.0.17.0_ARM.appx` (11,325,126 bytes).
- APPX SHA-256: `F5773A310C1CCF8A989CDAFF1DB47B2911FB46EC995966C596A103501C22498A`.
- Embedded corrected DLL SHA-256: `43598DCC36B4D2914C8305A6C8AD83106F3E06B9F4CBDAB1D0B1319FA06F059C`; matches the verified native build exactly.
- Manifest identity/publisher unchanged; version 1.0.17.0, architecture ARM, keepalive executable/entrypoint verified. PKCS#7 cryptographic signature and existing development signer `D12D4AD769E2DE1F8F0C82E58DD6533165658455` verified; this is not public production-certificate trust. MakeAppx unpack: PASS.
- Portable playback suite rerun after the version increment: PASS. Existing signed 1.0.16.0 APPX retained unchanged (SHA-256 `519963B1AB61F556B25381B27C643786C73AC5A5D9C72CC66484F4EB9E1BDD52`). Temporary signing PFX removed after build.
- Not deployed by the agent. Select/retain **Windows streaming (background audio)**, restart once, transfer Spotify, then perform the locked-startup/control/track-boundary matrix in STABILITY_PLAN.md. Physical acceptance remains open.

## Review follow-up: network ownership across queues/startup

- Portable playback suite: PASS, including `ControlWakeWaitsForActualPlaybackAndRejectsStaleReadiness`. ACK-only, old Windows Playing state, pre-roll-only, obsolete startup versions and pre-readiness PCM cannot release startup. A post-readiness sample plus Windows Playing completes it; pause/close cancel it.
- DealerTransportProbe: PASS (2). Imports the actual bridge and `leased_stream.rs`; checks synchronous queued-frame ownership, malformed JSON, rejected backlog, cancellation, asynchronous typed subscriber handling, release on Pending, and subscription drop.
- DealerLivenessProbe: PASS (2); existing retry/cancellation behavior remains covered.
- Final native ARM32 UWP release build: PASS at `f51b24e`; PE ARM32/AppContainer and all 53 managed imports verified. Local DLL SHA-256: `43598DCC36B4D2914C8305A6C8AD83106F3E06B9F4CBDAB1D0B1319FA06F059C`. Reverse-apply check for the tracked follow-up patch: PASS.
- Managed solution ARM Release build: PASS with `/t:Build /p:AppxPackage=false /p:GenerateAppxPackageOnBuild=false /p:UseDotNetNativeToolchain=false`. This validates the UWP/XAML compilation, not .NET Native packaging or hardware behavior. Do not invoke `/t:Compile` directly: it bypasses XAML generation and reports misleading missing generated control names.
- The signed 1.0.16.0 artifact below is unchanged and does not contain these fixes. The corrected 1.0.17.0 package is verified above. Locked uncached startup, PCM delivery, hardware-slot allocation and long-run control still require the Lumia.

## 2026-10-18 — Background control/real PCM candidate

- Portable playback suite: PASS, including PCM frame/timestamp/boundary/overrun tests, bounded control work and stored-reply completion, and source wiring checks. Existing account, recovery, transition and cancellation cases pass. Login Helper sibling source audit: SKIP (checkout absent).
- `cargo test --offline --manifest-path tests/DealerLivenessProbe/Cargo.toml`: PASS (2). Extracts production Dealer run loop; transient URL/socket failures retry and remain cancellable.
- `cargo test --offline --manifest-path tests/DealerTransportProbe/Cargo.toml`: PASS (1). Imports production bridge with mocked host/protocol sink; verifies invalid/oversized frames, bounded backlog, reply dispatch, closure and independent connection IDs. Does not emulate WinRT, Spotify service authentication or Mobile standby.
- Native ARM release build: PASS with existing UWP target/features and SDK binding patches. All **53** managed FFI imports exported; PE ARM32/AppContainer verified.
- DLL SHA-256: `BECC14EC3F82D3B21ECA132216CC2824DB806146A3B130DE4BE7B58D296AC294`.
- Physical acceptance is the matrix in STABILITY_PLAN.md. Host simulations are not evidence that ControlChannelTrigger hardware slots or locked wake work on this Lumia. Idle fresh-account discovery remains separate.

### Package verification

Release ARM/.NET Native packaging passed for source commit `74486ea`. Artifact: `LibreSpotUWP/AppPackages/LibreSpotUWP_1.0.16.0_ARM_Test/LibreSpotUWP_1.0.16.0_ARM.appx`. SHA-256: `519963B1AB61F556B25381B27C643786C73AC5A5D9C72CC66484F4EB9E1BDD52`.

Verified unchanged package identity/publisher, version `1.0.16.0`, architecture `arm`, and resolved keepalive extension executable `LibreSpotUWP.exe`. Embedded DLL matches the hash above. PKCS#7 cryptographic signature verification passed with the existing development signer `D12D4AD769E2DE1F8F0C82E58DD6533165658455`; this does not claim a publicly trusted production certificate. MakeAppx extraction passed. Temporary signing PFX was removed after the build. The prior 1.0.15.0 APPX is retained. No deployment or physical lock-screen acceptance was performed by this build.
The local/desktop boundary is documented in [LOCAL_TESTING.md](LOCAL_TESTING.md). Desktop results must not be reported as Lumia hardware acceptance.

## Automated and static checks

- Restore/build `Release|ARM` on the pinned Windows toolchain.
- Verify C# `LibrespotConfig` layout against the exact Rust FFI definition for ARM32, including Boolean size, enum widths, pointer alignment and callback lifetime.
- Run existing playback tests and smoke scripts only after dependencies are restored; record commands and outcomes.
- Inspect APPX contents, manifest capabilities, DLL machine type, signature, and absence of secrets.
- Stability change coverage: `dotnet run --project tests/LibreSpotUWP.Playback.Tests/LibreSpotUWP.Playback.Tests.csproj` includes guest/owner recovery authorization, snapshot ownership, renderer-stall classification, lifecycle/network wiring, and diagnostic redaction. Record the actual result for this change below; a passing portable test does not replace a UWP ARM build.

## Stability implementation validation (2026-10-18)

R0 physical evidence update: Device Portal confirmed 1.0.15.0 and allowed downloading two receiver log snapshots outside Git. Around 20:59 local time, native Pause/Resume events precede and follow an interval of native Connect-state and managed Spotify HTTP failures; receiver background entry/exit brackets that interval, with no Spirc termination or managed generation change logged in the short window. This confirms receiver network-request failures and intermittent command delivery, not their underlying OS/network cause. Separate controller-only and receiver-only lock tests remain pending; see the timeline and revised hypothesis in STABILITY_PLAN.md. No raw logs, account/device identifiers or LAN addresses are committed.

New failed physical scenario after initial acceptance: on 1.0.15.0 the user reports that locking the Spotify controller phone loses remote control; the Lumia continues playing and ignores Pause from the controller. Receiver lock state and root cause are unconfirmed. Initial functional acceptance does not cover this scenario. Track R0 in `STABILITY_PLAN.md` and `HIFI_UI_PLAN.md`; require owner/guest controller-lock tests, selected-target verification, native command/session traces and successful remote commands without touching the Lumia before closing it. This documentation update does not claim a runtime correction or a newly executed device test.

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

