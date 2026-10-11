//! Ship hull overlap resolution against other hulls and static obstacles:
//! `ShipShipInteraction` (XZ OBB SAT, mass-weighted separation, hull collision damage),
//! `ShipStaticObstacleContact`, `ShipDoodadInteraction` (`collide_ship` doodads) and
//! `ShipCliffInteraction` (steep emerged terrain as a virtual wall).

use std::collections::HashSet;

use crate::systems::skills::controllers::Vec3;

use super::harpoon;
use super::ship::{PhysShip, HALF_PI};
use super::PhysEnv;

/// `ShipHullPairDefaults`.
pub mod pair {
    pub const HULL_DETECT_INFLATE_LENGTH: f32 = 1.025;
    pub const HULL_DETECT_INFLATE_BEAM: f32 = 1.015;
    pub const BEAM_DETECT_TIGHTEN_MUL: f32 = 0.78;
    pub const MIN_PENETRATION_TO_ACT: f32 = 0.055;
    pub const MIN_PENETRATION_TO_DAMAGE: f32 = MIN_PENETRATION_TO_ACT * 1.15;
    pub const TANGENTIAL_RAMP_DEPTH_METERS: f32 = 0.22;
    pub const SEPARATION_PUSH_MULTIPLIER: f32 = 1.22;
    pub const SEPARATION_SLACK_METERS: f32 = 0.020;
    pub const CLOSING_SPEED_DAMP: f32 = 1.0;
    pub const TANGENTIAL_SLIP_DAMP: f32 = 0.86;
    pub const MIN_VERTICAL_OVERLAP: f32 = 0.12;
    pub const RESOLVE_PASSES: usize = 2;
    pub const MAX_PAIR_ITERATIONS: usize = 12;
    pub const DEEP_PENETRATION_START: f32 = 0.12;
    pub const DEEP_PENETRATION_BOOST: f32 = 0.72;
    pub const MIN_HALF_SEPARATION_METERS: f32 = 0.02;
    pub const MIN_LINEAR_SEPARATION_TO_APPLY_METERS: f32 = 0.018;
    pub const NOSE_CONTACT_COS_THRESHOLD: f32 = 0.65;
    pub const HULL_COLLISION_DAMAGE_COOLDOWN_SEC: f32 = 1.5;
    pub const HULL_DAMAGE_LOW_SPEED_THRESHOLD_MPS: f32 = 2.0;
    pub const HULL_DAMAGE_SPEED_INTERP_MAX_MPS: f32 = 10.0;
    pub const HULL_DAMAGE_SPEED_SCALED_MIN_PERCENT: i32 = 1;
    pub const HULL_DAMAGE_SPEED_SCALED_MAX_PERCENT: i32 = 10;
    pub const MASS_PUSH_STRENGTH: f32 = 2.0;
}

/// A hull damage request (`ApplyShipHullCollisionDamage` / `ApplyFloorCollisionDamageImmediate`):
/// `percent` of the target's max HP, dealt by `attacker` (the target itself for environment damage).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct HullDamage {
    pub target: u32,
    pub attacker: u32,
    pub percent: i32,
}

/// `TryObbXzMinPenetration`: 2D SAT of two bow-aligned rectangles; the smallest overlap and its
/// axis.
#[allow(clippy::too_many_arguments)]
pub fn obb_xz_min_penetration(
    ax: f32,
    az: f32,
    bow_a: f32,
    half_len_a: f32,
    half_wid_a: f32,
    bx: f32,
    bz: f32,
    bow_b: f32,
    half_len_b: f32,
    half_wid_b: f32,
) -> Option<(f32, f32, f32)> {
    let axes = [
        (bow_a.cos(), bow_a.sin()),
        (-bow_a.sin(), bow_a.cos()),
        (bow_b.cos(), bow_b.sin()),
        (-bow_b.sin(), bow_b.cos()),
    ];
    let mut found = false;
    let mut min_o = f32::MAX;
    let (mut best_nx, mut best_nz) = (1.0, 0.0);
    for (ux, uz) in axes {
        let len = (ux * ux + uz * uz).sqrt();
        if len < 1e-6 {
            continue;
        }
        let (nx, nz) = (ux / len, uz / len);
        let c_a = ax * nx + az * nz;
        let c_b = bx * nx + bz * nz;
        let r_a = project_obb_radius_xz(half_len_a, half_wid_a, bow_a, nx, nz);
        let r_b = project_obb_radius_xz(half_len_b, half_wid_b, bow_b, nx, nz);
        let overlap = (c_a + r_a).min(c_b + r_b) - (c_a - r_a).max(c_b - r_b);
        if overlap <= 0.0 {
            return None;
        }
        if overlap < min_o {
            min_o = overlap;
            best_nx = nx;
            best_nz = nz;
            found = true;
        }
    }
    found.then_some((min_o, best_nx, best_nz))
}

