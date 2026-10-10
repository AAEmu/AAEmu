//! `AreaTriggerManager` (`Core/Managers/World/AreaTriggerManager.cs`), `AreaTrigger`,
//! `AreaShape` / `AreaShapeType` (`Models/Game/World/AreaTrigger.cs`, `AreaShape*.cs`),
//! `WorldManager.GetAroundByShape`, and the `#region LoadServerDB` part of
//! `WorldManager.Load` (`aoe_shapes` → `GetAreaShapeById`, `wi_group_wis` →
//! `GetWorldInteractionGroup`).
//!
//! Area triggers are attached to doodads (`DoodadFuncClout`): every 200 ms the units inside the
//! shape around the owner doodad are compared with the previous list; entering units get the
//! trigger's buff (`InsideBuffTemplate.Apply`, through the skills hook) and leaving ones lose it
//! when their trigger count drops to 0; every `TickRate` ms the trigger's effects are applied to
//! the units inside (skills hook).
//!
//! `GetAround(…, useModelSize: true)` widens the radius by the units' `ModelSize` (actor model
//! radius × scale; doodad owners have none).

use std::collections::HashMap;
use std::sync::{Arc, Mutex, MutexGuard, Weak};
use std::time::{Duration, Instant};

use tracing::{debug, error};

use crate::data::sqlite::for_each_row;
use crate::data::world_templates::REGION_SIZE;
use crate::net::registry::PacketRegistry;
use crate::server::GameServer;
use crate::systems::DataLoadCtx;
use crate::world::{DoodadRef, WorldInstance, WorldObject};

/// `SkillTargetRelation`.
pub mod skill_target_relation {
    pub const ANY: u8 = 0;
    pub const FRIENDLY: u8 = 1;
    pub const PARTY: u8 = 2;
    pub const RAID: u8 = 3;
    pub const HOSTILE: u8 = 4;
    pub const OTHERS: u8 = 5;
}

/// `WorldInteractionGroup` (`wi_groups`).
pub mod world_interaction_group {
    pub const CRAFT: u32 = 1;
    pub const COLLECT: u32 = 2;
    pub const BUILDING: u32 = 3;
}

/// `AreaShapeType.Sphere`.
pub const AREA_SHAPE_SPHERE: u32 = 1;
/// `AreaShapeType.Cuboid`.
pub const AREA_SHAPE_CUBOID: u32 = 2;

/// `AreaTriggerManager.Initialize`: tick period.
pub const TICK_DELAY: Duration = Duration::from_millis(200);

/// `AreaShape` (`aoe_shapes` row).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct AreaShape {
    pub id: u32,
    /// `Type` (`AreaShapeType`; any `kind_id` is cast without validation).
    pub kind: u32,
    pub value1: f32,
    pub value2: f32,
    pub value3: f32,
}

/// `MathUtil.GetCuboidVertices(length, width, x, y, rotationZ)` (float).
pub fn get_cuboid_vertices(
    length: f32,
    width: f32,
    x: f32,
    y: f32,
    rotation_z: f32,
) -> [(f32, f32); 4] {
    let rad_front = rotation_z - std::f32::consts::PI / 2.0;
    let rad_right = rotation_z;
    let (cos_front, sin_front) = (rad_front.cos(), rad_front.sin());
    let (cos_right, sin_right) = (rad_right.cos(), rad_right.sin());
    let p1 = (width * cos_front + x, width * sin_front + y);
    let p1 = (length * cos_right + p1.0, length * sin_right + p1.1);
    let p2 = (width * cos_front + x, width * sin_front + y);
    let p2 = (-length * cos_right + p2.0, -length * sin_right + p2.1);
    let p3 = (-width * cos_front + x, -width * sin_front + y);
    let p3 = (-length * cos_right + p3.0, -length * sin_right + p3.1);
    let p4 = (-width * cos_front + x, -width * sin_front + y);
    let p4 = (length * cos_right + p4.0, length * sin_right + p4.1);
    [p1, p2, p3, p4]
}

fn sign(p1: (f32, f32), p2: (f32, f32), p3: (f32, f32)) -> f32 {
    (p1.0 - p3.0) * (p2.1 - p3.1) - (p2.0 - p3.0) * (p1.1 - p3.1)
}

