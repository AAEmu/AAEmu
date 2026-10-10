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
    /// `SummoningItem?.Id ?? 0`.
    pub summoning_item_id: u64,
    /// `Spawner` (the vehicles subsystem's slave spawner id, 0 = none).
    pub spawner_id: u32,
    /// `Spawner?.DespawnTime` (`None` = no spawner).
    pub spawner_despawn_time: Option<i32>,
    /// `ThrottleRequest` / `SteeringRequest` (`ShipRequestMoveType`, read by the physics).
    pub throttle_request: i8,
    pub steering_request: i8,
    /// `Throttle` / `Steering` (physics output).
    pub throttle: i8,
    pub steering: i8,
    /// `HarpoonRope` (`ShipHarpoonRopeState`).
    pub harpoon_rope: ShipHarpoonRopeState,
    /// `LeaveTask` / `CancelTokenSource` (generation of the pending despawn task, 0 = none).
    pub leave_task: u64,
}

/// `ShipHarpoonRopeState`.
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct ShipHarpoonRopeState {
    pub is_engaged: bool,
    /// `HookWorld` (world hook when `hook_basis_obj_id` is 0).
    pub hook_world: [f32; 3],
    pub hook_basis_obj_id: u32,
    pub hook_local_in_basis: [f32; 3],
    pub rope_length: f32,
    pub last_teared: bool,
    pub last_cutout: bool,
    pub max_launch_range: f32,
    pub hook_attached_to_terrain: bool,
    /// `ControllerExpireAtUtc` (unix ms).
    pub controller_expire_at_ms: Option<i64>,
}

impl ShipHarpoonRopeState {
    /// `Clear()`.
    pub fn clear(&mut self) {
        *self = Self::default();
    }
}

impl Slave {
    pub fn new(id: u32, template_id: u32) -> Self {
        Self {
            unit: UnitBase {
                template_id,
                // `HpTriggerPointsPercent` (constructor)
                hp_trigger_points_percent: vec![0, 25, 50, 75, 100],
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
            summoning_item_id: 0,
            spawner_id: 0,
            spawner_despawn_time: None,
            throttle_request: 0,
            steering_request: 0,
            throttle: 0,
            steering: 0,
            harpoon_rope: ShipHarpoonRopeState::default(),
            leave_task: 0,
        }
    }

    /// `AttachedCharacters.ContainsKey(point)`.
    pub fn has_attached_at(&self, point: u8) -> bool {
        self.attached_characters.iter().any(|(p, _)| *p == point)
    }
}

impl_unit_data!(Slave, BaseUnitType::Slave, unit_type_flag::SLAVE, {
    fn model_posture_type(&self) -> ModelPostureType {
        ModelPostureType::TurretState
    }
});
