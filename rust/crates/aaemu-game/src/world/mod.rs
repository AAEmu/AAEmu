//! The world: `WorldManager` (instances, the server-wide character list), `WorldInstance`
//! (object tables, region grid, visibility) and `Region` (per-sector object lists, neighbour
//! visibility) for every object kind (`world::object::WorldObject`).
//!
//! Locking: each instance has one grid mutex that serialises its visibility updates; while it is
//! held, object data mutexes may be locked (one at a time) to build packets. Code that holds an
//! object's data lock must never call into the grid. Full rules: [`object`].

pub mod object;
pub mod session;
pub mod unit_state;
#[cfg(test)]
mod unit_state_tests;
pub mod visibility;
#[cfg(test)]
mod world_manager_tests;
pub mod zone_conflicts;

use std::collections::HashMap;
use std::sync::atomic::AtomicBool;
use std::sync::{Arc, Mutex, MutexGuard};

use dashmap::DashMap;

use crate::data::world_templates::{WorldTemplate, REGION_SIZE, SECTORS_PER_CELL};
use crate::data::GameData;
use crate::managers::name::NameManager;
use crate::models::character::Character;
use crate::models::transform::PositionAndRotation;
use crate::net::connection::ConnectionHandle;
use crate::net::packet::ServerPacket;
use crate::packets::world::SCUnitsRemovedPacket;
use crate::packets::world_objects::{
    SCDoodadsCreatedPacket, SCDoodadsRemovedPacket, SCGimmickJointsBrokenPacket,
    SCGimmicksCreatedPacket, SCGimmicksRemovedPacket, SCTargetChangedPacket,
};

pub use object::{
    DoodadRef, Entity, GimmickRef, HouseRef, MateRef, NpcRef, ObjectCore, ObjectKind, RegionKey,
    ShipyardRef, SlaveRef, TransferRef, WorldObject,
};

/// `WorldManager.REGION_NEIGHBORHOOD_SIZE`.
const REGION_NEIGHBORHOOD_SIZE: i32 = 2;
/// Parent chains longer than this are treated as broken (cycle guard).
const MAX_PARENT_DEPTH: usize = 16;

/// Current time in unix milliseconds (`DateTime.UtcNow`).
pub fn now_ms() -> i64 {
    chrono::Utc::now().timestamp_millis()
}

/// What the visibility packets need besides the objects: game data (buff templates),
/// `NameManager` (owner names) and the current time.
#[derive(Clone, Copy)]
pub struct ViewCtx<'a> {
    pub data: &'a GameData,
    pub names: &'a NameManager,
    pub now_ms: i64,
}

/// A player character in the world (`Connection.ActiveChar`).
#[derive(Debug)]
pub struct Player {
    pub obj_id: u32,
    pub character_id: u32,
    /// The owning game connection (`Character.Connection`).
    pub conn: ConnectionHandle,
    pub data: Mutex<Character>,
    /// Region, `IsVisible`, transform children.
    pub(crate) core: ObjectCore,
    /// `Character.Subscribers` (time-of-day subscriptions).
    subscriptions: Mutex<Vec<u64>>,
}

pub type PlayerRef = Arc<Player>;

impl Player {
    pub fn new(character: Character, conn: ConnectionHandle) -> PlayerRef {
        Arc::new(Self {
            obj_id: character.obj_id,
            character_id: character.id,
            conn,
            data: Mutex::new(character),
            core: ObjectCore::default(),
            subscriptions: Mutex::new(Vec::new()),
        })
    }

    pub fn data(&self) -> MutexGuard<'_, Character> {
        self.data.lock().expect("character poisoned")
    }

    /// `Character.SendPacket`.
    pub fn send(&self, packet: impl ServerPacket + 'static) {
        self.conn.send(packet);
    }

    pub fn send_dyn(&self, packet: &dyn ServerPacket) {
        self.conn.send_dyn(packet);
    }

    pub fn region(&self) -> Option<RegionKey> {
        self.core.region()
    }

    pub fn is_visible(&self) -> bool {
        self.core.is_visible()
    }

    pub fn core(&self) -> &ObjectCore {
        &self.core
    }
}

/// A zone change produced by `Region.AddObject` (`Transform.ZoneId` setter ->
/// `OnZoneChange(old, new)`), for the caller to dispatch after the grid is released
/// (`session::handle_zone_changes`).
#[derive(Debug, Clone)]
pub struct ZoneChange {
    pub object: WorldObject,
    pub old_zone: u32,
    pub new_zone: u32,
}

#[derive(Debug, Default)]
struct Grid {
    /// Sector -> `_objects` (append on add, swap-remove on remove, like the C# array).
    regions: HashMap<(i32, i32), Vec<WorldObject>>,
}

impl Grid {
    fn objects(&self, key: (i32, i32)) -> &[WorldObject] {
        self.regions.get(&key).map_or(&[], Vec::as_slice)
    }
}