/// `ProjectObbRadiusXz`.
pub fn project_obb_radius_xz(half_len: f32, half_wid: f32, bow: f32, nx: f32, nz: f32) -> f32 {
    let d1 = bow.cos() * nx + bow.sin() * nz;
    let d2 = -bow.sin() * nx + bow.cos() * nz;
    half_len * d1.abs() + half_wid * d2.abs()
}

/// `GetPairMassWeights`.
fn pair_mass_weights(mass_a: f32, mass_b: f32) -> (f32, f32) {
    let sum = mass_a + mass_b;
    if sum < 1e-6 || mass_a <= 0.0 || mass_b <= 0.0 {
        return (0.5, 0.5);
    }
    let inv = 1.0 / sum;
    let w_phys_a = mass_b * inv;
    let t = pair::MASS_PUSH_STRENGTH;
    let eps = 1e-4;
    let wa = (0.5 + (w_phys_a - 0.5) * t).clamp(eps, 1.0 - eps);
    (wa, 1.0 - wa)
}

/// `IsOtherShipInNoseCone` (public for the CryPhysics collision damage).
pub fn nose_cone(to_x: f32, to_z: f32, bow: f32) -> bool {
    is_other_ship_in_nose_cone(to_x, to_z, bow)
}

fn is_other_ship_in_nose_cone(to_x: f32, to_z: f32, bow: f32) -> bool {
    let len_sq = to_x * to_x + to_z * to_z;
    if len_sq < 0.04 {
        return false;
    }
    let inv = 1.0 / len_sq.sqrt();
    let cos = (to_x * bow.cos() + to_z * bow.sin()) * inv;
    cos >= pair::NOSE_CONTACT_COS_THRESHOLD
}

/// `GetSpeedScaledHullDamagePercent`.
pub fn speed_scaled_hull_damage_percent(speed: f32) -> i32 {
    use pair::*;
    if speed <= HULL_DAMAGE_LOW_SPEED_THRESHOLD_MPS {
        return HULL_DAMAGE_SPEED_SCALED_MIN_PERCENT;
    }
    if speed >= HULL_DAMAGE_SPEED_INTERP_MAX_MPS {
        return HULL_DAMAGE_SPEED_SCALED_MAX_PERCENT;
    }
    let span = HULL_DAMAGE_SPEED_INTERP_MAX_MPS - HULL_DAMAGE_LOW_SPEED_THRESHOLD_MPS;
    let t = (speed - HULL_DAMAGE_LOW_SPEED_THRESHOLD_MPS) / span;
    let f = HULL_DAMAGE_SPEED_SCALED_MIN_PERCENT as f32
        + t * (HULL_DAMAGE_SPEED_SCALED_MAX_PERCENT - HULL_DAMAGE_SPEED_SCALED_MIN_PERCENT) as f32;
    f.round_ties_even() as i32
}

fn aabb_overlap_xz(a: &super::jitter::Aabb, b: &super::jitter::Aabb) -> bool {
    let ox = a.max.x.min(b.max.x) - a.min.x.max(b.min.x);
    let oz = a.max.z.min(b.max.z) - a.min.z.max(b.min.z);
    ox > 0.0 && oz > 0.0
}

fn two_mut<T>(v: &mut [T], i: usize, j: usize) -> (&mut T, &mut T) {
    debug_assert!(i < j);
    let (a, b) = v.split_at_mut(j);
    (&mut a[i], &mut b[0])
}

