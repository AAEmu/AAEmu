//! Buff-dependent cast restrictions from `skill_reqs` and its skill/tag bindings.
//! Derived from 10.x zone `sub_393616E0` / `sub_39CEECD0`; the tables and
//! `SkillReqFail` result also exist in the 1.2 client data/protocol.

use std::collections::{HashMap, HashSet};

use rusqlite::Connection;

use crate::data::sqlite::for_each_row;
use crate::server::GameServer;
use crate::world::WorldObject;

use super::data::SkillTemplate;

#[derive(Debug, Clone, Default)]
pub struct BuffSkillRequirement {
    pub id: u32,
    pub target: bool,
    pub buff_id: u32,
    pub buff_tag_id: u32,
    /// True: deny listed skills; false: only allow listed skills while the buff exists.
    pub default_result: bool,
    pub skill_ids: HashSet<u32>,
    pub skill_tags: HashSet<u32>,
}

pub fn load(conn: &Connection) -> rusqlite::Result<Vec<BuffSkillRequirement>> {
    let mut requirements = Vec::new();
    let mut indices = HashMap::new();
    for_each_row(conn, "SELECT * FROM skill_reqs ORDER BY id", [], |r| {
        let id = r.u32("id")?;
        if indices.insert(id, requirements.len()).is_some() {
            return Err(rusqlite::Error::InvalidParameterName(format!(
                "duplicate skill requirement {id}"
            )));
        }
        requirements.push(BuffSkillRequirement {
            id,
            target: r.bool_str("target")?,
            buff_id: r.u32_or("buff_id", 0)?,
            buff_tag_id: r.u32_or("buff_tag_id", 0)?,
            default_result: r.bool_str("default_result")?,
            ..Default::default()
        });
        Ok(())
    })?;
    // The reference loader ignores bindings whose requirement was removed from the data.
    for_each_row(conn, "SELECT * FROM skill_req_skills", [], |r| {
        if let Some(&index) = indices.get(&r.u32("skill_req_id")?) {
            requirements[index].skill_ids.insert(r.u32("skill_id")?);
        }
        Ok(())
    })?;
    for_each_row(conn, "SELECT * FROM skill_req_skill_tags", [], |r| {
        if let Some(&index) = indices.get(&r.u32("skill_req_id")?) {
            requirements[index]
                .skill_tags
                .insert(r.u32("skill_tag_id")?);
        }
        Ok(())
    })?;
    Ok(requirements)
}

fn buff_ids(unit: &WorldObject) -> HashSet<u32> {
    unit.with_base_unit(|b| {
        b.buffs()
            .effects
            .iter()
            .map(|e| e.template.id)
            .filter(|id| *id != 0)
            .collect()
    })
}

/// The first failed requirement id (used by the client's localized `SkillReqFail` message).
pub fn failed_requirement(
    server: &GameServer,
    template: &SkillTemplate,
    caster: &WorldObject,
    target: Option<&WorldObject>,
) -> Option<u32> {
    let data = &server.game_data.systems.skills;
    if data.buff_skill_requirements.is_empty() {
        return None;
    }
    // Snapshot each unit separately; never hold object locks together or per requirement.
    let caster_buffs = buff_ids(caster);
    let target_buffs = target.filter(|o| o.is_unit()).map(buff_ids);
    let tags = data.skill_tags.get(&template.id);
    for req in &data.buff_skill_requirements {
        let active = if req.target {
            let Some(active) = &target_buffs else {
                continue; // Positions, items and doodads have no target-unit buff restrictions.
            };
            active
        } else {
            &caster_buffs
        };
        let affected = active.contains(&req.buff_id)
            || data
                .buffs_by_tag(req.buff_tag_id)
                .is_some_and(|ids| ids.iter().any(|id| active.contains(id)));
        if !affected {
            continue;
        }
        let listed = req.skill_ids.contains(&template.id)
            || tags.is_some_and(|tags| tags.iter().any(|tag| req.skill_tags.contains(tag)));
        if listed == req.default_result {
            return Some(req.id);
        }
    }
    None
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn loader_resolves_nullable_buffs_and_skill_tag_bindings() {
        let conn = Connection::open_in_memory().unwrap();
        conn.execute_batch(
            "CREATE TABLE skill_reqs (id INT, target TEXT, buff_id INT, buff_tag_id INT, default_result TEXT);
             CREATE TABLE skill_req_skills (skill_req_id INT, skill_id INT);
             CREATE TABLE skill_req_skill_tags (skill_req_id INT, skill_tag_id INT);
             INSERT INTO skill_reqs VALUES (20,'t',NULL,77,'f'),(10,'f',55,NULL,'t');
             INSERT INTO skill_req_skills VALUES (10,123),(999,456);
             INSERT INTO skill_req_skill_tags VALUES (20,88);",
        ).unwrap();
        let requirements = load(&conn).unwrap();
        assert_eq!(requirements.len(), 2);
        assert_eq!(requirements[0].id, 10);
        assert_eq!(
            (requirements[0].buff_id, requirements[0].buff_tag_id),
            (55, 0)
        );
        assert!(requirements[0].default_result && !requirements[0].target);
        assert_eq!(requirements[0].skill_ids, HashSet::from([123]));
        assert_eq!(requirements[1].skill_tags, HashSet::from([88]));
        assert!(requirements[1].target && !requirements[1].default_result);
        conn.execute("INSERT INTO skill_reqs VALUES (10,'f',55,NULL,'t')", [])
            .unwrap();
        assert!(load(&conn).is_err());
    }

    #[test]
    #[ignore = "needs AAEMU_COMPACT_DB"]
    fn real_buff_skill_requirements_load_the_1_2_bindings() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let conn =
            Connection::open_with_flags(path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY).unwrap();
        let requirements = load(&conn).unwrap();
        assert_eq!(requirements.len(), 175);
        let potion = requirements.iter().find(|r| r.id == 100).unwrap();
        assert_eq!(potion.buff_id, 4676);
        assert!(potion.default_result && !potion.target);
        assert!(requirements
            .iter()
            .any(|r| !r.default_result && !r.skill_tags.is_empty()));
        assert!(requirements.iter().any(|r| r.target && r.buff_tag_id != 0));
    }
}
