//! Phase-3 subsystem: skills / buffs / effects / plots.
//!
//! C# scope: `Core/Managers/SkillManager` (effects, plots, tags, modifiers, combat buffs),
//! `PlotManager`, `EffectTaskManager`, `ManaRegenManager`, `Models/Game/Skills/**`, the
//! behaviour half of `Models/Game/Units/Buffs` (`BuffTemplate.Start/Dispel`, tick / dispel
//! tasks, tolerance, triggers, `TriggerRemoveOn`), `Unit` combat (`ReduceCurrentHp`, `DoDie`,
//! regen, `UseSkill`), cooldown / GCD handling and the skill packets.
//!
//! Foundation to build on: `models::unit` (`UnitBase`: hp/mp, `Bonuses`, `UnitCooldowns`,
//! `SkillModifiers` / `BuffModifiers`, `CombatBuffs`), `models::buff` (`Buffs` bookkeeping +
//! `BuffEvent`s), `systems::UnitHooks` (install yours in `System::start`) and
//! `systems::dispatch_buff_events`.
//!
//! Module layout (see `CONTRIBUTING-PORT.md`): packets in `packets.rs` (C2G + G2C of this
//! subsystem), tables in `data.rs`, managers in their own files; everything is wired through
//! the five items below, which `systems/mod.rs` already calls.

use std::sync::Arc;

use crate::net::registry::PacketRegistry;
use crate::server::GameServer;
use crate::systems::{DataLoadCtx, SaveStage};

/// Game-data tables of this subsystem (`server.game_data.systems.skills`).
#[derive(Debug, Default)]
pub struct Data {}

impl Data {
    /// Loads this subsystem's tables from `ctx` (TODO(skills): port the C# loaders).
    pub fn load(_ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        Ok(Self::default())
    }
}

/// Managers of this subsystem (`server.systems.skills`); add one field per manager.
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