/// `ShipShipInteraction.ResolveAllPairs(ships, dt)`: overlap resolution over all pairs, then the
/// hull damage of the hulls that really touched (once per pair per tick, per-ship cooldowns).
pub fn resolve_all_pairs(ships: &mut [&mut PhysShip], dt: f32, damage: &mut Vec<HullDamage>) {
    if ships.len() < 2 {
        return;
    }
    if dt > 0.0 {
        for s in ships.iter_mut() {
            s.hull_collision_cooldowns.retain(|_, v| {
                *v -= dt;
                *v > 0.0
            });
        }
    }
    let links = harpoon::ship_links(ships);
    let mut damaged: HashSet<u64> = HashSet::new();
    for _ in 0..pair::RESOLVE_PASSES {
        for i in 0..ships.len() {
            for j in i + 1..ships.len() {
                let (sa, sb) = two_mut(ships, i, j);
                let linked = links.contains(&(
                    sa.spec.obj_id.min(sb.spec.obj_id),
                    sa.spec.obj_id.max(sb.spec.obj_id),
                ));
                let Some((impact, max_pen)) = try_resolve_pair(sa, sb, linked) else {
                    continue;
                };
                if max_pen < pair::MIN_PENETRATION_TO_DAMAGE {
                    continue;
                }
                sa.replication.hold(10);
                sb.replication.hold(10);
                let (min_id, max_id) = if sa.spec.slave_id < sb.spec.slave_id {
                    (sa.spec.slave_id, sb.spec.slave_id)
                } else {
                    (sb.spec.slave_id, sa.spec.slave_id)
                };
                if !damaged.insert((u64::from(min_id) << 32) | u64::from(max_id)) {
                    continue;
                }
                apply_pair_hull_damage(sa, sb, impact, damage);
            }
        }
    }
    for s in ships.iter_mut() {
        s.sync_speed_from_bow_velocity();
    }
}

fn apply_pair_hull_damage(
    sa: &mut PhysShip,
    sb: &mut PhysShip,
    impact: f32,
    out: &mut Vec<HullDamage>,
) {
    let bow_a = sa.bow();
    let bow_b = sb.bow();
    let (ax, az) = sa.mass_box_center_xz();
    let (bx, bz) = sb.mass_box_center_xz();
    let scaled = speed_scaled_hull_damage_percent(impact);
    let a_nose = is_other_ship_in_nose_cone(bx - ax, bz - az, bow_a);
    let b_nose = is_other_ship_in_nose_cone(ax - bx, az - bz, bow_b);
    let dmg_a = if a_nose {
        pair::HULL_DAMAGE_SPEED_SCALED_MIN_PERCENT
    } else {
        scaled
    };
    let dmg_b = if b_nose {
        pair::HULL_DAMAGE_SPEED_SCALED_MIN_PERCENT
    } else {
        scaled
    };
    if sa
        .hull_collision_cooldowns
        .get(&sb.spec.slave_id)
        .is_none_or(|cd| *cd <= 0.0)
    {
        out.push(HullDamage {
            target: sa.spec.obj_id,
            attacker: sb.spec.obj_id,
            percent: dmg_a,
        });
        sa.hull_collision_cooldowns
            .insert(sb.spec.slave_id, pair::HULL_COLLISION_DAMAGE_COOLDOWN_SEC);
    }
    if sb
        .hull_collision_cooldowns
        .get(&sa.spec.slave_id)
        .is_none_or(|cd| *cd <= 0.0)
    {
        out.push(HullDamage {
            target: sb.spec.obj_id,
            attacker: sa.spec.obj_id,
            percent: dmg_b,
        });
        sb.hull_collision_cooldowns
            .insert(sa.spec.slave_id, pair::HULL_COLLISION_DAMAGE_COOLDOWN_SEC);
    }
}

