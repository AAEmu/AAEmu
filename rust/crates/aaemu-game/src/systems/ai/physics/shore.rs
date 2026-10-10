//! `Physics/ShipShoreInteraction`: friction on dry ground, the bow / stern shore probe with its
//! latch, the vertical penetration resolve and the visual ground pitch; plus
//! `Slave.TickBeachedHullDamage` / `TickStaticObstacleHullDamage`.

use crate::systems::ai::numerics::{deg_to_rad, Vec3Ext};
use crate::systems::skills::controllers::Vec3;

use super::jitter::yaw_pitch_roll;
use super::ship::{PhysShip, HALF_PI};
use super::PhysEnv;

/// `ShorePhysicsDefaults`.
pub mod defaults {
    pub const BOAT_BOTTOM_OFFSET_FRAC_OF_SIZE_Z: f32 = 0.0;
    pub const GROUND_FRICTION: f32 = 0.1;
    pub const DRY_GROUND_COLLISION_DAMPING: f32 = 1.0;
    pub const DRY_GROUND_ROLL_DEAD_ZONE_RAD: f32 = 0.1;
    pub const DRY_GROUND_ROLL_TORQUE_MUL: f32 = 0.1;
    pub const SHORE_ENTER_HYST: f32 = 0.5;
    pub const SHORE_EXIT_HYST: f32 = 0.35;
    pub const FLOOR_SMOOTH_RESPONSE: f32 = 16.0;
    pub const PRE_SHORE_BAND: f32 = 0.5;
    pub const PENETRATION_EPSILON: f32 = 0.04;
    pub const PENETRATION_RESPONSE: f32 = 4.5;
    pub const MAX_UP_STEP_EARLY: f32 = 0.04;
    pub const MAX_UP_STEP_LATE: f32 = 0.07;
    pub const VISUAL_GROUND_PITCH_MAX_DEG: f32 = 8.0;
    pub const VISUAL_GROUND_PITCH_PROBE_DISTANCE: f32 = 6.0;
    pub const VISUAL_GROUND_PITCH_RESPONSE: f32 = 2.0;
    pub const VISUAL_PITCH_FLOOR_SMOOTH_RESPONSE: f32 = 8.0;
}

/// `ApplyOnLandPhysics(slave, dt)`: when the hull centre is not under the water surface.
pub fn apply_on_land_physics(ship: &mut PhysShip) {
    let submerged = 0f32.max(ship.cached_water_surface - ship.body.position.y);
    let is_on_land = submerged <= 0.0;
    if !is_on_land {
        return;
    }
    let v = ship.body.velocity;
    let friction = Vec3::new(
        -v.x * defaults::GROUND_FRICTION,
        0.0,
        -v.z * defaults::GROUND_FRICTION,
    );
    ship.body.add_force(friction);
    ship.body.velocity *= defaults::DRY_GROUND_COLLISION_DAMPING;
    ship.body.angular_velocity *= defaults::DRY_GROUND_COLLISION_DAMPING;
    if ship.body.velocity.length() < 0.01 {
        ship.body.velocity = Vec3::new(0.0, 0.0, 0.0);
        ship.body.angular_velocity = Vec3::new(0.0, 0.0, 0.0);
        // GetYawPitchRollFromJMatrix(...).Item2: MathUtil.GetYawPitchRollFromQuat → pitch
        let q = ship.body.orientation;
        let roll_angle =
            crate::models::movement::get_yaw_pitch_roll_from_quat([q.x, q.y, q.z, q.w]).1;
        if roll_angle.abs() < defaults::DRY_GROUND_ROLL_DEAD_ZONE_RAD {
            // C# bug mirrored: the "correction torque" is added as a force.
            let mass = ship.body.mass();
            ship.body.add_force(Vec3::new(
                0.0,
                0.0,
                -roll_angle * mass * defaults::DRY_GROUND_ROLL_TORQUE_MUL,
            ));
        }
        if ship.throttle_request == 0 && ship.steering_request == 0 {
            ship.throttle = 0;
            ship.steering = 0;
            ship.throttle_smoothed = 0.0;
            ship.steering_smoothed = 0.0;
        }
    }
}

