//! The C# event plumbing the instances use:
//!
//! * `CharacterEvents` (`OnTeamJoin`, `OnTeamLeave`, `OnTeamKick`, `OnDungeonLeave`,
//!   `OnDisconnect`) and `UnitEvents.OnKill` subscriptions of dungeons / instant games
//!   ([`CharacterEvents`]); multicast semantics are kept: a handler subscribed twice runs twice,
//!   `-=` removes the last matching subscription, raising an event runs a snapshot;
//! * `WorldInstance.Events` (`OnUnitSpawn`, `OnDoodadSpawn`, `OnUnitKilled`,
//!   `OnUnitCombatStart`, `OnUnitCombatEnd`, `OnAreaClear`) with the `IndunEvent` subscriptions
//!   (`Models/Game/Indun/Events/*`) and their per-world state ([`WorldEventRegs`]);
//! * the `IndunAction` implementations (`Models/Game/Indun/Actions/*`).
//!
//! Entry points other subsystems call: [`on_unit_spawn`] (NPC spawner), [`on_doodad_spawned`]
//! (doodad spawner), [`on_kill_in_reduce_hp`] / [`on_do_die`] (skills combat), [`fire_disconnect`]
//! (session), [`on_team_join`] / [`on_team_leave`] (social teams).

use std::collections::HashMap;
use std::sync::{Arc, Mutex, Weak};

use crate::server::GameServer;
use crate::systems::skills::geometry::{distance_to, Target};
use crate::systems::social::team::Team;
use crate::world::{DoodadRef, NpcRef, Player, PlayerRef, WorldInstance, WorldObject};

use super::data::{IndunAction, IndunActionKind, IndunEvent, IndunEventKind};
use super::dungeon::{self, Dungeon};
use super::instant_game::{self, InstantGame};

// ---------------------------------------------------------------------------------------------
// Character events
// ---------------------------------------------------------------------------------------------

/// The `CharacterEvents` / `UnitEvents` members the instances subscribe to.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CharEvent {
    TeamJoin,
    TeamLeave,
    TeamKick,
    DungeonLeave,
    Disconnect,
    /// `UnitEvents.OnKill` (instant games).
    Kill,
}

/// The subscribed object (`Dungeon` / `InstantGame` handler methods).
#[derive(Debug, Clone)]
pub enum Handler {
    Dungeon(Arc<Dungeon>),
    Game(Arc<InstantGame>),
}

impl Handler {
    fn uid(&self) -> u64 {
        match self {
            Handler::Dungeon(d) => d.uid,
            Handler::Game(g) => g.uid,
        }
    }
}

#[derive(Debug)]
struct Sub {
    player: Weak<Player>,
    event: CharEvent,
    handler: Handler,
}

/// Character event subscriptions (`character.Events.X += handler`).
#[derive(Debug, Default)]
pub struct CharacterEvents {
    subs: Mutex<Vec<Sub>>,
}

impl CharacterEvents {
    fn lock(&self) -> std::sync::MutexGuard<'_, Vec<Sub>> {
        self.subs.lock().expect("character events poisoned")
    }

    /// `character.Events.X += handler`.
    pub fn subscribe(&self, p: &PlayerRef, event: CharEvent, handler: Handler) {
        let mut subs = self.lock();
        subs.retain(|s| s.player.strong_count() > 0);
        subs.push(Sub {
            player: Arc::downgrade(p),
            event,
            handler,
        });
    }

    /// `character.Events.X -= handler` (the last matching subscription).
    pub fn unsubscribe(&self, p: &PlayerRef, event: CharEvent, handler_uid: u64) {
        let mut subs = self.lock();
        let pos = subs.iter().rposition(|s| {
            s.event == event
                && s.handler.uid() == handler_uid
                && s.player.upgrade().is_some_and(|x| Arc::ptr_eq(&x, p))
        });
        if let Some(i) = pos {
            subs.remove(i);
        }
    }

    /// The handlers of one event of a player, in subscription order (the invocation snapshot).
    pub fn handlers(&self, p: &PlayerRef, event: CharEvent) -> Vec<Handler> {
        self.lock()
            .iter()
            .filter(|s| s.event == event && s.player.upgrade().is_some_and(|x| Arc::ptr_eq(&x, p)))
            .map(|s| s.handler.clone())
            .collect()
    }

    /// Number of subscriptions of one event of a player (tests / diagnostics).
    pub fn count(&self, p: &PlayerRef, event: CharEvent) -> usize {
        self.handlers(p, event).len()
    }
}

