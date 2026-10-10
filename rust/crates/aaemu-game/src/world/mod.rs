//! The world: `WorldManager` (instances, the server-wide character list, visibility),
//! `WorldInstance` (object lists, region grid) and `Region` (per-sector object lists, neighbour
//! visibility), restricted to player characters — NPCs, doodads, gimmicks, slaves, mates and
//! transfers are later phases.
//!
//! Locking: each instance has one grid mutex that serialises its visibility updates; while it is
//! held, the players' `data` mutexes may be locked (one at a time) to build packets. Code that
//! holds a player's `data` lock must never call into the grid.

pub mod session;
pub mod unit_state;

use std::collections::HashMap;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex, MutexGuard};

use dashmap::DashMap;

use crate::data::world_templates::{WorldTemplate, REGION_SIZE};
use crate::data::GameData;
use crate::models::character::Character;
use crate::net::connection::ConnectionHandle;
use crate::net::packet::ServerPacket;
use crate::packets::world::{SCUnitPointsPacket, SCUnitsRemovedPacket};

/// `WorldManager.REGION_NEIGHBORHOOD_SIZE`.
const REGION_NEIGHBORHOOD_SIZE: i32 = 2;

/// A player character in the world (`Connection.ActiveChar`).
#[derive(Debug)]
pub struct Player {
    pub obj_id: u32,
    pub character_id: u32,
    /// The owning game connection (`Character.Connection`).
    pub conn: ConnectionHandle,
    pub data: Mutex<Character>,
    /// `GameObject.Region` (instance id, sector x, sector y).
    region: Mutex<Option<(u32, i32, i32)>>,
    /// `IsVisible`.
    visible: AtomicBool,
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
            region: Mutex::new(None),
            visible: AtomicBool::new(false),
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

    pub fn region(&self) -> Option<(u32, i32, i32)> {
        *self.region.lock().expect("region poisoned")
    }

    pub fn is_visible(&self) -> bool {
        self.visible.load(Ordering::Relaxed)
    }
}

/// A `GameObject` in a region. Only players exist in this phase.
#[derive(Debug, Clone)]
pub enum WorldObject {
    Player(PlayerRef),
}

impl WorldObject {
    pub fn obj_id(&self) -> u32 {
        match self {
            WorldObject::Player(p) => p.obj_id,
        }
    }

    pub fn as_player(&self) -> Option<&PlayerRef> {
        match self {
            WorldObject::Player(p) => Some(p),
        }
    }

    /// `obj is Unit`.
    pub fn is_unit(&self) -> bool {
        true
    }
}

/// `Character.AddVisibleObject(character)`: `target` learns about `obj`.
fn add_visible_object(obj: &WorldObject, target: &PlayerRef, data: &GameData, now_ms: i64) {
    match obj {
        WorldObject::Player(p) => {
            let (state, points) = {
                let mut c = p.data();
                let state =
                    (!Arc::ptr_eq(p, target)).then(|| unit_state::unit_state(&mut c, data, now_ms));
                (
                    state,
                    SCUnitPointsPacket {
                        obj_id: p.obj_id,
                        hp: c.hp,
                        mp: c.mp,
                    },
                )
            };
            if let Some(state) = state {
                target.send(state);
            }
            target.send(points);
        }
    }
}

/// `Character.RemoveVisibleObject(character)`: `target` forgets `obj`.
fn remove_visible_object(obj: &WorldObject, target: &PlayerRef) {
    match obj {
        WorldObject::Player(p) => {
            // base: CurrentTarget reset (targets are a later phase), children (none).
            if !Arc::ptr_eq(p, target) {
                target.send(SCUnitsRemovedPacket {
                    ids: vec![p.obj_id],
                });
            }
        }
    }
}

/// A zone change produced by `Region.AddObject` (`Transform.ZoneId` setter ->
/// `OnZoneChange(old, new)`).
#[derive(Debug, Clone)]
pub struct ZoneChange {
    pub player: PlayerRef,
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
    /// `_characters` (instance players by obj id).
    characters: DashMap<u32, PlayerRef>,
}

