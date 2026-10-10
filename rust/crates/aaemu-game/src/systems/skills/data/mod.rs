//! Game-data tables of the skills subsystem (`server.game_data.systems.skills`): the full
//! `SkillManager.Load`, `PlotManager.Load`, `AnimationManager.Load`, `BuffGameData`, the
//! `unit_reqs`, `aoe_shapes`, actor model radii and npc tags.

pub mod effects;
pub mod misc;
pub mod plots;
pub mod templates;

#[cfg(test)]
mod buff_game_data_tests;
#[cfg(test)]
mod skill_manager_tests;

use std::collections::HashMap;
use std::sync::Arc;

use rusqlite::Connection;

use crate::data::sqlite::for_each_row;
use crate::models::unit::{
    BuffModifier, CombatBuffTemplate, DynamicBonusTemplate, LinearFuncTemplate, SkillAttribute,
    SkillModifier, UnitModifierType,
};
use crate::systems::DataLoadCtx;

pub use effects::{EffectKind, EffectTemplate, EffectType};
pub use misc::{AnimData, AreaShape, BuffGameData, BuffTolerance, UnitReq};
pub use plots::{PlotData, PlotEventTemplate, PlotNextEvent, PlotTree};
pub use templates::{
    BuffTemplate, SkillControllerTemplate, SkillEffect, SkillTemplate, TickEffect,
};

/// `BuffTriggerTemplate`.
#[derive(Debug, Clone, PartialEq)]
pub struct BuffTriggerTemplate {
    pub id: u32,
    /// `BuffEventTriggerKind` (raw `event_id`).
    pub kind: u16,
    pub effect_on_source: bool,
    /// `Effect` (resolved through `effects`).
    pub effect: (EffectKind, u32),
    pub use_damage_amount: bool,
    pub use_original_source: bool,
    pub target_buff_tag_id: u32,
    pub target_no_buff_tag_id: u32,
    pub synergy: bool,
}

/// `SkillReagent` / `SkillProduct`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SkillItemAmount {
    pub id: u32,
    pub skill_id: u32,
    pub item_id: u32,
    pub amount: i16,
}

/// The skills subsystem's tables.
#[derive(Debug, Default)]
pub struct Data {
    /// `_skills`.
    pub skills: HashMap<u32, Arc<SkillTemplate>>,
    /// `_buffs`.
    pub buffs: HashMap<u32, Arc<BuffTemplate>>,
    /// `_types` (effects table).
    pub effect_types: HashMap<u32, EffectType>,
    /// `_effects`.
    pub effects: HashMap<EffectKind, HashMap<u32, EffectTemplate>>,
    /// `_buffTags`: buff id -> tag ids.
    pub buff_tags: HashMap<u32, Vec<u32>>,
    /// `_taggedBuffs`: tag id -> buff ids.
    pub tagged_buffs: HashMap<u32, Vec<u32>>,
    /// `_skillTags`.
    pub skill_tags: HashMap<u32, Vec<u32>>,
    /// `_taggedSkills`.
    pub tagged_skills: HashMap<u32, Vec<u32>>,
    /// `_skillModifiers` by owner id.
    pub skill_modifiers: HashMap<u32, Vec<SkillModifier>>,
    /// `_buffTriggers` by buff id.
    pub buff_triggers: HashMap<u32, Vec<BuffTriggerTemplate>>,
    /// `_combatBuffs` by `ReqBuffId`.
    pub combat_buffs: HashMap<u32, Vec<CombatBuffTemplate>>,
    /// `_linearFuncs`.
    pub linear_funcs: HashMap<u32, LinearFuncTemplate>,
    /// `_skillReagents` (table order).
    pub skill_reagents: Vec<SkillItemAmount>,
    /// `_skillProducts` (table order).
    pub skill_products: Vec<SkillItemAmount>,
    /// `unit_modifiers WHERE owner_type='Item'` (`ItemManager.GetUnitModifiers`).
    pub item_unit_modifiers: HashMap<u32, Vec<crate::models::unit::BonusTemplate>>,
    pub plots: PlotData,
    pub anims: AnimData,
    pub area_shapes: HashMap<u32, AreaShape>,
    /// `ModelManager.GetActorModel(id).Radius`.
    pub model_radii: HashMap<u32, f32>,
    /// `UnitRequirementsGameData` by owner type.
    pub unit_reqs: HashMap<String, Vec<UnitReq>>,
    pub buff_game_data: BuffGameData,
    /// `TagsGameData` npc tags.
    pub npc_tags: HashMap<u32, Vec<u32>>,
    /// `ZoneGroup.BuffId`.
    pub zone_group_buffs: HashMap<u32, u32>,
}

