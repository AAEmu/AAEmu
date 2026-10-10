//! Item game data loaded next to `ItemManager.Load`: `GameData/ItemGameData` (`item_grade_buffs`),
//! `GameData/ItemConversionGameData` (`item_conv_*`), `GameData/FishDetailsGameData`
//! (`fish_details`), and the localized item names `ItemManager.Load` / `LocalizationManager` use
//! for the auction search (`ItemTemplate.searchString`, `ItemNameCache`,
//! `GetItemIdsBySearchName`, `GetItemTemplatesForAuctionSearch`, `MatchItemName`).

use std::collections::HashMap;
use std::sync::OnceLock;

use rusqlite::Connection;

use crate::data::items::{ItemData, ItemTemplate};
use crate::data::sqlite::for_each_row;
use crate::models::item::{Item, ItemClass};

/// `ItemConversionReagent`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct ItemConversionReagent {
    /// `ConversionSet` (never assigned: `_conversionSets` stays empty in C#).
    pub conversion_set: i32,
    pub conversion_id: u32,
    /// `ImplId` (`ItemImplEnum`).
    pub impl_id: i32,
    pub input_item_id: u32,
    pub min_level: i32,
    pub max_level: i32,
    pub min_item_grade: u8,
    pub max_item_grade: u8,
}

/// `ItemConversionProduct`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct ItemConversionProduct {
    pub conversion_id: u32,
    pub chance_rate: i32,
    pub output_item_id: u32,
    pub weight: i32,
    pub min_output: i32,
    pub max_output: i32,
}

/// `ItemConversionGameData`.
#[derive(Debug, Clone, Default)]
pub struct ItemConversionData {
    pub reagents: Vec<ItemConversionReagent>,
    pub products: Vec<ItemConversionProduct>,
    /// `_conversions` (`item_conv_sets.id` then `item_convs.item_conv_set_id`, NULL -> -1).
    pub conversions: Vec<i32>,
}

impl ItemConversionData {
    /// `GetReagentForItem(grade, implId, itemId, level)`.
    pub fn reagent_for_item(
        &self,
        grade: u8,
        impl_id: i32,
        item_id: u32,
        level: i32,
    ) -> Option<&ItemConversionReagent> {
        // figure out if there is a conversion for this specific item id first
        if let Some(r) = self.reagents.iter().find(|r| {
            item_id == r.input_item_id && grade >= r.min_item_grade && grade <= r.max_item_grade
        }) {
            return Some(r);
        }
        // Otherwise find a reagent depending on item grade
        self.reagents.iter().find(|r| {
            impl_id == r.impl_id
                && grade >= r.min_item_grade
                && grade <= r.max_item_grade
                && level >= r.min_level
                && level <= r.max_level
        })
    }

    /// `GetProductFromReagent(convReagent)`.
    pub fn product_from_reagent(
        &self,
        reagent: &ItemConversionReagent,
    ) -> Option<&ItemConversionProduct> {
        self.products
            .iter()
            .find(|p| p.conversion_id == reagent.conversion_id)
    }

    /// `IsValidConversionSet(opcode, reagent)`.
    pub fn is_valid_conversion_set(&self, opcode: i32, reagent: &ItemConversionReagent) -> bool {
        self.conversions.contains(&opcode) && reagent.conversion_id as i32 == opcode
    }
}

/// `FishDetails` (`fish_details`).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct FishDetails {
    pub id: i32,
    pub name: String,
    pub item_id: u32,
    pub min_weight: i32,
    pub max_weight: i32,
    pub min_length: i32,
    pub max_length: i32,
}

