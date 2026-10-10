//! The AI-facing members of `Models/Game/NPChar/Npc` and the `Unit` / `BaseUnit` helpers the
//! behaviors call: `MoveTowards`, `LookTowards`, `StopMovement`, `SetTarget`,
//! `CurrentAggroTarget`, `CurrentGameStance`, `BaseMoveSpeed`, `MoveSpeedMul`,
//! `NpcAi.GetRealMovementSpeed` / `GetRealMovementFlags`, `CanSeeTarget`, `UnitIsVisible`,
//! `CheckMovedPosition` and the buff gates of the movement code.
//!
//! Locking: every function copies what it needs out of an object's data lock and releases it
//! before calling into the world (broadcasts, visibility) or into another object.

use crate::models::movement::{
    GameStanceType, MoveKind, MoveType, MoveTypeActorFlags, MoveTypeFlags, UnitMove,
};
use crate::models::unit::UnitAttribute;
use crate::packets::world::SCOneUnitMovementPacket;
use crate::packets::world_objects::SCTargetChangedPacket;
use crate::server::GameServer;
use crate::systems::npcs::heightmap::ReferenceHeightAi;
use crate::systems::npcs::npc::data::NpcTemplate;
use crate::systems::npcs::npc::packets::SCAggroTargetChangedPacket;
use crate::systems::skills::controllers::{
    add_distance_to_front, add_distance_to_front_between, calculate_angle_from, calculate_distance,
    roll_pitch_yaw_sbytes_movement, skill_constants, ScState, Vec3,
};
use crate::systems::skills::data::BuffTemplate;
use crate::systems::skills::geometry::{self, Target};
use crate::systems::skills::{broadcast, buffs};
use crate::world::{now_ms, NpcRef, WorldObject};

/// `TagsEnum` values the AI checks.
pub mod tags {
    pub const STEALTH: u32 = 100;
    pub const RETURNING: u32 = 148;
    pub const NO_FIGHT: u32 = 345;
    pub const RIGHT: u32 = 1020;
    pub const LEFT: u32 = 1021;
    pub const DESCEND: u32 = 1022;
    pub const BACK: u32 = 1023;
    pub const FISH: u32 = 1025;
}

/// `BuffConstants.NpcReturn`.
pub const BUFF_NPC_RETURN: u32 = 550;

/// `Transform.World.Position` of an object.
pub fn world_pos(server: &GameServer, obj: &WorldObject) -> Vec3 {
    let p = geometry::world_pos(server, &Target::Obj(obj.clone()));
    Vec3::new(p.x, p.y, p.z)
}

/// `Transform.Local.Position`.
pub fn local_pos(obj: &WorldObject) -> Vec3 {
    let t = obj.transform();
    Vec3::new(t.x, t.y, t.z)
}

/// `MathUtil.CalculateDistance(GameObject, GameObject, includeZ)` (world positions).
pub fn distance_raw(server: &GameServer, a: &WorldObject, b: &WorldObject, include_z: bool) -> f32 {
    calculate_distance(world_pos(server, a), world_pos(server, b), include_z)
}

/// `BaseUnit.GetDistanceTo(target, includeZ)` (model radii subtracted).
pub fn distance_to(server: &GameServer, a: &WorldObject, b: &WorldObject, include_z: bool) -> f32 {
    geometry::distance_to(
        server,
        &Target::Obj(a.clone()),
        &Target::Obj(b.clone()),
        include_z,
    )
}

/// `Unit.ModelSize`.
pub fn model_size(server: &GameServer, obj: &WorldObject) -> f32 {
    geometry::model_size(server, &Target::Obj(obj.clone()))
}

/// `Npc.Template`.
pub fn template<'a>(server: &'a GameServer, npc: &NpcRef) -> Option<&'a NpcTemplate> {
    let id = npc.data().unit.template_id;
    server.game_data.systems.npcs.npc.template(id)
}

