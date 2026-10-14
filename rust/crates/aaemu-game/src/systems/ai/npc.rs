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
    let (pos, zone_id, can_fly, spawner_z, stance, alertness, model_id) = {
        let n = npc.data();
        let t = n.unit.transform;
        (
            Vec3::new(t.x, t.y, t.z),
            t.zone_id,
            n.can_fly,
            n.spawner_z,
            n.current_game_stance,
            n.current_alertness,
            n.unit.model_id,
        )
    };
    let underwater = server
        .game_data
        .systems
        .ai
        .ai
        .actor_model(model_id)
        .is_some_and(|m| m.underwater_creature);
    let target_dist = calculate_distance(pos, other, can_fly || underwater);
    if target_dist <= range_tolerance {
        return true;
    }
    let travel_dist = target_dist.min(distance);
    let new = add_distance_to_front_between(travel_dist, target_dist, pos, other);
    let new = if underwater {
        // Native bUnderwaterCreature uses 3D movement. Keep the authored step height;
        // projecting onto the seabed would move a surface fish away from its angler.
        underwater_step(server, obj.transform().instance_id, model_id, pos, new)
    } else if can_fly {
        // Flying NPCs keep the interpolated 3D step: taking their reference height like a
        // walker would snap every step back to the spawner's altitude and discard the climb
        // or descent that `AddDistanceToFrontBetween` just computed.
        new
    } else {
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
        Vec3::new(new.x, new.y, target_z)
    };
    // Keep the travelled heading when a step lands exactly on a waypoint. Computing
    // new -> other there yields atan2(0, 0), turning every arrival eastward.
    let angle = calculate_angle_from(pos, other);
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

