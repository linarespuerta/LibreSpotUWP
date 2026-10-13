# Home Assistant integration design

## Recommendation

Use a lightweight LAN companion service only after discovery MVP proves what inbound networking Windows 10 Mobile permits. It avoids coupling Home Assistant authentication and update cadence to a legacy UWP app. The Lumia should publish a small redacted state snapshot and accept only local, authenticated commands; the companion maps that contract to a Home Assistant `media_player` entity.

Direct UWP-to-Home-Assistant REST/WebSocket is simpler initially but makes the phone responsible for HA endpoint security and reconnection. A custom HA integration that polls the phone directly has the same reachability risk. Therefore the companion is the preferred resilient boundary, not yet an implementation decision.

## Contract

Versioned local messages should provide `availability`, playback state, track title/artist/album/artwork URL or cached reference, position/duration, volume, device name, and supported commands. Commands are play, pause, next, previous, seek when supported, and volume. They must not include Spotify access tokens, session blobs, audio keys, or raw native diagnostics.

Use an outbound Lumia connection or a companion-mediated LAN session if UWP inbound listeners are not viable. Authenticate with a randomly generated per-install secret stored in UWP secure storage and companion bind-mounted configuration; rotate without logging it. The companion exposes its HA integration locally, includes `/healthz`, explicit reconnect/backoff, and persistent configuration. Ports, Lumia address, and HA endpoint are runtime configuration, never hard-coded.

No Docker Compose file or Home Assistant configuration has been changed in this audit.