/// `character.Events.OnDungeonLeave(sender, args)`.
pub fn fire_dungeon_leave(server: &GameServer, p: &PlayerRef) {
    let handlers = server
        .systems
        .instances
        .events
        .characters
        .handlers(p, CharEvent::DungeonLeave);
    for h in handlers {
        if let Handler::Dungeon(d) = h {
            dungeon::on_dungeon_leave(server, &d, p);
        }
    }
}

/// `ActiveChar.Events.OnDisconnect(...)` (`GameConnection.OnDisconnect`).
pub fn fire_disconnect(server: &GameServer, p: &PlayerRef) {
    let handlers = server
        .systems
        .instances
        .events
        .characters
        .handlers(p, CharEvent::Disconnect);
    for h in handlers {
        if let Handler::Dungeon(d) = h {
            dungeon::on_disconnect(server, &d, p);
        }
    }
}

/// `character.Events.OnTeamJoin(sender, { Team, Player })` (`TeamManager.CreateNewTeam` /
/// `CreateSoloTeam`). Only reads `team` (callers may hold the team manager's lock).
pub fn on_team_join(server: &GameServer, p: &PlayerRef, team: &Team) {
    let handlers = server
        .systems
        .instances
        .events
        .characters
        .handlers(p, CharEvent::TeamJoin);
    for h in handlers {
        if let Handler::Dungeon(d) = h {
            dungeon::on_team_join(server, &d, p, team);
        }
    }
}

/// `character.Events.OnTeamLeave(sender, { Id, Team, Player })` (`TeamManager.AskRiskyTeam`).
/// Does not call into the team manager (callers hold its lock).
pub fn on_team_leave(server: &GameServer, p: &PlayerRef, team_id: u32) {
    let handlers = server
        .systems
        .instances
        .events
        .characters
        .handlers(p, CharEvent::TeamLeave);
    for h in handlers {
        if let Handler::Dungeon(d) = h {
            dungeon::on_team_leave(server, &d, p, team_id);
        }
    }
}

/// `killerUnit.Events.OnKill(sender, { Killer, Victim })` for the instant game handlers.
fn fire_kill(server: &GameServer, killer: &PlayerRef, victim: &WorldObject) {
    let handlers = server
        .systems
        .instances
        .events
        .characters
        .handlers(killer, CharEvent::Kill);
    for h in handlers {
        if let Handler::Game(g) = h {
            instant_game::on_kill(server, &g, killer, victim);
        }
    }
}

// ---------------------------------------------------------------------------------------------
// World events
// ---------------------------------------------------------------------------------------------

/// The `IndunEvent` handlers subscribed to one instance's `WorldEvents` (with multiplicity).
#[derive(Debug, Clone, Default)]
pub struct WorldEventRegs {
    pub unit_spawn: Vec<IndunEvent>,
    pub doodad_spawn: Vec<IndunEvent>,
    pub unit_killed: Vec<IndunEvent>,
    pub combat_start: Vec<IndunEvent>,
    pub combat_end: Vec<IndunEvent>,
    pub area_clear: Vec<IndunEvent>,
}

/// `IndunEventNoAliveChInRooms` per-world state (`_doodads[world]`, `_playerRoomCount[world]`).
#[derive(Debug, Clone)]
struct RoomState {
    doodad: DoodadRef,
    player_count: u32,
}

