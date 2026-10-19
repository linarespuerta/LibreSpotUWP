# Production WinRT bridge host tests

`cargo test --offline --manifest-path tests/DealerTransportProbe/Cargo.toml`

The test imports the actual sibling native checkout's `core/src/dealer/uwp_transport.rs`. Host callbacks, protocol dispatch and DealerShared are injected; no Spotify credentials or network are used. It exercises rejected-open cleanup, bounded inbound frames, invalid UTF-8 and pointer/length rejection, native dispatch/outgoing replies, balanced native work callbacks, closing connections and stale owner frames versus an independent guest connection.

The test does not execute WinRT, ControlChannelTrigger slot allocation, Windows Mobile suspension or the real Spotify protocol parser. Those remain device acceptance gates. The native ARM build compiles the bridge against the real types. If the source moves, update the explicit relative module path rather than copying its implementation into this harness.
