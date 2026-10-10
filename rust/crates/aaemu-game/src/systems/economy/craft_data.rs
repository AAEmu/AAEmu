//! Game data of the crafting port: `CraftManager.Load` (`crafts`, `craft_products`,
//! `craft_materials`, `craft_pack_crafts`) and the `SkillTemplate` members `CharacterCraft` reads
//! for a craft's skill (`consume_lp`, `actability_group_id`, `cooldown_time`).

use std::collections::HashMap;

use crate::data::items::TemplateKind;
use crate::data::sqlite::for_each_row;
use crate::data::{GameDataError, ItemData};
use crate::systems::DataLoadCtx;

/// `CraftMaterial`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct CraftMaterial {
    pub id: u32,
    pub craft_id: u32,
    pub item_id: u32,
    pub amount: i32,
    pub main_grade: bool,
}

/// `CraftProduct`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct CraftProduct {
    pub id: u32,
    pub craft_id: u32,
    pub item_id: u32,
    pub amount: i32,
    pub rate: i32,
    pub show_lower_crafts: bool,
    pub use_grade: bool,
    pub item_grade_id: u32,
}

/// `Craft`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Craft {
    pub id: u32,
    pub cast_delay: i32,
    pub tool_id: u32,
    pub skill_id: u32,
    pub wi_id: u32,
    pub milestone_id: u32,
    pub req_doodad_id: u32,
    pub need_bind: bool,
    pub ac_id: u32,
    pub actability_limit: i32,
    pub show_upper_craft: bool,
    pub recommend_level: i32,
    pub visible_order: i32,
    pub craft_products: Vec<CraftProduct>,
    pub craft_materials: Vec<CraftMaterial>,
    pub is_pack: bool,
}

impl Craft {
    /// `Craft.ResultsInBackpack`.
    pub fn results_in_backpack(&self, items: &ItemData) -> bool {
        self.craft_products.iter().any(|p| {
            items
                .template(p.item_id)
                .is_some_and(|t| matches!(t.kind, TemplateKind::Backpack { .. }))
        })
    }
}

/// The `SkillTemplate` members of a craft skill (`consume_lp`, `actability_group_id`,
/// `cooldown_time`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct CraftSkill {
    pub id: u32,
    pub consume_labor_power: i32,
    pub actability_group_id: i32,
    pub cooldown_time: i32,
}

/// Crafting tables (`server.game_data.systems.economy.craft`).
#[derive(Debug, Default)]
pub struct CraftData {
    /// `CraftManager._crafts`.
    pub crafts: HashMap<u32, Craft>,
    /// Skill facts of every skill a craft uses (`SkillManager.GetSkillTemplate(craft.SkillId)`).
    pub skills: HashMap<u32, CraftSkill>,
}

impl CraftData {
    /// `CraftManager.Load` (+ the craft skills).
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, GameDataError> {
        let mut d = Self::default();
        for_each_row(ctx.conn, "SELECT * FROM crafts", [], |r| {
            let c = Craft {
                id: r.u32("id")?,
                cast_delay: r.i32("cast_delay")?,
                tool_id: r.u32_or("tool_id", 0)?,
                skill_id: r.u32_or("skill_id", 0)?,
                wi_id: r.u32("wi_id")?,
                milestone_id: r.u32_or("milestone_id", 0)?,
                req_doodad_id: r.u32_or("req_doodad_id", 0)?,
                // `GetBoolean(column)` = GetInt64 != 0: the 't' / 'f' text columns read false.
                need_bind: r.bool("need_bind")?,
                ac_id: r.u32_or("ac_id", 0)?,
                actability_limit: r.i32("actability_limit")?,
                show_upper_craft: r.bool("show_upper_crafts")?,
                recommend_level: r.i32("recommend_level")?,
                visible_order: r.i32("visible_order")?,
                ..Craft::default()
            };
            d.crafts.insert(c.id, c);
            Ok(())
        })?;
        for_each_row(ctx.conn, "SELECT * FROM craft_products", [], |r| {
            let craft_id = r.u32("craft_id")?;
            let Some(craft) = d.crafts.get_mut(&craft_id) else {
                return Ok(());
            };
            craft.craft_products.push(CraftProduct {
                id: r.u32("id")?,
                craft_id,
                item_id: r.u32("item_id")?,
                amount: r.i32_or("amount", 1)?, // We always want to produce at least 1 item ?
                rate: r.i32("rate")?,
                show_lower_crafts: r.bool("show_lower_crafts")?,
                use_grade: r.bool("use_grade")?,
                item_grade_id: r.u32("item_grade_id")?,
            });
            Ok(())
        })?;
        for_each_row(ctx.conn, "SELECT * FROM craft_materials", [], |r| {
            let craft_id = r.u32("craft_id")?;
            let Some(craft) = d.crafts.get_mut(&craft_id) else {
                return Ok(());
            };
            craft.craft_materials.push(CraftMaterial {
                id: r.u32("id")?,
                craft_id,
                item_id: r.u32("item_id")?,
                amount: r.i32_or("amount", 1)?, // We always want to cost at least 1 item ?
                main_grade: r.bool("main_grade")?,
            });
            Ok(())
        })?;
        for_each_row(ctx.conn, "SELECT * FROM craft_pack_crafts", [], |r| {
            if let Some(craft) = d.crafts.get_mut(&r.u32("craft_id")?) {
                craft.is_pack = true;
            }
            Ok(())
        })?;
        let wanted: std::collections::HashSet<u32> =
            d.crafts.values().map(|c| c.skill_id).collect();
        for_each_row(ctx.conn, "SELECT * FROM skills", [], |r| {
            let id = r.u32("id")?;
            if wanted.contains(&id) {
                d.skills.insert(
                    id,
                    CraftSkill {
                        id,
                        consume_labor_power: r.i32_or("consume_lp", 0)?,
                        actability_group_id: r.i32_or("actability_group_id", 0)?,
                        cooldown_time: r.i32("cooldown_time")?,
                    },
                );
            }
            Ok(())
        })?;
        Ok(d)
    }

    /// `CraftManager.GetCraftById` (`None` where the C# throws `KeyNotFoundException`).
    pub fn craft(&self, id: u32) -> Option<&Craft> {
        self.crafts.get(&id)
    }
}