/// `ResolveTerrainContacts`: the shore resolve, then `TickBeachedHullDamage` (returns the hull
/// damage percent ticks to deal).
pub fn resolve_terrain_contacts(
    ship: &mut PhysShip,
    env: &dyn PhysEnv,
    dt_sec: f32,
    gravity: Vec3,
) -> u32 {
    resolve_land_collisions(ship, env, dt_sec, gravity);
    tick_beached_hull_damage(ship, dt_sec)
}

/// `Slave.TickBeachedHullDamage`: 1% per second while latched on shore.
pub fn tick_beached_hull_damage(ship: &mut PhysShip, dt: f32) -> u32 {
    if !ship.ground_contact_latched {
        ship.shore_ground_damage_seconds_accumulator = 0.0;
        return 0;
    }
    if dt <= 0.0 {
        return 0;
    }
    let mut ticks = 0;
    ship.shore_ground_damage_seconds_accumulator += dt;
    while ship.shore_ground_damage_seconds_accumulator >= 1.0 {
        ship.shore_ground_damage_seconds_accumulator -= 1.0;
        ticks += 1;
    }
    ticks
}

/// `Slave.TickStaticObstacleHullDamage`: 1% per second of static-obstacle contact (progress is
/// dropped after 0.35 s without contact).
pub fn tick_static_obstacle_hull_damage(ship: &mut PhysShip, dt: f32) -> u32 {
    if dt <= 0.0 {
        return 0;
    }
    let mut ticks = 0;
    if ship.static_obstacle_hull_damage_contact_active {
        ship.static_obstacle_hull_damage_no_contact_seconds = 0.0;
        ship.static_obstacle_hull_damage_seconds_accumulator += dt;
        while ship.static_obstacle_hull_damage_seconds_accumulator >= 1.0 {
            ship.static_obstacle_hull_damage_seconds_accumulator -= 1.0;
            ticks += 1;
        }
    } else {
        ship.static_obstacle_hull_damage_no_contact_seconds += dt;
        if ship.static_obstacle_hull_damage_no_contact_seconds >= 0.35 {
            ship.static_obstacle_hull_damage_seconds_accumulator = 0.0;
        }
    }
    ticks
}

/// `UpdateVisualGroundPitch`: replicated nose/stern pitch along the terrain slope while
/// grounded (the rigid body is not moved).
pub fn update_visual_ground_pitch(ship: &mut PhysShip, env: &dyn PhysEnv, dt: f32) {
    let dt = 0.0001f32.max(dt);
    let mut target = 0.0;
    if ship.cached_floor_level > ship.cached_water_surface || ship.ground_contact_latched {
        let yaw = yaw_pitch_roll(ship.body.orientation).0 + HALF_PI;
        let (c, s) = (yaw.cos(), yaw.sin());
        let (cx, cy) = (ship.body.position.x, ship.body.position.z);
        let d = defaults::VISUAL_GROUND_PITCH_PROBE_DISTANCE;
        let front_h = env.height(cx + c * d, cy + s * d);
        let back_h = env.height(cx - c * d, cy - s * d);
        let floor_a = 1.0 - (-defaults::VISUAL_PITCH_FLOOR_SMOOTH_RESPONSE * dt).exp();
        if !ship.ground_pitch_floor_smoothing_seeded {
            ship.ground_pitch_front_floor_smoothed = front_h;
            ship.ground_pitch_back_floor_smoothed = back_h;
            ship.ground_pitch_floor_smoothing_seeded = true;
        } else {
            ship.ground_pitch_front_floor_smoothed +=
                (front_h - ship.ground_pitch_front_floor_smoothed) * floor_a;
            ship.ground_pitch_back_floor_smoothed +=
                (back_h - ship.ground_pitch_back_floor_smoothed) * floor_a;
        }
        let slope = (ship.ground_pitch_front_floor_smoothed
            - ship.ground_pitch_back_floor_smoothed)
            .atan2(d * 2.0);
        let max = deg_to_rad(defaults::VISUAL_GROUND_PITCH_MAX_DEG);
        target = slope.clamp(-max, max);
        if ship.grounded_by_stern {
            target = -target;
        }
    } else {
        ship.ground_pitch_floor_smoothing_seeded = false;
    }
    let pitch_a = 1.0 - (-defaults::VISUAL_GROUND_PITCH_RESPONSE * dt).exp();
    ship.ground_pitch_angle += (target - ship.ground_pitch_angle) * pitch_a;
}