/// `WorldInstance` (+ its `Region[,]`).
#[derive(Debug)]
pub struct WorldInstance {
    pub id: u32,
    pub template: Arc<WorldTemplate>,
    grid: Mutex<Grid>,
    /// `_objects` (every object of the instance by obj id; the typed C# dictionaries are
    /// filtered views of it).
    objects: DashMap<u32, WorldObject>,
    /// `_characters` (instance players by obj id).
    characters: DashMap<u32, PlayerRef>,
}

impl WorldInstance {
    pub fn new(id: u32, template: Arc<WorldTemplate>) -> Self {
        Self {
            id,
            template,
            grid: Mutex::new(Grid::default()),
            objects: DashMap::new(),
            characters: DashMap::new(),
        }
    }

    /// `GetRegionByPos`.
    pub fn region_by_pos(&self, x: f32, y: f32) -> Option<(i32, i32)> {
        let sx = (x / REGION_SIZE as f32) as i32;
        let sy = (y / REGION_SIZE as f32) as i32;
        self.template.valid_region(sx, sy).then_some((sx, sy))
    }

    /// `WorldManager.GetNeighbors(world, x, y)` (includes the region itself).
    pub fn neighbors(&self, (x, y): (i32, i32)) -> Vec<(i32, i32)> {
        let mut out = Vec::new();
        for a in -REGION_NEIGHBORHOOD_SIZE..=REGION_NEIGHBORHOOD_SIZE {
            for b in -REGION_NEIGHBORHOOD_SIZE..=REGION_NEIGHBORHOOD_SIZE {
                if self.template.valid_region(x + a, y + b) {
                    out.push((x + a, y + b));
                }
            }
        }
        out
    }

    // ----- object tables (`AddObject` / `RemoveObject` / `Get*`) -----

    /// `WorldInstance.AddObject` (`TryAdd`: an existing entry is kept).
    pub fn add_object(&self, obj: impl Into<WorldObject>) {
        let obj = obj.into();
        if let WorldObject::Player(p) = &obj {
            self.characters.entry(p.obj_id).or_insert_with(|| p.clone());
        }
        self.objects.entry(obj.obj_id()).or_insert(obj);
    }

    /// `WorldInstance.RemoveObject`.
    pub fn remove_object(&self, obj_id: u32) -> bool {
        let a = self.objects.remove(&obj_id).is_some();
        let b = self.characters.remove(&obj_id).is_some();
        a | b
    }

    /// `GetGameObject(objId)`.
    pub fn object(&self, obj_id: u32) -> Option<WorldObject> {
        self.objects.get(&obj_id).map(|o| o.clone())
    }

    /// `GetUnit(objId)` (any unit kind).
    pub fn unit(&self, obj_id: u32) -> Option<WorldObject> {
        self.object(obj_id).filter(WorldObject::is_unit)
    }

    /// `GetNpc(objId)`.
    pub fn npc(&self, obj_id: u32) -> Option<NpcRef> {
        self.object(obj_id).and_then(|o| o.as_npc().cloned())
    }

    /// `GetDoodad(objId)`.
    pub fn doodad(&self, obj_id: u32) -> Option<DoodadRef> {
        self.object(obj_id).and_then(|o| o.as_doodad().cloned())
    }

    /// `GetSlaveByObjId(objId)`.
    pub fn slave(&self, obj_id: u32) -> Option<SlaveRef> {
        self.object(obj_id).and_then(|o| o.as_slave().cloned())
    }

    pub fn mate(&self, obj_id: u32) -> Option<MateRef> {
        self.object(obj_id).and_then(|o| o.as_mate().cloned())
    }

    pub fn house(&self, obj_id: u32) -> Option<HouseRef> {
        self.object(obj_id).and_then(|o| o.as_house().cloned())
    }

    pub fn transfer(&self, obj_id: u32) -> Option<TransferRef> {
        self.object(obj_id).and_then(|o| o.as_transfer().cloned())
    }

    pub fn shipyard(&self, obj_id: u32) -> Option<ShipyardRef> {
        self.object(obj_id).and_then(|o| o.as_shipyard().cloned())
    }

    pub fn gimmick(&self, obj_id: u32) -> Option<GimmickRef> {
        self.object(obj_id).and_then(|o| o.as_gimmick().cloned())
    }

    /// `GetCharacterByObjId(objId)`.
    pub fn character_by_obj_id(&self, obj_id: u32) -> Option<PlayerRef> {
        self.characters.get(&obj_id).map(|p| p.clone())
    }

    /// `GetAllCharacters`.
    pub fn all_characters(&self) -> Vec<PlayerRef> {
        self.characters.iter().map(|p| p.clone()).collect()
    }

    /// Every object of one kind (`GetAllNpcs`, `GetAllDoodads`, `GetAllSlaves`, ...).
    pub fn all_of_kind(&self, kind: ObjectKind) -> Vec<WorldObject> {
        self.objects
            .iter()
            .filter(|o| o.kind() == kind)
            .map(|o| o.clone())
            .collect()
    }

    /// `GetAllNpcs`.
    pub fn all_npcs(&self) -> Vec<NpcRef> {
        self.objects
            .iter()
            .filter_map(|o| o.as_npc().cloned())
            .collect()
    }

