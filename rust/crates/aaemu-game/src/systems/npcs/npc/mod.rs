//! NPCs: `NpcManager` (templates, `Create`, stats), `NpcGameData` (spawner templates),
//! `NpcSpawner` / `NpcSpawnerNpc`, NPC interaction packets.

pub mod data;
pub mod manager;
pub mod packets;
pub mod spawner;
pub mod spawner_data;

use std::sync::Arc;

use crate::net::registry::PacketRegistry;
use crate::server::GameServer;

/// Startup of the NPC part (`NpcManager.Load` runtime side).
pub async fn start(_server: &Arc<GameServer>) -> anyhow::Result<()> {
    Ok(())
}

/// NPC C->S packets.
pub fn register(reg: &mut PacketRegistry) {
    packets::register(reg);
}
