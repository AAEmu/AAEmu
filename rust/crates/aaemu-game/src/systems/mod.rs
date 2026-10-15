//! Pluggable game subsystems (phase 3+). Each subsystem lives in its own module and owns its
//! packets, game-data tables, managers and DB persistence, so parallel work never edits the
//! same lines of a shared file:
//!
//! | module | C# scope |
//! |---|---|
//! | [`skills`] | SkillManager (effects, plots, tags, modifiers), buffs / effects behaviour, cooldowns, combat, regen |
//! | [`npcs`] | NpcManager, DoodadManager, SpawnManager, AI, GimmickManager, loot drops of NPCs |
//! | [`economy`] | item actions / tasks, crafting, loot, mail, auction, trade, cash shop |
//! | [`social`] | team, expedition (guild management), family, friends, duel, crime / trial |
//! | [`quests`] | QuestManager, quest templates / acts / components, quest UI |
//! | [`instances`] | IndunManager (dungeons), InstantGameManager (battlefields), tower defs, world instances |
//! | [`housing`] | HousingManager, HousingGameData, house packets, taxes, house persistence |
//! | [`vehicles`] | MateManager, SlaveManager, ShipyardManager, TransferManager (mounts, vehicles, ships, carriages) |
//! | [`services`] | WebApi, Discord bot, Tick / Task managers, timed rewards, accounts, music, radar, sus, commands infrastructure, misc managers |
//! | [`ai`] | AIManager, NPC AI (behaviors, params, paths), geodata / navmesh, physics |
//!
//! Every subsystem module exposes the same five hooks (see `CONTRIBUTING-PORT.md`):
//!
//! * `pub fn register(reg: &mut PacketRegistry)` — its C->S packets;
//! * `pub struct Data` + `Data::load(&DataLoadCtx)` — its compact.sqlite3 / client file tables,
//!   reachable as `server.game_data.systems.<name>`;
//! * `pub struct System` — its managers (one field per manager), reachable as
//!   `server.systems.<name>`;
//! * `System::start(&Arc<GameServer>)` — DB loads / timers / hook installation at startup;
//! * `System::save(&GameServer, &mut tx, SaveStage)` — its part of `SaveManager.DoSave`.
//!
//! This file is the only central list; adding a *new* subsystem adds one line per list below.

pub mod ai;
pub mod economy;
pub mod housing;
pub mod instances;
pub mod npcs;
pub mod quests;
pub mod services;
pub mod skills;
pub mod social;
pub mod vehicles;

#[cfg(test)]
mod integration_tests;

use std::path::Path;
use std::sync::{Arc, RwLock};

use rusqlite::Connection;
use sqlx::MySql;

use crate::data::client_files::ClientFileManager;
use crate::data::GameDataError;
use crate::models::buff::BuffEvent;
use crate::net::registry::PacketRegistry;
use crate::server::GameServer;
use crate::world::{PlayerRef, WorldObject};

/// What a subsystem's `Data::load` gets (the same sources `GameData::load` uses).
pub struct DataLoadCtx<'a> {
    /// `Data/compact.sqlite3` (read-only).
    pub conn: &'a Connection,
    /// The server's app path (`Data/*.json` lives under `app_path/Data`).
    pub app_path: &'a Path,
    /// The client files (`ClientData.Sources`: directories / game_pak).
    pub files: &'a ClientFileManager,
    /// `DefaultLanguage`.
    pub language: &'a str,
}

/// The game-data tables of every subsystem (`server.game_data.systems`).
#[derive(Debug, Default)]
pub struct SystemsData {
    pub skills: skills::Data,
    pub npcs: npcs::Data,
    pub economy: economy::Data,
    pub social: social::Data,
    pub quests: quests::Data,
    pub instances: instances::Data,
    pub housing: housing::Data,
    pub vehicles: vehicles::Data,
    pub services: services::Data,
    pub ai: ai::Data,
}

