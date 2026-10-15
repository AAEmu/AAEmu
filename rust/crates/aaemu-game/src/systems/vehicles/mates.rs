//! `MateManager` (one per world instance), `CharacterMates` (the `mates` rows of a character),
//! `Mate` behaviour (`AddExp`, `StartUpdateXp` / `MateXpUpdateTask`, `RegenTick`,
//! `DoFallDamage`, `OnZoneChange`) and the mount / dismount flow.
//!
//! Locking: the manager lists are leaf locks (never held while locking a mate or a player);
//! at most one object data lock is held at a time.

use std::collections::HashMap;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex, MutexGuard};

use chrono::NaiveDateTime;
use dashmap::DashMap;
use sqlx::Row;

use crate::models::container::slot_type;
use crate::models::item::ItemClass;
use crate::models::mate::{Mate, MatePassengerInfo};
use crate::packets::world::SCUnitPointsPacket;
use crate::packets::world_objects::{SCTargetChangedPacket, SCUnitAttachedPacket};
use crate::server::GameServer;
use crate::systems::economy::error_message;
use crate::systems::skills::packets::SCExpChangedPacket;
use crate::systems::skills::progress::SCLevelChangedPacket;
use crate::systems::skills::types::{BuffEventTriggerKind, BuffRemoveOn, KillReason, SkillCaster};
use crate::systems::social::achievement::char_record_kind;
use crate::systems::social::achievement_hooks::{AchievementInput, RecordMatch};
use crate::world::{Entity, MateRef, PlayerRef, WorldObject};

use super::data::attach_point;
use super::packets::{
    item_task_type, SCItemTaskSuccessPacket, SCMateSpawnedPacket, SCUnitDetachedPacket,
    SCUnitNameChangedPacket,
};
use super::slaves::attach_reason;
use super::stats;
use super::{broadcast, now_ms, set_unit_position, world_of};

/// `MateXpUpdateTask.Exp`.
pub const MATE_XP_TASK_EXP: i32 = 300;
/// `MateXpUpdateTask` delay.
pub const MATE_XP_TASK_DELAY_MS: u64 = 60_000;

/// HP / MP a mate row starts with (`CharacterMates.CreateNewMate`) and is restored to by the
/// stablehand revive; the spawn clamps both to the mate's current pools (`MaxHp` / `MaxMp`), so
/// this always means "full".
pub const MATE_FULL_POOL: i32 = 9999;

static XP_TASK_GEN: AtomicU64 = AtomicU64::new(1);

/// `MateDb` (one `mates` row).
#[derive(Debug, Clone, PartialEq)]
pub struct MateDb {
    pub id: u32,
    pub item_id: u64,
    pub name: String,
    pub xp: i32,
    pub level: u16,
    pub mileage: i32,
    pub hp: i32,
    pub mp: i32,
    pub owner: u32,
    pub updated_at: NaiveDateTime,
    pub created_at: NaiveDateTime,
}

/// `CharacterMates` (`_mates` by item id in insertion order, `_removedMates`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct CharacterMates {
    pub mates: Vec<MateDb>,
    pub removed: Vec<u32>,
}

impl CharacterMates {
    /// `GetMateInfo(itemId)`.
    pub fn get(&self, item_id: u64) -> Option<&MateDb> {
        self.mates.iter().find(|m| m.item_id == item_id)
    }

    pub fn get_mut(&mut self, item_id: u64) -> Option<&mut MateDb> {
        self.mates.iter_mut().find(|m| m.item_id == item_id)
    }
}

/// `MateManager` of one world instance (`_activeMates`: owner character id -> mates, in
/// insertion order).
#[derive(Debug, Default)]
pub struct MateManager {
    active: Mutex<Vec<(u32, Vec<MateRef>)>>,
}

impl MateManager {
    fn active(&self) -> MutexGuard<'_, Vec<(u32, Vec<MateRef>)>> {
        self.active.lock().expect("mates poisoned")
    }

    /// `GetActiveMates(ownerId)`.
    pub fn get_active_mates(&self, owner_id: u32) -> Vec<MateRef> {
        self.active()
            .iter()
            .find(|(o, _)| *o == owner_id)
            .map(|(_, l)| l.clone())
            .unwrap_or_default()
    }

    fn all(&self) -> Vec<MateRef> {
        self.active()
            .iter()
            .flat_map(|(_, l)| l.iter().cloned())
            .collect()
    }

    /// `GetActiveMateByTlId(tlId)`.
    pub fn get_active_mate_by_tl_id(&self, tl_id: u32) -> Option<MateRef> {
        self.all()
            .into_iter()
            .find(|m| u32::from(m.data().unit.tl_id) == tl_id)
    }

    /// `GetActiveMateByMateObjId(objId)`.
    pub fn get_active_mate_by_obj_id(&self, obj_id: u32) -> Option<MateRef> {
        self.all().into_iter().find(|m| m.obj_id == obj_id)
    }

    /// `GetIsMounted(objId, out attachPoint)`.
    pub fn get_is_mounted(&self, obj_id: u32) -> Option<(MateRef, u8)> {
        for m in self.all() {
            let found = m
                .data()
                .passengers
                .iter()
                .find(|(_, i)| i.obj_id == obj_id)
                .map(|(p, _)| *p);
            if let Some(p) = found {
                return Some((m, p));
            }
        }
        None
    }

    /// `_activeMates[ownerId]` get-or-add, then `Add(mate)`; `false` when the owner already
    /// had a mate (the C# despawns that one instead).
    fn add_or_first(&self, owner_id: u32, mate: &MateRef) -> Result<(), MateRef> {
        let mut a = self.active();
        let idx = match a.iter().position(|(o, _)| *o == owner_id) {
            Some(i) => i,
            None => {
                a.push((owner_id, Vec::new()));
                a.len() - 1
            }
        };
        if let Some(first) = a[idx].1.first() {
            return Err(first.clone());
        }
        a[idx].1.push(mate.clone());
        Ok(())
    }

    fn remove(&self, owner_id: u32, obj_id: u32) {
        let mut a = self.active();
        if let Some(i) = a.iter().position(|(o, _)| *o == owner_id) {
            a[i].1.retain(|m| m.obj_id != obj_id);
            if a[i].1.is_empty() {
                a.remove(i);
            }
        }
    }

    fn remove_key(&self, key: u32) {
        self.active().retain(|(o, _)| *o != key);
    }
}

/// Every instance's `MateManager` plus the characters' `CharacterMates`.
#[derive(Debug, Default)]
pub struct MateManagers {
    managers: DashMap<u32, Arc<MateManager>>,
    characters: Mutex<HashMap<u32, CharacterMates>>,
    name_regex: std::sync::OnceLock<regex::Regex>,
}

