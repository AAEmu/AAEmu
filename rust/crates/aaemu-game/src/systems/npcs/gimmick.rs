//! `GimmickManager` (stub: TODO(npcs) — being ported).

use std::sync::Arc;

use crate::net::registry::PacketRegistry;
use crate::server::GameServer;
use crate::systems::DataLoadCtx;

/// Game-data tables of `GimmickManager`.
#[derive(Debug, Default)]
pub struct GimmickData {}

impl GimmickData {
    pub fn load(_ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        Ok(Self::default())
    }
}

/// Runtime state of `GimmickManager`.
#[derive(Debug, Default)]
pub struct GimmickManager {}

/// Startup (loads, timers).
pub async fn start(_server: &Arc<GameServer>) -> anyhow::Result<()> {
    Ok(())
}

/// C->S packets.
pub fn register(_reg: &mut PacketRegistry) {}