impl SystemsData {
    /// Loads every subsystem's tables (a failing loader aborts startup, as in the 1.2 C#).
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, GameDataError> {
        Ok(Self {
            skills: skills::Data::load(ctx)?,
            npcs: npcs::Data::load(ctx)?,
            economy: economy::Data::load(ctx)?,
            social: social::Data::load(ctx)?,
            quests: quests::Data::load(ctx)?,
            instances: instances::Data::load(ctx)?,
            housing: housing::Data::load(ctx)?,
            vehicles: vehicles::Data::load(ctx)?,
            services: services::Data::load(ctx)?,
            ai: ai::Data::load(ctx)?,
        })
    }
}

/// The managers of every subsystem (`server.systems`).
#[derive(Debug, Default)]
pub struct Systems {
    pub skills: skills::System,
    pub npcs: npcs::System,
    pub economy: economy::System,
    pub social: social::System,
    pub quests: quests::System,
    pub instances: instances::System,
    pub housing: housing::System,
    pub vehicles: vehicles::System,
    pub services: services::System,
    pub ai: ai::System,
    /// Behaviour hooks shared code calls into (installed by the owning subsystem at start).
    pub hooks: Hooks,
}

/// Registers every subsystem's C->S packets (called by `packets::build_registry`).
pub fn register_packets(reg: &mut PacketRegistry) {
    skills::register(reg);
    npcs::register(reg);
    economy::register(reg);
    social::register(reg);
    quests::register(reg);
    instances::register(reg);
    housing::register(reg);
    vehicles::register(reg);
    services::register(reg);
    ai::register(reg);
}

/// Starts every subsystem after the phase-1/2 managers are up (`ManagerOrchestrator` order:
/// skills before NPCs / spawns before quests).
/// Runs a synchronous periodic tick body on the blocking pool and waits for it. World ticks take
/// std locks and can run for seconds (e.g. spawning the NPCs of newly active regions); run on an
/// async worker they stall the connection tasks queued behind them on that worker (the LIFO
/// slot is not stealable), which showed up as multi-second packet delays under live testing.
pub async fn run_blocking<F>(server: &Arc<GameServer>, f: F)
where
    F: FnOnce(&GameServer) + Send + 'static,
{
    let s = server.clone();
    if let Err(e) = tokio::task::spawn_blocking(move || f(&s)).await {
        tracing::error!("tick task failed: {e}");
    }
}

pub async fn start_all(server: &Arc<GameServer>) -> anyhow::Result<()> {
    tracing::debug!("Starting subsystem skills");
    skills::System::start(server).await?;
    tracing::debug!("Starting subsystem npcs");
    npcs::System::start(server).await?;
    tracing::debug!("Starting subsystem economy");
    economy::System::start(server).await?;
    tracing::debug!("Starting subsystem social");
    social::System::start(server).await?;
    tracing::debug!("Starting subsystem quests");
    quests::System::start(server).await?;
    tracing::debug!("Starting subsystem instances");
    instances::System::start(server).await?;
    tracing::debug!("Starting subsystem housing");
    housing::System::start(server).await?;
    tracing::debug!("Starting subsystem vehicles");
    vehicles::System::start(server).await?;
    tracing::debug!("Starting subsystem services");
    services::System::start(server).await?;
    tracing::debug!("Starting subsystem ai");
    ai::System::start(server).await?;
    Ok(())
}

/// Where a subsystem's rows go in `SaveManager.DoSave` (C# order: houses, mails, items,
/// auction, crimes, characters, slaves).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SaveStage {
    /// Before the items (houses, mails).
    BeforeItems,
    /// After the items, before the characters (auction, crimes).
    AfterItems,
    /// After the characters (slaves, per-character side tables).
    AfterCharacters,
}

/// Runs one save stage for every subsystem inside the `DoSave` transaction. Returns the number
/// of rows written (0 = nothing to commit).
pub async fn save_stage(
    server: &GameServer,
    tx: &mut sqlx::Transaction<'_, MySql>,
    stage: SaveStage,
) -> crate::Result<usize> {
    save_stage_inner(server, tx, stage, false).await
}