/// World event subscriptions + room state (`server.systems.instances.events`).
#[derive(Debug, Default)]
pub struct Events {
    pub characters: CharacterEvents,
    worlds: Mutex<HashMap<u32, WorldEventRegs>>,
    rooms: Mutex<HashMap<(u32, u32), RoomState>>,
}

impl Events {
    fn worlds(&self) -> std::sync::MutexGuard<'_, HashMap<u32, WorldEventRegs>> {
        self.worlds.lock().expect("world events poisoned")
    }

    fn rooms(&self) -> std::sync::MutexGuard<'_, HashMap<(u32, u32), RoomState>> {
        self.rooms.lock().expect("rooms poisoned")
    }

    /// A copy of one world's subscriptions.
    pub fn regs(&self, world_id: u32) -> WorldEventRegs {
        self.worlds().get(&world_id).cloned().unwrap_or_default()
    }

    /// Drops every subscription / state of a removed world.
    pub fn remove_world(&self, world_id: u32) {
        self.worlds().remove(&world_id);
        self.rooms().retain(|(_, w), _| *w != world_id);
    }

    /// `GetRoomDoodad(worldId)`.
    pub fn room_doodad(&self, event_id: u32, world_id: u32) -> Option<DoodadRef> {
        tracing::warn!("GetRoomDoodad, world {world_id}");
        match self.rooms().get(&(event_id, world_id)) {
            Some(s) => {
                tracing::warn!(
                    "RoomDoodad found templateId={}",
                    s.doodad.data().template_id
                );
                Some(s.doodad.clone())
            }
            None => {
                tracing::warn!("RoomDoodad not found, world {world_id}");
                None
            }
        }
    }

    /// `GetRoomPlayerCount(instanceId)` (0 when unknown; the C# throws `KeyNotFoundException`).
    pub fn room_player_count(&self, event_id: u32, world_id: u32) -> u32 {
        self.rooms()
            .get(&(event_id, world_id))
            .map_or(0, |s| s.player_count)
    }

    /// `SetRoomPlayerCount(instanceId, count)`.
    pub fn set_room_player_count(&self, event_id: u32, world_id: u32, count: u32) {
        if let Some(s) = self.rooms().get_mut(&(event_id, world_id)) {
            s.player_count = count;
        }
    }
}

/// The doodads of the instance's region grid (`region.GetList(doodadList, 0)` over
/// `worldInstance.Regions`), in object id order.
fn region_doodads(world: &WorldInstance) -> Vec<DoodadRef> {
    let mut v: Vec<DoodadRef> = world
        .all_doodads()
        .into_iter()
        .filter(|d| WorldObject::from(d).region().is_some())
        .collect();
    v.sort_by_key(|d| d.obj_id);
    v
}

/// The NPCs of the instance's region grid, in object id order.
fn region_npcs(world: &WorldInstance) -> Vec<NpcRef> {
    let mut v: Vec<NpcRef> = world
        .all_npcs()
        .into_iter()
        .filter(|n| WorldObject::from(n).region().is_some())
        .collect();
    v.sort_by_key(|n| n.obj_id);
    v
}

