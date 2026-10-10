//! `PhysicsManager.SendUpdatedMovementData`: the visual turn bank, ground pitch and wave pitch,
//! the replication smoothing and the `ShipMoveType` broadcast with `SCOneUnitMovementPacket`,
//! plus the transform written back to the slave.

use crate::models::movement::{MoveKind, MoveType, MoveTypeFlags, ShipMove};
use crate::systems::ai::data::ShipModel;
use crate::systems::ai::numerics::{deg_to_rad, Quat};

use super::jitter::yaw_pitch_roll;
use super::ship::{from_quaternion, PhysShip};
use super::PhysEnv;

/// `TryGetShipMassBoxWaterlineExtents`: (length, beam, height, mass).
fn waterline_extents(model: &ShipModel, scale: f32) -> (f32, f32, f32, f32) {
    let s = scale.max(0.01);
    let hx = model.mass_box_size_x * s;
    let hy = model.mass_box_size_y * s;
    let length = hx.max(hy).max(0.25);
    let beam = hx.min(hy).max(0.25);
    let height = (model.mass_box_size_z * s).max(0.15);
    let mass = model.mass.max(10.0);
    (length, beam, height, mass)
}

/// `GetVisualWavePitchModelFactors`: (max amplitude rad, angular frequency).
pub fn visual_wave_pitch_model_factors(model: &ShipModel, scale: f32) -> (f32, f32) {
    const BASE_DEG: f32 = 3.0;
    const BASE_HZ: f32 = 0.06;
    let (length, _, _, mass) = waterline_extents(model, scale);
    let len_ratio = (14.0 / length).clamp(0.35, 2.5);
    let mass_ratio = (85000.0 / mass).sqrt().clamp(0.45, 2.2);
    let amp_mul = len_ratio.powf(0.38) * mass_ratio.powf(0.28);
    let max_deg = (BASE_DEG * amp_mul).clamp(1.1, 5.5);
    let freq_mul = (length / 14.0).clamp(0.5, 2.2).powf(-0.18);
    let hz = (BASE_HZ * freq_mul).clamp(0.042, 0.078);
    (deg_to_rad(max_deg), 2.0 * std::f32::consts::PI * hz)
}

/// `ComputeVisualWavePitchOnWater` (advances the wave phase).
pub fn visual_wave_pitch_on_water(ship: &mut PhysShip, dt: f32) -> f32 {
    if ship.cached_floor_level > ship.cached_water_surface || ship.ground_contact_latched {
        return 0.0;
    }
    let submerged = 0f32.max(ship.cached_water_surface - ship.body.position.y);
    let depth_mul = (submerged / 0.32).clamp(0.0, 1.0);
    if depth_mul <= 0.0 {
        return 0.0;
    }
    let (max_amp, omega) = visual_wave_pitch_model_factors(&ship.model, ship.spec.scale);
    ship.wave_pitch_phase += omega * dt;
    if ship.wave_pitch_phase > std::f32::consts::PI * 4000.0 {
        ship.wave_pitch_phase -= std::f32::consts::PI * 4000.0;
    }
    let phase_off = (ship.spec.obj_id & 511) as f32 * 0.211;
    (ship.wave_pitch_phase + phase_off).sin() * max_amp * depth_mul
}

/// `ComputeVisualMaxBankDegFromShipModel`.
pub fn visual_max_bank_deg(model: &ShipModel, scale: f32) -> f32 {
    let (length, beam, height, mass) = waterline_extents(model, scale);
    let length_factor = (length / 14.0).clamp(0.35, 2.8).powf(0.22);
    let beam_factor = (1.5 / beam).clamp(0.65, 1.6).powf(0.28);
    let mass_factor = (85000.0 / mass).clamp(0.2, 4.0).powf(0.18);
    let height_factor = (16.0 / height).clamp(0.5, 2.0).powf(0.12);
    (9.0 * length_factor * beam_factor * mass_factor * height_factor).clamp(5.0, 14.0)
}

