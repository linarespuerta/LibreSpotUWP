# Home Assistant code round (M3)

Status: **WP1–WP6 written on branch `feature/ha-link` (2026-11-02), package version 1.0.35.0. WP0 is folded into WP5 as on-device evidence (the link records which standby slot it gets). Pending: the Windows ARM build and WP0/WP7 on the Lumia and homelab — see the checklist in `TEST_PLAN.md`.** The plan below is unchanged except for the deviations listed in section 10. This document refines [HOME_ASSISTANT_DESIGN.md](HOME_ASSISTANT_DESIGN.md) and roadmap milestone M3. All Lumia/LAN behaviour below is **unverified** until measured on the device.

## 1. Goal

Home Assistant shows the Lumia ("Marantz Sala") as a `media_player` entity with live state, track metadata, artwork, position and volume. From HA, the user can play/pause, skip, seek, set volume, shuffle and repeat. No Spotify credentials leave the phone, and no HA admin token is stored on the phone.

## 2. What the codebase already gives us

| Need | Existing piece | Notes |
|---|---|---|
| Single source of truth for state | `IMediaService.MediaStateChanged` + `Models/MediaState.cs` | Already consumed by `LiveTileService` and the SMTC, so an HA bridge is just one more subscriber. |
| Commands | `MediaService.ResumeAsync/PauseAsync/Next/Previous/Seek/SetVolumeAsync/SetShuffleAsync/SetRepeatAsync` | The SMTC handlers (`OnSmtcButtonPressed`, `OnSmtcPlayReceived`, …) already call these from non-UI threads. They also route to the Web API when the selected Connect device is remote. |
| Value domains | Volume `ushort` 0..65535; repeat `0=off, 1=context, 2=track` (`ToLibrespotRepeatMode`); state `Stopped/Loading/Playing/Paused` | Map these to HA domains in one pure function. |
| Standby-capable outbound socket | `Services/WinRtDealerTransport.cs` | Uses `MessageWebSocket` + `ControlChannelTrigger(RequestHardwareSlot)` + push/keep-alive tasks with `IsNetworkRequested`. Background wakes are routed through `App.OnBackgroundActivated`. This pattern is already proven on the Lumia for Spotify Dealer traffic. |
| Secret storage | `ISecureStorage` (`SaveAsync/LoadAsync/DeleteAsync`) | For the per-install HA link secret. |
| Persisted opt-in flags | `Helpers/UserSettings.cs` (`SpotifyLocalDiscoveryEnabled` pattern) | Add the HA flag here, default `false`, on all three settings-page variants. |
| Capabilities | `privateNetworkClientServer`, `internetClientServer`, `backgroundMediaPlayback` | No manifest change is needed for an outbound LAN WebSocket. |
| Redaction | `Services/DiagnosticRedactor.cs` | Use it for every log line about the link (URL host, secret). |
| Host tests | `tests/LibreSpotUWP.Playback.Tests` (net8.0, links app sources, custom `Assert` harness) | Passes on Fedora (checked 2026-11-02). New pure code must be linkable here, which means no WinRT types. |

## 3. Research findings that shape the design

1. **An inbound listener on a locked Lumia is the weak spot.** `STABILITY_PLAN.md` already shows that Rust/`StreamSocketListener` listeners have no broker ownership in Connected Standby. Polling the phone from HA would fail exactly when the phone is locked. The only standby-capable pattern this app has proven is an **outbound** WinRT socket with a `ControlChannelTrigger` hardware slot.
2. **The system limits `ControlChannelTrigger` slots by policy.** Microsoft does not publish the number, and the API docs mark `ControlChannelTriggerResourceType` "not supported on Windows Phone", even though the Dealer channel gets `HardwareSlotAllocated` on this Lumia. Whether a **second** hardware slot is granted is the main technical unknown, so WP0 tests it first.
3. **A Home Assistant integration can host its own endpoint.** It can register an `HomeAssistantView` on HA's aiohttp server and upgrade the request to a `web.WebSocketResponse`, using `requires_auth = False` plus its own secret check. With that, **the HA integration is the companion**: there is no extra container to deploy, secure or update.
4. **The built-in HA Spotify integration is complementary, not a replacement.** Since Spotify's Feb-2026 Development Mode rules, it needs a Premium app owner, one Client ID, at most 5 users and a reduced endpoint set. It only sees the configured account, so guest-account sessions on the Lumia are invisible, and it is cloud-dependent. It still works as a zero-code stopgap, because "Marantz Sala" appears as a Spotify Connect source.
5. **HA media player API:** use `MediaPlayerEntityFeature` flags; the legacy `SUPPORT_*` constants are gone. Report position with `media_position` + `media_position_updated_at`.

