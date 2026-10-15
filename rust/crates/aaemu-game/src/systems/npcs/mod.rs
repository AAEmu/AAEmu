//! Phase-3 subsystem: NPCs / doodads / spawns and the world-object driven managers.
//!
//! C# scope: `Core/Managers/UnitManagers/NpcManager`, `DoodadManager`, `Core/Managers/World/
//! SpawnManager`, `GimmickManager`, `AreaTriggerManager`, `PortalManager`, `SubZoneManager`,
//! `FishSchoolManager`, `PublicFarmManager`, `SpecialtyManager`, `SphereQuestManager` (world
//! side), `Models/Game/NPChar/**` (templates, spawners), `Models/Game/DoodadObj/**` (templates,
//! funcs, phases, spawners, persistence), `Models/Game/World/Interactions`, heightmaps.
//! The NPC AI is a later phase (`Npc::has_ai` / `ai_should_tick` stay the hook).
//!
//! Layout (one owner per file, see `CONTRIBUTING-PORT.md`):
//!
//! | module | C# |
//! |---|---|
//! | [`json`] | `JsonHelper` (Newtonsoft-lenient JSON for `Data/Worlds/**`) |
//! | [`hooks`] | the behaviour this subsystem needs from skills / economy / quests |
//! | [`npc`] | `NpcManager`, `NpcGameData`, `Npc` stats, `NpcSpawner`, NPC interaction packets |
//! | [`doodad`] | `DoodadManager`, `Doodad` funcs / phases, `DoodadSpawner`, persistence, `SCDoodad*` |
//! | [`spawn_manager`] | `SpawnManager` (one per world instance) |
//! | [`heightmap`] | `WorldCell` heightmaps, `WorldManager.GetHeight` |
//! | [`gimmick`] | `GimmickManager`, `GimmickGameData`, `GimmickSpawner` |
//! | [`area_trigger`], [`subzone`], [`sphere_quest`] | `AreaTriggerManager`, `SubZoneManager`, `SphereQuestManager` |
//! | [`portal`], [`specialty`], [`public_farm`], [`fish_school`] | `PortalManager`, `SpecialtyManager`, `PublicFarmManager`, `FishSchoolManager` |

pub mod area_trigger;
pub mod bridges;
pub mod doodad;
pub mod fish_school;
pub mod gimmick;
pub mod heightmap;
pub mod hooks;
pub mod json;
pub mod npc;
pub mod portal;
pub mod public_farm;
pub mod schedule;
pub mod schedule_item_manager;
pub mod schedule_item_packets;
pub mod schedule_item_persistence;
pub mod schedule_item_runtime;
pub mod schedule_item_session;
pub mod schedule_items;
pub mod spawn_manager;
pub mod specialty;
pub mod sphere_quest;
pub mod subzone;

use std::sync::Arc;

use crate::net::registry::PacketRegistry;
use crate::server::GameServer;
use crate::systems::{DataLoadCtx, SaveStage};

/// Game-data tables of this subsystem (`server.game_data.systems.npcs`).
#[derive(Debug, Default)]
pub struct Data {
    /// `NpcManager` templates, customs, body parts, postures, merchant goods.
    pub npc: npc::data::NpcData,
    /// `NpcGameData` (spawner templates and members).
    pub npc_spawners: npc::spawner_data::NpcSpawnerData,
    /// `DoodadManager` templates, func groups, funcs, phase funcs and func templates.
    pub doodad: doodad::data::DoodadData,
    /// `GimmickGameData`.
    pub gimmick: gimmick::GimmickData,
    /// `AreaTriggerManager` / `aoe_shapes` / `WorldManager` world-interaction groups.
    pub area_trigger: area_trigger::AreaTriggerData,
    /// `SubZoneManager`.
    pub subzone: subzone::SubZoneData,
    /// `SphereQuestManager` / `SphereGameData` (world side).
    pub sphere: sphere_quest::SphereData,
    /// `PortalManager` (open portals, world gates, teleports).
    pub portal: portal::PortalTables,
    /// `SpecialtyManager`.
    pub specialty: specialty::SpecialtyData,
    /// `PublicFarmManager` / `CommonFarmGameData`.
    pub public_farm: public_farm::PublicFarmData,
    /// `FishSchoolManager` / `FishDetailsGameData`.
    pub fish_school: fish_school::FishSchoolData,
    /// `GameScheduleManager` / `SchedulesGameData`.
    pub schedule: schedule::ScheduleData,
    /// `ScheduleItemGameData` (`schedule_items`).
    pub schedule_items: schedule_items::ScheduleItemData,
}

