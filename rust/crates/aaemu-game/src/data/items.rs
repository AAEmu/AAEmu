//! `ItemManager.Load` (item templates and every item table it reads): `item_configs`,
//! `item_look_convert_*`, `item_grades`, `holdables`, `wearables`, `wearable_kinds`,
//! `wearable_slots`, `dyeable_items`, `equip_item_attr_modifiers`, `item_procs`,
//! `equip_item_set_bonuses`, the class tables (`item_armors`, `item_weapons`,
//! `item_accessories`, `item_summon_mates`, `item_summon_slaves`, `item_body_parts`,
//! `item_enchanting_gems`, `item_backpacks`), the hard-coded UCC / music / treasure templates, the
//! base `items` table, `equip_slot_enchanting_costs`, `item_grade_enchanting_supports`,
//! `item_socket_chances`, `item_cap_scales`, `item_grade_distributions`,
//! `loot_pack_dropping_npcs`, `doodad_func_convert_fish_items`, `item_spawn_doodads`,
//! `unit_modifiers` (items), `armor_grade_buffs`, `item_sets` / `item_set_items`, and the
//! "invalid_item_" names. The localized search strings live in `systems::economy::items_data`
//! (they need the localization tables).

use std::collections::HashMap;

use rusqlite::Connection;

use super::formulas::Formula;
use super::sqlite::for_each_row;
use crate::models::item::ItemClass;
use crate::models::unit::{BonusTemplate, UnitAttribute, UnitModifierType};

/// `ItemBindType`.
pub mod bind_type {
    pub const NORMAL: u32 = 1;
    pub const BIND_ON_PICKUP: u32 = 2;
    pub const BIND_ON_EQUIP: u32 = 3;
    pub const BIND_ON_UNPACK: u32 = 4;
    pub const BIND_ON_PICKUP_PACK: u32 = 5;
    pub const BIND_ON_AUCTION_WIN: u32 = 6;
}

/// `Item.CrestInk` etc. (the hard-coded templates of `ItemManager.Load`).
pub const ITEM_CREST_INK: u32 = 17663;
pub const ITEM_CREST_STAMP: u32 = 17662;
pub const ITEM_SHEET_MUSIC: u32 = 28051;
pub const ITEM_TREASURE_MAP_WITH_COORDINATES: u32 = 24581;
/// `Item.Coins`.
pub const ITEM_COINS: u32 = 500;
/// `Item.DawnStone`.
pub const ITEM_DAWN_STONE: u32 = 327;
/// `Item.TaxCertificate`.
pub const ITEM_TAX_CERTIFICATE: u32 = 31891;
/// `Item.BoundTaxCertificate`.
pub const ITEM_BOUND_TAX_CERTIFICATE: u32 = 31892;
/// `Item.AppraisalCertificate`.
pub const ITEM_APPRAISAL_CERTIFICATE: u32 = 28085;
/// `Item.SalonCertificate`.
pub const ITEM_SALON_CERTIFICATE: u32 = 30811;

/// `BackpackType` values `ItemContainer.CanAccept` allows in player containers.
pub mod backpack_type {
    pub const GLIDER: u32 = 2;
    pub const TOY_FLAG: u32 = 7;
}

/// `ItemGrade`.
pub mod item_grade {
    pub const CRUDE: u8 = 0;
    pub const BASIC: u8 = 1;
    pub const GRAND: u8 = 2;
    pub const RARE: u8 = 3;
    pub const ARCANE: u8 = 4;
    pub const HEROIC: u8 = 5;
    pub const UNIQUE: u8 = 6;
    pub const CELESTIAL: u8 = 7;
    pub const DIVINE: u8 = 8;
    pub const EPIC: u8 = 9;
    pub const LEGENDARY: u8 = 10;
    pub const MYTHIC: u8 = 11;
}

/// `ArmorType`.
pub mod armor_type {
    pub const CLOTH: u32 = 1;
    pub const LEATHER: u32 = 2;
    pub const METAL: u32 = 3;
    pub const PET_ARMOR: u32 = 4;
    pub const ETC: u32 = 5;
}

/// `EquipmentItemSlotType`.
pub mod equipment_item_slot_type {
    pub const HEAD: u32 = 1;
    pub const NECK: u32 = 2;
    pub const CHEST: u32 = 3;
    pub const WAIST: u32 = 4;
    pub const LEGS: u32 = 5;
    pub const HANDS: u32 = 6;
    pub const FEET: u32 = 7;
    pub const ARMS: u32 = 8;
    pub const BACK: u32 = 9;
    pub const EAR: u32 = 10;
    pub const FINGER: u32 = 11;
    pub const UNDERSHIRT: u32 = 12;
    pub const UNDERPANTS: u32 = 13;
    pub const MAINHAND: u32 = 14;
    pub const OFFHAND: u32 = 15;
    pub const TWO_HANDED: u32 = 16;
    pub const ONE_HANDED: u32 = 17;
    pub const RANGED: u32 = 18;
    pub const AMMUNITION: u32 = 19;
    pub const SHIELD: u32 = 20;
    pub const INSTRUMENT: u32 = 21;
    pub const BAG: u32 = 22;
    pub const FACE: u32 = 23;
    pub const HAIR: u32 = 24;
    pub const GLASSES: u32 = 25;
    pub const RESERVED: u32 = 26;
    pub const TAIL: u32 = 27;
    pub const BODY: u32 = 28;
    pub const BEARD: u32 = 29;
    pub const BACKPACK: u32 = 30;
    pub const COSPLAY: u32 = 31;
}

/// `ItemImplEnum`.
pub mod item_impl {
    pub const MISC: i32 = 0;
    pub const WEAPON: i32 = 1;
    pub const ARMOR: i32 = 2;
    pub const BODY: i32 = 3;
    pub const BAG: i32 = 4;
    pub const HOUSING: i32 = 5;
    pub const HOUSING_DECORATION: i32 = 6;
    pub const TOOL: i32 = 7;
    pub const SUMMON_SLAVE: i32 = 8;
    pub const SPAWN_DOODAD: i32 = 9;
    pub const ACCEPT_QUEST: i32 = 10;
    pub const SUMMON_MATE: i32 = 11;
    pub const RECIPE: i32 = 12;
    pub const CRAFTING: i32 = 13;
    pub const PORTAL: i32 = 14;
    pub const ENCHANTING_GEM: i32 = 15;
    pub const REPORT_CRIME: i32 = 16;
    pub const LOGIC_DOODAD: i32 = 17;
    pub const HAS_UCC: i32 = 18;
    pub const OPEN_EMBLEM_UI: i32 = 19;
    pub const SHIPYARD: i32 = 20;
    pub const SOCKET: i32 = 21;
    pub const BACKPACK: i32 = 22;
    pub const OPEN_PAPER: i32 = 23;
    pub const ACCESSORY: i32 = 24;
    pub const TREASURE: i32 = 25;
    pub const MUSIC_SHEET: i32 = 26;
    pub const DYEING: i32 = 27;
    pub const SLAVE_EQUIPMENT: i32 = 28;
    pub const GRADE_ENCHANTING_SUPPORT: i32 = 29;
    pub const MATE_ARMOR: i32 = 30;
    pub const LOCATION: i32 = 31;
}

/// `ProcChanceKind`.
pub mod proc_chance_kind {
    pub const HIT_ANY: u32 = 1;
    pub const HIT_MELEE: u32 = 2;
    pub const HIT_MELEE_CRIT: u32 = 3;
    pub const HIT_SPELL: u32 = 4;
    pub const HIT_SPELL_CRIT: u32 = 5;
    pub const HIT_RANGED: u32 = 6;
    pub const HIT_RANGED_CRIT: u32 = 7;
    pub const HIT_SIEGE: u32 = 8;
    pub const TAKE_DAMAGE_ANY: u32 = 9;
    pub const TAKE_DAMAGE_MELEE: u32 = 10;
    pub const TAKE_DAMAGE_MELEE_CRIT: u32 = 11;
    pub const TAKE_DAMAGE_SPELL: u32 = 12;
    pub const TAKE_DAMAGE_SPELL_CRIT: u32 = 13;
    pub const TAKE_DAMAGE_RANGED: u32 = 14;
    pub const TAKE_DAMAGE_RANGED_CRIT: u32 = 15;
    pub const TAKE_DAMAGE_SIEGE: u32 = 16;
    pub const HIT_HEAL: u32 = 17;
    pub const HIT_HEAL_CRIT: u32 = 18;
    pub const FIRE_SKILL: u32 = 19;
    pub const HIT_SKILL: u32 = 20;
}

