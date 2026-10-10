//! `ShipHarpoonRopeController` (Launch 13749 / Cut 13750, `CSSkillControllerStatePacket`) and
//! `CSUnhangPacket`. The tow forces themselves are physics (`ShipHarpoonTowPhysics`, owned by
//! the physics port); [`tick_tension_tear_and_get_hull_recoil_delta_v`] and
//! [`tick_harpoon_rope_controller_lifetime`] are the per-tick entry points the physics calls.

use std::collections::HashMap;
use std::sync::Mutex;

use crate::models::slave::ShipHarpoonRopeState;
use crate::server::GameServer;
use crate::systems::skills::packets::SCSkillControllerStatePacket;
use crate::systems::skills::types::SkillCastTarget;
use crate::world::{now_ms, PlayerRef, SlaveRef, WorldInstance, WorldObject};

use super::packets::SCUnhungPacket;
use super::{broadcast, world_of};

/// `HarpoonMechanicsDebug.ShipLaunchHarpoonSkillId` / `ShipCutHarpoonRopeSkillId`.
pub const SHIP_LAUNCH_HARPOON_SKILL_ID: u32 = 13749;
pub const SHIP_CUT_HARPOON_ROPE_SKILL_ID: u32 = 13750;

const TEAR_STRETCH_METERS: f32 = 3.0;
const TEAR_JERK_METERS_PER_SEC: f32 = 9.0;
const TEAR_JERK_MIN_STRETCH_METERS: f32 = 1.0;
const RECOIL_DV_PER_STRETCH: f32 = 0.12;
const RECOIL_DV_PER_JERK: f32 = 0.02;
const RECOIL_MAX_DELTA_V: f32 = 1.6;
/// `SkillControllerKind.Rope`.
const SKILL_CONTROLLER_KIND_ROPE: u32 = 5;
/// `ShipHarpoonTowPhysics.ServerRopePaidLengthAdditiveMeters` / `SlackMarginMeters`.
pub const SERVER_ROPE_PAID_LENGTH_ADDITIVE_METERS: f32 = 12.5;
pub const SLACK_MARGIN_METERS: f32 = 0.5;

/// `_tensionHistoryByHarpoonObjId` (stretch of the previous tick).
static TENSION_HISTORY: Mutex<Option<HashMap<u32, f32>>> = Mutex::new(None);

fn history<R>(f: impl FnOnce(&mut HashMap<u32, f32>) -> R) -> R {
    let mut g = TENSION_HISTORY.lock().expect("harpoon history poisoned");
    f(g.get_or_insert_with(HashMap::new))
}

fn dist(a: [f32; 3], b: [f32; 3]) -> f32 {
    ((a[0] - b[0]).powi(2) + (a[1] - b[1]).powi(2) + (a[2] - b[2]).powi(2)).sqrt()
}

fn world_position(world: &WorldInstance, obj: &WorldObject) -> [f32; 3] {
    let p = world.world_pos_rot(obj);
    [p.x, p.y, p.z]
}

/// `basis.Transform.World` rotation applied to `local * scale` plus the basis position.
fn basis_to_world(world: &WorldInstance, basis: &WorldObject, local: [f32; 3]) -> [f32; 3] {
    let p = world.world_pos_rot(basis);
    let s = basis.scale();
    let r = p
        .to_quaternion()
        .rotate([local[0] * s, local[1] * s, local[2] * s]);
    [r[0] + p.x, r[1] + p.y, r[2] + p.z]
}

/// `TryResolveHookFromSkillTarget(target, caster, ...)`: (hook world, basis obj id, local).
fn resolve_hook(
    world: &WorldInstance,
    target: &SkillCastTarget,
) -> Option<([f32; 3], u32, [f32; 3])> {
    match *target {
        SkillCastTarget::Position {
            obj_id1, x, y, z, ..
        } => {
            if obj_id1 != 0 {
                let basis = world.object(obj_id1).filter(WorldObject::is_unit)?;
                let local = [x, y, z];
                return Some((basis_to_world(world, &basis, local), obj_id1, local));
            }
            Some(([x, y, z], 0, [0.0; 3]))
        }
        SkillCastTarget::Position2 { x, y, z, .. } | SkillCastTarget::Position3 { x, y, z, .. } => {
            Some(([x, y, z], 0, [0.0; 3]))
        }
        _ => None,
    }
}

