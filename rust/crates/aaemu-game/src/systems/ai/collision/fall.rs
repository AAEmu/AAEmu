//! `ZWUnitFell` (zone → world, type 16; sender x2game `sub_3935C850`): the zone's fall
//! check before the world applies fall damage.
//!
//! The zone actor's fall tracker (`sub_392D7600`) keeps the maximum downward speed (+380) and
//! the fall start z (+384); on landing (on ground, not swimming / levitating / on a ship) it
//! computes `hVel = √(40·(startZ − z))` (`√(2·g·h)`, g = 20) and sends
//! `impactVel = min(maxVel, hVel)` only when `impactVel ≥ pl_fallDamage_SpeedSafe` (20 m/s).
//! Only unit kinds 0, 1, 2, 5 (character, NPC, mate, …) are reported.
//!
//! The zone has a second sender: the WZUnitMovement path (`sub_39232570`) forwards the
//! client's `state.fallVel` (+84) unchanged when it exceeds 20 m/s. That path equals the C#
//! behaviour (client value, no cap) and is what runs with `Physics.FallDamageZoneCap = false`
//! or without the collision service; the default applies the tracker's (path a) gate and cap.
//!
//! The 1.2 client reports its own `fallVel` (u16) and the C# `DoFallDamage` uses it directly.
//! With `Physics.FallDamageZoneCap` the world now runs the zone's check first:
//!
//! * the start z is the highest position since the unit was last on the ground (ground =
//!   the collision query service's ground height within [`GROUND_EPSILON`]);
//! * no damage when `impactVel < 20` (the zone would not have sent ZWUnitFell);
//! * the client value is capped at `impactVel` converted to the client's units
//!   ([`FALL_VEL_UNITS_PER_MPS`], *(inferred)*: the C# curve's fatal value 32000 ≈ 63.25 m/s
//!   `pl_fallDamage_SpeedFatal` × 506).

use dashmap::DashMap;

/// `pl_fallDamage_SpeedSafe` (CGame +488).
pub const SPEED_SAFE: f32 = 20.0;
/// `pl_fallDamage_SpeedFatal` (+492).
pub const SPEED_FATAL: f32 = 63.25;
/// Fall gravity of the cap (`√(40·h)` = `√(2·20·h)`).
pub const FALL_GRAVITY: f32 = 20.0;
/// *(inferred)* client `fallVel` units per m/s (`32000 / 63.25`).
pub const FALL_VEL_UNITS_PER_MPS: f32 = 32000.0 / 63.25;
/// A unit is on the ground when its z is within this of the ground height.
pub const GROUND_EPSILON: f32 = 0.5;

/// Per-unit fall state (`+380` max downward speed, `+384` start z).
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct FallState {
    pub start_z: f32,
    pub max_down_vel: f32,
    pub airborne: bool,
    pub last_z: f32,
    pub last_ms: i64,
}

/// The trackers of every unit (by obj id).
#[derive(Debug, Default)]
pub struct FallTracker {
    states: DashMap<u32, FallState>,
}

/// `√(40·h)` (`hVel`, 0 for h ≤ 0).
pub fn height_velocity(h: f32) -> f32 {
    if h <= 0.0 {
        0.0
    } else {
        (2.0 * FALL_GRAVITY * h).sqrt()
    }
}

/// `impactVel = min(maxVel, √(40·(startZ − z)))`.
pub fn impact_velocity(max_down_vel: f32, start_z: f32, land_z: f32) -> f32 {
    max_down_vel.min(height_velocity(start_z - land_z))
}

