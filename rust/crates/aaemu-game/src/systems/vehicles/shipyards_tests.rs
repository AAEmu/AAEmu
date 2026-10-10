//! Port of AAEmu.UnitTests/Game/Core/Managers/ShipyardManagerTests.cs
//!
//! `ShipyardManager.Initialize()` schedules exactly one `ShipyardTickTask` (start delay one
//! minute, repeated every minute). The Rust manager runs it as one Tokio ticker started by
//! `systems::vehicles::System::start` (`spawn_ticker(TICK_INTERVAL, TICK_START_DELAY,
//! shipyard_tick)`); the schedule is checked on the constants and the ticker with a shortened
//! delay, observed through its effect (`UpdateShipyardInfo` gives a fresh shipyard the tax
//! protection buff).

use std::sync::Arc;
use std::time::Duration;

use super::*;
use crate::data::GameData;
use crate::models::shipyard::ShipyardData;
use crate::systems::skills::data::BuffTemplate;
use crate::systems::vehicles::data::ShipyardTemplate;
use crate::test_support::test_server_with;

#[tokio::test]
async fn initialize_schedules_tick() {
    let mut d = GameData::empty();
    d.systems.skills.add_buff(BuffTemplate {
        id: buff::TAX_PROTECTION,
        ..Default::default()
    });
    d.systems.vehicles.shipyard_templates.insert(
        3,
        ShipyardTemplate {
            id: 3,
            tax_duration: 3_600_000,
            ..ShipyardTemplate::default()
        },
    );
    let server = Arc::new(test_server_with(d));
    server.set_self();
    let obj_id = 0x00F0_0100;
    let shipyard = Entity::new(
        obj_id,
        Shipyard::new(ShipyardData {
            id: 1,
            template_id: 3,
            spawned: chrono::Utc::now().timestamp(),
            obj_id,
            ..ShipyardData::default()
        }),
    );
    server
        .systems
        .vehicles
        .shipyards
        .shipyards
        .lock()
        .unwrap()
        .push((1, shipyard.clone()));
    let has_protection = || shipyard.data().unit.buffs.check_buff(buff::TAX_PROTECTION);
    assert_eq!(TICK_START_DELAY, Duration::from_secs(60));
    assert_eq!(TICK_INTERVAL, Duration::from_secs(60));

    let step = Duration::from_millis(400);
    super::super::spawn_ticker(&server, step, step, shipyard_tick);
    tokio::time::sleep(step / 4).await;
    assert!(!has_protection(), "no shipyard tick before the start delay");

    tokio::time::sleep(step).await;
    assert!(
        has_protection(),
        "the shipyard tick ran after the start delay"
    );
}
