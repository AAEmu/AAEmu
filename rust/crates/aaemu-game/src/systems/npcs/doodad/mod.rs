//! Doodads: `DoodadManager` (templates, funcs, phase funcs), the `Doodad` phase machine and
//! funcs, `DoodadSpawner`, persistence of player doodads, `SCDoodad*` packets, doodad C->S
//! packets, `Models/Game/World/Interactions`.

pub mod coffer;
pub mod data;
pub mod db;
pub mod funcs;
pub mod interactions;
pub mod logic;
pub mod manager;
pub mod packets;
pub mod phase;
pub mod phase_funcs;
pub mod runtime;
pub mod spawner;
pub mod tasks;
pub mod templates;

use std::sync::Arc;

use crate::net::registry::PacketRegistry;
use crate::server::GameServer;
use crate::systems::SaveStage;

/// Startup of the doodad part: `zone_climate_elems` (`ZoneManager` climates, used by the growth
/// bonus) and the doodad half of `TimeManager.OnTimeOfDayChange`.
pub async fn start(server: &Arc<GameServer>) -> anyhow::Result<()> {
    server
        .systems
        .npcs
        .doodads
        .set_growth_rate(server.config.world.growth_rate);
    manager::load_climate_elems(server);
    manager::start_tod_watch(server);
    Ok(())
}

/// Doodad C->S packets.
pub fn register(reg: &mut PacketRegistry) {
    packets::register(reg);
    coffer::register(reg);
}

/// Drain on-change doodad writes before `SaveManager.DoSave` persists item state.
pub async fn save(
    server: &GameServer,
    _tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
    stage: SaveStage,
) -> crate::Result<usize> {
    if stage == SaveStage::BeforeItems {
        server.systems.npcs.doodads.persistence.flush().await?;
    }
    Ok(0)
}