/// `ItemConfig` (`item_configs`, first row).
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct ItemConfig {
    pub durability_decrement_chance: f32,
    pub durability_repair_cost_factor: f32,
    pub durability_const: f32,
    pub holdable_durability_const: f32,
    pub wearable_durability_const: f32,
    pub death_durability_loss_ratio: u8,
    pub item_stat_const: i32,
    pub holdable_stat_const: i32,
    pub wearable_stat_const: i32,
    pub stat_value_const: i32,
}

/// `GradeTemplate` (the columns the item model needs everywhere; every column is in
/// [`FullGradeTemplate`]).
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct GradeTemplate {
    pub grade: i32,
    pub grade_order: i32,
    pub durability: f32,
}

/// `GradeTemplate` with every `item_grades` column.
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct FullGradeTemplate {
    pub grade: i32,
    pub grade_order: i32,
    pub holdable_dps: f32,
    pub holdable_armor: f32,
    pub holdable_magic_dps: f32,
    pub holdable_heal_dps: f32,
    pub wearable_armor: f32,
    pub wearable_magic_resistance: f32,
    pub durability: f32,
    pub upgrade_ratio: i32,
    pub stat_multiplier: i32,
    pub refund_multiplier: i32,
    pub enchant_success_ratio: i32,
    pub enchant_great_success_ratio: i32,
    pub enchant_break_ratio: i32,
    pub enchant_downgrade_ratio: i32,
    pub enchant_downgrade_min: i32,
    pub enchant_downgrade_max: i32,
    pub enchant_cost: i32,
    pub currency_id: i32,
}

impl FullGradeTemplate {
    /// The [`GradeTemplate`] subset.
    pub fn short(&self) -> GradeTemplate {
        GradeTemplate {
            grade: self.grade,
            grade_order: self.grade_order,
            durability: self.durability,
        }
    }
}

/// `Holdable` (`holdables`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct Holdable {
    pub id: u32,
    pub kind_id: u32,
    pub speed: i32,
    pub extra_damage_pierce_factor: i32,
    pub extra_damage_slash_factor: i32,
    pub extra_damage_blunt_factor: i32,
    pub max_range: i32,
    pub angle: i32,
    pub enchanted_dps1000: i32,
    pub slot_type_id: u32,
    pub damage_scale: i32,
    pub formula_dps: Formula,
    pub formula_mdps: Formula,
    pub formula_armor: Formula,
    pub formula_hdps: Formula,
    pub min_range: i32,
    pub sheathe_priority: i32,
    pub durability_ratio: f32,
    pub renew_category: i32,
    pub item_proc_id: i32,
    pub stat_multiplier: i32,
    pub anim_r1_id: u32,
    pub anim_l1_id: u32,
    pub anim_r2_id: u32,
    pub anim_l2_id: u32,
    pub anim_r3_id: u32,
    pub anim_l3_id: u32,
}

impl Holdable {
    /// `Holdable.DefaultFistAnimId`.
    pub const DEFAULT_FIST_ANIM_ID: u32 = 2;

    /// `Holdable.GetAttackAnimId(attackIndex, leftHand)`.
    pub fn attack_anim_id(&self, attack_index: i32, left_hand: bool) -> u32 {
        let (a1, a2, a3) = if left_hand {
            (self.anim_l1_id, self.anim_l2_id, self.anim_l3_id)
        } else {
            (self.anim_r1_id, self.anim_r2_id, self.anim_r3_id)
        };
        let valid: Vec<u32> = [a1, a2, a3].into_iter().filter(|a| *a > 0).collect();
        if valid.is_empty() {
            return if left_hand {
                self.anim_r1_id
            } else {
                Self::DEFAULT_FIST_ANIM_ID
            };
        }
        // Math.Abs(int.MinValue) throws in C#; unsigned_abs keeps the same index otherwise.
        valid[(attack_index.unsigned_abs() as usize) % valid.len()]
    }
}

/// `Wearable`.
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct Wearable {
    pub type_id: u32,
    pub slot_type_id: u32,
    pub armor_bp: i32,
    pub magic_resistance_bp: i32,
}

/// `WearableKind` (`wearable_kinds`).
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct WearableKind {
    pub type_id: u32,
    pub armor_ratio: i32,
    pub magic_resistance_ratio: i32,
    pub full_buf_id: u32,
    pub half_buf_id: u32,
    pub extra_damage_pierce: i32,
    pub extra_damage_slash: i32,
    pub extra_damage_blunt: i32,
    pub durability_ratio: f32,
}

/// `WearableSlot` (`wearable_slots`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct WearableSlot {
    pub slot_type_id: u32,
    pub coverage: i32,
}

/// `AttributeModifiers` (`equip_item_attr_modifiers`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct AttributeModifiers {
    pub id: u32,
    pub str_weight: i32,
    pub dex_weight: i32,
    pub sta_weight: i32,
    pub int_weight: i32,
    pub spi_weight: i32,
}

impl AttributeModifiers {
    /// `AllWeight`.
    pub fn all_weight(&self) -> i32 {
        self.str_weight + self.dex_weight + self.sta_weight + self.int_weight + self.spi_weight
    }

    /// `Count` (number of positive weights).
    pub fn count(&self) -> i32 {
        [
            self.str_weight,
            self.dex_weight,
            self.sta_weight,
            self.int_weight,
            self.spi_weight,
        ]
        .iter()
        .filter(|w| **w > 0)
        .count() as i32
    }
}

/// `ItemProcTemplate` (`item_procs`). `SkillTemplate` is resolved by the skills subsystem from
/// `skill_id` (`ItemManager.OnSkillsLoaded`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct ItemProcTemplate {
    pub id: u32,
    pub skill_id: u32,
    /// `ProcChanceKind` (see [`proc_chance_kind`]).
    pub chance_kind: u32,
    pub chance_rate: u32,
    pub chance_param: u32,
    pub cooldown_sec: u32,
    pub finisher: bool,
    pub item_level_based_chance_bonus: u32,
}

/// `EquipItemSetBonus`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct EquipItemSetBonus {
    pub num_pieces: i32,
    pub buff_id: u32,
    pub item_proc_id: u32,
}

/// `EquipItemSet` (`equip_item_set_bonuses` grouped by set).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct EquipItemSet {
    pub id: u32,
    pub bonuses: Vec<EquipItemSetBonus>,
}

/// `ItemLookConvert` (`item_look_convert_required_items`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct ItemLookConvert {
    pub id: u32,
    pub slot_id: u32,
    pub required_item_id: u32,
    pub required_item_count: i32,
}

/// `EquipSlotEnchantingCost`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct EquipSlotEnchantingCost {
    pub id: u32,
    pub slot_type_id: u32,
    pub cost: i32,
}

/// `ItemGradeEnchantingSupport`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct ItemGradeEnchantingSupport {
    pub id: u32,
    pub item_id: u32,
    pub require_grade_min: i32,
    pub require_grade_max: i32,
    pub add_success_ratio: i32,
    pub add_success_mul: i32,
    pub add_great_success_ratio: i32,
    pub add_great_success_mul: i32,
    pub add_break_ratio: i32,
    pub add_break_mul: i32,
    pub add_downgrade_ratio: i32,
    pub add_downgrade_mul: i32,
    pub add_great_success_grade: i32,
}

/// `ItemCapScale`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct ItemCapScale {
    pub id: u32,
    pub skill_id: u32,
    pub scale_min: i32,
    pub scale_max: i32,
}

/// `GradeDistributions` (`item_grade_distributions`; `weights[i]` = `Weight{i}`).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct GradeDistributions {
    pub id: i32,
    pub name: String,
    pub weights: [i32; 12],
}

/// `LootPackDroppingNpc`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct LootPackDroppingNpc {
    pub id: u32,
    pub npc_id: u32,
    pub loot_pack_id: u32,
    pub default_pack: bool,
}

/// `LootPackConvertFish` (`doodad_func_convert_fish_items`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct LootPackConvertFish {
    pub id: u32,
    pub item_id: u32,
    pub loot_pack_id: u32,
    pub doodad_func_convert_fish_id: u32,
}

/// `ArmorGradeBuff`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct ArmorGradeBuff {
    pub id: u8,
    pub armor_type: u32,
    pub item_grade: u32,
    pub buff_id: u32,
}

/// `ItemSetItem`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct ItemSetItem {
    pub id: u32,
    pub item_set_id: u32,
    pub item_id: u32,
    pub count: i32,
}