    /// `GetAllDoodads`.
    pub fn all_doodads(&self) -> Vec<DoodadRef> {
        self.objects
            .iter()
            .filter_map(|o| o.as_doodad().cloned())
            .collect()
    }

    /// `GetAllSlaves`.
    pub fn all_slaves(&self) -> Vec<SlaveRef> {
        self.objects
            .iter()
            .filter_map(|o| o.as_slave().cloned())
            .collect()
    }

    /// `GetNpcByTemplateId(templateId)` (first match; C# order is unspecified too).
    pub fn npc_by_template_id(&self, template_id: u32) -> Option<NpcRef> {
        self.all_npcs()
            .into_iter()
            .find(|n| n.data().unit.template_id == template_id)
    }

    /// `GetDoodadByDbId(dbId)`.
    pub fn doodad_by_db_id(&self, db_id: u32) -> Option<DoodadRef> {
        self.all_doodads()
            .into_iter()
            .find(|d| d.data().db_id == db_id)
    }

    /// `GetDoodadByHouseDbId(houseDbId)`.
    pub fn doodads_by_house_db_id(&self, house_db_id: u32) -> Vec<DoodadRef> {
        self.all_doodads()
            .into_iter()
            .filter(|d| d.data().owner_db_id == house_db_id)
            .collect()
    }

    /// Number of objects in the instance (all kinds).
    pub fn object_count(&self) -> usize {
        self.objects.len()
    }

    pub fn player_count(&self) -> usize {
        self.characters.len()
    }

    // ----- transforms -----

    /// `Transform.World` of `obj`: its local transform composed with its parents'
    /// (`Transform.GetWorldPosition`). A parent that is not in this instance ends the chain.
    pub fn world_pos_rot(&self, obj: &WorldObject) -> PositionAndRotation {
        let mut chain = vec![obj.transform()];
        let mut scales = Vec::new();
        let mut parent_id = chain[0].parent_obj_id;
        while parent_id != 0 && chain.len() <= MAX_PARENT_DEPTH {
            let Some(parent) = self.object(parent_id) else {
                break;
            };
            let t = parent.transform();
            scales.push(parent.scale());
            parent_id = t.parent_obj_id;
            chain.push(t);
        }
        // chain = [obj, parent, grandparent, ...]; compose from the root down.
        let mut world = chain.last().expect("non-empty").local();
        for i in (0..chain.len() - 1).rev() {
            world = PositionAndRotation::compose(&world, scales[i], &chain[i].local());
        }
        world
    }

    /// `WorldManager.GetRootObj(obj)`.
    pub fn root_object(&self, obj: &WorldObject) -> WorldObject {
        let mut cur = obj.clone();
        for _ in 0..MAX_PARENT_DEPTH {
            let parent_id = cur.transform().parent_obj_id;
            match (parent_id != 0).then(|| self.object(parent_id)).flatten() {
                Some(p) => cur = p,
                None => break,
            }
        }
        cur
    }

    /// `WorldManager.GetRegion(obj)`: the sector of the root object's world position.
    pub fn region_of(&self, obj: &WorldObject) -> Option<(i32, i32)> {
        let root = self.root_object(obj);
        let p = self.world_pos_rot(&root);
        self.region_by_pos(p.x, p.y)
    }

    /// Attaches `child` to `parent` (`Transform.Parent = parent.Transform`): the child's
    /// transform becomes relative to the parent (`local` is kept as given) and it follows the
    /// parent's visibility (`Transform.Children`).
    pub fn attach_child(&self, parent: &WorldObject, child: &WorldObject) {
        child.with_transform(|t| t.parent_obj_id = parent.obj_id());
        parent.core().add_child(child.clone());
    }

    /// Detaches `child` from its parent (`Transform.Parent = null`), keeping its current world
    /// position as the new local one.
    pub fn detach_child(&self, child: &WorldObject) {
        let parent_id = child.transform().parent_obj_id;
        if parent_id == 0 {
            return;
        }
        let world = self.world_pos_rot(child);
        child.with_transform(|t| {
            t.parent_obj_id = 0;
            t.set_position(
                world.x,
                world.y,
                world.z,
                world.rot_x,
                world.rot_y,
                world.rot_z,
            );
        });
        if let Some(parent) = self.object(parent_id) {
            parent.core().remove_child(child.obj_id());
        }
    }

    /// `Transform.StickyParent = parent?.Transform` (`SetStickyParent`): detaches the object
    /// from its parent (`Parent = null`), records the sticky parent and, when there is one,
    /// makes it the real parent too (`Parent = stickyParent`, world position kept). The C#
    /// never fills `StickyChildren` (`AttachStickyTransform` has no caller), so sticky parents
    /// carry no delta movement of their own.
    pub fn set_sticky_parent(&self, child: &WorldObject, parent: Option<&WorldObject>) {
        let new_id = parent.map_or(0, WorldObject::obj_id);
        if child.core().sticky_parent() == new_id {
            return;
        }
        self.detach_child(child);
        child.core().set_sticky_parent_id(new_id);
        if let Some(parent) = parent {
            crate::systems::npcs::doodad::funcs::world::attach_keeping_world(self, parent, child);
        }
    }