impl MateManagers {
    /// `world.MateManager` (created on first use, like `WorldManager.CreateWorld`).
    pub fn get(&self, instance_id: u32) -> Arc<MateManager> {
        self.managers.entry(instance_id).or_default().clone()
    }

    /// All instances' managers.
    pub fn all(&self) -> Vec<Arc<MateManager>> {
        self.managers.iter().map(|e| e.value().clone()).collect()
    }

    /// `owner.Mates` access.
    pub fn character<R>(&self, char_id: u32, f: impl FnOnce(&mut CharacterMates) -> R) -> R {
        let mut m = self.characters.lock().expect("character mates poisoned");
        f(m.entry(char_id).or_default())
    }

    /// The loaded `CharacterMates` of a character (copy).
    pub fn character_mates(&self, char_id: u32) -> Option<CharacterMates> {
        self.characters
            .lock()
            .expect("character mates poisoned")
            .get(&char_id)
            .cloned()
    }

    pub(crate) fn name_regex(&self, server: &GameServer) -> &regex::Regex {
        self.name_regex.get_or_init(|| {
            server
                .config
                .character_name_regex
                .as_deref()
                .and_then(|r| regex::Regex::new(r).ok())
                .unwrap_or_else(|| {
                    regex::Regex::new(crate::managers::name::DEFAULT_CHARACTER_NAME_REGEX)
                        .expect("valid regex")
                })
        })
    }
}

fn manager_of(server: &GameServer, p: &PlayerRef) -> Arc<MateManager> {
    let instance = p.data().unit.transform.instance_id;
    server.systems.vehicles.mates.get(instance)
}

/// `Mate.IsMaxLevel`.
pub fn is_max_level(server: &GameServer, mate: &Mate) -> bool {
    mate.unit.level >= server.game_data.experience.max_mate_level()
}

// ---------------------------------------------------------------------------------------------
// MateManager
// ---------------------------------------------------------------------------------------------

/// `ChangeStateMate(connection, tlId, newState)` (no packet in 1.2).
pub fn change_state_mate(server: &GameServer, owner: &PlayerRef, tl_id: u32, new_state: u8) {
    let owner_id = owner.data().id;
    for m in manager_of(server, owner).get_active_mates(owner_id) {
        let mut d = m.data();
        if u32::from(d.unit.tl_id) != tl_id {
            continue;
        }
        d.user_state = new_state;
    }
}

/// `ChangeTargetMate(connection, tlId, objId)`.
pub fn change_target_mate(server: &GameServer, owner: &PlayerRef, tl_id: u32, obj_id: u32) {
    let Some(mate) = manager_of(server, owner).get_active_mate_by_tl_id(tl_id) else {
        return;
    };
    if mate.data().owner_obj_id != owner.obj_id {
        return;
    }
    let world = world_of(server, &WorldObject::from(&mate));
    let target = if obj_id > 0 {
        world
            .as_ref()
            .and_then(|w| w.unit(obj_id))
            .map_or(0, |u| u.obj_id())
    } else {
        0
    };
    let tl = {
        let mut d = mate.data();
        d.unit.current_target = target;
        d.unit.tl_id
    };
    broadcast(
        server,
        &WorldObject::from(&mate),
        &SCTargetChangedPacket {
            obj_id: mate.obj_id,
            target_obj_id: target,
        },
        true,
    );
    tracing::debug!(
        "ChangeTargetMate. tlId: {tl}, objId: {}, targetObjId: {obj_id}",
        mate.obj_id
    );
}

/// `RenameMount(connection, tlId, newName)`.
pub fn rename_mount(server: &GameServer, owner: &PlayerRef, tl_id: u32, new_name: &str) -> bool {
    let Some(mate) = manager_of(server, owner).get_active_mate_by_tl_id(tl_id) else {
        return false;
    };
    if mate.data().owner_obj_id != owner.obj_id {
        return false;
    }
    let regex = server.systems.vehicles.mates.name_regex(server);
    if new_name.trim().is_empty() || !regex.is_match(new_name) {
        return false;
    }
    mate.data().unit.name = crate::managers::name::normalize_name(new_name);
    broadcast(
        server,
        &WorldObject::from(&mate),
        &SCUnitNameChangedPacket {
            obj_id: mate.obj_id,
            name: new_name.to_string(),
        },
        false,
    );
    true
}

/// `MountMate(connection, tlId, attachPoint, reason)`.
pub fn mount_mate(server: &GameServer, character: &PlayerRef, tl_id: u32, point: u8, reason: u8) {
    let Some(mate) = manager_of(server, character).get_active_mate_by_tl_id(tl_id) else {
        return;
    };
    let (seat, owner_obj, mate_name) = {
        let d = mate.data();
        (
            d.passenger(point).copied(),
            d.owner_obj_id,
            d.unit.name.clone(),
        )
    };
    let char_name = character.data().unit.name.clone();
    match seat {
        Some(seat) => {
            if point == attach_point::DRIVER && owner_obj != character.obj_id {
                tracing::warn!(
                    "MountMate. Non-owner {char_name} ({}) tried to take the first seat on mount {mate_name} ({})",
                    character.obj_id,
                    mate.obj_id
                );
                return;
            }
            if seat.obj_id == 0 {
                let me = WorldObject::from(character);
                broadcast(
                    server,
                    &me,
                    &SCUnitAttachedPacket {
                        child_obj_id: character.obj_id,
                        point,
                        reason,
                        obj_id: mate.obj_id,
                    },
                    true,
                );
                if let Some(s) = mate.data().passenger_mut(point) {
                    s.obj_id = character.obj_id;
                    s.reason = reason;
                }
                if let Some(world) = world_of(server, &me) {
                    world.attach_child(&WorldObject::from(&mate), &me);
                }
                {
                    let mut c = character.data();
                    c.unit.transform.set_xyz(0.0, 0.0, 0.0);
                    c.is_riding = true;
                    c.attached_point = point;
                }
                character.core().set_visible(true);
            }
        }
        None => {
            tracing::warn!(
                "MountMate. Player {char_name} ({}) tried to take a invalid seat {point} on mount {mate_name} ({})",
                character.obj_id,
                mate.obj_id
            );
            return;
        }
    }
    trigger_remove_on(server, &WorldObject::from(character), BuffRemoveOn::Mount);
    tracing::debug!(
        "MountMate. mountTlId: {tl_id}, attachPoint: {point}, reason: {reason}, seats: {:?}",
        mate.data().passengers
    );
}

fn trigger_remove_on(server: &GameServer, obj: &WorldObject, on: BuffRemoveOn) {
    crate::systems::skills::buffs::trigger_remove_on(server, obj, on, 0);
    crate::systems::dispatch_buff_events(server, obj);
}