/// `MathUtil.PointInTriangle`.
pub fn point_in_triangle(
    point: (f32, f32),
    v1: (f32, f32),
    v2: (f32, f32),
    v3: (f32, f32),
) -> bool {
    let b1 = sign(point, v1, v2) < 0.0;
    let b2 = sign(point, v2, v3) < 0.0;
    let b3 = sign(point, v3, v1) < 0.0;
    b1 == b2 && b2 == b3
}

impl AreaShape {
    /// `AreaShape.ComputeCuboid(origin, toCheck)` with the origin's world position / yaw and
    /// each candidate's world position. Note (C#): the rectangle is tested as the triangles
    /// (v0,v1,v2) and (v1,v2,v3), which overlap instead of splitting it on a diagonal, so a
    /// corner region next to v3 / v0 is never inside (mirrored).
    pub fn compute_cuboid<T: Clone>(
        &self,
        origin: (f32, f32, f32),
        origin_rot_z: f32,
        to_check: &[(T, [f32; 3])],
    ) -> Vec<(T, [f32; 3])> {
        let z_offset = self.value3;
        let list: Vec<_> = to_check
            .iter()
            .filter(|(_, p)| p[2] >= origin.2 - z_offset && p[2] <= origin.2 + z_offset)
            .cloned()
            .collect();
        if list.is_empty() {
            return list;
        }
        let v = get_cuboid_vertices(self.value1, self.value2, origin.0, origin.1, origin_rot_z);
        list.into_iter()
            .filter(|(_, p)| {
                let tri1 = point_in_triangle((p[0], p[1]), v[0], v[1], v[2]);
                let tri2 = point_in_triangle((p[0], p[1]), v[1], v[2], v[3]);
                tri1 || tri2
            })
            .collect()
    }
}

/// `aoe_shapes` + `wi_group_wis` (`server.game_data.systems.npcs.area_trigger`).
#[derive(Debug, Default)]
pub struct AreaTriggerData {
    /// `_areaShapes`.
    pub shapes: HashMap<u32, AreaShape>,
    /// `_worldInteractionGroups` (wi id → `WorldInteractionGroup`).
    pub world_interaction_groups: HashMap<u32, u32>,
}

impl AreaTriggerData {
    /// `WorldManager.Load` `#region LoadServerDB`.
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        let mut d = Self::default();
        let mut duplicate = None;
        for_each_row(ctx.conn, "SELECT * FROM wi_group_wis", [], |r| {
            let id = r.u32("wi_id")?;
            let group = r.u32("wi_group_id")?;
            // `_worldInteractionGroups.Add` throws on a duplicate key.
            if d.world_interaction_groups.insert(id, group).is_some() {
                duplicate.get_or_insert(id);
            }
            Ok(())
        })?;
        if let Some(id) = duplicate {
            return Err(crate::data::GameDataError::File(format!(
                "wi_group_wis: duplicate wi_id {id}"
            )));
        }
        for_each_row(ctx.conn, "SELECT * FROM aoe_shapes", [], |r| {
            let shape = AreaShape {
                id: r.u32("id")?,
                kind: r.u32("kind_id")?,
                value1: r.f32("value1")?,
                value2: r.f32("value2")?,
                value3: r.f32("value3")?,
            };
            d.shapes.entry(shape.id).or_insert(shape); // TryAdd
            Ok(())
        })?;
        Ok(d)
    }

    /// `WorldManager.GetAreaShapeById`.
    pub fn get_area_shape_by_id(&self, id: u32) -> Option<AreaShape> {
        self.shapes.get(&id).copied()
    }

    /// `WorldManager.GetWorldInteractionGroup(worldInteractionType)`.
    pub fn get_world_interaction_group(&self, world_interaction_type: u32) -> Option<u32> {
        self.world_interaction_groups
            .get(&world_interaction_type)
            .copied()
    }
}

// ---------------------------------------------------------------------------------------------
// GetAround / GetAroundByShape
// ---------------------------------------------------------------------------------------------

/// `RadiusFitsCurrentRegion` (float remainders, as C#).
fn radius_fits_current_region(x: f32, y: f32, radius: f32) -> bool {
    let region = REGION_SIZE as f32;
    let x_mod = x % region;
    if x_mod - radius < 0.0 || x_mod + radius > region {
        return false;
    }
    let y_mod = y % region;
    !(y_mod - radius < 0.0 || y_mod + radius > region)
}