/// World saves already captured housing, territory authority and mail with item/wallet
/// snapshots. Their prepared batches are written by SaveManager, so do not recapture them.
pub(crate) async fn save_prepared_financial_stage(
    server: &GameServer,
    tx: &mut sqlx::Transaction<'_, MySql>,
    stage: SaveStage,
) -> crate::Result<usize> {
    save_stage_inner(server, tx, stage, true).await
}

async fn save_stage_inner(
    server: &GameServer,
    tx: &mut sqlx::Transaction<'_, MySql>,
    stage: SaveStage,
    prepared_financials: bool,
) -> crate::Result<usize> {
    let s = &server.systems;
    let mut n = 0;
    n += s.skills.save(server, tx, stage).await?;
    n += s.npcs.save(server, tx, stage).await?;
    // C# DoSave order: houses before mails.
    if !prepared_financials {
        n += s.housing.save(server, tx, stage).await?;
        n += s.economy.save(server, tx, stage).await?;
    }
    n += s.social.save(server, tx, stage).await?;
    n += s.quests.save(server, tx, stage).await?;
    n += s.instances.save(server, tx, stage).await?;
    n += s.vehicles.save(server, tx, stage).await?;
    n += s.services.save(server, tx, stage).await?;
    n += s.ai.save(server, tx, stage).await?;
    Ok(n)
}

/// Behaviour that lives in a subsystem but is triggered by shared code (the C# virtual calls
/// that cross module boundaries). All methods default to "not ported yet" no-ops.
pub trait UnitHooks: Send + Sync {
    /// A buff entered / was refreshed in / left an object's `Buffs` (drained by
    /// [`dispatch_buff_events`]). The skills subsystem implements `BuffTemplate.Start` /
    /// `Dispel`, modifier caches, combat buffs, `SCBuffCreatedPacket` / `SCBuffRemovedPacket`.
    fn on_buff_event(&self, _server: &GameServer, _owner: &WorldObject, _event: &BuffEvent) {}

    /// `Unit.OnZoneChange(last, new)` (zone group buffs) for every unit; for players it runs
    /// first in `world::session::on_zone_change` (`base.OnZoneChange`).
    fn on_unit_zone_change(
        &self,
        _server: &GameServer,
        _unit: &WorldObject,
        _last_zone: u32,
        _new_zone: u32,
    ) {
    }
}

/// A replaceable trait object (install once at start, read lock-free-ish afterwards).
pub struct HookSlot<T: ?Sized>(RwLock<Option<Arc<T>>>);

impl<T: ?Sized> Default for HookSlot<T> {
    fn default() -> Self {
        Self(RwLock::new(None))
    }
}

impl<T: ?Sized> std::fmt::Debug for HookSlot<T> {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(if self.get().is_some() {
            "HookSlot(installed)"
        } else {
            "HookSlot(empty)"
        })
    }
}

impl<T: ?Sized> HookSlot<T> {
    pub fn install(&self, hook: Arc<T>) {
        *self.0.write().expect("hook poisoned") = Some(hook);
    }

    pub fn get(&self) -> Option<Arc<T>> {
        self.0.read().expect("hook poisoned").clone()
    }
}

/// The hook slots (`server.systems.hooks`).
#[derive(Debug, Default)]
pub struct Hooks {
    /// Installed by the skills subsystem.
    pub unit: HookSlot<dyn UnitHooks>,
    /// Installed by the ai subsystem (`npc.Ai` call sites of npcs / skills).
    pub ai: HookSlot<dyn ai::hooks::AiHooks>,
    /// Installed by the slaves subsystem (harpoon rope state for the ship physics).
    pub ship_physics: HookSlot<dyn ai::physics::ShipPhysicsHooks>,
    /// Installed by the social subsystem (achievement record producers of every subsystem).
    pub achievements: HookSlot<dyn social::achievement_hooks::AchievementHooks>,
}