/// `TryResolvePair`: `Some((peak impact speed, max penetration))` when the hulls were pushed apart.
fn try_resolve_pair(
    sa: &mut PhysShip,
    sb: &mut PhysShip,
    skip_vel_damp: bool,
) -> Option<(f32, f32)> {
    use pair::*;
    let bb_a = sa.body.world_aabb();
    let bb_b = sb.body.world_aabb();
    if !aabb_overlap_xz(&bb_a, &bb_b) {
        return None;
    }
    let overlap_y = bb_a.max.y.min(bb_b.max.y) - bb_a.min.y.max(bb_b.min.y);
    if overlap_y < MIN_VERTICAL_OVERLAP {
        return None;
    }
    let bow_a = sa.bow();
    let bow_b = sb.bow();
    let half_len_a = sa.model.mass_box_size_y * sa.spec.scale * 0.5 * HULL_DETECT_INFLATE_LENGTH;
    let half_len_b = sb.model.mass_box_size_y * sb.spec.scale * 0.5 * HULL_DETECT_INFLATE_LENGTH;
    let wid_a = sa.model.mass_box_size_x
        * sa.spec.scale
        * 0.5
        * HULL_DETECT_INFLATE_BEAM
        * BEAM_DETECT_TIGHTEN_MUL;
    let wid_b = sb.model.mass_box_size_x
        * sb.spec.scale
        * 0.5
        * HULL_DETECT_INFLATE_BEAM
        * BEAM_DETECT_TIGHTEN_MUL;

    let mut had = false;
    let mut peak = 0f32;
    let mut max_pen = 0f32;
    for _ in 0..MAX_PAIR_ITERATIONS {
        let bb_a = sa.body.world_aabb();
        let bb_b = sb.body.world_aabb();
        if !aabb_overlap_xz(&bb_a, &bb_b) {
            break;
        }
        let overlap_y = bb_a.max.y.min(bb_b.max.y) - bb_a.min.y.max(bb_b.min.y);
        if overlap_y < MIN_VERTICAL_OVERLAP {
            break;
        }
        let (ax, az) = sa.mass_box_center_xz();
        let (bx, bz) = sb.mass_box_center_xz();
        let Some((pen, mut nx, mut nz)) = obb_xz_min_penetration(
            ax, az, bow_a, half_len_a, wid_a, bx, bz, bow_b, half_len_b, wid_b,
        ) else {
            break;
        };
        if pen <= 1e-4 || pen < MIN_PENETRATION_TO_ACT {
            break;
        }
        max_pen = max_pen.max(pen);
        let (dx, dz) = (bx - ax, bz - az);
        if nx * dx + nz * dz < 0.0 {
            nx = -nx;
            nz = -nz;
        }
        let mut half_sep = pen * 0.5;
        let deep = 0f32.max(pen - DEEP_PENETRATION_START);
        half_sep *= 1.0 + DEEP_PENETRATION_BOOST * 1.25f32.min(deep);
        let linear_sep = half_sep + SEPARATION_SLACK_METERS;
        if linear_sep < MIN_LINEAR_SEPARATION_TO_APPLY_METERS {
            break;
        }
        let mut mv = linear_sep.max(MIN_HALF_SEPARATION_METERS);
        mv *= SEPARATION_PUSH_MULTIPLIER;
        let va = sa.body.velocity;
        let vb = sb.body.velocity;
        let rel = (va.x - vb.x) * nx + (va.z - vb.z) * nz;
        peak = peak.max(rel.abs());
        let (wa, wb) = pair_mass_weights(sa.body.mass(), sb.body.mass());
        let move_a = 2.0 * mv * wa;
        let move_b = 2.0 * mv * wb;
        sa.body.position -= Vec3::new(nx * move_a, 0.0, nz * move_a);
        sb.body.position += Vec3::new(nx * move_b, 0.0, nz * move_b);
        had = true;
        if !skip_vel_damp {
            damp_pair_velocities(sa, sb, nx, nz, pen, wa, wb);
        }
    }
    had.then_some((peak, max_pen))
}

fn damp_pair_velocities(
    sa: &mut PhysShip,
    sb: &mut PhysShip,
    nx: f32,
    nz: f32,
    pen: f32,
    wa: f32,
    wb: f32,
) {
    use pair::*;
    let mut va = sa.body.velocity;
    let mut vb = sb.body.velocity;
    let closing = (va.x - vb.x) * nx + (va.z - vb.z) * nz;
    if closing > 0.0 {
        let ra = closing * wa * CLOSING_SPEED_DAMP;
        let rb = closing * wb * CLOSING_SPEED_DAMP;
        va = Vec3::new(va.x - nx * ra, va.y, va.z - nz * ra);
        vb = Vec3::new(vb.x + nx * rb, vb.y, vb.z + nz * rb);
    }
    let blend = ((pen - MIN_PENETRATION_TO_ACT) / TANGENTIAL_RAMP_DEPTH_METERS).clamp(0.0, 1.0);
    let (tx, tz) = (-nz, nx);
    let rel_t = (va.x - vb.x) * tx + (va.z - vb.z) * tz;
    let slip_k = (1.0 - TANGENTIAL_SLIP_DAMP) * blend;
    let slip_a = rel_t * wa * slip_k;
    let slip_b = rel_t * wb * slip_k;
    va = Vec3::new(va.x - tx * slip_a, va.y, va.z - tz * slip_a);
    vb = Vec3::new(vb.x + tx * slip_b, vb.y, vb.z + tz * slip_b);
    sa.body.velocity = va;
    sb.body.velocity = vb;
}

/// `ShipStaticObstacleContact` constants.
pub mod static_contact {
    pub const DEFAULT_REPLICATION_CONTACT_HOLD_TICKS: u8 = 8;
    pub const BRIDGE_CEILING_REPLICATION_CONTACT_HOLD_TICKS: u8 = 24;
    pub const MIN_PENETRATION_METERS_FOR_ENV_HULL_DAMAGE: f32 = 0.015;
}

