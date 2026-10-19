// Compile the production bridge itself with injected host callbacks/protocol sink.
#![cfg(test)]
#![allow(dead_code, unreachable_patterns)]
use std::{sync::{Arc, Mutex, atomic::{self, AtomicBool, AtomicU64, AtomicUsize, Ordering}}, time::Duration};
use tokio::{select, sync::{mpsc, Semaphore}, task::JoinHandle};
#[derive(Debug, PartialEq)]
struct Error(&'static str);
impl Error {
    fn unavailable(message: &'static str) -> Self { Self(message) }
    fn invalid_argument(message: &'static str) -> Self { Self(message) }
    fn cancelled(message: &'static str) -> Self { Self(message) }
}
type Url = String;
type WsResult<T> = Result<T, Error>;
enum WsMessage { Text(String), Close(Option<()>) }
macro_rules! info { ($($arg:tt)*) => {{ let _ = format_args!($($arg)*); }}; }
macro_rules! warn { ($($arg:tt)*) => {{ let _ = format_args!($($arg)*); }}; }
struct DealerShared { uwp_id: AtomicU64, closed: Semaphore, received: AtomicUsize }
impl DealerShared {
    async fn closed(&self) { let _ = self.closed.acquire().await; }
    fn dispatch(&self, _: serde_json::Value, sender: &mpsc::UnboundedSender<WsMessage>) {
        self.received.fetch_add(1, Ordering::SeqCst);
        let _ = sender.send(WsMessage::Text("{\"type\":\"reply\"}".to_owned()));
    }
}
#[path = "../../../../librespot-upstream/core/src/dealer/uwp_transport.rs"]
mod transport;

static OPEN_ALLOWED: AtomicBool = AtomicBool::new(true);
static LAST_ID: AtomicU64 = AtomicU64::new(0);
static SENT: AtomicUsize = AtomicUsize::new(0);
static CLOSED: AtomicUsize = AtomicUsize::new(0);
static ACTIVITY: AtomicUsize = AtomicUsize::new(0);
extern "C" fn open(id: u64, _: *const std::ffi::c_char) -> bool {
    LAST_ID.store(id, Ordering::SeqCst);
    if !OPEN_ALLOWED.load(Ordering::SeqCst) { return false; }
    unsafe { transport::push(id, 1, std::ptr::null(), 0) }
}
extern "C" fn send(_: u64, _: *const u8, _: usize) -> bool { SENT.fetch_add(1, Ordering::SeqCst); true }
extern "C" fn close(id: u64) {
    CLOSED.fetch_add(1, Ordering::SeqCst);
    unsafe { transport::push(id, 0, std::ptr::null(), 0); }
}
extern "C" fn activity(_: u64, _: u32, _: bool) { ACTIVITY.fetch_add(1, Ordering::SeqCst); }
fn shared() -> Arc<DealerShared> { Arc::new(DealerShared { uwp_id: AtomicU64::new(0), closed: Semaphore::new(0), received: AtomicUsize::new(0) }) }

#[tokio::test]
async fn production_bridge_bounds_frames_dispatches_replies_and_isolates_connections() {
    transport::register(transport::Callbacks { open, send, close, activity });
    OPEN_ALLOWED.store(false, Ordering::SeqCst);
    assert!(transport::connect(&"wss://example.invalid".to_string(), &shared()).await.is_err());
    let rejected = LAST_ID.load(Ordering::SeqCst);
    assert!(!unsafe { transport::push(rejected, 1, std::ptr::null(), 0) }, "failed open leaked its registry entry");

    OPEN_ALLOWED.store(true, Ordering::SeqCst);
    let owner = shared();
    let (send_a, receive_a) = transport::connect(&"wss://example.invalid/a".to_string(), &owner).await.unwrap();
    let a = LAST_ID.load(Ordering::SeqCst);
    let guest = shared();
    let (send_b, receive_b) = transport::connect(&"wss://example.invalid/b".to_string(), &guest).await.unwrap();
    let b = LAST_ID.load(Ordering::SeqCst);
    assert_ne!(a, b);
    let text = b"{\"type\":\"request\"}";
    assert!(!unsafe { transport::push(a, 2, std::ptr::null(), text.len()) });
    assert!(!unsafe { transport::push(a, 2, text.as_ptr(), 2 * 1024 * 1024 + 1) });
    assert!(!unsafe { transport::push(a, 2, [255u8].as_ptr(), 1) });
    for _ in 0..16 { assert!(unsafe { transport::push(a, 2, text.as_ptr(), text.len()) }); }
    assert!(!unsafe { transport::push(a, 2, text.as_ptr(), text.len()) }, "inbound queue grew beyond its frame budget");
    for _ in 0..20 { tokio::task::yield_now().await; }
    assert_eq!(owner.received.load(Ordering::SeqCst), 16);
    assert!(SENT.load(Ordering::SeqCst) >= 16, "native protocol replies never reached the host callback");
    {
        let _scope = transport::network_activity(a, 3).unwrap();
        assert_eq!(ACTIVITY.load(Ordering::SeqCst), 1);
    }
    assert_eq!(ACTIVITY.load(Ordering::SeqCst), 2, "native work did not balance its wake ownership");

    owner.closed.close();
    tokio::time::timeout(Duration::from_secs(1), async { send_a.await.unwrap(); receive_a.await.unwrap(); }).await.unwrap();
    assert!(!unsafe { transport::push(a, 2, text.as_ptr(), text.len()) }, "closed owner accepted a stale frame");
    assert!(unsafe { transport::push(b, 2, text.as_ptr(), text.len()) }, "closing the owner invalidated another connection");
    for _ in 0..10 { tokio::task::yield_now().await; }
    assert_eq!(guest.received.load(Ordering::SeqCst), 1);
    guest.closed.close();
    tokio::time::timeout(Duration::from_secs(1), async { send_b.await.unwrap(); receive_b.await.unwrap(); }).await.unwrap();
    assert!(CLOSED.load(Ordering::SeqCst) >= 3);
}