/// `MathUtil.GetSlaveRotationFromDegrees(degX, degY, degZ)` → (x, z, y) shorts.
pub fn slave_rotation_from_degrees(deg_x: f32, deg_y: f32, deg_z: f32) -> (i16, i16, i16) {
    let q = Quat::from_yaw_pitch_roll(deg_z, deg_x, deg_y);
    const K: f32 = 0.000_030_52;
    (
        float_to_short(q.x / K),
        float_to_short(q.z / K),
        float_to_short(q.y / K),
    )
}

/// `(short)floatValue` (saturating to int, then wrapping to 16 bits).
pub fn float_to_short(v: f32) -> i16 {
    (v as i32) as i16
}

/// The output of one `SendUpdatedMovementData`.
#[derive(Debug, Clone, PartialEq)]
pub struct MovementUpdate {
    pub obj_id: u32,
    pub move_type: MoveType,
    /// The new `Transform.Local` (position, (roll, pitch, yaw)).
    pub position: crate::systems::skills::controllers::Vec3,
    pub rotation: (f32, f32, f32),
}

/// `SendUpdatedMovementData(slave, rigidBody, deltaTime)`.
pub fn send_updated_movement_data(
    ship: &mut PhysShip,
    env: &dyn PhysEnv,
    dt_sec: f32,
    now_ms: i64,
) -> MovementUpdate {
    let rb_orientation = ship.body.orientation;
    let rpy = yaw_pitch_roll(rb_orientation);
    let max_bank_deg = visual_max_bank_deg(&ship.model, ship.spec.scale);
    const BANK_RESPONSE: f32 = 7.5;
    let dt = 0.0001f32.max(dt_sec);
    let max_bank_rad = deg_to_rad(max_bank_deg);
    let yaw_rate = ship.body.angular_velocity.y;
    let v = ship.body.velocity;
    let horiz = (v.x * v.x + v.z * v.z).sqrt();
    let speed_factor = (horiz / 2.5).clamp(0.0, 1.0);
    let target_bank = (-yaw_rate * 0.9).clamp(-max_bank_rad, max_bank_rad) * speed_factor;
    let a = 1.0 - (-BANK_RESPONSE * dt).exp();
    ship.bank_angle += (target_bank - ship.bank_angle) * a;

    super::shore::update_visual_ground_pitch(ship, env, dt_sec);
    let wave = visual_wave_pitch_on_water(ship, dt);

    let contact = ship.replication.contact_hold_ticks > 0;
    let lambda_h = if contact { 11.0 } else { 22.0 };
    let lambda_v = if contact { 4.0 } else { 8.0 };
    let lambda_b = if contact { 2.0 } else { 4.0 };
    let alpha_h = 1.0 - (-lambda_h * dt).exp();
    let alpha_v = 1.0 - (-lambda_v * dt).exp();
    let alpha_b = 1.0 - (-lambda_b * dt).exp();

    let p = ship.body.position;
    let (tgt_x, tgt_y, tgt_z) = (p.x, p.z, p.y);
    let (tgt_vx, tgt_vy, tgt_vz) = (v.x, v.z, v.y);
    let rep = &mut ship.replication;
    if !rep.seeded {
        rep.pos_x = tgt_x;
        rep.pos_y = tgt_y;
        rep.pos_z = tgt_z;
        rep.vel_px = tgt_vx;
        rep.vel_py = tgt_vy;
        rep.vel_pz = tgt_vz;
        rep.bank_smoothed = ship.bank_angle;
        rep.ground_pitch_smoothed = ship.ground_pitch_angle;
        rep.seeded = true;
    } else {
        rep.pos_x += (tgt_x - rep.pos_x) * alpha_h;
        rep.pos_y += (tgt_y - rep.pos_y) * alpha_h;
        rep.pos_z += (tgt_z - rep.pos_z) * alpha_v;
        rep.vel_px += (tgt_vx - rep.vel_px) * alpha_h;
        rep.vel_py += (tgt_vy - rep.vel_py) * alpha_h;
        rep.vel_pz += (tgt_vz - rep.vel_pz) * alpha_v;
        rep.bank_smoothed += (ship.bank_angle - rep.bank_smoothed) * alpha_b;
        rep.ground_pitch_smoothed +=
            (ship.ground_pitch_angle - rep.ground_pitch_smoothed) * alpha_v;
    }
    let rep = ship.replication;
    let banked = (
        rpy.0,
        rpy.1 + rep.bank_smoothed,
        rpy.2 + rep.ground_pitch_smoothed + wave,
    );
    let (rot_z, rot_y, rot_x) = slave_rotation_from_degrees(banked.0, banked.1, banked.2);

    const VEL_MULTIPLIER: f32 = 2048.0;
    let av = ship.body.angular_velocity;
    let sm = ShipMove {
        x: rep.pos_x,
        y: rep.pos_y,
        z: rep.pos_z,
        vel_x: float_to_short(rep.vel_px * VEL_MULTIPLIER),
        vel_y: float_to_short(rep.vel_py * VEL_MULTIPLIER),
        vel_z: float_to_short(rep.vel_pz * VEL_MULTIPLIER),
        rotation_x: rot_x,
        rotation_y: rot_y,
        rotation_z: rot_z,
        ang_vel_x: av.x,
        ang_vel_y: av.z,
        ang_vel_z: av.y,
        steering: ship.steering,
        throttle: ship.throttle,
        zone_id: ship.view.zone_id as u16,
        stuck: false,
    };
    let mut mt = MoveType::new(MoveKind::Ship(sm));
    mt.flags = MoveTypeFlags(0);
    mt.time = (now_ms - ship.spec.spawn_time_ms).max(0) as u32;

    // Transform.Local.SetPosition(rb X, Z, Y); ApplyFromQuaternion(rb.Orientation) — C# bug
    // mirrored: the Jitter quaternion is applied without the Y/Z swap of
    // SyncTransformWithRigidBody, so between ticks the yaw sits in the pitch slot — then
    // SetRotation(X, Y + bank, Z + ground pitch + wave).
    let position = crate::systems::skills::controllers::Vec3::new(p.x, p.z, p.y);
    let q = rb_orientation;
    let (rx, ry, rz) = from_quaternion(q.x, q.y, q.z, q.w);
    let rotation = (
        rx,
        ry + rep.bank_smoothed,
        rz + rep.ground_pitch_smoothed + wave,
    );
    ship.pos = position;
    ship.rot = rotation;
    if ship.replication.contact_hold_ticks > 0 {
        ship.replication.contact_hold_ticks -= 1;
    }
    MovementUpdate {
        obj_id: ship.spec.obj_id,
        move_type: mt,
        position,
        rotation,
    }
}

