# Lumia HiFi architecture audit

## Scope and provenance

This repository is a fork of `megabytesme/LibreSpotUWP` at commit `46c7c1441086c091a26a582308705ec0f5289d24`. It is an UWP C# host coupled through C ABI P/Invoke to a Rust-built `librespot.dll`. Upstream licensing is CC BY-NC-SA 4.0; attribution and share-alike/non-commercial terms must be retained.

## Confirmed components

| Area | Evidence | Responsibility |
| --- | --- | --- |
| UWP host | `LibreSpotUWP.sln`, `LibreSpotUWP.csproj` | App lifecycle, XAML UI, credentials, media state |
| Native bridge | `Interop/LibreSpot.cs` | Sequential `LibrespotConfig`, Cdecl entry points, events and PCM ring ABI |
| Session lifecycle | `Services/LibrespotService.cs` | Native instance creation, token/credential handoff, playback commands, event mapping |
| Audio | `AudioFormatProbe.cs`, `LibrespotRingBufferPlayer.cs`, `NativeWindowsAudioPlayer.cs` | 44.1 kHz stereo format negotiation; AudioGraph ring renderer or native backend |
| UI | `Views/Win10_1507`, `Win10_1709`, `Win11` | OS-specific XAML player, library and settings screens |
| Resilience | `PlaybackLifecycleCoordinator.cs`, `LogService.cs` | Recovery coordination and bounded, redacted diagnostic logging |

`BuildConfig` creates a native configuration only after the UWP side has initialized the audio backend and probed a PCM format. It currently names the receiver from `Environment.MachineName`, uses cached local folders, requests 320 kbps, disables normalization, and passes access/playback credentials to Rust. The source keeps callback delegates alive and frees its allocated UTF-8 configuration members after native construction.

## Platform boundary

The package manifest targets `Windows.Universal`, declares 15063 as its manifest minimum, while the project currently sets 16299 as its project minimum and 19041 as target SDK. ARM is the Windows 10 Mobile architecture; it is not ARM64. `librespot.dll` is listed as package content but is absent from this clone, so the project cannot package as checked out.

## Discovery boundary

Spotify Connect discovery requires all of the following: native Zeroconf advertisement, a reachable local HTTP endpoint implementing the Connect handshake, compatible device authentication, suitable UWP LAN capabilities, and a physical-device network test. The manifest now requests both `privateNetworkClientServer` and `internetClientServer`; see `DISCOVERY_AUDIT.md`.

## Design direction

Keep the host as the authority for UI, secret storage, media state, and diagnostics. Keep Connect protocol, session and audio decode in Rust. Add a narrow, versioned diagnostic interface/event surface rather than exposing credentials or extending FFI speculatively. The Home Assistant path should consume a local, redacted state/control contract and never become part of Spotify authentication.
