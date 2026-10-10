//! Smaller tables the skill code needs: `AnimationManager` (anims + `combat_sync_event_list.g`),
//! `aoe_shapes` (`WorldManager.GetAreaShapeById`), actor model radii (`ModelManager`),
//! `unit_reqs` (`UnitRequirementsGameData`), `buff_modifiers` / `buff_tolerances`
//! (`BuffGameData`), `tagged_npcs` (`TagsGameData`), zone group buffs.

use std::collections::HashMap;

use rusqlite::Connection;

use crate::data::client_files::ClientFileManager;
use crate::data::sqlite::for_each_row;
use crate::models::unit::{BuffAttribute, BuffModifier, UnitModifierType};

/// `Anim`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Anim {
    pub id: u32,
    pub name: String,
    pub looping: bool,
    pub category: u32,
    /// From `game/combat_sync_event_list.g` (`nuian_male` only).
    pub duration: i32,
    pub combat_sync_time: i32,
}

/// `AnimationManager`.
#[derive(Debug, Clone, Default)]
pub struct AnimData {
    pub anims: HashMap<u32, Anim>,
    pub by_name: HashMap<String, u32>,
}

/// One `.g` model section: animation name -> (total_time, combat_sync_time).
type GFileSection = (String, Vec<(String, i32, i32)>);

/// `AnimationManager.ParseGFile`: `None` on a syntax error (the C# logs and returns null).
pub fn parse_g_file(text: &str) -> Option<Vec<GFileSection>> {
    let mut res: Vec<GFileSection> = Vec::new();
    let mut have_anim = false;
    for line in text.split("\r\n") {
        let spaces = line.chars().take_while(|c| c.is_whitespace()).count();
        let trimmed = line.trim_matches(' ');
        if spaces == 0 {
            res.push((line.trim_matches('"').to_string(), Vec::new()));
            have_anim = false;
        } else if spaces == 4 && !res.is_empty() {
            let section = res.last_mut()?;
            if section.1.iter().any(|a| a.0 == trimmed) {
                return None; // TryAdd failed: syntax error
            }
            section.1.push((trimmed.to_string(), 0, 0));
            have_anim = true;
        } else if spaces == 8 && have_anim {
            let props: Vec<&str> = trimmed.split(' ').collect();
            if props.len() != 2 {
                return None;
            }
            let anim = res.last_mut()?.1.last_mut()?;
            match props[0] {
                "total_time" => anim.1 = props[1].parse().ok()?,
                "combat_sync_time" => anim.2 = props[1].parse().ok()?,
                other => tracing::warn!("Unknown property {other} in combat_sync_event_list.g"),
            }
        } else {
            return None;
        }
    }
    Some(res)
}

impl AnimData {
    pub fn get(&self, id: u32) -> Option<&Anim> {
        self.anims.get(&id)
    }

    pub fn load(conn: &Connection, files: &ClientFileManager) -> rusqlite::Result<Self> {
        let mut d = AnimData::default();
        for_each_row(conn, "SELECT * FROM anims", [], |r| {
            let a = Anim {
                id: r.u32("id")?,
                name: r.string("name")?,
                looping: r.bool("loop")?,
                category: r.u32("category_id")?,
                duration: 0,
                combat_sync_time: 0,
            };
            if d.by_name.insert(a.name.clone(), a.id).is_some() || d.anims.contains_key(&a.id) {
                return Err(rusqlite::Error::InvalidParameterName(format!(
                    "duplicate anim {} (Dictionary.Add)",
                    a.id
                )));
            }
            d.anims.insert(a.id, a);
            Ok(())
        })?;
        let file = "game/combat_sync_event_list.g";
        if !files.file_exists(file) {
            // The C# throws inside ParseGFile (null string) when the client files are missing.
            tracing::error!("Error reading {file}: no client file source has it");
            return Ok(d);
        }
        match parse_g_file(&files.read_to_string(file)) {
            None => tracing::error!("Error reading {file}"),
            Some(sections) => {
                for (model, anims) in sections {
                    if model != "nuian_male" {
                        continue;
                    }
                    for (name, total, sync) in anims {
                        if let Some(id) = d.by_name.get(&name) {
                            if let Some(a) = d.anims.get_mut(id) {
                                a.duration = total;
                                a.combat_sync_time = sync;
                            }
                        }
                    }
                }
            }
        }
        Ok(d)
    }
}

