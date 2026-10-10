//! Game data of the store / specialty / taxation / expert / item-look ports:
//! `SpecialtyManager.Load` (`specialties`, `specialty_bundle_items`, `specialty_npcs`),
//! `TaxationsManager.Load` (`taxations`), `CharacterManager._expandExpertLimits`
//! (`expand_expert_limits`), the `ItemManager` look-convert tables
//! (`item_look_convert_required_items` / `_holdables` / `_wearables`), the
//! `GradeTemplate.RefundMultiplier` of `item_grades` and the specialty pack list in
//! `ItemManager.GetAllItems()` order.

use std::collections::{HashMap, HashSet};

use crate::data::sqlite::for_each_row;
use crate::data::GameDataError;
use crate::systems::DataLoadCtx;

/// `Specialty`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct Specialty {
    pub id: u32,
    pub row_zone_group_id: u32,
    pub col_zone_group_id: u32,
    pub ratio: u32,
    pub profit: u32,
    pub vendor_exist: bool,
}

/// `SpecialtyBundleItem` (`Item` = the template of `item_id`, looked up when needed).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct SpecialtyBundleItem {
    pub id: u32,
    pub item_id: u32,
    pub specialty_bundle_id: u32,
    pub profit: u32,
    pub ratio: u32,
}

/// `SpecialtyNpc`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct SpecialtyNpc {
    pub id: u32,
    pub name: String,
    pub npc_id: u32,
    pub specialty_bundle_id: u32,
}

/// `Taxation`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct Taxation {
    pub id: u32,
    pub tax: u32,
    pub show: bool,
}

/// `ExpandExpertLimit`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct ExpandExpertLimit {
    pub id: u32,
    pub expand_count: u8,
    pub life_point: i32,
    pub item_id: u32,
    pub item_count: i32,
}

/// `ItemLookConvert`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct ItemLookConvert {
    pub id: u32,
    pub slot_id: u32,
    pub required_item_id: u32,
    pub required_item_count: i32,
}

/// The tables of this file (`server.game_data.systems.economy.store`).
#[derive(Debug, Default)]
pub struct StoreData {
    /// `GradeTemplate.RefundMultiplier` by grade.
    pub refund_multipliers: HashMap<i32, i32>,
    /// `SpecialtyManager._specialties`.
    pub specialties: HashMap<u32, Specialty>,
    /// `SpecialtyManager._specialtyBundleItems`.
    pub specialty_bundle_items: HashMap<u32, SpecialtyBundleItem>,
    /// `SpecialtyManager._specialtyNpc` (by NPC template id).
    pub specialty_npcs: HashMap<u32, SpecialtyNpc>,
    /// `SpecialtyManager._specialtyBundleItemsMapped` (item id -> bundle id -> bundle item).
    pub specialty_bundle_items_mapped: HashMap<u32, HashMap<u32, SpecialtyBundleItem>>,
    /// `(template id, SpecialtyZoneId)` of every item template with a `SpecialtyZoneId`, in
    /// `ItemManager.GetAllItems()` order (typed template tables first, then `items`).
    pub specialty_packs: Vec<(u32, u32)>,
    /// `TaxationsManager.Taxations`.
    pub taxations: HashMap<u32, Taxation>,
    /// `CharacterManager._expandExpertLimits` (step = index, `ORDER BY expand_count`).
    pub expand_expert_limits: Vec<ExpandExpertLimit>,
    /// `ItemManager._itemLookConverts`.
    pub item_look_converts: HashMap<u32, ItemLookConvert>,
    /// `ItemManager._holdableItemLookConverts` (holdable id -> convert id).
    pub holdable_item_look_converts: HashMap<u32, u32>,
    /// `ItemManager._wearableItemLookConverts` (wearable slot id -> convert id).
    pub wearable_item_look_converts: HashMap<u32, u32>,
    /// `item_weapons.holdable_id` by item id (`WeaponTemplate.HoldableTemplate.Id`).
    pub weapon_holdables: HashMap<u32, u32>,
    /// `item_armors.slot_type_id` by item id.
    pub armor_slot_types: HashMap<u32, u32>,
}

/// The typed template tables of `ItemManager.Load`, in load order.
const TEMPLATE_TABLES: [&str; 8] = [
    "SELECT item_id FROM item_armors",
    "SELECT item_id FROM item_weapons",
    "SELECT item_id FROM item_accessories",
    "SELECT item_id FROM item_summon_mates",
    "SELECT item_id FROM item_summon_slaves",
    "SELECT item_id FROM item_body_parts ORDER BY id",
    "SELECT item_id FROM item_enchanting_gems",
    "SELECT item_id FROM item_backpacks",
];

/// `Item.CrestInk`, `CrestStamp`, `SheetMusic`, `TreasureMapWithCoordinates` (the hack-fix
/// templates added after the typed tables).
const HACK_TEMPLATES: [u32; 4] = [17663, 17662, 28051, 24581];

