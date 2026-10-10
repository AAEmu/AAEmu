//! Phase-3 subsystem: quests.
//!
//! C# scope: `QuestManager`, `Models/Game/Quests/**` (templates, acts, components, the
//! quest state machine on `Character.Quests`), quest packets, quest-giver interaction with
//! NPCs / doodads (`systems::npcs` objects through `world::object`).
//!
//! Module layout (see `CONTRIBUTING-PORT.md`): packets in `packets.rs` (C2G + G2C of this
//! subsystem), tables in `data.rs`, managers in their own files; everything is wired through
//! the five items below, which `systems/mod.rs` already calls.

use std::sync::Arc;

use crate::net::registry::PacketRegistry;
use crate::server::GameServer;
use crate::systems::{DataLoadCtx, SaveStage};

/// Game-data tables of this subsystem (`server.game_data.systems.quests`).
#[derive(Debug, Default)]
pub struct Data {}

impl Data {
    /// Loads this subsystem's tables from `ctx` (TODO(quests): port the C# loaders).
    pub fn load(_ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        Ok(Self::default())
    }
}

/// Managers of this subsystem (`server.systems.quests`); add one field per manager.
#[derive(Debug, Default)]
pub struct System {}

impl System {
    /// Startup after the phase-1/2 managers: DB loads, timers, hook installation.
    pub async fn start(_server: &Arc<GameServer>) -> anyhow::Result<()> {
        Ok(())
    }

    /// This subsystem's part of `SaveManager.DoSave` (inside its transaction). Returns the
    /// number of rows written.
    pub async fn save(
        &self,
        _server: &GameServer,
        _tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
        _stage: SaveStage,
    ) -> crate::Result<usize> {
        Ok(0)
    }
}

/// Registers this subsystem's C->S packets (`reg.register::<P>(LEVEL_PLAIN, cs::P)`).
pub fn register(_reg: &mut PacketRegistry) {}