/// `IndunEvent.Subscribe(worldInstance)`.
pub fn subscribe(server: &GameServer, ev: &IndunEvent, world: &Arc<WorldInstance>) {
    let events = &server.systems.instances.events;
    match ev.kind {
        IndunEventKind::DoodadSpawneds { .. } => {
            events
                .worlds()
                .entry(world.id)
                .or_default()
                .doodad_spawn
                .push(*ev);
        }
        IndunEventKind::NoAliveChInRooms { room_id } => {
            let Some(room) = server.game_data.systems.instances.indun.room(room_id) else {
                tracing::error!("[IndunEvent] room {room_id} does not exist");
                return;
            };
            let doodads: Vec<DoodadRef> = region_doodads(world)
                .into_iter()
                .filter(|d| d.data().template_id == room.doodad_id)
                .collect();
            if doodads.is_empty() {
                return;
            }
            if doodads.len() > 1 {
                tracing::warn!("[IndunEvent] DoodadList returned higher than one doodad count.");
            }
            events.rooms().insert(
                (ev.id, world.id),
                RoomState {
                    doodad: doodads[0].clone(),
                    player_count: 0,
                },
            );
            events
                .worlds()
                .entry(world.id)
                .or_default()
                .area_clear
                .push(*ev);
        }
        IndunEventKind::NpcCombatEndeds { .. } => {
            events
                .worlds()
                .entry(world.id)
                .or_default()
                .combat_end
                .push(*ev);
        }
        IndunEventKind::NpcCombatStarteds { .. } => {
            events
                .worlds()
                .entry(world.id)
                .or_default()
                .combat_start
                .push(*ev);
        }
        IndunEventKind::NpcKilleds { .. } => {
            events
                .worlds()
                .entry(world.id)
                .or_default()
                .unit_killed
                .push(*ev);
        }
        IndunEventKind::NpcSpawneds { .. } => {
            events
                .worlds()
                .entry(world.id)
                .or_default()
                .unit_spawn
                .push(*ev);
        }
    }
}

fn remove_last(list: &mut Vec<IndunEvent>, id: u32) {
    if let Some(i) = list.iter().rposition(|e| e.id == id) {
        list.remove(i);
    }
}

/// `IndunEvent.UnSubscribe(worldInstance)`.
///
/// Note (C#): `IndunEventDoodadSpawneds.UnSubscribe` uses `+=`, adding the handler again;
/// mirrored (the world is removed right after in `DestroyDungeon`).
pub fn unsubscribe(server: &GameServer, ev: &IndunEvent, world_id: u32) {
    let events = &server.systems.instances.events;
    let mut worlds = events.worlds();
    let regs = worlds.entry(world_id).or_default();
    match ev.kind {
        IndunEventKind::DoodadSpawneds { .. } => regs.doodad_spawn.push(*ev),
        IndunEventKind::NoAliveChInRooms { .. } => {
            events.rooms().remove(&(ev.id, world_id));
            remove_last(&mut regs.area_clear, ev.id);
        }
        IndunEventKind::NpcCombatEndeds { .. } => remove_last(&mut regs.combat_end, ev.id),
        IndunEventKind::NpcCombatStarteds { .. } => remove_last(&mut regs.combat_start, ev.id),
        IndunEventKind::NpcKilleds { .. } => remove_last(&mut regs.unit_killed, ev.id),
        IndunEventKind::NpcSpawneds { .. } => remove_last(&mut regs.unit_spawn, ev.id),
    }
}

/// Runs one action (`action.Execute(world)`; a missing action is a NullReferenceException in
/// C#, logged here).
fn execute_action(server: &GameServer, action_id: u32, world: &Arc<WorldInstance>) {
    match server
        .game_data
        .systems
        .instances
        .indun
        .action(action_id)
        .copied()
    {
        Some(a) => execute(server, &a, world),
        None => tracing::error!("IndunAction {action_id} does not exist"),
    }
}