/// `UnMountMate(character, tlId, attachPoint, reason)`: a client can only leave its own seat.
pub fn unmount_mate(server: &GameServer, character: &PlayerRef, tl_id: u32, point: u8, reason: u8) {
    let instance = character.data().unit.transform.instance_id;
    let Some(mate) = server
        .systems
        .vehicles
        .mates
        .get(instance)
        .get_active_mate_by_tl_id(tl_id)
    else {
        return;
    };
    if !mate
        .data()
        .passenger(point)
        .is_some_and(|seat| seat.obj_id == character.obj_id)
    {
        return;
    }
    unmount_mate_by(server, Some(instance), tl_id, point, reason);
}

/// `UnMountMate` in the instance of `instance_id` (or the mate found in any instance).
fn unmount_mate_by(
    server: &GameServer,
    instance_id: Option<u32>,
    tl_id: u32,
    point: u8,
    reason: u8,
) {
    let mate = match instance_id {
        Some(i) => server
            .systems
            .vehicles
            .mates
            .get(i)
            .get_active_mate_by_tl_id(tl_id),
        None => server
            .systems
            .vehicles
            .mates
            .all()
            .into_iter()
            .find_map(|m| m.get_active_mate_by_tl_id(tl_id)),
    };
    let Some(mate) = mate else {
        return;
    };
    stop_update_xp(&mate);
    let target = {
        let mut d = mate.data();
        match d.passenger_mut(point) {
            Some(seat) if seat.obj_id != 0 => {
                let id = seat.obj_id;
                seat.obj_id = 0;
                seat.reason = reason;
                server.world.character_by_obj_id(id)
            }
            _ => None,
        }
    };
    let Some(target) = target else {
        tracing::debug!(
            "UnMountMate. No valid seat entry, mountTlId: {tl_id}, characterObjId: 0, attachPoint: {point}, reason: {reason}"
        );
        return;
    };
    let mate_obj = WorldObject::from(&mate);
    let t = WorldObject::from(&target);
    let mate_world = world_of(server, &mate_obj);
    let mate_pos = mate_world
        .as_ref()
        .map(|w| w.world_pos_rot(&mate_obj))
        .unwrap_or_else(|| mate.data().unit.transform.local());
    if let Some(w) = world_of(server, &t) {
        w.detach_child(&t);
    }
    set_unit_position(
        server,
        &t,
        mate_pos.x,
        mate_pos.y,
        mate_pos.z,
        mate_pos.rot_x,
        mate_pos.rot_y,
        mate_pos.rot_z,
    );
    {
        let mut c = target.data();
        c.is_riding = false;
        c.attached_point = attach_point::NONE;
    }
    broadcast(
        server,
        &t,
        &SCUnitDetachedPacket {
            obj_id: target.obj_id,
            reason,
        },
        true,
    );
    // targetObj.Events.OnUnmount (buff triggers)
    crate::systems::skills::buffs::fire_unit_event(
        server,
        target.obj_id,
        BuffEventTriggerKind::Unmount,
        0,
    );
    trigger_remove_on(server, &mate_obj, BuffRemoveOn::Unmount);
    trigger_remove_on(server, &t, BuffRemoveOn::Unmount);
    tracing::debug!(
        "UnMountMate. mountTlId: {tl_id}, targetObjId: {}, attachPoint: {point}, reason: {reason}",
        target.obj_id
    );
}

/// `AddActiveMateAndSpawn(owner, mate, item)`.
fn add_active_mate_and_spawn(
    server: &GameServer,
    owner: &PlayerRef,
    mate: &MateRef,
    item_update: Option<crate::models::item_task::ItemTask>,
) {
    let owner_id = owner.data().id;
    let manager = manager_of(server, owner);
    if let Err(first) = manager.add_or_first(owner_id, mate) {
        // TODO (C#): multiple pets of different types; the C# despawns the old one and returns
        // without spawning the new mate.
        let tl = u32::from(first.data().unit.tl_id);
        despawn_mate(server, owner, tl);
        return;
    }
    owner.send(SCItemTaskSuccessPacket {
        action: item_task_type::UPDATE_SUMMON_MATE_ITEM,
        tasks: item_update.into_iter().collect(),
        force_remove: Vec::new(),
    });
    let spawned = {
        let d = mate.data();
        SCMateSpawnedPacket {
            tl_id: d.unit.tl_id,
            id: d.id,
            item_id: d.item_id,
            user_state: d.user_state,
            experience: d.experience,
            mileage: d.mileage,
            spawn_delay_time: d.spawn_delay_time,
            skills: d.skills.clone(),
        }
    };
    owner.send(spawned);
    // Thread.Sleep(50): not reproduced (packet order is kept by the connection queue).
    if let Some(world) = world_of(server, &WorldObject::from(owner)) {
        let changes = world.spawn(mate, &server.view_ctx());
        crate::world::session::handle_zone_changes(server, changes);
    }
    tracing::debug!(
        "Mount spawned. ownerObjId: {}, tlId: {}, mateObjId: {}",
        owner.obj_id,
        mate.data().unit.tl_id,
        mate.obj_id
    );
}

/// `GameObject.Delete()` of a mate + the id releases of the callers (once).
fn delete_mate(server: &GameServer, mate: &MateRef) {
    let (tl, already) = {
        let mut d = mate.data();
        let a = d.deleted;
        d.deleted = true;
        (d.unit.tl_id, a)
    };
    stop_update_xp(mate);
    if let Some(world) = server.world.world_of(&WorldObject::from(mate)) {
        world.delete(mate);
    }
    if already {
        // Deviation: the C# releases the ids of a stale (already deleted) mate a second time.
        return;
    }
    server.ids.object.release_id(mate.obj_id);
    if tl > 0 {
        server.ids.tl.release_id(u32::from(tl));
    }
}

/// `RemoveActiveMateAndDespawn(owner, tlId)`.
pub fn remove_active_mate_and_despawn(server: &GameServer, owner: &PlayerRef, tl_id: u32) {
    let manager = manager_of(server, owner);
    let Some(mate) = manager.get_active_mate_by_tl_id(tl_id) else {
        return;
    };
    let instance = owner.data().unit.transform.instance_id;
    let passengers = mate.data().passengers.clone();
    for (point, _) in &passengers {
        unmount_mate_by(
            server,
            Some(instance),
            tl_id,
            *point,
            attach_reason::SLAVE_BINDING,
        );
    }
    manager.remove(owner.data().id, mate.obj_id);
    delete_mate(server, &mate);
    tracing::debug!(
        "Mount removed. ownerObjId: {}, tlId: {tl_id}, mateObjId: {}",
        owner.obj_id,
        mate.obj_id
    );
}

