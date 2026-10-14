//! NPCs: `NpcManager` (templates, `Create`, stats), `NpcGameData` (spawner templates),
//! `NpcSpawner` / `NpcSpawnerNpc`, NPC interaction packets, `Npc` attribute getters ([`stats`])
//! and `Npc.DoDie` ([`death`]).

pub mod data;
pub mod death;
pub mod manager;
pub mod packets;
pub mod residency;
pub mod spawner;
pub mod spawner_data;
pub mod stats;

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