/// `WorldManager.GetAround<Unit>(obj, radius, useModelSize)` with each unit's world position.
pub fn get_around_units(
    server: &GameServer,
    world: &WorldInstance,
    obj: &WorldObject,
    radius: f32,
    use_model_size: bool,
) -> Vec<(WorldObject, [f32; 3])> {
    use crate::systems::skills::geometry::{model_size, Target};
    let mut result = Vec::new();
    if radius <= 0.0 {
        return result;
    }
    let mut radius = radius;
    if use_model_size {
        radius += model_size(server, &Target::Obj(obj.clone()));
    }
    let Some((inst, rx, ry)) = obj.region() else {
        return result;
    };
    if inst != world.id {
        return result;
    }
    let origin = world.world_pos_rot(obj);
    let sqrad = radius * radius;
    let regions = if radius > 0.0 && radius_fits_current_region(origin.x, origin.y, radius) {
        vec![(rx, ry)]
    } else {
        world.neighbors((rx, ry))
    };
    let id = obj.obj_id();
    for r in regions {
        for o in world.objects_in_region(r) {
            if !o.is_unit() || o.obj_id() == id {
                continue;
            }
            let p = world.world_pos_rot(&o);
            let mut final_rad = sqrad;
            if use_model_size {
                let ms = model_size(server, &Target::Obj(o.clone()));
                final_rad += ms * ms;
            }
            let dx = (p.x - origin.x) * (p.x - origin.x);
            if dx > final_rad {
                continue;
            }
            let dy = (p.y - origin.y) * (p.y - origin.y);
            if dx + dy < final_rad {
                result.push((o, [p.x, p.y, p.z]));
            }
        }
    }
    result
}

/// `WorldManager.GetAroundByShape<Unit>(obj, shape)`: `None` for an unknown shape type.
pub fn get_around_by_shape(
    server: &GameServer,
    world: &WorldInstance,
    obj: &WorldObject,
    shape: &AreaShape,
) -> Option<Vec<WorldObject>> {
    match shape.kind {
        AREA_SHAPE_SPHERE => {
            let radius = if shape.value1 > 0.0 {
                shape.value1
            } else {
                40.0
            };
            Some(
                get_around_units(server, world, obj, radius, true)
                    .into_iter()
                    .map(|(o, _)| o)
                    .collect(),
            )
        }
        AREA_SHAPE_CUBOID => {
            let diagonal =
                f64::from(shape.value1 * shape.value1 + shape.value2 * shape.value2).sqrt();
            let res = get_around_units(server, world, obj, diagonal as f32, true);
            let origin = world.world_pos_rot(obj);
            Some(
                shape
                    .compute_cuboid((origin.x, origin.y, origin.z), origin.rot_z, &res)
                    .into_iter()
                    .map(|(o, _)| o)
                    .collect(),
            )
        }
        _ => {
            error!("AreaShape had impossible type");
            None
        }
    }
}

/// `Region.HasPlayerActivity()` of `region`: a player in its neighbourhood.
pub fn region_has_player_activity(world: &WorldInstance, region: (i32, i32)) -> bool {
    world.neighbors(region).into_iter().any(|r| {
        world
            .objects_in_region(r)
            .iter()
            .any(|o| o.as_player().is_some())
    })
}

// ---------------------------------------------------------------------------------------------
// AreaTrigger
// ---------------------------------------------------------------------------------------------

#[derive(Debug, Default)]
struct AreaTriggerState {
    /// `Units` (inside the shape at the last check).
    units: Vec<WorldObject>,
    /// `_lastTick` (`None` = `DateTime.MinValue`).
    last_tick: Option<Instant>,
}

/// `AreaTrigger`.
#[derive(Debug, Default)]
pub struct AreaTrigger {
    pub shape: Option<AreaShape>,
    pub owner: Option<DoodadRef>,
    pub caster: Option<WorldObject>,
    pub skill_id: u32,
    pub tl_id: u32,
    /// `SkillTargetRelation`.
    pub target_relation: u8,
    /// `InsideBuffTemplate?.BuffId` (`None` = no template).
    pub inside_buff_id: Option<u32>,
    /// `EffectsPerBuff` (buff id → effect template ids).
    pub effects_per_buff: HashMap<u32, Vec<u32>>,
    /// `TickRate` (ms).
    pub tick_rate: i32,
    state: Mutex<AreaTriggerState>,
}