/// `RemoveAndDespawnAllActiveOwnedMates(character)` of one instance. The active list is keyed by
/// the character id (as `GetActiveMates`), so exactly that entry is removed once its mates were
/// despawned. Deviation: the C# removed the entries by `OwnerObjId` and left the character's
/// entry (the deleted mates stayed listed).
pub fn remove_and_despawn_all_active_owned_mates(
    server: &GameServer,
    instance_id: u32,
    character_id: u32,
) {
    let manager = server.systems.vehicles.mates.get(instance_id);
    for mate in manager.get_active_mates(character_id) {
        let (tl, passengers) = {
            let d = mate.data();
            (u32::from(d.unit.tl_id), d.passengers.clone())
        };
        for (point, _) in &passengers {
            unmount_mate_by(
                server,
                Some(instance_id),
                tl,
                *point,
                attach_reason::SLAVE_BINDING,
            );
        }
        delete_mate(server, &mate);
    }
    manager.remove_key(character_id);
}

/// `Blink`: `if (character.IsRiding)` every active mate of the character is unmounted from the
/// driver seat (`AttachUnitReason.None`).
pub fn unmount_active_mates_on_blink(server: &GameServer, p: &PlayerRef) {
    let (riding, id) = {
        let c = p.data();
        (c.is_riding, c.id)
    };
    if !riding {
        return;
    }
    for m in manager_of(server, p).get_active_mates(id) {
        let tl = u32::from(m.data().unit.tl_id);
        unmount_mate(server, p, tl, attach_point::DRIVER, attach_reason::NONE);
    }
}

// ---------------------------------------------------------------------------------------------
// CharacterMates
// ---------------------------------------------------------------------------------------------

/// `CharacterMates.CreateNewMate(itemId, npcTemplate)`.
fn create_new_mate(
    server: &GameServer,
    owner_id: u32,
    item_id: u64,
    npc_id: u32,
) -> Option<MateDb> {
    let tpl = server.game_data.systems.npcs.npc.template(npc_id)?;
    let exists = server
        .systems
        .vehicles
        .mates
        .character(owner_id, |cm| cm.get(item_id).is_some());
    if exists {
        return None;
    }
    let id = match server.ids.mate.get_next_id() {
        Ok(id) => id,
        Err(e) => {
            tracing::error!("{e}");
            return None;
        }
    };
    let now = chrono::Utc::now().naive_utc();
    let db = MateDb {
        id,
        item_id,
        level: u16::from(tpl.level),
        name: server
            .game_data
            .systems
            .npcs
            .npc
            .localized_name(tpl.id, &tpl.name),
        owner: owner_id,
        mileage: 0,
        xp: server.game_data.experience.exp_for_level(tpl.level, true),
        hp: MATE_FULL_POOL,
        mp: MATE_FULL_POOL,
        updated_at: now,
        created_at: now,
    };
    server
        .systems
        .vehicles
        .mates
        .character(owner_id, |cm| cm.mates.push(db.clone()));
    Some(db)
}

/// The item a character owns (inventory containers first, then the item manager).
pub(crate) fn owned_item(
    server: &GameServer,
    owner: &PlayerRef,
    item_id: u64,
) -> Option<crate::models::item::Item> {
    let containers: Vec<_> = owner
        .data()
        .inventory
        .containers
        .iter()
        .map(|(_, c)| c.clone())
        .collect();
    for c in containers {
        if let Some(i) = c
            .lock()
            .expect("container poisoned")
            .get_item_by_id(item_id)
        {
            return Some(i.clone());
        }
    }
    server.items.item_by_id(item_id)
}

/// In-place change of an item the character owns (see [`owned_item`]).
pub(crate) fn update_owned_item<R>(
    server: &GameServer,
    owner: Option<&PlayerRef>,
    item_id: u64,
    f: impl FnOnce(&mut crate::models::item::Item) -> R,
) -> Option<R> {
    if let Some(owner) = owner {
        let containers: Vec<_> = owner
            .data()
            .inventory
            .containers
            .iter()
            .map(|(_, c)| c.clone())
            .collect();
        for c in containers {
            let mut g = c.lock().expect("container poisoned");
            if let Some(i) = g.get_item_by_id_mut(item_id) {
                return Some(f(i));
            }
        }
    }
    server.items.update_item(item_id, f)
}

