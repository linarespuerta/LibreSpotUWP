# Dealer liveness diagnostic

This isolated host diagnostic compiles the **original `run` function** extracted by `build.rs` from the sibling native checkout's `core/src/dealer/mod.rs`. It does not copy/reimplement that function or modify production Rust. URL resolution and WebSocket connection results are injected; surrounding ownership and timeout wrappers are simplified. No Spotify requests, credentials, Windows Mobile APIs or audio are involved.

Run from this directory with `cargo test --offline`. The cached dependencies must be present. Set `DEALER_SOURCE` to an explicit source path to inspect another checkout. This does not build the UWP DLL or the native workspace.

The two tests characterize a defect in the inspected implementation: a `get_url` error ends the task rather than retrying; a normal socket error does retry, but a subsequent URL-refresh error ends it. A retained sender models the real Dealer object's shared subscription ownership and demonstrates that task termination need not close the subscription. Tokio's paused clock advances retry delays without wall-clock waits.

Passing means the failure path was reproduced, **not that it was fixed**. These assertions must change when that path is corrected. They neither reproduce controller lock nor establish the cause of a particular Lumia event. Correlate native control logs and controller-selected target on the actual device before attributing a hardware report to this path.
