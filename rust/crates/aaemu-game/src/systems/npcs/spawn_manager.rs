//! `SpawnManager` (one per world instance) (stub: TODO(npcs) — being ported).

use std::sync::Arc;

use crate::server::GameServer;

/// `WorldInstance.SpawnManager` of every instance.
#[derive(Debug, Default)]
pub struct SpawnManagers {}

/// `WorldManager.CreateStaticInstances` spawn part.
pub async fn start(_server: &Arc<GameServer>) -> anyhow::Result<()> {
    Ok(())
}

/// `Npc.DoDie` → `Spawner.DoDespawn(npc)`: schedules the despawn (corpse) and, when allowed,
/// the respawn of an NPC that died. No-op for NPCs without a spawner.
pub fn on_npc_died(_server: &GameServer, _npc: &crate::world::NpcRef) {}