## 4. Decision

| Option | Locked/idle reach | Moving parts | Verdict |
|---|---|---|---|
| **A. Lumia dials HA:** custom integration exposes a WebSocket endpoint; the Lumia keeps an outbound `MessageWebSocket` under `ControlChannelTrigger` | Same mechanism as the proven Dealer channel (if a 2nd slot is granted) | App + 1 custom integration | **Chosen** |
| B. HA polls an HTTP server on the Lumia (`StreamSocketListener`) | Not standby-capable without broker work | App + integration; phone IP in HA config | Fallback only if WP0 fails |
| C. Separate companion container | Same reachability problem as A or B, plus one more hop | +1 service to operate | Rejected for now |
| D. Built-in Spotify integration | Cloud-based; one account only | Zero code | Optional stopgap, documented only |

The design doc's preference for a companion (C) is superseded. The custom integration plays the companion role inside HA.

```
 Lumia (UWP)                                         Home Assistant (homelab)
 ┌──────────────────────────────┐   ws://<ha>:8123    ┌──────────────────────────────┐
 │ MediaService ─► HaBridge     │ ── /api/lumia_hifi ─►│ LinkView (aiohttp WS)        │
 │   (state mapper, throttle)   │   Bearer <secret>    │   └► LumiaLink (per entry)   │
 │ HaLinkTransport              │ ◄── commands ─────── │        └► media_player entity│
 │   MessageWebSocket + CCT     │ ─── state / ack ───► │ config_flow (secret, url)    │
 └──────────────────────────────┘                      └──────────────────────────────┘
```

## 5. Contract v1 (to be frozen in WP1)

- **Endpoint:** `GET /api/lumia_hifi/v1/link`, upgraded to a WebSocket. Header `Authorization: Bearer <secret>`. Wrong or missing secret → HTTP 401 before the upgrade, compared in constant time. If a second connection arrives for the same entry, it replaces the first, and the first is closed with code 4001.
- **Frames:** UTF-8 JSON, one object per message, max 16 KiB, always `{"v":1,"type":"…"}`. Unknown `type` → `error`, connection kept. Unknown `v` → close 4002.
- **Lumia → HA**
  - `hello`: `device_id` (random per-install GUID, **not** a Spotify ID), `device_name`, `app_version`, `commands` (supported list). Must be the first frame; anything else first → close 4003.
  - `state`: a **full snapshot**, never a delta. Fields:
    - `state`: `playing|paused|buffering|idle`
    - `title`, `artist`, `album`, `artwork_url` (https Spotify CDN or null)
    - `content_id` (`spotify:track:…` or null), `duration_ms`, `position_ms`
    - `volume` (0.0–1.0, 3 decimals), `shuffle` (bool), `repeat` (`off|all|one`)
    - `session`: `connected|offline|recovering`
    - `output`: `local|remote` (from `IsSpotifyConnectRemote`), `seq` (monotonic uint)
    - Sent on change, throttled to ≥ 250 ms, plus once immediately after `hello` and after every command.
  - `ack`: `id`, `ok`, optional `error` (`unsupported|invalid|failed|busy`).