/// `AreaShapeType`.
pub mod area_shape_type {
    pub const SPHERE: u32 = 1;
    pub const CUBOID: u32 = 2;
}

/// `AreaShape` (aoe_shapes rows).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct AreaShape {
    pub id: u32,
    pub kind: u32,
    pub value1: f32,
    pub value2: f32,
    pub value3: f32,
}

pub fn load_area_shapes(conn: &Connection) -> rusqlite::Result<HashMap<u32, AreaShape>> {
    let mut out = HashMap::new();
    for_each_row(conn, "SELECT * FROM aoe_shapes", [], |r| {
        let s = AreaShape {
            id: r.u32("id")?,
            kind: r.u32("kind_id")?,
            value1: r.f32("value1")?,
            value2: r.f32("value2")?,
            value3: r.f32("value3")?,
        };
        out.entry(s.id).or_insert(s); // TryAdd
        Ok(())
    })?;
    Ok(out)
}

/// `ModelManager.GetActorModel(modelId)?.Radius` for every model id that is an actor model.
pub fn load_actor_model_radii(conn: &Connection) -> rusqlite::Result<HashMap<u32, f32>> {
    let mut actor = HashMap::new();
    for_each_row(conn, "SELECT * FROM actor_models", [], |r| {
        actor.insert(r.u32("id")?, r.f32("radius")?);
        Ok(())
    })?;
    let mut out = HashMap::new();
    for_each_row(conn, "SELECT * FROM models", [], |r| {
        let sub_type = r.string_or("sub_type", "")?;
        if sub_type == "ActorModel" {
            if let Some(radius) = actor.get(&r.u32("sub_id")?) {
                out.insert(r.u32("id")?, *radius);
            }
        }
        Ok(())
    })?;
    Ok(out)
}

/// `UnitReqs`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct UnitReq {
    pub id: u32,
    pub owner_id: u32,
    pub owner_type: String,
    pub kind: u32,
    pub value1: u32,
    pub value2: u32,
}

/// `UnitRequirementsGameData`: requirement rows by owner type (table order).
pub fn load_unit_reqs(conn: &Connection) -> rusqlite::Result<HashMap<String, Vec<UnitReq>>> {
    let mut out: HashMap<String, Vec<UnitReq>> = HashMap::new();
    let mut seen = std::collections::HashSet::new();
    for_each_row(conn, "SELECT * FROM unit_reqs", [], |r| {
        let u = UnitReq {
            id: r.u32("id")?,
            owner_id: r.u32("owner_id")?,
            owner_type: r.string("owner_type")?,
            kind: r.u32("kind_id")?,
            value1: r.u32("value1")?,
            value2: r.u32("value2")?,
        };
        seen.insert(u.id);
        out.entry(u.owner_type.clone()).or_default().push(u);
        Ok(())
    })?;
    Ok(out)
}

/// `BuffTolerance` (+ steps sorted by id, `PostLoad`).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BuffTolerance {
    pub id: u32,
    pub buff_tag_id: u32,
    pub step_duration: u32,
    pub final_step_buff_id: u32,
    pub character_time_reduction: u32,
    /// (step id, hit chance, time reduction).
    pub steps: Vec<(u32, u32, u32)>,
}

impl BuffTolerance {
    /// `GetFirstStep`.
    pub fn first_step(&self) -> Option<(u32, u32, u32)> {
        self.steps.first().copied()
    }

    /// `GetStepAfter(step)` (`First(st => st.Id > step.Id)`: `None` = the C# throws).
    pub fn step_after(&self, step_id: u32) -> Option<(u32, u32, u32)> {
        self.steps.iter().find(|s| s.0 > step_id).copied()
    }
}

/// `BuffGameData`.
#[derive(Debug, Clone, Default)]
pub struct BuffGameData {
    pub buff_modifiers: HashMap<u32, Vec<BuffModifier>>,
    /// By buff tag id.
    pub tolerances: HashMap<u32, BuffTolerance>,
}

