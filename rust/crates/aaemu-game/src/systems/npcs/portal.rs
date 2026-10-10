//! `PortalManager` (stub: TODO(npcs) — being ported).

use std::sync::Arc;

use crate::net::registry::PacketRegistry;
use crate::server::GameServer;
use crate::systems::DataLoadCtx;

/// Game-data tables of `PortalManager`.
#[derive(Debug, Default)]
pub struct PortalTables {}

impl PortalTables {
    pub fn load(_ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        Ok(Self::default())
    }
}

/// Runtime state of `PortalManager`.
#[derive(Debug, Default)]
pub struct PortalManager {}

/// Startup (loads, timers).
pub async fn start(_server: &Arc<GameServer>) -> anyhow::Result<()> {
    Ok(())
}

/// C->S packets.
pub fn register(_reg: &mut PacketRegistry) {}

/// This module's part of `SaveManager.DoSave`.
pub async fn save(
    _server: &GameServer,
    _tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
    _stage: crate::systems::SaveStage,
) -> crate::Result<usize> {
    Ok(0)
}