/// `ShipStaticObstacleContact.ApplySeparationAndSurfaceDampXz`.
pub fn apply_separation_and_surface_damp_xz(
    ship: &mut PhysShip,
    nx: f32,
    nz: f32,
    move_meters: f32,
    closing_speed_damp: f32,
    tangential_slip_damp: f32,
    hold_ticks: u8,
) {
    ship.body.position -= Vec3::new(nx * move_meters, 0.0, nz * move_meters);
    let mut va = ship.body.velocity;
    let closing = va.x * nx + va.z * nz;
    if closing > 0.0 {
        va = Vec3::new(
            va.x - nx * closing * closing_speed_damp,
            va.y,
            va.z - nz * closing * closing_speed_damp,
        );
    }
    let (tx, tz) = (-nz, nx);
    let rel_t = va.x * tx + va.z * tz;
    let slip = rel_t * (1.0 - tangential_slip_damp);
    va = Vec3::new(va.x - tx * slip, va.y, va.z - tz * slip);
    ship.body.velocity = va;
    if hold_ticks > 0 {
        ship.replication.hold(hold_ticks);
    }
}

/// A doodad near a ship (`WorldManager.GetAround<Doodad>(ship, r)` with the template flags).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct DoodadObstacle {
    /// World position (game axes) and yaw.
    pub pos: Vec3,
    pub rot_z: f32,
    pub scale: f32,
    /// `Template.SimRadius` / `CollideShip` / `NoCollision` (`None` = no template).
    pub sim_radius: i32,
    pub collide_ship: bool,
    pub no_collision: bool,
    pub has_template: bool,
    /// `doodad.ParentWorld?.Id == world.Id`.
    pub same_world: bool,
}

/// `DoodadObstacleDefaults`.
pub mod doodad {
    pub const DEFAULT_FOOTPRINT_HALF_EXTENT_METERS: f32 = 3.5;
    pub const SIM_RADIUS_CM_TO_METERS: f32 = 0.01;
    pub const VERTICAL_HALF_HEIGHT_METERS: f32 = 14.0;
    pub const QUERY_RADIUS_PADDING_METERS: f32 = 28.0;
    pub const RESOLVE_PASSES: usize = 2;
    pub const MAX_PAIR_ITERATIONS: usize = 10;
    pub const MIN_PENETRATION_TO_ACT: f32 = 0.05;
    pub const MIN_VERTICAL_OVERLAP: f32 = 0.12;
    pub const SEPARATION_PUSH_MULTIPLIER: f32 = 1.18;
    pub const SEPARATION_SLACK_METERS: f32 = 0.025;
    pub const CLOSING_SPEED_DAMP: f32 = 1.0;
    pub const TANGENTIAL_SLIP_DAMP: f32 = 0.82;
}

/// The doodad query radius around a ship.
pub fn doodad_query_radius(ship: &PhysShip) -> f32 {
    let half_len = ship.model.mass_box_size_y * ship.spec.scale * 0.5;
    let half_beam = ship.model.mass_box_size_x * ship.spec.scale * 0.5;
    half_len
        + half_beam
        + doodad::QUERY_RADIUS_PADDING_METERS
        + doodad::DEFAULT_FOOTPRINT_HALF_EXTENT_METERS * 2.0
}

fn obstacle_footprint_half_extent(d: &DoodadObstacle) -> f32 {
    if d.sim_radius <= 0 {
        return doodad::DEFAULT_FOOTPRINT_HALF_EXTENT_METERS * d.scale;
    }
    let meters = d.sim_radius as f32 * doodad::SIM_RADIUS_CM_TO_METERS;
    meters.max(1.2) * d.scale
}

/// `ShipDoodadInteraction.ResolveAll` for one ship (its nearby doodads), then the speed resync.
pub fn resolve_ship_vs_doodads(ship: &mut PhysShip, doodads: &[DoodadObstacle]) {
    if ship.view.in_region {
        for _ in 0..doodad::RESOLVE_PASSES {
            for d in doodads {
                if !d.same_world || !d.has_template || !d.collide_ship || d.no_collision {
                    continue;
                }
                try_resolve_ship_vs_doodad(ship, d);
            }
        }
    }
}

fn hull_sat_extents(ship: &PhysShip) -> (f32, f32) {
    let half_len =
        ship.model.mass_box_size_y * ship.spec.scale * 0.5 * pair::HULL_DETECT_INFLATE_LENGTH;
    let half_wid = ship.model.mass_box_size_x
        * ship.spec.scale
        * 0.5
        * pair::HULL_DETECT_INFLATE_BEAM
        * pair::BEAM_DETECT_TIGHTEN_MUL;
    (half_len, half_wid)
}