/// `GetHookWorldPosition(harpoonSlave)`.
pub fn get_hook_world_position(world: &WorldInstance, st: &ShipHarpoonRopeState) -> [f32; 3] {
    if !st.is_engaged {
        return [0.0; 3];
    }
    if st.hook_basis_obj_id == 0 {
        return st.hook_world;
    }
    match world
        .object(st.hook_basis_obj_id)
        .filter(WorldObject::is_unit)
    {
        Some(b) => basis_to_world(world, &b, st.hook_local_in_basis),
        None => st.hook_world,
    }
}

/// `ResolveRopeControllerExpireUtc(launchSkill)`.
fn resolve_rope_controller_expire(server: &GameServer) -> Option<i64> {
    let skills = &server.game_data.systems.skills;
    let launch = skills.skill(SHIP_LAUNCH_HARPOON_SKILL_ID)?;
    if launch.skill_controller_id == 0 {
        return None;
    }
    let sc = skills.skill_controller(launch.skill_controller_id)?;
    if sc.kind_id != SKILL_CONTROLLER_KIND_ROPE {
        return None;
    }
    let mut ms = i32::MAX;
    if sc.value[0] > 0 {
        ms = ms.min(sc.value[0]);
    }
    if sc.value[1] > 0 {
        ms = ms.min(sc.value[1]);
    }
    (ms != i32::MAX).then(|| now_ms() + i64::from(ms))
}

/// `BroadcastSkillControllerRopeState(harpoonSlave, len, teared, cutouted, except)`.
fn broadcast_rope_state(
    server: &GameServer,
    slave: &SlaveRef,
    len: f32,
    teared: bool,
    cutouted: bool,
    except: Option<u32>,
) {
    let obj = WorldObject::from(slave);
    let Some(world) = world_of(server, &obj) else {
        return;
    };
    let pkt = SCSkillControllerStatePacket {
        obj_id: slave.obj_id,
        sc_type: 0,
        len,
        teared,
        cutouted,
    };
    for p in world.players_around(&obj) {
        if except == Some(p.obj_id) {
            continue;
        }
        p.send(pkt);
    }
}

/// `OnLaunchSucceeded(harpoonSlave, target, operator)`.
pub fn on_launch_succeeded(
    server: &GameServer,
    slave: &SlaveRef,
    target: &SkillCastTarget,
    operator: Option<&PlayerRef>,
) {
    let obj = WorldObject::from(slave);
    let Some(world) = world_of(server, &obj) else {
        return;
    };
    let Some((hook_world, basis, local)) = resolve_hook(&world, target) else {
        return;
    };
    if slave.data().harpoon_rope.is_engaged {
        break_rope_for_clients(server, slave, false);
    } else {
        slave.data().harpoon_rope.clear();
    }
    let max_range = server
        .game_data
        .systems
        .skills
        .skill(SHIP_LAUNCH_HARPOON_SKILL_ID)
        .map_or(0.0, |t| (t.max_range as f32).max(0.0));
    let origin = world_position(&world, &obj);
    let initial_len = dist(origin, hook_world);
    let basis_is_other_boat = basis != 0
        && world.slave(basis).is_some_and(|b| {
            let tpl = b.data().unit.template_id;
            server
                .game_data
                .systems
                .vehicles
                .slave_template(tpl)
                .is_some_and(super::data::SlaveTemplate::is_a_boat)
        });
    let terrain =
        !super::ship_physics::is_water(server, &world, hook_world) && !basis_is_other_boat;
    let expire = resolve_rope_controller_expire(server);
    {
        let mut d = slave.data();
        let st = &mut d.harpoon_rope;
        st.is_engaged = true;
        st.hook_world = hook_world;
        st.hook_basis_obj_id = basis;
        st.hook_local_in_basis = local;
        st.rope_length = initial_len;
        st.max_launch_range = max_range;
        st.last_teared = false;
        st.last_cutout = false;
        st.hook_attached_to_terrain = terrain;
        st.controller_expire_at_ms = expire;
    }
    broadcast_rope_state(
        server,
        slave,
        initial_len,
        false,
        false,
        operator.map(|p| p.obj_id),
    );
}

