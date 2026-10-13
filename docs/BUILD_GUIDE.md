# Reproducible build guide

## Current audit result

This workstation now has Visual Studio Build Tools, the Windows 10 SDK, Rust nightly, an ARM32 UWP `LibreSpotUWP/librespot.dll`, and a local development signing certificate. Its provenance and binary validation are recorded in [NATIVE_DLL_PROVENANCE.md](NATIVE_DLL_PROVENANCE.md).

The project targets UAP SDK `10.0.19041.0` with a project minimum of `10.0.16299.0`. Its manifest independently declares device-family minimum `10.0.15063.0` and maximum tested `10.0.17763.0`. Those version declarations must be reconciled deliberately before a Lumia deployment; they are not evidence that the current source will run on an earlier OS.

## ARM host build result (2026-10-12)

The command below was run against the checked-out solution:

```powershell
msbuild .\LibreSpotUWP.sln /t:Build /p:Configuration=Release /p:Platform=ARM /m
```

The C# host, XAML, .NET Native ARM compilation, package layout, and every P/Invoke binding to `librespot.dll` completed successfully. The first package attempt stopped at signing because the checkout did not contain a PFX (`APPX0104`/`APPX0107`). A local development certificate with the required Code Signing EKU was then created and excluded by `.gitignore`. The signed ARM package now builds successfully.

Validated baseline package (2026-10-12):

- Path: `LibreSpotUWP\\LibreSpotUWP\\AppPackages\\LibreSpotUWP_1.0.5.0_ARM_Test\\LibreSpotUWP_1.0.5.0_ARM.appx`
- Architecture: ARM32
- SHA-256: `6C1DCBF675A2DB5B219563EC0214FD624F703F3CD5BF43BACF13729047FECDF4`
- Publisher identity: `CN=MegaBytesMe`
- Certificate: local self-signed development certificate; it is not a Microsoft Store certificate and must be trusted on the target device before sideloading.
- Public certificate for sideload preparation: `LibreSpotUWP\\LibreSpotUWP\\AppPackages\\LibreSpotUWP_1.0.5.0_ARM_Test\\LibreSpotUWP_Development.cer`

The PFX private key is intentionally never committed. Rebuilds that package/sign must provide the local certificate password through the MSBuild property `PackageCertificatePassword`; do not put that password in source control or documentation.

Current update package after the version increment is `LibreSpotUWP\\LibreSpotUWP\\AppPackages\\LibreSpotUWP_1.0.6.0_ARM_Test\\LibreSpotUWP_1.0.6.0_ARM.appx`.

The 1.0.6.0 package was installed successfully on the Lumia through Windows Device Portal. The app reported version `1.0.6.0`, retained the existing authenticated session and user data, and launched normally. Discovery remained disabled in that baseline package.

The next discovery MVP package is version `1.0.7.0` and adds a default-off local discovery setting.

Discovery MVP package (built locally, not yet physically validated):

- Path: `LibreSpotUWP\\LibreSpotUWP\\AppPackages\\LibreSpotUWP_1.0.7.0_ARM_Test\\LibreSpotUWP_1.0.7.0_ARM.appx`
- Architecture: ARM32
- SHA-256: `2F4F403CA705CEC03B134C68B19BAA4720F1762C2A55B9EED873C81C2FAA2CE2`
- Local package contents include `AppxManifest.xml`, `librespot.dll`, and `AppxBlockMap.xml`.

## Required Windows host toolchain

- Visual Studio with **Universal Windows Platform development** and **Desktop development with C++** workloads.
- Windows 10 SDK `10.0.19041.0` with ARM build tools, matching the project target. Do not retarget to an older SDK until compatibility has been reviewed.
- Windows 10 Mobile ARM tooling and .NET Native/UWP build tools.
- Rust nightly and `rust-src` required by `-Z build-std=std,panic_abort`. `thumbv7a-uwp-windows-msvc` is a built-in Rust target, but is not shipped as a precompiled Rustup target; the build creates its standard library from `rust-src`.
- The exact `megabytesme/librespot` revision used by the host, including dependencies restored through its `deps.ps1`.

## Native build

From the compatible Rust fork, after reviewing its dependency script and revision:

```powershell
.\deps.ps1
cargo +nightly build -Z "build-std=std,panic_abort" --target thumbv7a-uwp-windows-msvc --release --no-default-features --features "native-tls uwp-backend with-libmdns"
```

`with-libmdns` is required for the discovery MVP. The README's previous command omitted it while also using `--no-default-features`, producing a DLL with no discovery backend. Copy only the resulting ARM32 release `librespot.dll` into `LibreSpotUWP/LibreSpotUWP/` for the local package build. Record its Rust commit, SHA-256, Cargo.lock hash, target triple, and command in build artifacts; do not commit a DLL unless project policy explicitly adopts reproducible binary artifacts.

## UWP build and package

Restore packages, select `Release|ARM`, then build/package with MSBuild or Visual Studio. Confirm that the manifest target and project min-version are compatible with the Lumia OS. The project enables .NET Native for ARM Release and references a PFX that is absent from the checkout; create or select a user-controlled development certificate before packaging. Back up the installed APPX and data/session rollback plan before sideloading. Validate package architecture, signature, DLL architecture, and device deployment through Windows Device Portal.

## Host preflight (validated 2026-10-12)

Visual Studio Build Tools 2022, MSBuild, the Windows 10 SDK `10.0.19041.0`, Rust nightly, and `rust-src` are installed on this workstation. Rust nightly confirms that it recognizes `thumbv7a-uwp-windows-msvc`. The native ARM32 DLL and local development certificate are present and validated. The installed `dotnet` runtime is not an SDK and is not used to build this legacy UWP project. See [LOCAL_TESTING.md](LOCAL_TESTING.md) for the desktop-versus-Lumia test boundary.

Before attempting a build, validate the installed Visual Studio workloads and command-line build tools in a Developer PowerShell:

```powershell
msbuild .\LibreSpotUWP.sln /restore /t:Build /p:Configuration=Release /p:Platform=ARM
```

Only run that command after placing a DLL built from the pinned native revision at `LibreSpotUWP\librespot.dll` and selecting a development certificate. For daily code changes, use an incremental `Debug|ARM` or `Release|ARM` build and package only when an installable APPX is needed. A successful host build does not validate Windows 10 Mobile deployment or Spotify Connect discovery; those are separate hardware gates.

## CI recommendation

Use a Windows runner with the required VS/SDK image pinned by version. Cache NuGet/Cargo only by lockfile hash, build ARM Release, emit an unsigned artifact for inspection, and keep signing keys outside source control. Hardware deployment remains a manual gated stage.
