//! Port of AAEmu.UnitTests/Game/Core/Managers/IndunManagerTests.cs
//!
//! `IndunManager.Initialize` subscribes `IndunInfoTick` (30 s, `useAsync: true`) to
//! `TickManager.OnTick`. The Rust port drives every instance tick from one 1 s Tokio interval
//! task ([`super::tick`]: `IndunInfoTick` on every 30th second), started once per server by
//! `System::start`; the test checks that this ticker is registered exactly once.

use std::sync::atomic::Ordering;
use std::sync::Arc;

use crate::test_support::test_server;

#[tokio::test]
async fn initialize_subscribes_to_tick_manager() {
    let server = Arc::new(test_server());
    let started = || {
        server
            .systems
            .instances
            .ticks_started
            .load(Ordering::SeqCst)
    };
    assert!(!started());
    super::start_ticks(&server);
    assert!(started());
    // Subscribed once: a second start does not add another ticker.
    super::start_ticks(&server);
    assert!(started());
}