/// `Unit.MoveSpeedMul` (`CalculateWithBonuses(1000, MoveSpeedMul) / 1000`).
pub fn move_speed_mul(obj: &WorldObject) -> f32 {
    let now = now_ms();
    obj.with_unit(|u| {
        (u.unit()
            .calculate_with_bonuses(1000.0, UnitAttribute::MoveSpeedMul, now) as f32)
            / 1000.0
    })
    .unwrap_or(1.0)
}

/// `NpcAi.GetRealMovementSpeed(baseMoveSpeed)`.
pub fn real_movement_speed(obj: &WorldObject, base_move_speed: f64) -> f64 {
    let now = now_ms();
    let speed_mul = obj
        .with_unit(|u| {
            u.unit()
                .calculate_with_bonuses(0.0, UnitAttribute::MoveSpeedMul, now)
        })
        .unwrap_or(0.0)
        / 1000.0
        + 1.0;
    if (speed_mul - 1.0).abs() > f64::EPSILON {
        base_move_speed * speed_mul
    } else {
        base_move_speed
    }
}

/// `NpcAi.GetRealMovementFlags(moveSpeed)`: 3 = stand still, 5 = walk, 4 = run.
pub fn real_movement_flags(move_speed: f64) -> u8 {
    if move_speed < 0.1 {
        3
    } else if move_speed < 2.0 {
        5
    } else {
        4
    }
}

/// `Npc.BaseMoveSpeed` (`returning` = the current behavior is `ReturnStateBehavior`).
pub fn base_move_speed(server: &GameServer, npc: &NpcRef, returning: bool) -> f32 {
    let (template_id, stance, in_battle) = {
        let n = npc.data();
        (
            n.unit.template_id,
            n.current_game_stance,
            n.unit.is_in_battle,
        )
    };
    let Some(model_id) = server
        .game_data
        .systems
        .npcs
        .npc
        .template(template_id)
        .map(|t| t.model_id)
    else {
        return 1.0;
    };
    let Some(model) = server.game_data.systems.ai.ai.actor_model(model_id) else {
        return 1.0;
    };
    let Some(stance) = model.stance(stance) else {
        return 1.0;
    };
    if returning {
        return stance.ai_move_speed_sprint;
    }
    if in_battle {
        return stance.ai_move_speed_run.min(stance.max_speed);
    }
    stance.ai_move_speed_walk.min(stance.max_speed)
}

/// `Npc.CurrentGameStance` setter.
pub fn set_game_stance(npc: &NpcRef, value: GameStanceType) {
    let mut n = npc.data();
    if n.current_game_stance == value {
        return;
    }
    if n.can_fly {
        n.current_game_stance = GameStanceType::FLY;
        return;
    }
    if n.unit.is_under_water {
        n.current_game_stance = if value == GameStanceType::COMBAT {
            GameStanceType::CO_SWIM
        } else {
            GameStanceType::SWIM
        };
        return;
    }
    n.current_game_stance = value;
}

/// `Npc.CurrentAlertness = value`.
pub fn set_alertness(npc: &NpcRef, value: crate::models::movement::MoveTypeAlertness) {
    npc.data().current_alertness = value;
}

/// The templates of the buffs on `obj` (`Buffs.HasEffectsMatchingCondition(e => ...)`).
fn has_effect(
    server: &GameServer,
    obj: &WorldObject,
    pred: impl Fn(&BuffTemplate) -> bool,
) -> bool {
    let ids: Vec<u32> =
        obj.with_base_unit(|b| b.buffs().effects.iter().map(|e| e.template.id).collect());
    let data = &server.game_data.systems.skills;
    ids.into_iter()
        .any(|id| data.buff(id).is_some_and(|t| pred(t)))
}

/// `Buffs.HasEffectsMatchingCondition(Stun || Sleep || Root || Knockdown || Fastened)`.
pub fn is_rooted(server: &GameServer, obj: &WorldObject) -> bool {
    has_effect(server, obj, |t| {
        t.stun || t.sleep || t.root || t.knockdown || t.fastened
    })
}

