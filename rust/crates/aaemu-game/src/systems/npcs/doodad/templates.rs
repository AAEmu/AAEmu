//! Doodad template data: `Models/Game/DoodadObj/Templates` (`DoodadTemplate`,
//! `DoodadCofferTemplate`), `DoodadFunc`, `DoodadPhaseFunc`, `DoodadFuncGroups`, `DoodadGroups`,
//! `Details/DoodadFuncConsumeChangerItem`, the `DoodadObj/Static` enums and one [`FuncTemplate`]
//! variant per class of `Models/Game/DoodadObj/Funcs` (121 classes).
//!
//! Only data lives here. The C# `Use` bodies are behaviour: implement them on [`FuncTemplate`]
//! (`impl FuncTemplate { pub fn use_(..) }`) in the doodad behaviour modules. A template is found
//! with `DoodadData::func_template(func_id, func_type)` (classes deriving from
//! `DoodadFuncTemplate`, [`FuncTemplate::is_phase_func`] = false) or
//! `DoodadData::phase_func_template(func_id, func_type)` (`DoodadPhaseFuncTemplate`).
//!
//! C# enum-typed fields keep the raw value that the C# cast produces (a C# enum holds any value of
//! its underlying type): `AttachPointKind` / `SkillTargetRelation` -> `u8`, `BondKind` /
//! `FactionsEnum` -> `u32`, `WorldInteractionType` / `Climate` -> `i32`.

/// `AttachPointKind` (`DoodadObj.Static`, byte enum).
pub mod attach_point_kind {
    pub use crate::models::unit::attach_point::*;
    pub const PASSENGER1: u8 = 3;
    pub const PASSENGER2: u8 = 4;
    pub const PASSENGER3: u8 = 5;
    pub const PASSENGER4: u8 = 6;
    pub const PASSENGER5: u8 = 7;
    pub const PASSENGER6: u8 = 8;
    pub const CANNON0: u8 = 9;
    pub const CANNON1: u8 = 10;
    pub const CANNON2: u8 = 11;
    pub const CANNON3: u8 = 12;
    pub const CANNON4: u8 = 13;
    pub const CANNON5: u8 = 14;
    pub const CANNON6: u8 = 15;
    pub const CANNON7: u8 = 16;
    pub const CANNON8: u8 = 17;
    pub const HOOK: u8 = 18;
    pub const MAST0: u8 = 19;
    pub const MAST1: u8 = 20;
    pub const SAIL0: u8 = 21;
    pub const SAIL1: u8 = 22;
    pub const ANCHOR: u8 = 23;
    pub const LADDER_LEFT: u8 = 24;
    pub const LADDER_RIGHT: u8 = 25;
    pub const PLANK_LEFT: u8 = 26;
    pub const PLANK_RIGHT: u8 = 27;
    pub const LAMP_FRONT: u8 = 28;
    pub const LAMP_REAR: u8 = 29;
    pub const TRAILED0: u8 = 30;
    pub const TRAILED1: u8 = 31;
    pub const TRAILED2: u8 = 32;
    pub const TRAILED3: u8 = 33;
    pub const TRAILED4: u8 = 34;
    pub const HELMS: u8 = 35;
    pub const HEAL_POINT0: u8 = 36;
    pub const HEAL_POINT1: u8 = 37;
    pub const HEAL_POINT2: u8 = 38;
    pub const HEAL_POINT3: u8 = 39;
    pub const HEAL_POINT4: u8 = 40;
    pub const HEAL_POINT5: u8 = 41;
    pub const HEAL_POINT6: u8 = 42;
    pub const HEAL_POINT7: u8 = 43;
    pub const HEAL_POINT8: u8 = 44;
    pub const HEAL_POINT9: u8 = 45;
    pub const CANNON9: u8 = 46;
    pub const CANNON10: u8 = 47;
    pub const CANNON11: u8 = 48;
    pub const CANNON12: u8 = 49;
    pub const CANNON13: u8 = 50;
    pub const CANNON14: u8 = 51;
    pub const CANNON15: u8 = 52;
    pub const CANNON16: u8 = 53;
    pub const CANNON17: u8 = 54;
    pub const CANNON18: u8 = 55;
    pub const CANNON19: u8 = 56;
    pub const NAME_PLATE01: u8 = 57;
    pub const LADDER_REAR_LEFT: u8 = 58;
    pub const LADDER_REAR_RIGHT: u8 = 59;
    pub const BOX0: u8 = 60;
    pub const BOX1: u8 = 61;
    pub const BOX2: u8 = 62;
    pub const BOX3: u8 = 63;
    pub const BOX4: u8 = 64;
    pub const BOX5: u8 = 65;
    pub const BOX6: u8 = 66;
    pub const BOX7: u8 = 67;
    pub const BOX8: u8 = 68;
    pub const BOX9: u8 = 69;
    pub const BOX10: u8 = 70;
    pub const BOX11: u8 = 71;
    pub const BOX12: u8 = 72;
    pub const BOX13: u8 = 73;
    pub const BOX14: u8 = 74;
    pub const BOX15: u8 = 75;
    pub const BOX16: u8 = 76;
    pub const BOX17: u8 = 77;
    pub const BOX18: u8 = 78;
    pub const BOX19: u8 = 79;
    pub const TELESCOPE: u8 = 80;
    pub const BELL: u8 = 81;
    pub const RUDDER: u8 = 82;
    pub const SAIL2: u8 = 83;
    pub const MAST2: u8 = 84;
}

/// `AttachUnitReason` (`DoodadObj.Static`, byte enum).
pub mod attach_unit_reason {
    pub const NONE: u8 = 0;
    pub const MOUNT_MATE_LEFT: u8 = 1;
    pub const MOUNT_MATE_RIGHT: u8 = 2;
    pub const MOUNT_MATE_BACK: u8 = 3;
    pub const MOUNT_MATE_FORWARD: u8 = 4;
    pub const SLAVE_BINDING: u8 = 5;
    pub const NEW_MASTER: u8 = 6;
    pub const BOARD_TRANSFER: u8 = 7;
    pub const FOUND_MISSED_PARENT: u8 = 8;
    pub const INIT_FROM_NUB: u8 = 9;
    pub const PREFAB_CHANGED: u8 = 10;
    pub const TRANSFER_BINDING: u8 = 11;
    pub const HOUSING_SLAVE_BINDING: u8 = 12;
}

/// `BondKind` (`DoodadObj.Static`, uint enum).
pub mod bond_kind {
    pub const NONE: u32 = 0;
    pub const BOND_INVALID: u32 = 1;
    pub const BOND_CHAIR_SINGLE: u32 = 2;
    pub const BOND_CHAIR_DOUBLE: u32 = 3;
    pub const BOND_CHAIR_TRIPLE: u32 = 4;
    pub const BOND_CHAIR_QUADRA: u32 = 5;
    pub const BOND_BED_SINGLE: u32 = 6;
    pub const BOND_BED_DOUBLE: u32 = 7;
    pub const BOND_PUNISH_STAND: u32 = 8;
    pub const BOND_PUNISH_KNEE: u32 = 9;
    pub const BOND_DOLL_SIT: u32 = 10;
}

/// `DoodadFuncPermission` (`DoodadObj.Static`, byte enum).
pub mod doodad_func_permission {
    pub const ANY: u8 = 0;
    pub const PERMISSION1: u8 = 1;
    pub const PERMISSION2: u8 = 2;
    pub const OWNER_ONLY: u8 = 3;
    pub const PERMISSION4: u8 = 4;
    pub const OWNER_RAID_MEMBERS: u8 = 5;
    pub const SAME_ACCOUNT: u8 = 6;
    pub const ZONE_RESIDENTS: u8 = 8;
}

