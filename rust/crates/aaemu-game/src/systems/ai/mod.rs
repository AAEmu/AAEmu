//! Phase-4 subsystem: NPC AI, AI paths, geodata (navmesh) and physics.
//!
//! C# scope: `Models/Game/AI/**` (the v2 framework: `NpcAi`, behaviors, AI characters, params,
//! `AiPathHandler`, `AiUtils`, A* `PathNode`), `Core/Managers/AIManager`,
//! `Core/Managers/AiPathsManager` (`Data/Path/*.path`), `Core/Managers/AiGeodataManager` +
//! `Models/CryEngine` navigation data, `GameData/AiGameData`, `NpcManager.LoadAiParams`, the AI
//! members of `Npc` (`MoveTowards`, `LookTowards`, `StopMovement`, `SetTarget`,
//! `CurrentAggroTarget`, `CurrentGameStance`, `BaseMoveSpeed`, `OnDamageReceived`,
//! `CheckIfEmptyAggroToReturn`, `ClearAllAggro`, `FindPath`), `NpcEvents`,
//! `Units/Route/Simulation`, `Gimmick.MoveTowards`, the `ModelManager` tables the AI and the
//! physics read.
//!
//! | module | C# |
//! |---|---|
//! | [`lua`] | the NLua subset the parameter strings need |
//! | [`params`] | `Models/Game/AI/v2/Params/**`, AI enums |
//! | [`data`] | `AiGameData`, `ModelManager` (actor / ship models, stances) |
//! | [`framework`] | `NpcAi`, `BehaviorKind`, transitions, `AiCharacters/**`, `AiUtils.GetAiByType` |
//! | [`behaviors`] | `Behavior`, `BaseCombatBehavior` and every behavior |
//! | [`npc`], [`combat`], [`skills`], [`npc_events`] | the AI members of `Npc` / `Unit`, `NpcEvents` |
//! | [`path_handler`] | `AiPathHandler`, `AiPathPoint`, `AiPathsManager` |
//! | [`simulation`] | `Units/Route/Simulation` (+ `Move` / `NpcDeleteTask`) |
//! | [`gimmick`] | `Gimmick.MoveTowards`, the flytrap gimmick |
//! | [`geodata`] | `AiGeoDataManager`, `.bai` readers, A* `PathNode` |
//! | [`manager`] | `AIManager` (registry, 100 ms tick, posting calls to an AI) |
//! | [`hooks`] | the AI entry points for npcs / skills |
//! | [`physics`] | `Physics/**`, `PhysicsManager`, `WaterBodies` (ship physics, water) |

pub mod behaviors;
pub mod combat;
pub mod data;
pub mod framework;
pub mod geodata;
pub mod gimmick;
pub mod hooks;
pub mod lua;
pub mod manager;
pub mod npc;
pub mod npc_events;
pub mod numerics;
pub mod packets;
pub mod params;
pub mod path_handler;
pub mod physics;
pub mod simulation;
pub mod skills;

#[cfg(test)]
mod flow_tests;
#[cfg(test)]
mod real_data_tests;

use std::path::PathBuf;
use std::sync::{Arc, Mutex};

use dashmap::DashMap;

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
pub struct System {
    /// `AIManager`.
    pub ais: manager::AiManager,
    /// `AiPathsManager`.
    pub paths: path_handler::AiPathsManager,
    /// `npc.Simulation` (NPCs spawned by a spawner).
    pub simulations: DashMap<u32, simulation::SimRef>,
    /// `WorldTemplate.GeoData` of every template.
    pub geodata: geodata::GeoDataStore,
    /// `Gimmick.Target` / barrel angles of the gimmicks moved by NPCs.
    pub gimmick_moves: gimmick::GimmickMoves,
    /// `WorldInstance.Physics` / `WorldInstance.Water`.
    pub physics: physics::PhysicsSystem,
    /// `Data/Path` (set at start).
    paths_folder: Mutex<Option<PathBuf>>,
}

impl System {
    /// Startup (`ManagerOrchestrator`: `AiPathsManager.Load`, `AIManager.Initialize`, the
    /// geodata of every world template) and hook installation.
    pub async fn start(server: &Arc<GameServer>) -> anyhow::Result<()> {
        let folder = server.config.app_path.join("Data").join("Path");
        server.systems.ai.set_paths_folder(folder);
        let srv = server.clone();
        tokio::task::spawn_blocking(move || {
            srv.systems.ai.paths.load();
            geodata::start(&srv);
        })
        .await?;
        hooks::install(server);
        manager::start_tick(server);
        Ok(())
    }

    /// Sets `Data/Path` for both the AI paths and the simulations.
    pub fn set_paths_folder(&self, folder: PathBuf) {
        self.paths.set_folder(&folder);
        *self.paths_folder.lock().expect("paths folder poisoned") = Some(folder);
    }

    /// `Data/Path`.
    pub fn paths_folder(&self) -> Option<PathBuf> {
        self.paths_folder
            .lock()
            .expect("paths folder poisoned")
            .clone()
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