    /// `Transform.StickyParent?.GameObject` (when it is still in this world).
    pub fn sticky_parent(&self, obj: &WorldObject) -> Option<WorldObject> {
        match obj.core().sticky_parent() {
            0 => None,
            id => self.object(id),
        }
    }

    // ----- visibility (`WorldManager.AddVisibleObject` / `RemoveVisibleObject`, `Region`) -----

    /// `Region.AddToCharacters(obj)`.
    fn add_to_characters(
        &self,
        grid: &Grid,
        region: (i32, i32),
        obj: &WorldObject,
        ctx: &ViewCtx<'_>,
    ) {
        let objects = grid.objects(region);
        if objects.is_empty() {
            return;
        }
        let id = obj.obj_id();
        if let WorldObject::Player(me) = obj {
            let others = objects.iter().filter(|o| o.obj_id() != id);
            for go in others.clone() {
                match go {
                    // Doodads and gimmicks have their own batch packets below.
                    WorldObject::Doodad(_) | WorldObject::Gimmick(_) => continue,
                    WorldObject::Npc(n) => {
                        let mut d = n.data();
                        if d.has_ai {
                            d.ai_should_tick = true; // "turn on the motion of the visible NPC"
                        }
                    }
                    _ => {}
                }
                visibility::add_visible_object(self, go, me, ctx);
            }
            let doodads: Vec<Vec<u8>> = others
                .clone()
                .filter_map(WorldObject::as_doodad)
                .map(|d| visibility::doodad_body(self, d, ctx.now_ms))
                .collect();
            for chunk in doodads.chunks(SCDoodadsCreatedPacket::MAX_COUNT_PER_PACKET) {
                me.send(SCDoodadsCreatedPacket {
                    bodies: chunk.to_vec(),
                });
            }
            let gimmicks: Vec<Vec<u8>> = others
                .filter_map(WorldObject::as_gimmick)
                .map(|g| visibility::gimmick_body(self, g))
                .collect();
            for chunk in gimmicks.chunks(SCGimmicksCreatedPacket::MAX_COUNT_PER_PACKET) {
                me.send(SCGimmicksCreatedPacket {
                    bodies: chunk.to_vec(),
                });
            }
            if !gimmicks.is_empty() {
                me.send(SCGimmickJointsBrokenPacket::default());
            }
        }
        for other in objects.iter().filter(|o| o.obj_id() != id) {
            if let Some(p) = other.as_player() {
                visibility::add_visible_object(self, obj, p, ctx);
            }
        }
    }

    /// `Region.RemoveFromCharacters(obj)`.
    fn remove_from_characters(&self, grid: &Grid, region: (i32, i32), obj: &WorldObject) {
        let objects = grid.objects(region);
        if objects.is_empty() {
            return;
        }
        let id = obj.obj_id();
        match obj {
            WorldObject::Player(me) => {
                let others: Vec<&WorldObject> =
                    objects.iter().filter(|o| o.obj_id() != id).collect();
                let unit_ids: Vec<u32> = others
                    .iter()
                    .filter(|o| o.is_unit())
                    .map(|o| o.obj_id())
                    .collect();
                for o in &others {
                    if let WorldObject::Npc(n) = o {
                        let mut d = n.data();
                        if d.has_ai {
                            d.ai_should_tick = false;
                        }
                    }
                }
                for chunk in unit_ids.chunks(SCUnitsRemovedPacket::MAX_COUNT_PER_PACKET) {
                    me.send(SCUnitsRemovedPacket {
                        ids: chunk.to_vec(),
                    });
                }
                let doodad_ids: Vec<u32> = others
                    .iter()
                    .filter(|o| o.kind() == ObjectKind::Doodad)
                    .map(|o| o.obj_id())
                    .collect();
                let max = SCDoodadsRemovedPacket::MAX_COUNT_PER_PACKET;
                for (i, chunk) in doodad_ids.chunks(max).enumerate() {
                    me.send(SCDoodadsRemovedPacket {
                        last: doodad_ids.len() - i * max <= max,
                        ids: chunk.to_vec(),
                    });
                }
                // Note (C#): gimmicks are units too, so they are in both packets.
                let gimmick_ids: Vec<u32> = others
                    .iter()
                    .filter(|o| o.kind() == ObjectKind::Gimmick)
                    .map(|o| o.obj_id())
                    .collect();
                for chunk in gimmick_ids.chunks(SCGimmicksRemovedPacket::MAX_COUNT_PER_PACKET) {
                    me.send(SCGimmicksRemovedPacket {
                        ids: chunk.to_vec(),
                    });
                }
                let reset = {
                    let mut c = me.data();
                    let t = c.unit.current_target;
                    if t != 0 && unit_ids.contains(&t) {
                        c.unit.current_target = 0;
                        true
                    } else {
                        false
                    }
                };
                if reset {
                    me.send(SCTargetChangedPacket {
                        obj_id: me.obj_id,
                        target_obj_id: 0,
                    });
                }
                for other in others {
                    if let Some(p) = other.as_player() {
                        visibility::remove_visible_object(self, obj, p);
                    }
                }
            }
            _ => {
                // Only players outside the object's (new) neighbourhood lose it.
                let obj_region = obj.is_visible().then(|| self.region_of(obj)).flatten();
                let obj_neighbors = obj_region.map(|r| self.neighbors(r)).unwrap_or_default();
                for other in objects.iter().filter(|o| o.obj_id() != id) {
                    let Some(p) = other.as_player() else {
                        continue;
                    };
                    if obj_region.is_some() {
                        if let Some(cr) = self.region_of(other) {
                            if Some(cr) == obj_region || obj_neighbors.contains(&cr) {
                                continue;
                            }
                        }
                    }
                    visibility::remove_visible_object(self, obj, p);
                }
            }
        }
    }

