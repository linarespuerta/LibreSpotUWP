# Test plan

## Hi-Fi now-playing UI on the stability source line (1.0.29)

- The opt-in Hi-Fi player is layered on the existing stability changes in this source tree; it does not replace the classic player by default or alter the audio backend/native DLL. Physical stability acceptance remains open.
- Portable playback suite: PASS, including stability regressions and `HiFiPlayerIsOptInAndReusesExistingPlaybackState`. Login Helper source audit: SKIP because its sibling checkout is absent.
- ARM Release/.NET Native build: PASS, exit 0. APPX signed with the established development certificate (`CN=MegaBytesMe`, thumbprint `D12D4AD769E2DE1F8F0C82E58DD6533165658455`). SignTool confirms the signer but reports the expected untrusted self-signed root on this PC.
- Artifact: `LibreSpotUWP/AppPackages/LibreSpotUWP_1.0.29.0_ARM_Test/LibreSpotUWP_1.0.29.0_ARM.appx`, 11,350,247 bytes; SHA-256 `ED01CE67796386FB0A3ED9C7462AA3DFE8FD9ACE67BF19273CA274B3B6D10F7B`. Identity/version/architecture: `34151aa6-2b17-4a37-bddb-2ee83fdf53e4`, `CN=MegaBytesMe`, 1.0.29.0, ARM. Embedded `librespot.dll` is unchanged (SHA-256 `43598DCC36B4D2914C8305A6C8AD83106F3E06B9F4CBDAB1D0B1319FA06F059C`).
- MakeAppx extraction: PASS; 49 packaged native XAML URIs resolve in PRI, including all Settings variants. ARM dependency sidecars are included. Lumia visual review and stability journey remain pending; package/build success does not establish device acceptance.

## Same-track resume correction (1.0.26)