/// `ModelKind` (`DoodadObj.Static`, uint enum).
pub mod model_kind {
    pub const DEFAULT: u32 = 1;
    pub const PLANT: u32 = 2;
    pub const FLOWER: u32 = 3;
    pub const GRASS: u32 = 4;
    pub const TREE_BIG: u32 = 5;
    pub const TREE_MID: u32 = 6;
    pub const TREE_SMALL: u32 = 7;
}

/// `DoodadFuncGroups.DoodadFuncGroupKind`.
pub mod doodad_func_group_kind {
    pub const START: u32 = 1;
    pub const NORMAL: u32 = 2;
    pub const END: u32 = 3;
}

/// `DoodadFuncGroups` (`doodad_func_groups`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncGroups {
    pub id: u32,
    /// `Almighty` (`doodad_almighty_id`, the template id).
    pub almighty: u32,
    /// `GroupKindId` ([`doodad_func_group_kind`]).
    pub group_kind_id: u32,
    pub sound_id: u32,
    /// `Model`: always `None` (C# null). Only the first, discarded `doodad_func_groups` pass of
    /// `DoodadManager.Load` reads the `model` column; the pass that keeps the groups does not.
    pub model: Option<String>,
}

/// `DoodadGroups` (`doodad_groups`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct DoodadGroups {
    pub id: u32,
    pub guard_on_field_time: u32,
    pub is_export: bool,
    pub removed_by_house: bool,
}

/// `DoodadTemplate` (`doodad_almighties`); a `DoodadCofferTemplate` when
/// [`DoodadTemplate::coffer_capacity`] is set.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadTemplate {
    pub id: u32,
    pub once_one_man: bool,
    pub once_one_interaction: bool,
    pub mgmt_spawn: bool,
    pub percent: i32,
    pub min_time: i32,
    pub max_time: i32,
    /// `ModelKindId` ([`model_kind`]).
    pub model_kind_id: u32,
    pub use_creator_faction: bool,
    pub force_tod_top_priority: bool,
    pub milestone_id: u32,
    pub group_id: u32,
    /// `Group`: the `DoodadGroups` row of `group_id` (always present: a missing group fails the
    /// load, as the C# `GameException`).
    pub group: DoodadGroups,
    pub use_target_decal: bool,
    pub use_target_silhouette: bool,
    pub use_target_highlight: bool,
    pub target_decal_size: f32,
    pub sim_radius: i32,
    pub collide_ship: bool,
    pub collide_vehicle: bool,
    /// `ClimateId` (`Climate`, int enum).
    pub climate_id: i32,
    pub save_indun: bool,
    pub force_up_action: bool,
    pub parentable: bool,
    pub childable: bool,
    /// `FactionId` (`FactionsEnum`).
    pub faction_id: u32,
    pub growth_time: i32,
    pub despawn_on_collision: bool,
    pub no_collision: bool,
    pub restrict_zone_id: u32,
    /// `FuncGroups`, in `doodad_func_groups` row order.
    pub func_groups: Vec<DoodadFuncGroups>,
    /// Helper property `TotalDoodadGrowthTime` (`CreateTemplateCaches`).
    pub total_doodad_growth_time: i32,
    /// `DoodadCofferTemplate.Capacity` (`Some` = the C# object is a `DoodadCofferTemplate`).
    pub coffer_capacity: Option<i32>,
}

impl DoodadTemplate {
    /// `template is DoodadCofferTemplate`.
    pub fn is_coffer(&self) -> bool {
        self.coffer_capacity.is_some()
    }

    /// `GrantsVocationWhenUsed`: the group gives vocation badges when used.
    pub fn grants_vocation_when_used(&self) -> bool {
        matches!(self.group_id, 2 | 3 | 4 | 5 | 12 | 39 | 40 | 65)
    }
}

/// `DoodadFunc` (`doodad_funcs`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFunc {
    /// `GroupId` (`doodad_func_group_id`).
    pub group_id: u32,
    /// `FuncId` (`actual_func_id`, the template id in the `func_type` table).
    pub func_id: u32,
    /// `FuncKey` (`id`, the `doodad_funcs` row id; `GetFunc(funcId)` looks this up).
    pub func_key: u32,
    /// `FuncType` (`actual_func_type`, the C# class name).
    pub func_type: String,
    pub next_phase: i32,
    pub sound_id: u32,
    /// `SkillId` (`func_skill_id`).
    pub skill_id: u32,
    pub perm_id: u32,
    /// `Count` (`act_count`).
    pub count: i32,
}

/// `DoodadPhaseFunc` (`doodad_phase_funcs`). The C# runtime helper `PulseTriggered` lives on the
/// shared list object (state shared by every doodad of the group); it is not part of the data.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadPhaseFunc {
    pub group_id: u32,
    pub func_id: u32,
    pub func_type: String,
}

/// `Details/DoodadFuncConsumeChangerItem` (`doodad_func_consume_changer_items`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct DoodadFuncConsumeChangerItem {
    pub id: u32,
    pub doodad_func_consume_changer_id: u32,
    pub item_id: u32,
}

/// `DoodadFuncAnimate` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncAnimate {
    pub id: u32,
    pub name: String,
    pub play_once: bool,
}

/// `DoodadFuncAreaTrigger` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncAreaTrigger {
    pub id: u32,
    pub npc_id: u32,
    pub is_enter: bool,
}

/// `DoodadFuncAttachment` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncAttachment {
    pub id: u32,
    /// `AttachPointKind` ([`attach_point_kind`]).
    pub attach_point_id: u8,
    pub space: i32,
    /// `BondKind` ([`bond_kind`]).
    pub bond_kind_id: u32,
}

/// `DoodadFuncAuctionUi` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncAuctionUi {
    pub id: u32,
}

/// `DoodadFuncBankUi` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncBankUi {
    pub id: u32,
}

/// `DoodadFuncBinding` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncBinding {
    pub id: u32,
    pub district_id: u32,
}

/// `DoodadFuncBubble` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncBubble {
    pub id: u32,
    pub bubble_id: u32,
}

/// `DoodadFuncBuff` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncBuff {
    pub id: u32,
    pub buff_id: u32,
    pub radius: f32,
    pub count: i32,
    pub perm_id: u32,
    pub relationship_id: u32,
}

/// `DoodadFuncButcher` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncButcher {
    pub id: u32,
    pub corpse_model: String,
}

/// `DoodadFuncBuyFish` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncBuyFish {
    pub id: u32,
    pub item_id: u32,
}

/// `DoodadFuncBuyFishItem` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncBuyFishItem {
    pub id: u32,
    pub doodad_func_buy_fish_id: u32,
    pub item_id: u32,
}

/// `DoodadFuncBuyFishModel` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncBuyFishModel {
    pub id: u32,
    pub name: String,
}

/// `DoodadFuncCatch` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncCatch {
    pub id: u32,
}

/// `DoodadFuncCerealHarvest` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncCerealHarvest {
    pub id: u32,
}

/// `DoodadFuncCleanupLogicLink` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncCleanupLogicLink {
    pub id: u32,
}

/// `DoodadFuncClimateReact` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncClimateReact {
    pub id: u32,
    pub next_phase: i32,
}

/// `DoodadFuncClimb` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncClimb {
    pub id: u32,
    pub climb_type_id: u32,
    pub allow_horizontal_multi_hanger: bool,
}

