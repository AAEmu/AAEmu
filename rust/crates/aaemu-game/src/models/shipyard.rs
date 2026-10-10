//! `Models/Game/Shipyard/Shipyard` + `ShipyardData` (the wire format of
//! `SCShipyardStatePacket`). Building steps and persistence belong to the shipyard subsystem.

use aaemu_commons::{Marshal, PacketStream};

use crate::impl_unit_data;
use crate::models::item::UnixTime;
use crate::models::transform::convert_long;
use crate::models::unit::{unit_type_flag, BaseUnitType, UnitBase};

/// `ShipyardData`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct ShipyardData {
    pub id: u64,
    pub template_id: u32,
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub z_rot: f32,
    pub money_amount: i32,
    pub actions: i32,
    pub kind: u32,
    pub kind2: u32,
    pub owner_name: String,
    /// `Type3` (`FactionsEnum`).
    pub kind3: u32,
    /// `Spawned` (unix seconds).
    pub spawned: UnixTime,
    pub obj_id: u32,
    pub hp: i32,
    pub step: i32,
}

impl Marshal for ShipyardData {
    /// `ShipyardData.Write`.
    fn write(&self, s: &mut PacketStream) {
        s.write_u64(self.id)
            .write_u32(self.template_id)
            .write_i64(convert_long(self.x))
            .write_i64(convert_long(self.y))
            .write_f32(self.z)
            .write_f32(self.z_rot)
            .write_i32(self.money_amount)
            .write_i32(self.actions)
            .write_u32(self.kind)
            .write_u32(self.kind2)
            .write_str(&self.owner_name)
            .write_u32(self.kind3)
            .write_i64(self.spawned)
            .write_bc(self.obj_id)
            .write_i32(self.hp);
    }
}

/// `Shipyard`.
#[derive(Debug, Clone, PartialEq)]
pub struct Shipyard {
    pub unit: UnitBase,
    pub data: ShipyardData,
    pub current_step: i32,
    pub all_action: i32,
    pub num_action: i32,
    /// `BaseAction`.
    pub base_action: i32,
    /// `IsDirty`.
    pub is_dirty: bool,
}

impl Shipyard {
    pub fn new(data: ShipyardData) -> Self {
        Self {
            unit: UnitBase {
                template_id: data.template_id,
                ..UnitBase::default()
            },
            data,
            current_step: 0,
            all_action: 0,
            num_action: 0,
            base_action: 0,
            is_dirty: true,
        }
    }
}

// `Scale => 1.0f` (the UnitBase default).
impl_unit_data!(Shipyard, BaseUnitType::Shipyard, unit_type_flag::SHIPYARD);