/// `owner.Mates.SpawnMount(skillItem)`.
pub fn spawn_mount(server: &GameServer, owner: &PlayerRef, skill_item_id: u64) {
    let owner_id = owner.data().id;
    let manager = manager_of(server, owner);
    let mut despawned_old = false;
    for old in manager.get_active_mates(owner_id) {
        let tl = u32::from(old.data().unit.tl_id);
        despawn_mate(server, owner, tl);
        despawned_old = true;
    }
    if despawned_old {
        return;
    }
    let Some(item) = owned_item(server, owner, skill_item_id) else {
        return;
    };
    let npc_id = match server
        .game_data
        .items
        .template(item.template_id)
        .map(|t| &t.kind)
    {
        Some(crate::data::items::TemplateKind::SummonMate { npc_id }) => *npc_id,
        _ => {
            tracing::error!(
                "SpawnMount: item template {} is not a SummonMateTemplate (InvalidCastException)",
                item.template_id
            );
            return;
        }
    };
    let Some(template) = server.game_data.systems.npcs.npc.template(npc_id).cloned() else {
        tracing::error!("SpawnMount: npc template {npc_id} missing (NullReferenceException)");
        return;
    };
    // A dead mate (item flag set, or a saved row without HP) cannot be summoned until a
    // stablehand revives it; the client never checks the flag on its own.
    let saved = server
        .systems
        .vehicles
        .mates
        .character(owner_id, |cm| cm.get(skill_item_id).cloned());
    if item.details.mate_dead != 0 || saved.as_ref().is_some_and(|db| db.hp <= 0) {
        // A legacy row (zero HP, flag unset) gets the flag now so the client offers the revive.
        let (level, xp) = saved
            .as_ref()
            .map_or((item.details.mate_level, item.details.mate_exp), |db| {
                (db.level as u8, db.xp)
            });
        record_mate_death(server, owner, skill_item_id, level, xp);
        super::error(owner, error_message::MATE_CANNOT_SPAWN_WHILE_CORPSE);
        return;
    }
    let Ok(tl_id) = server.ids.tl.get_next_id() else {
        return;
    };
    let Ok(obj_id) = server.ids.object.get_next_id() else {
        return;
    };
    let db = saved.or_else(|| create_new_mate(server, owner_id, skill_item_id, npc_id));
    let Some(db) = db else {
        tracing::error!(
            "SpawnMount: no mate info for item {skill_item_id} (NullReferenceException)"
        );
        return;
    };
    let (owner_faction, owner_world) = {
        let c = owner.data();
        (c.unit.faction_id, c.unit.transform)
    };
    let owner_pos = server
        .world
        .world(owner_world.instance_id)
        .map(|w| w.world_pos_rot(&WorldObject::from(owner)))
        .unwrap_or_else(|| owner_world.local());
    let mut m = Mate::new(db.id, template.id);
    m.unit.tl_id = tl_id as u16;
    m.unit.owner_id = owner_id;
    m.unit.name = db.name.clone();
    m.unit.model_id = template.model_id;
    m.unit.faction_id = owner_faction;
    m.unit.level = db.level as u8;
    // New mates start with the configured high pool values in create_new_mate. A zero HP row
    // never gets here (dead mates are refused above); zero MP is an exhausted mate.
    m.unit.hp = db.hp.max(0);
    m.unit.mp = db.mp.max(0);
    m.unit.scale = template.scale;
    m.owner_obj_id = owner.obj_id;
    m.item_id = db.item_id;
    m.user_state = 1;
    m.experience = db.xp;
    m.mileage = db.mileage;
    m.spawn_delay_time = 0;
    m.mate_kind_id = template.mate_kind_id;
    // TODO (C#): spawn with the template's seat count; 2 seats by default.
    m.passengers = vec![
        (
            attach_point::DRIVER,
            MatePassengerInfo {
                obj_id: 0,
                reason: 0,
            },
        ),
        (
            attach_point::PASSENGER0,
            MatePassengerInfo {
                obj_id: 0,
                reason: 0,
            },
        ),
    ];
    // Transform = Owner.Transform.CloneDetached(mount)
    m.unit.transform = crate::models::transform::Transform {
        instance_id: owner_world.instance_id,
        world_id: owner_world.world_id,
        zone_id: owner_world.zone_id,
        parent_obj_id: 0,
        ..Default::default()
    };
    m.unit.transform.set_position(
        owner_pos.x,
        owner_pos.y,
        owner_pos.z,
        owner_pos.rot_x,
        owner_pos.rot_y,
        owner_pos.rot_z,
    );
    m.skills = server.game_data.systems.vehicles.mate_skills(npc_id);
    // Equipment = ItemManager.GetItemContainerForCharacter(Owner.Id, EquipmentMate, mount, Id)
    let container = server.items.container_for_character(
        owner_id,
        slot_type::EQUIPMENT_MATE,
        db.id,
        &server.ids.container,
    );
    {
        let c = container.lock().expect("container poisoned");
        for item in &c.items {
            if let Some(slot) = m.equipment.get_mut(item.slot.max(0) as usize) {
                *slot = Some(item.clone());
            }
        }
    }
    let mate = Entity::new(obj_id, m);
    let mate_obj = WorldObject::from(&mate);
    // Template buffs (`buff.Apply(mount, SkillCasterUnit(mount.ObjId), mount, ...)`)
    for buff_id in &template.buffs {
        let Some(t) = server.game_data.systems.skills.buff(*buff_id).cloned() else {
            continue;
        };
        crate::systems::skills::buffs::apply_template(
            server,
            &t,
            Some(&mate_obj),
            &SkillCaster::Unit(obj_id),
            &mate_obj,
            None,
        );
    }
    // mount.UpdateGearBonuses(null, null) (refreshes the pools too).
    crate::systems::skills::stats::update_unit_gear_bonuses(server, &mate_obj, None, None);
    crate::systems::dispatch_buff_events(server, &mate_obj);
    {
        let mut d = mate.data();
        d.unit.hp = d.unit.hp.min(d.unit.max_hp);
        d.unit.mp = d.unit.mp.min(d.unit.max_mp);
        // Transform.Local.AddDistanceToFront(3f)
        let rz = d.unit.transform.rot_z;
        d.unit.transform.x += -3.0 * rz.sin();
        d.unit.transform.y += 3.0 * rz.cos();
    }
    let update = Some(super::packets::item_update(&item));
    add_active_mate_and_spawn(server, owner, &mate, update);
    let hp = mate.data().unit.hp;
    crate::systems::skills::combat::post_update_current_hp(
        server,
        &mate_obj,
        Some(&mate_obj),
        0,
        hp,
        KillReason::Unknown,
    );
}

/// `owner.Mates.DespawnMate(tlId)`.
pub fn despawn_mate(server: &GameServer, owner: &PlayerRef, tl_id: u32) {
    if !manager_of(server, owner)
        .get_active_mate_by_tl_id(tl_id)
        .is_some_and(|mate| mate.data().owner_obj_id == owner.obj_id)
    {
        return;
    }
    let owner_id = owner.data().id;
    if let Some(mate) = manager_of(server, owner).get_active_mate_by_tl_id(tl_id) {
        let snapshot = {
            let d = mate.data();
            (
                d.item_id,
                d.unit.hp,
                d.unit.mp,
                d.unit.level,
                d.experience,
                d.mileage,
                d.unit.name.clone(),
            )
        };
        server.systems.vehicles.mates.character(owner_id, |cm| {
            if let Some(db) = cm.get_mut(snapshot.0) {
                db.hp = snapshot.1;
                db.mp = snapshot.2;
                db.level = u16::from(snapshot.3);
                db.xp = snapshot.4;
                db.mileage = snapshot.5;
                db.name = snapshot.6.clone();
                db.updated_at = chrono::Utc::now().naive_utc();
            }
        });
        if snapshot.1 <= 0 {
            // Idempotent: the death hook already flagged the item and told the client.
            record_mate_death(server, owner, snapshot.0, snapshot.3, snapshot.4);
        }
    }
    remove_active_mate_and_despawn(server, owner, tl_id);
}

/// Marks the mate of the summon item `item_id` dead: the `mates` row loses its HP, the item gets
/// its dead flag (plus the mate's live level / exp, which the client prices the revive from) and
/// is marked dirty, and the owner receives `SCItemTaskSuccess(MateDeath, ItemUpdate)`. Returns
/// `true` when the flag changed; repeated calls change and send nothing.
fn record_mate_death(
    server: &GameServer,
    owner: &PlayerRef,
    item_id: u64,
    level: u8,
    exp: i32,
) -> bool {
    if item_id == 0 {
        return false;
    }
    let owner_id = owner.data().id;
    server.systems.vehicles.mates.character(owner_id, |cm| {
        if let Some(db) = cm.get_mut(item_id) {
            if db.hp != 0 {
                db.hp = 0;
                db.updated_at = chrono::Utc::now().naive_utc();
            }
        }
    });
    let task = update_owned_item(server, Some(owner), item_id, |i| {
        if i.class != ItemClass::SummonMate || i.details.mate_dead != 0 {
            return None;
        }
        i.details.mate_dead = 1;
        i.details.mate_level = level;
        i.details.mate_exp = exp;
        i.dirty = true;
        Some(super::packets::item_update(i))
    })
    .flatten();
    let Some(task) = task else {
        return false;
    };
    owner.send(SCItemTaskSuccessPacket {
        action: item_task_type::MATE_DEATH,
        tasks: vec![task],
        force_remove: Vec::new(),
    });
    true
}

