//! Doodads: `DoodadManager` (templates, funcs, phase funcs), the `Doodad` phase machine and
//! funcs, `DoodadSpawner`, persistence of player doodads, `SCDoodad*` packets, doodad C->S
//! packets, `Models/Game/World/Interactions`.

pub mod data;
pub mod manager;
pub mod spawner;

use std::sync::Arc;

use crate::net::registry::PacketRegistry;
use crate::server::GameServer;
use crate::systems::SaveStage;

/// Startup of the doodad part.
pub async fn start(_server: &Arc<GameServer>) -> anyhow::Result<()> {
    Ok(())
}

/// Doodad C->S packets.
pub fn register(_reg: &mut PacketRegistry) {}

/// Doodad part of `SaveManager.DoSave`.
pub async fn save(
    _server: &GameServer,
    _tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
    _stage: SaveStage,
) -> crate::Result<usize> {
    Ok(0)
}