- **HA → Lumia**
  - `command`: `id` (string ≤ 64), `name` ∈ `play|pause|next|previous|seek|volume_set|shuffle_set|repeat_set`, plus `position_ms` / `volume` / `shuffle` / `repeat` as applicable. HA marks a command failed if there is no `ack` within 5 s.
- **Mapping:**
  - Playback state: `Playing→playing`, `Paused→paused`, `Loading→buffering`, `Stopped→idle`. The HA entity becomes `unavailable` when no link is connected.
  - Volume: `ushort/65535`.
  - Repeat: `0/1/2 ↔ off/all/one`.
- **Never in the payload:** account IDs/usernames, tokens, session blobs, audio keys, context URIs of private playlists, raw logs, phone IP.
- **Liveness:** no application heartbeat, to protect the battery. Connection loss = socket close/error. Every Lumia → HA message carries `seq`. HA treats state older than 15 min (one CCT keep-alive interval) with no traffic as stale, but keeps the entity available while the socket is open.

## 6. Work packages and Definitions of Done

Order matters: WP1–WP4 run entirely on Fedora, need no device, and can all be done in the next session. WP5–WP6 need the Windows host to compile. WP0 and WP7 need the Lumia.

### WP0 — Device spike: second hardware control-channel slot (Windows + Lumia, timeboxed ½ day)
Build a throwaway branch that opens a second `ControlChannelTrigger` + `MessageWebSocket` to a `ws://` LAN echo server, while the Dealer channel is active.

**DoD**
- [ ] The log records `ControlChannelTriggerStatus` for the second channel while the Dealer reports `HardwareSlotAllocated`.
- [ ] With the phone locked for 10 min, a frame pushed from the server wakes the push task (`OnBackgroundActivated` telemetry) and the reply arrives.
- [ ] Result recorded in this doc. If no hardware slot is granted, record the decision instead: either fall back to software slot/foreground-only (entity is `unavailable` while locked and idle), or switch to option B.
- [ ] The spike branch is not merged; the baseline APPX is preserved before installing.

### WP1 — Contract specification and fixtures (Fedora)
**DoD**
- [ ] `contracts/lumia_hifi/v1/` contains JSON Schema files for every frame, plus valid/invalid example fixtures, including an oversized frame, an unknown type and a wrong version.
- [ ] `docs/HOME_ASSISTANT_DESIGN.md` points to the schema and says that option A supersedes the companion recommendation.
- [ ] The fixtures contain no real IPs, account names or secrets.

### WP2 — HA custom integration `lumia_hifi` (Fedora)
Location: `integrations/home_assistant/custom_components/lumia_hifi/` (HACS-compatible layout later).

**DoD**
- [ ] `manifest.json` has the domain, `version`, `iot_class: local_push`, `config_flow: true`, `dependencies: ["http"]` and a pinned minimum HA version.
- [ ] The config flow asks for the device name, generates a 256-bit secret with `secrets.token_urlsafe`, and shows the link URL and secret once. An options flow can rotate the secret, which closes the live link. The secret is stored only in the config entry.
- [ ] `LinkView` implements §5 exactly: auth before upgrade, size limit, hello-first, replace-on-reconnect, close codes.
- [ ] The `media_player` entity:
  - exposes state, title, artist, album, `entity_picture` (remote URL), duration, position plus `updated_at`, volume, shuffle and repeat;
  - sets `supported_features` from the `hello.commands` list (PLAY, PAUSE, NEXT_TRACK, PREVIOUS_TRACK, SEEK, VOLUME_SET, VOLUME_STEP, SHUFFLE_SET, REPEAT_SET);
  - is `unavailable` without a link;
  - raises `HomeAssistantError` on a nack or timeout.
- [ ] Diagnostics download (`diagnostics.py`) redacts the secret.
- [ ] No polling and no blocking I/O in the event loop. The package passes `ruff` and `mypy --strict`.