/// Reports gameplay events to the achievement subsystem (no-op when its hooks are not
/// installed). Call with no character / `ItemTxn` / container lock held.
pub fn report_achievements(
    server: &GameServer,
    player: &PlayerRef,
    inputs: &[social::achievement_hooks::AchievementInput],
) {
    if let Some(h) = server.systems.hooks.achievements.get() {
        h.report(server, player, inputs);
    }
}

/// [`report_achievements`] for a single event.
pub fn report_achievement(
    server: &GameServer,
    player: &PlayerRef,
    input: social::achievement_hooks::AchievementInput,
) {
    report_achievements(server, player, &[input]);
}

/// Like [`refresh_achievements`] for a caller that holds a lock the report path also takes (the
/// team, family and expedition managers): the refresh is queued and runs from
/// [`flush_achievements`].
pub fn refresh_achievements_later(server: &GameServer, player: &PlayerRef) {
    if let Some(h) = server.systems.hooks.achievements.get() {
        h.refresh_later(player);
    }
}

/// Runs the queued achievement events (call right after releasing the economy operation lock or
/// at the end of a packet, with no character lock held).
pub fn flush_achievements(server: &GameServer) {
    if let Some(h) = server.systems.hooks.achievements.get() {
        h.flush_deferred(server);
    }
}

/// Re-reports everything the character's current state proves (level, abilities, memberships,
/// completed quests, ...) to the achievement subsystem. Same locking rule as
/// [`report_achievements`].
pub fn refresh_achievements(server: &GameServer, player: &PlayerRef) {
    if let Some(h) = server.systems.hooks.achievements.get() {
        h.refresh_current(server, player);
    }
}

/// Drains `owner`'s buff events and hands them to [`UnitHooks::on_buff_event`]. Call it after
/// mutating an object's `Buffs`, once its data lock is released.
pub fn dispatch_buff_events(server: &GameServer, owner: &WorldObject) {
    let events = owner.with_base_unit(|b| b.buffs_mut().take_events());
    if events.is_empty() {
        return;
    }
    if let Some(h) = server.systems.hooks.unit.get() {
        for e in &events {
            h.on_buff_event(server, owner, e);
        }
    }
}

#[cfg(test)]
mod tests {
    use std::sync::Mutex;

    use super::*;
    use crate::data::skills::{BuffKind, BuffTemplate};
    use crate::models::buff::{Buff, SkillCaster};
    use crate::models::npc::Npc;
    use crate::world::Entity;

    #[derive(Default)]
    struct Recorder(Mutex<Vec<(u32, String)>>);

    impl UnitHooks for Recorder {
        fn on_buff_event(&self, _server: &GameServer, owner: &WorldObject, event: &BuffEvent) {
            let what = match event {
                BuffEvent::Started(b) => format!("start {}", b.template.id),
                BuffEvent::Refreshed(b) => format!("refresh {}", b.template.id),
                BuffEvent::Removed(b) => format!("remove {}", b.template.id),
            };
            self.0.lock().unwrap().push((owner.obj_id(), what));
        }
    }

    #[tokio::test]
    async fn buff_events_reach_the_installed_hook() {
        let server = crate::test_support::test_server();
        let npc = WorldObject::from(Entity::new(0x200, Npc::new(1)));
        let add = |id| {
            npc.with_unit(|u| {
                let t = BuffTemplate::simple(id, BuffKind::Good);
                u.buffs_mut()
                    .add_buff(Buff::new(t, SkillCaster::default(), None, 0), 0, 0, 0);
            });
        };
        // Without a hook the events are simply drained.
        add(7);
        dispatch_buff_events(&server, &npc);
        assert!(!npc.with_base_unit(|b| b.buffs().has_events()));

        let rec = Arc::new(Recorder::default());
        server.systems.hooks.unit.install(rec.clone());
        add(8);
        npc.with_unit(|u| u.buffs_mut().remove_buff(7));
        dispatch_buff_events(&server, &npc);
        assert_eq!(
            *rec.0.lock().unwrap(),
            vec![
                (0x200, "start 8".to_string()),
                (0x200, "remove 7".to_string())
            ]
        );
    }
}
