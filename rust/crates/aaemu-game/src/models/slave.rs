//! `Models/Game/Units/Slave` (vehicles / ships / siege): identity and the state the visibility
//! packets read. Templates, spawning, physics, ship control and persistence belong to the
//! slave subsystem.

use crate::impl_unit_data;
use crate::models::item::Item;
use crate::models::npc::UNIT_EQUIPMENT_SLOTS;
use crate::models::unit::{unit_type_flag, BaseUnitType, ModelPostureType, UnitBase};

/// `Slave`.
#[derive(Debug, Clone, PartialEq)]
pub struct Slave {
    pub unit: UnitBase,
    /// `Id` (BaseUnit.Id: the slaves table id).
    pub id: u32,
    /// `Summoner?.Id ?? 0` (character id).
    pub summoner_id: u32,
    /// `Summoner?.ObjId ?? 0`.
    pub summoner_obj_id: u32,
    /// `Summoner?.Name ?? ""`.
    pub summoner_name: String,
    /// `OwnerType`.
    pub owner_type: BaseUnitType,
    /// `AttachPointId` (-1 = not attached to its owner).
    pub attach_point_id: i8,
    /// `OwnerObjId` (the unit this slave is attached to).
    pub owner_obj_id: u32,
    /// `BondingObjId`.
    pub bonding_obj_id: u32,
    /// `AttachedCharacters` (attach point -> character obj id), in insertion order.
    pub attached_characters: Vec<(u8, u32)>,
    /// `AttachedDoodads` / `AttachedSlaves` (obj ids; the objects are transform children).
    pub attached_doodads: Vec<u32>,
    pub attached_slaves: Vec<u32>,
    /// `Equipment` (28 slots).
    pub equipment: Vec<Option<Item>>,
    /// `SpawnTime` (unix ms).
    pub spawn_time_ms: i64,
}

impl Slave {
    pub fn new(id: u32, template_id: u32) -> Self {
        Self {
            unit: UnitBase {
                template_id,
                ..UnitBase::default()
            },
            id,
            summoner_id: 0,
            summoner_obj_id: 0,
            summoner_name: String::new(),
            owner_type: BaseUnitType::Character,
            attach_point_id: -1,
            owner_obj_id: 0,
            bonding_obj_id: 0,
            attached_characters: Vec::new(),
            attached_doodads: Vec::new(),
            attached_slaves: Vec::new(),
            equipment: vec![None; UNIT_EQUIPMENT_SLOTS],
            spawn_time_ms: 0,
        }
    }
}

impl_unit_data!(Slave, BaseUnitType::Slave, unit_type_flag::SLAVE, {
    fn model_posture_type(&self) -> ModelPostureType {
        ModelPostureType::TurretState
    }
});
