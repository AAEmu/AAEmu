//! `Models/Game/Units/Mate` (mounts / pets): identity and the state the visibility packets
//! read. Templates, experience, skills, mounting and persistence belong to the mate subsystem.

use crate::impl_unit_data;
use crate::models::item::Item;
use crate::models::npc::UNIT_EQUIPMENT_SLOTS;
use crate::models::unit::{unit_type_flag, BaseUnitType, UnitBase};

/// `MatePassengerInfo`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct MatePassengerInfo {
    pub obj_id: u32,
    /// `AttachUnitReason`.
    pub reason: u8,
}

/// `Mate`.
#[derive(Debug, Clone, PartialEq)]
pub struct Mate {
    pub unit: UnitBase,
    /// `Id` (mates table id).
    pub id: u32,
    pub owner_obj_id: u32,
    /// `Passengers` (attach point -> passenger), in insertion order.
    pub passengers: Vec<(u8, MatePassengerInfo)>,
    /// `ItemId` (the summoning item).
    pub item_id: u64,
    pub user_state: u8,
    pub experience: i32,
    pub mileage: i32,
    pub spawn_delay_time: u32,
    /// `Skills` (learned skill ids).
    pub skills: Vec<u32>,
    /// `Equipment` (28 slots).
    pub equipment: Vec<Option<Item>>,
}

impl Mate {
    pub fn new(id: u32, template_id: u32) -> Self {
        Self {
            unit: UnitBase {
                template_id,
                ..UnitBase::default()
            },
            id,
            owner_obj_id: 0,
            passengers: Vec::new(),
            item_id: 0,
            user_state: 0,
            experience: 0,
            mileage: 0,
            spawn_delay_time: 0,
            skills: Vec::new(),
            equipment: vec![None; UNIT_EQUIPMENT_SLOTS],
        }
    }
}

impl_unit_data!(Mate, BaseUnitType::Mate, unit_type_flag::MATE);