/// `ItemSet` (`Items` in insertion order).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct ItemSet {
    pub id: u32,
    pub kind_id: u32,
    pub name: String,
    pub items: Vec<ItemSetItem>,
}

/// `RuneTemplate` specifics (`item_enchanting_gems`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct RuneTemplate {
    pub equip_slot_group_id: u32,
    pub equip_level: u8,
    pub item_grade_id: u8,
}

/// `BackpackTemplate` specifics (`item_backpacks`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct BackpackTemplate {
    pub asset_id: u32,
    pub backpack_type: u32,
    pub declare_siege_zone_group_id: u32,
    pub heavy: bool,
    pub asset2_id: u32,
    pub normal_speciality: bool,
    pub use_as_stat: bool,
    pub skin_kind_id: u32,
}

/// `BodyPartTemplate` specifics (`item_body_parts`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct BodyPartTemplate {
    pub item_id: u32,
    pub model_id: u32,
    pub npc_only: bool,
    pub slot_type_id: u32,
    pub beauty_shop_only: bool,
}

/// The `ItemTemplate` subclass and its class-specific fields.
#[derive(Debug, Clone, PartialEq)]
pub enum TemplateKind {
    Item,
    Armor(EquipTemplate),
    Weapon(EquipTemplate),
    Accessory(EquipTemplate),
    SummonMate { npc_id: u32 },
    SummonSlave { slave_id: u32 },
    BodyPart { slot_type_id: u32, model_id: u32 },
    Rune,
    Backpack { backpack_type: u32 },
    Ucc,
    MusicSheet,
    TreasureMap,
}

/// `EquipItemTemplate` + the armor/weapon/accessory specifics.
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct EquipTemplate {
    /// Wearable / holdable `slot_type_id` (`EquipmentItemSlotType`).
    pub slot_type_id: u32,
    /// `SlotTemplate.Coverage` (armor / accessory).
    pub coverage: i32,
    /// `KindTemplate.DurabilityRatio` (armor / accessory) or `HoldableTemplate.DurabilityRatio`.
    pub durability_ratio: f32,
    pub durability_multiplier: i32,
    pub charge_lifetime: i32,
    pub charge_count: i32,
    pub default_dye_item_id: u32,
    /// `ModSetId` (`equip_item_attr_modifiers` id, 0 = none).
    pub mod_set_id: u32,
    pub repairable: bool,
    /// `BaseEnchantable` (armor / weapon only).
    pub base_enchantable: bool,
    /// `BaseEquipment` (armor / weapon only).
    pub base_equipment: bool,
    pub recharge_buff_id: u32,
    /// `EquipItemSetId` (`eiset_id`).
    pub equip_item_set_id: u32,
    /// `ItemLookConvert` (its id; armor: by wearable slot, weapon: by holdable).
    pub item_look_convert_id: Option<u32>,
    /// `WeaponTemplate.HoldableTemplate` (holdable id; weapons only).
    pub holdable_id: u32,
    /// `WearableTemplate` / `KindTemplate` type (`ArmorType`; armor / accessory only).
    pub armor_type_id: u32,
}

/// `ItemTemplate` (subset).
#[derive(Debug, Clone, PartialEq)]
pub struct ItemTemplate {
    pub id: u32,
    pub kind: TemplateKind,
    pub name: String,
    pub bind_type: u32,
    pub max_count: i32,
    pub gradable: bool,
    pub fixed_grade: i32,
    pub exp_abs_lifetime: i32,
    pub exp_online_lifetime: i32,
    /// `ExpDate` as unix seconds (`None` = `DateTime.MinValue`).
    pub exp_date: Option<i64>,
    pub category_id: i32,
    pub level: i32,
    pub price: i32,
    pub refund: i32,
    pub pickup_limit: i32,
    pub sellable: bool,
    pub use_skill_id: u32,
    pub use_skill_as_reagent: bool,
    /// `ImplId` (`ItemImplEnum`).
    pub impl_id: i32,
    pub buff_id: u32,
    pub loot_multi: bool,
    pub loot_quest_id: u32,
    pub honor_price: i32,
    pub level_requirement: i32,
    pub auction_category_a: i32,
    pub auction_category_b: i32,
    pub auction_category_c: i32,
    pub level_limit: i32,
    pub disenchantable: bool,
    pub living_point_price: i32,
    pub char_gender: u8,
    pub specialty_zone_id: u32,
}

impl ItemTemplate {
    pub fn new(id: u32, kind: TemplateKind) -> Self {
        Self {
            id,
            kind,
            name: String::new(),
            bind_type: 0,
            max_count: 0,
            gradable: false,
            fixed_grade: 0,
            exp_abs_lifetime: 0,
            exp_online_lifetime: 0,
            exp_date: None,
            category_id: 0,
            level: 0,
            price: 0,
            refund: 0,
            pickup_limit: 0,
            sellable: false,
            use_skill_id: 0,
            use_skill_as_reagent: false,
            impl_id: 0,
            buff_id: 0,
            loot_multi: false,
            loot_quest_id: 0,
            honor_price: 0,
            level_requirement: 0,
            auction_category_a: 0,
            auction_category_b: 0,
            auction_category_c: 0,
            level_limit: 0,
            disenchantable: false,
            living_point_price: 0,
            char_gender: 0,
            specialty_zone_id: 0,
        }
    }

    /// A plain `ItemTemplate` (tests / synthetic data).
    pub fn plain(id: u32, max_count: i32) -> Self {
        Self {
            max_count,
            fixed_grade: -1,
            ..Self::new(id, TemplateKind::Item)
        }
    }

    /// `ItemTemplate.ClassType`.
    pub fn class_type(&self) -> ItemClass {
        match self.kind {
            TemplateKind::Item => ItemClass::Item,
            TemplateKind::Armor(_) => ItemClass::Armor,
            TemplateKind::Weapon(_) => ItemClass::Weapon,
            TemplateKind::Accessory(_) => ItemClass::Accessory,
            TemplateKind::SummonMate { .. } => ItemClass::SummonMate,
            TemplateKind::SummonSlave { .. } => ItemClass::SummonSlave,
            TemplateKind::BodyPart { .. } => ItemClass::BodyPart,
            TemplateKind::Rune => ItemClass::Rune,
            TemplateKind::Backpack { .. } => ItemClass::Backpack,
            TemplateKind::Ucc => ItemClass::UccItem,
            TemplateKind::MusicSheet => ItemClass::MusicSheetItem,
            TemplateKind::TreasureMap => ItemClass::TreasureMap,
        }
    }

    pub fn equip(&self) -> Option<&EquipTemplate> {
        match &self.kind {
            TemplateKind::Armor(e) | TemplateKind::Weapon(e) | TemplateKind::Accessory(e) => {
                Some(e)
            }
            _ => None,
        }
    }

    /// `template is EquipItemTemplate`.
    pub fn is_equip(&self) -> bool {
        self.equip().is_some()
    }

    /// The `EquipmentItemSlotType` used by `EquipmentContainer.GetAllowedGearSlots` / `CanAccept`
    /// (`None` = not equippable).
    pub fn gear_slot_type(&self) -> Option<u32> {
        match &self.kind {
            TemplateKind::BodyPart { slot_type_id, .. } => Some(*slot_type_id),
            TemplateKind::Armor(e) | TemplateKind::Weapon(e) | TemplateKind::Accessory(e) => {
                Some(e.slot_type_id)
            }
            TemplateKind::Backpack { .. } => Some(EQUIPMENT_ITEM_SLOT_TYPE_BACKPACK),
            _ => None,
        }
    }

    /// `template.BindType.HasFlag(flag)` (the C# enum is not a flags enum, so this is a bit
    /// test of the raw value).
    pub fn bind_type_has_flag(&self, flag: u32) -> bool {
        self.bind_type & flag == flag
    }
}

/// `EquipmentItemSlotType.Backpack`.
pub const EQUIPMENT_ITEM_SLOT_TYPE_BACKPACK: u32 = 30;