    /// `WorldManager.AddVisibleObject(obj)`: place the object in its region (first spawn) or move
    /// it between regions, updating who sees it; children follow. Returns the zone changes
    /// caused by `Region.AddObject`.
    pub fn add_visible_object(
        &self,
        obj: impl Into<WorldObject>,
        ctx: &ViewCtx<'_>,
    ) -> Vec<ZoneChange> {
        let obj = obj.into();
        let mut out = Vec::new();
        let mut grid = self.grid.lock().expect("grid poisoned");
        self.add_visible_locked(&mut grid, &obj, ctx, &mut out, 0);
        out
    }

    fn add_visible_locked(
        &self,
        grid: &mut Grid,
        obj: &WorldObject,
        ctx: &ViewCtx<'_>,
        out: &mut Vec<ZoneChange>,
        depth: usize,
    ) {
        if depth > MAX_PARENT_DEPTH {
            return;
        }
        let Some(region) = self.region_of(obj) else {
            return;
        };
        let current = obj
            .region()
            .filter(|(inst, _, _)| *inst == self.id)
            .map(|(_, x, y)| (x, y));
        if current == Some(region) {
            return;
        }
        match current {
            None => {
                self.region_add_object(grid, region, obj, out);
                obj.core().set_region(Some((self.id, region.0, region.1)));
                for n in self.neighbors(region) {
                    self.add_to_characters(grid, n, obj, ctx);
                }
            }
            Some(cur) => {
                let old_n = self.neighbors(cur);
                let new_n = self.neighbors(region);
                for d in old_n.iter().filter(|r| !new_n.contains(r)) {
                    self.remove_from_characters(grid, *d, obj);
                }
                for d in new_n.iter().filter(|r| !old_n.contains(r)) {
                    if obj.is_visible() {
                        self.add_to_characters(grid, *d, obj, ctx);
                    }
                }
                self.region_add_object(grid, region, obj, out);
                obj.core().set_region(Some((self.id, region.0, region.1)));
                Self::region_remove_object(grid, cur, obj.obj_id());
                for child in obj.core().children() {
                    self.add_visible_locked(grid, &child, ctx, out, depth + 1);
                }
            }
        }
        // "Ensure children are added on initial spawn" (runs after a move too, as in C#).
        for child in obj.core().children() {
            self.add_visible_locked(grid, &child, ctx, out, depth + 1);
        }
    }

    /// `Region.AddObject`: appends the object and updates `Transform.InstanceId` / `ZoneId`.
    fn region_add_object(
        &self,
        grid: &mut Grid,
        region: (i32, i32),
        obj: &WorldObject,
        out: &mut Vec<ZoneChange>,
    ) {
        grid.regions.entry(region).or_default().push(obj.clone());
        let world = self.world_pos_rot(obj);
        let zone = self.template.zone_id_at(world.x, world.y);
        if zone == 0 {
            let local = obj.transform();
            tracing::debug!(
                obj_id = obj.obj_id(),
                kind = ?obj.kind(),
                parent_obj_id = local.parent_obj_id,
                local_x = local.x,
                local_y = local.y,
                local_z = local.z,
                world_x = world.x,
                world_y = world.y,
                world_z = world.z,
                "Object position has no zone"
            );
        }
        let id = self.id;
        let changed = obj.with_transform(|t| {
            t.instance_id = id;
            if zone > 0 && zone != t.zone_id {
                let old = t.zone_id;
                t.zone_id = zone;
                Some(old)
            } else {
                None
            }
        });
        if let Some(old_zone) = changed {
            out.push(ZoneChange {
                object: obj.clone(),
                old_zone,
                new_zone: zone,
            });
        }
    }

    /// `Region.RemoveObject` (swap with the last entry, like the C# array).
    fn region_remove_object(grid: &mut Grid, region: (i32, i32), obj_id: u32) {
        if let Some(list) = grid.regions.get_mut(&region) {
            if let Some(i) = list.iter().position(|o| o.obj_id() == obj_id) {
                list.swap_remove(i);
            }
            if list.is_empty() {
                grid.regions.remove(&region);
            }
        }
    }

