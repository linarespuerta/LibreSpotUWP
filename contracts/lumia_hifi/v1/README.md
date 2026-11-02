# Lumia HiFi <-> Home Assistant contract v1

Status: **frozen for implementation (WP1).** Behaviour on the Lumia and the LAN is unverified until WP7.

The Lumia dials Home Assistant; Home Assistant never connects to the phone. One WebSocket carries UTF-8 JSON text frames, one JSON object per message.

## Transport rules

- Endpoint: `GET /api/lumia_hifi/v1/link`, upgraded to a WebSocket. Header `Authorization: Bearer <secret>`.
  Missing or wrong secret -> HTTP 401 **before** the upgrade (constant-time comparison).
- Maximum frame size: **16384 bytes** (UTF-8). A larger frame is a protocol error: the receiver answers `error` / `too_large` and closes with code 4004.
  As a transport guard, Home Assistant cuts frames above 1 MiB at the WebSocket layer (close 1009, no `error` frame).
- Every frame has `"v": 1` and a `"type"`. Unknown `type` -> `error` / `unknown_type`, connection stays open.
  Unparseable JSON or schema violation -> `error` / `invalid_frame`, connection stays open. Unknown `v` -> close 4002.
- The first Lumia frame must be `hello`. Before it:
  - a `hello` that violates the schema -> `error` / `invalid_frame`, then close **4005** (the phone should surface "invalid identity", for example a bad device name);
  - an oversized frame -> `error` / `too_large`, then close 4004;
  - unparseable, unknown-type or binary frames -> `error` with that code, then close 4003;
  - any other valid frame -> close 4003.
- A second connection for the same config entry replaces the first; the first is closed with 4001.
- There is no application heartbeat (battery). Connection loss is the socket closing or erroring.

## Stricter than JSON Schema

Both runtime validators enforce rules the schemas cannot express:

- **Strict RFC 8259 JSON**: no comments, single quotes, unquoted names, trailing commas, `NaN`/`Infinity`, leading zeros, raw control characters or trailing content.
- **No duplicate member names** (a frame with two `id`s is ambiguous).
- **Integers are written as integers**: `1.0` or `9e4` where the schema says `integer` is rejected, although JSON Schema treats them as equal. For `v` this means `"v": 1.0` is an unsupported version.
- **Integers that do not fit in 64 bits** are out of range, never an error of the validator itself.

## Frames

| Direction | `type` | Schema | Purpose |
|---|---|---|---|
| Lumia -> HA | `hello` | `schema/hello.schema.json` | identity and the list of supported commands |
| Lumia -> HA | `state` | `schema/state.schema.json` | **full** snapshot; sent after `hello`, after every command, and on change (throttled to >= 250 ms) |
| Lumia -> HA | `ack` | `schema/ack.schema.json` | result of one command; HA times out after 5 s |
| HA -> Lumia | `command` | `schema/command.schema.json` | `play`, `pause`, `next`, `previous`, `seek`, `volume_set`, `shuffle_set`, `repeat_set` |
| either | `error` | `schema/error.schema.json` | protocol-level problem |

## Value mapping (Lumia)

| App value | Wire value |
|---|---|
| `LibrespotPlaybackState` `Playing` / `Paused` / `Loading` / `Stopped` | `playing` / `paused` / `buffering` / `idle` |
| `MediaState.Volume` `ushort` 0..65535 | `volume` = value / 65535, rounded to 3 decimals; inverse rounds to nearest |
| `RepeatMode` 0 / 1 / 2 | `off` / `all` / `one` |
| `IsSpotifyConnectRemote` | `output`: `remote` if true, else `local` |
| `IsOffline`, `IsRecoveringOnlinePlayback`, `IsSessionConnected` | `session`: `offline` / `recovering` / `connected` (in that precedence; not connected also maps to `offline`) |

Text fields are trimmed and truncated to 256 characters. Non-`https` artwork URLs and content ids that are not `spotify:track:` / `spotify:episode:` become `null`.

## Never on the wire

Account identifiers or usernames, Spotify tokens, session blobs, audio keys, context URIs (they can name private playlists), raw native diagnostics, the phone's IP address.

## Fixtures

`fixtures/valid/*.json` must validate against the schema selected by their `type`. `fixtures/invalid/*.json` (named `<type>__<reason>.json`) must be rejected by the schema and by both validators.
`fixtures/runtime_only/*.json` are schema-valid but rejected by both validators (see "Stricter than JSON Schema"); `fixtures/malformed/*.txt` are not valid JSON. Each of these two folders has an `expected.json` with the error code both validators must report. They are shared by three test suites: JSON Schema validation (Python), the runtime validator of the Home Assistant integration (Python), and the C# mapper/parser (the `state_*` fixtures are also the exact golden output of the C# mapper). Fixtures must never contain real IPs, account names or secrets.