/// Item game data of the economy (`server.game_data.systems.economy.items`).
#[derive(Debug, Default)]
pub struct ItemsData {
    /// `ItemGameData._itemGradeBuffs` (item id -> grade -> buff id).
    pub item_grade_buffs: HashMap<u32, HashMap<u8, u32>>,
    /// `ItemConversionGameData`.
    pub conversion: ItemConversionData,
    /// `FishDetailsGameData._fishDetails` (by item id).
    pub fish_details: HashMap<u32, FishDetails>,
    /// `LocalizationManager.Get("items", "name", id)` in the default language (first row wins,
    /// as `AddTranslation`'s `TryAdd`).
    pub default_item_names: HashMap<u32, String>,
    /// `LocalizationManager.ItemNameCache`: every locale column (in table column order) with its
    /// lower-cased, non-blank item names (last row wins).
    pub item_name_cache: Vec<(String, HashMap<i64, String>)>,
    /// `ItemTemplate.searchString` by template id (built on first use, the templates live in
    /// `GameData::items`).
    search_strings: OnceLock<Vec<(u32, String)>>,
}

impl ItemsData {
    /// Loads the tables (see the module docs). `language` = `DefaultLanguage`.
    pub fn load(conn: &Connection, language: &str) -> rusqlite::Result<Self> {
        let mut d = ItemsData::default();

        // ItemGameData
        for_each_row(conn, "SELECT * FROM item_grade_buffs", [], |r| {
            let item_id = r.u32("item_id")?;
            let grade = r.u8("item_grade_id")?;
            let buff_id = r.u32("buff_id")?;
            if d.item_grade_buffs
                .entry(item_id)
                .or_default()
                .insert(grade, buff_id)
                .is_some()
            {
                return Err(rusqlite::Error::InvalidParameterName(format!(
                    "duplicate item_grade_buffs {item_id}/{grade} (Dictionary.Add)"
                )));
            }
            Ok(())
        })?;

        // ItemConversionGameData
        let c = &mut d.conversion;
        for_each_row(conn, "SELECT * FROM item_conv_reagent_filters", [], |r| {
            c.reagents.push(ItemConversionReagent {
                conversion_id: r.u32("item_conv_rpack_id")?,
                impl_id: r.i32("item_impl_id")?,
                min_level: r.i32("min_level")?,
                max_level: r.i32("max_level")?,
                min_item_grade: r.u8("item_grade_id")?,
                max_item_grade: r.u8("max_item_grade_id")?,
                ..Default::default()
            });
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM item_conv_reagents", [], |r| {
            c.reagents.push(ItemConversionReagent {
                conversion_id: r.u32("item_conv_rpack_id")?,
                input_item_id: r.u32("item_id")?,
                min_item_grade: r.u8("grade_id")?,
                max_item_grade: r.u8("max_grade_id")?,
                ..Default::default()
            });
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM item_conv_sets", [], |r| {
            c.conversions.push(r.i32("id")?);
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM item_convs", [], |r| {
            c.conversions.push(r.i32_or("item_conv_set_id", -1)?);
            Ok(())
        })?;
        for_each_row(
            conn,
            "SELECT base.id, base.item_conv_ppack_id, A.chance_rate, base.item_id, base.weight, base.min, base.max FROM item_conv_products AS base LEFT JOIN item_conv_ppacks AS A ON base.item_conv_ppack_id=A.id",
            [],
            |r| {
                c.products.push(ItemConversionProduct {
                    conversion_id: r.u32("item_conv_ppack_id")?,
                    chance_rate: r.i32_or("chance_rate", 0)?,
                    output_item_id: r.u32("item_id")?,
                    weight: r.i32("weight")?,
                    min_output: r.i32("min")?,
                    max_output: r.i32("max")?,
                });
                Ok(())
            },
        )?;

        // Localized names (LocalizationManager.Load, the parts the item code reads).
        let lang = language.replace('"', "");
        let mut fish_names: HashMap<i64, String> = HashMap::new();
        {
            let mut stmt = conn.prepare(
                "SELECT * FROM localized_texts WHERE (tbl_name = 'items' AND tbl_column_name = 'name') \
                 OR (tbl_name = 'fish_details' AND tbl_column_name = 'name')",
            )?;
            let names: Vec<String> = stmt.column_names().iter().map(|s| s.to_string()).collect();
            let locales: Vec<String> = names
                .iter()
                .filter(|c| {
                    !matches!(c.as_str(), "id" | "tbl_name" | "tbl_column_name" | "idx")
                        && !c.ends_with("_ver")
                })
                .cloned()
                .collect();
            d.item_name_cache = locales
                .iter()
                .map(|l| (l.clone(), HashMap::new()))
                .collect();
            let mut rows = stmt.query([])?;
            while let Some(row) = rows.next()? {
                let r = crate::data::sqlite::SqlRow::new(row, &names);
                let tbl = r.string("tbl_name")?;
                let idx = r.i64("idx")?;
                let text = r.string_or(&lang, "")?;
                if tbl == "fish_details" {
                    fish_names.entry(idx).or_insert(text);
                    continue;
                }
                d.default_item_names.entry(idx as u32).or_insert(text);
                for (locale, group) in d.item_name_cache.iter_mut() {
                    let name = r.string_or(locale, "")?.to_lowercase();
                    if name.trim().is_empty() {
                        continue;
                    }
                    group.insert(idx, name);
                }
            }
        }

        // FishDetailsGameData
        for_each_row(conn, "SELECT * FROM fish_details", [], |r| {
            let id = r.i32("id")?;
            let fallback = r.string("name")?;
            let name = match fish_names.get(&i64::from(id)) {
                Some(t) if !t.is_empty() => t.clone(),
                _ => fallback,
            };
            let t = FishDetails {
                id,
                name,
                item_id: r.u32("item_id")?,
                min_weight: r.i32("min_weight")?,
                max_weight: r.i32("max_weight")?,
                min_length: r.i32("min_length")?,
                max_length: r.i32("max_length")?,
            };
            d.fish_details.entry(t.item_id).or_insert(t);
            Ok(())
        })?;
        Ok(d)
    }

