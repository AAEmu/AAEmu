//! The part of `ItemManager.Load` (item templates) that item creation, durability and the wire
//! format need: `item_configs`, `item_grades`, `holdables`, `wearables`, `wearable_kinds`,
//! `wearable_slots`, `dyeable_items`, the class tables (`item_armors`, `item_weapons`,
//! `item_accessories`, `item_summon_mates`, `item_summon_slaves`, `item_body_parts`,
//! `item_enchanting_gems`, `item_backpacks`), the hard-coded UCC / music / treasure templates and
//! the base `items` table. Loot, enchanting, sets, procs, look conversion and stat modifiers are
//! later phases.

use std::collections::HashMap;

use rusqlite::Connection;

use super::sqlite::for_each_row;
use crate::models::item::ItemClass;

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

/// `BackpackType` values `ItemContainer.CanAccept` allows in player containers.
pub mod backpack_type {
    pub const GLIDER: u32 = 2;
    pub const TOY_FLAG: u32 = 7;
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

/// `GradeTemplate` (only the columns this phase uses).
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct GradeTemplate {
    pub grade: i32,
    pub grade_order: i32,
    pub durability: f32,
}

/// `Holdable` (subset).
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct Holdable {
    pub id: u32,
    pub kind_id: u32,
    pub slot_type_id: u32,
    pub durability_ratio: f32,
}

/// `Wearable`.
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct Wearable {
    pub type_id: u32,
    pub slot_type_id: u32,
    pub armor_bp: i32,
    pub magic_resistance_bp: i32,
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

/// `EquipItemTemplate` + the armor/weapon/accessory specifics used for durability and slots.
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
}

/// `EquipmentItemSlotType.Backpack`.
pub const EQUIPMENT_ITEM_SLOT_TYPE_BACKPACK: u32 = 30;

/// Item game data.
#[derive(Debug, Clone, Default)]
pub struct ItemData {
    pub config: ItemConfig,
    pub grades: HashMap<i32, GradeTemplate>,
    pub templates: HashMap<u32, ItemTemplate>,
}

impl ItemData {
    pub fn template(&self, id: u32) -> Option<&ItemTemplate> {
        self.templates.get(&id)
    }