/// `Buffs.HasEffectsMatchingCondition(Stun || Sleep || Silence)`.
pub fn is_silenced(server: &GameServer, obj: &WorldObject) -> bool {
    has_effect(server, obj, |t| t.stun || t.sleep || t.silence)
}

/// `Buffs.CheckBuffsExcludingTags(GetBuffsByTagId(Shackle), [DecreaseMoveSpeed]) ||
/// Buffs.CheckBuffs(GetBuffsByTagId(Snare))`.
pub fn is_shackled_or_snared(server: &GameServer, obj: &WorldObject) -> bool {
    let data = &server.game_data.systems.skills;
    let shackled = obj.with_base_unit(|b| {
        b.buffs().check_buffs_excluding_tags(
            data.buffs_by_tag(skill_constants::SHACKLE),
            &[skill_constants::DECREASE_MOVE_SPEED],
            |id| data.buff_tags(id),
        )
    });
    shackled || buffs::check_buffs_by_tag(server, obj, skill_constants::SNARE)
}

/// `(ActiveSkillController?.State ?? Ended) == Running`.
pub fn skill_controller_running(obj: &WorldObject) -> bool {
    crate::systems::skills::controllers::active(obj)
        .is_some_and(|sc| sc.state() == ScState::Running)
}

/// `DisplacedUntil.HasValue && DisplacedUntil.Value > DateTime.UtcNow`.
pub fn is_displaced(obj: &WorldObject) -> bool {
    let now = now_ms();
    obj.with_unit(|u| {
        let s = u.unit().skill_state.displaced_until_ms;
        let d = u.unit().displaced_until_ms;
        s > now || d.is_some_and(|d| d > now)
    })
    .unwrap_or(false)
}

/// `IsDead`.
pub fn is_dead(obj: &WorldObject) -> bool {
    obj.with_unit(|u| u.unit().is_dead()).unwrap_or(false)
}

/// `BaseUnit.CanSeeTarget(target)`: visible and not stealthed.
pub fn can_see_target(server: &GameServer, target: &WorldObject) -> bool {
    if !target.is_visible() {
        return false;
    }
    !buffs::check_buff_tag(server, target, tags::STEALTH)
}

/// `BaseUnit.UnitIsVisible(unit)`: the unit's region is one of the owner's neighbour regions.
pub fn unit_is_visible(server: &GameServer, owner: &WorldObject, unit: &WorldObject) -> bool {
    let (Some((inst, rx, ry)), unit_region) = (owner.region(), unit.region()) else {
        return false;
    };
    let Some(world) = server.world.world(inst) else {
        return false;
    };
    // `unit.Region?.Id ?? 0` compared with the neighbours' ids: a unit without a region never
    // matches (region ids start at 1 in C# regions? they are x * CellY + y, so (0, 0) is id 0
    // and matches a null region; mirrored by treating "no region" as region (0, 0) of the
    // owner's instance).
    let unit_key = match unit_region {
        Some((ui, ux, uy)) if ui == inst => (ux, uy),
        Some(_) => return false,
        None => (0, 0),
    };
    world.neighbors((rx, ry)).contains(&unit_key)
}

/// `Unit.CheckMovedPosition(oldPosition)`: `WorldManager.AddVisibleObject(this)` (the
/// `OnMovement` event has no subscribers).
pub fn check_moved_position(server: &GameServer, obj: &WorldObject) {
    if let Some(world) = geometry::world_of(server, obj) {
        let changes = world.add_visible_object(obj, &server.view_ctx());
        crate::world::session::handle_zone_changes(server, changes);
    }
}

/// `(uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds`.
pub fn ms_of_day() -> u32 {
    now_ms().rem_euclid(86_400_000) as u32
}

fn in_combat_flag(obj: &WorldObject, base: MoveTypeFlags) -> MoveTypeFlags {
    let in_battle = obj.with_unit(|u| u.unit().is_in_battle).unwrap_or(false);
    if in_battle {
        base | MoveTypeFlags::IN_COMBAT
    } else {
        base
    }
}

