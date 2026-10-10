//! `Physics/ShipHarpoonTowPhysics`: the tow of a hull by its ship-harpoon line (hook on dry
//! land: pull toward the hook; hook on another boat: mass-weighted closing impulses on both
//! hulls), the "taut line" test of `ShipController.HasTautHarpoonEngaged` and the ship–ship
//! link test used to skip overlap damping.
//!
//! The rope state itself (`ShipHarpoonRopeState`, `ShipHarpoonRopeController`: launch, cut,
//! tension tear, controller lifetime, hook position) belongs to the slaves subsystem; it hands
//! the lines of a hull's attached slaves to the physics each tick
//! ([`super::ShipPhysicsHooks::harpoon_lines`]).

use std::collections::HashSet;

use crate::systems::ai::numerics::Vec3Ext;
use crate::systems::skills::controllers::Vec3;

use super::ship::PhysShip;

/// One harpoon cannon slave below a hull (depth-first over `AttachedSlaves`).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct HarpoonLine {
    pub child_obj_id: u32,
    /// The cannon is a direct child of the hull (`hull.AttachedSlaves`).
    pub direct: bool,
    /// `child.Transform.World.Position` (game axes).
    pub cannon_pos: Vec3,
    /// `ShipHarpoonRopeController.GetHookWorldPosition(child)` (game axes).
    pub hook_pos: Vec3,
    /// `HarpoonRope.IsEngaged`.
    pub engaged: bool,
    /// `HarpoonRope.HookBasisObjId`.
    pub hook_basis_obj_id: u32,
    /// `HarpoonRope.RopeLength`.
    pub rope_length: f32,
    /// `HarpoonRope.HookAttachedToTerrain`.
    pub hook_attached_to_terrain: bool,
}

pub const TOW_ACCEL_PER_METER_STRETCH: f32 = 1.0;
pub const TOW_MAX_ACCEL: f32 = 2.0;
pub const SLACK_MARGIN_METERS: f32 = 0.5;
pub const MIN_HOOK_HORIZONTAL_DISTANCE: f32 = 0.2;
pub const SERVER_ROPE_PAID_LENGTH_ADDITIVE_METERS: f32 = 12.5;
pub const TOW_YAW_ASSIST_RAD_PER_SEC: f32 = 0.3;
pub const SHIP_PAIR_DOMINANCE_SPEED_COEFF: f32 = 0.04;
pub const SHIP_PAIR_DOMINANT_TOW_HULL_MUL: f32 = 1.12;
pub const SHIP_PAIR_DOMINANT_BASIS_HULL_MUL: f32 = 1.12;
pub const SHIP_PAIR_EXTRA_SLACK_MARGIN_METERS: f32 = 22.0;
pub const SHIP_PAIR_MIN_STRETCH_METERS: f32 = 0.12;
pub const SHIP_PAIR_MAX_GUESS_BASIS_DISTANCE_METERS: f32 = 45.0;
pub const SHIP_PAIR_MAX_CHORD_OVER_PAID_METERS: f32 = 12.0;
pub const SHIP_PAIR_BOW_YAW_MASS_SCALE_REFERENCE: f32 = 2000.0;
pub const SHIP_PAIR_BOW_YAW_MASS_SCALE_MIN: f32 = 0.2;

/// `ShipController.HasTautHarpoonEngaged(hull)` (direct children only).
pub fn has_taut_harpoon_engaged(lines: &[HarpoonLine]) -> bool {
    lines.iter().filter(|l| l.direct && l.engaged).any(|l| {
        let dist = l.cannon_pos.distance(l.hook_pos);
        let paid = l.rope_length + SERVER_ROPE_PAID_LENGTH_ADDITIVE_METERS;
        paid <= dist + SLACK_MARGIN_METERS
    })
}