/// The death of a mate (`Unit.DoDie` event point, see `vehicles::on_death_event`): the summon
/// item is flagged dead and the owner is told ([`record_mate_death`] + `MATE_DEAD`). The corpse
/// stays in the world until the owner dismisses it.
pub fn on_death(server: &GameServer, mate: &MateRef) {
    let (item_id, owner_obj, level, exp) = {
        let d = mate.data();
        (d.item_id, d.owner_obj_id, d.unit.level, d.experience)
    };
    let Some(owner) = server.world.character_by_obj_id(owner_obj) else {
        return;
    };
    if record_mate_death(server, &owner, item_id, level, exp) {
        super::error(&owner, error_message::MATE_DEAD);
    }
}

/// `CSRepairPetItemsPacket` (the stablehand revive): every dead summon mate item in the bag and
/// the bank is revived for the total of `floor(0.5 * level + level^2.3 + 0.5)` copper per item
/// (the client's `GetTotalRepairsForPetItems`). Nothing happens unless `npc_obj_id` is the
/// character's current interaction object, a stabler within reach, none of the dead mates is
/// still summoned and the character can pay.
pub fn revive_pet_items(server: &GameServer, player: &PlayerRef, npc_obj_id: u32) {
    use crate::models::item_task::{task_type, ItemTask};
    use crate::systems::economy::inventory::{self, Inv, ItemTxn};
    if npc_obj_id == 0 || player.data().unit.current_interaction_object != npc_obj_id {
        return;
    }
    let Some(world) = server.world.world_of(&WorldObject::from(player)) else {
        return;
    };
    let Some(npc) = world.npc(npc_obj_id) else {
        return;
    };
    let stabler = server
        .systems
        .economy
        .hooks
        .npcs
        .get()
        .and_then(|h| h.shop_info(server, &npc))
        .is_some_and(|i| i.stabler);
    if !stabler {
        tracing::warn!(
            "Attempting to revive a mate while not at a stabler, NPC: {}",
            npc.obj_id
        );
        return;
    }
    let me = world.world_pos_rot(&WorldObject::from(player));
    let them = world.world_pos_rot(&WorldObject::from(&npc));
    if me.distance_to(&them, false) > 5.0 {
        super::error(player, error_message::TOO_FAR_AWAY);
        return;
    }
    let owner_id = player.data().id;
    let inv = Inv::of(player);
    let containers: Vec<_> = [slot_type::INVENTORY, slot_type::BANK]
        .into_iter()
        .filter_map(|t| inv.get(t))
        .collect();
    let mut txn = ItemTxn::begin(server);
    // (item id, detail level) of the dead mates.
    let mut dead = Vec::new();
    for c in &containers {
        let c = c.lock().expect("container poisoned");
        dead.extend(
            c.items
                .iter()
                .filter(|i| i.class == ItemClass::SummonMate && i.details.mate_dead != 0)
                .map(|i| (i.id, i.details.mate_level)),
        );
    }
    if dead.is_empty() {
        tracing::debug!("RepairPetItems: no dead mate item, NPC: {}", npc.obj_id);
        return;
    }
    let active = manager_of(server, player).get_active_mates(owner_id);
    if active
        .iter()
        .any(|m| dead.iter().any(|(id, _)| *id == m.data().item_id))
    {
        super::error(player, error_message::MATE_CANNOT_REPAIR_WHILE_CORPSE);
        return;
    }
    let total: i64 = dead.iter().map(|(_, l)| i64::from(revive_cost(*l))).sum();
    let Ok(cost) = i32::try_from(total) else {
        return;
    };
    if player.data().money < i64::from(cost) {
        inventory::send_error_message_to(&mut txn.out(), player, error_message::NOT_ENOUGH_MONEY);
        return;
    }
    if cost > 0 {
        inventory::change_money_at(
            &mut txn.out(),
            player,
            slot_type::INVENTORY,
            -cost,
            task_type::DEPOSIT_MONEY,
        );
    }
    let now = chrono::Utc::now().naive_utc();
    let mut tasks = Vec::new();
    for (item_id, _) in &dead {
        for c in &containers {
            let mut c = c.lock().expect("container poisoned");
            if let Some(i) = c.get_item_by_id_mut(*item_id) {
                i.details.mate_dead = 0;
                i.dirty = true;
                tasks.push(ItemTask::update(i));
                break;
            }
        }
    }
    server.systems.vehicles.mates.character(owner_id, |cm| {
        for (item_id, _) in &dead {
            if let Some(db) = cm.get_mut(*item_id) {
                db.hp = MATE_FULL_POOL;
                db.mp = MATE_FULL_POOL;
                db.updated_at = now;
            }
        }
    });
    let mut out = txn.out();
    out.send(
        player,
        SCItemTaskSuccessPacket {
            action: task_type::REPAIR_PETS,
            tasks,
            force_remove: Vec::new(),
        },
    );
    inventory::send_error_message_to(&mut out, player, error_message::MATE_RESURRECTED);
}

/// The revive price of one dead mate item of detail level `level` (client formula).
pub fn revive_cost(level: u8) -> i32 {
    let l = f64::from(level);
    (0.5 * l + l.powf(2.3) + 0.5).floor() as i32
}

// ---------------------------------------------------------------------------------------------
// Mate behaviour
// ---------------------------------------------------------------------------------------------

/// `Mate.AddExp(expDelta)`.
pub fn add_exp(server: &GameServer, mate: &MateRef, exp_delta: i32) {
    if exp_delta <= 0 {
        return;
    }
    let exp_table = &server.game_data.experience;
    let max = exp_table.max_mate_level();
    let (delta, leveled_up, level, exp, item_id, owner_obj, owner_id) = {
        let mut d = mate.data();
        if d.unit.level >= max {
            return;
        }
        let delta = (server.config.world.exp_rate * f64::from(exp_delta)).round_ties_even() as i32;
        let mut new_exp = d.experience.wrapping_add(delta);
        let (new_level, overflow) = match exp_table.level_from_exp(new_exp, d.unit.level, true) {
            Ok(v) => v,
            Err(e) => {
                tracing::error!("Mate.AddExp: {e}");
                return;
            }
        };
        let leveled = new_level > d.unit.level;
        if new_level >= max {
            new_exp -= overflow;
        }
        d.experience = new_exp;
        d.unit.level = new_level;
        (
            delta,
            leveled,
            new_level,
            new_exp,
            d.item_id,
            d.owner_obj_id,
            d.unit.owner_id,
        )
    };
    // UpdateMateItemData
    let owner = server.world.character_by_obj_id(owner_obj);
    if item_id > 0 {
        update_owned_item(server, owner.as_ref(), item_id, |i| {
            if i.class == ItemClass::SummonMate {
                i.details.mate_exp = exp;
                i.details.mate_level = level;
                i.dirty = true;
            }
        });
    }
    // DbInfo.Xp / DbInfo.Level
    server.systems.vehicles.mates.character(owner_id, |cm| {
        if let Some(db) = cm.get_mut(item_id) {
            db.xp = exp;
            db.level = u16::from(level);
        }
    });
    let Some(owner) = owner else {
        tracing::error!("Mate.AddExp: owner {owner_obj} not found (NullReferenceException)");
        return;
    };
    owner.send(SCExpChangedPacket {
        obj_id: mate.obj_id,
        exp: delta,
        should_add_ability_exp: false,
    });
    if leveled_up {
        stats::refresh_object_pools(server, &WorldObject::from(mate));
        broadcast(
            server,
            &WorldObject::from(mate),
            &SCLevelChangedPacket {
                obj_id: mate.obj_id,
                level,
            },
            true,
        );
        crate::systems::quests::events::on_mate_level_up(server, &owner);
        // PET_LEVEL: the mate's absolute level against its npc template.
        let template_id = mate.data().unit.template_id;
        crate::systems::report_achievement(
            server,
            &owner,
            AchievementInput::Absolute {
                kind_id: char_record_kind::PET_LEVEL,
                value: i64::from(level),
                matcher: RecordMatch::Value1(template_id),
            },
        );
    }
}

