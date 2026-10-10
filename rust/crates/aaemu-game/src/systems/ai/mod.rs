//! Phase-4 subsystem: NPC AI, AI paths, geodata (navmesh) and physics.
//!
//! C# scope: `Models/Game/AI/**` (the v2 framework: `NpcAi`, behaviors, AI characters, params,
//! `AiPathHandler`, `AiUtils`, A* `PathNode`), `Core/Managers/AIManager`,
//! `Core/Managers/AiPathsManager` (`Data/Path/*.path`), `GameData/AiGameData`,
//! `NpcManager.LoadAiParams`, the AI members of `Npc` (`MoveTowards`, `LookTowards`,
//! `StopMovement`, `SetTarget`, `CurrentAggroTarget`, `BaseMoveSpeed`, `OnDamageReceived`,
//! `CheckIfEmptyAggroToReturn`, `ClearAllAggro`, `FindPath`), `NpcEvents`, the
//! `ModelManager` tables the AI and the physics read.
//!
//! | module | C# |
//! |---|---|
//! | [`lua`] | the NLua subset the parameter strings need |
//! | [`params`] | `Models/Game/AI/v2/Params/**`, AI enums |
//! | [`data`] | `AiGameData`, `ModelManager` (actor / ship models, stances) |

pub mod data;
pub mod lua;
pub mod params;

use std::sync::Arc;

use crate::net::registry::PacketRegistry;
use crate::server::GameServer;
use crate::systems::{DataLoadCtx, SaveStage};

/// Game-data tables of this subsystem (`server.game_data.systems.ai`).
#[derive(Debug, Default)]
pub struct Data {
    pub ai: data::AiData,
}

impl Data {
    /// Loads this subsystem's tables (a failing loader aborts startup).
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        Ok(Self {
            ai: data::AiData::load(ctx)?,
        })
    }
}

/// Managers of this subsystem (`server.systems.ai`).
#[derive(Debug, Default)]
pub struct System {}

impl System {
    /// Startup: AI paths, the AI tick, hook installation.
    pub async fn start(_server: &Arc<GameServer>) -> anyhow::Result<()> {
        Ok(())
    }

    /// Nothing of this subsystem is persisted.
    pub async fn save(
        &self,
        _server: &GameServer,
        _tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
        _stage: SaveStage,
    ) -> crate::Result<usize> {
        Ok(0)
    }
}

/// This subsystem has no C->S packets.
pub fn register(_reg: &mut PacketRegistry) {}