fn set_ground_contact_latch(ship: &mut PhysShip, latched: bool) {
    ship.ground_contact_latched = latched;
    ship.ground_contact_latched_time = 0.0;
}

/// `ResolveLandCollisions`.
fn resolve_land_collisions(ship: &mut PhysShip, env: &dyn PhysEnv, dt_sec: f32, gravity: Vec3) {
    let dt = 0.0001f32.max(dt_sec);
    let scale = ship.spec.scale;
    let hull_height = 0.01f32.max(ship.model.mass_box_size_z * scale);
    let boat_bottom =
        ship.body.position.y - hull_height * defaults::BOAT_BOTTOM_OFFSET_FRAC_OF_SIZE_Z;

    let heading = ship.bow();
    let (dir_x, dir_z) = (heading.cos(), heading.sin());
    let along = ship.body.velocity.x * dir_x + ship.body.velocity.z * dir_z;
    let moving_backward = along < -0.05 || (along.abs() <= 0.05 && ship.throttle_request < 0);
    let use_stern_probe = if ship.ground_contact_latched {
        ship.grounded_by_stern
    } else {
        moving_backward
    };
    let probe_sign = if use_stern_probe { -1.0 } else { 1.0 };
    let half_length = 0.5 * ship.model.mass_box_size_y * scale;
    let center_offset = ship.model.mass_center_y * scale;
    let dist_to_face = if probe_sign > 0.0 {
        half_length + center_offset
    } else {
        half_length - center_offset
    };
    let probe_dist = 0.01f32.max(dist_to_face);
    let probe_x = ship.body.position.x + dir_x * probe_dist * probe_sign;
    let probe_y = ship.body.position.z + dir_z * probe_dist * probe_sign;
    let contact_floor = env.height(probe_x, probe_y);
    let water_at_probe = env
        .water_surface(Vec3::new(probe_x, probe_y, ship.body.position.y))
        .0;

    {
        let a = 1.0 - (-defaults::FLOOR_SMOOTH_RESPONSE * dt).exp();
        if !ship.ground_contact_latched && !ship.ground_contact_floor_smoothing_seeded {
            ship.ground_contact_floor_smoothed = contact_floor;
            ship.ground_contact_floor_smoothing_seeded = true;
        } else {
            ship.ground_contact_floor_smoothed +=
                (contact_floor - ship.ground_contact_floor_smoothed) * a;
        }
    }
    let floor_smoothed = ship.ground_contact_floor_smoothed;

    let enter_delta = (water_at_probe + defaults::SHORE_ENTER_HYST) - floor_smoothed;
    let exit_delta = (water_at_probe + defaults::SHORE_EXIT_HYST) - floor_smoothed;
    if !ship.ground_contact_latched && (0.0..=defaults::PRE_SHORE_BAND).contains(&enter_delta) {
        let v = ship.body.velocity;
        let t = 1.0 - (enter_delta / defaults::PRE_SHORE_BAND);
        let damp = 1.0 - 0.85 * t;
        ship.body.velocity = Vec3::new(v.x, v.y * damp, v.z);
    }

    if !ship.ground_contact_latched {
        if enter_delta >= 0.0 {
            return;
        }
        set_ground_contact_latch(ship, true);
    } else if exit_delta >= 0.0 {
        set_ground_contact_latch(ship, false);
        return;
    }

    {
        let v = ship.body.velocity;
        if v.y.abs() > 0.01 {
            ship.body.velocity = Vec3::new(v.x, 0.0, v.z);
        }
    }
    ship.ground_contact_latched_time += 0f32.max(dt_sec);

    let penetration = floor_smoothed - boat_bottom;
    if penetration <= 0.0 {
        return;
    }

    let rb = &ship.body;
    let water_at_center = env
        .water_surface(Vec3::new(rb.position.x, rb.position.z, rb.position.y))
        .0;
    let center_above_waterline = ship.body.position.y > water_at_center + 0.001;
    let max_up_step = if ship.ground_contact_latched_time < 0.30 {
        defaults::MAX_UP_STEP_EARLY
    } else {
        defaults::MAX_UP_STEP_LATE
    };
    if penetration > defaults::PENETRATION_EPSILON && !center_above_waterline {
        let a = 1.0 - (-defaults::PENETRATION_RESPONSE * dt).exp();
        let step = (penetration * a).min(max_up_step);
        ship.body.position += Vec3::new(0.0, step, 0.0);
        let v = ship.body.velocity;
        if v.y.abs() > 0.01 {
            ship.body.velocity = Vec3::new(v.x, 0.0, v.z);
        }
    } else if center_above_waterline {
        let v = ship.body.velocity;
        if v.y > 0.0 {
            ship.body.velocity = Vec3::new(v.x, 0.0, v.z);
        }
    }
    ship.body.add_force(gravity * -1.0);

    let escape_throttle_sign = if ship.grounded_by_stern { 1 } else { -1 };
    let is_escape_throttle = ship.throttle_request != 0
        && i32::from(ship.throttle_request.signum()) == escape_throttle_sign;
    let deep_contact = penetration > 0.25;
    let collision_damping = if is_escape_throttle {
        1.0
    } else if deep_contact {
        0.88
    } else {
        0.95
    };
    ship.body.velocity *= collision_damping;
    ship.body.angular_velocity *= collision_damping;
}

