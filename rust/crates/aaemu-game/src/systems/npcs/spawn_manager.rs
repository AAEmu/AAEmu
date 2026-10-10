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