impl FallTracker {
    /// Records a position. `ground` = the ground height under it (None = unknown: treated as
    /// on ground, the tracker restarts). Returns the impact velocity when this sample lands a
    /// fall (`None` while airborne / on the ground without a fall).
    pub fn sample(&self, obj_id: u32, z: f32, ground: Option<f32>, now_ms: i64) -> Option<f32> {
        let mut s = self.states.entry(obj_id).or_insert(FallState {
            start_z: z,
            max_down_vel: 0.0,
            airborne: false,
            last_z: z,
            last_ms: now_ms,
        });
        let dt = (now_ms - s.last_ms) as f32 / 1000.0;
        if dt > 0.0 && z < s.last_z {
            let v = (s.last_z - z) / dt;
            if v > s.max_down_vel {
                s.max_down_vel = v;
            }
        }
        s.last_z = z;
        s.last_ms = now_ms;
        let on_ground = ground.is_none_or(|g| z - g <= GROUND_EPSILON);
        if on_ground {
            let landed = s.airborne;
            let impact = impact_velocity(s.max_down_vel, s.start_z, z);
            s.airborne = false;
            s.start_z = z;
            s.max_down_vel = 0.0;
            return landed.then_some(impact);
        }
        if !s.airborne {
            s.airborne = true;
            s.start_z = z;
            s.max_down_vel = 0.0;
        } else if z > s.start_z {
            s.start_z = z;
        }
        None
    }

    /// The fall start z / airborne state of a unit.
    pub fn state(&self, obj_id: u32) -> Option<FallState> {
        self.states.get(&obj_id).map(|s| *s)
    }

    /// Resets a unit (teleport, death, unmount …).
    pub fn reset(&self, obj_id: u32) {
        self.states.remove(&obj_id);
    }

    /// The fall start of a unit that is landing now: the start z (or the last z when it was
    /// on the ground).
    pub fn start_z(&self, obj_id: u32) -> Option<f32> {
        self.states.get(&obj_id).map(|s| s.start_z)
    }
}

/// The zone's decision on a client fall report: `None` = the zone would not send ZWUnitFell
/// (no damage), `Some(v)` = the client `fallVel` to use (capped at the zone's impact
/// velocity).
pub fn zone_fall_vel(client_fall_vel: u16, start_z: f32, land_z: f32) -> Option<u16> {
    let client_mps = f32::from(client_fall_vel) / FALL_VEL_UNITS_PER_MPS;
    let impact = impact_velocity(client_mps, start_z, land_z);
    if impact < SPEED_SAFE {
        return None;
    }
    Some((impact * FALL_VEL_UNITS_PER_MPS).min(f32::from(client_fall_vel)) as u16)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn impact_is_capped_by_height() {
        assert_eq!(height_velocity(10.0), 20.0);
        assert_eq!(impact_velocity(50.0, 110.0, 100.0), 20.0);
        assert_eq!(impact_velocity(15.0, 110.0, 100.0), 15.0);
        assert_eq!(height_velocity(-1.0), 0.0);
    }

    #[test]
    fn zone_gate_and_cap() {
        // 9 m drop: √360 ≈ 18.97 < 20 → no ZWUnitFell
        assert_eq!(zone_fall_vel(30000, 109.0, 100.0), None);
        // 50 m drop: √2000 ≈ 44.7 m/s; client says 60 m/s → capped
        let v = zone_fall_vel(30360, 150.0, 100.0).unwrap();
        assert_eq!(v, (2000f32.sqrt() * FALL_VEL_UNITS_PER_MPS) as u16);
        // client slower than the height cap: unchanged
        assert_eq!(zone_fall_vel(12000, 200.0, 100.0), Some(12000));
    }

    #[test]
    fn tracker_detects_a_landing() {
        let t = FallTracker::default();
        assert_eq!(t.sample(1, 150.0, Some(150.0), 0), None);
        assert_eq!(t.sample(1, 151.0, Some(100.0), 100), None); // jump off a cliff
        assert_eq!(t.sample(1, 130.0, Some(100.0), 1100), None);
        assert_eq!(t.state(1).unwrap().start_z, 151.0);
        let v = t.sample(1, 100.0, Some(100.0), 2100).unwrap();
        // max downward speed 30 m/s, height cap √(40·51) ≈ 45.2
        assert_eq!(v, 30.0);
        assert!(!t.state(1).unwrap().airborne);
        assert_eq!(t.sample(1, 100.0, Some(100.0), 2200), None);
    }
}