/// `IndunAction.Execute(worldInstance)`.
pub fn execute(server: &GameServer, action: &IndunAction, world: &Arc<WorldInstance>) {
    match action.kind {
        IndunActionKind::ChangeDoodadPhases {
            doodad_almighty_id,
            doodad_func_group_id,
        } => {
            let doodads: Vec<DoodadRef> = region_doodads(world)
                .into_iter()
                .filter(|d| d.data().template_id == doodad_almighty_id)
                .collect();
            if doodads.is_empty() {
                tracing::warn!("{doodad_almighty_id} is not found!");
            }
            for d in &doodads {
                crate::systems::npcs::doodad::manager::do_change_phase(
                    server,
                    world,
                    d,
                    None,
                    doodad_func_group_id as i32,
                );
            }
            tracing::warn!(
                "IndunActionChangeDoodadPhases: Doodad {doodad_almighty_id} change phase to {doodad_func_group_id}"
            );
        }
        IndunActionKind::RemoveTaggedNpcs { tag_id } => {
            let tagged = server
                .game_data
                .systems
                .skills
                .npc_tags
                .get(&tag_id)
                .cloned()
                .unwrap_or_default();
            for npc in region_npcs(world) {
                let template = npc.data().unit.template_id;
                if !tagged.contains(&template) {
                    continue;
                }
                // Npc.Delete(): detach the AI, then GameObject.Delete().
                crate::systems::npcs::npc::manager::delete(server, world, &npc);
            }
            tracing::warn!("IndunActionRemoveTaggedNpcs: {tag_id}");
        }
        IndunActionKind::SetRoomCleareds { indun_room_id } => {
            if let Some(d) = server.systems.instances.dungeon_of_world(world.id) {
                d.set_room_cleared(indun_room_id);
            }
            tracing::warn!("Room Clear: {indun_room_id}");
            fire_area_clear(server, world);
        }
        IndunActionKind::NpcSpawner => {
            tracing::warn!("IndunActionNpcSpawner");
        }
    }
}

/// `worldInstance.Events.OnAreaClear(...)` (`IndunEventNoAliveChInRooms.OnAreaClear` logs).
fn fire_area_clear(server: &GameServer, world: &WorldInstance) {
    let regs = server.systems.instances.events.regs(world.id);
    for _ in &regs.area_clear {
        tracing::warn!("OnAreaClear, world {}", world.id);
    }
}

/// `world.Events.OnUnitSpawn(world, { Npc })` (`NpcSpawner` after `npc.Spawn()`):
/// `IndunEventNpcSpawneds` runs its start action (not the chain).
pub fn on_unit_spawn(server: &GameServer, world: &Arc<WorldInstance>, npc: &NpcRef) {
    super::tower_def::on_spawn(server, npc);
    let regs = server.systems.instances.events.regs(world.id);
    if regs.unit_spawn.is_empty() {
        return;
    }
    let template = npc.data().unit.template_id;
    for ev in &regs.unit_spawn {
        let IndunEventKind::NpcSpawneds { npc_id } = ev.kind else {
            continue;
        };
        if template != npc_id {
            tracing::warn!("IndunEventNpcSpawneds - need npc={template}, not npc={npc_id}");
            continue;
        }
        tracing::warn!("IndunEventNpcSpawneds - {npc_id}");
        execute_action(server, ev.start_action_id, world);
    }
}

/// `Last.ParentWorld.Events.OnDoodadSpawn(...)` (`DoodadSpawner.Spawn` for worlds other than the
/// default world template that are dungeons): `IndunEventDoodadSpawneds` runs its start action.
pub fn on_doodad_spawned(server: &GameServer, world: &Arc<WorldInstance>, doodad: &DoodadRef) {
    if world.template.id == server.game_data.worlds.default_world_template_id() {
        return;
    }
    if server
        .systems
        .instances
        .dungeon_of_world(world.id)
        .is_none()
    {
        return;
    }
    let regs = server.systems.instances.events.regs(world.id);
    let template = doodad.data().template_id;
    for ev in &regs.doodad_spawn {
        let IndunEventKind::DoodadSpawneds {
            doodad_almighty_id, ..
        } = ev.kind
        else {
            continue;
        };
        tracing::warn!("IndunEventDoodadSpawneds - {template}, {doodad_almighty_id}");
        if template != doodad_almighty_id {
            continue;
        }
        execute_action(server, ev.start_action_id, world);
    }
}

/// `world.Events.OnUnitKilled(world, { Killer, Victim })`: `IndunEventNpcKilleds` runs the
/// action chain when its NPC died.
fn fire_unit_killed(server: &GameServer, world: &Arc<WorldInstance>, victim: &WorldObject) {
    let regs = server.systems.instances.events.regs(world.id);
    if regs.unit_killed.is_empty() {
        return;
    }
    let Some(npc) = victim.as_npc() else {
        return;
    };
    let template = npc.data().unit.template_id;
    for ev in &regs.unit_killed {
        let IndunEventKind::NpcKilleds { npc_id } = ev.kind else {
            continue;
        };
        if template != npc_id {
            continue;
        }
        tracing::warn!("IndunEventNpcKilleds - {npc_id}, {}", ev.id);
        super::indun::do_indun_actions(server, ev.start_action_id, world);
    }
}

