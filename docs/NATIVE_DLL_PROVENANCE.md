# ARM32 UWP native DLL

`LibreSpotUWP/librespot.dll` is the ARM32 UWP native library used by this checkout.

- Native source revisions: `3195733` (`fix(discovery): advertise routable LAN address`) and `08f192c` (`fix(ffi): preserve managed DJ ABI`). The latter keeps the managed `librespot_dj_next_set` import valid without enabling unsupported DJ commands in this base.
- Target: `thumbv7a-uwp-windows-msvc`.
- Toolchain: `nightly-2026-09-29` with `rust-src` and `-Z build-std=std,panic_abort`.
- Windows SDK libraries: `10.0.19041.0` ARM (`um` and `ucrt`).
- Features: `native-tls uwp-backend with-libmdns`.
- SHA-256: `700D10ED8C575E427BD401B14AA8E5AB21C4B21C635969583426CAD1FF37572E`.

## Compatibility validation

`dumpbin` confirms the result has PE machine `0x01C4` (ARM), the AppContainer DLL characteristic, and Windows GUI subsystem. It imports UWP-facing API-set libraries plus `ws2_32`, `crypt32`, `secur32`, `xaudio2_8`, and `vcruntime140_app`.

The managed host declares 51 `librespot_*` imports; the DLL exports all 51, including `librespot_dj_next_set`.

## Reproducibility note

The current `windows-sys` 0.60.2 and 0.61.2 generated bindings omit four required 32-bit ARM definitions (`SERVENT`, `WSADATA`, `MEMORY_BASIC_INFORMATION`, and `DELAYLOAD_INFO`) even though their UWP APIs are linked by the dependency graph. The local vendor copies under `../librespot-vendor/` add `target_arch = "arm"` to the equivalent 32-bit definitions. `../.cargo/uwp-windows-sys-patch.toml` selects those sources for the build.

The ARM linker also requires the 10.0.19041.0 `um\arm` and `ucrt\arm` directories explicitly in `LIB` and `/LIBPATH`; `deps.ps1` prepares these paths but they are not reliably preserved through every Cargo/rustc invocation. See `BUILD_GUIDE.md` for the canonical command. This avoids the misleading `LNK1181: mincore.lib` failure.

This is a binding-generation correction only: it retains the ARM/UWP target and produces an AppContainer binary. Do not replace it by building a desktop ARM target or by using an x86/x64 DLL on Windows 10 Mobile.
## 1.0.16.0 background-control candidate — 2026-10-18

Supersedes the earlier binary provenance above for the current local/package DLL. The generated DLL remains ignored by Git; source provenance and the patch are tracked instead.

- Native commit: `b62320452eb02621f0c2315cdabaae8ea412c5fd`, based on `9f55482`.
- Source delta: `../native-patches/0001-uwp-background-control.patch`. The native origin belongs to a third party and was **not pushed**. The application fork carries the patch; native Cargo.lock/deps.ps1 local changes were not included in this commit.
- SHA-256: `BECC14EC3F82D3B21ECA132216CC2824DB806146A3B130DE4BE7B58D296AC294`.
- ARM32 UWP/AppContainer verified; all 53 managed imports exported (two additive Dealer transport exports). Existing feature set and SDK patches unchanged.
- The bridge is opt-in: without registered callbacks the portable Dealer transport remains in use. Authentication, Spotify protocol processing and credential ownership remain native.
- See TEST_PLAN.md for host checks and STABILITY_PLAN.md for unverified physical acceptance. This is not a claim of certified Windows Mobile background behavior.

## Network-work lifetime follow-up (not packaged)

The current local `LibreSpotUWP/librespot.dll` now comes from native HEAD `f51b24e882f69ffdf91f95c05885222577eb7e7b`. Apply `0001` then `0002-uwp-network-work-lifetime.patch` to baseline `9f55482`. The native commits remain local; no third-party-origin push was made.

- SHA-256: `43598DCC36B4D2914C8305A6C8AD83106F3E06B9F4CBDAB1D0B1319FA06F059C`.
- Final offline ARM32 UWP release build: PASS. PE machine `0x01C4`, AppContainer flag and all 53 managed FFI exports verified before copying into the application directory. Existing features/SDK binding patches unchanged.
- The previous local DLL was backed up to a unique temporary file before replacement. The already-issued 1.0.16.0 APPX retains its original DLL and original package hash; no new APPX was generated.
- Requires the matching managed startup/PCM changes. This validates compilation and binary compatibility, not physical Lumia standby behavior or .NET Native packaging of the follow-up.