    /// `WorldManager.RemoveVisibleObject(obj)` (children follow). Note (C#): the object's own
    /// region is notified twice — once directly and once as part of `GetNeighbors()`, which
    /// includes the region itself — so players in the same sector receive the removal packets
    /// twice. Mirrored.
    pub fn remove_visible_object(&self, obj: impl Into<WorldObject>) {
        let obj = obj.into();
        let mut grid = self.grid.lock().expect("grid poisoned");
        self.remove_visible_locked(&mut grid, &obj, 0);
    }

    fn remove_visible_locked(&self, grid: &mut Grid, obj: &WorldObject, depth: usize) {
        if depth > MAX_PARENT_DEPTH {
            return;
        }
        let Some((inst, rx, ry)) = obj.region() else {
            return;
        };
        if inst != self.id {
            return;
        }
        let region = (rx, ry);
        Self::region_remove_object(grid, region, obj.obj_id());
        self.remove_from_characters(grid, region, obj);
        for n in self.neighbors(region) {
            self.remove_from_characters(grid, n, obj);
        }
        obj.core().set_region(None);
        for child in obj.core().children() {
            self.remove_visible_locked(grid, &child, depth + 1);
        }
    }

    /// The old-instance half of the C# region change to a region of another instance
    /// (`currentRegion.FindDifferenceBetweenRegions(region)` is every old neighbour then):
    /// every player around stops seeing the object (and a player stops seeing them), the object
    /// leaves its region; children follow.
    pub fn transfer_out(&self, obj: &WorldObject) {
        let mut grid = self.grid.lock().expect("grid poisoned");
        self.transfer_out_locked(&mut grid, obj, 0);
    }

    fn transfer_out_locked(&self, grid: &mut Grid, obj: &WorldObject, depth: usize) {
        if depth > MAX_PARENT_DEPTH {
            return;
        }
        let Some((inst, rx, ry)) = obj.region() else {
            return;
        };
        if inst != self.id {
            return;
        }
        for n in self.neighbors((rx, ry)) {
            self.remove_from_characters(grid, n, obj);
        }
        Self::region_remove_object(grid, (rx, ry), obj.obj_id());
        obj.core().set_region(None);
        for child in obj.core().children() {
            self.transfer_out_locked(grid, &child, depth + 1);
        }
    }

    /// `GameObject.Show()` (`IsVisible = true; AddVisibleObject`).
    pub fn show(&self, obj: impl Into<WorldObject>, ctx: &ViewCtx<'_>) -> Vec<ZoneChange> {
        let obj = obj.into();
        obj.core().set_visible(true);
        self.add_visible_object(obj, ctx)
    }

    /// `GameObject.Hide()` (`IsVisible = false; RemoveVisibleObject`).
    pub fn hide(&self, obj: impl Into<WorldObject>) {
        let obj = obj.into();
        obj.core().set_visible(false);
        self.remove_visible_object(obj);
    }

    /// `GameObject.Spawn()` (`ParentWorld.AddObject(this); Show()`).
    pub fn spawn(&self, obj: impl Into<WorldObject>, ctx: &ViewCtx<'_>) -> Vec<ZoneChange> {
        let obj = obj.into();
        self.add_object(&obj);
        self.show(obj, ctx)
    }

    /// `GameObject.Delete()` (`Hide(); Transform.DetachAll(); ParentWorld.RemoveObject(this)`).
    /// Kind-specific cleanup (spawners, DB rows, AI) is the owning subsystem's job.
    pub fn delete(&self, obj: impl Into<WorldObject>) {
        let obj = obj.into();
        self.hide(&obj);
        self.detach_child(&obj);
        for child in obj.core().take_children() {
            child.with_transform(|t| t.parent_obj_id = 0);
        }
        self.remove_object(obj.obj_id());
    }

    // ----- neighbourhood queries (`GetAround<T>`) -----

    /// `WorldManager.GetAround<GameObject>(obj)`: the objects in the neighbouring regions (own
    /// region included), excluding `obj` itself.
    pub fn objects_around(&self, obj: &WorldObject) -> Vec<WorldObject> {
        self.around(obj, |o| Some(o.clone()))
    }

    /// `GetAround<T>(obj)` with a kind filter, e.g. `world.around(&obj, |o| o.as_npc().cloned())`.
    pub fn around<T>(
        &self,
        obj: &WorldObject,
        mut f: impl FnMut(&WorldObject) -> Option<T>,
    ) -> Vec<T> {
        let grid = self.grid.lock().expect("grid poisoned");
        let Some((inst, rx, ry)) = obj.region() else {
            return Vec::new();
        };
        if inst != self.id {
            return Vec::new();
        }
        let id = obj.obj_id();
        let mut out = Vec::new();
        for n in self.neighbors((rx, ry)) {
            for o in grid.objects(n) {
                if o.obj_id() != id {
                    if let Some(v) = f(o) {
                        out.push(v);
                    }
                }
            }
        }
        out
    }