### WP3 — Lumia simulator and integration tests (Fedora)
**DoD**
- [ ] `integrations/home_assistant/tools/lumia_sim.py` (aiohttp client) speaks contract v1, plays a scripted session and answers commands. Usable by hand against a dev HA.
- [ ] The `pytest` + `pytest-homeassistant-custom-component` suite (run via `uv`) covers:
  - config flow;
  - auth rejection (no/wrong secret → 401);
  - hello-first;
  - state → entity attributes;
  - each service call → command frame → ack;
  - ack timeout → error;
  - disconnect → `unavailable`;
  - reconnect replaces the old socket;
  - secret rotation drops the link;
  - schema validation of every frame against the WP1 schemas.
- [ ] `podman-compose` file under `integrations/home_assistant/dev/` starts a **throwaway** HA on Fedora with the integration bind-mounted. A one-command smoke run with the simulator shows a working entity. The homelab HA is not touched.
- [ ] All tests are green and the command to run them is documented in `docs/LOCAL_TESTING.md`.

### WP4 — C# contract core (Fedora, net8 host tests)
New WinRT-free files, linked into the test project:
- `Services/HomeAssistant/HaContract.cs`: DTOs and Newtonsoft serialization.
- `HaStateMapper.cs`: `MediaState` → `state` frame.
- `HaCommandParser.cs`: validated command records.
- `HaStateThrottle.cs`: ≥ 250 ms coalescing, injected clock.

**DoD**
- [ ] The mapper covers every `LibrespotPlaybackState`, the volume edges (0, 65535), repeat 0/1/2, narration/DJ (uses `DisplayTitle`/`DisplayArtist`), a null `Metadata`, `output` local/remote and offline/recovering.
- [ ] The parser rejects unknown names, a missing `id`, an out-of-range volume/position, `v≠1` and oversized frames.
- [ ] Serialized frames validate against the WP1 schemas: either a test reads the fixtures, or the Python suite validates C#-generated golden files.
- [ ] A test asserts that the serialized state never contains `OwnerAccountId`, tokens or any key outside the schema.
- [ ] `dotnet run --project tests/LibreSpotUWP.Playback.Tests` is green with `TreatWarningsAsErrors`.

### WP5 — C# link transport and command dispatch (Windows build host)
- `HaLinkTransport` reuses the Dealer pattern: `MessageWebSocket`, `SetRequestHeader("Authorization", …)`, `ControlChannelTrigger` with its own task prefix `LibreSpotUWP.HaLink.`, push and keep-alive tasks, and routing in `App.OnBackgroundActivated` next to `WinRtDealerTransport.OwnsTask`.
- `HaBridge` subscribes to `MediaStateChanged` and dispatches commands to the same `MediaService` methods the SMTC uses.

**DoD**
- [ ] Reconnects with exponential backoff (1 s → 5 min, with jitter), re-armed on network-status change. It never retries when the flag is off or the secret is missing.
- [ ] Commands run serialized. `seek`, `volume_set`, `shuffle_set` and `repeat_set` reuse the existing debounce/serialization paths; there is no new playback path.
- [ ] Turning the flag off closes the socket and unregisters the tasks. On startup, stale `LibreSpotUWP.HaLink.*` registrations are cleaned up the way the Dealer cleans its own.
- [ ] Logs go through `DiagnosticRedactor`: no secret and no full URL.
- [ ] ARM Release build succeeds and the package version is bumped.

### WP6 — Settings and pairing UX (Windows build host)
**DoD**
- [ ] `UserSettings.HomeAssistantLinkEnabled` (default `false`) and `HomeAssistantLinkUrl` are added; the secret is stored via `ISecureStorage` only.
- [ ] Controls are on all three settings variants (1507/1709/Win11): URL, secret (password box, never re-displayed), enable switch and a status line (`Disabled/Connecting/Connected/Auth failed/Unreachable`).
- [ ] Input validation: `http(s)` scheme, host required. `https` is accepted but documented as needing a cert the phone trusts.
- [ ] Optional, only if cheap: scan the config-flow QR with the existing ZXing dependency.