/// `DoodadFuncClout` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncClout {
    pub id: u32,
    pub duration: i32,
    pub tick: i32,
    /// `SkillTargetRelation` (byte enum).
    pub target_relation: u8,
    pub buff_id: u32,
    pub projectile_id: u32,
    pub show_to_friendly_only: bool,
    pub next_phase: i32,
    pub aoe_shape_id: u32,
    pub target_buff_tag_id: u32,
    pub target_no_buff_tag_id: u32,
    pub use_origin_source: bool,
    pub effects: Vec<u32>,
}

/// `DoodadFuncCoffer` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncCoffer {
    pub id: u32,
    pub capacity: i32,
}

/// `DoodadFuncCofferPerm` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncCofferPerm {
    pub id: u32,
}

/// `DoodadFuncConditionalUse` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncConditionalUse {
    pub id: u32,
    pub skill_id: u32,
    pub fake_skill_id: u32,
    pub quest_id: u32,
    pub quest_trigger_phase: u32,
    pub item_id: u32,
    pub item_trigger_phase: u32,
}

/// `DoodadFuncConsumeChanger` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncConsumeChanger {
    pub id: u32,
    pub slot_id: u32,
    pub count: i32,
}

/// `DoodadFuncConsumeChangerModel` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncConsumeChangerModel {
    pub id: u32,
    pub name: String,
}

/// `DoodadFuncConsumeChangerModelItem` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncConsumeChangerModelItem {
    pub id: u32,
    pub doodad_func_consume_changer_model_id: u32,
    pub item_id: u32,
}

/// `DoodadFuncConsumeItem` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncConsumeItem {
    pub id: u32,
    pub item_id: u32,
    pub count: i32,
}

/// `DoodadFuncConvertFish` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncConvertFish {
    pub id: u32,
}

/// `DoodadFuncConvertFishItem` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncConvertFishItem {
    pub id: u32,
    pub doodad_func_convert_fish_id: u32,
    pub item_id: u32,
    pub loot_pack_id: u32,
}

/// `DoodadFuncCraftAct` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncCraftAct {
    pub id: u32,
}

/// `DoodadFuncCraftCancel` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncCraftCancel {
    pub id: u32,
}

/// `DoodadFuncCraftDirect` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncCraftDirect {
    pub id: u32,
    pub next_phase: i32,
}

/// `DoodadFuncCraftGetItem` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncCraftGetItem {
    pub id: u32,
}

/// `DoodadFuncCraftInfo` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncCraftInfo {
    pub id: u32,
}

/// `DoodadFuncCraftPack` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncCraftPack {
    pub id: u32,
    pub craft_pack_id: u32,
}

/// `DoodadFuncCraftStart` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncCraftStart {
    pub id: u32,
}

/// `DoodadFuncCraftStartCraft` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncCraftStartCraft {
    pub id: u32,
    pub doodad_func_craft_start_id: u32,
    pub craft_id: u32,
}

/// `DoodadFuncCropHarvest` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncCropHarvest {
    pub id: u32,
}

/// `DoodadFuncCrystalCollect` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncCrystalCollect {
    pub id: u32,
}

/// `DoodadFuncCutdown` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncCutdown {
    pub id: u32,
}

/// `DoodadFuncCutdowning` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncCutdowning {
    pub id: u32,
}

/// `DoodadFuncDairyCollect` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncDairyCollect {
    pub id: u32,
}

/// `DoodadFuncDeclareSiege` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncDeclareSiege {
    pub id: u32,
}

/// `DoodadFuncDig` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncDig {
    pub id: u32,
}

/// `DoodadFuncDigTerrain` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncDigTerrain {
    pub id: u32,
    pub radius: i32,
    pub life: i32,
}

/// `DoodadFuncDyeingredientCollect` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncDyeingredientCollect {
    pub id: u32,
}

/// `DoodadFuncEnterInstance` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncEnterInstance {
    pub id: u32,
    pub zone_id: u32,
    pub item_id: u32,
}

/// `DoodadFuncEnterSysInstance` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncEnterSysInstance {
    pub id: u32,
    pub zone_id: u32,
    /// `FactionsEnum`.
    pub faction_id: u32,
}

/// `DoodadFuncEvidenceItemLoot` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncEvidenceItemLoot {
    pub id: u32,
    pub skill_id: u32,
    pub crime_value: i16,
    pub crime_kind_id: u32,
}

/// `DoodadFuncExchange` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncExchange {
    pub id: u32,
}

/// `DoodadFuncExitIndun` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncExitIndun {
    pub id: u32,
    pub return_point_id: u32,
}

/// `DoodadFuncFakeUse` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncFakeUse {
    pub id: u32,
    pub skill_id: u32,
    pub fake_skill_id: u32,
    pub target_parent: bool,
}

/// `DoodadFuncFeed` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncFeed {
    pub id: u32,
    pub item_id: u32,
    pub count: i32,
}

/// `DoodadFuncFiberCollect` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncFiberCollect {
    pub id: u32,
}

/// `DoodadFuncFinal` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncFinal {
    pub id: u32,
    pub after: i32,
    pub respawn: bool,
    pub min_time: i32,
    pub max_time: i32,
    pub show_tip: bool,
    pub show_end_time: bool,
    pub tip: String,
}

/// `DoodadFuncFishSchool` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncFishSchool {
    pub id: u32,
    pub npc_spawner_id: u32,
}

/// `DoodadFuncFruitPick` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncFruitPick {
    pub id: u32,
}

/// `DoodadFuncGassExtract` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncGassExtract {
    pub id: u32,
}

/// `DoodadFuncGrowth` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncGrowth {
    pub id: u32,
    pub delay: i32,
    pub start_scale: i32,
    pub end_scale: i32,
    pub next_phase: i32,
}

/// `DoodadFuncHarvest` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncHarvest {
    pub id: u32,
}

/// `DoodadFuncHouseFarm` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncHouseFarm {
    pub id: u32,
    pub item_category_id: u32,
}

/// `DoodadFuncHousingArea` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncHousingArea {
    pub id: u32,
    /// `FactionsEnum`.
    pub faction_id: u32,
    pub radius: i32,
}

/// `DoodadFuncHunger` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncHunger {
    pub id: u32,
    pub hungry_term: i32,
    pub full_step: i32,
    pub phase_change_limit: i32,
    pub next_phase: i32,
}

/// `DoodadFuncInsertCounter` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncInsertCounter {
    pub id: u32,
    pub count: i32,
    pub item_id: u32,
    pub item_count: i32,
}

/// `DoodadFuncLogic` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncLogic {
    pub id: u32,
    pub operation_id: u32,
    pub delay_id: u32,
}

/// `DoodadFuncLogicFamilyProvider` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncLogicFamilyProvider {
    pub id: u32,
    pub family_id: u32,
}

/// `DoodadFuncLogicFamilySubscriber` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncLogicFamilySubscriber {
    pub id: u32,
    pub family_id: u32,
}

/// `DoodadFuncLootItem` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncLootItem {
    pub id: u32,
    /// `WorldInteractionType` (int enum).
    pub world_interaction_id: i32,
    pub item_id: u32,
    pub count_min: i32,
    pub count_max: i32,
    pub percent: i32,
    pub remain_time: i32,
    pub group_id: u32,
}

/// `DoodadFuncLootPack` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncLootPack {
    pub id: u32,
    pub loot_pack_id: u32,
}

/// `DoodadFuncMachinePartsCollect` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncMachinePartsCollect {
    pub id: u32,
}

/// `DoodadFuncMedicalingredientMine` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncMedicalingredientMine {
    pub id: u32,
}