    /// `WorldManager.GetAround<Character>(obj)`.
    pub fn players_around(&self, obj: impl Into<WorldObject>) -> Vec<PlayerRef> {
        self.around(&obj.into(), |o| o.as_player().cloned())
    }

    /// The objects of one sector, in list order.
    pub fn objects_in_region(&self, region: (i32, i32)) -> Vec<WorldObject> {
        let grid = self.grid.lock().expect("grid poisoned");
        grid.objects(region).to_vec()
    }

    /// `GetInCell<GameObject>(x, y)`: the objects of every sector of a cell, sector by sector.
    pub fn objects_in_cell(&self, x: i32, y: i32) -> Vec<WorldObject> {
        let grid = self.grid.lock().expect("grid poisoned");
        let mut out = Vec::new();
        for a in x * SECTORS_PER_CELL..(x + 1) * SECTORS_PER_CELL {
            for b in y * SECTORS_PER_CELL..(y + 1) * SECTORS_PER_CELL {
                if self.template.valid_region(a, b) {
                    out.extend(grid.objects((a, b)).iter().cloned());
                }
            }
        }
        out
    }

    /// The obj ids of one sector, in list order (tests / debugging).
    pub fn region_object_ids(&self, region: (i32, i32)) -> Vec<u32> {
        let grid = self.grid.lock().expect("grid poisoned");
        grid.objects(region)
            .iter()
            .map(WorldObject::obj_id)
            .collect()
    }
}

/// `WorldManager` (player / instance bookkeeping).
#[derive(Debug, Default)]
pub struct WorldManager {
    worlds: DashMap<u32, Arc<WorldInstance>>,
    /// `_characters` (server-wide players by obj id).
    characters: DashMap<u32, PlayerRef>,
    /// `Character.UsedCharacterObjIds` (character id -> obj id, kept across sessions).
    used_character_obj_ids: Mutex<HashMap<u32, u32>>,
    /// `IsSnowing`.
    pub is_snowing: AtomicBool,
    /// `ZoneManager._conflicts` runtime state (`ZoneConflict.CurrentZoneState`, timers).
    pub zone_conflicts: zone_conflicts::ZoneConflicts,
}

impl WorldManager {
    /// `CreateWorldInstance(template, channel, overrideInstanceId: true, fixedInstanceId)`.
    pub fn create_world_instance(
        &self,
        template: Arc<WorldTemplate>,
        id: u32,
    ) -> Arc<WorldInstance> {
        if let Some(w) = self.worlds.get(&id) {
            return w.clone();
        }
        let w = Arc::new(WorldInstance::new(id, template));
        self.worlds.insert(id, w.clone());
        w
    }

    /// Whether an instance with this id exists (`_worlds.ContainsKey`).
    pub fn has_world(&self, id: u32) -> bool {
        self.worlds.contains_key(&id)
    }

    /// `RemoveWorld(worldInstanceId)` (`false` = no such instance).
    pub fn remove_world(&self, id: u32) -> bool {
        self.worlds.remove(&id).is_some()
    }

    /// The cross-instance part of `AddVisibleObject`: an object whose transform names another
    /// instance than the one its region belongs to (a player moved by `SCLoadInstancePacket`)
    /// leaves the old instance's region grid first (see [`WorldInstance::transfer_out`]).
    pub fn leave_other_instance(&self, obj: &WorldObject, new_instance: u32) {
        let Some((old, _, _)) = obj.region() else {
            return;
        };
        if old == new_instance {
            return;
        }
        match self.worlds.get(&old).map(|w| w.clone()) {
            Some(w) => w.transfer_out(obj),
            // The old instance is gone (destroyed dungeon): only the region link remains.
            None => obj.core().set_region(None),
        }
    }

    /// `GetWorld(instanceId)`.
    pub fn world(&self, id: u32) -> Option<Arc<WorldInstance>> {
        let w = self.worlds.get(&id).map(|w| w.clone());
        if w.is_none() {
            tracing::error!("GetWorld: No such World Instance {id}");
        }
        w
    }

    pub fn worlds(&self) -> Vec<Arc<WorldInstance>> {
        self.worlds.iter().map(|w| w.clone()).collect()
    }

    /// The instance an object is registered in (`obj.ParentWorld`, by its region or transform).
    pub fn world_of(&self, obj: &WorldObject) -> Option<Arc<WorldInstance>> {
        let inst = obj
            .region()
            .map(|(i, _, _)| i)
            .unwrap_or_else(|| obj.transform().instance_id);
        self.worlds.get(&inst).map(|w| w.clone())
    }

    /// `GetGameObject(objId)` across all instances.
    pub fn object(&self, obj_id: u32) -> Option<WorldObject> {
        self.worlds.iter().find_map(|w| w.object(obj_id))
    }

    /// `Character.UsedCharacterObjIds.TryGetValue`.
    pub fn used_obj_id(&self, character_id: u32) -> Option<u32> {
        self.used_character_obj_ids
            .lock()
            .expect("obj ids poisoned")
            .get(&character_id)
            .copied()
    }