impl AreaTrigger {
    fn state(&self) -> MutexGuard<'_, AreaTriggerState> {
        self.state.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// Obj ids of the units inside at the last check.
    pub fn unit_ids(&self) -> Vec<u32> {
        self.state().units.iter().map(WorldObject::obj_id).collect()
    }

    /// `OnEnter(unit)`.
    fn on_enter(&self, server: &GameServer, unit: &WorldObject) {
        let (Some(caster), Some(_), Some(buff_id)) =
            (&self.caster, &self.owner, self.inside_buff_id)
        else {
            return;
        };
        let valid = match server.systems.npcs.hooks.skills.get() {
            Some(h) => h.is_relation_valid(server, self.target_relation, caster, unit),
            None => default_is_relation_valid(self.target_relation, caster, unit),
        };
        if !valid {
            return;
        }
        let count = unit.with_unit(|u| {
            u.unit_mut().increment_trigger_count(buff_id);
            u.unit().trigger_count(buff_id)
        });
        if count == Some(1) {
            if let Some(h) = server.systems.npcs.hooks.skills.get() {
                h.apply_area_buff(server, caster, unit, buff_id);
            }
        }
    }

    /// `OnLeave(unit)`.
    fn on_leave(&self, server: &GameServer, unit: &WorldObject) {
        if let Some(buff_id) = self.inside_buff_id {
            leave_unit(server, unit, buff_id);
        }
    }

    /// `OnDelete()`.
    pub fn on_delete(&self, server: &GameServer) {
        if let Some(buff_id) = self.inside_buff_id {
            let units = self.state().units.clone();
            for unit in &units {
                leave_unit(server, unit, buff_id);
            }
        }
    }

    /// `UpdateUnits()`.
    fn update_units(self: &Arc<Self>, server: &GameServer) {
        let (Some(owner), Some(shape)) = (&self.owner, &self.shape) else {
            remove_area_trigger(server, self);
            return;
        };
        if !owner.is_visible() {
            remove_area_trigger(server, self);
            return;
        }
        let owner_obj = WorldObject::from(owner);
        let Some(world) = server.world.world_of(&owner_obj) else {
            return;
        };
        // Get units currently in the shape
        let Some(current) = get_around_by_shape(server, &world, &owner_obj, shape) else {
            return;
        };
        let old = self.state().units.clone();
        let left: Vec<_> = old
            .iter()
            .filter(|o| current.iter().all(|n| n.obj_id() != o.obj_id()))
            .cloned()
            .collect();
        let new: Vec<_> = current
            .iter()
            .filter(|n| old.iter().all(|o| o.obj_id() != n.obj_id()))
            .cloned()
            .collect();
        for unit in &new {
            self.on_enter(server, unit);
            debug!(
                "AreaShape Enter {} {} with count {} unit in shape arround {}",
                shape.id,
                shape.kind,
                current.len(),
                owner.obj_id
            );
        }
        for unit in &left {
            self.on_leave(server, unit);
            debug!(
                "AreaShape Leave {} {} with count {} unit in shape arround {}",
                shape.id,
                shape.kind,
                current.len(),
                owner.obj_id
            );
        }
        self.state().units = current;
    }

    /// `ApplyEffects()`.
    fn apply_effects(&self, server: &GameServer) {
        let (Some(buff_id), Some(caster)) = (self.inside_buff_id, &self.caster) else {
            return;
        };
        let Some(effects) = self.effects_per_buff.get(&buff_id) else {
            return;
        };
        let units = self.state().units.clone();
        let hook = server.systems.npcs.hooks.skills.get();
        let to_apply = match &hook {
            Some(h) => h.filter_with_relation(server, self.target_relation, caster, units),
            None => default_filter_with_relation(self.target_relation, caster, units),
        };
        if let Some(h) = hook {
            for unit in &to_apply {
                h.apply_area_effects(server, caster, unit, buff_id, self.skill_id, effects);
            }
        }
    }

    /// `Tick(delta)` (the manager calls it every 200 ms).
    pub fn tick(self: &Arc<Self>, server: &GameServer) {
        self.update_units(server);
        if self.tick_rate > 0 {
            let due = self
                .state()
                .last_tick
                .is_none_or(|t| t.elapsed().as_secs_f64() * 1000.0 > f64::from(self.tick_rate));
            if due {
                self.apply_effects(server);
                self.state().last_tick = Some(Instant::now());
            }
        }
    }
}

