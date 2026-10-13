# Spotify Connect discovery audit

## Current implementation status (2026-10-12)

The 1.0.7.0 discovery MVP now exposes a persisted `SpotifyLocalDiscoveryEnabled` setting, disabled by default, on all settings-page variants. `LibrespotService.BuildConfig` passes that value to the native `enable_discovery` field. The UI warns that a restart is required because the native listener is created during service startup. The 1.0.6.0 package was physically installed and regression-tested on the Lumia before this change; discovery itself still requires the next ARM package and LAN validation.

## Lumia LAN diagnosis (2026-10-12)

With 1.0.7.0 enabled, Ubuntu observed the Lumia as `Marantz Sala` with a valid `_spotify-connect._tcp` record, but mDNS resolved it to `169.254.63.27:50007`. Neither that address nor the Device Portal address `192.168.10.28` accepted the HTTP `getInfo` request, so the advertisement was not yet reachable by a LAN client. This is not a successful discovery acceptance test: mDNS publication works, while the advertised address/listener path remains unresolved.

Native commit `3195733` now selects the routable address chosen by a UDP route probe before registering the libmdns A record, logs the selected address, and preserves discovery startup errors instead of discarding them. It passes host `cargo check -p librespot-discovery --features with-libmdns`; an ARM package is pending because the current ARM build is blocked in `windows-sys` by missing generated WinSock/Windows type definitions.

## Confirmed root cause

The managed configuration explicitly disables discovery:

```csharp
// Services/LibrespotService.cs, BuildConfig
    enable_discovery = UserSettings.SpotifyLocalDiscoveryEnabled,
```

The same struct exposes this field in `Interop/LibreSpot.cs`, so it reaches the native ABI. The package now declares `privateNetworkClientServer` and `internetClientServer`; the latter is required when the Lumia's active network profile prevents inbound Connect traffic under the private-network capability alone.

This explains the observed absence of `_spotify-connect._tcp` records and the absence of an open Connect listener. It does **not** prove that merely setting the Boolean to `true` will work on Windows 10 Mobile.

## Native audit: pinned revision verified

The application links to revision `2c58d7db09607105e17769ec41c210eff23a8333` of `megabytesme/librespot`. Its Rust `#[repr(C)] LibrespotConfig` has the same ordered discovery field as the managed struct (`enable_discovery` immediately after the four string pointers), and `parse_ffi_config` copies that Boolean to `RunnerSetup.enable_discovery`. This verifies the intended FFI path for the checked revision; it does not validate binary layout on ARM32, which still requires an ABI smoke test.

When `enable_discovery` is true, `Runner::run` creates `librespot_discovery::Discovery` with the configured device ID, client ID, name, type, and port `0`. The discovery server binds an ephemeral TCP port on `0.0.0.0` on Windows, advertises `_spotify-connect._tcp` with `VERSION=1.0` and `CPath=/`, and serves the required `GET?action=getInfo` and `POST?action=addUser` paths. It has graceful listener and responder shutdown handles.

### Build-feature blocker

The README's ARM build command is:

```powershell
cargo +nightly build -Z "build-std=std,panic_abort" --target thumbv7a-uwp-windows-msvc --release --no-default-features --features "native-tls uwp-backend"
```

Because it uses `--no-default-features`, it excludes the root crate's default `with-libmdns` feature. The configured feature list restores `native-tls` and `uwp-backend` only; it contains **no discovery backend**. `Discovery::launch()` therefore returns “librespot built without zeroconf backends”, while the runner discards that error with `.launch().ok()`. This is a confirmed second root cause: changing only the C# Boolean cannot advertise an endpoint with a DLL built by the documented command.

The checked Rust manifest calls `with-libmdns` the pure-Rust, all-platform mDNS backend. That is a source-level compatibility claim, not a successful UWP/ARM32 build or device test.

## Remaining native uncertainty

The host README pins the Rust dependency to `https://github.com/megabytesme/librespot` and UI source links to commit `2c58d7db09607105e17769ec41c210eff23a8333`. On 2026-10-12, Git object metadata for that repository was retrieved and identified its current `dev` tip as `f81dd0893544191eb23c8f1b48b4f8119e454547` (`Added Spotify DJ next vibe support`). Its repository tree confirms a dedicated `discovery` crate with `discovery/src/lib.rs`, `discovery/src/server.rs`, `discovery/src/avahi.rs`, and `discovery/Cargo.toml`.

The source blobs for the pinned revision were subsequently available through the local Git object database and were inspected. An attempt to hydrate the newer `dev` checkout still failed because the local proxy at `127.0.0.1:9` rejected the connection; the newer `dev` tip is not used as evidence here.

### Native-audit acquisition gate

Before implementation, obtain a complete local checkout of the exact `2c58d7db09607105e17769ec41c210eff23a8333` revision (including blobs and submodules, if any), record `git rev-parse HEAD`, and preserve its `Cargo.lock`. Do not substitute the current `dev` tip merely because it has a discovery crate: doing so could silently change the FFI ABI or the UWP dependency set.

The source review resolves the implementation questions above. Remaining validation is empirical:

1. Build the pinned revision for `thumbv7a-uwp-windows-msvc` with `with-libmdns` enabled.
2. Confirm the resulting ARM32 DLL exports and accepts the managed config without an ABI fault.
3. Verify `libmdns` can bind and advertise on an actual Windows 10 Mobile LAN interface.
4. Verify the listener is reachable and the Spotify client completes the `getInfo` / `addUser` handshake.
5. Capture discovery startup failure in a diagnosable way; the current runner intentionally loses the launch error.

## Smallest safe MVP

1. Add a default-off `LocalDiscoveryEnabled` setting and a clear settings warning.
2. When enabled, set `enable_discovery` from that setting; do not change login/session storage.
3. Build the pinned native revision with `--features "native-tls uwp-backend with-libmdns"`; pin and record the result. Do not rely on the existing documented feature set.
4. Add `privateNetworkClientServer` to the manifest only after confirming it is supported by the target package/minimum OS.
5. Preserve the native discovery launch error in redacted diagnostics instead of converting it to `None` silently; include requested state, native start outcome, advertised service name/port/address, and shutdown outcome.
6. Test on a Lumia before calling the feature functional.

## Required physical validation

From a LAN Linux host run `avahi-browse -rt _spotify-connect._tcp`, inspect TXT records, and connect to the advertised endpoint. Verify discovery and transfer from a second Premium Family account, then regression-test the already-authenticated account. Capture no tokens in logs. Failure modes to distinguish are: no advertisement, advertisement without TCP reachability, invalid HTTP handshake, cross-account authentication rejection, and audio/session failure after transfer.