/// Item game data (`ItemManager`'s template tables).
#[derive(Debug, Clone, Default)]
pub struct ItemData {
    pub config: ItemConfig,
    /// `_grades` (the short form; see [`ItemData::full_grades`]).
    pub grades: HashMap<i32, GradeTemplate>,
    pub templates: HashMap<u32, ItemTemplate>,
    /// `_templates` insertion order (template ids; filled by [`ItemData::load`]).
    pub template_order: Vec<u32>,
    /// `_grades` with every column.
    pub full_grades: HashMap<i32, FullGradeTemplate>,
    /// `_gradesOrdered` (grade order -> grade id).
    pub grades_ordered: HashMap<i32, i32>,
    /// `ItemManager.MaxGradeId` / `MaxGradeValue`.
    pub max_grade_id: i32,
    pub max_grade_value: i32,
    /// `_holdables`.
    pub holdables: HashMap<u32, Holdable>,
    /// `_wearables` (key `type_id * 128 + slot_type_id`).
    pub wearables: HashMap<u32, Wearable>,
    /// `_wearableKinds`.
    pub wearable_kinds: HashMap<u32, WearableKind>,
    /// `_wearableSlots`.
    pub wearable_slots: HashMap<u32, WearableSlot>,
    /// `_defaultDyeIds`.
    pub default_dye_ids: HashMap<u32, u32>,
    /// `_modifiers`.
    pub modifiers: HashMap<u32, AttributeModifiers>,
    /// `_itemProcTemplates`.
    pub item_procs: HashMap<u32, ItemProcTemplate>,
    /// `_equipItemSets`.
    pub equip_item_sets: HashMap<u32, EquipItemSet>,
    /// `_itemLookConverts`, `_holdableItemLookConverts`, `_wearableItemLookConverts`.
    pub item_look_converts: HashMap<u32, ItemLookConvert>,
    pub holdable_item_look_converts: HashMap<u32, u32>,
    pub wearable_item_look_converts: HashMap<u32, u32>,
    /// `RuneTemplate` / `BackpackTemplate` / `BodyPartTemplate` specifics by item id.
    pub runes: HashMap<u32, RuneTemplate>,
    pub backpacks: HashMap<u32, BackpackTemplate>,
    pub body_parts: HashMap<u32, BodyPartTemplate>,
    /// `_enchantingCosts` (by slot type).
    pub enchanting_costs: HashMap<u32, EquipSlotEnchantingCost>,
    /// `_enchantingSupports` (by item id).
    pub enchanting_supports: HashMap<u32, ItemGradeEnchantingSupport>,
    /// `_socketChance`.
    pub socket_chance: HashMap<u32, u32>,
    /// `_itemCapScales` (by skill id).
    pub item_cap_scales: HashMap<u32, ItemCapScale>,
    /// `_itemGradeDistributions`.
    pub grade_distributions: HashMap<i32, GradeDistributions>,
    /// `_lootPackDroppingNpc` (by npc template id, rows in table order).
    pub loot_pack_dropping_npc: HashMap<u32, Vec<LootPackDroppingNpc>>,
    /// `_lootPackConvertFish` (by item id).
    pub loot_pack_convert_fish: HashMap<u32, Vec<LootPackConvertFish>>,
    /// `_itemDoodadTemplates` (doodad id -> item ids).
    pub item_doodads: HashMap<u32, Vec<u32>>,
    /// `_itemUnitModifiers`.
    pub item_unit_modifiers: HashMap<u32, Vec<BonusTemplate>>,
    /// `_armorGradeBuffs` (armor type -> grade -> buff).
    pub armor_grade_buffs: HashMap<u32, HashMap<u32, ArmorGradeBuff>>,
    /// `_itemSets`.
    pub item_sets: HashMap<u32, ItemSet>,
}

fn load_err(msg: String) -> rusqlite::Error {
    rusqlite::Error::InvalidParameterName(msg)
}

impl ItemData {
    pub fn template(&self, id: u32) -> Option<&ItemTemplate> {
        self.templates.get(&id)
    }

    /// `GetGradeTemplate`.
    pub fn grade(&self, grade: i32) -> Option<&GradeTemplate> {
        self.grades.get(&grade)
    }

    /// `GetGradeTemplate` with every column.
    pub fn full_grade(&self, grade: i32) -> Option<&FullGradeTemplate> {
        self.full_grades.get(&grade)
    }

    /// Adds a grade to both grade tables (tests / synthetic data).
    pub fn insert_grade(&mut self, g: FullGradeTemplate) {
        self.grades.insert(g.grade, g.short());
        self.grades_ordered.insert(g.grade_order, g.grade);
        self.full_grades.insert(g.grade, g);
    }

    /// `GetGradeTemplateByOrder`.
    pub fn grade_by_order(&self, order: i32) -> Option<&FullGradeTemplate> {
        self.grades_ordered
            .get(&order)
            .and_then(|g| self.full_grades.get(g))
    }

    /// `GetEquippedItemSet`.
    pub fn equip_item_set(&self, id: u32) -> Option<&EquipItemSet> {
        self.equip_item_sets.get(&id)
    }

    /// `GetHoldable`.
    pub fn holdable(&self, id: u32) -> Option<&Holdable> {
        self.holdables.get(&id)
    }

    /// `WeaponTemplate.HoldableTemplate` of `template` (`None` for non-weapons).
    pub fn holdable_of(&self, template: &ItemTemplate) -> Option<&Holdable> {
        match &template.kind {
            TemplateKind::Weapon(e) => self.holdables.get(&e.holdable_id),
            _ => None,
        }
    }

    /// `ArmorTemplate/AccessoryTemplate.KindTemplate`.
    pub fn wearable_kind_of(&self, template: &ItemTemplate) -> Option<&WearableKind> {
        match &template.kind {
            TemplateKind::Armor(e) | TemplateKind::Accessory(e) => {
                self.wearable_kinds.get(&e.armor_type_id)
            }
            _ => None,
        }
    }

    /// `ArmorTemplate/AccessoryTemplate.WearableTemplate`.
    pub fn wearable_of(&self, template: &ItemTemplate) -> Option<&Wearable> {
        match &template.kind {
            TemplateKind::Armor(e) | TemplateKind::Accessory(e) => self
                .wearables
                .get(&(e.armor_type_id * 128 + e.slot_type_id)),
            _ => None,
        }
    }

    /// `EquipItemTemplate.ItemLookConvert`.
    pub fn item_look_convert_of(&self, template: &ItemTemplate) -> Option<&ItemLookConvert> {
        template
            .equip()?
            .item_look_convert_id
            .and_then(|id| self.item_look_converts.get(&id))
    }

    /// `GetEquipSlotEnchantingCost`.
    pub fn equip_slot_enchanting_cost(
        &self,
        slot_type_id: u32,
    ) -> Option<&EquipSlotEnchantingCost> {
        self.enchanting_costs.get(&slot_type_id)
    }

    /// `GetItemGradEnchantingSupportByItemId`.
    pub fn enchanting_support(&self, item_id: u32) -> Option<&ItemGradeEnchantingSupport> {
        self.enchanting_supports.get(&item_id)
    }

    /// `GetLootPackIdByNpcId` (empty when none).
    pub fn loot_packs_by_npc(&self, npc_id: u32) -> &[LootPackDroppingNpc] {
        self.loot_pack_dropping_npc
            .get(&npc_id)
            .map_or(&[], Vec::as_slice)
    }

    /// `GetLootPackIdByItemId` (fish -> trophy loot packs).
    pub fn loot_packs_by_fish(&self, item_id: u32) -> &[LootPackConvertFish] {
        self.loot_pack_convert_fish
            .get(&item_id)
            .map_or(&[], Vec::as_slice)
    }

    /// `GetGradeDistributions`.
    pub fn grade_distributions(&self, id: u8) -> Option<&GradeDistributions> {
        self.grade_distributions.get(&i32::from(id))
    }

    /// `GetSocketChance(numSockets)` (chance of the *next* socket).
    pub fn socket_chance(&self, num_sockets: u32) -> u32 {
        self.socket_chance
            .get(&num_sockets.wrapping_add(1))
            .copied()
            .unwrap_or(0)
    }

    /// `GetItemCapScale(skillId)`.
    pub fn item_cap_scale(&self, skill_id: u32) -> Option<&ItemCapScale> {
        self.item_cap_scales.get(&skill_id)
    }

    /// `GetAttributeModifiers(id)` (`None` where the C# indexer throws).
    pub fn attribute_modifiers(&self, id: u32) -> Option<&AttributeModifiers> {
        self.modifiers.get(&id)
    }

    /// `GetItemIdsFromDoodad`.
    pub fn item_ids_from_doodad(&self, doodad_id: u32) -> &[u32] {
        self.item_doodads.get(&doodad_id).map_or(&[], Vec::as_slice)
    }

    /// `GetItemProcTemplate`.
    pub fn item_proc(&self, id: u32) -> Option<&ItemProcTemplate> {
        self.item_procs.get(&id)
    }