/// `DoodadFuncMow` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncMow {
    pub id: u32,
}

/// `DoodadFuncNaviMarkPosToMap` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncNaviMarkPosToMap {
    pub id: u32,
    pub x: i32,
    pub y: i32,
    pub z: i32,
}

/// `DoodadFuncNaviNaming` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncNaviNaming {
    pub id: u32,
}

/// `DoodadFuncNaviOpenBounty` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncNaviOpenBounty {
    pub id: u32,
}

/// `DoodadFuncNaviOpenMailbox` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncNaviOpenMailbox {
    pub id: u32,
    pub duration: i32,
}

/// `DoodadFuncNaviOpenPortal` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncNaviOpenPortal {
    pub id: u32,
}

/// `DoodadFuncNaviRemove` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncNaviRemove {
    pub id: u32,
    pub req_labor_power: i32,
}

/// `DoodadFuncNaviRemoveTimer` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncNaviRemoveTimer {
    pub id: u32,
    pub after: i32,
}

/// `DoodadFuncNaviTeleport` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncNaviTeleport {
    pub id: u32,
}

/// `DoodadFuncOpenFarmInfo` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncOpenFarmInfo {
    pub id: u32,
    pub farm_id: u32,
}

/// `DoodadFuncOpenPaper` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncOpenPaper {
    pub id: u32,
    pub book_page_id: u32,
    pub book_id: u32,
}

/// `DoodadFuncOreMine` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncOreMine {
    pub id: u32,
}

/// `DoodadFuncParentInfo` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncParentInfo {
    pub id: u32,
}

/// `DoodadFuncParrot` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncParrot {
    pub id: u32,
}

/// `DoodadFuncPlantCollect` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncPlantCollect {
    pub id: u32,
}

/// `DoodadFuncPlayFlowGraph` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncPlayFlowGraph {
    pub id: u32,
    pub event_on_phase_change_id: u32,
    pub event_on_visible_id: u32,
}

/// `DoodadFuncPulse` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncPulse {
    pub id: u32,
    pub flag: bool,
}

/// `DoodadFuncPulseTrigger` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncPulseTrigger {
    pub id: u32,
    pub flag: bool,
    pub next_phase: i32,
}

/// `DoodadFuncPurchase` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncPurchase {
    pub id: u32,
    pub item_id: u32,
    pub count: i32,
    pub coin_item_id: u32,
    pub coin_count: i32,
    pub currency_id: u32,
}

/// `DoodadFuncPuzzleIn` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncPuzzleIn {
    pub id: u32,
    pub group_id: u32,
    pub ratio: f32,
}

/// `DoodadFuncPuzzleOut` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncPuzzleOut {
    pub id: u32,
    pub group_id: u32,
    pub ratio: f32,
    pub anim: String,
    pub projectile_id: u32,
    pub loot_pack_id: u32,
    pub delay: i32,
    pub next_phase: i32,
    pub projectile_delay: i32,
}

/// `DoodadFuncPuzzleRoll` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncPuzzleRoll {
    pub id: u32,
    pub item_id: u32,
    pub count: i32,
}

/// `DoodadFuncQuest` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncQuest {
    pub id: u32,
    pub quest_kind_id: u32,
    pub quest_id: u32,
}

/// `DoodadFuncRatioChange` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncRatioChange {
    pub id: u32,
    pub ratio: i32,
    pub next_phase: i32,
}

/// `DoodadFuncRatioRespawn` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncRatioRespawn {
    pub id: u32,
    pub ratio: i32,
    pub spawn_doodad_id: u32,
}

/// `DoodadFuncRecoverItem` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncRecoverItem {
    pub id: u32,
}

/// `DoodadFuncRemoveInstance` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncRemoveInstance {
    pub id: u32,
    pub zone_id: u32,
}

/// `DoodadFuncRemoveItem` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncRemoveItem {
    pub id: u32,
    pub item_id: u32,
    pub count: i32,
}

/// `DoodadFuncRenewItem` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncRenewItem {
    pub id: u32,
    pub skill_id: u32,
}

/// `DoodadFuncRequireItem` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncRequireItem {
    pub id: u32,
    /// `WorldInteractionType` (int enum).
    pub world_interaction_id: i32,
    pub item_id: u32,
}

/// `DoodadFuncRequireQuest` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncRequireQuest {
    pub id: u32,
    /// `WorldInteractionType` (int enum).
    pub world_interaction_id: i32,
    pub quest_id: u32,
}

/// `DoodadFuncRespawn` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncRespawn {
    pub id: u32,
    pub min_time: i32,
    pub max_time: i32,
}

/// `DoodadFuncRockMine` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncRockMine {
    pub id: u32,
}

/// `DoodadFuncSeedCollect` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncSeedCollect {
    pub id: u32,
}

/// `DoodadFuncShear` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncShear {
    pub id: u32,
    pub shear_type_id: u32,
    pub shear_term: i32,
}

/// `DoodadFuncSiegePeriod` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncSiegePeriod {
    pub id: u32,
    pub siege_period_id: u32,
    pub next_phase: i32,
    pub defense: bool,
}

/// `DoodadFuncSign` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncSign {
    pub id: u32,
    pub name: String,
    pub pick_num: i32,
}

/// `DoodadFuncSkillHit` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncSkillHit {
    pub id: u32,
    pub skill_id: u32,
}

/// `DoodadFuncSkinOff` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncSkinOff {
    pub id: u32,
}

/// `DoodadFuncSoilCollect` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncSoilCollect {
    pub id: u32,
}

/// `DoodadFuncSpawnGimmick` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncSpawnGimmick {
    pub id: u32,
    pub gimmick_id: u32,
    /// `FactionsEnum`.
    pub faction_id: u32,
    pub scale: f32,
    pub offset_x: f32,
    pub offset_y: f32,
    pub offset_z: f32,
    pub velocity_x: f32,
    pub velocity_y: f32,
    pub velocity_z: f32,
    pub angle_x: f32,
    pub angle_y: f32,
    pub angle_z: f32,
    pub next_phase: i32,
}

/// `DoodadFuncSpawnMgmt` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncSpawnMgmt {
    pub id: u32,
    pub group_id: u32,
    pub spawn: bool,
    pub zone_id: u32,
}

/// `DoodadFuncSpiceCollect` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncSpiceCollect {
    pub id: u32,
}

/// `DoodadFuncStampMaker` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncStampMaker {
    pub id: u32,
    pub consume_money: i32,
    pub item_id: u32,
    pub consume_item_id: u32,
    pub consume_count: i32,
}

/// `DoodadFuncStoreUi` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncStoreUi {
    pub id: u32,
    pub merchant_pack_id: u32,
}

/// `DoodadFuncTimer` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncTimer {
    pub id: u32,
    pub delay: i32,
    pub next_phase: i32,
    pub keep_requester: bool,
    pub show_tip: bool,
    pub show_end_time: bool,
    pub tip: String,
}

/// `DoodadFuncTod` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncTod {
    pub id: u32,
    pub tod: u32,
    pub next_phase: i32,
    /// Helper property: `Tod` as hours, computed by `DoodadManager.Load`.
    pub tod_as_hours: f32,
}

/// `DoodadFuncTreeByProductsCollect` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncTreeByProductsCollect {
    pub id: u32,
}

/// `DoodadFuncUccImprint` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncUccImprint {
    pub id: u32,
}

/// `DoodadFuncUse` (`DoodadFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncUse {
    pub id: u32,
    pub skill_id: u32,
}

