//! Port of AAEmu.UnitTests/Game/Core/Managers/ManaRegenManagerTests.cs
//!
//! C#: `ManaRegenManager.Initialize` subscribes its `Tick` to `TickManager.OnTick` (200 ms).
//! Rust: `regen::start_tickers` (called from `System::start`) spawns the 200 ms mana regen
//! ticker. The subscription is observed through its effect: a registration whose character is
//! not in the world is dropped by the first `mana_regen_tick`.

use std::sync::Arc;
use std::time::Duration;

use crate::data::GameData;
use crate::test_support::test_server_with;

use super::regen::{start_tickers, ManaRegenEntry};

#[tokio::test]
async fn initialize_subscribes_to_tick_manager() {
    let server = Arc::new(test_server_with(GameData::empty()));
    server.systems.skills.mana_regen.lock().unwrap().insert(
        1,
        ManaRegenEntry {
            obj_id: 0xDEAD,
            tick: 1000.0,
            tick_level_mana_cost: 1.0,
            level: 1,
        },
    );
    start_tickers(&server);
    let mut ticked = false;
    for _ in 0..100 {
        tokio::time::sleep(Duration::from_millis(50)).await;
        if server.systems.skills.mana_regen.lock().unwrap().is_empty() {
            ticked = true;
            break;
        }
    }
    assert!(ticked, "the mana regen tick never ran");
}
