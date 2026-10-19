# Reproducible build guide

## XAML resource-root regression prevention

The 1.0.17.0 package passed signing/native checks but failed physical Settings navigation. The generated Settings `InitializeComponent` methods requested `ms-appx:///LibreSpotUWP/Views/...`, while the package PRI contained only `Files/Views/...`. The 1.0.15.0 native executable used the correct `ms-appx:///Views/...` URIs. All three Settings variants were affected; the other compiled resource entries were unchanged. This is a generated resource-root mismatch, not evidence of an audio, credential or missing-dependency failure. The exact invocation that first contaminated the incremental cache was not conclusively established.

For recovery, preserve issued APPX packages and move only the exact generated `LibreSpotUWP/obj/ARM/Release` and `LibreSpotUWP/bin/ARM/Release` directories to a unique temporary backup. Verify resolved paths remain under the project before moving them. Do not edit `.g.i.cs` files by hand, delete a workspace root or rebuild the same issued package version with different contents.

Run the canonical build **from the directory containing LibreSpotUWP.csproj**, using `/t:Build` and the existing signing workflow. Never invoke `/t:Compile` directly as a UWP build shortcut. Keep the verified native DLL; this managed-resource fix does not require rebuilding Rust.

`ValidateGeneratedApplicationXamlUris`, attached after `MarkupCompilePass1`, runs `tools/Test-GeneratedXamlUris.ps1` before native compilation/packaging. It fails if a generated application XAML URI does not resolve under the project source root or no URIs were inspected. Run its regression checks from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Test-GeneratedXamlUris.ps1 -SelfTest
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Test-GeneratedXamlUris.ps1 -GeneratedRoot obj/ARM/Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Test-GeneratedXamlUris.ps1 -PackagePath LibreSpotUWP/AppPackages/LibreSpotUWP_1.0.18.0_ARM_Test/LibreSpotUWP_1.0.18.0_ARM.appx
```

The self-test accepts all three correct Settings URIs and rejects their duplicated-project-prefix counterparts. Package mode extracts PRI metadata with MakePri and compares the **packaged native executable's** `ms-appx:///...xaml` strings against PRI `Files/...xbf` entries, explicitly requiring all three Settings variants. Running it against 1.0.17.0 must fail for the three incorrect prefixes; use this as a negative regression check. Signing, MakeAppx extraction and desktop tests alone do not validate navigation on Windows Mobile.

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

The next discovery MVP package was version `1.0.7.0` and added a default-off local discovery setting.

The first 1.0.7.0 LAN test published the Lumia as `Marantz Sala`, but advertised the unreachable link-local address `169.254.63.27:50007`; HTTP `getInfo` timed out. Native commit `3195733` contains the lean address-selection and startup-diagnostics fix. Package `1.0.8.0` contains native commits `3195733` and `08f192c`.

Package `1.0.9.0` adds the standard UWP `internetClientServer` capability alongside `privateNetworkClientServer`. This is a managed-package-only fix for LAN clients that can see mDNS but cannot open the advertised Connect TCP port; the native DLL is not rebuilt. Verify the advertised port with `avahi-browse -rt _spotify-connect._tcp`, then test it from the LAN with `nc -vz -w 3 <lumia-ip> <port>`.

Validated on the Lumia (`192.168.10.28`): TCP connection succeeds and `GET /?action=getInfo` returns HTTP 200 with `status: 101`, `statusString: "OK"`, and `remoteName: "Marantz Sala"`. A second Spotify Premium account also discovers the device. If the app is suspended or closed, its dynamic mDNS/TCP endpoint disappears; keep LibreSpot running (screen on for this current UWP lifecycle) while testing Connect.

Discovery MVP package (built locally, not yet physically validated):

- Path: `LibreSpotUWP\\LibreSpotUWP\\AppPackages\\LibreSpotUWP_1.0.9.0_ARM_Test\\LibreSpotUWP_1.0.9.0_ARM.appx`
- Architecture: ARM32
- SHA-256: `C10C910910341E18F55EF8E87C598D755F76415C8F13AB3064B8BF3AC1840F23`
- Native DLL SHA-256: `700D10ED8C575E427BD401B14AA8E5AB21C4B21C635969583426CAD1FF37572E`
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

## ARM32 dependency and linker preflight

The ARM32 build has two independent requirements that must be applied together:

1. Cargo's generated `windows-sys` 0.60.2/0.61.2 bindings omit ARM32 definitions used by this dependency graph. Use `../.cargo/uwp-windows-sys-patch.toml` via Cargo's `--config` option. Without it Cargo uses the registry copies and fails with missing `WSADATA`, `SERVENT`, `MEMORY_BASIC_INFORMATION`, and `DELAYLOAD_INFO`.
2. SDK 10.0.19041.0 contains the required ARM `mincore.lib`, but the linker may not inherit the `LIB` value set by `deps.ps1`. Set `LIB` and linker `/LIBPATH` values explicitly; short paths avoid spaces in `RUSTFLAGS`.

```powershell
$env:LIB = 'C:\PROGRA~2\WI3CF2~1\10\Lib\100190~1.0\um\arm;C:\PROGRA~2\WI3CF2~1\10\Lib\100190~1.0\ucrt\arm;' + $env:LIB
$env:RUSTFLAGS = '--cfg windows_raw_dylib -C link-arg=/LIBPATH:C:\PROGRA~2\WI3CF2~1\10\Lib\100190~1.0\um\arm -C link-arg=/LIBPATH:C:\PROGRA~2\WI3CF2~1\10\Lib\100190~1.0\ucrt\arm'
& "$env:USERPROFILE\.cargo\bin\rustup.exe" run nightly cargo build --config "C:/Users/pablo/OneDrive/Documentos/spotifyConnect/.cargo/uwp-windows-sys-patch.toml" -Z build-std=std,panic_abort --target thumbv7a-uwp-windows-msvc --release --no-default-features --features "native-tls uwp-backend with-libmdns"
```

Do not omit `--config` or the ARM linker paths. The earlier failed attempt omitted the local Cargo patch, then exposed the separate `mincore.lib` search-path failure.

## UWP build and package

Restore packages, select `Release|ARM`, then build/package with MSBuild or Visual Studio. Confirm that the manifest target and project min-version are compatible with the Lumia OS. The project enables .NET Native for ARM Release and references a PFX that is absent from the checkout; create or select a user-controlled development certificate before packaging. Back up the installed APPX and data/session rollback plan before sideloading. Validate package architecture, signature, DLL architecture, and device deployment through Windows Device Portal.

## Host preflight (validated 2026-10-12)

Visual Studio Build Tools 2022, MSBuild, the Windows 10 SDK `10.0.19041.0`, Rust nightly, and `rust-src` are installed on this workstation. Rust nightly confirms that it recognizes `thumbv7a-uwp-windows-msvc`. The native ARM32 DLL and local development certificate are present and validated. The installed `dotnet` runtime is not an SDK and is not used to build this legacy UWP project. See [LOCAL_TESTING.md](LOCAL_TESTING.md) for the desktop-versus-Lumia test boundary.

Before attempting a build, validate the installed Visual Studio workloads and command-line build tools in a Developer PowerShell:

```powershell
msbuild .\LibreSpotUWP.sln /restore /t:Build /p:Configuration=Release /p:Platform=ARM
```

Only run that command after placing a DLL built from the pinned native revision at `LibreSpotUWP\librespot.dll` and selecting a development certificate. For daily code changes, use an incremental `Debug|ARM` or `Release|ARM` build and package only when an installable APPX is needed. A successful host build does not validate Windows 10 Mobile deployment or Spotify Connect discovery; those are separate hardware gates.

## Spotify Connect background keep-alive

On Windows Mobile the receiver is deliberately advertised as `Speaker`, even though the
underlying OS family is a phone. `Smartphone` is a Spotify controller/client identity;
advertising it made Connect mirror the source playback position while transfers remained
stuck in `Connecting`. Validate this in `/?action=getInfo`: `deviceType` must be `Speaker`.

When `SpotifyLocalDiscoveryEnabled` is enabled, the app requests UWP extended execution while it is foregrounded, independently of an active Spotify playback session. The request is renewed at launch, on suspension, and when a Connect session becomes idle. `ExtendedExecutionReason.Unspecified` does not guarantee execution in Windows 10 Mobile Connected Standby. Locked idle mDNS/TCP availability is currently an unmet requirement, not a validated capability. Bring the app to the foreground for reliable discovery until the locked-device matrix in `STABILITY_PLAN.md` passes. Revocation logs help diagnosis but do not by themselves distinguish a listener defect from network power policy.

## CI recommendation

Use a Windows runner with the required VS/SDK image pinned by version. Cache NuGet/Cargo only by lockfile hash, build ARM Release, emit an unsigned artifact for inspection, and keep signing keys outside source control. Hardware deployment remains a manual gated stage.
