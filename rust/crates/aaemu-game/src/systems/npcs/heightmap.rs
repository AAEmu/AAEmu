//! `WorldCell` heightmaps + `WorldManager.GetHeight` (stub: TODO(npcs) — being ported).

use std::sync::Arc;

use crate::server::GameServer;

/// Heightmaps of every world template (lazy per cell).
#[derive(Debug, Default)]
pub struct Heightmaps {}

/// Startup (`WorldManager.LoadHeightmaps`).
pub async fn start(_server: &Arc<GameServer>) -> anyhow::Result<()> {
    Ok(())
}