/// `ApplyTerrainHookTow(hull, rb, dt, bow, speedToAlongVelScale, maxForward, maxBackward)`.
pub fn apply_terrain_hook_tow(
    ship: &mut PhysShip,
    dt: f32,
    bow_x: f32,
    bow_z: f32,
    speed_to_along_vel_scale: f32,
    max_forward_speed: f32,
    max_backward_speed: f32,
) {
    if ship.harpoon_lines.is_empty() || dt <= 0.0 {
        return;
    }
    let (mut sx, mut sz) = (0f32, 0f32);
    let p = ship.body.position;
    for l in &ship.harpoon_lines {
        if !l.engaged || !l.hook_attached_to_terrain {
            continue;
        }
        let dist = l.cannon_pos.distance(l.hook_pos);
        let paid = l.rope_length + SERVER_ROPE_PAID_LENGTH_ADDITIVE_METERS;
        if paid > dist + SLACK_MARGIN_METERS {
            continue;
        }
        let stretch = 0f32.max(dist - paid);
        let accel = TOW_MAX_ACCEL.min(TOW_ACCEL_PER_METER_STRETCH * stretch);
        if accel <= 0.0 {
            continue;
        }
        let (dx, dz) = (l.hook_pos.x - p.x, l.hook_pos.y - p.z);
        let len = (dx * dx + dz * dz).sqrt();
        if len < MIN_HOOK_HORIZONTAL_DISTANCE {
            continue;
        }
        let imp = accel * dt;
        sx += dx / len * imp;
        sz += dz / len * imp;
    }
    if sx * sx + sz * sz < 1e-10 {
        return;
    }
    let vx = ship.body.velocity.x + sx;
    let vz = ship.body.velocity.z + sz;
    let mut along = vx * bow_x + vz * bow_z;
    let mul = speed_to_along_vel_scale.max(1e-4);
    along = along.clamp(max_backward_speed * mul, max_forward_speed * mul);
    let perp_x = vx - along * bow_x;
    let perp_z = vz - along * bow_z;
    ship.body.velocity = Vec3::new(
        along * bow_x + perp_x,
        ship.body.velocity.y,
        along * bow_z + perp_z,
    );
    ship.speed = along / mul;
    let sum_len = (sx * sx + sz * sz).sqrt();
    if sum_len < 1e-6 {
        return;
    }
    let cross = bow_x * (sz / sum_len) - bow_z * (sx / sum_len);
    ship.rot_speed += (-cross * TOW_YAW_ASSIST_RAD_PER_SEC * dt).clamp(-0.85, 0.85);
}

/// `ResolveShipPairBasisSlave`: the index of the other boat hull of a ship–ship line.
fn resolve_basis(ships: &[&mut PhysShip], tow: usize, line: &HarpoonLine) -> Option<usize> {
    let tow_obj = ships[tow].spec.obj_id;
    if line.hook_basis_obj_id != 0 {
        return ships.iter().position(|s| {
            s.spec.obj_id == line.hook_basis_obj_id
                && s.spec.kind.is_boat()
                && s.spec.obj_id != tow_obj
        });
    }
    if line.hook_attached_to_terrain {
        return None;
    }
    let max_d2 =
        SHIP_PAIR_MAX_GUESS_BASIS_DISTANCE_METERS * SHIP_PAIR_MAX_GUESS_BASIS_DISTANCE_METERS;
    let mut best = None;
    let mut best_d2 = f32::MAX;
    for (i, s) in ships.iter().enumerate() {
        if s.spec.obj_id == tow_obj || !s.spec.kind.is_boat() {
            continue;
        }
        let d2 = (line.hook_pos - s.pos).length_squared();
        if d2 > max_d2 || d2 >= best_d2 {
            continue;
        }
        best_d2 = d2;
        best = Some(i);
    }
    best
}

/// The hull pairs linked by an engaged ship harpoon (`AreBoatHullsLinkedByEngagedShipHarpoon`),
/// as `(min obj id, max obj id)`.
pub fn ship_links(ships: &[&mut PhysShip]) -> HashSet<(u32, u32)> {
    let mut out = HashSet::new();
    for (i, s) in ships.iter().enumerate() {
        for l in &s.harpoon_lines {
            if !l.engaged {
                continue;
            }
            let other = if l.hook_basis_obj_id != 0 {
                // explicit basis: any hull of that obj id
                Some(l.hook_basis_obj_id)
            } else if ships.len() >= 2 {
                resolve_basis(ships, i, l).map(|j| ships[j].spec.obj_id)
            } else {
                None
            };
            if let Some(o) = other {
                if o != s.spec.obj_id {
                    out.insert((o.min(s.spec.obj_id), o.max(s.spec.obj_id)));
                }
            }
        }
    }
    out
}