/// The trigger-count / buff removal shared by `OnLeave` and `OnDelete`.
fn leave_unit(server: &GameServer, unit: &WorldObject, buff_id: u32) {
    let count = unit.with_unit(|u| {
        u.unit_mut().decrement_trigger_count(buff_id);
        u.unit().trigger_count(buff_id)
    });
    if count == Some(0) {
        unit.with_base_unit(|b| b.buffs_mut().remove_buff(buff_id));
        crate::systems::dispatch_buff_events(server, unit);
    }
}

/// `SkillTargetingUtil.IsRelationValid` for the relations that need no faction / team logic
/// (the default of [`super::hooks::SkillsBridge::is_relation_valid`]): Any / Party / unknown →
/// true, Others → different object, Raid → the caster itself (no team), Friendly / Hostile →
/// false.
pub fn default_is_relation_valid(relation: u8, caster: &WorldObject, target: &WorldObject) -> bool {
    use skill_target_relation::*;
    match relation {
        FRIENDLY | HOSTILE => false,
        RAID => caster.obj_id() == target.obj_id(),
        OTHERS => caster.obj_id() != target.obj_id(),
        _ => true,
    }
}

/// `SkillTargetingUtil.FilterWithRelation` without faction / team / mate logic (the default of
/// [`super::hooks::SkillsBridge::filter_with_relation`]): Raid → `[caster]` (no team, no mate:
/// the C# appends the caster), Friendly / Hostile / Others → none.
pub fn default_filter_with_relation(
    relation: u8,
    caster: &WorldObject,
    units: Vec<WorldObject>,
) -> Vec<WorldObject> {
    use skill_target_relation::*;
    match relation {
        FRIENDLY | HOSTILE | OTHERS => Vec::new(),
        RAID => vec![caster.clone()],
        _ => units,
    }
}

// ---------------------------------------------------------------------------------------------
// AreaTriggerManager
// ---------------------------------------------------------------------------------------------

/// `AreaTriggerManager` (`server.systems.npcs.area_triggers`).
#[derive(Debug, Default)]
pub struct AreaTriggerManager {
    /// `_areaTriggers`.
    triggers: Mutex<Vec<Arc<AreaTrigger>>>,
    /// `_addQueue`.
    add_queue: Mutex<Vec<Arc<AreaTrigger>>>,
    /// `_removeQueue`.
    remove_queue: Mutex<Vec<Arc<AreaTrigger>>>,
    /// `Doodad.AttachAreaTriggers` (owner obj id → triggers).
    attached: Mutex<HashMap<u32, Vec<Arc<AreaTrigger>>>>,
}

fn lock<T>(m: &Mutex<T>) -> MutexGuard<'_, T> {
    m.lock().unwrap_or_else(|e| e.into_inner())
}

impl AreaTriggerManager {
    /// The active triggers (a snapshot).
    pub fn triggers(&self) -> Vec<Arc<AreaTrigger>> {
        lock(&self.triggers).clone()
    }

    /// `Doodad.AttachAreaTriggers` of a doodad.
    pub fn attached_to(&self, doodad_obj_id: u32) -> Vec<Arc<AreaTrigger>> {
        lock(&self.attached)
            .get(&doodad_obj_id)
            .cloned()
            .unwrap_or_default()
    }

    /// `Tick(delta)`.
    pub fn tick(&self, server: &GameServer) {
        {
            let mut q = lock(&self.add_queue);
            if !q.is_empty() {
                lock(&self.triggers).append(&mut q);
            }
        }
        for trigger in self.triggers() {
            let active = trigger.owner.as_ref().is_some_and(|o| {
                let obj = WorldObject::from(o);
                match (obj.region(), server.world.world_of(&obj)) {
                    (Some((_, rx, ry)), Some(w)) => region_has_player_activity(&w, (rx, ry)),
                    _ => false,
                }
            });
            if active {
                trigger.tick(server);
            }
        }
        let removed = std::mem::take(&mut *lock(&self.remove_queue));
        if !removed.is_empty() {
            let mut list = lock(&self.triggers);
            for r in &removed {
                // List.Remove: the first occurrence
                if let Some(i) = list.iter().position(|t| Arc::ptr_eq(t, r)) {
                    list.remove(i);
                }
            }
        }
    }
}