impl BuffGameData {
    pub fn load(conn: &Connection) -> rusqlite::Result<Self> {
        let mut d = BuffGameData::default();
        for_each_row(conn, "SELECT * FROM buff_modifiers", [], |r| {
            let attr = r.u32("buff_attribute_id")? as u8;
            let Some(buff_attribute) = BuffAttribute::from_u8(attr) else {
                tracing::warn!("buff_modifiers: unknown buff attribute {attr}");
                return Ok(());
            };
            let m = BuffModifier {
                id: r.u32("id")?,
                owner_id: r.u32("owner_id")?,
                tag_id: r.u32_or("tag_id", 0)?,
                buff_attribute,
                unit_modifier_type: UnitModifierType::from_u8(r.u32("unit_modifier_type_id")? as u8),
                value: r.i32("value")?,
                buff_id: r.u32_or("buff_id", 0)?,
                synergy: r.bool("synergy")?,
            };
            d.buff_modifiers.entry(m.owner_id).or_default().push(m);
            Ok(())
        })?;
        let mut by_id: HashMap<u32, u32> = HashMap::new();
        for_each_row(conn, "SELECT * FROM buff_tolerances", [], |r| {
            let t = BuffTolerance {
                id: r.u32("id")?,
                buff_tag_id: r.u32("buff_tag_id")?,
                step_duration: r.u32("step_duration")?,
                final_step_buff_id: r.u32("final_step_buff_id")?,
                character_time_reduction: r.u32("character_time_reduction")?,
                steps: Vec::new(),
            };
            if d.tolerances.contains_key(&t.buff_tag_id) || by_id.contains_key(&t.id) {
                return Err(rusqlite::Error::InvalidParameterName(format!(
                    "duplicate buff tolerance {} (Dictionary.Add)",
                    t.id
                )));
            }
            by_id.insert(t.id, t.buff_tag_id);
            d.tolerances.insert(t.buff_tag_id, t);
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM buff_tolerance_steps", [], |r| {
            let tol = r.u32("buff_tolerance_id")?;
            let Some(tag) = by_id.get(&tol) else {
                return Ok(());
            };
            if let Some(t) = d.tolerances.get_mut(tag) {
                t.steps
                    .push((r.u32("id")?, r.u32("hit_chance")?, r.u32("time_reduction")?));
            }
            Ok(())
        })?;
        for t in d.tolerances.values_mut() {
            t.steps.sort_by_key(|s| s.0);
        }
        Ok(d)
    }
}

/// `TagsGameData` npc tags: tag id -> npc template ids.
pub fn load_npc_tags(conn: &Connection) -> rusqlite::Result<HashMap<u32, Vec<u32>>> {
    let mut out: HashMap<u32, Vec<u32>> = HashMap::new();
    for_each_row(conn, "SELECT * FROM tagged_npcs", [], |r| {
        out.entry(r.u32("tag_id")?)
            .or_default()
            .push(r.u32("npc_id")?);
        Ok(())
    })?;
    Ok(out)
}

/// `ZoneGroup.BuffId` per zone group id (only non-zero buffs).
pub fn load_zone_group_buffs(conn: &Connection) -> rusqlite::Result<HashMap<u32, u32>> {
    let mut out = HashMap::new();
    for_each_row(conn, "SELECT * FROM zone_groups", [], |r| {
        let buff = r.u32_or("buff_id", 0)?;
        if buff != 0 {
            out.insert(r.u32("id")?, buff);
        }
        Ok(())
    })?;
    Ok(out)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn g_file_parsing() {
        let text = "\"nuian_male\"\r\n    attack_1\r\n        total_time 1200\r\n        combat_sync_time 400\r\n\"elf_female\"\r\n    attack_1\r\n        total_time 1\r\n";
        let s = parse_g_file(text).unwrap();
        assert_eq!(s.len(), 2);
        assert_eq!(s[0].0, "nuian_male");
        assert_eq!(s[0].1, vec![("attack_1".to_string(), 1200, 400)]);
        assert!(parse_g_file("\"a\"\r\n  bad").is_none());
    }
}