fn bow_of(rb: &super::jitter::RigidBody) -> f32 {
    super::jitter::yaw_pitch_roll(rb.orientation).0 + super::ship::HALF_PI
}

/// `ResyncSlaveSpeedFromRigidBodyAlongBow`.
fn resync_speed(ship: &mut PhysShip) {
    let bow = bow_of(&ship.body);
    let mul = ship.view.move_speed_mul / 4.0 * 0.001f32.max(ship.turn_speed_velocity_mul);
    let along = ship.body.velocity.x * bow.cos() + ship.body.velocity.z * bow.sin();
    ship.speed = along / mul;
}

/// `ApplyShipPairBowYawTowardPull`.
fn bow_yaw_toward_pull(ship: &mut PhysShip, pull_x: f32, pull_z: f32, dt: f32, mass_scale: f32) {
    if dt <= 0.0 {
        return;
    }
    let pl = (pull_x * pull_x + pull_z * pull_z).sqrt();
    if pl < 1e-5 {
        return;
    }
    let (px, pz) = (pull_x / pl, pull_z / pl);
    let bow = bow_of(&ship.body);
    let cross = bow.cos() * pz - bow.sin() * px;
    ship.rot_speed += (-cross * TOW_YAW_ASSIST_RAD_PER_SEC * dt * mass_scale).clamp(-0.85, 0.85);
}

