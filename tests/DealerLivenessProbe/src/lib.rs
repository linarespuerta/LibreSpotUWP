// Diagnostic harness: compile the unmodified run function from DEALER_SOURCE.
// Network/URL acquisition and surrounding ownership are injected; no Spotify traffic.
#![cfg(test)]
use std::{future::Future, pin::Pin, sync::Arc, task::{Context, Poll}, time::Duration};
use tokio::{select, sync::{Semaphore, mpsc}, task::JoinHandle};
use futures_util::future::join_all;

type Error = &'static str;
type Url = String;
type GetUrlResult = Result<Url, Error>;
const WEBSOCKET_CLOSE_TIMEOUT: Duration = Duration::from_secs(3);
const RECONNECT_INTERVAL: Duration = Duration::from_secs(10);
macro_rules! error { ($($arg:tt)*) => { let _ = format_args!($($arg)*); }; }
macro_rules! warn { ($($arg:tt)*) => { let _ = format_args!($($arg)*); }; }

struct TimeoutOnDrop<T>(JoinHandle<T>);
impl<T> TimeoutOnDrop<T> { fn new(handle: JoinHandle<T>, _: Duration) -> Self { Self(handle) } }
impl<T> Future for TimeoutOnDrop<T> {
    type Output = Result<T, tokio::task::JoinError>;
    fn poll(mut self: Pin<&mut Self>, cx: &mut Context<'_>) -> Poll<Self::Output> { Pin::new(&mut self.0).poll(cx) }
}
impl<T> Drop for TimeoutOnDrop<T> { fn drop(&mut self) { self.0.abort(); } }

struct DealerShared { closed: Semaphore, _retained_subscription_sender: mpsc::UnboundedSender<()> }
impl DealerShared {
    fn is_closed(&self) -> bool { self.closed.is_closed() }
    async fn closed(&self) { let _ = self.closed.acquire().await; }
}
async fn connect(_: &Url, _: Option<&Url>, _: &Arc<DealerShared>) -> Result<(JoinHandle<()>, JoinHandle<()>), Error> {
    Err("injected websocket connection failure")
}
include!(concat!(env!("OUT_DIR"), "/dealer_run.rs"));

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicUsize, Ordering};

    #[tokio::test(start_paused = true)]
    async fn transient_url_errors_retry_and_remain_cancellable() {
        let (sender, mut receiver) = mpsc::unbounded_channel();
        let shared = Arc::new(DealerShared { closed: Semaphore::new(0), _retained_subscription_sender: sender });
        let calls = Arc::new(AtomicUsize::new(0));
        let attempts = calls.clone();
        let stop = shared.clone();
        let result = run(shared.clone(), None, move || {
            if attempts.fetch_add(1, Ordering::SeqCst) >= 2 { stop.closed.close(); }
            async { Err("injected transient URL/token failure") }
        }, None).await;
        assert_eq!(result, Ok(()));
        assert!(calls.load(Ordering::SeqCst) >= 3, "URL failures stopped retries");
        assert!(matches!(receiver.try_recv(), Err(mpsc::error::TryRecvError::Empty)), "subscription remains open, not disconnected");
        assert!(shared.is_closed());
    }

    #[tokio::test(start_paused = true)]
    async fn socket_and_url_refresh_failures_both_retry_until_cancelled() {
        let (sender, _receiver) = mpsc::unbounded_channel();
        let shared = Arc::new(DealerShared { closed: Semaphore::new(0), _retained_subscription_sender: sender });
        let calls = Arc::new(AtomicUsize::new(0));
        let attempts = calls.clone();
        let started = tokio::time::Instant::now();
        let stop = shared.clone();
        let result = run(shared, None, move || {
            let call = attempts.fetch_add(1, Ordering::SeqCst);
            if call >= 2 { stop.closed.close(); }
            async move { if call == 0 { Ok("injected endpoint".to_string()) } else { Err("injected transient refresh failure") } }
        }, None).await;
        assert_eq!(result, Ok(()));
        assert!(calls.load(Ordering::SeqCst) >= 3);
        assert!(started.elapsed() >= RECONNECT_INTERVAL, "normal socket error does retry");
    }
}
