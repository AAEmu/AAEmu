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