/// `ApplyShipPairHarpoonTowImpulses(shipsThisTick, dt)`.
pub fn apply_ship_pair_tow_impulses(ships: &mut [&mut PhysShip], dt: f32) {
    if ships.is_empty() || dt <= 0.0 {
        return;
    }
    for tow in 0..ships.len() {
        let lines = ships[tow].harpoon_lines.clone();
        for line in lines {
            if !line.engaged {
                continue;
            }
            let Some(basis) = resolve_basis(ships, tow, &line) else {
                continue;
            };
            if basis == tow {
                continue;
            }
            let dist = line.cannon_pos.distance(line.hook_pos);
            let paid = line.rope_length + SERVER_ROPE_PAID_LENGTH_ADDITIVE_METERS;
            if dist > paid + SHIP_PAIR_MAX_CHORD_OVER_PAID_METERS {
                continue;
            }
            if paid > dist + SLACK_MARGIN_METERS + SHIP_PAIR_EXTRA_SLACK_MARGIN_METERS {
                continue;
            }
            let raw_stretch = dist - paid;
            if raw_stretch <= 0.0 {
                continue;
            }
            let stretch = raw_stretch.max(SHIP_PAIR_MIN_STRETCH_METERS);
            let accel = TOW_MAX_ACCEL.min(TOW_ACCEL_PER_METER_STRETCH * stretch);
            if accel <= 0.0 {
                continue;
            }
            let tp = ships[tow].body.position;
            let (dx, dz) = (line.hook_pos.x - tp.x, line.hook_pos.y - tp.z);
            let len = (dx * dx + dz * dz).sqrt();
            if len < MIN_HOOK_HORIZONTAL_DISTANCE {
                continue;
            }
            let (dx, dz) = (dx / len, dz / len);
            let m_tow = ships[tow].body.mass();
            let m_basis = ships[basis].body.mass();
            let total = m_tow + m_basis;
            if total < 1e-3 {
                continue;
            }
            let horiz = |s: &PhysShip| {
                (s.body.velocity.x * s.body.velocity.x + s.body.velocity.z * s.body.velocity.z)
                    .sqrt()
            };
            let s_tow = m_tow * (1.0 + SHIP_PAIR_DOMINANCE_SPEED_COEFF * horiz(ships[tow]));
            let s_basis = m_basis * (1.0 + SHIP_PAIR_DOMINANCE_SPEED_COEFF * horiz(ships[basis]));
            let impulse_mul = if s_tow >= s_basis {
                SHIP_PAIR_DOMINANT_TOW_HULL_MUL
            } else {
                SHIP_PAIR_DOMINANT_BASIS_HULL_MUL
            };
            let imp = accel * dt * impulse_mul;
            let tow_share = m_basis / total;
            let basis_share = m_tow / total;
            let reduced = total * tow_share * basis_share;
            let yaw_scale = (SHIP_PAIR_BOW_YAW_MASS_SCALE_REFERENCE
                / (SHIP_PAIR_BOW_YAW_MASS_SCALE_REFERENCE + reduced))
                .clamp(SHIP_PAIR_BOW_YAW_MASS_SCALE_MIN, 1.0);
            {
                let t = &mut ships[tow];
                t.body.velocity += Vec3::new(dx * imp * tow_share, 0.0, dz * imp * tow_share);
                resync_speed(t);
                bow_yaw_toward_pull(t, dx, dz, dt, yaw_scale);
            }
            {
                let b = &mut ships[basis];
                b.body.velocity += Vec3::new(-dx * imp * basis_share, 0.0, -dz * imp * basis_share);
                resync_speed(b);
                bow_yaw_toward_pull(b, -dx, -dz, dt, yaw_scale);
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::super::ship::tests::{model, spec};
    use super::super::ship::{PhysShip, ShipGameView};
    use super::*;

    fn line(hook: Vec3, rope: f32, terrain: bool, basis: u32) -> HarpoonLine {
        HarpoonLine {
            child_obj_id: 99,
            direct: true,
            cannon_pos: Vec3::new(0.0, 0.0, 100.0),
            hook_pos: hook,
            engaged: true,
            hook_basis_obj_id: basis,
            rope_length: rope,
            hook_attached_to_terrain: terrain,
        }
    }

    #[test]
    fn taut_terrain_hook_pulls_the_hull_toward_the_hook() {
        let mut s = PhysShip::new(
            spec(1, 0.0, 0.0, 100.0, 0.0),
            model(),
            ShipGameView::default(),
        );
        // hook 40 m east, rope paid 20 + 12.5 -> 7.5 m stretch -> 2 m/s² cap
        s.harpoon_lines = vec![line(Vec3::new(40.0, 0.0, 100.0), 20.0, true, 0)];
        assert!(has_taut_harpoon_engaged(&s.harpoon_lines));
        let bow = s.bow();
        apply_terrain_hook_tow(&mut s, 0.04, bow.cos(), bow.sin(), 0.25, 6.0, -3.0);
        assert!(s.body.velocity.x > 0.07, "{:?}", s.body.velocity);
        // slack rope: nothing
        let mut t = PhysShip::new(
            spec(2, 0.0, 0.0, 100.0, 0.0),
            model(),
            ShipGameView::default(),
        );
        t.harpoon_lines = vec![line(Vec3::new(10.0, 0.0, 100.0), 20.0, true, 0)];
        assert!(!has_taut_harpoon_engaged(&t.harpoon_lines));
        apply_terrain_hook_tow(&mut t, 0.04, 0.0, 1.0, 0.25, 6.0, -3.0);
        assert_eq!(t.body.velocity.x, 0.0);
    }

    #[test]
    fn ship_pair_tow_closes_both_hulls() {
        let mut a = PhysShip::new(
            spec(1, 0.0, 0.0, 100.0, 0.0),
            model(),
            ShipGameView::default(),
        );
        let mut b = PhysShip::new(
            spec(2, 40.0, 0.0, 100.0, 0.0),
            model(),
            ShipGameView::default(),
        );
        a.harpoon_lines = vec![line(Vec3::new(40.0, 0.0, 100.0), 20.0, false, 2)];
        let links = ship_links(&[&mut a, &mut b]);
        assert!(links.contains(&(1, 2)));
        apply_ship_pair_tow_impulses(&mut [&mut a, &mut b], 0.04);
        assert!(a.body.velocity.x > 0.0 && b.body.velocity.x < 0.0);
        // equal masses: symmetric impulses
        assert!((a.body.velocity.x + b.body.velocity.x).abs() < 1e-6);
    }
}