#[cfg(test)]
mod tests {
    use super::super::ship::tests::{model, spec};
    use super::super::ship::{PhysShip, ShipGameView};
    use super::super::tests::FakeEnv;
    use super::*;

    #[test]
    fn rotation_shorts_follow_the_csharp_axis_swap() {
        // yaw-only physics rotation of 0.5 rad: CreateFromYawPitchRoll(0, 0.5, 0) = pitch about X
        let (z, y, x) = slave_rotation_from_degrees(0.5, 0.0, 0.0);
        assert_eq!(y, 0);
        assert_eq!(x, 0);
        assert_eq!(z, float_to_short(0.25f32.sin() / 0.000_030_52));
        assert_eq!(float_to_short(40000.0), (40000i32) as i16);
    }

    #[test]
    fn first_update_seeds_the_replication_and_reports_the_hull() {
        let env = FakeEnv::flat(100.0, 50.0);
        let mut s = PhysShip::new(
            spec(7, 10.0, 20.0, 99.0, 0.0),
            model(),
            ShipGameView::default(),
        );
        s.cached_water_surface = 100.0;
        s.cached_floor_level = 50.0;
        s.body.velocity = crate::systems::skills::controllers::Vec3::new(0.0, 0.0, 2.0);
        s.throttle = 30;
        let u = send_updated_movement_data(&mut s, &env, 0.04, 5000);
        let MoveKind::Ship(m) = u.move_type.kind else {
            panic!("ship move expected")
        };
        assert_eq!((m.x, m.y, m.z), (10.0, 20.0, 99.0));
        assert_eq!(m.vel_y, 4096);
        assert_eq!(m.throttle, 30);
        assert_eq!(u.move_type.time, 5000);
        assert!(s.replication.seeded);
        // wave pitch only on water: some rotation is present
        assert!(s.wave_pitch_phase > 0.0);
        assert!((3.0..=14.0).contains(&visual_max_bank_deg(&s.model, 1.0)));
    }
}