    /// `GetUnitModifiers(itemId)`.
    pub fn unit_modifiers(&self, item_id: u32) -> &[BonusTemplate] {
        self.item_unit_modifiers
            .get(&item_id)
            .map_or(&[], Vec::as_slice)
    }

    /// `GetArmorGradeBuff(type, grade)`.
    pub fn armor_grade_buff(&self, armor_type: u32, grade: u32) -> Option<&ArmorGradeBuff> {
        self.armor_grade_buffs.get(&armor_type)?.get(&grade)
    }

    /// `GetItemSet`.
    pub fn item_set(&self, id: u32) -> Option<&ItemSet> {
        self.item_sets.get(&id)
    }

    /// `GetAllItems`.
    pub fn all_templates(&self) -> impl Iterator<Item = &ItemTemplate> {
        self.templates.values()
    }

    /// `IsAutoEquipTradePack(itemTemplateId)`: a backpack template that does not bind on equip.
    pub fn is_auto_equip_trade_pack(&self, template_id: u32) -> bool {
        self.template(template_id).is_some_and(|t| {
            matches!(t.kind, TemplateKind::Backpack { .. })
                && !t.bind_type_has_flag(bind_type::BIND_ON_EQUIP)
        })
    }

    /// `Armor/Accessory/Weapon.MaxDurability` for an item of `template` with `grade`. `None` when
    /// the grade template is missing (a `NullReferenceException` in C#) or for non-equipment.
    #[allow(clippy::excessive_precision)] // the C# float literal 0.0099999998f
    pub fn max_durability(&self, template: &ItemTemplate, grade: i32) -> Option<u8> {
        let g = self.grade(grade)?;
        let durability_const = self.config.durability_const;
        let (first, e) = match &template.kind {
            TemplateKind::Armor(e) | TemplateKind::Accessory(e) => {
                // (int)(template.SlotTemplate.Coverage * 100 + 0.5f)
                (((e.coverage * 100) as f32 + 0.5f32) as i32, e)
            }
            TemplateKind::Weapon(e) => (
                // (int)(GetHoldableDurabilityConst() * 100 + 0.5f)
                (self.config.holdable_durability_const * 100.0 + 0.5f32) as i32,
                e,
            ),
            _ => return None,
        };
        let ratio = (e.durability_ratio * 1000.0 + 0.5f32) as i32;
        // (int)(a * b * grade.Durability * 1000 * 0.00000001f) * DurabilityConst
        let inner =
            (first.wrapping_mul(ratio) as f32 * g.durability * 1000.0 * 0.000_000_01f32) as i32;
        let durability = inner as f32 * durability_const;
        // (float)Math.Round(durability * DurabilityMultiplier * 0.0099999998f)  (ToEven)
        let v = f64::from(durability * e.durability_multiplier as f32 * 0.009_999_999_8f32)
            .round_ties_even() as f32;
        Some(v as u8)
    }