/// `Mate.StartUpdateXp(owner)`: schedules `MateXpUpdateTask` (60 s) unless one is pending or
/// the mate is at the maximum level.
pub fn start_update_xp(server: &GameServer, owner: &PlayerRef, mate: &MateRef) {
    let gen = {
        let mut d = mate.data();
        if d.xp_task != 0 || is_max_level(server, &d) {
            return;
        }
        let gen = XP_TASK_GEN.fetch_add(1, Ordering::Relaxed);
        d.xp_task = gen;
        gen
    };
    let (Some(server), Ok(rt)) = (server.self_arc(), tokio::runtime::Handle::try_current()) else {
        return;
    };
    let (owner, mate) = (owner.clone(), mate.clone());
    rt.spawn(async move {
        tokio::time::sleep(std::time::Duration::from_millis(MATE_XP_TASK_DELAY_MS)).await;
        if mate.data().xp_task != gen {
            return;
        }
        mate_xp_update_task(&server, &owner, &mate);
    });
}

/// `MateXpUpdateTask.Execute`.
pub fn mate_xp_update_task(server: &GameServer, owner: &PlayerRef, mate: &MateRef) {
    mate.data().xp_task = 0;
    add_exp(server, mate, MATE_XP_TASK_EXP);
    crate::world::session::send_message(
        owner,
        crate::managers::chat::chat_type::SYSTEM,
        &format!("pet received {MATE_XP_TASK_EXP} experience points"),
    );
    tracing::debug!(
        "[MateXpUpdateTask] Id {}, ObjId {}, AddExp {MATE_XP_TASK_EXP}",
        mate.data().id,
        mate.obj_id
    );
}

/// `Mate.StopUpdateXp()`.
pub fn stop_update_xp(mate: &MateRef) {
    mate.data().xp_task = 0;
}

/// `Mate.RegenTick(delta)`.
pub fn regen_tick(server: &GameServer, mate: &MateRef) {
    let obj = WorldObject::from(mate);
    let (needs, dead, tl, passengers) = {
        let d = mate.data();
        (
            d.unit.needs_regen(),
            d.unit.is_dead(),
            u32::from(d.unit.tl_id),
            d.passengers.clone(),
        )
    };
    if !needs {
        return;
    }
    if dead {
        for (point, info) in passengers.iter().rev() {
            if server.world.character_by_obj_id(info.obj_id).is_some() {
                unmount_mate_by(server, None, tl, *point, attach_reason::NONE);
            }
        }
        return;
    }
    stats::refresh_object_pools(server, &obj);
    let (old, hp, mp) = {
        let mut d = mate.data();
        let u = &mut d.unit;
        let old = u.hp;
        if u.is_in_battle {
            u.hp += u.persistent_hp_regen;
            u.mp += u.persistent_mp_regen;
        } else {
            u.hp += u.hp_regen;
            u.mp += u.mp_regen;
        }
        u.hp = u.hp.min(u.max_hp);
        u.mp = u.mp.min(u.max_mp);
        (old, u.hp, u.mp)
    };
    broadcast(
        server,
        &obj,
        &SCUnitPointsPacket {
            obj_id: mate.obj_id,
            hp,
            mp,
        },
        false,
    );
    crate::systems::skills::combat::post_update_current_hp(
        server,
        &obj,
        Some(&obj),
        old,
        hp,
        KillReason::Unknown,
    );
}

/// `Mate.DoFallDamage(fallVel)`: when the mount dies, its riders take fall damage too and the
/// ones that die are unmounted.
pub fn do_fall_damage(server: &GameServer, mate: &MateRef, fall_vel: u16) -> i32 {
    let obj = WorldObject::from(mate);
    let dmg = crate::systems::skills::combat::do_fall_damage(server, &obj, fall_vel);
    let (hp, tl, riders) = {
        let d = mate.data();
        (d.unit.hp, u32::from(d.unit.tl_id), d.passengers.clone())
    };
    if hp <= 0 {
        for (point, info) in riders.iter().rev() {
            let Some(rider) = server.world.character_by_obj_id(info.obj_id) else {
                continue;
            };
            crate::systems::skills::combat::do_fall_damage(
                server,
                &WorldObject::from(&rider),
                fall_vel,
            );
            if rider.data().unit.hp <= 0 {
                let instance = rider.data().unit.transform.instance_id;
                unmount_mate_by(
                    server,
                    Some(instance),
                    tl,
                    *point,
                    attach_reason::SLAVE_BINDING,
                );
            }
        }
    }
    dmg
}

/// `Mate.OnZoneChange(last, new)`: the base unit zone change, then every passenger's.
pub fn on_zone_change(server: &GameServer, mate: &MateRef, last_zone: u32, new_zone: u32) {
    let passengers = mate.data().passengers.clone();
    for (_, info) in passengers {
        if let Some(p) = server.world.character_by_obj_id(info.obj_id) {
            crate::world::session::on_zone_change(server, &p, last_zone, new_zone);
        }
    }
}

/// `GetActiveMateByMateObjId(objId)?.OwnerObjId` over every instance (`Team.IsObjMember`).
pub fn active_mate_owner_obj_id(server: &GameServer, mate_obj_id: u32) -> Option<u32> {
    server
        .systems
        .vehicles
        .mates
        .all()
        .into_iter()
        .find_map(|m| m.get_active_mate_by_obj_id(mate_obj_id))
        .map(|m| m.data().owner_obj_id)
}

/// `MateManager.GetActiveMates(character.Id).FirstOrDefault()`.
pub fn first_active_mate(server: &GameServer, p: &PlayerRef) -> Option<MateRef> {
    let id = p.data().id;
    manager_of(server, p)
        .get_active_mates(id)
        .into_iter()
        .next()
}