### WP7 — Physical and LAN validation (Lumia + homelab, needs explicit approval to install the integration on the homelab HA)
**DoD**: these rows of a new HA section in `TEST_PLAN.md` are executed and recorded.

| Case | Pass criterion |
|---|---|
| Foreground playback | Entity shows correct metadata/artwork within 1 s of a track change |
| Each command from HA UI | Takes effect within 1 s (unlocked), the controller phone reflects it |
| Guest account session | Entity follows the guest session; no account data in HA |
| Locked, playing, 30 min | State keeps updating; commands work |
| Locked, idle 15/60 min | Command from HA wakes the link (or documented unmet per WP0) |
| Wi-Fi drop/return, HA restart, phone restart | Link recovers without user action; entity `unavailable` while down |
| Wrong/rotated secret | 401, status "Auth failed", no retry storm |
| Battery | Idle drain with link on vs off over 2 h, recorded |

## 7. Global definition of done for the round
- [ ] Everything is behind the opt-in flag. With the flag off, the app behaves byte-for-byte as before: no sockets and no task registrations.
- [ ] No IPs, secrets or account identifiers in code, fixtures, docs or logs. LAN addresses are runtime configuration only.
- [ ] Existing host tests and the new Python/C# tests are green. The ARM32 build is green on Windows.
- [ ] `ROADMAP.md` M3 and `HOME_ASSISTANT_DESIGN.md` are updated. Claims about the device are labelled verified or unverified according to WP7 evidence.
- [ ] The homelab HA/Docker configuration is changed only after explicit approval, with a rollback note (remove `custom_components/lumia_hifi`, delete the entry).
- [ ] Small commits on a feature branch from `audit/initial-architecture` (e.g. `feature/ha-link`).

## 8. Out of scope for this round
`play_media` (start a Spotify URI from HA), browse media, TTS/announcements, source selection between Connect devices, HACS publication, and an HA → Spotify account bridge.

## 9. Risks and open questions
- **Second hardware CCT slot** may be refused on Windows 10 Mobile. WP0 decides this.
- **Battery:** the CCT keep-alive runs at least every 15 min, but HA command traffic wakes the radio. WP7 measures it.
- **HA reached over `https` behind a reverse proxy:** Windows 10 Mobile TLS (no TLS 1.3, old root store) may fail. Prefer plain LAN `ws://` to HA's internal port; the secret protects authentication but not confidentiality on the LAN, which is accepted for metadata/commands only.
- **Remote output mode** (`IsSpotifyConnectRemote`): the entity reports what the app controls, not only local playback. Confirm that this matches what the user expects in WP7.
- **HA version** on the homelab must be at least the integration's minimum. Check before WP7.

## 10. Implementation notes (WP1–WP4)

Verified on Fedora only: 130 Python tests (contract, config flow, endpoint, entity, secrets), the C# host suite (123 checks, 28 of them for this round), `ruff` + `mypy --strict`, and `integrations/home_assistant/dev/smoke.sh` against `ghcr.io/home-assistant/home-assistant:2026.10.4`. Nothing here has run on the Lumia or the homelab.

Deviations from the plan, and why:

- **Mapper input is a plain DTO.** `HaStateMapper` takes `HaStateInput`, and `HaStateInputFactory.FromMediaState` adapts the app's `MediaState`. Both are covered by host tests (the test project now links `MediaState` and references `SpotifyAPI.Web`).
- **DJ narration shows `Spotify DJ` / `Spotify`.** The narration text is generated commentary that can address the listener, so it is not sent (the plan said it would use `DisplayArtist`).
- **No stale-state logic yet.** `last_seen` is kept in diagnostics only (as an attribute it made every frame a recorder write); marking the entity stale after 15 minutes without traffic is deferred until WP7 shows whether it is needed.
- **Entry lookup is by secret.** One endpoint serves every entry; the bearer secret picks the entry, compared in constant time. Failed attempts go through Home Assistant's own ban mechanism.
- **Runtime validator has no dependencies.** Home Assistant 2026.10 replaced `voluptuous` with `probatio`, so `protocol.py` uses neither; only the config flow imports `probatio` (falling back to `voluptuous` on older releases).
- **Link URL in the config flow** comes from Home Assistant's internal URL. In the throwaway container this is the container address, not a phone-reachable one; on the homelab, check it before typing it into the app.
- **Minimum Home Assistant version is untested below 2026.10.4.** Check the homelab version before WP7.

Open items for the Windows host (WP5/WP6): the new files are listed in `LibreSpotUWP.csproj` and compile with C# 7.3 in the host test project, but the UWP ARM build itself has not been run.

### Code review fixes (2026-11-02)

A review of WP1–WP4 found ten defects; all are fixed, each with a regression test:

1. The C# parser threw on an integer too large for a 64-bit `v`; it now reports an unsupported version.
2. `HaStateThrottle.TryFlush` returns the remaining wait, so a timer that fires early re-arms instead of stranding the last snapshot.
3. Artwork falls through `ArtworkUri` -> album image -> track cover, taking the first https URL (a local `ms-appdata:` cache path no longer hides a usable cover).
4. A seek beyond 24 h is clamped, and any out-of-contract command value raises `HomeAssistantError` instead of an unhandled `FrameError`.
5. Only `hello`, `state` and connection changes write entity state; acks and errors no longer create recorder rows.
6. `HaFrames.Hello` trims and cuts the device name (falling back to "Lumia HiFi") and drops duplicate/unknown commands; a malformed hello now gets `invalid_frame` and close 4005, not 4003.
7. Frames up to 1 MiB follow the contract (`too_large` + 4004); above that the transport closes with 1009, as now documented.
8. Every pre-hello problem, binary frames included, gets an error frame and a close; replying to a peer that is already gone no longer raises out of the handler.
9. Both validators share the rules JSON Schema cannot express (strict RFC 8259 syntax, no duplicate names, integers written as integers, `v` must be the integer 1), pinned by the new `runtime_only/` and `malformed/` fixture folders.
10. Truncated text is trimmed again so it never ends in whitespace.

## 11. WP5–WP6 implementation notes

- **Split for testability.** Protocol behaviour is in WinRT-free classes covered by the host suite: `HaLinkSession` (hello, throttled snapshots, serialized commands with ack-then-state, error replies, idle tracking for background wakes), `HaCommandDispatcher` (4 s budget, `busy`/`failed` acks) and `HaLinkPolicy` (close-code classification, backoff 1 s -> 5 min with jitter, address and secret validation). `HomeAssistantLinkService` only owns the `MessageWebSocket`, the `ControlChannelTrigger`, timers and reconnection.
- **WP0 folded in.** Instead of a throwaway spike, the link requests its own hardware slot (`LibreSpotUWP.HaLink.*` tasks) and records the result in the Settings status line and the `ha-link-standby-slot` telemetry. Without a slot the link still works while the app runs.
- **Commands** use the same `IMediaService` calls as the lock-screen controls; no new playback path.
- **Settings**: one shared `HomeAssistantSettingsController` drives the section on all three variants. The secret is write-only (credential vault, never re-displayed); "Save and connect" turns the switch on. Addresses are normalized (`http://host:8123` -> `ws://host:8123/api/lumia_hifi/v1/link`).
- **Off by default.** With the switch off the app opens no socket and registers no task; at startup it only removes stale `LibreSpotUWP.HaLink.*` registrations.
- **Compile evidence off Windows**: `tests/WinRtTypeCheck` type-checks the WinRT-facing sources against SDK 10.0.19041 metadata with C# 7.3. XAML compilation and .NET Native are only exercised by the Windows build.