/// `DoodadFuncWaterVolume` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncWaterVolume {
    pub id: u32,
    pub level_change: f32,
    pub duration: f32,
}

/// `DoodadFuncZoneReact` (`DoodadPhaseFuncTemplate`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct DoodadFuncZoneReact {
    pub id: u32,
    pub zone_group_id: u32,
    pub next_phase: i32,
}

/// Every func template class of `Models/Game/DoodadObj/Funcs` (one variant per C# class).
#[derive(Debug, Clone, PartialEq)]
pub enum FuncTemplate {
    Animate(DoodadFuncAnimate),
    AreaTrigger(DoodadFuncAreaTrigger),
    Attachment(DoodadFuncAttachment),
    AuctionUi(DoodadFuncAuctionUi),
    BankUi(DoodadFuncBankUi),
    Binding(DoodadFuncBinding),
    Bubble(DoodadFuncBubble),
    Buff(DoodadFuncBuff),
    Butcher(DoodadFuncButcher),
    BuyFish(DoodadFuncBuyFish),
    BuyFishItem(DoodadFuncBuyFishItem),
    BuyFishModel(DoodadFuncBuyFishModel),
    Catch(DoodadFuncCatch),
    CerealHarvest(DoodadFuncCerealHarvest),
    CleanupLogicLink(DoodadFuncCleanupLogicLink),
    ClimateReact(DoodadFuncClimateReact),
    Climb(DoodadFuncClimb),
    Clout(DoodadFuncClout),
    Coffer(DoodadFuncCoffer),
    CofferPerm(DoodadFuncCofferPerm),
    ConditionalUse(DoodadFuncConditionalUse),
    ConsumeChanger(DoodadFuncConsumeChanger),
    ConsumeChangerModel(DoodadFuncConsumeChangerModel),
    ConsumeChangerModelItem(DoodadFuncConsumeChangerModelItem),
    ConsumeItem(DoodadFuncConsumeItem),
    ConvertFish(DoodadFuncConvertFish),
    ConvertFishItem(DoodadFuncConvertFishItem),
    CraftAct(DoodadFuncCraftAct),
    CraftCancel(DoodadFuncCraftCancel),
    CraftDirect(DoodadFuncCraftDirect),
    CraftGetItem(DoodadFuncCraftGetItem),
    CraftInfo(DoodadFuncCraftInfo),
    CraftPack(DoodadFuncCraftPack),
    CraftStart(DoodadFuncCraftStart),
    CraftStartCraft(DoodadFuncCraftStartCraft),
    CropHarvest(DoodadFuncCropHarvest),
    CrystalCollect(DoodadFuncCrystalCollect),
    Cutdown(DoodadFuncCutdown),
    Cutdowning(DoodadFuncCutdowning),
    DairyCollect(DoodadFuncDairyCollect),
    DeclareSiege(DoodadFuncDeclareSiege),
    Dig(DoodadFuncDig),
    DigTerrain(DoodadFuncDigTerrain),
    DyeingredientCollect(DoodadFuncDyeingredientCollect),
    EnterInstance(DoodadFuncEnterInstance),
    EnterSysInstance(DoodadFuncEnterSysInstance),
    EvidenceItemLoot(DoodadFuncEvidenceItemLoot),
    Exchange(DoodadFuncExchange),
    ExitIndun(DoodadFuncExitIndun),
    FakeUse(DoodadFuncFakeUse),
    Feed(DoodadFuncFeed),
    FiberCollect(DoodadFuncFiberCollect),
    Final(DoodadFuncFinal),
    FishSchool(DoodadFuncFishSchool),
    FruitPick(DoodadFuncFruitPick),
    GassExtract(DoodadFuncGassExtract),
    Growth(DoodadFuncGrowth),
    Harvest(DoodadFuncHarvest),
    HouseFarm(DoodadFuncHouseFarm),
    HousingArea(DoodadFuncHousingArea),
    Hunger(DoodadFuncHunger),
    InsertCounter(DoodadFuncInsertCounter),
    Logic(DoodadFuncLogic),
    LogicFamilyProvider(DoodadFuncLogicFamilyProvider),
    LogicFamilySubscriber(DoodadFuncLogicFamilySubscriber),
    LootItem(DoodadFuncLootItem),
    LootPack(DoodadFuncLootPack),
    MachinePartsCollect(DoodadFuncMachinePartsCollect),
    MedicalingredientMine(DoodadFuncMedicalingredientMine),
    Mow(DoodadFuncMow),
    NaviMarkPosToMap(DoodadFuncNaviMarkPosToMap),
    NaviNaming(DoodadFuncNaviNaming),
    NaviOpenBounty(DoodadFuncNaviOpenBounty),
    NaviOpenMailbox(DoodadFuncNaviOpenMailbox),
    NaviOpenPortal(DoodadFuncNaviOpenPortal),
    NaviRemove(DoodadFuncNaviRemove),
    NaviRemoveTimer(DoodadFuncNaviRemoveTimer),
    NaviTeleport(DoodadFuncNaviTeleport),
    OpenFarmInfo(DoodadFuncOpenFarmInfo),
    OpenPaper(DoodadFuncOpenPaper),
    OreMine(DoodadFuncOreMine),
    ParentInfo(DoodadFuncParentInfo),
    Parrot(DoodadFuncParrot),
    PlantCollect(DoodadFuncPlantCollect),
    PlayFlowGraph(DoodadFuncPlayFlowGraph),
    Pulse(DoodadFuncPulse),
    PulseTrigger(DoodadFuncPulseTrigger),
    Purchase(DoodadFuncPurchase),
    PuzzleIn(DoodadFuncPuzzleIn),
    PuzzleOut(DoodadFuncPuzzleOut),
    PuzzleRoll(DoodadFuncPuzzleRoll),
    Quest(DoodadFuncQuest),
    RatioChange(DoodadFuncRatioChange),
    RatioRespawn(DoodadFuncRatioRespawn),
    RecoverItem(DoodadFuncRecoverItem),
    RemoveInstance(DoodadFuncRemoveInstance),
    RemoveItem(DoodadFuncRemoveItem),
    RenewItem(DoodadFuncRenewItem),
    RequireItem(DoodadFuncRequireItem),
    RequireQuest(DoodadFuncRequireQuest),
    Respawn(DoodadFuncRespawn),
    RockMine(DoodadFuncRockMine),
    SeedCollect(DoodadFuncSeedCollect),
    Shear(DoodadFuncShear),
    SiegePeriod(DoodadFuncSiegePeriod),
    Sign(DoodadFuncSign),
    SkillHit(DoodadFuncSkillHit),
    SkinOff(DoodadFuncSkinOff),
    SoilCollect(DoodadFuncSoilCollect),
    SpawnGimmick(DoodadFuncSpawnGimmick),
    SpawnMgmt(DoodadFuncSpawnMgmt),
    SpiceCollect(DoodadFuncSpiceCollect),
    StampMaker(DoodadFuncStampMaker),
    StoreUi(DoodadFuncStoreUi),
    Timer(DoodadFuncTimer),
    Tod(DoodadFuncTod),
    TreeByProductsCollect(DoodadFuncTreeByProductsCollect),
    UccImprint(DoodadFuncUccImprint),
    Use(DoodadFuncUse),
    WaterVolume(DoodadFuncWaterVolume),
    ZoneReact(DoodadFuncZoneReact),
}