/// `AreaTriggerManager.AddAreaTrigger(trigger)` (also `Owner.AttachAreaTriggers.Add`).
pub fn add_area_trigger(server: &GameServer, trigger: Arc<AreaTrigger>) {
    let m = &server.systems.npcs.area_triggers;
    if let Some(owner) = &trigger.owner {
        lock(&m.attached)
            .entry(owner.obj_id)
            .or_default()
            .push(trigger.clone());
    }
    lock(&m.add_queue).push(trigger);
}

/// `AreaTriggerManager.RemoveAreaTrigger(trigger)`: `OnDelete` now, removal at the end of the
/// next tick. Note (C#): calling it twice runs `OnDelete` twice (trigger counts decremented
/// twice).
pub fn remove_area_trigger(server: &GameServer, trigger: &Arc<AreaTrigger>) {
    trigger.on_delete(server);
    lock(&server.systems.npcs.area_triggers.remove_queue).push(trigger.clone());
}

/// `Doodad` deletion (`Doodad.cs`): removes every trigger attached to the doodad and clears
/// its `AttachAreaTriggers`.
pub fn remove_doodad_area_triggers(server: &GameServer, doodad_obj_id: u32) {
    let triggers = lock(&server.systems.npcs.area_triggers.attached)
        .remove(&doodad_obj_id)
        .unwrap_or_default();
    for t in &triggers {
        remove_area_trigger(server, t);
    }
}

/// Startup: `AreaTriggerManager.Initialize` (200 ms tick).
pub async fn start(server: &Arc<GameServer>) -> anyhow::Result<()> {
    let weak: Weak<GameServer> = Arc::downgrade(server);
    tokio::spawn(async move {
        let mut interval = tokio::time::interval(TICK_DELAY);
        interval.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Delay);
        loop {
            interval.tick().await;
            let Some(server) = weak.upgrade() else {
                break;
            };
            server.systems.npcs.area_triggers.tick(&server);
        }
    });
    Ok(())
}