fn send_move(server: &GameServer, obj: &WorldObject, mt: MoveType) {
    broadcast(
        server,
        obj,
        &SCOneUnitMovementPacket {
            obj_id: obj.obj_id(),
            move_type: mt,
        },
        false,
    );
}

/// What `MoveTowards` needs from the NPC's AI for `WorldManager.GetReferenceHeight(ai, ...)`.
#[derive(Debug, Clone, Copy)]
pub struct MoveAi {
    /// `ai.GetCurrentBehavior() is HoldPositionBehavior or IdleBehavior`.
    pub hold_or_idle: bool,
}

/// `Npc.MoveTowards(other, distance, actorFlags, rangeTolerance)`: one movement step of at most
/// `distance` metres (before `MoveSpeedMul`) towards `other`, broadcast as
/// `SCOneUnitMovementPacket`. Returns true when already within `range_tolerance`.
pub fn move_towards(
    server: &GameServer,
    npc: &NpcRef,
    ai: Option<MoveAi>,
    other: Vec3,
    distance: f32,
    actor_flags: u8,
    range_tolerance: f32,
) -> bool {
    let obj = WorldObject::from(npc);
    if skill_controller_running(&obj) {
        return false;
    }
    if is_displaced(&obj) {
        return false;
    }
    let distance = distance * move_speed_mul(&obj); // Apply speed modifier
    if distance < 0.01 {
        return false;
    }
    if is_rooted(server, &obj) || is_dead(&obj) {
        return false;
    }
    if is_shackled_or_snared(server, &obj) {
        return false;
    }
    let (pos, zone_id, can_fly, spawner_z, stance, alertness) = {
        let n = npc.data();
        let t = n.unit.transform;
        (
            Vec3::new(t.x, t.y, t.z),
            t.zone_id,
            n.can_fly,
            n.spawner_z,
            n.current_game_stance,
            n.current_alertness,
        )
    };
    let target_dist = calculate_distance(pos, other, true);
    if target_dist <= range_tolerance {
        return true;
    }
    let travel_dist = target_dist.min(distance);
    let new = add_distance_to_front_between(travel_dist, target_dist, pos, other);
    let target_z = reference_height_in(
        server,
        obj.transform().instance_id,
        ai.map(|a| ReferenceHeightAi {
            can_fly,
            hold_or_idle: a.hold_or_idle,
            spawner_z,
            world_z: world_pos(server, &obj).z,
        }),
        new.x,
        new.y,
        new.z,
        zone_id,
    );
    let new = Vec3::new(new.x, new.y, target_z);
    let angle = calculate_angle_from(new, other);
    let (vel_x, vel_y) = add_distance_to_front(
        4000.0,
        0.0,
        0.0,
        (std::f64::consts::PI / 180.0 * angle) as f32,
    );
    let yaw = std::f32::consts::PI / 180.0 * (angle as f32 - 90.0);
    obj.with_transform(|t| {
        t.set_xyz(new.x, new.y, new.z);
        t.rot_x = 0.0;
        t.rot_y = 0.0;
        t.rot_z = yaw;
    });
    let (rx, ry, rz) = roll_pitch_yaw_sbytes_movement(0.0, 0.0, yaw);
    let um = UnitMove {
        x: new.x,
        y: new.y,
        z: new.z,
        vel_x: vel_x as i16,
        vel_y: vel_y as i16,
        rotation_x: rx,
        rotation_y: ry,
        rotation_z: rz,
        actor_flags: MoveTypeActorFlags(actor_flags),
        delta_movement: [0, 127, 0],
        stance,
        alertness,
        ..UnitMove::default()
    };
    let mut mt = MoveType::new(MoveKind::Unit(um));
    mt.flags = in_combat_flag(&obj, MoveTypeFlags::MOVING);
    mt.time = ms_of_day();
    check_moved_position(server, &obj);
    send_move(server, &obj, mt);
    false
}