fn try_resolve_ship_vs_doodad(ship: &mut PhysShip, d: &DoodadObstacle) -> bool {
    use doodad::*;
    let half_h = VERTICAL_HALF_HEIGHT_METERS * d.scale;
    let (d_min_y, d_max_y) = (d.pos.z - half_h, d.pos.z + half_h);
    let bb = ship.body.world_aabb();
    if bb.max.y.min(d_max_y) - bb.min.y.max(d_min_y) < MIN_VERTICAL_OVERLAP {
        return false;
    }
    let r = obstacle_footprint_half_extent(d);
    let bow_b = d.rot_z + HALF_PI;
    let (bx, bz) = (d.pos.x, d.pos.y);
    let bow_a = ship.bow();
    let (half_len_a, wid_a) = hull_sat_extents(ship);
    let mut had = false;
    for _ in 0..MAX_PAIR_ITERATIONS {
        let bb = ship.body.world_aabb();
        if bb.max.y.min(d_max_y) - bb.min.y.max(d_min_y) < MIN_VERTICAL_OVERLAP {
            break;
        }
        let (ax, az) = ship.mass_box_center_xz();
        let Some((pen, mut nx, mut nz)) =
            obb_xz_min_penetration(ax, az, bow_a, half_len_a, wid_a, bx, bz, bow_b, r, r)
        else {
            break;
        };
        if pen >= static_contact::MIN_PENETRATION_METERS_FOR_ENV_HULL_DAMAGE {
            ship.static_obstacle_hull_damage_contact_active = true;
        }
        if pen < MIN_PENETRATION_TO_ACT {
            break;
        }
        if nx * (bx - ax) + nz * (bz - az) < 0.0 {
            nx = -nx;
            nz = -nz;
        }
        let mv = pen * SEPARATION_PUSH_MULTIPLIER + SEPARATION_SLACK_METERS;
        apply_separation_and_surface_damp_xz(
            ship,
            nx,
            nz,
            mv,
            CLOSING_SPEED_DAMP,
            TANGENTIAL_SLIP_DAMP,
            static_contact::DEFAULT_REPLICATION_CONTACT_HOLD_TICKS,
        );
        had = true;
    }
    had
}

/// `CliffObstacleDefaults`.
pub mod cliff {
    pub const GRADIENT_SAMPLE_METERS: f32 = 3.25;
    pub const MIN_GRADIENT_MAGNITUDE: f32 = 1.05;
    pub const MIN_ROCK_HEIGHT_ABOVE_WATER_METERS: f32 = 0.08;
    pub const EMERGED_ROCK_PROBE_MAX_DISTANCE_METERS: f32 = 14.0;
    pub const MAX_GRADIENT_MAGNITUDE_SANITY: f32 = 6.0;
    pub const WALL_HALF_THICKNESS_METERS: f32 = 0.45;
    pub const WALL_LEAD_BEYOND_EMERGED_PROBE_METERS: f32 = 1.15;
    pub const WALL_ALONG_ASCENT_MIN_METERS: f32 = 3.5;
    pub const VERTICAL_HALF_HEIGHT_METERS: f32 = 22.0;
    pub const MAX_PAIR_ITERATIONS: usize = 3;
    pub const MIN_PENETRATION_TO_ACT: f32 = 0.11;
    pub const MIN_VERTICAL_OVERLAP: f32 = 0.12;
    pub const MAX_SEPARATION_STEP_METERS_PER_ITERATION: f32 = 0.095;
    pub const SEPARATION_PUSH_MULTIPLIER: f32 = 0.99;
    pub const OVERLAP_RESOLVE_FRACTION_PER_ITERATION: f32 = 0.48;
    pub const SEPARATION_SLACK_METERS: f32 = 0.002;
    pub const CLOSING_SPEED_DAMP: f32 = 0.14;
    pub const TANGENTIAL_SLIP_DAMP: f32 = 0.97;
    pub const ABOVE_WATERLINE_SKIP_METERS: f32 = 0.18;
}

