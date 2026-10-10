//! `Models/Game/NPChar/Npc` (+ `Models/Game/Units/Portal`, which is an `Npc`): identity and the
//! state the visibility packets read. Templates, spawners, AI, aggro, skills and the formula
//! attributes are ported by the NPC subsystem (`systems::npcs`) on top of this shell.

use crate::impl_unit_data;
use crate::models::item::Item;
use crate::models::transform::Transform;
use crate::models::unit::{unit_type_flag, BaseUnitType, ModelPostureType, UnitBase};

/// `EquipmentContainer` size (`EquipmentItemSlot.Cosplay + 1`).
pub const UNIT_EQUIPMENT_SLOTS: usize = 28;

/// `Portal` (a summoned portal NPC): `TeleportPosition` + `LinkedPortal`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct PortalInfo {
    pub teleport_position: Transform,
    /// `LinkedPortal?.ObjId ?? 0`.
    pub linked_portal_obj_id: u32,
}

/// `Npc`.
#[derive(Debug, Clone, PartialEq)]
pub struct Npc {
    pub unit: UnitBase,
    /// `Template.Gender` / `Template.Race` (`RaceGender => 16 * Gender + Race`).
    pub template_gender: u8,
    pub template_race: u8,
    /// `AnimActionId`: the idle posture for the current time of day (from
    /// `Template.NpcPostureSets`, computed by the NPC subsystem; 0 = none).
    pub anim_action_id: u32,
    /// `Equipment` (`EquipmentContainer`, 28 slots).
    pub equipment: Vec<Option<Item>>,
    /// `Spawner?.Id` (0 = not spawned by a spawner).
    pub spawner_id: u32,
    /// `DeadTime` (unix ms, 0 = `DateTime.MinValue`).
    pub dead_time_ms: i64,
    /// `CanFly`.
    pub can_fly: bool,
    /// `CurrentAggroTarget?.ObjId ?? 0`.
    pub current_aggro_target: u32,
    /// Set for `Portal` NPCs.
    pub portal: Option<PortalInfo>,
    /// `Ai != null` (the AI itself belongs to the NPC subsystem).
    pub has_ai: bool,
    /// `Ai.ShouldTick`: set / cleared by the visibility code when players come and go
    /// (`Region.AddToCharacters` / `RemoveFromCharacters`).
    pub ai_should_tick: bool,
}

impl Npc {
    /// A new NPC of `template_id` (`NpcManager.Create` fills the template-derived fields).
    pub fn new(template_id: u32) -> Self {
        Self {
            unit: UnitBase {
                template_id,
                ..UnitBase::default()
            },
            template_gender: 0,
            template_race: 0,
            anim_action_id: 0,
            equipment: vec![None; UNIT_EQUIPMENT_SLOTS],
            spawner_id: 0,
            dead_time_ms: 0,
            can_fly: false,
            current_aggro_target: 0,
            portal: None,
            has_ai: false,
            ai_should_tick: false,
        }
    }

    /// `Equipment.GetItemBySlot(slot)`.
    pub fn equipment_item(&self, slot: usize) -> Option<&Item> {
        self.equipment.get(slot).and_then(Option::as_ref)
    }
}

impl_unit_data!(Npc, BaseUnitType::Npc, unit_type_flag::NPC, {
    fn model_posture_type(&self) -> ModelPostureType {
        if self.anim_action_id > 0 {
            ModelPostureType::ActorModelState
        } else {
            ModelPostureType::None
        }
    }
    fn race_gender(&self) -> u8 {
        16u8.wrapping_mul(self.template_gender)
            .wrapping_add(self.template_race)
    }
});
