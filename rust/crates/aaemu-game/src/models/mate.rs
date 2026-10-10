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
    /// `Template.MateKindId` (the formulas' `mate_kind`).
    pub mate_kind_id: i32,
    /// `MateXpUpdateTask` (generation of the pending task, 0 = none).
    pub xp_task: u64,
    /// `SusManager` / despawn bookkeeping: the mate was deleted (`Delete()` ran).
    pub deleted: bool,
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
            mate_kind_id: 0,
            xp_task: 0,
            deleted: false,
        }
    }

    /// `Passengers.TryGetValue(attachPoint)`.
    pub fn passenger(&self, point: u8) -> Option<&MatePassengerInfo> {
        self.passengers
            .iter()
            .find(|(p, _)| *p == point)
            .map(|(_, i)| i)
    }

    /// Mutable `Passengers[attachPoint]`.
    pub fn passenger_mut(&mut self, point: u8) -> Option<&mut MatePassengerInfo> {
        self.passengers
            .iter_mut()
            .find(|(p, _)| *p == point)
            .map(|(_, i)| i)
    }
}

impl_unit_data!(Mate, BaseUnitType::Mate, unit_type_flag::MATE);