    /// `ItemGameData.GetItemBuff(itemId, gradeId)` (the buff id; the skills subsystem resolves
    /// the `BuffTemplate`).
    pub fn item_grade_buff(&self, item_id: u32, grade: u8) -> Option<u32> {
        self.item_grade_buffs.get(&item_id)?.get(&grade).copied()
    }

    /// `ItemTemplate.searchString` of every template, in `_templates` order.
    fn search_strings(&self, items: &ItemData) -> &[(u32, String)] {
        self.search_strings.get_or_init(|| {
            ordered_templates(items)
                .into_iter()
                .map(|t| {
                    let loc = self
                        .default_item_names
                        .get(&t.id)
                        .map_or("", String::as_str);
                    (t.id, format!("{} {}", t.name, loc).to_lowercase())
                })
                .collect()
        })
    }

    /// `ItemTemplate.searchString` of `template_id`.
    pub fn search_string(&self, items: &ItemData, template_id: u32) -> Option<&str> {
        self.search_strings(items)
            .iter()
            .find(|(id, _)| *id == template_id)
            .map(|(_, s)| s.as_str())
    }

    /// `ItemManager.GetItemIdsBySearchName(searchString)`: ids whose search string contains
    /// `search` (case-sensitive, as in C#: the keyword is not lower-cased).
    pub fn item_ids_by_search_name(&self, items: &ItemData, search: &str) -> Vec<u32> {
        self.search_strings(items)
            .iter()
            .filter(|(_, s)| s.contains(search))
            .map(|(id, _)| *id)
            .collect()
    }

