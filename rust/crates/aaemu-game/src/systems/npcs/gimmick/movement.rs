//! `GimmickMovementHandler` and its subclasses, plus `Gimmick.MoveAlongZAxis`, as pure functions
//! over the gimmick fields they read / write ([`MovementState`]).

use super::spawner::GimmickSpawner;

/// `GimmickMovementElevator.Tick`'s `MaxVelocity`.
pub const ELEVATOR_MAX_VELOCITY: f32 = 4.5;
/// `GimmickMovementFloatToSurface`: 2.5 m/s ("100m in 40 seconds").
pub const FLOAT_TO_SURFACE_SPEED: f32 = 2.5;

/// `Gimmick.MovementHandler` (set by `GimmickManager.AddActiveGimmick`).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum MovementHandler {
    /// `GimmickMovementElevator` (template 0 with an `EntityGuid > 0`, gimmick_spawns.json).
    Elevator,
    /// `GimmickMovementFloatToSurface` (template 37, Recovered Treasure Chest).
    FloatToSurface,
}

/// The `Gimmick` members the movement code reads and writes.
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct MovementState {
    /// `Transform.Local.Position.Z` (= world Z: gimmicks have no parent).
    pub z: f32,
    /// `Vel`.
    pub vel: [f32; 3],
    /// `IsMoving`.
    pub is_moving: bool,
    /// `MoveDown`.
    pub move_down: bool,
}

/// `Math.Sign(float)` (NaN would throw in C#; 0 here).
fn sign(v: f32) -> f32 {
    if v > 0.0 {
        1.0
    } else if v < 0.0 {
        -1.0
    } else {
        0.0
    }
}

/// `Gimmick.MoveAlongZAxis(gimmick, ref position, target, maxVelocity, deltaTime, ref velocityZ,
/// ref isMovingDown)` (`isMovingDown` is never written). Note (C#): standing exactly on the
/// target moves by 0 and keeps `IsMoving = true`.
pub fn move_along_z_axis(
    st: &mut MovementState,
    position_z: &mut f32,
    target_z: f32,
    max_velocity: f32,
    delta_time: f32,
) {
    let distance_z = target_z - *position_z;
    let velocity_z = max_velocity * sign(distance_z);
    let moving_distance = velocity_z * delta_time;
    if distance_z.abs() >= moving_distance.abs() {
        *position_z += moving_distance;
        st.vel[2] = velocity_z;
        st.is_moving = true;
    } else {
        *position_z = target_z;
        st.vel = [0.0; 3];
        st.is_moving = false;
    }
}

/// `GimmickMovementElevator.Tick(delta)`: moves `st.z` towards the next stop.
pub fn elevator_tick(st: &mut MovementState, spawner: &GimmickSpawner, delta_time: f32) {
    let mut z = st.z;
    let is_moving_down = st.move_down;
    let is_in_middle_z = spawner.middle_z > 0.0;
    let vel_z = st.vel[2];
    let target = if is_in_middle_z {
        if z < spawner.middle_z && vel_z >= 0.0 && !is_moving_down {
            spawner.middle_z
        } else if z < spawner.top_z && vel_z >= 0.0 && !is_moving_down {
            spawner.top_z
        } else if z > spawner.middle_z && vel_z <= 0.0 && is_moving_down {
            spawner.middle_z
        } else {
            spawner.bottom_z
        }
    } else if z < spawner.top_z && vel_z >= 0.0 {
        spawner.top_z
    } else {
        spawner.bottom_z
    };
    move_along_z_axis(st, &mut z, target, ELEVATOR_MAX_VELOCITY, delta_time);
    st.z = z; // Transform.Local.SetHeight(position.Z)
}

/// `GimmickMovementElevator.AfterMove`: once stopped, waits `WaitTime` seconds and picks the
/// next direction. `last_pos_z` = `LastPos.Z` (already the current position). Returns the new
/// `WaitTime` (unix ms) when it was set.
pub fn elevator_after_move(
    st: &mut MovementState,
    last_pos_z: f32,
    spawner: &GimmickSpawner,
    now_ms: i64,
) -> Option<i64> {
    if st.is_moving {
        return None;
    }
    let wait_until = now_ms + (f64::from(spawner.wait_time) * 1000.0) as i64;
    if last_pos_z > spawner.bottom_z && st.move_down {
        st.move_down = true;
    } else if last_pos_z < spawner.top_z && !st.move_down {
        st.move_down = false;
    } else {
        st.move_down = !st.move_down;
    }
    Some(wait_until)
}

