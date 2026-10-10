//! Phase-4 subsystem: instances — dungeons (`Indun`), system instances (Mirage Island,
//! Library), instant games (battlefields / arenas), tower defense data and packets, and the
//! world-instance lifecycle they need.
//!
//! C# scope: `IndunManager` (+ `Models/Game/Indun/**`, `GameData/IndunGameData`),
//! `InstantGameManager` (+ `Models/Game/InstantGame/**`, `GameData/BattlefieldGameData`),
//! `Models/Game/TowerDefs/**` (+ `GameData/TowerDefGameData`), the instance parts of
//! `WorldManager` (`CreateWorldInstance`, `RemoveWorld`, `CreateStaticInstances` dungeon list,
//! `WorldInstance.CleanupInstance`), `Character.MainWorldPosition` handling, and the packets
//! `CSInstanceLoaded`, `CSUnknownInstance`, `CS*InstantGame*`, `SCProcessingInstance`,
//! `SC*InstantGame*`, `SCTowerDef*`, `SCSkillCooldownReset`.
//!
//! Module layout: [`data`] (game data), [`worlds`] (instance creation / removal, transforms),
//! [`dungeon`] (`Dungeon`, `DungeonLoaderTask`), [`indun`] (`IndunManager`), [`events`]
//! (character / world events, indun events and actions), [`instant_game`]
//! (`InstantGameManager`, `InstantGame`), [`packets`], [`config`] (`Dungeons` section).
//!
//! Tower defense: the C# only loads `TowerDefGameData` and has packets (sent by the
//! `towerdef` GM command, not ported with the command system); there is no runtime manager.

pub mod config;
pub mod data;
pub mod dungeon;
pub mod events;
pub mod indun;
pub mod instant_game;
pub mod packets;
pub mod worlds;

#[cfg(test)]
mod flow_tests;
#[cfg(test)]
mod manager_tests;
#[cfg(test)]
mod tests;

use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::Arc;
use std::time::Duration;

use dashmap::DashMap;

use crate::net::registry::PacketRegistry;
use crate::server::GameServer;
use crate::systems::{DataLoadCtx, SaveStage};

use dungeon::Dungeon;

/// Game-data tables of this subsystem (`server.game_data.systems.instances`).
#[derive(Debug, Default)]
pub struct Data {
    /// `IndunGameData`.
    pub indun: data::IndunData,
    /// `BattlefieldGameData`.
    pub battlefields: data::BattlefieldData,
    /// `TowerDefGameData`.
    pub tower_defs: data::TowerDefData,
    /// `ZoneManager._zones` order (zone keys of a zone group).
    pub zone_order: data::ZoneOrder,
}

impl Data {
    /// Loads this subsystem's tables from `ctx` (`Data/battlefields.json` must exist, as in C#).
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        data::load_all(ctx)
    }
}

/// Managers of this subsystem (`server.systems.instances`).
#[derive(Debug, Default)]
pub struct System {
    /// `IndunManager` (entry history).
    pub indun: indun::IndunManager,
    /// `WorldInstance.DungeonInstance` by instance id.
    pub dungeons: DashMap<u32, Arc<Dungeon>>,
    /// Instance members the world module does not carry (channel ids).
    pub worlds: worlds::InstanceWorlds,
    /// Character / world event subscriptions.
    pub events: events::Events,
    /// `InstantGameManager`.
    pub instant_games: instant_game::InstantGameManager,
    next_uid: AtomicU64,
    ticks_started: AtomicBool,
}

impl System {
    /// Startup after the NPC subsystem (spawns exist): `CreateStaticInstances` (the
    /// `Dungeons.AutoCreate` system instances), the indun / dungeon / battlefield ticks.
    pub async fn start(server: &Arc<GameServer>) -> anyhow::Result<()> {
        for cfg in server.config.dungeons.auto_create.clone() {
            let Some(zone_key) = server
                .game_data
                .worlds
                .get_world_template_by_name(&cfg.name)
                .and_then(|t| t.zone_keys.first().copied())
            else {
                tracing::error!("Dungeons.AutoCreate: unknown world {}", cfg.name);
                continue;
            };
            let _ =
                indun::create_system_instance(server, None, zone_key, cfg.channel, true, cfg.id);
        }
        start_ticks(server);
        Ok(())
    }

    /// This subsystem's part of `SaveManager.DoSave` (nothing is persisted).
    pub async fn save(
        &self,
        _server: &GameServer,
        _tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
        _stage: SaveStage,
    ) -> crate::Result<usize> {
        Ok(0)
    }

    /// A new object identity (dungeons, instant games).
    pub fn next_uid(&self) -> u64 {
        self.next_uid.fetch_add(1, Ordering::Relaxed) + 1
    }

    /// `world.DungeonInstance`.
    pub fn dungeon_of_world(&self, world_id: u32) -> Option<Arc<Dungeon>> {
        self.dungeons.get(&world_id).map(|d| d.clone())
    }

    /// Every dungeon in instance id order.
    pub fn all_dungeons(&self) -> Vec<Arc<Dungeon>> {
        let mut v: Vec<(u32, Arc<Dungeon>)> = self
            .dungeons
            .iter()
            .map(|e| (*e.key(), e.value().clone()))
            .collect();
        v.sort_by_key(|(k, _)| *k);
        v.into_iter().map(|(_, d)| d).collect()
    }
}

/// One second of the instance ticks: `AreaClearTick` (1 s) of every dungeon,
/// `LeaveDungeonTick` (5 s), `IndunInfoTick` (30 s), `BattlefieldTick` (15 s).
pub fn tick(server: &GameServer, second: u64) {
    for d in server.systems.instances.all_dungeons() {
        if d.st().area_clear_tick {
            dungeon::area_clear_tick(server, &d);
        }
    }
    if second.is_multiple_of(5) {
        for d in server.systems.instances.all_dungeons() {
            if d.st().leave_tick {
                dungeon::leave_dungeon_tick(server, &d);
            }
        }
    }
    if second.is_multiple_of(15) {
        instant_game::battlefield_tick(server);
    }
    if second.is_multiple_of(30) {
        indun::indun_info_tick(server);
    }
}

fn start_ticks(server: &Arc<GameServer>) {
    if server
        .systems
        .instances
        .ticks_started
        .swap(true, Ordering::SeqCst)
    {
        return;
    }
    let weak = Arc::downgrade(server);
    tokio::spawn(async move {
        let mut iv = tokio::time::interval(Duration::from_secs(1));
        let mut second = 0u64;
        loop {
            iv.tick().await;
            second += 1;
            let Some(server) = weak.upgrade() else {
                break;
            };
            tick(&server, second);
        }
    });
}

/// Registers this subsystem's C->S packets.
pub fn register(reg: &mut PacketRegistry) {
    packets::register(reg);
}