/// A 3D living-collider step without the ground projection used by walking NPCs.
fn underwater_step(
    server: &GameServer,
    instance_id: u32,
    model_id: u32,
    from: Vec3,
    to: Vec3,
) -> Vec3 {
    let collider = server
        .game_data
        .systems
        .ai
        .collision
        .living
        .get(&model_id)
        .map(crate::systems::ai::collision::displacement::Collider::from_living)
        .unwrap_or_default();
    let hit = crate::systems::ai::collision::sweep_capsule(
        server,
        instance_id,
        [from.x, from.y, from.z + collider.height],
        [to.x, to.y, to.z + collider.height],
        collider.radius,
        collider.half_height,
        crate::systems::ai::collision::query::mask::SOLID_TERRAIN,
    );
    if let Some(Some(contact)) = hit {
        let distance = calculate_distance(from, to, true);
        if distance > f32::EPSILON {
            return add_distance_to_front_between(
                (contact.dist - 0.1).max(0.0).min(distance),
                distance,
                from,
                to,
            );
        }
        return from;
    }
    to
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
///
/// A walker is never resolved above its current level plus the collision service's step-up
/// allowance ([`crate::systems::ai::collision::GROUND_PROBE_UP`], 2 m): a cave's heightmap
/// column is the surface above its roof, so accepting that (or the spawner Z) would teleport
/// the walker out of the cave. With no reachable floor the current level is kept.
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
    let Some(a) = ai else {
        return reference_height(server, None, x, y, z, zone_id);
    };
    if a.can_fly || a.hold_or_idle {
        return reference_height(server, Some(a), x, y, z, zone_id);
    }
    let step_limit = a.world_z + crate::systems::ai::collision::GROUND_PROBE_UP;
    // Prefer the actual collision floor below / within the step range: the probe starts at
    // the current level + 2 m, so a hit is always reachable, and a hit at Z = 0 is a valid
    // surface. A missed probe is not a floor; its terrain fallback (in a cave the surface
    // above the walker) is only usable through the heightmap query below.
    if let Some(Some(g)) = crate::systems::ai::collision::ground_height_probe(
        server,
        instance_id,
        x,
        y,
        a.world_z,
        crate::systems::ai::collision::GROUND_PROBE_UP,
        crate::systems::ai::collision::GROUND_PROBE_DOWN,
    ) {
        if g <= step_limit {
            return g;
        }
    }
    // Terrain / navigation fallback (heightmap or navmesh): only a known, reachable floor
    // (0 = unknown, as in `GetHeight`).
    let terrain = crate::systems::npcs::heightmap::get_height(server, zone_id, x, y, z);
    if terrain != 0.0 && terrain <= step_limit {
        return terrain;
    }
    // No reachable floor: keep the walker at its current level instead of the spawner Z.
    a.world_z
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
    use std::collections::HashMap;
    use std::sync::Arc;

    use aaemu_physics::geometry::primitives::BoxPrim;
    use aaemu_physics::geometry::shapes::stream::{save_phys_geometry, PhysGeomHeader};
    use aaemu_physics::geometry::shapes::BoxGeom;
    use aaemu_physics::math::{Quat, Vec3 as PhysicsVec3};

    use crate::data::GameData;
    use crate::models::npc::Npc;
    use crate::opcodes::sc;
    use crate::systems::ai::collision::objects::{apply_doodads, collision_flags, DoodadInfo};
    use crate::systems::ai::collision::terrain::tests::Plane;
    use crate::systems::ai::data::ActorModel;
    use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};
    use crate::world::Entity;

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

    fn water_server() -> GameServer {
        let mut data = GameData::empty();
        for (model, underwater) in [(1373, true), (1374, false)] {
            data.systems.ai.ai.actor_models.insert(
                model,
                Arc::new(ActorModel {
                    id: if underwater { 633 } else { 634 },
                    radius: 0.5,
                    height: 0.5,
                    movement_id: 0,
                    underwater_creature: underwater,
                    ..ActorModel::default()
                }),
            );
            data.systems.skills.model_radii.insert(model, 0.5);
            data.systems.ai.collision.living.insert(
                model,
                crate::systems::ai::collision::data::LivingParams::default(),
            );
        }
        let server = test_server_with(data);
        let shape = BoxGeom::new(&BoxPrim::aligned(
            PhysicsVec3::ZERO,
            PhysicsVec3::splat(1.0),
        ));
        let header = PhysGeomHeader {
            inertia: PhysicsVec3::splat(1.0),
            q: Quat::IDENTITY,
            origin: PhysicsVec3::ZERO,
            volume: 8.0,
            unk52: 1,
            surface_idx: 0,
        };
        let mut blob = Vec::new();
        save_phys_geometry(&header, &shape, &mut blob);
        let files = HashMap::from([(
            "game/objects/test/water_wall.cgf".into(),
            crate::systems::ai::collision::cgf::tests::synthetic_cgf(&blob),
        )]);
        assert!(server
            .systems
            .ai
            .collision
            .test_files
            .set(Arc::new(files))
            .is_ok());
        assert!(server
            .systems
            .ai
            .collision
            .test_terrain
            .set(Arc::new(Plane {
                a: 103.795,
                b: 0.0,
                c: 0.0,
                n: 1024,
            }))
            .is_ok());
        server
    }

    fn water_npc(server: &GameServer, obj_id: u32, model: u32, can_fly: bool) -> NpcRef {
        let mut n = Npc::new(13072);
        n.unit.model_id = model;
        n.unit.scale = 1.0;
        n.unit.hp = 1000;
        n.unit.max_hp = 1000;
        n.unit.transform.set_xyz(500.0, 500.0, 113.7);
        n.spawner_z = Some(113.7);
        n.can_fly = can_fly;
        let npc = Entity::new(obj_id, n);
        server
            .world
            .world(0)
            .unwrap()
            .spawn(&npc, &server.view_ctx());
        npc
    }

    #[tokio::test]
    async fn underwater_move_preserves_height_and_fishing_range_while_walkers_snap() {
        let server = water_server();
        let mut client = TestClient::new(&server, 1, 1);
        let mut character = test_character(1, "Angler", 430.0, 500.0);
        character.unit.transform.z = 113.7;
        let angler = spawn_player(&server, &client, character, 0x100);
        let fish = water_npc(&server, 0x200, 1373, false);
        let walker = water_npc(&server, 0x201, 1374, false);
        let flyer = water_npc(&server, 0x202, 1374, true);
        client.drain();
        for npc in [&fish, &walker, &flyer] {
            move_towards(
                &server,
                npc,
                Some(MoveAi {
                    hold_or_idle: false,
                }),
                Vec3::new(520.0, 500.0, 113.7),
                10.0,
                4,
                1.0,
            );
        }
        let fish_pos = fish.data().unit.transform;
        assert!((fish_pos.x - 510.0).abs() < 0.001);
        assert!((fish_pos.z - 113.7).abs() < 0.001);
        assert!((walker.data().unit.transform.z - 103.795).abs() < 0.001);
        assert!((flyer.data().unit.transform.z - 113.7).abs() < 0.001);
        // The real response skills have an 80 m range. A spurious 10 m seabed drop
        // would put this fish outside it despite the same horizontal movement.
        assert!(
            distance_to(
                &server,
                &WorldObject::from(&fish),
                &WorldObject::from(&angler),
                true
            ) <= 80.0
        );
        assert!(client.drain_ops().contains(&sc::SCOneUnitMovementPacket));
    }

    #[tokio::test]
    async fn underwater_move_sweeps_solids_without_projecting_onto_seabed() {
        let server = water_server();
        let fish = water_npc(&server, 0x200, 1373, false);
        let world = crate::systems::ai::collision::phys_world(&server, 0).unwrap();
        let files = crate::systems::ai::collision::files(&server).unwrap();
        let models = crate::systems::ai::collision::model_cache(&server, &files);
        let obstacle = DoodadInfo {
            obj_id: 42,
            model: "cgf://objects/test/water_wall.cgf".into(),
            pos: PhysicsVec3::new(505.0, 500.0, 114.2),
            q: Quat::IDENTITY,
            scale: 1.0,
            flags: collision_flags(false, false, false),
        };
        let mut shapes = HashMap::new();
        assert_eq!(
            apply_doodads(&world, &files, &models, &mut shapes, &[obstacle]),
            (1, 0)
        );
        move_towards(
            &server,
            &fish,
            Some(MoveAi {
                hold_or_idle: false,
            }),
            Vec3::new(520.0, 500.0, 113.7),
            10.0,
            4,
            1.0,
        );
        let position = fish.data().unit.transform;
        assert!(position.x > 500.0 && position.x < 504.0, "{position:?}");
        assert!((position.z - 113.7).abs() < 0.001);
    }
}

#[cfg(test)]
#[path = "npc_height_tests.rs"]
mod npc_height_tests;