/// `GimmickMovementFloatToSurface.Tick`: the Z translation to apply, when the point
/// `movement + 1` m above the gimmick is still water (`is_water`: the server asks
/// `systems::ai::physics::is_water`).
pub fn float_to_surface_tick(delta_time: f32, is_water: impl FnOnce(f32) -> bool) -> Option<f32> {
    let movement = FLOAT_TO_SURFACE_SPEED * delta_time;
    is_water(movement + 1.0).then_some(movement)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn spawner() -> GimmickSpawner {
        GimmickSpawner {
            top_z: 294.17,
            middle_z: 0.0,
            bottom_z: 261.73,
            wait_time: 6.0,
            ..GimmickSpawner::default()
        }
    }

    /// One `GimmickTick` worth of state changes for an elevator (handler tick, the
    /// `Vel = deltaPosition * deltaTime` overwrite, AfterMove).
    fn full_tick(st: &mut MovementState, sp: &GimmickSpawner, dt: f32, now: i64) -> Option<i64> {
        let last = st.z;
        elevator_tick(st, sp, dt);
        let d = st.z - last;
        st.vel = [0.0, 0.0, d * dt]; // C# bug: multiplied, not divided
        elevator_after_move(st, st.z, sp, now)
    }

    #[test]
    fn move_along_z_axis_steps_and_snaps() {
        let mut st = MovementState::default();
        let mut z = 10.0f32;
        move_along_z_axis(&mut st, &mut z, 11.0, 4.5, 0.05);
        assert_eq!(z, 10.0 + 4.5 * 0.05);
        assert_eq!(st.vel[2], 4.5);
        assert!(st.is_moving);
        let mut z = 10.95f32;
        st.vel = [1.0, 2.0, 3.0];
        move_along_z_axis(&mut st, &mut z, 11.0, 4.5, 0.05);
        assert_eq!(z, 11.0);
        assert_eq!(st.vel, [0.0; 3]);
        assert!(!st.is_moving);
        // On the target: zero move, still "moving" (C#).
        move_along_z_axis(&mut st, &mut z, 11.0, 4.5, 0.05);
        assert_eq!(z, 11.0);
        assert!(st.is_moving);
        assert_eq!(st.vel[2], 0.0);
    }

    #[test]
    fn elevator_goes_up_waits_and_comes_down() {
        let sp = spawner();
        let mut st = MovementState {
            z: 261.76044,
            ..Default::default()
        };
        let dt = 0.05f32;
        // Up: 32.4 m at 4.5 m/s = 144 ticks of 0.225 m.
        let mut ticks = 0;
        let wait = loop {
            ticks += 1;
            if let Some(w) = full_tick(&mut st, &sp, dt, 1000) {
                break w;
            }
            assert!(st.z <= sp.top_z && ticks < 1000);
        };
        assert_eq!(st.z, sp.top_z);
        assert!((143..=146).contains(&ticks), "{ticks}");
        assert_eq!(wait, 7000); // 1000 + 6 s
        assert!(st.move_down);
        // Down again after the wait.
        let before = st.z;
        full_tick(&mut st, &sp, dt, 8000);
        assert!(st.z < before);
        assert!((before - st.z - 0.225).abs() < 1e-4);
        let mut ticks = 1;
        while full_tick(&mut st, &sp, dt, 8000).is_none() {
            ticks += 1;
            assert!(ticks < 1000);
        }
        assert_eq!(st.z, sp.bottom_z);
        assert!(!st.move_down);
    }

    #[test]
    fn elevator_with_middle_stop() {
        let sp = GimmickSpawner {
            middle_z: 270.0,
            ..spawner()
        };
        let mut st = MovementState {
            z: 261.73,
            ..Default::default()
        };
        while full_tick(&mut st, &sp, 0.05, 0).is_none() {}
        assert_eq!(st.z, 270.0);
        assert!(!st.move_down); // 270 < top and not moving down
        while full_tick(&mut st, &sp, 0.05, 0).is_none() {}
        assert_eq!(st.z, sp.top_z);
        assert!(st.move_down);
        while full_tick(&mut st, &sp, 0.05, 0).is_none() {}
        assert_eq!(st.z, 270.0);
        assert!(st.move_down);
        while full_tick(&mut st, &sp, 0.05, 0).is_none() {}
        assert_eq!(st.z, sp.bottom_z);
    }

    #[test]
    fn float_to_surface_needs_water() {
        assert_eq!(float_to_surface_tick(0.05, |_| false), None);
        assert_eq!(float_to_surface_tick(0.05, |h| h > 1.0), Some(0.125));
    }
}