- Portable playback suite: PASS, including `SameTrackResumeKeepsPollingUntilPreRollIsReady` and all simulated 10 ms AudioGraph quantum tests. Login Helper source audit: SKIP because its sibling checkout is absent.
- ARM Release/.NET Native build: PASS, 0 errors (63 existing warnings). No Rust rebuild; packaged `librespot.dll` SHA-256 remains `43598DCC36B4D2914C8305A6C8AD83106F3E06B9F4CBDAB1D0B1319FA06F059C`.
- Artifact: `LibreSpotUWP/AppPackages/LibreSpotUWP_1.0.26.0_ARM_Test/LibreSpotUWP_1.0.26.0_ARM.appx`, 11,340,934 bytes; SHA-256 `9D4F4D4FAA957233E5D9D12A95A00414D3E27C259647956CCC6FEC8E67677DDF`. Identity/version/architecture: `34151aa6-2b17-4a37-bddb-2ee83fdf53e4`, `CN=MegaBytesMe`, 1.0.26.0, ARM.
- Signed with the existing development certificate (`D12D4AD769E2DE1F8F0C82E58DD6533165658455`). MakeAppx unpack: PASS; generated-XAML URI self-test: PASS; packaged native XAML/PRI audit: PASS (48 URIs including Settings). SignTool policy verification reports the expected untrusted self-signed root on this PC; the Lumia must trust the existing development certificate as for prior packages.
- Physical Lumia journey remains pending. Install 1.0.26.0, verify About, then execute the pause/play, repeated-cycle, track-boundary, dual-device lock and latency measurements documented in [STABILITY_PLAN.md](STABILITY_PLAN.md#same-track-pauseresume-failure-and-regression-journey-2026-10-19). Host test/build success does not establish device audio or control behavior.

## Background source pause recovery (1.0.27)

- Portable suite: PASS, including `UnexpectedPlatformSourcePauseOnlyRecoversWhenPlaybackIsDesired`; explicit pause and disposed-source cases do not re-arm playback. Login Helper source audit: SKIP because its sibling checkout is absent.
- ARM Release/.NET Native package build: PASS, exit 0, with existing XAML/C# warnings. The package was emitted unsigned, then signed from the Windows certificate store using the established development identity (`CN=MegaBytesMe`, thumbprint `D12D4AD769E2DE1F8F0C82E58DD6533165658455`).
- Artifact: `LibreSpotUWP/AppPackages/LibreSpotUWP_1.0.27.0_ARM_Test/LibreSpotUWP_1.0.27.0_ARM.appx`, 11,342,081 bytes; SHA-256 `147EC61D6D872E9200DF3099056299CFDBB74C732722E2C6A5AE934799A98EB1`. Identity/version/architecture remain `34151aa6-2b17-4a37-bddb-2ee83fdf53e4`, `CN=MegaBytesMe`, 1.0.27.0, ARM. Embedded `librespot.dll` is unchanged (SHA-256 `43598DCC36B4D2914C8305A6C8AD83106F3E06B9F4CBDAB1D0B1319FA06F059C`).
- MakeAppx extraction: PASS; 48 packaged native XAML URIs resolve in PRI, including Settings. SignTool reports the expected untrusted self-signed development root on this PC; the signer matches previous Lumia packages. The output folder includes ARM and other architecture dependency sidecars; use ARM dependencies only if Device Portal requests them.
- Lumia installation and locked playback test are pending. This package includes source-pause recovery but is not yet evidence that the lock interruption is fixed on device.
- Field result: on 1.0.27.0, immediate re-arm logged `source-starting`, but PCM requests and native producer/read counters then stalled after app resume while MediaPlayer still reported Playing. The 1.0.27 mitigation did not pass physical acceptance; see the 1.0.28 follow-up in `STABILITY_PLAN.md`.

## Resume-time MediaPlayer re-arm (1.0.28)

- Portable playback suite: PASS, including `PlatformPlaybackRearmsAfterResumeOnlyWhenRequested`; re-arm is limited to active `PlatformMedia` playback, and explicit pause/disposal/other backend cases are rejected. Login Helper source audit: SKIP because its sibling checkout is absent.
- ARM Release/.NET Native build and APPX: PASS, exit 0, with existing XAML/C# warnings. Signed from the Windows certificate store with the established development certificate (`CN=MegaBytesMe`, thumbprint `D12D4AD769E2DE1F8F0C82E58DD6533165658455`).
- Artifact: `LibreSpotUWP/AppPackages/LibreSpotUWP_1.0.28.0_ARM_Test/LibreSpotUWP_1.0.28.0_ARM.appx`, 11,341,041 bytes; SHA-256 `5844220496830510C4AB863DFC9397C11FD3928D98FD43F86F97303C56EB6B7D`. Identity/version/architecture: `34151aa6-2b17-4a37-bddb-2ee83fdf53e4`, `CN=MegaBytesMe`, 1.0.28.0, ARM. Native `librespot.dll` is unchanged at SHA-256 `43598DCC36B4D2914C8305A6C8AD83106F3E06B9F4CBDAB1D0B1319FA06F059C`.
- MakeAppx extraction and packaged XAML/PRI audit: PASS (48 routes, including Settings). The output directory includes ARM dependency sidecars for Device Portal if requested. Lumia validation remains pending; package generation is not evidence that locked playback is fixed.

## Locked recovery and delay follow-up (source after 1.0.26)

- `ProducerRecoveryRetriesTransientNetworkFailuresUntilReady`: PASS. Simulates repeated DNS/connect timeouts followed by successful recovery, verifies retryable versus non-retryable/cancelled failures, and asserts exponential backoff caps at 30 seconds and production no longer stops after three transient errors.
- `PlatformPcmTimelinePreservesFramesAndBoundaries`: PASS with an added 100-quantum track-boundary continuity check; the long-run source clock remains frame-derived with no cumulative boundary drift.
- Full portable suite: PASS (all simulated AudioGraph tests). Login Helper source audit: SKIP (sibling checkout absent).
- ARM Release/.NET Native build: PASS, 0 errors; unsigned package output was redirected to a unique temporary directory and did not overwrite or replace APPX 1.0.26.0.
- These follow-up source changes are not in APPX 1.0.26.0; the source-pause recovery is included in APPX 1.0.27.0, with its build and package verification recorded above. Device Portal evidence and the remaining physical/latency gates are recorded in [STABILITY_PLAN.md](STABILITY_PLAN.md#locked-playback-interruption-and-recovery-retry-follow-up-2026-10-19).

## PCM ring discriminator (1.0.24)

- Same-track resume regression: `SameTrackResumeKeepsPollingUntilPreRollIsReady` simulates an initial poll below the 200 ms pre-roll threshold followed by PCM arrival. It requires polling to remain scheduled while the playback-readiness task is pending, resume once pre-roll is available, and stop polling after readiness. Physical pause/play, repeated-cycle, lock and remote-control journey is specified in [STABILITY_PLAN.md](STABILITY_PLAN.md#same-track-pauseresume-failure-and-regression-journey-2026-10-19); this host test does not establish Lumia audio output or locked behavior.

- ARM Release managed/XAML compilation: PASS with existing warnings. Portable suite execution is blocked because this host has no .NET SDK.
- APPX packaging/signing: PASS. Built with the MSBuild package layout and signed with the existing development certificate (`D12D4AD769E2DE1F8F0C82E58DD6533165658455`). MakeAppx extraction passed and found the embedded APPX signature. SignTool reports only the expected untrusted self-signed root on this PC; the Lumia must already trust the same development certificate as for earlier builds.
- Artifact: `LibreSpotUWP/AppPackages/LibreSpotUWP_1.0.24.0_ARM_Test/LibreSpotUWP_1.0.24.0_ARM.appx`, 11,338,091 bytes signed, SHA-256 `6EBE8AE8B666282575AE9293F06B39B0C797BF95B1E50DEE6D46B44C75CBD669`. Identity `34151aa6-2b17-4a37-bddb-2ee83fdf53e4`, `CN=MegaBytesMe`, version 1.0.24.0, ARM. Packaged `librespot.dll` unchanged at SHA-256 `43598DCC36B4D2914C8305A6C8AD83106F3E06B9F4CBDAB1D0B1319FA06F059C`; no Rust rebuild.

- Added a five-second heartbeat snapshot correlating native write/generation sequences with managed read sequence and consumer gate; no per-sample logging or track/account data.
- Portable suite execution is blocked on this host because no .NET SDK is installed (`dotnet run` reports “No .NET SDKs were found”). Run after SDK restoration; ARM build and package integrity checks remain required before installing.
- Lumia reproduction captured with this signed 1.0.24 package: an external third-track handoff reached native generation 3 while managed `activeGeneration` stayed at 2; `writeSequence` and `readSequence` then froze with `consumerEnabled=True` and 128,640 buffered bytes beyond the boundary. Root cause was the ready-transition application being gated on the consumer already being disabled. The source now applies ready handoffs while the consumer remains enabled, with a regression test for that ordering. ARM Release/.NET Native build: PASS, zero errors. Signed 1.0.25.0 artifact: `LibreSpotUWP/AppPackages/LibreSpotUWP_1.0.25.0_ARM_Test/LibreSpotUWP_1.0.25.0_ARM.appx` (11,337,785 bytes; SHA-256 `A894389B232104848AEF305303B6872B52FB2B1508CAC83B53D31CD5FFCD9B20`); MakeAppx unpack: PASS (62 entries); identity and unchanged native DLL hash verified. Portable suite execution remains blocked because this host has no .NET SDK. Lumia retest across repeated remote track changes and lock/relock is pending; the correction is not yet device-validated. Full evidence and limits: [STABILITY_PLAN.md](STABILITY_PLAN.md#lumia-1024-reproduced-transition-failure-and-correction-2026-10-19).

## Live MediaStreamSource candidate (1.0.23)

- Portable playback suite: PASS, including source-contract audit that the unbounded real-PCM stream enables `IsLive` only when runtime API metadata confirms the property exists. Login Helper source audit: SKIP (sibling checkout absent).
- The IsLive property is documented from Windows 10 build 16299 / Universal API Contract v5. Its documented effect is live-source pipeline/timestamp handling; this is not a documented keepalive or lock-screen guarantee.
- WPR collection on the Lumia: FAILED before a trace was produced (`GeneralProfile.wprp` returned `Failed to start tracing`; Device Portal remained Idle). No causal ETW conclusion is recorded.
- ARM Release/.NET Native build: PASS; package created at `LibreSpotUWP/AppPackages/LibreSpotUWP_1.0.23.0_ARM_Test/LibreSpotUWP_1.0.23.0_ARM.appx` (11,336,380 bytes), SHA-256 `DEEBA7E38E307C459209853DE4FA720BC9BA2C7781A4425853568E91CB0F8C0A`.
- Package identity: PASS (`34151aa6-2b17-4a37-bddb-2ee83fdf53e4`, version 1.0.23.0, ARM, publisher `CN=MegaBytesMe`). Embedded `librespot.dll` hash remains `43598DCC36B4D2914C8305A6C8AD83106F3E06B9F4CBDAB1D0B1319FA06F059C`; no Rust rebuild. All 48 packaged native XAML URIs resolve in PRI, including Settings pages. MakeAppx extraction: PASS.
- APPX signature CMS integrity: PASS with the existing development signer `D12D4AD769E2DE1F8F0C82E58DD6533165658455`. SignTool trust-chain policy reports the expected untrusted self-signed development root; this is not production certificate trust.
- Lumia locked playback/control, multiple track boundaries, relock and soak: pending user validation; do not call the stability goal complete.
- Lumia 1.0.23 follow-up: target build `15254.603` logged that `MediaStreamSource.IsLive` is unavailable, so the guarded setter had no effect on this device. After one accepted external track handoff, PCM counters stopped at requests=1355, samples=1354, pending=1, reported availableBytes=0 and positionMs=26620 for >128 seconds while app heartbeats/process remained alive. No repeated track-load loop or unhandled exception was present in this interval; Device Portal crash-dump setting was initially disabled and no dump existed. It is now enabled for this package under the user's prior authorization. `availableBytes=0` is ambiguous because it also reports consumer-disabled; next diagnostic must correlate consumerEnabled with native write/generation and managed read sequences. Full field result and limits: [STABILITY_PLAN.md](STABILITY_PLAN.md#lumia-1023-field-result-2026-10-19).
- Acceptance steps and rationale/default-backend history: see [STABILITY_PLAN.md](STABILITY_PLAN.md#1023-live-mediastreamsource-semantics-candidate).

## Receiver-lock investigation package (1.0.22)

- ARM Release/.NET Native build: PASS. Artifact: `LibreSpotUWP/AppPackages/LibreSpotUWP_1.0.22.0_ARM_Test/LibreSpotUWP_1.0.22.0_ARM.appx`, 11,335,515 bytes, SHA-256 `B0B6A9E77110CB408845144EB3B7CE7425539F45737D87B9DFDF33A33BABB157`.
- Verification: PASS existing package identity and development signer, ARM/version/keepalive manifest, unchanged embedded native DLL, 48 native XAML URI/PRI mappings including all Settings pages, and MakeAppx extraction. Signature verification is cryptographic validity, not public certificate trust. Prior packages retained; temporary private signing PFX removed.
- Native transport/liveness probes: 4/4 PASS; XAML checker self-test: PASS. Review found no additional blocker in this focused diff; actual lock behavior remains unverified.
- Portable playback suite: PASS, including concurrent PCM diagnostic counters, redacted outstanding-work snapshot, close/idle ownership and production local-budget cancellation/cleanup wiring. The latter checks are source audits, not simulated WinRT execution. Login Helper source audit remains skipped because its sibling checkout is absent.
- Review: no per-sample log writes; diagnostic timer is disposed, does not request execution/network privileges and cannot establish audible output. No native rebuild, credential fallback, buffer/duration tweak or audio quality claim. Actual OS cancellation still ends the channel; a local diagnostic budget does not prove socket death.
- Device test and acceptance limits are specified in STABILITY_PLAN.md. The immediate lock silence is not marked fixed without that test.

## Dealer constructor regression (1.0.21)

- Generated ARM Release/.NET Native artifact: `LibreSpotUWP/AppPackages/LibreSpotUWP_1.0.21.0_ARM_Test/LibreSpotUWP_1.0.21.0_ARM.appx`, 11,331,168 bytes, SHA-256 `217343F4CBD03A313559C1B8271C15A11723120C797AA6D8A1E08E6864C8DC5C`.
- Package verification: PASS manifest identity/version/ARM/keepalive entrypoint, cryptographic signature with existing development signer, unchanged embedded native DLL, MakeAppx extraction and 48 native XAML URI/PRI mappings including all three Settings variants. Development signing is not public trust. Previous 1.0.20 package retained unchanged. Temporary signing PFX removed by build cleanup.
- Portable playback suite: PASS, including production keepalive constant (15..1439 minute contract), configuration HRESULT classification and source wiring. Login Helper source audit skipped because its sibling checkout is absent.
- Native transport probe: 2/2 PASS. Native liveness probe: 2/2 PASS. Native source and DLL unchanged.
- XAML URI self-test: PASS; generated ARM Release URIs: 43/43 PASS.
- Code review: checked constructor units/range, hardware-slot requirement, registration ordering, cancellation suppression, secret-free stage diagnostics and unchanged account/FFI/audio boundaries. No new blocking finding in this focused diff. Permanent-error retry suppression is not implemented; diagnostic classification does not change native retry behavior.
- Physical acceptance pending: hardware slot allocated, real transferred audio, continued remote authority while locked, natural track transitions and long soak. This fixes the confirmed invalid constructor argument, not a claim of completed background acceptance.

## PlatformMedia Mobile output policy regression

- Artifact generated: `LibreSpotUWP/AppPackages/LibreSpotUWP_1.0.20.0_ARM_Test/LibreSpotUWP_1.0.20.0_ARM.appx` (11,328,554 bytes). SHA-256: `B3BF06E0DD8A3861FC4CA883D377CA139A15FDD1F6839D8C67F04843A1EAB39A`.
- ARM Release/.NET Native build: PASS. Manifest version/identity/ARM/keepalive, existing development cryptographic signer and MakeAppx unpack: PASS. The signer is still self-signed development, not public production trust.
- Generated XAML URI validation: PASS (43). Packaged native executable/PRI mapping: PASS (48, all three Settings variants). New Mobile system-default checkpoint marker verified in the native executable.
- Embedded native DLL remains `43598DCC36B4D2914C8305A6C8AD83106F3E06B9F4CBDAB1D0B1319FA06F059C`. Existing 1.0.19.0 diagnostic package retained unchanged (`8C9F2D62E41E42A6A188A18D42964D4EFE68F72FAB13F2FF8A18D762F1C4DBF4`); earlier accepted-backend rollback packages retained. Temporary signing PFX removed after build.

- `MobilePlatformDefaultNeverTouchesAudioDeviceSetter`: PASS using the production policy. Mobile default/null/whitespace invokes no resolver/setter; Mobile explicit selection throws before either; desktop explicit output and reset-to-default remain functional. A source wiring check verifies that the renderer calls this policy with the actual Mobile predicate and requested output ID.
- Full portable playback suite: PASS. Native DLL unchanged; no Rust rebuild, DSP change or bit-perfect claim.
- Evidence from 1.0.19.0: three consecutive traced switch attempts terminate at the MediaPlayer output assignment boundary. OS dump list remains empty. Internal OS failure mechanism unproven; physical acceptance of the bypass and subsequent initialization remains required.
- Not deployed by the agent. Device gate: retain System default output, select Windows streaming, verify the default-route and renderer-ready checkpoints and actual audio, then restart once for a fresh authenticated WinRT Dealer and perform the locked-control matrix. A successful host build is not physical crash/background acceptance.

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
- `UnexpectedPlatformSourcePauseOnlyRecoversWhenPlaybackIsDesired` verifies a MediaStreamSource pause can re-arm MediaPlayer only during desired playback, and cannot undo explicit pause or source disposal; production wiring is also audited. This is a portable policy/source-wiring test, not a simulation of Windows 10 Mobile suspension.
- 2026-10-19 follow-up result: `dotnet run --project tests/LibreSpotUWP.Playback.Tests/LibreSpotUWP.Playback.Tests.csproj -c Release` passed all portable tests (Login Helper source audit skipped because its sibling checkout is absent). `MSBuild.exe LibreSpotUWP.sln /t:Build /p:Configuration=Release /p:Platform=ARM /p:AppxPackage=false /p:GenerateAppxPackageOnBuild=false /m /v:quiet` passed with exit 0; existing XAML/C# warnings remain. No APPX was generated. Lumia lock-screen verification is pending.
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

## Hi-Fi U1 implementation validation (2026-10-19)

- `dotnet run --project tests/LibreSpotUWP.Playback.Tests/LibreSpotUWP.Playback.Tests.csproj --configuration Release`: passed all registered portable tests, including `HiFiPlayerIsOptInAndReusesExistingPlaybackState`; Login Helper audit skipped because the sibling checkout is absent.
- ARM Release XAML resource validation passed all 44 generated application URIs, including `ms-appx:///Views/Win10_1709/HiFiPlayerPage.xaml`; managed C# compilation produced the ARM executable. The final .NET Native optimization stage did not complete in this turn, so there is no full native ARM build result for U1.
- A local Release build command unintentionally produced an unsigned, non-.NET-Native byproduct at `LibreSpotUWP/AppPackages/LibreSpotUWP_1.0.28.0_ARM_Test/LibreSpotUWP_1.0.28.0_ARM.appx` (SHA256 `D313799EF562193AA60AF46402D0148CFED2A5D5600771D0E85E9FADFDF30E2E`), overwriting the previously signed 1.0.28.0 artifact in that output path. Do not install or distribute this byproduct. No new signed device candidate was generated and no deployment was performed.
- Lumia rendered-layout, touch-target, rotation, artwork, transport and playback-continuity review: pending. R0 lock/background stability is still open; this UI work does not change that status.

## Hi-Fi lock-screen candidate (2026-10-19)

- Portable Release suite passed, including bounded wallpaper composition and the existing playback/control journey; Login Helper sibling audit skipped because that checkout is absent.
- The optional wallpaper scheduler is isolated from the media-state path and does no work without a current track or explicit opt-in. The on-app vinyl animation stops when its page unloads. Neither change is evidence that Windows 10 Mobile will keep the receiver alive while locked.
- Release|ARM .NET Native package 1.0.30.0: build and signing passed (exit 0); all 49 packaged native XAML URIs resolve. APPX identity `34151aa6-2b17-4a37-bddb-2ee83fdf53e4`, publisher `CN=MegaBytesMe`, architecture `arm`; existing development signer thumbprint `D12D4AD769E2DE1F8F0C82E58DD6533165658455`. Final APPX SHA-256 `E7137848CC2E5EA8A710AEE2ABA442B915E825A96E3011FB44205C441A8EFD41` (11,361,454 bytes). Physical Lumia validation pending: cover display with the opt-in enabled, unchanged transport controls, then controller and Lumia lock/unlock cycles and long-running remote control. Capture redacted diagnostics immediately if Connect drops; do not close R0 based on a wallpaper/UI test.
- 1.0.31.0 visibility correction: the Release portable suite passed, including a guard that the lock-screen artwork follows a fresh Hi-Fi preference while preserving explicit off and that the composed background responds to album color. ARM Release/.NET Native build and signing passed; all 49 packaged XAML URIs resolve. Identity/publisher/architecture remain unchanged; signed APPX SHA-256 `35EEFC43EB90965A418EBB66BA439C96F45567DAC3AB417D977C2DDD9C43F1FB` (11,361,337 bytes). No device installation or wallpaper API result is claimed. On Lumia, confirm the background changes behind the unchanged Windows controller, then check long-running playback and locked remote control separately.
- 1.0.32.0 layout refinement: Release portable suite passed after moving the centered cover upward and asserting that the former vinyl area is symmetric background. ARM Release/.NET Native build, development signing, and 49 packaged XAML URI checks passed. Package identity/publisher/architecture unchanged; signed APPX SHA-256 `9DC9B4F0804127F2EEBEDA87374EF6D2E90F47F947EE3D817C6F1B21C60F648D` (11,361,559 bytes). Physical placement relative to the Lumia's lower clock remains unverified until installed.

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


## 2026-10-19 — Playback recovery fix package 1.0.33.0

- Release|ARM .NET Native package built successfully from the current workspace as an ARM32 UWP package. Version bumped to 1.0.33.0; package identity/publisher remain `34151aa6-2b17-4a37-bddb-2ee83fdf53e4` / `CN=MegaBytesMe`.
- APPX: `LibreSpotUWP/AppPackages/LibreSpotUWP_1.0.33.0_ARM_Test/LibreSpotUWP_1.0.33.0_ARM.appx` (11,363,150 bytes), SHA-256 `16D3ADEFA81218EFC564253D97989FE3A797BC86D1485DB5885AF5252E6EA79E`.
- Signed using the existing development certificate from the CurrentUser certificate store (thumbprint `D12D4AD769E2DE1F8F0C82E58DD6533165658455`); only the public `.cer` is included beside the package. SignTool identifies the same signer but `/pa` on this PC reports its self-signed root as untrusted. This is a development sideload artifact, not a production-trusted signature.
- MakeAppx extraction passed; manifest confirms ARM, version 1.0.33.0, and unchanged identity/publisher. The packaged `librespot.dll` hash matches the local ARM DLL (`43598DCC36B4D2914C8305A6C8AD83106F3E06B9F4CBDAB1D0B1319FA06F059C`). The packaged XAML/PRI audit passed for all 49 native XAML URIs.
- The complete portable playback suite and managed ARM build passed before packaging. The APPX has not been installed on the Lumia; lock/relock, remote-control, network-recovery and long-run behavior remain physical-device checks.
## 2026-10-19 — Streaming Hi-Fi name and receiver artwork (1.0.34.0)

- User-visible package, tile, protocol and About-page name changed to `Streaming Hi-Fi`; internal package identity, namespaces, assembly name, Spotify ownership boundaries and receiver device name are unchanged. Package version advanced from 1.0.33.0 to 1.0.34.0.
- Replaced the 50 square, target-size, wide-tile and splash artwork variants with resized versions of the supplied 2048×2048 receiver illustration. The wide tile and splash center the full receiver on a matching charcoal background. Small square app-list variants use a transparent rounded-corner mask. PNG palette optimization reduced the image set to about 2.6 MB; the 400% large-tile asset uses a 64-color, lightly softened rendition to satisfy the UWP 204,800-byte maximum (final file 131,706 bytes). Original user-supplied art is preserved at `docs/artwork/StreamingHiFiReceiver.jfif`.
- Portable playback suite passed. ARM Release/.NET Native package generation passed. MakeAppx extraction confirmed name, version, ARM architecture and all sampled artwork files; packaged native DLL hash matches the source DLL. The 49-URI XAML/PRI package audit passed.
- APPX: `LibreSpotUWP/AppPackages/LibreSpotUWP_1.0.34.0_ARM_Test/LibreSpotUWP_1.0.34.0_ARM.appx` (12,367,009 bytes); SHA-256 `4753E68DB172A0AB7D1A155F5FDB4CC8EFBF3F5FDDC461DDB84E288E007297BE`.
- Signed by the existing development certificate (thumbprint `D12D4AD769E2DE1F8F0C82E58DD6533165658455`); public certificate is beside the APPX. SignTool reports the self-signed root as untrusted by this PC's policy, as with earlier development packages. This is a development sideload package, not a production-trusted signature. No Lumia installation or physical visual inspection has been done.
## 2026-11-02 — Home Assistant link candidate (1.0.35.0, WP5–WP7)

Built on Fedora only. Passed off-device: the portable host suite (139 checks, including the link session, retry policy, settings validation and a static audit of the WinRT wiring), the WinRT type check (`dotnet build tests/WinRtTypeCheck`, C# 7.3 against SDK 10.0.19041 metadata), 130 integration tests and the podman smoke test. **Not yet run:** the ARM Release/.NET Native build, XAML compilation, signing, and anything on the Lumia or the homelab. Nothing below is verified until recorded here with evidence.

### Build gate (Windows workstation)

1. `msbuild .\LibreSpotUWP.sln /restore /t:Build /p:Configuration=Release /p:Platform=ARM` from `feature/ha-link`, then `tools/Test-GeneratedXamlUris.ps1` against `obj/ARM/Release` and the APPX (the three Settings variants changed).
2. Record the APPX SHA-256, size, identity, publisher and architecture as for earlier packages. Keep the 1.0.34.0 APPX as the rollback package before installing.
3. Regression first, with the Home Assistant switch **off** (default): baseline playback, Connect transfer, lock/unlock. Confirm the log has no `Home Assistant link` lines other than task cleanup.

### Home Assistant install (homelab — requires the owner's explicit approval)

1. Check the homelab Home Assistant version (the integration is tested on 2026.10.4).
2. Copy `integrations/home_assistant/custom_components/lumia_hifi` to the HA config `custom_components/`, restart, add "Lumia HiFi". Note the address shown; if it is not reachable from the phone (container address), use the homelab LAN address with port 8123 instead.
3. Rollback: delete the config entry, remove the directory, restart.

### WP0 + WP7 matrix (Lumia + homelab)

| Case | Steps | Pass criterion |
|---|---|---|
| WP0 standby slot | Enable the link; read the Settings status line and the `ha-link-standby-slot` telemetry | Records `hardware slot`, `software slot` or the failure. Hardware slot: continue with the locked cases. Otherwise the locked-idle row is a documented unmet requirement. |
| Pairing | Enter address + secret, Save and connect | Status "Connected."; HA entity available within 5 s |
| Wrong secret | Enter a wrong secret | Status "rejected the secret"; no retry storm in the log; HA logs one failed attempt |
| Foreground playback | Play from a controller | Entity title/artist/album/artwork correct within 1 s of each track change |
| Each command from HA | play, pause, next, previous, seek, volume, shuffle, repeat | Effect within 1 s (unlocked); controller phone reflects it; one ack per command |
| Guest account session | Second account transfers to the Lumia | Entity follows; no account name or id visible in HA state or diagnostics |
| Locked, playing, 30 min | Lock the Lumia during playback | State keeps updating; commands work |
| Locked, idle 15/60 min | Pause, lock, wait, send play from HA | Command wakes the link (`ha-link-background-wake`) or documented unmet per WP0 |
| Wi-Fi drop/return | Toggle Wi-Fi on the phone | Entity unavailable while down; reconnects without user action after return |
| HA restart | Restart Home Assistant | Link returns within the backoff (≤ 5 min, usually seconds) |
| Phone/app restart | Restart the app, then the phone | Link returns after launch; switch state persisted |
| Secret rotation | Rotate in HA options | Phone shows "rejected the secret" and stops; works after entering the new one |
| Switch off | Turn the Settings switch off | Socket closed, HA entity unavailable, no `LibreSpotUWP.HaLink.*` background tasks remain |
| Battery | 2 h idle with link on vs off | Drain difference recorded |

Known risk to check first: Windows 10 Mobile must accept `MessageWebSocket.SetRequestHeader("Authorization", ...)`. If the connect fails with an argument/header HRESULT, record it; the fallback is a contract change, not a workaround in the app.