    /// `ItemManager.GetItemTemplatesForAuctionSearch(searchTemplate)` with the `AuctionSearch`
    /// fields it reads (`Keyword`, `CategoryA/B/C`).
    pub fn item_templates_for_auction_search<'a>(
        &self,
        items: &'a ItemData,
        keyword: &str,
        category_a: u8,
        category_b: u8,
        category_c: u8,
    ) -> Vec<&'a ItemTemplate> {
        let matches = |t: &ItemTemplate| {
            (category_a == 0 || t.auction_category_a == i32::from(category_a))
                && (category_b == 0 || t.auction_category_b == i32::from(category_b))
                && (category_c == 0 || t.auction_category_c == i32::from(category_c))
        };
        let item_ids = if keyword.is_empty() {
            Vec::new()
        } else {
            self.item_ids_by_search_name(items, keyword)
        };
        let all = ordered_templates(items);
        if item_ids.is_empty() {
            return all.into_iter().filter(|t| matches(t)).collect();
        }
        let mut res = Vec::new();
        for id in item_ids {
            for t in &all {
                if (id == 0 || t.id == id) && matches(t) {
                    res.push(*t);
                }
            }
        }
        res
    }

    /// `LocalizationManager.MatchItemName(itemTemplateId, keywords, exactMatch)`.
    pub fn match_item_name(&self, template_id: u32, keywords: &str, exact_match: bool) -> bool {
        if keywords.trim().is_empty() {
            return false;
        }
        let k = keywords.to_lowercase();
        let words: Vec<&str> = k.split(' ').filter(|w| !w.trim().is_empty()).collect();
        for (_, names) in &self.item_name_cache {
            let Some(name) = names.get(&i64::from(template_id)) else {
                continue;
            };
            if name.trim().is_empty() {
                continue;
            }
            if exact_match {
                if *name == k {
                    return true;
                }
                continue;
            }
            if words.iter().all(|w| name.contains(w)) {
                return true;
            }
        }
        false
    }

    /// `FishDetailsGameData.GetFishSize(templateId)` with `length_roll` =
    /// `Random.Shared.Next(MinLength, MaxLength)`. `None` where the C# indexer throws.
    pub fn fish_size(&self, template_id: u32, length_roll: i32) -> Option<(f32, f32)> {
        let f = self.fish_details.get(&template_id)?;
        let length = length_roll as f32;
        // length / MaxLength: float / int
        let amount = length / f.max_length as f32;
        let weight = lerp(f.min_weight as f32, f.max_weight as f32, amount);
        Some((length, weight))
    }

    /// `FishDetailsGameData.Create(item)`: a new `BigFish(item.Id, template, 1)` with
    /// `CreateTime = now` and a random size rolled from the fish details of `item.MadeUnitId`
    /// (`Random.Shared.Next(MinLength, MaxLength)`). `None` when the template is unknown or
    /// where the C# indexer throws (no fish details).
    pub fn create_big_fish(
        &self,
        items: &ItemData,
        item: &Item,
        world_id: u8,
        rng: &mut dyn super::loot::LootRng,
        now: i64,
    ) -> Option<Item> {
        let template = items.template(item.template_id)?;
        let f = self.fish_details.get(&item.made_unit_id)?;
        let roll = rng.next_i32(f.min_length, f.max_length);
        let (length, weight) = self.fish_size(item.made_unit_id, roll)?;
        Some(Item {
            class: ItemClass::BigFish,
            id: item.id,
            template_id: template.id,
            slot: -1,
            grade: 0,
            flags: 0,
            count: 1,
            lifespan_mins: 0,
            made_unit_id: 0,
            world_id,
            create_time: now,
            unsecure_time: 0,
            unpack_time: 0,
            charge_start_time: 0,
            ucc_id: 0,
            details: crate::models::item::ItemDetails {
                fish_weight: weight,
                fish_length: length,
                ..Default::default()
            },
            bind_on_unpack: template.bind_type == crate::data::items::bind_type::BIND_ON_UNPACK,
            owner_id: 0,
            slot_type: 0,
            expiration_time: 0,
            expiration_online_minutes_left: 0.0,
            charge_count: 0,
            dirty: true,
        })
    }
}

fn lerp(v1: f32, v2: f32, t: f32) -> f32 {
    v1 + (v2 - v1) * t
}

/// The templates in `_templates` insertion order when known ([`ItemData::template_order`]),
/// otherwise by id.
pub fn ordered_templates(items: &ItemData) -> Vec<&ItemTemplate> {
    if items.template_order.len() == items.templates.len() {
        items
            .template_order
            .iter()
            .filter_map(|id| items.templates.get(id))
            .collect()
    } else {
        let mut v: Vec<_> = items.templates.values().collect();
        v.sort_by_key(|t| t.id);
        v
    }
}
