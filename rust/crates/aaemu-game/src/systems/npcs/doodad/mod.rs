//! Doodads: `DoodadManager` (templates, funcs, phase funcs), the `Doodad` phase machine and
//! funcs, `DoodadSpawner`, persistence of player doodads, `SCDoodad*` packets, doodad C->S
//! packets, `Models/Game/World/Interactions`.

pub mod data;
pub mod manager;
pub mod spawner;
pub mod funcs;
pub mod coffer;
pub mod interactions;
pub mod db;
pub mod packets;
pub mod phase;
pub mod phase_funcs;
pub mod runtime;
pub mod tasks;
pub mod templates;

use std::sync::Arc;

use crate::net::registry::PacketRegistry;
use crate::server::GameServer;
use crate::systems::SaveStage;

/// Startup of the doodad part: `zone_climate_elems` (`ZoneManager` climates, used by the growth
/// bonus) and the doodad half of `TimeManager.OnTimeOfDayChange`.
pub async fn start(server: &Arc<GameServer>) -> anyhow::Result<()> {
    manager::load_climate_elems(server);
    manager::start_tod_watch(server);
    Ok(())
}

/// Doodad C->S packets.
pub fn register(reg: &mut PacketRegistry) {
    packets::register(reg);
    coffer::register(reg);
}

/// Doodad part of `SaveManager.DoSave`: nothing (C# doodads save themselves on every change,
/// see [`db`]).
pub async fn save(
    _server: &GameServer,
    _tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
    _stage: SaveStage,
) -> crate::Result<usize> {
    Ok(0)
}