fn first_above_water_rock_uphill(
    env: &dyn PhysEnv,
    cx: f32,
    cz: f32,
    ax: f32,
    az: f32,
    body_y: f32,
) -> Option<f32> {
    for s in [2.0f32, 3.5, 5.5, 7.5, 10.0, 12.5, 14.0] {
        if s > cliff::EMERGED_ROCK_PROBE_MAX_DISTANCE_METERS {
            break;
        }
        let (sx, sz) = (cx + ax * s, cz + az * s);
        let hf = env.height(sx, sz);
        let wf = env.water_surface(Vec3::new(sx, sz, body_y)).0;
        if hf >= wf + cliff::MIN_ROCK_HEIGHT_ABOVE_WATER_METERS {
            return Some(s);
        }
    }
    None
}

/// `ShipCliffInteraction.TryResolveShipVsSteepTerrain` (one ship; the caller resyncs the speed).
pub fn resolve_ship_vs_steep_terrain(ship: &mut PhysShip, env: &dyn PhysEnv) {
    use cliff::*;
    if !ship.view.in_region {
        return;
    }
    let p = ship.body.position;
    let water_at_center = env.water_surface(Vec3::new(p.x, p.z, p.y)).0;
    if p.y > water_at_center + ABOVE_WATERLINE_SKIP_METERS {
        return;
    }
    let (cx, cz) = ship.mass_box_center_xz();
    let d = GRADIENT_SAMPLE_METERS;
    let hxp = env.height(cx + d, cz);
    let hxm = env.height(cx - d, cz);
    let hzp = env.height(cx, cz + d);
    let hzm = env.height(cx, cz - d);
    let gx = (hxp - hxm) / (2.0 * d);
    let gz = (hzp - hzm) / (2.0 * d);
    let g_len_sq = gx * gx + gz * gz;
    if g_len_sq < MIN_GRADIENT_MAGNITUDE * MIN_GRADIENT_MAGNITUDE {
        return;
    }
    let g_len = g_len_sq.sqrt();
    if g_len > MAX_GRADIENT_MAGNITUDE_SANITY {
        return;
    }
    let (ax, az) = (gx / g_len, gz / g_len);
    let Some(emerged) = first_above_water_rock_uphill(env, cx, cz, ax, az, p.y) else {
        return;
    };
    let wall_along = (emerged + WALL_LEAD_BEYOND_EMERGED_PROBE_METERS).clamp(
        WALL_ALONG_ASCENT_MIN_METERS,
        EMERGED_ROCK_PROBE_MAX_DISTANCE_METERS,
    );
    let (bx, bz) = (cx + ax * wall_along, cz + az * wall_along);
    let floor_at_wall = env.height(bx, bz);
    let water_at_wall = env.water_surface(Vec3::new(bx, bz, p.y)).0;
    if floor_at_wall < water_at_wall + MIN_ROCK_HEIGHT_ABOVE_WATER_METERS {
        return;
    }
    let bow_a = ship.bow();
    let (half_len_a, wid_a) = hull_sat_extents(ship);
    let (d_min_y, d_max_y) = (
        floor_at_wall - VERTICAL_HALF_HEIGHT_METERS,
        floor_at_wall + VERTICAL_HALF_HEIGHT_METERS,
    );
    let r_b = WALL_HALF_THICKNESS_METERS;
    for _ in 0..MAX_PAIR_ITERATIONS {
        let bb = ship.body.world_aabb();
        if bb.max.y.min(d_max_y) - bb.min.y.max(d_min_y) < MIN_VERTICAL_OVERLAP {
            break;
        }
        let (acx, acz) = ship.mass_box_center_xz();
        let (nx, nz) = (ax, az);
        let c_a = acx * nx + acz * nz;
        let c_b = bx * nx + bz * nz;
        let r_a = project_obb_radius_xz(half_len_a, wid_a, bow_a, nx, nz);
        let pen = (c_a + r_a).min(c_b + r_b) - (c_a - r_a).max(c_b - r_b);
        if pen >= static_contact::MIN_PENETRATION_METERS_FOR_ENV_HULL_DAMAGE {
            ship.static_obstacle_hull_damage_contact_active = true;
        }
        if pen < MIN_PENETRATION_TO_ACT {
            break;
        }
        let mv = ((pen * SEPARATION_PUSH_MULTIPLIER + SEPARATION_SLACK_METERS)
            * OVERLAP_RESOLVE_FRACTION_PER_ITERATION)
            .min(MAX_SEPARATION_STEP_METERS_PER_ITERATION);
        apply_separation_and_surface_damp_xz(
            ship,
            nx,
            nz,
            mv,
            CLOSING_SPEED_DAMP,
            TANGENTIAL_SLIP_DAMP,
            static_contact::DEFAULT_REPLICATION_CONTACT_HOLD_TICKS,
        );
    }
}

