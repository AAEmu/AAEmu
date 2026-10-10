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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn house_state_layout() {
        let mut h = House::new(0x0A0B0C0D, 300);
        h.unit.tl_id = 7;
        h.unit.model_id = 0x10;
        h.unit.owner_id = 5;
        h.unit.name = "Hut".into();
        h.co_owner_id = 6;
        h.account_id = 9;
        h.permission = housing_permission::PUBLIC;
        h.current_step = 1;
        h.all_action = 40;
        h.base_action = 3;
        h.num_action = 4;
        h.tax = 250;
        h.allow_recover = true;
        h.sell_price = 1000;
        h.sell_to_player_id = 8;
        let pos = PositionAndRotation::new(1.0, 2.0, 3.5, 0.0, 0.0, 0.0);
        let mut s = PacketStream::new();
        h.write(0x010203, &pos, "Own", "", &mut s);

        let mut e = PacketStream::new();
        e.write_u16(7)
            .write_u32(0x0A0B0C0D)
            .write_bc(0x010203)
            .write_u32(300)
            .write_pisc(&[0x10, 0])
            .write_u32(6)
            .write_u32(5)
            .write_str("Own")
            .write_u32(9)
            .write_u8(2)
            .write_i32(40)
            .write_i32(7) // CurrentAction = base + num
            .write_i32(250)
            .write_i64(4096 << 32)
            .write_i64(8192 << 32)
            .write_f32(3.5)
            .write_str("Hut")
            .write_bool(true)
            .write_u32(1000)
            .write_u32(8)
            .write_str("");
        assert_eq!(s.as_bytes(), e.as_bytes());

        // Finished house: allstep / curstep are 0.
        h.current_step = -1;
        let mut s2 = PacketStream::new();
        h.write(0x010203, &pos, "Own", "", &mut s2);
        assert_eq!(s2.len(), s.len());
        assert_eq!(s2.as_bytes()[34..42], [0; 8]);
    }
}