impl WorldInstance {
    pub fn new(id: u32, template: Arc<WorldTemplate>) -> Self {
        Self {
            id,
            template,
            grid: Mutex::new(Grid::default()),
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

    /// `WorldInstance.AddObject`.
    pub fn add_object(&self, p: &PlayerRef) {
        self.characters.insert(p.obj_id, p.clone());
    }

    /// `WorldInstance.RemoveObject`.
    pub fn remove_object(&self, obj_id: u32) -> bool {
        self.characters.remove(&obj_id).is_some()
    }

    pub fn player_count(&self) -> usize {
        self.characters.len()
    }

    /// `Region.AddToCharacters(obj)`.
    fn add_to_characters(
        &self,
        grid: &Grid,
        region: (i32, i32),
        obj: &WorldObject,
        data: &GameData,
        now_ms: i64,
    ) {
        let objects = grid.objects(region);
        if objects.is_empty() {
            return;
        }
        let WorldObject::Player(me) = obj;
        for go in objects.iter().filter(|o| o.obj_id() != me.obj_id) {
            add_visible_object(go, me, data, now_ms);
        }
        // Doodads / gimmicks of the region: later phase.
        for other in objects.iter().filter(|o| o.obj_id() != obj.obj_id()) {
            if let Some(p) = other.as_player() {
                add_visible_object(obj, p, data, now_ms);
            }
        }
    }

    /// `Region.RemoveFromCharacters(obj)`.
    fn remove_from_characters(&self, grid: &Grid, region: (i32, i32), obj: &WorldObject) {
        let objects = grid.objects(region);
        if objects.is_empty() {
            return;
        }
        match obj {
            WorldObject::Player(me) => {
                let unit_ids: Vec<u32> = objects
                    .iter()
                    .filter(|o| o.is_unit() && o.obj_id() != me.obj_id)
                    .map(WorldObject::obj_id)
                    .collect();
                for chunk in unit_ids.chunks(SCUnitsRemovedPacket::MAX_COUNT_PER_PACKET) {
                    me.send(SCUnitsRemovedPacket {
                        ids: chunk.to_vec(),
                    });
                }
                // Doodad / gimmick removal packets and the current-target reset: later phase.
                for other in objects.iter().filter(|o| o.obj_id() != me.obj_id) {
                    if let Some(p) = other.as_player() {
                        remove_visible_object(obj, p);
                    }
                }
            }
        }
    }

    /// `WorldManager.AddVisibleObject(obj)`: place the object in its region (first spawn) or move
    /// it between regions, updating who sees it. Returns the zone change caused by
    /// `Region.AddObject`, if any.
    pub fn add_visible_object(
        &self,
        player: &PlayerRef,
        data: &GameData,
        now_ms: i64,
    ) -> Option<ZoneChange> {
        let mut grid = self.grid.lock().expect("grid poisoned");
        let (x, y) = {
            let c = player.data();
            (c.transform.x, c.transform.y)
        };
        let region = self.region_by_pos(x, y)?;
        let current = player
            .region()
            .filter(|(inst, _, _)| *inst == self.id)
            .map(|(_, x, y)| (x, y));
        if current == Some(region) {
            return None;
        }
        let obj = WorldObject::Player(player.clone());
        let zone_change;
        match current {
            None => {
                zone_change = self.region_add_object(&mut grid, region, &obj);
                *player.region.lock().expect("region poisoned") =
                    Some((self.id, region.0, region.1));
                for n in self.neighbors(region) {
                    self.add_to_characters(&grid, n, &obj, data, now_ms);
                }
            }
            Some(cur) => {
                let old_n = self.neighbors(cur);
                let new_n = self.neighbors(region);
                for d in old_n.iter().filter(|r| !new_n.contains(r)) {
                    self.remove_from_characters(&grid, *d, &obj);
                }
                for d in new_n.iter().filter(|r| !old_n.contains(r)) {
                    if player.is_visible() {
                        self.add_to_characters(&grid, *d, &obj, data, now_ms);
                    }
                }
                zone_change = self.region_add_object(&mut grid, region, &obj);
                *player.region.lock().expect("region poisoned") =
                    Some((self.id, region.0, region.1));
                Self::region_remove_object(&mut grid, cur, obj.obj_id());
            }
        }
        zone_change
    }

    /// `Region.AddObject`: appends the object and updates `Transform.InstanceId` / `ZoneId`.
    fn region_add_object(
        &self,
        grid: &mut Grid,
        region: (i32, i32),
        obj: &WorldObject,
    ) -> Option<ZoneChange> {
        grid.regions.entry(region).or_default().push(obj.clone());
        let WorldObject::Player(p) = obj;
        let mut c = p.data();
        c.transform.instance_id = self.id;
        let zone = self.template.zone_id_at(c.transform.x, c.transform.y);
        if zone > 0 && zone != c.transform.zone_id {
            let old = c.transform.zone_id;
            c.transform.zone_id = zone;
            return Some(ZoneChange {
                player: p.clone(),
                old_zone: old,
                new_zone: zone,
            });
        }
        None
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

    /// `WorldManager.RemoveVisibleObject(obj)`. Note (C#): the object's own region is notified
    /// twice — once directly and once as part of `GetNeighbors()`, which includes the region
    /// itself — so players in the same sector receive `SCUnitsRemovedPacket` twice. Mirrored.
    pub fn remove_visible_object(&self, player: &PlayerRef) {
        let mut grid = self.grid.lock().expect("grid poisoned");
        let Some((inst, rx, ry)) = player.region() else {
            return;
        };
        if inst != self.id {
            return;
        }
        let region = (rx, ry);
        let obj = WorldObject::Player(player.clone());
        Self::region_remove_object(&mut grid, region, player.obj_id);
        self.remove_from_characters(&grid, region, &obj);
        for n in self.neighbors(region) {
            self.remove_from_characters(&grid, n, &obj);
        }
        *player.region.lock().expect("region poisoned") = None;
    }

    /// `GameObject.Show()` (`IsVisible = true; AddVisibleObject`).
    pub fn show(&self, player: &PlayerRef, data: &GameData, now_ms: i64) -> Option<ZoneChange> {
        player.visible.store(true, Ordering::Relaxed);
        self.add_visible_object(player, data, now_ms)
    }

    /// `GameObject.Hide()` (`IsVisible = false; RemoveVisibleObject`).
    pub fn hide(&self, player: &PlayerRef) {
        player.visible.store(false, Ordering::Relaxed);
        self.remove_visible_object(player);
    }

    /// `WorldManager.GetAround<Character>(obj)`: players in the neighbouring regions (own
    /// region included), excluding `obj` itself.
    pub fn players_around(&self, player: &PlayerRef) -> Vec<PlayerRef> {
        let grid = self.grid.lock().expect("grid poisoned");
        let Some((inst, rx, ry)) = player.region() else {
            return Vec::new();
        };
        if inst != self.id {
            return Vec::new();
        }
        let mut out = Vec::new();
        for n in self.neighbors((rx, ry)) {
            for o in grid.objects(n) {
                if o.obj_id() != player.obj_id {
                    if let Some(p) = o.as_player() {
                        out.push(p.clone());
                    }
                }
            }
        }
        out
    }

    /// The objects of one sector, in list order (tests / debugging).
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
            .find(|p| p.data().name.to_lowercase() == name)
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

    /// `BroadcastPacketToServer`.
    pub fn broadcast_to_server(&self, packet: &dyn ServerPacket) {
        for p in self.all_characters() {
            p.send_dyn(packet);
        }
    }
}

/// `GameObject.BroadcastPacket(packet, self)`.
pub fn broadcast(
    world: &WorldInstance,
    player: &PlayerRef,
    packet: &dyn ServerPacket,
    include_self: bool,
) {
    for p in world.players_around(player) {
        p.send_dyn(packet);
    }
    if include_self {
        player.send_dyn(packet);
    }
}
