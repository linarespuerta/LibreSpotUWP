# Lumia HiFi roadmap

Goal 5 is reopened after a new 1.0.15.0 report: locking the controller phone loses remote control while the Lumia keeps playing. R0 diagnosis/correction and owner/guest controller-lock acceptance are the first functional priority of the next delivery; see [STABILITY_PLAN.md](STABILITY_PLAN.md). Goal 2 remains planned in [HIFI_UI_PLAN.md](HIFI_UI_PLAN.md), with R0 required before accepting the combined UI candidate. Locked idle discovery and long-run qualification remain separate pending checks.

## M0 — Reproducible baseline

- Objective: establish a repeatable ARM32 baseline.
- Technical changes: none to production code; pin Rust revision and record toolchain/artifact hashes.
- Dependencies: Windows UWP/ARM32 toolchain and native source access.
- Risks: SDK/mobile incompatibility; ABI mismatch; unsigned package.
- Validation: clean ARM Release build and package inspection.
- Acceptance criteria: package contains ARM32 `librespot.dll`, is signed for sideloading, and deploys without replacing the working installation until approved.
- Rollback: retain the prior APPX and do not uninstall it before a backup is available.

## M1 — Discovery evidence and opt-in MVP

- Objective: make only the proven native discovery capability configurable.
- Technical changes: inspect Rust; add opt-in setting, capability if compatible, diagnostics, and `enable_discovery` wiring.
- Dependencies: M0 and Rust source audit.
- Risks: UWP socket restrictions, unusable advertisement, Spotify protocol mismatch.
- Validation: avahi browse, TCP/HTTP handshake, and two-account physical tests.
- Acceptance criteria: all Phase 3 criteria in the project prompt pass on the Lumia.
- Rollback: disable the flag or reinstall the baseline APPX.

## M2 — Dedicated Now Playing

- Objective: appliance-style AMOLED player without changing transport behavior.
- Technical changes: focused Lumia XAML view, cached art, state binding, dimming/burn-in mitigation behind settings.
- Dependencies: stable M1 or an independently stable current playback baseline.
- Risks: UI memory/CPU pressure and audio disruption.
- Validation: sustained playback plus navigation, screen-on, and track-change profiling.
- Acceptance criteria: responsive touch controls and no observed playback regression.
- Rollback: retain the previous player view behind a feature flag until device testing passes.

## M3 — Home Assistant contract

- Objective: a local, secure media-player integration design validated without deployment.
- Technical changes: define redacted status/control API and companion prototype if direct UWP hosting is unreliable.
- Dependencies: M1 state/event diagnostics.
- Risks: LAN reachability, authentication, stale state.
- Validation: contract tests and isolated Docker Compose test environment.
- Acceptance criteria: Home Assistant entity reports state, metadata, volume and supported controls.
- Rollback: stop/remove only the companion service; no HA configuration mutation without approval.

## M4 — Audio and reliability hardening

- Objective: improve measured stability and signal-path transparency.
- Technical changes: add metrics and only justified buffer/recovery/configuration changes.
- Dependencies: M0 physical build and long-running device access.
- Risks: clicks, resampling, battery/thermal impact, credential/session loss.
- Validation: extended playback, Wi-Fi interruption, restart, and format tests.
- Acceptance criteria: no unverified bit-perfect claim; documented measured behavior and recovery results.
- Rollback: restore established settings/package and preserve logs with secrets redacted.