/// `OnCutRope(harpoonSlave, operator)`.
pub fn on_cut_rope(server: &GameServer, slave: &SlaveRef) {
    break_rope_for_clients(server, slave, true);
}

/// `OnOperatorLeftSlave(slave, leavingOperator)`.
pub fn on_operator_left_slave(server: &GameServer, slave: &SlaveRef) {
    break_rope_for_clients(server, slave, false);
}

/// `BreakRopeForClients(slave, cutouted)`.
pub fn break_rope_for_clients(server: &GameServer, slave: &SlaveRef, cutouted: bool) {
    let len = {
        let mut d = slave.data();
        if !d.harpoon_rope.is_engaged {
            return;
        }
        let len = d.harpoon_rope.rope_length;
        d.harpoon_rope.clear();
        len
    };
    broadcast(
        server,
        &WorldObject::from(slave),
        &SCSkillControllerStatePacket {
            obj_id: slave.obj_id,
            sc_type: 0,
            len,
            teared: true,
            cutouted,
        },
        false,
    );
}

/// `ClampClientReportedRopeLength(slave, len)`.
fn clamp_len(world: &WorldInstance, slave: &SlaveRef, len: f32) -> f32 {
    let st = slave.data().harpoon_rope;
    if !len.is_finite() {
        return st.rope_length;
    }
    let len = len.max(0.0);
    if st.max_launch_range > 0.0 {
        return len.min(st.max_launch_range + SERVER_ROPE_PAID_LENGTH_ADDITIVE_METERS);
    }
    let pos = world_position(world, &WorldObject::from(slave));
    let chord = dist(pos, get_hook_world_position(world, &st));
    len.min(chord + SERVER_ROPE_PAID_LENGTH_ADDITIVE_METERS + SLACK_MARGIN_METERS + 40.0)
}

/// `TryBreakRopeIfHookOutOfRange(slave)`.
fn try_break_if_out_of_range(server: &GameServer, world: &WorldInstance, slave: &SlaveRef) -> bool {
    let st = slave.data().harpoon_rope;
    if !st.is_engaged || st.max_launch_range <= 0.0 {
        return false;
    }
    let pos = world_position(world, &WorldObject::from(slave));
    let d = dist(pos, get_hook_world_position(world, &st));
    if d <= st.max_launch_range + 1.5 {
        return false;
    }
    break_rope_for_clients(server, slave, false);
    true
}

/// `TryApplySkillControllerState(character, objId, len, teared, cutouted)`
/// (`CSSkillControllerStatePacket` with a rope payload).
pub fn try_apply_skill_controller_state(
    server: &GameServer,
    character: &PlayerRef,
    obj_id: u32,
    len: f32,
    _teared: bool,
    cutouted: bool,
) {
    let Some(world) = world_of(server, &WorldObject::from(character)) else {
        return;
    };
    let Some(slave) = world.slave(obj_id) else {
        return;
    };
    let (attached, engaged) = {
        let d = slave.data();
        (
            d.attached_characters
                .iter()
                .any(|(_, c)| *c == character.obj_id),
            d.harpoon_rope.is_engaged,
        )
    };
    if !attached || !engaged {
        return;
    }
    let clamped = clamp_len(&world, &slave, len);
    {
        let mut d = slave.data();
        d.harpoon_rope.rope_length = clamped;
        // Client-reported "teared" must not drive server break.
        d.harpoon_rope.last_teared = false;
        d.harpoon_rope.last_cutout = cutouted;
    }
    if try_break_if_out_of_range(server, &world, &slave) {
        return;
    }
    if cutouted {
        break_rope_for_clients(server, &slave, cutouted);
        return;
    }
    broadcast_rope_state(
        server,
        &slave,
        clamped,
        false,
        false,
        Some(character.obj_id),
    );
}