impl StoreData {
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, GameDataError> {
        let mut d = Self::default();
        let c = ctx.conn;
        for_each_row(c, "SELECT * FROM item_grades", [], |r| {
            d.refund_multipliers
                .insert(r.i32("id")?, r.i32("refund_multiplier")?);
            Ok(())
        })?;
        for_each_row(c, "SELECT * FROM specialties", [], |r| {
            let s = Specialty {
                id: r.u32("id")?,
                row_zone_group_id: r.u32("row_zone_group_id")?,
                col_zone_group_id: r.u32("col_zone_group_id")?,
                ratio: r.u32("ratio")?,
                profit: r.u32("profit")?,
                // C# reads `GetBoolean("id", true)`: only the row with id 1 is "t"/"1".
                vendor_exist: r.bool_str("id")?,
            };
            d.specialties.insert(s.id, s);
            Ok(())
        })?;
        for_each_row(c, "SELECT * FROM specialty_bundle_items", [], |r| {
            let b = SpecialtyBundleItem {
                id: r.u32("id")?,
                item_id: r.u32("item_id")?,
                specialty_bundle_id: r.u32("specialty_bundle_id")?,
                profit: r.u32("profit")?,
                ratio: r.u32("ratio")?,
            };
            d.specialty_bundle_items.insert(b.id, b);
            d.specialty_bundle_items_mapped
                .entry(b.item_id)
                .or_default()
                .insert(b.specialty_bundle_id, b);
            Ok(())
        })?;
        for_each_row(c, "SELECT * FROM specialty_npcs", [], |r| {
            let n = SpecialtyNpc {
                id: r.u32("id")?,
                name: r.string("name")?,
                npc_id: r.u32("npc_id")?,
                specialty_bundle_id: r.u32("specialty_bundle_id")?,
            };
            d.specialty_npcs.insert(n.npc_id, n);
            Ok(())
        })?;
        for_each_row(c, "SELECT * FROM taxations", [], |r| {
            let t = Taxation {
                id: r.u32("id")?,
                tax: r.u32("tax")?,
                show: r.bool_str("show")?,
            };
            d.taxations.insert(t.id, t);
            Ok(())
        })?;
        for_each_row(
            c,
            "SELECT * FROM expand_expert_limits ORDER BY expand_count ASC",
            [],
            |r| {
                d.expand_expert_limits.push(ExpandExpertLimit {
                    id: r.u32("id")?,
                    expand_count: r.u8("expand_count")?,
                    life_point: r.i32("life_point")?,
                    item_id: r.u32_or("item_id", 0)?,
                    item_count: r.i32("item_count")?,
                });
                Ok(())
            },
        )?;
        for_each_row(
            c,
            "SELECT * FROM item_look_convert_required_items",
            [],
            |r| {
                let l = ItemLookConvert {
                    id: r.u32("item_look_convert_id")?,
                    slot_id: 0,
                    required_item_id: r.u32("item_id")?,
                    required_item_count: r.i32("item_count")?,
                };
                d.item_look_converts.entry(l.id).or_insert(l);
                Ok(())
            },
        )?;
        for_each_row(c, "SELECT * FROM item_look_convert_holdables", [], |r| {
            let convert = r.u32("item_look_convert_id")?;
            d.holdable_item_look_converts
                .entry(r.u32("holdable_id")?)
                .or_insert(convert);
            Ok(())
        })?;
        for_each_row(c, "SELECT * FROM item_look_convert_wearables", [], |r| {
            let convert = r.u32("item_look_convert_id")?;
            d.wearable_item_look_converts
                .entry(r.u32("wearable_slot_id")?)
                .or_insert(convert);
            Ok(())
        })?;
        for_each_row(c, "SELECT * FROM item_weapons", [], |r| {
            d.weapon_holdables
                .insert(r.u32("item_id")?, r.u32("holdable_id")?);
            Ok(())
        })?;
        for_each_row(c, "SELECT * FROM item_armors", [], |r| {
            d.armor_slot_types
                .insert(r.u32("item_id")?, r.u32("slot_type_id")?);
            Ok(())
        })?;

        // ItemManager.GetAllItems() order of the templates with a SpecialtyZoneId.
        let mut zones: HashMap<u32, u32> = HashMap::new();
        let mut items_order = Vec::new();
        for_each_row(c, "SELECT * FROM items", [], |r| {
            let id = r.u32("id")?;
            let zone = r.u32_or("specialty_zone_id", 0)?;
            zones.insert(id, zone);
            items_order.push(id);
            Ok(())
        })?;
        let mut seen = HashSet::new();
        let mut order = Vec::new();
        for sql in TEMPLATE_TABLES {
            for_each_row(c, sql, [], |r| {
                if r.is_null("item_id")? {
                    return Ok(()); // item_body_parts skips rows without an item id
                }
                let id = r.u32("item_id")?;
                if seen.insert(id) {
                    order.push(id);
                }
                Ok(())
            })?;
        }
        for id in HACK_TEMPLATES.into_iter().chain(items_order) {
            if seen.insert(id) {
                order.push(id);
            }
        }
        d.specialty_packs = order
            .into_iter()
            .filter_map(|id| zones.get(&id).filter(|z| **z != 0).map(|z| (id, *z)))
            .collect();
        Ok(d)
    }

    /// `GradeTemplate.RefundMultiplier` (`None` where the C# dereferences a null template).
    pub fn refund_multiplier(&self, grade: u8) -> Option<i32> {
        self.refund_multipliers.get(&i32::from(grade)).copied()
    }

    /// `CharacterManager.GetExpandExpertLimit(step)`.
    pub fn expand_expert_limit(&self, step: u8) -> Option<&ExpandExpertLimit> {
        self.expand_expert_limits.get(usize::from(step))
    }

    /// `EquipItemTemplate.ItemLookConvert` of an armor / weapon template (`None` for
    /// accessories and items without a convert entry).
    pub fn item_look_convert(&self, template_id: u32) -> Option<&ItemLookConvert> {
        let convert = if let Some(slot) = self.armor_slot_types.get(&template_id) {
            self.wearable_item_look_converts.get(slot)
        } else if let Some(holdable) = self.weapon_holdables.get(&template_id) {
            self.holdable_item_look_converts.get(holdable)
        } else {
            None
        }?;
        self.item_look_converts.get(convert)
    }
}