    /// `ItemManager.Load`. As in C#, an empty `item_configs` table aborts the whole load
    /// (`if (!reader.Read()) return;`) and leaves every table empty.
    pub fn load(conn: &Connection) -> rusqlite::Result<Self> {
        let mut data = ItemData::default();
        let mut has_config = false;
        {
            let mut stmt = conn.prepare("SELECT * FROM item_configs")?;
            let names: Vec<String> = stmt.column_names().iter().map(|s| s.to_string()).collect();
            let mut rows = stmt.query([])?;
            if let Some(row) = rows.next()? {
                let r = super::sqlite::SqlRow::new(row, &names);
                data.config = ItemConfig {
                    durability_decrement_chance: r.f32("durability_decrement_chance")?,
                    durability_repair_cost_factor: r.f32("durability_repair_cost_factor")?,
                    durability_const: r.f32("durability_const")?,
                    holdable_durability_const: r.f32("holdable_durability_const")?,
                    wearable_durability_const: r.f32("wearable_durability_const")?,
                    death_durability_loss_ratio: r.u8("death_durability_loss_ratio")?,
                    item_stat_const: r.i32("item_stat_const")?,
                    holdable_stat_const: r.i32("holdable_stat_const")?,
                    wearable_stat_const: r.i32("wearable_stat_const")?,
                    stat_value_const: r.i32("stat_value_const")?,
                };
                has_config = true;
            }
        }
        if !has_config {
            tracing::warn!("item_configs is empty: item templates not loaded (as in C#)");
            return Ok(data);
        }
        tracing::info!("Loading item templates ...");

        for_each_row(
            conn,
            "SELECT * FROM item_look_convert_required_items",
            [],
            |r| {
                let t = ItemLookConvert {
                    id: r.u32("item_look_convert_id")?,
                    slot_id: 0,
                    required_item_id: r.u32("item_id")?,
                    required_item_count: r.i32("item_count")?,
                };
                data.item_look_converts.entry(t.id).or_insert(t);
                Ok(())
            },
        )?;
        for_each_row(conn, "SELECT * FROM item_look_convert_holdables", [], |r| {
            let convert = r.u32("item_look_convert_id")?;
            data.holdable_item_look_converts
                .entry(r.u32("holdable_id")?)
                .or_insert(convert);
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM item_look_convert_wearables", [], |r| {
            let convert = r.u32("item_look_convert_id")?;
            data.wearable_item_look_converts
                .entry(r.u32("wearable_slot_id")?)
                .or_insert(convert);
            Ok(())
        })?;

        for_each_row(conn, "SELECT * FROM item_grades", [], |r| {
            let g = FullGradeTemplate {
                grade: r.i32("id")?,
                grade_order: r.i32("grade_order")?,
                holdable_dps: r.f32("var_holdable_dps")?,
                holdable_armor: r.f32("var_holdable_armor")?,
                holdable_magic_dps: r.f32("var_holdable_magic_dps")?,
                wearable_armor: r.f32("var_wearable_armor")?,
                wearable_magic_resistance: r.f32("var_wearable_magic_resistance")?,
                durability: r.f32("durability_value")?,
                upgrade_ratio: r.i32("upgrade_ratio")?,
                stat_multiplier: r.i32("stat_multiplier")?,
                refund_multiplier: r.i32("refund_multiplier")?,
                enchant_success_ratio: r.i32("grade_enchant_success_ratio")?,
                enchant_great_success_ratio: r.i32("grade_enchant_great_success_ratio")?,
                enchant_break_ratio: r.i32("grade_enchant_break_ratio")?,
                enchant_downgrade_ratio: r.i32("grade_enchant_downgrade_ratio")?,
                enchant_cost: r.i32("grade_enchant_cost")?,
                holdable_heal_dps: r.f32("var_holdable_heal_dps")?,
                enchant_downgrade_min: r.i32("grade_enchant_downgrade_min")?,
                enchant_downgrade_max: r.i32("grade_enchant_downgrade_max")?,
                currency_id: r.i32("currency_id")?,
            };
            if data.full_grades.contains_key(&g.grade)
                || data.grades_ordered.contains_key(&g.grade_order)
            {
                return Err(load_err(format!(
                    "duplicate item grade {} / order {} (Dictionary.Add)",
                    g.grade, g.grade_order
                )));
            }
            data.insert_grade(g);
            Ok(())
        })?;
        // MaxGradeId = _grades.Keys.Max() (throws on an empty table).
        data.max_grade_id = data
            .full_grades
            .keys()
            .copied()
            .max()
            .ok_or_else(|| load_err("item_grades is empty (Max of empty sequence)".into()))?;
        data.max_grade_value = data.grades_ordered.keys().copied().max().unwrap_or(0);

        for_each_row(conn, "SELECT * FROM holdables", [], |r| {
            let h = Holdable {
                id: r.u32("id")?,
                kind_id: r.u32("kind_id")?,
                speed: r.i32("speed")?,
                extra_damage_pierce_factor: r.i32("extra_damage_pierce_factor")?,
                extra_damage_slash_factor: r.i32("extra_damage_slash_factor")?,
                extra_damage_blunt_factor: r.i32("extra_damage_blunt_factor")?,
                max_range: r.i32("max_range")?,
                angle: r.i32("angle")?,
                enchanted_dps1000: r.i32("enchanted_dps1000")?,
                slot_type_id: r.u32("slot_type_id")?,
                damage_scale: r.i32("damage_scale")?,
                formula_dps: Formula::new(r.string("formula_dps")?),
                formula_mdps: Formula::new(r.string("formula_mdps")?),
                formula_armor: Formula::new(r.string("formula_armor")?),
                min_range: r.i32("min_range")?,
                sheathe_priority: r.i32("sheathe_priority")?,
                durability_ratio: r.f32("durability_ratio")?,
                renew_category: r.i32("renew_category")?,
                item_proc_id: r.i32("item_proc_id")?,
                stat_multiplier: r.i32("stat_multiplier")?,
                formula_hdps: Formula::new(r.string("formula_hdps")?),
                anim_r1_id: r.u32_or("anim_r1_id", 0)?,
                anim_l1_id: r.u32_or("anim_l1_id", 0)?,
                anim_r2_id: r.u32_or("anim_r2_id", 0)?,
                anim_l2_id: r.u32_or("anim_l2_id", 0)?,
                anim_r3_id: r.u32_or("anim_r3_id", 0)?,
                anim_l3_id: r.u32_or("anim_l3_id", 0)?,
            };
            if data.holdables.insert(h.id, h).is_some() {
                return Err(load_err("duplicate holdable (Dictionary.Add)".into()));
            }
            Ok(())
        })?;

        for_each_row(conn, "SELECT * FROM wearables", [], |r| {
            let w = Wearable {
                type_id: r.u32("armor_type_id")?,
                slot_type_id: r.u32("slot_type_id")?,
                armor_bp: r.i32("armor_bp")?,
                magic_resistance_bp: r.i32("magic_resistance_bp")?,
            };
            let key = w.type_id.wrapping_mul(128).wrapping_add(w.slot_type_id);
            if data.wearables.insert(key, w).is_some() {
                return Err(load_err("duplicate wearable (Dictionary.Add)".into()));
            }
            Ok(())
        })?;

        for_each_row(conn, "SELECT * FROM wearable_kinds", [], |r| {
            let k = WearableKind {
                type_id: r.u32("armor_type_id")?,
                armor_ratio: r.i32("armor_ratio")?,
                magic_resistance_ratio: r.i32("magic_resistance_ratio")?,
                full_buf_id: r.u32("full_buff_id")?,
                half_buf_id: r.u32("half_buff_id")?,
                extra_damage_pierce: r.i32("extra_damage_pierce")?,
                extra_damage_slash: r.i32("extra_damage_slash")?,
                extra_damage_blunt: r.i32("extra_damage_blunt")?,
                durability_ratio: r.f32("durability_ratio")?,
            };
            if data.wearable_kinds.insert(k.type_id, k).is_some() {
                return Err(load_err("duplicate wearable kind (Dictionary.Add)".into()));
            }
            Ok(())
        })?;

        for_each_row(conn, "SELECT * FROM wearable_slots", [], |r| {
            let s = WearableSlot {
                slot_type_id: r.u32("slot_type_id")?,
                coverage: r.i32("coverage")?,
            };
            if data.wearable_slots.insert(s.slot_type_id, s).is_some() {
                return Err(load_err("duplicate wearable slot (Dictionary.Add)".into()));
            }
            Ok(())
        })?;

        for_each_row(conn, "SELECT * FROM dyeable_items", [], |r| {
            if data
                .default_dye_ids
                .insert(r.u32("item_id")?, r.u32("default_dyeing_item_id")?)
                .is_some()
            {
                return Err(load_err("duplicate dyeable item (Dictionary.Add)".into()));
            }
            Ok(())
        })?;

        for_each_row(conn, "SELECT * FROM equip_item_attr_modifiers", [], |r| {
            let m = AttributeModifiers {
                id: r.u32("id")?,
                str_weight: r.i32("str_weight")?,
                dex_weight: r.i32("dex_weight")?,
                sta_weight: r.i32("sta_weight")?,
                int_weight: r.i32("int_weight")?,
                spi_weight: r.i32("spi_weight")?,
            };
            if data.modifiers.insert(m.id, m).is_some() {
                return Err(load_err("duplicate attr modifier (Dictionary.Add)".into()));
            }
            Ok(())
        })?;

        for_each_row(conn, "SELECT * FROM item_procs", [], |r| {
            let p = ItemProcTemplate {
                id: r.u32("id")?,
                skill_id: r.u32("skill_id")?,
                chance_kind: r.u32("chance_kind_id")?,
                chance_rate: r.u32("chance_rate")?,
                chance_param: r.u32("chance_param")?,
                cooldown_sec: r.u32("cooldown_sec")?,
                finisher: r.bool_str("finisher")?,
                item_level_based_chance_bonus: r.u32("item_level_based_chance_bonus")?,
            };
            if data.item_procs.insert(p.id, p).is_some() {
                return Err(load_err("duplicate item proc (Dictionary.Add)".into()));
            }
            Ok(())
        })?;

        for_each_row(conn, "SELECT * FROM equip_item_set_bonuses", [], |r| {
            let id = r.u32("equip_item_set_id")?;
            let set = data
                .equip_item_sets
                .entry(id)
                .or_insert_with(|| EquipItemSet {
                    id,
                    bonuses: Vec::new(),
                });
            let bonus = EquipItemSetBonus {
                num_pieces: r.i32("num_pieces")?,
                buff_id: r.u32_or("buff_id", 0)?,
                item_proc_id: r.u32_or("proc_id", 0)?,
            };
            if bonus.buff_id != 0 || bonus.item_proc_id != 0 {
                set.bonuses.push(bonus);
            }
            Ok(())
        })?;

        let missing = |what: &str, k: u32| load_err(format!("{what} {k} missing (KeyNotFound)"));
        let look_convert = |map: &HashMap<u32, u32>,
                            converts: &HashMap<u32, ItemLookConvert>,
                            key: u32|
         -> rusqlite::Result<Option<u32>> {
            match map.get(&key) {
                None => Ok(None),
                Some(c) if converts.contains_key(c) => Ok(Some(*c)),
                Some(c) => Err(missing("item_look_convert", *c)),
            }
        };

        let mut tmp = Vec::new();
        for_each_row(conn, "SELECT * FROM item_armors", [], |r| {
            let id = r.u32("item_id")?;
            let slot = r.u32("slot_type_id")?;
            let ty = r.u32("type_id")?;
            let key = ty.wrapping_mul(128).wrapping_add(slot);
            let w = data
                .wearables
                .get(&key)
                .ok_or_else(|| missing("wearable", key))?;
            let kind = data
                .wearable_kinds
                .get(&ty)
                .ok_or_else(|| missing("wearable_kind", ty))?;
            let coverage = data
                .wearable_slots
                .get(&slot)
                .ok_or_else(|| missing("wearable_slot", slot))?
                .coverage;
            tmp.push(ItemTemplate::new(
                id,
                TemplateKind::Armor(EquipTemplate {
                    slot_type_id: w.slot_type_id,
                    coverage,
                    durability_ratio: kind.durability_ratio,
                    durability_multiplier: r.i32("durability_multiplier")?,
                    charge_lifetime: r.i32_or("charge_lifetime", 0)?,
                    charge_count: r.i32_or("charge_count", 0)?,
                    default_dye_item_id: data.default_dye_ids.get(&id).copied().unwrap_or(0),
                    mod_set_id: r.u32_or("mod_set_id", 0)?,
                    repairable: r.bool_str("repairable")?,
                    base_enchantable: r.bool_str("base_enchantable")?,
                    base_equipment: r.bool_str("base_equipment")?,
                    recharge_buff_id: r.u32_or("recharge_buff_id", 0)?,
                    equip_item_set_id: r.u32_or("eiset_id", 0)?,
                    item_look_convert_id: look_convert(
                        &data.wearable_item_look_converts,
                        &data.item_look_converts,
                        slot,
                    )?,
                    holdable_id: 0,
                    armor_type_id: ty,
                }),
            ));
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM item_weapons", [], |r| {
            let hid = r.u32("holdable_id")?;
            let h = data
                .holdables
                .get(&hid)
                .ok_or_else(|| missing("holdable", hid))?;
            tmp.push(ItemTemplate::new(
                r.u32("item_id")?,
                TemplateKind::Weapon(EquipTemplate {
                    slot_type_id: h.slot_type_id,
                    coverage: 0,
                    durability_ratio: h.durability_ratio,
                    durability_multiplier: r.i32("durability_multiplier")?,
                    charge_lifetime: r.i32_or("charge_lifetime", 0)?,
                    charge_count: r.i32_or("charge_count", 0)?,
                    default_dye_item_id: 0,
                    mod_set_id: r.u32_or("mod_set_id", 0)?,
                    repairable: r.bool_str("repairable")?,
                    // reader.GetBoolean("base_enchantable") (numeric): 't' -> false.
                    base_enchantable: r.bool("base_enchantable")?,
                    base_equipment: r.bool_str("base_equipment")?,
                    recharge_buff_id: r.u32_or("recharge_buff_id", 0)?,
                    equip_item_set_id: r.u32_or("eiset_id", 0)?,
                    item_look_convert_id: look_convert(
                        &data.holdable_item_look_converts,
                        &data.item_look_converts,
                        hid,
                    )?,
                    holdable_id: hid,
                    armor_type_id: 0,
                }),
            ));
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM item_accessories", [], |r| {
            let slot = r.u32("slot_type_id")?;
            let ty = r.u32("type_id")?;
            let key = ty.wrapping_mul(128).wrapping_add(slot);
            let w = data
                .wearables
                .get(&key)
                .ok_or_else(|| missing("wearable", key))?;
            let kind = data
                .wearable_kinds
                .get(&ty)
                .ok_or_else(|| missing("wearable_kind", ty))?;
            let coverage = data
                .wearable_slots
                .get(&slot)
                .ok_or_else(|| missing("wearable_slot", slot))?
                .coverage;
            tmp.push(ItemTemplate::new(
                r.u32("item_id")?,
                TemplateKind::Accessory(EquipTemplate {
                    slot_type_id: w.slot_type_id,
                    coverage,
                    durability_ratio: kind.durability_ratio,
                    durability_multiplier: r.i32("durability_multiplier")?,
                    charge_lifetime: r.i32_or("charge_lifetime", 0)?,
                    charge_count: r.i32_or("charge_count", 0)?,
                    default_dye_item_id: 0,
                    mod_set_id: r.u32_or("mod_set_id", 0)?,
                    repairable: r.bool_str("repairable")?,
                    base_enchantable: false,
                    base_equipment: false,
                    recharge_buff_id: r.u32_or("recharge_buff_id", 0)?,
                    equip_item_set_id: r.u32_or("eiset_id", 0)?,
                    item_look_convert_id: None,
                    holdable_id: 0,
                    armor_type_id: ty,
                }),
            ));
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM item_summon_mates", [], |r| {
            tmp.push(ItemTemplate::new(
                r.u32("item_id")?,
                TemplateKind::SummonMate {
                    npc_id: r.u32("npc_id")?,
                },
            ));
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM item_summon_slaves", [], |r| {
            tmp.push(ItemTemplate::new(
                r.u32("item_id")?,
                TemplateKind::SummonSlave {
                    slave_id: r.u32("slave_id")?,
                },
            ));
            Ok(())
        })?;
        let mut body_parts = Vec::new();
        for_each_row(conn, "SELECT * FROM item_body_parts ORDER BY id", [], |r| {
            if r.is_null("item_id")? {
                return Ok(());
            }
            let b = BodyPartTemplate {
                item_id: r.u32("item_id")?,
                model_id: r.u32("model_id")?,
                npc_only: r.bool_str("npc_only")?,
                slot_type_id: r.u32("slot_type_id")?,
                beauty_shop_only: r.bool_str("beautyshop_only")?,
            };
            body_parts.push(b);
            tmp.push(ItemTemplate::new(
                b.item_id,
                TemplateKind::BodyPart {
                    slot_type_id: b.slot_type_id,
                    model_id: b.model_id,
                },
            ));
            Ok(())
        })?;
        let mut runes = Vec::new();
        for_each_row(conn, "SELECT * FROM item_enchanting_gems", [], |r| {
            let id = r.u32("item_id")?;
            runes.push((
                id,
                RuneTemplate {
                    equip_slot_group_id: r.u32_or("equip_slot_group_id", 0)?,
                    equip_level: r.u8_or("equip_level", 0)?,
                    item_grade_id: r.u8_or("item_grade_id", 0)?,
                },
            ));
            tmp.push(ItemTemplate::new(id, TemplateKind::Rune));
            Ok(())
        })?;
        let mut backpacks = Vec::new();
        for_each_row(conn, "SELECT * FROM item_backpacks", [], |r| {
            let id = r.u32("item_id")?;
            let b = BackpackTemplate {
                asset_id: r.u32("asset_id")?,
                backpack_type: r.u32("backpack_type_id")?,
                declare_siege_zone_group_id: r.u32("declare_siege_zone_group_id")?,
                // reader.GetBoolean(column) (numeric): 't' -> false.
                heavy: r.bool("heavy")?,
                asset2_id: r.u32("asset2_id")?,
                normal_speciality: r.bool("normal_specialty")?,
                use_as_stat: r.bool("use_as_stat")?,
                skin_kind_id: r.u32("skin_kind_id")?,
            };
            backpacks.push((id, b));
            tmp.push(ItemTemplate::new(
                id,
                TemplateKind::Backpack {
                    backpack_type: b.backpack_type,
                },
            ));
            Ok(())
        })?;
        // TODO (C#): HACK-FIX FOR CREST INK/STAMP/MUSIC
        tmp.push(ItemTemplate::new(ITEM_CREST_INK, TemplateKind::Ucc));
        tmp.push(ItemTemplate::new(ITEM_CREST_STAMP, TemplateKind::Ucc));
        tmp.push(ItemTemplate::new(
            ITEM_SHEET_MUSIC,
            TemplateKind::MusicSheet,
        ));
        tmp.push(ItemTemplate::new(
            ITEM_TREASURE_MAP_WITH_COORDINATES,
            TemplateKind::TreasureMap,
        ));
        for t in tmp {
            let id = t.id;
            data.template_order.push(id);
            if data.templates.insert(id, t).is_some() {
                return Err(load_err(format!(
                    "duplicate item template {id} (Dictionary.Add)"
                )));
            }
        }
        // The class-specific tables keep the last row of an item id, like the template objects.
        for b in body_parts {
            data.body_parts.insert(b.item_id, b);
        }
        for (id, r) in runes {
            data.runes.insert(id, r);
        }
        for (id, b) in backpacks {
            data.backpacks.insert(id, b);
        }

        // Templates the `items` table names (the others keep a null Name -> "invalid_item_").
        let mut named = std::collections::HashSet::new();
        for_each_row(conn, "SELECT * FROM items", [], |r| {
            let id = r.u32("id")?;
            named.insert(id);
            if !data.templates.contains_key(&id) {
                data.template_order.push(id);
            }
            let t = data
                .templates
                .entry(id)
                .or_insert_with(|| ItemTemplate::new(id, TemplateKind::Item));
            t.name = r.string_or("name", "")?;
            t.category_id = r.i32("category_id")?;
            t.level = r.i32("level")?;
            t.price = r.i32("price")?;
            t.refund = r.i32("refund")?;
            t.bind_type = r.u32("bind_id")?;
            t.pickup_limit = r.i32("pickup_limit")?;
            t.max_count = r.i32("max_stack_size")?;
            t.sellable = r.bool_str("sellable")?;
            t.use_skill_id = r.u32("use_skill_id")?;
            t.use_skill_as_reagent = r.bool_str("use_skill_as_reagent")?;
            t.impl_id = r.i32("impl_id")?;
            t.buff_id = r.u32("buff_id")?;
            t.gradable = r.bool_str("gradable")?;
            t.loot_multi = r.bool_str("loot_multi")?;
            t.loot_quest_id = r.u32("loot_quest_id")?;
            t.honor_price = r.i32("honor_price")?;
            t.exp_abs_lifetime = r.i32("exp_abs_lifetime")?;
            t.exp_online_lifetime = r.i32("exp_online_lifetime")?;
            t.exp_date = if r.is_null("exp_date")? {
                None
            } else {
                parse_sqlite_datetime(&r.string("exp_date")?)
            };
            t.specialty_zone_id = if r.is_null("specialty_zone_id")? {
                0
            } else {
                r.u32("specialty_zone_id")?
            };
            t.level_requirement = r.i32("level_requirement")?;
            t.auction_category_a = r.i32_or("auction_a_category_id", 0)?;
            t.auction_category_b = r.i32_or("auction_b_category_id", 0)?;
            t.auction_category_c = r.i32_or("auction_c_category_id", 0)?;
            t.level_limit = r.i32("level_limit")?;
            t.fixed_grade = r.i32("fixed_grade")?;
            t.disenchantable = r.bool_str("disenchantable")?;
            t.living_point_price = r.i32("living_point_price")?;
            t.char_gender = r.u8("char_gender_id")?;
            Ok(())
        })?;

        for_each_row(conn, "SELECT * FROM equip_slot_enchanting_costs", [], |r| {
            let c = EquipSlotEnchantingCost {
                id: r.u32("id")?,
                slot_type_id: r.u32("slot_type_id")?,
                cost: r.i32("cost")?,
            };
            data.enchanting_costs.entry(c.slot_type_id).or_insert(c);
            Ok(())
        })?;

        for_each_row(
            conn,
            "SELECT * FROM item_grade_enchanting_supports",
            [],
            |r| {
                let s = ItemGradeEnchantingSupport {
                    id: r.u32("id")?,
                    item_id: r.u32("item_id")?,
                    require_grade_min: r.i32("require_grade_min")?,
                    require_grade_max: r.i32("require_grade_max")?,
                    add_success_ratio: r.i32("add_success_ratio")?,
                    add_success_mul: r.i32("add_success_mul")?,
                    add_great_success_ratio: r.i32("add_great_success_ratio")?,
                    add_great_success_mul: r.i32("add_great_success_mul")?,
                    add_break_ratio: r.i32("add_break_ratio")?,
                    add_break_mul: r.i32("add_break_mul")?,
                    add_downgrade_ratio: r.i32("add_downgrade_ratio")?,
                    add_downgrade_mul: r.i32("add_downgrade_mul")?,
                    add_great_success_grade: r.i32("add_great_success_grade")?,
                };
                data.enchanting_supports.entry(s.item_id).or_insert(s);
                Ok(())
            },
        )?;

        for_each_row(conn, "SELECT * FROM item_socket_chances", [], |r| {
            let n = r.u32("num_sockets")?;
            let chance = r.u32("success_ratio")?;
            data.socket_chance.entry(n).or_insert(chance);
            Ok(())
        })?;

        for_each_row(conn, "SELECT * FROM item_cap_scales", [], |r| {
            let c = ItemCapScale {
                id: r.u32("id")?,
                skill_id: r.u32("skill_id")?,
                scale_min: r.i32("scale_min")?,
                scale_max: r.i32("scale_max")?,
            };
            data.item_cap_scales.entry(c.skill_id).or_insert(c);
            Ok(())
        })?;

        for_each_row(conn, "SELECT * FROM item_grade_distributions", [], |r| {
            let mut weights = [0i32; 12];
            for (i, w) in weights.iter_mut().enumerate() {
                *w = r.i32(&format!("weight_{i}"))?;
            }
            let d = GradeDistributions {
                id: r.i32("id")?,
                name: r.string("name")?,
                weights,
            };
            if data.grade_distributions.insert(d.id, d).is_some() {
                return Err(load_err(
                    "duplicate grade distribution (Dictionary.Add)".into(),
                ));
            }
            Ok(())
        })?;

        for_each_row(conn, "SELECT * FROM loot_pack_dropping_npcs", [], |r| {
            let t = LootPackDroppingNpc {
                id: r.u32("id")?,
                npc_id: r.u32("npc_id")?,
                loot_pack_id: r.u32("loot_pack_id")?,
                // reader.GetBoolean("default_pack") (numeric): 't' -> false.
                default_pack: r.bool("default_pack")?,
            };
            data.loot_pack_dropping_npc
                .entry(t.npc_id)
                .or_default()
                .push(t);
            Ok(())
        })?;

        for_each_row(
            conn,
            "SELECT * FROM doodad_func_convert_fish_items",
            [],
            |r| {
                let t = LootPackConvertFish {
                    id: r.u32("id")?,
                    item_id: r.u32("item_id")?,
                    loot_pack_id: r.u32("loot_pack_id")?,
                    doodad_func_convert_fish_id: r.u32("doodad_func_convert_fish_id")?,
                };
                data.loot_pack_convert_fish
                    .entry(t.item_id)
                    .or_default()
                    .push(t);
                Ok(())
            },
        )?;

        for_each_row(conn, "SELECT * FROM item_spawn_doodads", [], |r| {
            let doodad_id = r.u32("doodad_id")?;
            let item_id = r.u32("item_id")?;
            data.item_doodads
                .entry(doodad_id)
                .or_default()
                .push(item_id);
            Ok(())
        })?;

        for_each_row(
            conn,
            "SELECT * FROM unit_modifiers WHERE owner_type='Item'",
            [],
            |r| {
                let item_id = r.u32("owner_id")?;
                let raw = r.u8("unit_attribute_id")?;
                let Some(attribute) = UnitAttribute::from_u8(raw) else {
                    // Deviation: the C# keeps the raw enum value; our UnitAttribute cannot.
                    tracing::warn!("unit_modifiers: item {item_id} has unknown attribute {raw}");
                    return Ok(());
                };
                data.item_unit_modifiers
                    .entry(item_id)
                    .or_default()
                    .push(BonusTemplate {
                        attribute,
                        modifier_type: UnitModifierType::from_u8(r.u8("unit_modifier_type_id")?),
                        value: r.i32("value")?,
                        linear_level_bonus: r.i32("linear_level_bonus")?,
                    });
                Ok(())
            },
        )?;

        for_each_row(conn, "SELECT * FROM armor_grade_buffs", [], |r| {
            let b = ArmorGradeBuff {
                id: r.u8("id")?,
                armor_type: r.u32("armor_type_id")?,
                item_grade: r.u32("item_grade_id")?,
                buff_id: r.u32("buff_id")?,
            };
            data.armor_grade_buffs
                .entry(b.armor_type)
                .or_default()
                .entry(b.item_grade)
                .or_insert(b);
            Ok(())
        })?;

        for_each_row(conn, "SELECT * FROM item_sets", [], |r| {
            let s = ItemSet {
                id: r.u32("id")?,
                kind_id: r.u32("kind_id")?,
                name: r.string("name")?,
                items: Vec::new(),
            };
            match data.item_sets.entry(s.id) {
                std::collections::hash_map::Entry::Occupied(_) => {
                    tracing::warn!("Duplicate entry for item_sets {}", s.id);
                }
                std::collections::hash_map::Entry::Vacant(v) => {
                    v.insert(s);
                }
            }
            Ok(())
        })?;

        for_each_row(conn, "SELECT * FROM item_set_items", [], |r| {
            let e = ItemSetItem {
                id: r.u32("id")?,
                item_set_id: r.u32("item_set_id")?,
                item_id: r.u32("item_id")?,
                count: r.i32("count")?,
            };
            match data.item_sets.get_mut(&e.item_set_id) {
                Some(set) => {
                    if !set.items.iter().any(|i| i.id == e.id) {
                        set.items.push(e);
                    }
                }
                None => tracing::warn!("Missing item set entry for item_set_items {}", e.id),
            }
            Ok(())
        })?;

        // Naming missing items names (has other templates, but not in items).
        let mut invalid = 0;
        for t in data.templates.values_mut() {
            if !named.contains(&t.id) {
                invalid += 1;
                t.name = format!("invalid_item_{}", t.id);
            }
        }
        tracing::info!(
            "Loaded {} item templates (with {invalid} unused) ...",
            data.templates.len()
        );
        Ok(data)
    }
}