/// `WorldManager.GetReferenceHeight(ai, x, y, z, zoneId)` (terrain / navmesh height, or the
/// spawner height for hold-position / idle / flying NPCs). Without heightmaps (tests,
/// `HeightMapsEnable = false`) the C# `GetHeight` returns 0 and the spawner / own Z is used.
pub fn reference_height(
    server: &GameServer,
    ai: Option<ReferenceHeightAi>,
    x: f32,
    y: f32,
    z: f32,
    zone_id: u32,
) -> f32 {
    match server.systems.npcs.heightmaps.get() {
        Some(h) => h.get_reference_height(ai.as_ref(), x, y, z, zone_id),
        None => match ai {
            None => 0.0,
            Some(a) => a.spawner_z.unwrap_or(a.world_z),
        },
    }
}

/// [`reference_height`] with the zone's ground: an NPC that walks (not flying, not holding /
/// idling at its spawner) stands on the physics ground (static geometry + terrain, the
/// collision service) under its position, like the zone's NPC living entities; the C#
/// reference height without the service.
#[allow(clippy::too_many_arguments)]
pub fn reference_height_in(
    server: &GameServer,
    instance_id: u32,
    ai: Option<ReferenceHeightAi>,
    x: f32,
    y: f32,
    z: f32,
    zone_id: u32,
) -> f32 {
    let walks = ai.is_some_and(|a| !a.can_fly && !a.hold_or_idle);
    if walks {
        if let Some(g) = crate::systems::ai::collision::ground_height(server, instance_id, x, y, z)
        {
            if g != 0.0 {
                return g;
            }
        }
    }
    reference_height(server, ai, x, y, z, zone_id)
}

/// `Npc.LookTowards(other, flags)`.
pub fn look_towards(server: &GameServer, npc: &NpcRef, other: Vec3, flags: u8) {
    let obj = WorldObject::from(npc);
    let (pos, alertness) = {
        let n = npc.data();
        let t = n.unit.transform;
        (Vec3::new(t.x, t.y, t.z), n.current_alertness)
    };
    let angle = calculate_angle_from(pos, other);
    let yaw = std::f32::consts::PI / 180.0 * (angle as f32 - 90.0);
    obj.with_transform(|t| {
        t.rot_x = 0.0;
        t.rot_y = 0.0;
        t.rot_z = yaw;
    });
    let (rx, ry, rz) = roll_pitch_yaw_sbytes_movement(0.0, 0.0, yaw);
    let um = UnitMove {
        x: pos.x,
        y: pos.y,
        z: pos.z,
        rotation_x: rx,
        rotation_y: ry,
        rotation_z: rz,
        actor_flags: MoveTypeActorFlags(flags),
        delta_movement: [0, 0, 0],
        stance: GameStanceType(0),
        alertness,
        ..UnitMove::default()
    };
    let mut mt = MoveType::new(MoveKind::Unit(um));
    mt.flags = in_combat_flag(&obj, MoveTypeFlags::MOVING);
    mt.time = ms_of_day();
    send_move(server, &obj, mt);
}

/// `Npc.StopMovement()`.
pub fn stop_movement(server: &GameServer, npc: &NpcRef) {
    let obj = WorldObject::from(npc);
    let (t, stance, alertness) = {
        let n = npc.data();
        (n.unit.transform, n.current_game_stance, n.current_alertness)
    };
    let (_, _, rz) = roll_pitch_yaw_sbytes_movement(t.rot_x, t.rot_y, t.rot_z);
    let um = UnitMove {
        x: t.x,
        y: t.y,
        z: t.z,
        rotation_z: rz,
        delta_movement: [0, 0, 0],
        stance,
        alertness,
        ..UnitMove::default()
    };
    let mut mt = MoveType::new(MoveKind::Unit(um));
    mt.flags = in_combat_flag(&obj, MoveTypeFlags::STOPPING);
    mt.time = ms_of_day();
    send_move(server, &obj, mt);
}