fn dup(what: &str, id: u32) -> rusqlite::Error {
    rusqlite::Error::InvalidParameterName(format!("duplicate {what} {id} (Dictionary.Add)"))
}

fn missing(what: &str, id: u32) -> rusqlite::Error {
    rusqlite::Error::InvalidParameterName(format!("{what} {id} not found (KeyNotFound)"))
}

impl Data {
    /// Loads every table (`PlotManager` and `AnimationManager` first, as the C# skill loader
    /// resolves `Plot` / `FireAnim` while reading the skills).
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        Ok(Self::load_from(ctx.conn, ctx.files)?)
    }

    pub fn load_from(
        conn: &Connection,
        files: &crate::data::client_files::ClientFileManager,
    ) -> rusqlite::Result<Self> {
        let mut d = Data {
            plots: PlotData::load(conn)?,
            anims: AnimData::load(conn, files)?,
            ..Data::default()
        };
        // skills
        {
            let plots = &d.plots;
            let anims = &d.anims;
            let skills = templates::load_skills(conn, &|p| plots.plot(p).is_some(), &|a| {
                anims.get(a).is_some()
            })?;
            for s in skills {
                if d.skills.contains_key(&s.id) {
                    return Err(dup("skill", s.id));
                }
                d.skills.insert(s.id, Arc::new(s));
            }
        }
        // buffs (+ buff_effects / tick effects / unit modifiers / dynamic modifiers)
        let mut buffs: HashMap<u32, BuffTemplate> = HashMap::new();
        for b in templates::load_buffs(conn)? {
            if buffs.contains_key(&b.id) {
                return Err(dup("buff", b.id));
            }
            buffs.insert(b.id, b);
        }
        for_each_row(conn, "SELECT * FROM buff_tick_effects", [], |r| {
            let buff_id = r.u32("buff_id")?;
            let t = buffs
                .get_mut(&buff_id)
                .ok_or_else(|| missing("buff", buff_id))?;
            t.tick_effects.push(TickEffect {
                effect_id: r.u32("effect_id")?,
                target_buff_tag_id: r.u32_or("target_buff_tag_id", 0)?,
                target_no_buff_tag_id: r.u32_or("target_nobuff_tag_id", 0)?,
            });
            Ok(())
        })?;
        for_each_row(
            conn,
            "SELECT * FROM unit_modifiers WHERE owner_type='Buff'",
            [],
            |r| {
                let buff_id = r.u32("owner_id")?;
                if let Some(t) = buffs.get_mut(&buff_id) {
                    if let Some(b) = templates::read_bonus(r)? {
                        t.bonuses.push(b);
                    }
                }
                Ok(())
            },
        )?;
        for_each_row(conn, "SELECT * FROM linear_funcs", [], |r| {
            let f = LinearFuncTemplate {
                id: r.u32("id")?,
                start_value: r.i32("start_value")?,
                end_value: r.i32("end_value")?,
            };
            d.linear_funcs.insert(f.id, f);
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM dynamic_unit_modifiers", [], |r| {
            let buff_id = r.u32("buff_id")?;
            if let Some(t) = buffs.get_mut(&buff_id) {
                if let Some(attribute) = templates::unit_attribute(r.u8("unit_attribute_id")?) {
                    t.dynamic_bonuses.push(DynamicBonusTemplate {
                        attribute,
                        modifier_type: UnitModifierType::from_u8(r.u8("unit_modifier_type_id")?),
                        func_id: r.u32("func_id")?,
                        func_type: r.string("func_type")?,
                    });
                }
            }
            Ok(())
        })?;
        effects::load_effect_tables(conn, &|id| buffs.contains_key(&id), &mut d.effects)?;
        d.effect_types = effects::load_effect_types(conn)?;
        // skill_effects
        {
            let effect_types = &d.effect_types;
            let all_effects = &d.effects;
            let skills = &mut d.skills;
            for_each_row(conn, "SELECT * FROM skill_effects", [], |r| {
                let skill_id = r.u32("skill_id")?;
                if !skills.contains_key(&skill_id) {
                    return Ok(());
                }
                let effect_id = r.u32("effect_id")?;
                let ty = effect_types
                    .get(&effect_id)
                    .ok_or_else(|| missing("effect", effect_id))?;
                let template = match EffectKind::from_name(&ty.actual_type) {
                    Some(kind) => {
                        if !all_effects
                            .get(&kind)
                            .is_some_and(|m| m.contains_key(&ty.actual_id))
                        {
                            return Err(missing(
                                &format!("{} effect", ty.actual_type),
                                ty.actual_id,
                            ));
                        }
                        Some((kind, ty.actual_id))
                    }
                    None => None,
                };
                let e = SkillEffect {
                    effect_id,
                    template,
                    weight: r.i32("weight")?,
                    start_level: r.u8("start_level")?,
                    end_level: r.u8("end_level")?,
                    friendly: r.bool_str("friendly")?,
                    non_friendly: r.bool_str("non_friendly")?,
                    target_buff_tag_id: r.u32_or("target_buff_tag_id", 0)?,
                    target_no_buff_tag_id: r.u32_or("target_nobuff_tag_id", 0)?,
                    source_buff_tag_id: r.u32_or("source_buff_tag_id", 0)?,
                    source_no_buff_tag_id: r.u32_or("source_nobuff_tag_id", 0)?,
                    chance: r.i32("chance")?,
                    front: r.bool_str("front")?,
                    back: r.bool_str("back")?,
                    target_npc_tag_id: r.u32_or("target_npc_tag_id", 0)?,
                    application_method: r.u32("application_method_id")?,
                    consume_source_item: r.bool_str("consume_source_item")?,
                    consume_item_id: r.u32_or("consume_item_id", 0)?,
                    consume_item_count: r.i32("consume_item_count")?,
                    always_hit: r.bool_str("always_hit")?,
                    item_set_id: r.u32_or("item_set_id", 0)?,
                    interaction_success_hit: r.bool_str("interaction_success_hit")?,
                };
                if let Some(s) = skills.get_mut(&skill_id) {
                    Arc::make_mut(s).effects.push(e);
                }
                Ok(())
            })?;
        }
        for_each_row(conn, "SELECT * FROM tagged_buffs", [], |r| {
            let tag = r.u32("tag_id")?;
            let buff = r.u32("buff_id")?;
            d.buff_tags.entry(buff).or_default().push(tag);
            d.tagged_buffs.entry(tag).or_default().push(buff);
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM skill_modifiers", [], |r| {
            let attr = r.u32("skill_attribute_id")? as u8;
            let Some(skill_attribute) = SkillAttribute::from_u8(attr) else {
                tracing::warn!("skill_modifiers: unknown skill attribute {attr}");
                return Ok(());
            };
            let m = SkillModifier {
                id: r.u32("id")?,
                owner_id: r.u32("owner_id")?,
                tag_id: r.u32_or("tag_id", 0)?,
                skill_attribute,
                unit_modifier_type: UnitModifierType::from_u8(r.u32("unit_modifier_type_id")? as u8),
                value: r.i32("value")?,
                skill_id: r.u32_or("skill_id", 0)?,
                synergy: r.bool_str("synergy")?,
            };
            d.skill_modifiers.entry(m.owner_id).or_default().push(m);
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM tagged_skills", [], |r| {
            let tag = r.u32("tag_id")?;
            let skill = r.u32("skill_id")?;
            d.skill_tags.entry(skill).or_default().push(tag);
            d.tagged_skills.entry(tag).or_default().push(skill);
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM combat_buffs", [], |r| {
            let c = CombatBuffTemplate {
                id: r.u32("id")?,
                hit_skill_id: r.u32_or("hit_skill_id", 0)?,
                hit_type: r.u32("hit_type_id")? as u8,
                req_skill_id: r.u32_or("req_skill_id", 0)?,
                buff_id: r.u32("buff_id")?,
                buff_from_source: r.bool_str("buff_from_source")?,
                buff_to_source: r.bool_str("buff_to_source")?,
                req_buff_id: r.u32("req_buff_id")?,
                is_heal_spell: r.bool_str("is_heal_spell")?,
            };
            d.combat_buffs.entry(c.req_buff_id).or_default().push(c);
            Ok(())
        })?;
        {
            let effect_types = &d.effect_types;
            let all_effects = &d.effects;
            let triggers = &mut d.buff_triggers;
            for_each_row(conn, "SELECT * FROM buff_triggers", [], |r| {
                let buff_id = r.u32("buff_id")?;
                let list = triggers.entry(buff_id).or_default();
                let effect_id = r.u32("effect_id")?;
                // GetEffectTemplate(id): unknown type id -> null; unknown actual id -> throws.
                let effect = match effect_types.get(&effect_id) {
                    None => None,
                    Some(ty) => match EffectKind::from_name(&ty.actual_type) {
                        None => None,
                        Some(kind) => {
                            if !all_effects
                                .get(&kind)
                                .is_some_and(|m| m.contains_key(&ty.actual_id))
                            {
                                return Err(missing(
                                    &format!("{} effect", ty.actual_type),
                                    ty.actual_id,
                                ));
                            }
                            Some((kind, ty.actual_id))
                        }
                    },
                };
                if let Some(effect) = effect {
                    list.push(BuffTriggerTemplate {
                        id: r.u32("id")?,
                        kind: r.i64("event_id")? as u16,
                        effect_on_source: r.bool_str("effect_on_source")?,
                        effect,
                        use_damage_amount: r.bool_str("use_damage_amount")?,
                        use_original_source: r.bool_str("use_original_source")?,
                        target_buff_tag_id: r.u32_or("target_buff_tag_id", 0)?,
                        target_no_buff_tag_id: r.u32_or("target_no_buff_tag_id", 0)?,
                        synergy: r.bool_str("synergy")?,
                    });
                }
                Ok(())
            })?;
        }
        let mut reagent_ids = std::collections::HashSet::new();
        for_each_row(conn, "SELECT * from skill_reagents", [], |r| {
            let t = SkillItemAmount {
                id: r.u32("id")?,
                skill_id: r.u32("skill_id")?,
                item_id: r.u32("item_id")?,
                amount: r.i16("amount")?,
            };
            if !reagent_ids.insert(t.id) {
                return Err(dup("skill reagent", t.id));
            }
            d.skill_reagents.push(t);
            Ok(())
        })?;
        let mut product_ids = std::collections::HashSet::new();
        for_each_row(conn, "SELECT * from skill_products", [], |r| {
            let t = SkillItemAmount {
                id: r.u32("id")?,
                skill_id: r.u32("skill_id")?,
                item_id: r.u32("item_id")?,
                amount: r.i16("amount")?,
            };
            if !product_ids.insert(t.id) {
                return Err(dup("skill product", t.id));
            }
            d.skill_products.push(t);
            Ok(())
        })?;
        for_each_row(
            conn,
            "SELECT * FROM unit_modifiers WHERE owner_type='Item'",
            [],
            |r| {
                if let Some(b) = templates::read_bonus(r)? {
                    d.item_unit_modifiers
                        .entry(r.u32("owner_id")?)
                        .or_default()
                        .push(b);
                }
                Ok(())
            },
        )?;
        d.buffs = buffs.into_iter().map(|(k, v)| (k, Arc::new(v))).collect();
        d.area_shapes = misc::load_area_shapes(conn)?;
        d.model_radii = misc::load_actor_model_radii(conn)?;
        d.unit_reqs = misc::load_unit_reqs(conn)?;
        d.buff_game_data = BuffGameData::load(conn)?;
        d.npc_tags = misc::load_npc_tags(conn)?;
        d.zone_group_buffs = misc::load_zone_group_buffs(conn)?;
        Ok(d)
    }

    // ---------------------------------------------------------------- SkillManager getters

    /// `GetSkillTemplate(id)`.
    pub fn skill(&self, id: u32) -> Option<&Arc<SkillTemplate>> {
        self.skills.get(&id)
    }

    /// `GetBuffTemplate(id)`.
    pub fn buff(&self, id: u32) -> Option<&Arc<BuffTemplate>> {
        self.buffs.get(&id)
    }

    /// `GetEffectTemplate(id, type)`.
    pub fn effect(&self, kind: EffectKind, id: u32) -> Option<&EffectTemplate> {
        self.effects.get(&kind)?.get(&id)
    }

    /// `GetEffectTemplate(id, typeName)`.
    pub fn effect_by_name(&self, id: u32, type_name: &str) -> Option<&EffectTemplate> {
        self.effect(EffectKind::from_name(type_name)?, id)
    }

    /// `GetEffectTemplate(id)` (through the effects table).
    pub fn effect_by_type_id(&self, id: u32) -> Option<(EffectKind, &EffectTemplate)> {
        let ty = self.effect_types.get(&id)?;
        let kind = EffectKind::from_name(&ty.actual_type)?;
        Some((kind, self.effect(kind, ty.actual_id)?))
    }

    /// `GetBuffTags(buffId)`.
    pub fn buff_tags(&self, buff_id: u32) -> &[u32] {
        self.buff_tags.get(&buff_id).map_or(&[], Vec::as_slice)
    }

    /// `GetBuffsByTagId(tagId)` (`None` = null in C#).
    pub fn buffs_by_tag(&self, tag_id: u32) -> Option<&[u32]> {
        self.tagged_buffs.get(&tag_id).map(Vec::as_slice)
    }

    /// `GetSkillTags(skillId)`.
    pub fn skill_tags(&self, skill_id: u32) -> &[u32] {
        self.skill_tags.get(&skill_id).map_or(&[], Vec::as_slice)
    }

    /// `GetSkillsByTag(tagId)`.
    pub fn skills_by_tag(&self, tag_id: u32) -> &[u32] {
        self.tagged_skills.get(&tag_id).map_or(&[], Vec::as_slice)
    }

    /// `GetModifiersByOwnerId(id)`.
    pub fn skill_modifiers_by_owner(&self, id: u32) -> &[SkillModifier] {
        self.skill_modifiers.get(&id).map_or(&[], Vec::as_slice)
    }

    /// `BuffGameData.GetModifiersForBuff(ownerId)`.
    pub fn buff_modifiers_for_buff(&self, id: u32) -> &[BuffModifier] {
        self.buff_game_data
            .buff_modifiers
            .get(&id)
            .map_or(&[], Vec::as_slice)
    }

    /// `GetCombatBuffs(reqBuffId)`.
    pub fn combat_buffs_for(&self, req_buff_id: u32) -> &[CombatBuffTemplate] {
        self.combat_buffs
            .get(&req_buff_id)
            .map_or(&[], Vec::as_slice)
    }

    /// `GetBuffTriggerTemplates(buffId)`.
    pub fn buff_triggers_for(&self, buff_id: u32) -> &[BuffTriggerTemplate] {
        self.buff_triggers.get(&buff_id).map_or(&[], Vec::as_slice)
    }

    /// `GetLinearFunc(id)`.
    pub fn linear_func(&self, id: u32) -> Option<LinearFuncTemplate> {
        self.linear_funcs.get(&id).copied()
    }

    /// `GetSkillReagentsBySkillId(id)`.
    pub fn reagents_for(&self, skill_id: u32) -> Vec<SkillItemAmount> {
        self.skill_reagents
            .iter()
            .filter(|r| r.skill_id == skill_id)
            .copied()
            .collect()
    }

    /// `GetSkillProductsBySkillId(id)`.
    pub fn products_for(&self, skill_id: u32) -> Vec<SkillItemAmount> {
        self.skill_products
            .iter()
            .filter(|r| r.skill_id == skill_id)
            .copied()
            .collect()
    }

    /// `GetSkillControllerTemplate` (`GetEffectTemplate(id, "SkillController")`).
    pub fn skill_controller(&self, id: u32) -> Option<&SkillControllerTemplate> {
        match self.effect(EffectKind::SkillController, id)? {
            EffectTemplate::SkillController(t) => Some(t),
            _ => None,
        }
    }

    /// `UnitRequirementsGameData.GetSkillRequirements(skillId)`.
    pub fn skill_requirements(&self, skill_id: u32) -> Vec<&UnitReq> {
        self.unit_reqs
            .get("Skill")
            .map(|l| l.iter().filter(|u| u.owner_id == skill_id).collect())
            .unwrap_or_default()
    }

    /// `BuffGameData.GetBuffToleranceForBuffTag(tag)`.
    pub fn tolerance_for_tag(&self, tag: u32) -> Option<&BuffTolerance> {
        self.buff_game_data.tolerances.get(&tag)
    }

    /// `ModelManager.GetActorModel(modelId)?.Radius ?? 0`.
    pub fn model_radius(&self, model_id: u32) -> f32 {
        self.model_radii.get(&model_id).copied().unwrap_or(0.0)
    }

    /// `TagsGameData.GetIdsByTagId(Npcs, tag)`.
    pub fn npcs_by_tag(&self, tag: u32) -> &[u32] {
        self.npc_tags.get(&tag).map_or(&[], Vec::as_slice)
    }

    /// `SkillManager.GetNpSkillTemplate(npcSkill)`: the template of an NPC skill, `None` for an
    /// unknown skill or (the dungeon `InCombat` filter, `SkillUseConditionKind.InCombat = 0`) a
    /// skill that respects the global cooldown or has a plot.
    pub fn np_skill(&self, skill_id: u32, use_condition: i32) -> Option<&Arc<SkillTemplate>> {
        let t = self.skill(skill_id);
        if use_condition == 0 {
            if let Some(t) = t {
                if !t.ignore_global_cooldown || t.plot_id.is_some() {
                    return None;
                }
            }
        }
        t
    }

    /// `SkillManager.GetSkillActAbility(skillId)` (`ActabilityType`, 0 = None).
    pub fn skill_actability(&self, skill_id: u32) -> i32 {
        self.skill(skill_id).map_or(0, |t| t.actability_group_id)
    }

    /// `SkillManager.GetSpawnGimmickEffect(gimmickTemplateId)`: the first spawn effect of a
    /// gimmick (dictionary order: by effect id here).
    pub fn spawn_gimmick_effect(
        &self,
        gimmick_template_id: u32,
    ) -> Option<&effects::SpawnGimmickEffect> {
        let map = self.effects.get(&EffectKind::SpawnGimmickEffect)?;
        let mut ids: Vec<&u32> = map.keys().collect();
        ids.sort();
        ids.into_iter().find_map(|id| match map.get(id) {
            Some(EffectTemplate::SpawnGimmick(e)) if e.gimmick_id == gimmick_template_id => Some(e),
            _ => None,
        })
    }

    /// `IsDefaultSkill` / `IsCommonSkill` live in `data::skills::SkillData` (phase 2).
    pub fn is_loaded(&self) -> bool {
        !self.skills.is_empty()
    }

    /// Adds a skill template (tests / synthetic data).
    pub fn add_skill(&mut self, t: SkillTemplate) {
        self.skills.insert(t.id, Arc::new(t));
    }

    /// Adds a buff template (tests / synthetic data).
    pub fn add_buff(&mut self, t: BuffTemplate) {
        self.buffs.insert(t.id, Arc::new(t));
    }

    /// Adds an effect template (tests / synthetic data); also registers an `effects` row with
    /// id `type_id` pointing at it when `type_id != 0`.
    pub fn add_effect(&mut self, kind: EffectKind, t: EffectTemplate, type_id: u32) {
        let id = t.id();
        self.effects.entry(kind).or_default().insert(id, t);
        if type_id != 0 {
            self.effect_types.insert(
                type_id,
                EffectType {
                    id: type_id,
                    actual_id: id,
                    actual_type: kind.name().to_string(),
                },
            );
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Loads every table of the real 1.2 compact.sqlite3 and reports row counts
    /// (`AAEMU_COMPACT_DB=... cargo test -p aaemu-game skills_real_compact_db -- --ignored`).
    #[test]
    #[ignore = "needs AAEMU_COMPACT_DB"]
    fn skills_real_compact_db() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let conn =
            Connection::open_with_flags(&path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY).unwrap();
        let mut files = crate::data::client_files::ClientFileManager::new();
        if let Ok(pak) = std::env::var("AAEMU_GAME_PAK") {
            files.add_source(&pak, std::path::Path::new("."));
        }
        let d = Data::load_from(&conn, &files).unwrap();
        let effect_counts: Vec<String> = EffectKind::ALL
            .iter()
            .map(|k| format!("{}={}", k.name(), d.effects.get(k).map_or(0, |m| m.len())))
            .collect();
        let skill_effects: usize = d.skills.values().map(|s| s.effects.len()).sum();
        let tick_effects: usize = d.buffs.values().map(|b| b.tick_effects.len()).sum();
        let bonuses: usize = d.buffs.values().map(|b| b.bonuses.len()).sum();
        let dyn_bonuses: usize = d.buffs.values().map(|b| b.dynamic_bonuses.len()).sum();
        let triggers: usize = d.buff_triggers.values().map(Vec::len).sum();
        let anims_with_sync = d.anims.anims.values().filter(|a| a.duration > 0).count();
        eprintln!(
            "skills {} skill_effects {} buffs {} tick_effects {} buff bonuses {} dynamic {} \
             effect types {} buff tags {} tagged skills {} skill modifiers {} buff modifiers {} \
             tolerances {} combat buffs {} triggers {} reagents {} products {} linear funcs {} \
             item modifiers {} plots {} plot events {} plot conditions {} trees {} anims {} \
             (with client timing {}) aoe shapes {} model radii {} unit reqs {} npc tags {} \
             zone group buffs {}\n{}",
            d.skills.len(),
            skill_effects,
            d.buffs.len(),
            tick_effects,
            bonuses,
            dyn_bonuses,
            d.effect_types.len(),
            d.tagged_buffs.len(),
            d.tagged_skills.len(),
            d.skill_modifiers.values().map(Vec::len).sum::<usize>(),
            d.buff_game_data
                .buff_modifiers
                .values()
                .map(Vec::len)
                .sum::<usize>(),
            d.buff_game_data.tolerances.len(),
            d.combat_buffs.values().map(Vec::len).sum::<usize>(),
            triggers,
            d.skill_reagents.len(),
            d.skill_products.len(),
            d.linear_funcs.len(),
            d.item_unit_modifiers.values().map(Vec::len).sum::<usize>(),
            d.plots.plots.len(),
            d.plots.events.len(),
            d.plots.conditions.len(),
            d.plots.trees.len(),
            d.anims.anims.len(),
            anims_with_sync,
            d.area_shapes.len(),
            d.model_radii.len(),
            d.unit_reqs.values().map(Vec::len).sum::<usize>(),
            d.npc_tags.len(),
            d.zone_group_buffs.len(),
            effect_counts.join(" "),
        );
        assert!(d.skills.len() > 10_000);
        assert!(d.buffs.len() > 5_000);
        assert!(d.plots.trees.len() > 1_000);
        // Every skill effect / trigger points at a loaded template.
        for s in d.skills.values() {
            for e in &s.effects {
                if let Some((k, id)) = e.template {
                    assert!(d.effect(k, id).is_some());
                }
            }
        }
        // Melee auto attack exists.
        assert!(d.skill(2).is_some());
    }
}