/// `(class name, derives from DoodadPhaseFuncTemplate)` of every class in
/// `Models/Game/DoodadObj/Funcs` (what `DoodadManager.Load` finds through reflection to create the
/// template dictionaries).
pub const FUNC_TEMPLATE_CLASSES: [(&str, bool); 121] = [
    ("DoodadFuncAnimate", true),
    ("DoodadFuncAreaTrigger", false),
    ("DoodadFuncAttachment", false),
    ("DoodadFuncAuctionUi", false),
    ("DoodadFuncBankUi", false),
    ("DoodadFuncBinding", false),
    ("DoodadFuncBubble", false),
    ("DoodadFuncBuff", false),
    ("DoodadFuncButcher", false),
    ("DoodadFuncBuyFish", false),
    ("DoodadFuncBuyFishItem", true),
    ("DoodadFuncBuyFishModel", true),
    ("DoodadFuncCatch", false),
    ("DoodadFuncCerealHarvest", false),
    ("DoodadFuncCleanupLogicLink", false),
    ("DoodadFuncClimateReact", true),
    ("DoodadFuncClimb", false),
    ("DoodadFuncClout", true),
    ("DoodadFuncCoffer", true),
    ("DoodadFuncCofferPerm", false),
    ("DoodadFuncConditionalUse", false),
    ("DoodadFuncConsumeChanger", false),
    ("DoodadFuncConsumeChangerModel", true),
    ("DoodadFuncConsumeChangerModelItem", true),
    ("DoodadFuncConsumeItem", true),
    ("DoodadFuncConvertFish", false),
    ("DoodadFuncConvertFishItem", true),
    ("DoodadFuncCraftAct", false),
    ("DoodadFuncCraftCancel", false),
    ("DoodadFuncCraftDirect", true),
    ("DoodadFuncCraftGetItem", false),
    ("DoodadFuncCraftInfo", false),
    ("DoodadFuncCraftPack", false),
    ("DoodadFuncCraftStart", false),
    ("DoodadFuncCraftStartCraft", true),
    ("DoodadFuncCropHarvest", false),
    ("DoodadFuncCrystalCollect", false),
    ("DoodadFuncCutdown", false),
    ("DoodadFuncCutdowning", false),
    ("DoodadFuncDairyCollect", false),
    ("DoodadFuncDeclareSiege", true),
    ("DoodadFuncDig", false),
    ("DoodadFuncDigTerrain", false),
    ("DoodadFuncDyeingredientCollect", false),
    ("DoodadFuncEnterInstance", false),
    ("DoodadFuncEnterSysInstance", false),
    ("DoodadFuncEvidenceItemLoot", false),
    ("DoodadFuncExchange", true),
    ("DoodadFuncExitIndun", false),
    ("DoodadFuncFakeUse", false),
    ("DoodadFuncFeed", false),
    ("DoodadFuncFiberCollect", false),
    ("DoodadFuncFinal", true),
    ("DoodadFuncFishSchool", true),
    ("DoodadFuncFruitPick", false),
    ("DoodadFuncGassExtract", false),
    ("DoodadFuncGrowth", true),
    ("DoodadFuncHarvest", false),
    ("DoodadFuncHouseFarm", true),
    ("DoodadFuncHousingArea", false),
    ("DoodadFuncHunger", true),
    ("DoodadFuncInsertCounter", false),
    ("DoodadFuncLogic", true),
    ("DoodadFuncLogicFamilyProvider", true),
    ("DoodadFuncLogicFamilySubscriber", true),
    ("DoodadFuncLootItem", false),
    ("DoodadFuncLootPack", false),
    ("DoodadFuncMachinePartsCollect", false),
    ("DoodadFuncMedicalingredientMine", false),
    ("DoodadFuncMow", false),
    ("DoodadFuncNaviMarkPosToMap", false),
    ("DoodadFuncNaviNaming", false),
    ("DoodadFuncNaviOpenBounty", false),
    ("DoodadFuncNaviOpenMailbox", false),
    ("DoodadFuncNaviOpenPortal", false),
    ("DoodadFuncNaviRemove", false),
    ("DoodadFuncNaviRemoveTimer", true),
    ("DoodadFuncNaviTeleport", false),
    ("DoodadFuncOpenFarmInfo", false),
    ("DoodadFuncOpenPaper", false),
    ("DoodadFuncOreMine", false),
    ("DoodadFuncParentInfo", false),
    ("DoodadFuncParrot", true),
    ("DoodadFuncPlantCollect", false),
    ("DoodadFuncPlayFlowGraph", true),
    ("DoodadFuncPulse", true),
    ("DoodadFuncPulseTrigger", true),
    ("DoodadFuncPurchase", false),
    ("DoodadFuncPuzzleIn", true),
    ("DoodadFuncPuzzleOut", true),
    ("DoodadFuncPuzzleRoll", false),
    ("DoodadFuncQuest", false),
    ("DoodadFuncRatioChange", true),
    ("DoodadFuncRatioRespawn", true),
    ("DoodadFuncRecoverItem", false),
    ("DoodadFuncRemoveInstance", false),
    ("DoodadFuncRemoveItem", false),
    ("DoodadFuncRenewItem", false),
    ("DoodadFuncRequireItem", true),
    ("DoodadFuncRequireQuest", true),
    ("DoodadFuncRespawn", true),
    ("DoodadFuncRockMine", false),
    ("DoodadFuncSeedCollect", false),
    ("DoodadFuncShear", false),
    ("DoodadFuncSiegePeriod", true),
    ("DoodadFuncSign", true),
    ("DoodadFuncSkillHit", false),
    ("DoodadFuncSkinOff", false),
    ("DoodadFuncSoilCollect", false),
    ("DoodadFuncSpawnGimmick", true),
    ("DoodadFuncSpawnMgmt", true),
    ("DoodadFuncSpiceCollect", false),
    ("DoodadFuncStampMaker", false),
    ("DoodadFuncStoreUi", false),
    ("DoodadFuncTimer", true),
    ("DoodadFuncTod", true),
    ("DoodadFuncTreeByProductsCollect", false),
    ("DoodadFuncUccImprint", false),
    ("DoodadFuncUse", false),
    ("DoodadFuncWaterVolume", true),
    ("DoodadFuncZoneReact", true),
];