/// The mate part of `Npc.DoDie` experience: every active mate of the player gets `exp`
/// (`mate.AddExp`) and the player a "Pet gained" message.
pub fn reward_kill_exp(server: &GameServer, p: &PlayerRef, exp: i32) {
    let id = p.data().id;
    for mate in manager_of(server, p).get_active_mates(id) {
        add_exp(server, &mate, exp);
        // TODO (C#): Proper message?
        crate::world::session::send_message(
            p,
            crate::managers::chat::chat_type::SYSTEM,
            &format!("Pet gained {exp} XP"),
        );
    }
}

/// `Unit.DespawnMate(character)` of `Unit.DoDie`: a dead character's mates are despawned (for
/// a mate victim, its owner's; nothing happens while the character is alive). Deaths of any
/// other victim kind are not mate deaths and are ignored.
pub fn despawn_mate_on_death(server: &GameServer, victim: &WorldObject) {
    let character = match victim {
        WorldObject::Player(p) => p.clone(),
        WorldObject::Mate(m) => {
            let owner_obj_id = m.data().owner_obj_id;
            let Some(owner) = server.world.character_by_obj_id(owner_obj_id) else {
                tracing::error!(
                    "DespawnMate: no character (NullReferenceException): mate obj_id {} owner_obj_id {owner_obj_id}",
                    m.obj_id
                );
                return;
            };
            owner
        }
        _ => return,
    };
    if character.data().unit.hp > 0 {
        return;
    }
    let id = character.data().id;
    for mate in manager_of(server, &character).get_active_mates(id) {
        let tl = u32::from(mate.data().unit.tl_id);
        despawn_mate(server, &character, tl);
    }
}

/// `Buffs.AddBuff` (stun / sleep / root, `driver_only = false`: every active mate's driver
/// seat) and `Skill.Use` (`Template.Unmount`, `driver_only = true`: only where the character is
/// the driver) for a riding character.
pub fn unmount_riding_character(server: &GameServer, p: &PlayerRef, driver_only: bool) {
    let (riding, id) = {
        let c = p.data();
        (c.is_riding, c.id)
    };
    if !riding {
        return;
    }
    for mate in manager_of(server, p).get_active_mates(id) {
        let (tl, driver) = {
            let d = mate.data();
            (
                u32::from(d.unit.tl_id),
                d.passenger(attach_point::DRIVER).map_or(0, |i| i.obj_id),
            )
        };
        if driver_only && driver != p.obj_id {
            continue;
        }
        unmount_mate(server, p, tl, attach_point::DRIVER, attach_reason::NONE);
    }
}

// ---------------------------------------------------------------------------------------------
// Persistence (`CharacterMates.Load` / `Save`)
// ---------------------------------------------------------------------------------------------

/// `CharacterMates.Load(connection)`: replaces the character's rows.
pub async fn load_character_mates(server: &GameServer, owner_id: u32) -> sqlx::Result<()> {
    let rows = sqlx::query(
        "SELECT CAST(id AS UNSIGNED) AS id, CAST(item_id AS UNSIGNED) AS item_id, name, \
         CAST(xp AS SIGNED) AS xp, CAST(level AS SIGNED) AS level, CAST(mileage AS SIGNED) AS mileage, \
         CAST(hp AS SIGNED) AS hp, CAST(mp AS SIGNED) AS mp, CAST(owner AS UNSIGNED) AS owner, \
         updated_at, created_at FROM mates WHERE `owner` = ?",
    )
    .bind(owner_id)
    .fetch_all(&server.db)
    .await?;
    let mut list = CharacterMates::default();
    for r in rows {
        let item_id: u64 = r.try_get("item_id")?;
        if list.get(item_id).is_some() {
            // `_mates.Add` throws on a duplicate item id (aborts Character.Load).
            return Err(sqlx::Error::Protocol(format!(
                "mates: duplicate item id {item_id} for owner {owner_id}"
            )));
        }
        list.mates.push(MateDb {
            id: r.try_get::<u64, _>("id")? as u32,
            item_id,
            name: r.try_get("name")?,
            xp: r.try_get::<i64, _>("xp")? as i32,
            level: r.try_get::<i64, _>("level")? as u16,
            mileage: r.try_get::<i64, _>("mileage")? as i32,
            hp: r.try_get::<i64, _>("hp")? as i32,
            mp: r.try_get::<i64, _>("mp")? as i32,
            owner: r.try_get::<u64, _>("owner")? as u32,
            updated_at: r.try_get("updated_at")?,
            created_at: r.try_get("created_at")?,
        });
    }
    server
        .systems
        .vehicles
        .mates
        .characters
        .lock()
        .expect("character mates poisoned")
        .insert(owner_id, list);
    Ok(())
}

/// `CharacterMates.Save(connection, transaction)`.
pub async fn save_character_mates(
    server: &GameServer,
    conn: &mut sqlx::MySqlConnection,
    owner_id: u32,
) -> sqlx::Result<usize> {
    let Some(cm) = server.systems.vehicles.mates.character_mates(owner_id) else {
        return Ok(0);
    };
    let mut n = 0;
    if !cm.removed.is_empty() {
        let ids: Vec<String> = cm.removed.iter().map(u32::to_string).collect();
        sqlx::query(&format!(
            "DELETE FROM mates WHERE owner = ? AND id IN({})",
            ids.join(",")
        ))
        .bind(owner_id)
        .execute(&mut *conn)
        .await?;
        server.systems.vehicles.mates.character(owner_id, |c| {
            c.removed.retain(|id| !cm.removed.contains(id));
        });
    }
    for m in &cm.mates {
        sqlx::query(
            "REPLACE INTO mates(`id`,`item_id`,`name`,`xp`,`level`,`mileage`,`hp`,`mp`,`owner`,`updated_at`,`created_at`) \
             VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
        )
        .bind(m.id)
        .bind(m.item_id)
        .bind(&m.name)
        .bind(m.xp)
        .bind(m.level)
        .bind(m.mileage)
        .bind(m.hp)
        .bind(m.mp)
        .bind(m.owner)
        .bind(m.updated_at)
        .bind(m.created_at)
        .execute(&mut *conn)
        .await?;
        n += 1;
    }
    Ok(n)
}

/// `Character.Load` part: the previously selected character's mates of every instance are
/// despawned (`CSSelectCharacterPacket`).
pub fn on_character_selected(server: &GameServer, character_id: u32) {
    let instances: Vec<u32> = server
        .systems
        .vehicles
        .mates
        .managers
        .iter()
        .map(|e| *e.key())
        .collect();
    for i in instances {
        remove_and_despawn_all_active_owned_mates(server, i, character_id);
    }
}

/// `now` for the mate rows.
pub fn now_naive() -> NaiveDateTime {
    chrono::DateTime::from_timestamp_millis(now_ms())
        .unwrap_or_default()
        .naive_utc()
}