impl Data {
    /// Loads this subsystem's tables from `ctx` (a failing loader aborts startup).
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        Ok(Self {
            npc: npc::data::NpcData::load(ctx)?,
            npc_spawners: npc::spawner_data::NpcSpawnerData::load(ctx)?,
            doodad: doodad::data::DoodadData::load(ctx)?,
            gimmick: gimmick::GimmickData::load(ctx)?,
            area_trigger: area_trigger::AreaTriggerData::load(ctx)?,
            subzone: subzone::SubZoneData::load(ctx)?,
            sphere: sphere_quest::SphereData::load(ctx)?,
            portal: portal::PortalTables::load(ctx)?,
            specialty: specialty::SpecialtyData::load(ctx)?,
            public_farm: public_farm::PublicFarmData::load(ctx)?,
            fish_school: fish_school::FishSchoolData::load(ctx)?,
            schedule: schedule::ScheduleData::load(ctx)?,
            schedule_items: schedule_items::ScheduleItemData::load(ctx)?,
        })
    }
}

/// Managers of this subsystem (`server.systems.npcs`).
#[derive(Debug, Default)]
pub struct System {
    /// `NpcManager` (runtime part).
    pub npcs: npc::manager::NpcManager,
    /// `DoodadManager` (runtime part: coffers, player doodads, phase timers).
    pub doodads: doodad::manager::DoodadManager,
    /// `WorldInstance.SpawnManager` for every instance.
    pub spawns: spawn_manager::SpawnManagers,
    /// `WorldCell` heightmaps (lazy, per world template).
    pub heightmaps: heightmap::Heightmaps,
    /// `WorldInstance.GimmickManager` for every instance.
    pub gimmicks: gimmick::GimmickManager,
    /// `AreaTriggerManager`.
    pub area_triggers: area_trigger::AreaTriggerManager,
    /// `SphereQuestManager` (world side).
    pub spheres: sphere_quest::SphereQuestManager,
    /// `PortalManager` (runtime).
    pub portals: portal::PortalManager,
    /// `SpecialtyManager` (runtime ratios).
    pub specialty: specialty::SpecialtyManager,
    /// `PublicFarmManager`.
    pub public_farms: public_farm::PublicFarmManager,
    /// `FishSchoolManager`.
    pub fish_schools: fish_school::FishSchoolManager,
    /// `ScheduleItemManager` (account-wide schedule-item progress and its prepared save).
    pub schedule_items: schedule_item_manager::ScheduleItemManager,
    /// Behaviour of other subsystems this one calls (installed by them at start).
    pub hooks: hooks::NpcsHooks,
}

impl System {
    /// Startup after the phase-1/2 managers (`ManagerOrchestrator` order): heightmaps, spawn
    /// data, persistent doodads, spawning, timers.
    pub async fn start(server: &Arc<GameServer>) -> anyhow::Result<()> {
        install_hooks(server);
        heightmap::start(server).await?;
        area_trigger::start(server).await?;
        portal::start(server).await?;
        specialty::start(server).await?;
        public_farm::start(server).await?;
        sphere_quest::start(server).await?;
        doodad::start(server).await?;
        npc::start(server).await?;
        gimmick::start(server).await?;
        // FishSchoolManager.Initialize (its Load(world) runs at the end of each doodad spawn).
        fish_school::start(server).await?;
        // `WorldManager.CreateStaticInstances`: SpawnManager.Load + SpawnAll for main_world.
        spawn_manager::start(server).await?;
        // `ScheduleItemManager.Load`: the saved account progress, then the accrual tick.
        let accounts = server
            .systems
            .npcs
            .schedule_items
            .restore(&server.db)
            .await?;
        tracing::info!("Loaded schedule item progress of {accounts} accounts");
        schedule_item_session::start_tick_task(server);
        Ok(())
    }

    /// This subsystem's part of `SaveManager.DoSave` (inside its transaction). Returns the
    /// number of rows written.
    pub async fn save(
        &self,
        server: &GameServer,
        tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
        stage: SaveStage,
    ) -> crate::Result<usize> {
        let mut n = 0;
        n += doodad::save(server, tx, stage).await?;
        n += portal::save(server, tx, stage).await?;
        Ok(n)
    }
}

/// Registers this subsystem's C->S packets.
pub fn register(reg: &mut PacketRegistry) {
    npc::register(reg);
    doodad::register(reg);
    portal::register(reg);
    specialty::register(reg);
    public_farm::register(reg);
    subzone::register(reg);
    gimmick::register(reg);
    fish_school::register(reg);
    area_trigger::register(reg);
    sphere_quest::register(reg);
    schedule_item_packets::register(reg);
}

#[cfg(test)]
mod flow_tests;
#[cfg(test)]
mod manager_tests;
#[cfg(test)]
mod schedule_item_session_tests;
#[cfg(test)]
mod spawn_tests;

/// Installs the NPC subsystem's implementations of the other subsystems' hooks (no I/O; also
/// used by the flow tests).
pub fn install_hooks(server: &GameServer) {
    bridges::install(server);
}
