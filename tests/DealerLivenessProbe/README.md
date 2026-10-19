# Dealer liveness diagnostic

This isolated host diagnostic compiles the **original `run` function** extracted by `build.rs` from the sibling native checkout's `core/src/dealer/mod.rs`. It does not copy/reimplement that function or modify production Rust. URL resolution and WebSocket connection results are injected; surrounding ownership and timeout wrappers are simplified. No Spotify requests, credentials, Windows Mobile APIs or audio are involved.

Run from this directory with `cargo test --offline`. The cached dependencies must be present. Set `DEALER_SOURCE` to an explicit source path to inspect another checkout. This does not build the UWP DLL or the native workspace.

The two tests now verify the correction: transient URL-refresh errors and socket-connect errors both retry, and closing the Dealer cancels either retry path. A retained sender models the Dealer object's shared subscription ownership. Tokio's paused clock advances retry delays without wall-clock waits. The earlier characterization tests reproduced terminal URL errors; their assertions were replaced when that production loop was corrected.

Passing establishes the injected retry/cancellation behavior, **not Windows Mobile network availability**. The tests neither reproduce controller lock nor establish the cause of a particular Lumia event. Correlate native control logs and controller-selected target on the actual device before attributing a hardware report to this path.