/// `EnumerateAttachedSlaveDescendants(root)`.
fn descendants(world: &WorldInstance, root: &SlaveRef, out: &mut Vec<SlaveRef>) {
    let children = root.data().attached_slaves.clone();
    for c in children {
        if let Some(s) = world.slave(c) {
            out.push(s.clone());
            descendants(world, &s, out);
        }
    }
}

/// The harpoon lines of `hull` for the tow physics: every slave below the hull (depth-first
/// over `AttachedSlaves`), its cannon position and hook position (`GetHookWorldPosition`).
pub fn harpoon_lines(
    world: &WorldInstance,
    hull: &SlaveRef,
) -> Vec<crate::systems::ai::physics::harpoon::HarpoonLine> {
    use crate::systems::skills::controllers::Vec3;
    let direct = hull.data().attached_slaves.clone();
    let mut children = Vec::new();
    descendants(world, hull, &mut children);
    children
        .iter()
        .map(|child| {
            let st = child.data().harpoon_rope;
            let c = world_position(world, &WorldObject::from(child));
            let h = get_hook_world_position(world, &st);
            crate::systems::ai::physics::harpoon::HarpoonLine {
                child_obj_id: child.obj_id,
                direct: direct.contains(&child.obj_id),
                cannon_pos: Vec3::new(c[0], c[1], c[2]),
                hook_pos: Vec3::new(h[0], h[1], h[2]),
                engaged: st.is_engaged,
                hook_basis_obj_id: st.hook_basis_obj_id,
                rope_length: st.rope_length,
                hook_attached_to_terrain: st.hook_attached_to_terrain,
            }
        })
        .collect()
}

/// `TickHarpoonRopeControllerLifetime(hull)`: breaks ropes whose skill controller expired.
pub fn tick_harpoon_rope_controller_lifetime(server: &GameServer, hull: &SlaveRef) {
    let Some(world) = world_of(server, &WorldObject::from(hull)) else {
        return;
    };
    let now = now_ms();
    let mut nodes = vec![hull.clone()];
    descendants(&world, hull, &mut nodes);
    for n in nodes {
        let st = n.data().harpoon_rope;
        if st.is_engaged && st.controller_expire_at_ms.is_some_and(|t| now >= t) {
            break_rope_for_clients(server, &n, false);
        }
    }
}

/// `TickTensionTearAndGetHullRecoilDeltaV(hull, dtSec)`: breaks over-stretched ropes and returns
/// the recoil delta-V for the shooter hull (world x / y).
pub fn tick_tension_tear_and_get_hull_recoil_delta_v(
    server: &GameServer,
    hull: &SlaveRef,
    dt_sec: f32,
) -> [f32; 2] {
    let Some(world) = world_of(server, &WorldObject::from(hull)) else {
        return [0.0; 2];
    };
    if hull.data().attached_slaves.is_empty() || dt_sec <= 0.0 {
        return [0.0; 2];
    }
    let mut recoil = [0.0f32; 2];
    let mut children = Vec::new();
    descendants(&world, hull, &mut children);
    for child in children {
        let st = child.data().harpoon_rope;
        if !st.is_engaged {
            history(|h| h.remove(&child.obj_id));
            continue;
        }
        let cannon = world_position(&world, &WorldObject::from(&child));
        let hook = get_hook_world_position(&world, &st);
        let d = dist(cannon, hook);
        let paid = st.rope_length + SERVER_ROPE_PAID_LENGTH_ADDITIVE_METERS;
        let stretch = d - paid;
        if stretch <= 0.0 {
            history(|h| h.insert(child.obj_id, 0.0));
            continue;
        }
        let prev = history(|h| h.insert(child.obj_id, stretch));
        let jerk = prev.map_or(0.0, |p| (stretch - p) / dt_sec);
        let tear_by_stretch = stretch >= TEAR_STRETCH_METERS;
        let tear_by_jerk =
            jerk >= TEAR_JERK_METERS_PER_SEC && stretch >= TEAR_JERK_MIN_STRETCH_METERS;
        if !tear_by_stretch && !tear_by_jerk {
            continue;
        }
        break_rope_for_clients(server, &child, false);
        let dx = hook[0] - cannon[0];
        let dy = hook[1] - cannon[1];
        let d2 = dx * dx + dy * dy;
        if d2 < 1e-6 {
            continue;
        }
        let inv = 1.0 / d2.sqrt();
        let (ux, uy) = (dx * inv, dy * inv);
        let excess = (jerk - TEAR_JERK_METERS_PER_SEC).max(0.0);
        let dv =
            RECOIL_MAX_DELTA_V.min(stretch * RECOIL_DV_PER_STRETCH + excess * RECOIL_DV_PER_JERK);
        recoil[0] += -ux * dv;
        recoil[1] += -uy * dv;
    }
    recoil
}