impl FuncTemplate {
    /// The C# class name (`GetType().Name`, the `actual_func_type` of `doodad_funcs`).
    pub fn func_type(&self) -> &'static str {
        match self {
            Self::Animate(_) => "DoodadFuncAnimate",
            Self::AreaTrigger(_) => "DoodadFuncAreaTrigger",
            Self::Attachment(_) => "DoodadFuncAttachment",
            Self::AuctionUi(_) => "DoodadFuncAuctionUi",
            Self::BankUi(_) => "DoodadFuncBankUi",
            Self::Binding(_) => "DoodadFuncBinding",
            Self::Bubble(_) => "DoodadFuncBubble",
            Self::Buff(_) => "DoodadFuncBuff",
            Self::Butcher(_) => "DoodadFuncButcher",
            Self::BuyFish(_) => "DoodadFuncBuyFish",
            Self::BuyFishItem(_) => "DoodadFuncBuyFishItem",
            Self::BuyFishModel(_) => "DoodadFuncBuyFishModel",
            Self::Catch(_) => "DoodadFuncCatch",
            Self::CerealHarvest(_) => "DoodadFuncCerealHarvest",
            Self::CleanupLogicLink(_) => "DoodadFuncCleanupLogicLink",
            Self::ClimateReact(_) => "DoodadFuncClimateReact",
            Self::Climb(_) => "DoodadFuncClimb",
            Self::Clout(_) => "DoodadFuncClout",
            Self::Coffer(_) => "DoodadFuncCoffer",
            Self::CofferPerm(_) => "DoodadFuncCofferPerm",
            Self::ConditionalUse(_) => "DoodadFuncConditionalUse",
            Self::ConsumeChanger(_) => "DoodadFuncConsumeChanger",
            Self::ConsumeChangerModel(_) => "DoodadFuncConsumeChangerModel",
            Self::ConsumeChangerModelItem(_) => "DoodadFuncConsumeChangerModelItem",
            Self::ConsumeItem(_) => "DoodadFuncConsumeItem",
            Self::ConvertFish(_) => "DoodadFuncConvertFish",
            Self::ConvertFishItem(_) => "DoodadFuncConvertFishItem",
            Self::CraftAct(_) => "DoodadFuncCraftAct",
            Self::CraftCancel(_) => "DoodadFuncCraftCancel",
            Self::CraftDirect(_) => "DoodadFuncCraftDirect",
            Self::CraftGetItem(_) => "DoodadFuncCraftGetItem",
            Self::CraftInfo(_) => "DoodadFuncCraftInfo",
            Self::CraftPack(_) => "DoodadFuncCraftPack",
            Self::CraftStart(_) => "DoodadFuncCraftStart",
            Self::CraftStartCraft(_) => "DoodadFuncCraftStartCraft",
            Self::CropHarvest(_) => "DoodadFuncCropHarvest",
            Self::CrystalCollect(_) => "DoodadFuncCrystalCollect",
            Self::Cutdown(_) => "DoodadFuncCutdown",
            Self::Cutdowning(_) => "DoodadFuncCutdowning",
            Self::DairyCollect(_) => "DoodadFuncDairyCollect",
            Self::DeclareSiege(_) => "DoodadFuncDeclareSiege",
            Self::Dig(_) => "DoodadFuncDig",
            Self::DigTerrain(_) => "DoodadFuncDigTerrain",
            Self::DyeingredientCollect(_) => "DoodadFuncDyeingredientCollect",
            Self::EnterInstance(_) => "DoodadFuncEnterInstance",
            Self::EnterSysInstance(_) => "DoodadFuncEnterSysInstance",
            Self::EvidenceItemLoot(_) => "DoodadFuncEvidenceItemLoot",
            Self::Exchange(_) => "DoodadFuncExchange",
            Self::ExitIndun(_) => "DoodadFuncExitIndun",
            Self::FakeUse(_) => "DoodadFuncFakeUse",
            Self::Feed(_) => "DoodadFuncFeed",
            Self::FiberCollect(_) => "DoodadFuncFiberCollect",
            Self::Final(_) => "DoodadFuncFinal",
            Self::FishSchool(_) => "DoodadFuncFishSchool",
            Self::FruitPick(_) => "DoodadFuncFruitPick",
            Self::GassExtract(_) => "DoodadFuncGassExtract",
            Self::Growth(_) => "DoodadFuncGrowth",
            Self::Harvest(_) => "DoodadFuncHarvest",
            Self::HouseFarm(_) => "DoodadFuncHouseFarm",
            Self::HousingArea(_) => "DoodadFuncHousingArea",
            Self::Hunger(_) => "DoodadFuncHunger",
            Self::InsertCounter(_) => "DoodadFuncInsertCounter",
            Self::Logic(_) => "DoodadFuncLogic",
            Self::LogicFamilyProvider(_) => "DoodadFuncLogicFamilyProvider",
            Self::LogicFamilySubscriber(_) => "DoodadFuncLogicFamilySubscriber",
            Self::LootItem(_) => "DoodadFuncLootItem",
            Self::LootPack(_) => "DoodadFuncLootPack",
            Self::MachinePartsCollect(_) => "DoodadFuncMachinePartsCollect",
            Self::MedicalingredientMine(_) => "DoodadFuncMedicalingredientMine",
            Self::Mow(_) => "DoodadFuncMow",
            Self::NaviMarkPosToMap(_) => "DoodadFuncNaviMarkPosToMap",
            Self::NaviNaming(_) => "DoodadFuncNaviNaming",
            Self::NaviOpenBounty(_) => "DoodadFuncNaviOpenBounty",
            Self::NaviOpenMailbox(_) => "DoodadFuncNaviOpenMailbox",
            Self::NaviOpenPortal(_) => "DoodadFuncNaviOpenPortal",
            Self::NaviRemove(_) => "DoodadFuncNaviRemove",
            Self::NaviRemoveTimer(_) => "DoodadFuncNaviRemoveTimer",
            Self::NaviTeleport(_) => "DoodadFuncNaviTeleport",
            Self::OpenFarmInfo(_) => "DoodadFuncOpenFarmInfo",
            Self::OpenPaper(_) => "DoodadFuncOpenPaper",
            Self::OreMine(_) => "DoodadFuncOreMine",
            Self::ParentInfo(_) => "DoodadFuncParentInfo",
            Self::Parrot(_) => "DoodadFuncParrot",
            Self::PlantCollect(_) => "DoodadFuncPlantCollect",
            Self::PlayFlowGraph(_) => "DoodadFuncPlayFlowGraph",
            Self::Pulse(_) => "DoodadFuncPulse",
            Self::PulseTrigger(_) => "DoodadFuncPulseTrigger",
            Self::Purchase(_) => "DoodadFuncPurchase",
            Self::PuzzleIn(_) => "DoodadFuncPuzzleIn",
            Self::PuzzleOut(_) => "DoodadFuncPuzzleOut",
            Self::PuzzleRoll(_) => "DoodadFuncPuzzleRoll",
            Self::Quest(_) => "DoodadFuncQuest",
            Self::RatioChange(_) => "DoodadFuncRatioChange",
            Self::RatioRespawn(_) => "DoodadFuncRatioRespawn",
            Self::RecoverItem(_) => "DoodadFuncRecoverItem",
            Self::RemoveInstance(_) => "DoodadFuncRemoveInstance",
            Self::RemoveItem(_) => "DoodadFuncRemoveItem",
            Self::RenewItem(_) => "DoodadFuncRenewItem",
            Self::RequireItem(_) => "DoodadFuncRequireItem",
            Self::RequireQuest(_) => "DoodadFuncRequireQuest",
            Self::Respawn(_) => "DoodadFuncRespawn",
            Self::RockMine(_) => "DoodadFuncRockMine",
            Self::SeedCollect(_) => "DoodadFuncSeedCollect",
            Self::Shear(_) => "DoodadFuncShear",
            Self::SiegePeriod(_) => "DoodadFuncSiegePeriod",
            Self::Sign(_) => "DoodadFuncSign",
            Self::SkillHit(_) => "DoodadFuncSkillHit",
            Self::SkinOff(_) => "DoodadFuncSkinOff",
            Self::SoilCollect(_) => "DoodadFuncSoilCollect",
            Self::SpawnGimmick(_) => "DoodadFuncSpawnGimmick",
            Self::SpawnMgmt(_) => "DoodadFuncSpawnMgmt",
            Self::SpiceCollect(_) => "DoodadFuncSpiceCollect",
            Self::StampMaker(_) => "DoodadFuncStampMaker",
            Self::StoreUi(_) => "DoodadFuncStoreUi",
            Self::Timer(_) => "DoodadFuncTimer",
            Self::Tod(_) => "DoodadFuncTod",
            Self::TreeByProductsCollect(_) => "DoodadFuncTreeByProductsCollect",
            Self::UccImprint(_) => "DoodadFuncUccImprint",
            Self::Use(_) => "DoodadFuncUse",
            Self::WaterVolume(_) => "DoodadFuncWaterVolume",
            Self::ZoneReact(_) => "DoodadFuncZoneReact",
        }
    }

    /// `DoodadFuncTemplate.Id` / `DoodadPhaseFuncTemplate.Id`.
    pub fn id(&self) -> u32 {
        match self {
            Self::Animate(f) => f.id,
            Self::AreaTrigger(f) => f.id,
            Self::Attachment(f) => f.id,
            Self::AuctionUi(f) => f.id,
            Self::BankUi(f) => f.id,
            Self::Binding(f) => f.id,
            Self::Bubble(f) => f.id,
            Self::Buff(f) => f.id,
            Self::Butcher(f) => f.id,
            Self::BuyFish(f) => f.id,
            Self::BuyFishItem(f) => f.id,
            Self::BuyFishModel(f) => f.id,
            Self::Catch(f) => f.id,
            Self::CerealHarvest(f) => f.id,
            Self::CleanupLogicLink(f) => f.id,
            Self::ClimateReact(f) => f.id,
            Self::Climb(f) => f.id,
            Self::Clout(f) => f.id,
            Self::Coffer(f) => f.id,
            Self::CofferPerm(f) => f.id,
            Self::ConditionalUse(f) => f.id,
            Self::ConsumeChanger(f) => f.id,
            Self::ConsumeChangerModel(f) => f.id,
            Self::ConsumeChangerModelItem(f) => f.id,
            Self::ConsumeItem(f) => f.id,
            Self::ConvertFish(f) => f.id,
            Self::ConvertFishItem(f) => f.id,
            Self::CraftAct(f) => f.id,
            Self::CraftCancel(f) => f.id,
            Self::CraftDirect(f) => f.id,
            Self::CraftGetItem(f) => f.id,
            Self::CraftInfo(f) => f.id,
            Self::CraftPack(f) => f.id,
            Self::CraftStart(f) => f.id,
            Self::CraftStartCraft(f) => f.id,
            Self::CropHarvest(f) => f.id,
            Self::CrystalCollect(f) => f.id,
            Self::Cutdown(f) => f.id,
            Self::Cutdowning(f) => f.id,
            Self::DairyCollect(f) => f.id,
            Self::DeclareSiege(f) => f.id,
            Self::Dig(f) => f.id,
            Self::DigTerrain(f) => f.id,
            Self::DyeingredientCollect(f) => f.id,
            Self::EnterInstance(f) => f.id,
            Self::EnterSysInstance(f) => f.id,
            Self::EvidenceItemLoot(f) => f.id,
            Self::Exchange(f) => f.id,
            Self::ExitIndun(f) => f.id,
            Self::FakeUse(f) => f.id,
            Self::Feed(f) => f.id,
            Self::FiberCollect(f) => f.id,
            Self::Final(f) => f.id,
            Self::FishSchool(f) => f.id,
            Self::FruitPick(f) => f.id,
            Self::GassExtract(f) => f.id,
            Self::Growth(f) => f.id,
            Self::Harvest(f) => f.id,
            Self::HouseFarm(f) => f.id,
            Self::HousingArea(f) => f.id,
            Self::Hunger(f) => f.id,
            Self::InsertCounter(f) => f.id,
            Self::Logic(f) => f.id,
            Self::LogicFamilyProvider(f) => f.id,
            Self::LogicFamilySubscriber(f) => f.id,
            Self::LootItem(f) => f.id,
            Self::LootPack(f) => f.id,
            Self::MachinePartsCollect(f) => f.id,
            Self::MedicalingredientMine(f) => f.id,
            Self::Mow(f) => f.id,
            Self::NaviMarkPosToMap(f) => f.id,
            Self::NaviNaming(f) => f.id,
            Self::NaviOpenBounty(f) => f.id,
            Self::NaviOpenMailbox(f) => f.id,
            Self::NaviOpenPortal(f) => f.id,
            Self::NaviRemove(f) => f.id,
            Self::NaviRemoveTimer(f) => f.id,
            Self::NaviTeleport(f) => f.id,
            Self::OpenFarmInfo(f) => f.id,
            Self::OpenPaper(f) => f.id,
            Self::OreMine(f) => f.id,
            Self::ParentInfo(f) => f.id,
            Self::Parrot(f) => f.id,
            Self::PlantCollect(f) => f.id,
            Self::PlayFlowGraph(f) => f.id,
            Self::Pulse(f) => f.id,
            Self::PulseTrigger(f) => f.id,
            Self::Purchase(f) => f.id,
            Self::PuzzleIn(f) => f.id,
            Self::PuzzleOut(f) => f.id,
            Self::PuzzleRoll(f) => f.id,
            Self::Quest(f) => f.id,
            Self::RatioChange(f) => f.id,
            Self::RatioRespawn(f) => f.id,
            Self::RecoverItem(f) => f.id,
            Self::RemoveInstance(f) => f.id,
            Self::RemoveItem(f) => f.id,
            Self::RenewItem(f) => f.id,
            Self::RequireItem(f) => f.id,
            Self::RequireQuest(f) => f.id,
            Self::Respawn(f) => f.id,
            Self::RockMine(f) => f.id,
            Self::SeedCollect(f) => f.id,
            Self::Shear(f) => f.id,
            Self::SiegePeriod(f) => f.id,
            Self::Sign(f) => f.id,
            Self::SkillHit(f) => f.id,
            Self::SkinOff(f) => f.id,
            Self::SoilCollect(f) => f.id,
            Self::SpawnGimmick(f) => f.id,
            Self::SpawnMgmt(f) => f.id,
            Self::SpiceCollect(f) => f.id,
            Self::StampMaker(f) => f.id,
            Self::StoreUi(f) => f.id,
            Self::Timer(f) => f.id,
            Self::Tod(f) => f.id,
            Self::TreeByProductsCollect(f) => f.id,
            Self::UccImprint(f) => f.id,
            Self::Use(f) => f.id,
            Self::WaterVolume(f) => f.id,
            Self::ZoneReact(f) => f.id,
        }
    }

    /// The class derives from `DoodadPhaseFuncTemplate` (else from `DoodadFuncTemplate`).
    pub fn is_phase_func(&self) -> bool {
        matches!(
            self,
            Self::Animate(_)
                | Self::BuyFishItem(_)
                | Self::BuyFishModel(_)
                | Self::ClimateReact(_)
                | Self::Clout(_)
                | Self::Coffer(_)
                | Self::ConsumeChangerModel(_)
                | Self::ConsumeChangerModelItem(_)
                | Self::ConsumeItem(_)
                | Self::ConvertFishItem(_)
                | Self::CraftDirect(_)
                | Self::CraftStartCraft(_)
                | Self::DeclareSiege(_)
                | Self::Exchange(_)
                | Self::Final(_)
                | Self::FishSchool(_)
                | Self::Growth(_)
                | Self::HouseFarm(_)
                | Self::Hunger(_)
                | Self::Logic(_)
                | Self::LogicFamilyProvider(_)
                | Self::LogicFamilySubscriber(_)
                | Self::NaviRemoveTimer(_)
                | Self::Parrot(_)
                | Self::PlayFlowGraph(_)
                | Self::Pulse(_)
                | Self::PulseTrigger(_)
                | Self::PuzzleIn(_)
                | Self::PuzzleOut(_)
                | Self::RatioChange(_)
                | Self::RatioRespawn(_)
                | Self::RequireItem(_)
                | Self::RequireQuest(_)
                | Self::Respawn(_)
                | Self::SiegePeriod(_)
                | Self::Sign(_)
                | Self::SpawnGimmick(_)
                | Self::SpawnMgmt(_)
                | Self::Timer(_)
                | Self::Tod(_)
                | Self::WaterVolume(_)
                | Self::ZoneReact(_)
        )
    }
}
