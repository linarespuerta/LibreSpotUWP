use std::{env, fs, path::PathBuf};
fn main() {
    let source_path = env::var("DEALER_SOURCE").unwrap_or_else(|_| {
        PathBuf::from(env::var("CARGO_MANIFEST_DIR").unwrap())
            .join("../../../librespot-upstream/core/src/dealer/mod.rs")
            .to_string_lossy().into_owned()
    });
    let source = fs::read_to_string(&source_path).unwrap();
    let marker = "async fn run<F, Fut>(";
    let start = source.find(marker).expect("original Dealer run function");
    let open = start + source[start..].find('{').unwrap();
    let mut depth = 0;
    let mut end = None;
    for (offset, ch) in source[open..].char_indices() {
        if ch == '{' { depth += 1; }
        if ch == '}' { depth -= 1; if depth == 0 { end = Some(open + offset + 1); break; } }
    }
    let body = &source[start..end.expect("closing brace")];
    fs::write(PathBuf::from(env::var("OUT_DIR").unwrap()).join("dealer_run.rs"), body).unwrap();
    println!("cargo:rerun-if-changed={source_path}");
    println!("cargo:rerun-if-env-changed=DEALER_SOURCE");
}