/// `Npc.SetTarget(other)` minus `Ai.AlreadyTargeted` (set by the caller):
/// `CurrentTarget = other` and `SCTargetChangedPacket` to everyone around.
pub fn set_target(server: &GameServer, npc: &NpcRef, other: Option<&WorldObject>) {
    let id = other.map_or(0, WorldObject::obj_id);
    npc.data().unit.current_target = id;
    broadcast(
        server,
        &WorldObject::from(npc),
        &SCTargetChangedPacket {
            obj_id: npc.obj_id,
            target_obj_id: id,
        },
        true,
    );
}

/// `Npc.CurrentAggroTarget = value`: a new non-null target receives
/// `SCAggroTargetChangedPacket` (when it is a player).
pub fn set_current_aggro_target(npc: &NpcRef, value: Option<&WorldObject>) {
    let id = value.map_or(0, WorldObject::obj_id);
    {
        let mut n = npc.data();
        if n.current_aggro_target == id {
            return;
        }
        n.current_aggro_target = id;
    }
    if let Some(p) = value.and_then(WorldObject::as_player) {
        p.send(SCAggroTargetChangedPacket {
            npc_id: npc.obj_id,
            target_id: id,
        });
    }
}

/// `MathUtil.IsFront(obj1, obj2, fovScale)`.
pub fn is_front(server: &GameServer, a: &WorldObject, b: &WorldObject, fov_scale: f64) -> bool {
    if fov_scale >= 2.0 {
        return true;
    }
    if fov_scale <= 0.0 {
        return false;
    }
    let pa = geometry::world_pos(server, &Target::Obj(a.clone()));
    let pb = geometry::world_pos(server, &Target::Obj(b.clone()));
    // CalculateAngleFrom(obj1, obj2) = angle(obj1 -> obj2) - 90 - obj1.Rotation.Z (degrees)
    let angle =
        f64::from(pb.y - pa.y).atan2(f64::from(pb.x - pa.x)) * 180.0 / std::f64::consts::PI % 360.0;
    let deg = clamp_deg_angle(angle - 90.0 - f64::from(pa.rot_z) * 180.0 / std::f64::consts::PI);
    deg >= -90.0 * fov_scale && deg <= 90.0 * fov_scale
}

/// `MathUtil.ClampDegAngle`.
pub fn clamp_deg_angle(mut a: f64) -> f64 {
    while a > 180.0 {
        a -= 360.0;
    }
    while a < -180.0 {
        a += 360.0;
    }
    a
}

/// `Ai.Owner.BroadcastPacket(packet, false)` (an NPC is never its own receiver).
pub fn broadcast_packet(
    c: &super::framework::AiCtx<'_>,
    packet: &dyn crate::net::packet::ServerPacket,
) {
    broadcast(c.server, &c.obj, packet, false);
}

/// `owner.ParentWorld.GetUnit(objId)`.
pub fn world_unit(server: &GameServer, owner: &WorldObject, obj_id: u32) -> Option<WorldObject> {
    geometry::world_of(server, owner)?.unit(obj_id)
}

/// `WorldManager.GetAround<T>(obj, radius, useModelSize)` for every kind (callers filter).
pub fn around(
    server: &GameServer,
    obj: &WorldObject,
    radius: f32,
    use_model_size: bool,
) -> Vec<WorldObject> {
    geometry::around_radius(server, &Target::Obj(obj.clone()), radius, use_model_size)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn movement_flags() {
        assert_eq!(real_movement_flags(0.05), 3);
        assert_eq!(real_movement_flags(1.9), 5);
        assert_eq!(real_movement_flags(2.0), 4);
    }

    #[test]
    fn clamp_angles() {
        assert_eq!(clamp_deg_angle(270.0), -90.0);
        assert_eq!(clamp_deg_angle(-190.0), 170.0);
        assert_eq!(clamp_deg_angle(180.0), 180.0);
    }
}
