# Test plan

The local/desktop boundary is documented in [LOCAL_TESTING.md](LOCAL_TESTING.md). Desktop results must not be reported as Lumia hardware acceptance.

## Automated and static checks

- Restore/build `Release|ARM` on the pinned Windows toolchain.
- Verify C# `LibrespotConfig` layout against the exact Rust FFI definition for ARM32, including Boolean size, enum widths, pointer alignment and callback lifetime.
- Run existing playback tests and smoke scripts only after dependencies are restored; record commands and outcomes.
- Inspect APPX contents, manifest capabilities, DLL machine type, signature, and absence of secrets.

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

