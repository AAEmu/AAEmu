//! `Models/Game/Housing/House`: identity and the `Write` format of `SCHouseStatePacket`.
//! Templates, building steps, taxes, permissions and persistence belong to the housing
//! subsystem.

use aaemu_commons::PacketStream;

use crate::impl_unit_data;
use crate::models::item::{Item, UnixTime};
use crate::models::npc::UNIT_EQUIPMENT_SLOTS;
use crate::models::transform::{convert_long, PositionAndRotation};
use crate::models::unit::{unit_type_flag, BaseUnitType, ModelPostureType, UnitBase};

/// `HousingPermission`.
pub mod housing_permission {
    pub const PRIVATE: u8 = 0;
    pub const GUILD: u8 = 1;
    pub const PUBLIC: u8 = 2;
    pub const FAMILY: u8 = 3;
}

/// `House`.
#[derive(Debug, Clone, PartialEq)]
pub struct House {
    pub unit: UnitBase,
    /// `Id` (housings table id).
    pub id: u32,
    pub account_id: u32,
    pub co_owner_id: u32,
    /// `CurrentStep` (-1 = finished).
    pub current_step: i32,
    /// `Template.BuildSteps.Count`.
    pub build_step_count: i32,
    pub all_action: i32,
    pub base_action: i32,
    pub num_action: i32,
    /// `HousingPermission` ([`housing_permission`]).
    pub permission: u8,
    /// `Template?.Taxation?.Tax ?? 0`.
    pub tax: i32,
    pub place_date: UnixTime,
    pub protection_end_date: UnixTime,
    pub allow_recover: bool,
    pub sell_to_player_id: u32,
    pub sell_price: u32,
    /// `AttachedDoodads` (obj ids, sent together with the house).
    pub attached_doodads: Vec<u32>,
    /// `Equipment` (28 slots).
    pub equipment: Vec<Option<Item>>,
    pub is_dirty: bool,
}

impl House {
    pub fn new(id: u32, template_id: u32) -> Self {
        Self {
            unit: UnitBase {
                template_id,
                ..UnitBase::default()
            },
            id,
            account_id: 0,
            co_owner_id: 0,
            current_step: 0,
            build_step_count: 0,
            all_action: 0,
            base_action: 0,
            num_action: 0,
            permission: housing_permission::PRIVATE,
            tax: 0,
            place_date: 0,
            protection_end_date: 0,
            allow_recover: false,
            sell_to_player_id: 0,
            sell_price: 0,
            attached_doodads: Vec::new(),
            equipment: vec![None; UNIT_EQUIPMENT_SLOTS],
            is_dirty: false,
        }
    }

    /// `CurrentAction`.
    pub fn current_action(&self) -> i32 {
        self.base_action + self.num_action
    }

    /// The `SCUnitStatePacket` build step: 0 when finished, else
    /// `-BuildSteps.Count + CurrentStep`.
    pub fn net_build_step(&self) -> i16 {
        if self.current_step == -1 {
            0
        } else {
            (-self.build_step_count + self.current_step) as i16
        }
    }

    /// `House.Write` (`SCHouseStatePacket`). `owner_name` / `sell_to_name` are
    /// `NameManager.GetCharacterName(OwnerId / SellToPlayerId) ?? ""`.
    pub fn write(
        &self,
        obj_id: u32,
        world: &PositionAndRotation,
        owner_name: &str,
        sell_to_name: &str,
        s: &mut PacketStream,
    ) {
        s.write_u16(self.unit.tl_id)
            .write_u32(self.id)
            .write_bc(obj_id)
            .write_u32(self.unit.template_id)
            .write_pisc(&[i64::from(self.unit.model_id), 0])
            .write_u32(self.co_owner_id)
            .write_u32(self.unit.owner_id)
            .write_str(owner_name)
            .write_u32(self.account_id)
            .write_u8(self.permission);
        if self.current_step == -1 {
            s.write_i32(0).write_i32(0);
        } else {
            s.write_i32(self.all_action)
                .write_i32(self.current_action());
        }
        s.write_i32(self.tax)
            .write_i64(convert_long(world.x))
            .write_i64(convert_long(world.y))
            .write_f32(world.z)
            .write_str(&self.unit.name)
            .write_bool(self.allow_recover)
            .write_u32(self.sell_price)
            .write_u32(self.sell_to_player_id)
            .write_str(sell_to_name);
    }
}

impl_unit_data!(House, BaseUnitType::Housing, unit_type_flag::HOUSING, {
    fn model_posture_type(&self) -> ModelPostureType {
        ModelPostureType::HouseState
    }
});