#[cfg(test)]
mod tests {
    use super::super::ship::tests::{model, spec};
    use super::super::ship::{PhysShip, ShipGameView};
    use super::super::tests::FakeEnv;
    use super::*;

    #[test]
    fn beached_ship_latches_and_takes_hull_damage_every_second() {
        // shore rising under the bow: terrain at 101 north of y = 5
        let env = FakeEnv {
            ocean: 100.0,
            height: Box::new(|_x, y| if y > 5.0 { 101.0 } else { 80.0 }),
        };
        let mut s = PhysShip::new(
            spec(1, 0.0, 0.0, 99.5, 0.0),
            model(),
            ShipGameView::default(),
        );
        s.cached_water_surface = 100.0;
        s.cached_floor_level = 80.0;
        let g = Vec3::new(0.0, -9.81, 0.0);
        let mut dmg = 0;
        for _ in 0..30 {
            dmg += resolve_terrain_contacts(&mut s, &env, 0.04, g);
        }
        assert!(s.ground_contact_latched);
        assert_eq!(dmg, 1); // 30 * 0.04 s = 1.2 s latched
        assert!(s.body.position.y > 99.5);
        // static obstacle damage accumulator with a short gap
        s.static_obstacle_hull_damage_contact_active = true;
        assert_eq!(tick_static_obstacle_hull_damage(&mut s, 0.6), 0);
        s.static_obstacle_hull_damage_contact_active = false;
        assert_eq!(tick_static_obstacle_hull_damage(&mut s, 0.2), 0);
        s.static_obstacle_hull_damage_contact_active = true;
        assert_eq!(tick_static_obstacle_hull_damage(&mut s, 0.5), 1);
    }

    #[test]
    fn dry_land_friction_and_settling() {
        let mut s = PhysShip::new(
            spec(1, 0.0, 0.0, 105.0, 0.0),
            model(),
            ShipGameView::default(),
        );
        s.cached_water_surface = 100.0;
        s.body.velocity = Vec3::new(1.0, 0.0, 2.0);
        apply_on_land_physics(&mut s);
        assert_eq!(s.body.force, Vec3::new(-0.1, 0.0, -0.2));
        s.body.force = Vec3::new(0.0, 0.0, 0.0);
        s.body.velocity = Vec3::new(0.001, 0.0, 0.0);
        s.throttle = 5;
        apply_on_land_physics(&mut s);
        assert_eq!(s.body.velocity, Vec3::new(0.0, 0.0, 0.0));
        assert_eq!(s.throttle, 0);
    }
}
