# ARM32 UWP native DLL

`LibreSpotUWP/librespot.dll` is the ARM32 UWP native library used by this checkout.

- Native source revision: `f81dd0893544191eb23c8f1b48b4f8119e454547` (`Added Spotify DJ next vibe support`), matching the current managed import `librespot_dj_next_set`.
- Target: `thumbv7a-uwp-windows-msvc`.
- Toolchain: `nightly-2026-09-29` with `rust-src` and `-Z build-std=std,panic_abort`.
- Windows SDK libraries: `10.0.19041.0` ARM (`um` and `ucrt`).
- Features: `native-tls uwp-backend with-libmdns`.
- SHA-256: `1E4457A9A3659CAB75763ABB84B37BAE1FA23A57EC482AB77F58D27930CA5DA8`.

## Compatibility validation

`dumpbin` confirms the result has PE machine `0x01C4` (ARM), the AppContainer DLL characteristic, and Windows GUI subsystem. It imports UWP-facing API-set libraries plus `ws2_32`, `crypt32`, `secur32`, `xaudio2_8`, and `vcruntime140_app`.

The managed host declares 51 `librespot_*` imports; the DLL exports all 51, including `librespot_dj_next_set`.

## Reproducibility note

The current `windows-sys` 0.60.2 and 0.61.2 generated bindings omit four required 32-bit ARM definitions (`SERVENT`, `WSADATA`, `MEMORY_BASIC_INFORMATION`, and `DELAYLOAD_INFO`) even though their UWP APIs are linked by the dependency graph. The local vendor copies under `../librespot-vendor/` add `target_arch = "arm"` to the equivalent 32-bit definitions. `../.cargo/uwp-windows-sys-patch.toml` selects those sources for the build.

This is a binding-generation correction only: it retains the ARM/UWP target and produces an AppContainer binary. Do not replace it by building a desktop ARM target or by using an x86/x64 DLL on Windows 10 Mobile.