/// C->S packets (none).
pub fn register(_reg: &mut PacketRegistry) {}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::models::doodad::Doodad;
    use crate::models::npc::Npc;
    use crate::test_support::test_server;
    use crate::world::object::Entity;
    use crate::world::NpcRef;

    #[test]
    fn cuboid_vertices_and_triangles() {
        // 2 x 1 box (length along +X at yaw 0, width along -Y/+Y).
        let v = get_cuboid_vertices(2.0, 1.0, 0.0, 0.0, 0.0);
        let close =
            |a: (f32, f32), b: (f32, f32)| (a.0 - b.0).abs() < 1e-5 && (a.1 - b.1).abs() < 1e-5;
        assert!(close(v[0], (2.0, -1.0)));
        assert!(close(v[1], (-2.0, -1.0)));
        assert!(close(v[2], (-2.0, 1.0)));
        assert!(close(v[3], (2.0, 1.0)));
        let shape = AreaShape {
            id: 1,
            kind: AREA_SHAPE_CUBOID,
            value1: 2.0,
            value2: 1.0,
            value3: 5.0,
        };
        let pts = vec![
            (1, [1.5f32, 0.5, 0.0]),
            (2, [2.5, 0.0, 0.0]),
            (3, [0.0, 0.0, 6.0]),
            (4, [-1.9, -0.9, -4.0]),
            (5, [0.0, -0.5, 0.0]),
        ];
        let inside: Vec<i32> = shape
            .compute_cuboid((0.0, 0.0, 0.0), 0.0, &pts)
            .into_iter()
            .map(|(i, _)| i)
            .collect();
        // C# bug: the triangles (v0,v1,v2) and (v1,v2,v3) share the side v1-v2 instead of a
        // diagonal, so the corner around v3 / v0 (point 1) is not covered.
        assert_eq!(inside, vec![4, 5]);
    }

    fn spawn_npc(server: &GameServer, world: &WorldInstance, x: f32, y: f32) -> NpcRef {
        let npc = Entity::new(server.ids.object.get_next_id().unwrap(), Npc::new(1));
        {
            let mut n = npc.data();
            n.unit.transform.set_position(x, y, 0.0, 0.0, 0.0, 0.0);
            n.unit.transform.instance_id = world.id;
        }
        let _ = world.spawn(&npc, &server.view_ctx());
        npc
    }

    #[tokio::test]
    async fn trigger_enter_and_leave_counts() {
        let server = test_server();
        let world = server
            .world
            .worlds()
            .into_iter()
            .next()
            .expect("main world");
        let doodad = Entity::new(server.ids.object.get_next_id().unwrap(), Doodad::new(1));
        {
            let mut d = doodad.data();
            d.transform.set_position(100.0, 100.0, 0.0, 0.0, 0.0, 0.0);
            d.transform.instance_id = world.id;
        }
        let _ = world.spawn(&doodad, &server.view_ctx());
        let inside = spawn_npc(&server, &world, 105.0, 100.0);
        let outside = spawn_npc(&server, &world, 150.0, 100.0);
        let trigger = Arc::new(AreaTrigger {
            shape: Some(AreaShape {
                id: 1000,
                kind: AREA_SHAPE_SPHERE,
                value1: 10.0,
                value2: 0.0,
                value3: 0.0,
            }),
            owner: Some(doodad.clone()),
            caster: Some(WorldObject::from(&doodad)),
            inside_buff_id: Some(631),
            ..Default::default()
        });
        add_area_trigger(&server, trigger.clone());
        // No player around: the trigger does not tick.
        server.systems.npcs.area_triggers.tick(&server);
        assert!(trigger.unit_ids().is_empty());
        // Tick the trigger directly (player activity is checked by the manager).
        trigger.tick(&server);
        assert_eq!(trigger.unit_ids(), vec![inside.obj_id]);
        assert_eq!(inside.data().unit.trigger_count(631), 1);
        assert_eq!(outside.data().unit.trigger_count(631), 0);
        // Move out: leave decrements.
        inside.data().unit.transform.set_xyz(160.0, 100.0, 0.0);
        let _ = world.add_visible_object(&inside, &server.view_ctx());
        trigger.tick(&server);
        assert!(trigger.unit_ids().is_empty());
        assert_eq!(inside.data().unit.trigger_count(631), 0);
        // Back in, then the doodad is deleted: OnDelete decrements, removal on the next tick.
        inside.data().unit.transform.set_xyz(101.0, 100.0, 0.0);
        let _ = world.add_visible_object(&inside, &server.view_ctx());
        trigger.tick(&server);
        assert_eq!(inside.data().unit.trigger_count(631), 1);
        remove_doodad_area_triggers(&server, doodad.obj_id);
        assert_eq!(inside.data().unit.trigger_count(631), 0);
        server.systems.npcs.area_triggers.tick(&server);
        assert!(server.systems.npcs.area_triggers.triggers().is_empty());
        // Friendly relation without the skills hook: never valid.
        assert!(!default_is_relation_valid(
            skill_target_relation::FRIENDLY,
            &WorldObject::from(&doodad),
            &WorldObject::from(&inside)
        ));
    }

    #[test]
    fn region_fit() {
        assert!(radius_fits_current_region(32.0, 32.0, 10.0));
        assert!(!radius_fits_current_region(5.0, 32.0, 10.0));
        assert!(!radius_fits_current_region(32.0, 60.0, 10.0));
    }
}

#[cfg(test)]
mod real_data_tests {
    //! `AAEMU_COMPACT_DB=... cargo test -p aaemu-game real_area_trigger -- --ignored`.
    use rusqlite::{Connection, OpenFlags};

    use super::*;
    use crate::data::client_files::ClientFileManager;

    #[test]
    #[ignore = "needs AAEMU_COMPACT_DB"]
    fn real_area_trigger_tables() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let conn = Connection::open_with_flags(&path, OpenFlags::SQLITE_OPEN_READ_ONLY).unwrap();
        let files = ClientFileManager::new();
        let ctx = DataLoadCtx {
            conn: &conn,
            app_path: std::path::Path::new("."),
            files: &files,
            language: "en_us",
        };
        let d = AreaTriggerData::load(&ctx).unwrap();
        let rows = |t: &str| -> usize {
            conn.query_row(&format!("SELECT COUNT(*) FROM {t}"), [], |r| {
                r.get::<_, i64>(0)
            })
            .unwrap() as usize
        };
        assert_eq!(d.shapes.len(), rows("aoe_shapes"));
        assert_eq!(d.world_interaction_groups.len(), rows("wi_group_wis"));
        let s = d.get_area_shape_by_id(1000).unwrap();
        assert_eq!((s.kind, s.value1), (AREA_SHAPE_SPHERE, 40.0));
        assert_eq!(
            d.get_world_interaction_group(46),
            Some(world_interaction_group::CRAFT)
        );
        eprintln!(
            "aoe_shapes {} wi_group_wis {}",
            d.shapes.len(),
            d.world_interaction_groups.len()
        );
    }
}