#[cfg(test)]
mod tests {
    use super::super::ship::tests::{model, spec};
    use super::super::ship::{PhysShip, ShipGameView};
    use super::super::tests::FakeEnv;
    use super::*;

    #[test]
    fn sat_finds_the_smallest_overlap_axis() {
        // two axis-aligned 10x4 rectangles (bow along +X), 3 m apart sideways: overlap 1 m on Z
        let r = obb_xz_min_penetration(0.0, 0.0, 0.0, 5.0, 2.0, 0.0, 3.0, 0.0, 5.0, 2.0);
        let (pen, nx, nz) = r.expect("overlap");
        assert!((pen - 1.0).abs() < 1e-5 && nx.abs() < 1e-6 && (nz.abs() - 1.0).abs() < 1e-6);
        assert!(obb_xz_min_penetration(0.0, 0.0, 0.0, 5.0, 2.0, 0.0, 4.5, 0.0, 5.0, 2.0).is_none());
        assert_eq!(speed_scaled_hull_damage_percent(1.0), 1);
        assert_eq!(speed_scaled_hull_damage_percent(6.0), 6); // 1 + 0.5 * 9 = 5.5 -> 6 (even)
        assert_eq!(speed_scaled_hull_damage_percent(20.0), 10);
    }

    #[test]
    fn overlapping_hulls_are_pushed_apart_and_damaged_once() {
        let mut a = PhysShip::new(
            spec(1, 0.0, 0.0, 99.0, 0.0),
            model(),
            ShipGameView::default(),
        );
        let mut b = PhysShip::new(
            spec(2, 2.0, 0.0, 99.0, 0.0),
            model(),
            ShipGameView::default(),
        );
        a.body.velocity = Vec3::new(3.0, 0.0, 0.0);
        let mut dmg = Vec::new();
        resolve_all_pairs(&mut [&mut a, &mut b], 0.04, &mut dmg);
        assert!(a.body.position.x < 0.0 && b.body.position.x > 2.0);
        assert_eq!(dmg.len(), 2);
        // impact speed 3 m/s -> 1 + (3 - 2) / 8 * 9 = 2.125 -> 2%; b is not in a's nose cone
        assert_eq!(
            dmg[0],
            HullDamage {
                target: 1,
                attacker: 2,
                percent: 2
            }
        );
        // cooldown: no damage on the next overlap
        a.body.position = Vec3::new(0.0, 99.0, 0.0);
        b.body.position = Vec3::new(2.0, 99.0, 0.0);
        let mut dmg = Vec::new();
        resolve_all_pairs(&mut [&mut a, &mut b], 0.04, &mut dmg);
        assert!(dmg.is_empty());
        assert!(a.replication.contact_hold_ticks >= 10);
    }

    #[test]
    fn collide_ship_doodads_push_the_hull_out() {
        let mut s = PhysShip::new(
            spec(1, 0.0, 0.0, 99.0, 0.0),
            model(),
            ShipGameView::default(),
        );
        let pier = DoodadObstacle {
            pos: Vec3::new(4.0, 0.0, 99.0),
            rot_z: 0.0,
            scale: 1.0,
            sim_radius: 0,
            collide_ship: true,
            no_collision: false,
            has_template: true,
            same_world: true,
        };
        resolve_ship_vs_doodads(&mut s, &[pier]);
        assert!(s.body.position.x < 0.0);
        assert!(s.static_obstacle_hull_damage_contact_active);
        let mut t = PhysShip::new(
            spec(2, 0.0, 0.0, 99.0, 0.0),
            model(),
            ShipGameView::default(),
        );
        resolve_ship_vs_doodads(
            &mut t,
            &[DoodadObstacle {
                collide_ship: false,
                ..pier
            }],
        );
        assert_eq!(t.body.position.x, 0.0);
    }

    #[test]
    fn steep_emerged_rock_acts_as_a_wall() {
        // a slope (2 m / m) emerging from the water east of x = 8
        let env = FakeEnv {
            ocean: 100.0,
            height: Box::new(|x, _y| 90.0 + (x - 3.0) * 2.0),
        };
        let mut s = PhysShip::new(
            spec(1, 3.0, 0.0, 99.5, std::f32::consts::FRAC_PI_2),
            model(),
            ShipGameView::default(),
        );
        let before = s.body.position.x;
        resolve_ship_vs_steep_terrain(&mut s, &env);
        assert!(
            s.body.position.x < before,
            "{} -> {}",
            before,
            s.body.position.x
        );
    }
}