    /// `Character.UsedCharacterObjIds.TryAdd`.
    pub fn remember_obj_id(&self, character_id: u32, obj_id: u32) {
        self.used_character_obj_ids
            .lock()
            .expect("obj ids poisoned")
            .entry(character_id)
            .or_insert(obj_id);
    }

    /// `TryAddCharacter`.
    pub fn try_add_character(&self, p: &PlayerRef) -> bool {
        match self.characters.entry(p.obj_id) {
            dashmap::mapref::entry::Entry::Occupied(_) => false,
            dashmap::mapref::entry::Entry::Vacant(v) => {
                v.insert(p.clone());
                true
            }
        }
    }

    /// `TryRemoveCharacter`.
    pub fn try_remove_character(&self, obj_id: u32) -> bool {
        self.characters.remove(&obj_id).is_some()
    }

    /// `GetCharacterByObjId`.
    pub fn character_by_obj_id(&self, obj_id: u32) -> Option<PlayerRef> {
        self.characters.get(&obj_id).map(|p| p.clone())
    }

    /// `GetCharacterById`.
    pub fn character_by_id(&self, id: u32) -> Option<PlayerRef> {
        self.characters
            .iter()
            .find(|p| p.character_id == id)
            .map(|p| p.clone())
    }

    /// `GetCharacter(name)` (case-insensitive).
    pub fn character_by_name(&self, name: &str) -> Option<PlayerRef> {
        let name = name.to_lowercase();
        self.characters
            .iter()
            .find(|p| p.data().unit.name.to_lowercase() == name)
            .map(|p| p.clone())
    }

    /// The active character of a game connection.
    pub fn character_by_connection(&self, connection_id: u32) -> Option<PlayerRef> {
        self.characters
            .iter()
            .find(|p| p.conn.id == connection_id)
            .map(|p| p.clone())
    }

    /// `GetAllCharacters`.
    pub fn all_characters(&self) -> Vec<PlayerRef> {
        self.characters.iter().map(|p| p.clone()).collect()
    }

    /// `GetWorldsByTemplate(templateId)`.
    pub fn worlds_by_template(&self, template_id: u32) -> Vec<Arc<WorldInstance>> {
        self.worlds
            .iter()
            .filter(|w| w.template.id == template_id)
            .map(|w| w.clone())
            .collect()
    }

    /// `GetAllNpcsFromWorld(worldInstanceId)`: empty for an unknown instance.
    pub fn all_npcs_from_world(&self, world_instance_id: u32) -> Vec<NpcRef> {
        self.worlds
            .get(&world_instance_id)
            .map_or_else(Vec::new, |w| w.all_npcs())
    }

    /// `GetAllDoodadsFromWorld(worldId)`: empty for an unknown instance.
    pub fn all_doodads_from_world(&self, world_id: u32) -> Vec<DoodadRef> {
        self.worlds
            .get(&world_id)
            .map_or_else(Vec::new, |w| w.all_doodads())
    }

    /// `GetAllSlavesFromWorld(worldId)`: empty for an unknown instance.
    pub fn all_slaves_from_world(&self, world_id: u32) -> Vec<SlaveRef> {
        self.worlds
            .get(&world_id)
            .map_or_else(Vec::new, |w| w.all_slaves())
    }

    /// `GetTargetOrSelf(character, targetName, out firstNonNameArgument)`: the online character
    /// named `target_name` (and 1), else the character's current target when it is a player,
    /// else `character` itself (both with 0). `None` is the C# `null` name.
    pub fn target_or_self(
        &self,
        character: &PlayerRef,
        target_name: Option<&str>,
    ) -> (PlayerRef, i32) {
        if let Some(name) = target_name.filter(|n| !n.trim().is_empty()) {
            if let Some(player) = self.character_by_name(name) {
                return (player, 1);
            }
        }
        // `character.CurrentTarget is Character targetCharacter` (lock dropped before lookup).
        let target = character.data().unit.current_target;
        if target != 0 {
            if let Some(t) = self.character_by_obj_id(target) {
                return (t, 0);
            }
        }
        (character.clone(), 0)
    }

    /// `BroadcastPacketToServer`.
    pub fn broadcast_to_server(&self, packet: &dyn ServerPacket) {
        let mut out = crate::net::connection::Fanout::new(packet);
        for p in self.all_characters() {
            out.send(&p.conn);
        }
    }
}

/// `GameObject.BroadcastPacket(packet, self)`: every player around `obj`, plus `obj` itself
/// when `include_self` and it is a player (`Doodad.BroadcastPacket` never includes self).
pub fn broadcast(
    world: &WorldInstance,
    obj: impl Into<WorldObject>,
    packet: &dyn ServerPacket,
    include_self: bool,
) {
    let obj = obj.into();
    // Encoded once for every viewer.
    let mut out = crate::net::connection::Fanout::new(packet);
    for p in world.players_around(&obj) {
        out.send(&p.conn);
    }
    if include_self {
        if let Some(p) = obj.as_player() {
            out.send(&p.conn);
        }
    }
}
