//! Phase-3 subsystem: skills / buffs / effects / plots / combat.
//!
//! C# scope: `Core/Managers/SkillManager` (effects, plots, tags, modifiers, combat buffs),
//! `PlotManager`, `EffectTaskManager`, `ManaRegenManager`, `AnimationManager`,
//! `Models/Game/Skills/**`, the behaviour half of `Models/Game/Units/Buffs`
//! (`BuffTemplate.Start/Dispel`, tick / dispel tasks, tolerance, triggers, `TriggerRemoveOn`),
//! `Unit` combat (`ReduceCurrentHp`, `DoDie`, regen, aggro, `UseSkill`), cooldown / GCD handling
//! and the skill packets.
//!
//! Layout:
//! * [`data`] — every table (`server.game_data.systems.skills`);
//! * [`types`] — wire types (`SkillCaster`, `SkillCastTarget`, `SkillObject`, `CastAction`, ...);
//! * [`packets`] — the S->C and C->S packets of this subsystem.

pub mod data;
pub mod types;

use std::sync::Arc;

use crate::net::registry::PacketRegistry;
use crate::server::GameServer;
use crate::systems::SaveStage;

pub use data::Data;

/// Managers of this subsystem (`server.systems.skills`).
#[derive(Debug, Default)]
pub struct System {}

impl System {
    /// Startup after the phase-1/2 managers: hook installation, timers.
    pub async fn start(_server: &Arc<GameServer>) -> anyhow::Result<()> {
        Ok(())
    }

    /// This subsystem's part of `SaveManager.DoSave` (buffs are saved with the characters).
    pub async fn save(
        &self,
        _server: &GameServer,
        _tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
        _stage: SaveStage,
    ) -> crate::Result<usize> {
        Ok(0)
    }
}

/// Registers this subsystem's C->S packets.
pub fn register(_reg: &mut PacketRegistry) {}