/// `world.Events.OnUnitCombatEnd(world, { Npc })` (`IndunEventNpcCombatEndeds` logs).
fn fire_unit_combat_end(server: &GameServer, world: &WorldInstance, unit: &WorldObject) {
    let regs = server.systems.instances.events.regs(world.id);
    let Some(npc) = unit.as_npc() else {
        return;
    };
    for _ in &regs.combat_end {
        tracing::warn!("{} has left combat.", npc.data().unit.template_id);
    }
}

/// `world.Events.OnUnitCombatStart` (`IndunEventNpcCombatStarteds`; nothing raises it in C#).
pub fn fire_unit_combat_start(server: &GameServer, world: &WorldInstance, unit: &WorldObject) {
    let regs = server.systems.instances.events.regs(world.id);
    let Some(npc) = unit.as_npc() else {
        return;
    };
    let template = npc.data().unit.template_id;
    for ev in &regs.combat_start {
        if let IndunEventKind::NpcCombatStarteds { npc_id } = ev.kind {
            if template == npc_id {
                tracing::warn!("{template} has entered combat.");
            }
        }
    }
}

/// The dying part of `Unit.PostUpdateCurrentHp` (before `DoDie`, with a unit attacker):
/// `attackerUnit.Events.OnKill(attacker, { Target = attacker })` (no instance handler uses
/// `Target`) and, in a dungeon instance of a non-default world template,
/// `OnUnitKilled` + `OnUnitCombatEnd` + `Events.OnDeath`.
///
/// Note (C#): `DoDie` raises `OnUnitKilled` and `OnDeath` again, so dungeon kill events (and the
/// victim's death buff triggers) run twice per death; mirrored.
pub fn on_kill_in_reduce_hp(server: &GameServer, victim: &WorldObject) {
    let instance = victim.transform().instance_id;
    let Some(world) = server.world.worlds().into_iter().find(|w| w.id == instance) else {
        return;
    };
    if world.template.id == 0
        || server
            .systems
            .instances
            .dungeon_of_world(world.id)
            .is_none()
    {
        return;
    }
    fire_unit_killed(server, &world, victim);
    fire_unit_combat_end(server, &world, victim);
    crate::systems::skills::buffs::fire_unit_event(
        server,
        victim.obj_id(),
        crate::systems::skills::types::BuffEventTriggerKind::Death,
        0,
    );
}

/// `Unit.DoDie`: `ParentWorld.Events.OnUnitKilled(...)` and `killerUnit?.Events.OnKill(this,
/// { Killer, Victim })` (instant game scores).
pub fn on_do_die(server: &GameServer, victim: &WorldObject, killer: Option<&WorldObject>) {
    super::tower_def::on_death(server, victim);
    let instance = victim.transform().instance_id;
    if let Some(world) = server.world.worlds().into_iter().find(|w| w.id == instance) {
        fire_unit_killed(server, &world, victim);
    }
    if let Some(k) = killer.and_then(WorldObject::as_player) {
        fire_kill(server, k, victim);
    }
}

/// `WorldManager.GetAround<Character>(doodad, radius).Where(o => o.GetDistanceTo(doodad) <=
/// radius).Count()`.
pub fn players_in_room(server: &GameServer, doodad: &DoodadRef, radius: f32) -> usize {
    let origin = Target::Obj(WorldObject::from(doodad));
    crate::systems::skills::geometry::around_radius(server, &origin, radius, false)
        .into_iter()
        .filter(|o| o.as_player().is_some())
        .filter(|o| distance_to(server, &Target::Obj(o.clone()), &origin, false) <= radius)
        .count()
}