/// `SqliteDataReader.GetDateTime` for the text dates used in compact.sqlite3.
pub fn parse_sqlite_datetime(s: &str) -> Option<i64> {
    let s = s.trim();
    for fmt in [
        "%Y-%m-%d %H:%M:%S%.f",
        "%Y-%m-%d %H:%M:%S",
        "%Y-%m-%dT%H:%M:%S",
    ] {
        if let Ok(dt) = chrono::NaiveDateTime::parse_from_str(s, fmt) {
            return Some(dt.and_utc().timestamp());
        }
    }
    chrono::NaiveDate::parse_from_str(s, "%Y-%m-%d")
        .ok()
        .and_then(|d| d.and_hms_opt(0, 0, 0))
        .map(|d| d.and_utc().timestamp())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn armor_durability_formula() {
        let mut d = ItemData::default();
        d.config.durability_const = 1.0;
        d.grades.insert(
            0,
            GradeTemplate {
                grade: 0,
                grade_order: 0,
                durability: 1.0,
            },
        );
        let t = ItemTemplate::new(
            1,
            TemplateKind::Armor(EquipTemplate {
                coverage: 10,
                durability_ratio: 1.0,
                durability_multiplier: 100,
                ..EquipTemplate::default()
            }),
        );
        // (int)(1000 * 1000 * 1 * 1000 * 1e-8) = 10; *1 -> 10; round(10 * 100 * 0.01) = 10
        assert_eq!(d.max_durability(&t, 0), Some(10));
        assert_eq!(d.max_durability(&t, 1), None);
    }
}
