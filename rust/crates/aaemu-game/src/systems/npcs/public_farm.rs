//! `PublicFarmManager` (stub: TODO(npcs) — being ported).

use std::sync::Arc;

use crate::net::registry::PacketRegistry;
use crate::server::GameServer;
use crate::systems::DataLoadCtx;

/// Game-data tables of `PublicFarmManager`.
#[derive(Debug, Default)]
pub struct PublicFarmData {}

impl PublicFarmData {
    pub fn load(_ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        Ok(Self::default())
    }
}

/// Runtime state of `PublicFarmManager`.
#[derive(Debug, Default)]
pub struct PublicFarmManager {}

/// Startup (loads, timers).
pub async fn start(_server: &Arc<GameServer>) -> anyhow::Result<()> {
    Ok(())
}

/// C->S packets.
pub fn register(_reg: &mut PacketRegistry) {}
