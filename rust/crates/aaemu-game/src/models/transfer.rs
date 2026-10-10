//! `Models/Game/Units/Transfer` (carriages, airships, elevators on paths): identity and the
//! state the visibility packets read. Paths, movement and spawners belong to the transfer
//! subsystem.

use crate::impl_unit_data;
use crate::models::unit::{attach_point, unit_type_flag, BaseUnitType, ModelPostureType, UnitBase};

/// `Transfer`.
#[derive(Debug, Clone, PartialEq)]
pub struct Transfer {
    pub unit: UnitBase,
    /// `BondingObjId` (the transfer this one is attached to).
    pub bonding_obj_id: u32,
    /// `AttachPointId` (`AttachPointKind`; `None` (0) by default as in C#).
    pub attach_point_id: u8,
    /// `Bounded?.ObjId ?? 0`.
    pub bounded_obj_id: u32,
    /// `AttachedDoodads` / `AttachedCharacters` (obj ids).
    pub attached_doodads: Vec<u32>,
    pub attached_characters: Vec<u32>,
    pub spawner_id: u32,
    pub path_point_index: i32,
    pub speed: f32,
    pub rot_speed: f32,
    pub reverse: bool,
    /// `WaitTime` (unix ms).
    pub wait_time_ms: i64,
    // ---- movement state (`Transfer` route following) ----
    /// `Steering` (index of the current route, 1 by default).
    pub steering: i32,
    pub throttle: i8,
    /// `Rot` (x, y, z, w).
    pub rot: [f32; 4],
    pub velocity: [f32; 3],
    pub ang_vel: [f32; 3],
    pub rotation_degrees: f32,
    /// `Routes` (route index -> points).
    pub routes: Vec<Vec<crate::data::world_templates::WorldSpawnPosition>>,
    /// `TransferPath` (current route).
    pub transfer_path: Vec<crate::data::world_templates::WorldSpawnPosition>,
    pub move_to_path_enabled: bool,
    pub move_to_forward: bool,
    pub move_step_index: i32,
    pub v_position: [f32; 3],
    pub v_target: [f32; 3],
    pub v_distance: [f32; 3],
    pub v_velocity: [f32; 3],
    pub distance: f32,
    pub max_velocity_forward: f32,
    pub range_to_check_point: f32,
    pub vel_accel: f32,
    pub angle: f64,
    pub mass: f32,
    pub max_force: f32,
}

impl Transfer {
    pub fn new(template_id: u32) -> Self {
        Self {
            unit: UnitBase {
                template_id,
                ..UnitBase::default()
            },
            bonding_obj_id: 0,
            attach_point_id: attach_point::NONE,
            bounded_obj_id: 0,
            attached_doodads: Vec::new(),
            attached_characters: Vec::new(),
            spawner_id: 0,
            path_point_index: 0,
            speed: 0.0,
            rot_speed: 0.0,
            reverse: false,
            wait_time_ms: 0,
            steering: 1,
            throttle: 0,
            rot: [0.0; 4],
            velocity: [0.0; 3],
            ang_vel: [0.0; 3],
            rotation_degrees: 0.0,
            routes: Vec::new(),
            transfer_path: Vec::new(),
            move_to_path_enabled: false,
            move_to_forward: false,
            move_step_index: 0,
            v_position: [0.0; 3],
            v_target: [0.0; 3],
            v_distance: [0.0; 3],
            v_velocity: [0.0; 3],
            distance: 0.0,
            max_velocity_forward: 5.4,
            range_to_check_point: 1.5,
            vel_accel: 0.3,
            angle: 0.0,
            mass: 0.0,
            max_force: 0.0,
        }
    }

    /// `TimeLeft`.
    pub fn time_left(&self, now_ms: i64) -> u32 {
        if self.wait_time_ms > now_ms {
            (self.wait_time_ms - now_ms) as u32
        } else {
            0
        }
    }
}

impl_unit_data!(
    Transfer,
    BaseUnitType::Transfer,
    unit_type_flag::TRANSFER,
    {
        fn model_posture_type(&self) -> ModelPostureType {
            ModelPostureType::TurretState
        }
    }
);