/// The `CSStartSkillPacket` tail for a successful mount / slave skill on a slave caster.
pub fn on_slave_skill_used(
    server: &GameServer,
    slave: &SlaveRef,
    skill_id: u32,
    target: &SkillCastTarget,
    operator: &PlayerRef,
) {
    if skill_id == SHIP_LAUNCH_HARPOON_SKILL_ID {
        on_launch_succeeded(server, slave, target, Some(operator));
    } else if skill_id == SHIP_CUT_HARPOON_ROPE_SKILL_ID {
        on_cut_rope(server, slave);
    }
}

/// `CSUnhangPacket.Read`. 1.2 does not send the target obj id: it is the unit's sticky parent,
/// which is cleared; a ship the unit was hanging on (its harpoon rope) has its rope broken for
/// the clients.
pub fn unhang(server: &GameServer, character: &PlayerRef, unit_obj_id: u32, reason: u32) {
    let me = WorldObject::from(character);
    let world = world_of(server, &me);
    let mut target_obj_id = 0;
    let mut sticky_slave = None;
    if let Some(world) = &world {
        // Connection.ActiveChar.ParentWorld.GetBaseUnit(unitObjId)
        let unit = world
            .object(unit_obj_id)
            .filter(|o| !matches!(o, WorldObject::Gimmick(_)));
        if let Some(unit) = unit {
            let sticky = world.sticky_parent(&unit);
            sticky_slave = sticky.as_ref().and_then(|s| s.as_slave().cloned());
            target_obj_id = sticky.as_ref().map_or(0, WorldObject::obj_id);
            world.set_sticky_parent(&unit, None);
        }
    }
    tracing::trace!(
        "Unhang, unitObjId: {unit_obj_id}, targetObjId: {target_obj_id}, Reason: {reason}"
    );
    broadcast(
        server,
        &me,
        &SCUnhungPacket {
            unit_obj_id,
            target_obj_id,
            reason,
        },
        false,
    );
    if let Some(slave) = sticky_slave {
        break_rope_for_clients(server, &slave, false);
    }
}

/// `CSHangPacket.Read`: the unit's sticky parent becomes the target, `SCHungPacket` goes to
/// the others around the active character and a doodad target is used by the unit.
pub fn hang(server: &GameServer, character: &PlayerRef, unit_obj_id: u32, target_obj_id: u32) {
    tracing::trace!("Hang, unitObjId: {unit_obj_id}, targetObjId: {target_obj_id}");
    let me = WorldObject::from(character);
    let Some(world) = world_of(server, &me) else {
        return;
    };
    let unit = world
        .object(unit_obj_id)
        .filter(|o| !matches!(o, WorldObject::Gimmick(_)));
    let target = world.object(target_obj_id);
    if let (Some(unit), Some(target)) = (&unit, &target) {
        world.set_sticky_parent(unit, Some(target));
    }
    crate::world::broadcast(
        &world,
        &me,
        &crate::systems::npcs::doodad::interactions::SCHungPacket {
            unit_obj_id,
            target_obj_id,
        },
        false,
    );
    if let Some(doodad) = target.as_ref().and_then(WorldObject::as_doodad) {
        // doodad.Use(character): a null character is passed on as a null caster.
        crate::systems::npcs::doodad::manager::use_doodad(
            server,
            &world,
            doodad,
            unit.as_ref(),
            0,
            0,
        );
    }
}