    /// `GetGradeTemplate`.
    pub fn grade(&self, grade: i32) -> Option<&GradeTemplate> {
        self.grades.get(&grade)
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

    /// `ItemManager.Load` (template part). As in C#, an empty `item_configs` table aborts the
    /// whole load (`if (!reader.Read()) return;`) and leaves every table empty.
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

        let mut grades_ordered = HashMap::new();
        for_each_row(conn, "SELECT * FROM item_grades", [], |r| {
            let g = GradeTemplate {
                grade: r.i32("id")?,
                grade_order: r.i32("grade_order")?,
                durability: r.f32("durability_value")?,
            };
            data.grades.insert(g.grade, g);
            grades_ordered.insert(g.grade_order, g);
            Ok(())
        })?;

        let mut holdables = HashMap::new();
        for_each_row(conn, "SELECT * FROM holdables", [], |r| {
            let h = Holdable {
                id: r.u32("id")?,
                kind_id: r.u32("kind_id")?,
                slot_type_id: r.u32("slot_type_id")?,
                durability_ratio: r.f32("durability_ratio")?,
            };
            holdables.insert(h.id, h);
            Ok(())
        })?;

        let mut wearables = HashMap::new();
        for_each_row(conn, "SELECT * FROM wearables", [], |r| {
            let w = Wearable {
                type_id: r.u32("armor_type_id")?,
                slot_type_id: r.u32("slot_type_id")?,
                armor_bp: r.i32("armor_bp")?,
                magic_resistance_bp: r.i32("magic_resistance_bp")?,
            };
            wearables.insert(w.type_id * 128 + w.slot_type_id, w);
            Ok(())
        })?;

        let mut kind_ratio = HashMap::new();
        for_each_row(conn, "SELECT * FROM wearable_kinds", [], |r| {
            kind_ratio.insert(r.u32("armor_type_id")?, r.f32("durability_ratio")?);
            Ok(())
        })?;

        let mut slot_coverage = HashMap::new();
        for_each_row(conn, "SELECT * FROM wearable_slots", [], |r| {
            slot_coverage.insert(r.u32("slot_type_id")?, r.i32("coverage")?);
            Ok(())
        })?;

        let mut default_dyes = HashMap::new();
        for_each_row(conn, "SELECT * FROM dyeable_items", [], |r| {
            default_dyes.insert(r.u32("item_id")?, r.u32("default_dyeing_item_id")?);
            Ok(())
        })?;

        let missing = |what: &str, k: u32| {
            rusqlite::Error::InvalidParameterName(format!("{what} {k} missing (KeyNotFound)"))
        };
        let mut add = |t: ItemTemplate| -> rusqlite::Result<()> {
            if data.templates.insert(t.id, t).is_some() {
                return Err(rusqlite::Error::InvalidParameterName(
                    "duplicate item template (Dictionary.Add)".into(),
                ));
            }
            Ok(())
        };

        let mut tmp = Vec::new();
        for_each_row(conn, "SELECT * FROM item_armors", [], |r| {
            let id = r.u32("item_id")?;
            let slot = r.u32("slot_type_id")?;
            let ty = r.u32("type_id")?;
            let w = wearables
                .get(&(ty * 128 + slot))
                .ok_or_else(|| missing("wearable", ty * 128 + slot))?;
            tmp.push(ItemTemplate::new(
                id,
                TemplateKind::Armor(EquipTemplate {
                    slot_type_id: w.slot_type_id,
                    coverage: *slot_coverage
                        .get(&slot)
                        .ok_or_else(|| missing("wearable_slot", slot))?,
                    durability_ratio: *kind_ratio
                        .get(&ty)
                        .ok_or_else(|| missing("wearable_kind", ty))?,
                    durability_multiplier: r.i32("durability_multiplier")?,
                    charge_lifetime: r.i32_or("charge_lifetime", 0)?,
                    charge_count: r.i32_or("charge_count", 0)?,
                    default_dye_item_id: default_dyes.get(&id).copied().unwrap_or(0),
                }),
            ));
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM item_weapons", [], |r| {
            let hid = r.u32("holdable_id")?;
            let h = holdables
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
                }),
            ));
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM item_accessories", [], |r| {
            let slot = r.u32("slot_type_id")?;
            let ty = r.u32("type_id")?;
            let w = wearables
                .get(&(ty * 128 + slot))
                .ok_or_else(|| missing("wearable", ty * 128 + slot))?;
            tmp.push(ItemTemplate::new(
                r.u32("item_id")?,
                TemplateKind::Accessory(EquipTemplate {
                    slot_type_id: w.slot_type_id,
                    coverage: *slot_coverage
                        .get(&slot)
                        .ok_or_else(|| missing("wearable_slot", slot))?,
                    durability_ratio: *kind_ratio
                        .get(&ty)
                        .ok_or_else(|| missing("wearable_kind", ty))?,
                    durability_multiplier: r.i32("durability_multiplier")?,
                    charge_lifetime: r.i32_or("charge_lifetime", 0)?,
                    charge_count: r.i32_or("charge_count", 0)?,
                    default_dye_item_id: 0,
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
        for_each_row(conn, "SELECT * FROM item_body_parts ORDER BY id", [], |r| {
            if r.is_null("item_id")? {
                return Ok(());
            }
            tmp.push(ItemTemplate::new(
                r.u32("item_id")?,
                TemplateKind::BodyPart {
                    slot_type_id: r.u32("slot_type_id")?,
                    model_id: r.u32("model_id")?,
                },
            ));
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM item_enchanting_gems", [], |r| {
            tmp.push(ItemTemplate::new(r.u32("item_id")?, TemplateKind::Rune));
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM item_backpacks", [], |r| {
            tmp.push(ItemTemplate::new(
                r.u32("item_id")?,
                TemplateKind::Backpack {
                    backpack_type: r.u32("backpack_type_id")?,
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
            add(t)?;
        }

        for_each_row(conn, "SELECT * FROM items", [], |r| {
            let id = r.u32("id")?;
            let t = data
                .templates
                .entry(id)
                .or_insert_with(|| ItemTemplate::new(id, TemplateKind::Item));
            t.name = r.string_or("name", "")?;
            t.bind_type = r.u32("bind_id")?;
            t.max_count = r.i32("max_stack_size")?;
            t.gradable = r.bool_str("gradable")?;
            t.exp_abs_lifetime = r.i32("exp_abs_lifetime")?;
            t.exp_online_lifetime = r.i32("exp_online_lifetime")?;
            t.exp_date = if r.is_null("exp_date")? {
                None
            } else {
                parse_sqlite_datetime(&r.string("exp_date")?)
            };
            t.fixed_grade = r.i32("fixed_grade")?;
            t.category_id = r.i32("category_id")?;
            t.level = r.i32("level")?;
            t.price = r.i32("price")?;
            t.refund = r.i32("refund")?;
            t.pickup_limit = r.i32("pickup_limit")?;
            t.sellable = r.bool_str("sellable")?;
            t.use_skill_id = r.u32("use_skill_id")?;
            t.use_skill_as_reagent = r.bool_str("use_skill_as_reagent")?;
            t.impl_id = r.i32("impl_id")?;
            t.buff_id = r.u32("buff_id")?;
            t.loot_multi = r.bool_str("loot_multi")?;
            t.loot_quest_id = r.u32("loot_quest_id")?;
            t.honor_price = r.i32("honor_price")?;
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
            t.disenchantable = r.bool_str("disenchantable")?;
            t.living_point_price = r.i32("living_point_price")?;
            t.char_gender = r.u8("char_gender_id")?;
            Ok(())
        })?;
        let _ = grades_ordered;
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
