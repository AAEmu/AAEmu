//! `GameData/LootGameData`: `loots`, `loot_groups`, `loot_actability_groups` and the generated
//! `LootPack`s (`Models/Game/Items/Loots`).

use std::collections::HashMap;

use rusqlite::Connection;

use crate::data::sqlite::for_each_row;

/// `Loot` (`loots`; `DropRate` already mapped by the C# query: `<= 1` -> 10 000 000).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct Loot {
    pub id: u32,
    pub group: u32,
    pub item_id: u32,
    pub drop_rate: u32,
    pub min_amount: i32,
    pub max_amount: i32,
    pub loot_pack_id: u32,
    pub grade_id: u8,
    pub always_drop: bool,
}

/// `LootGroups` (`loot_groups`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct LootGroups {
    pub id: u32,
    pub pack_id: u32,
    pub group_no: u32,
    pub drop_rate: u32,
    pub item_grade_distribution_id: u8,
}

/// `LootActabilityGroups` (`loot_actability_groups`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct LootActabilityGroups {
    pub id: u32,
    pub loot_pack_id: u32,
    pub group_id: u32,
    pub max_dice: u32,
    pub min_dice: u32,
}

/// `LootPack` (the data part; the rolls are in `systems::economy::loot`). The C# dictionaries
/// are kept as insertion-ordered vectors.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct LootPack {
    pub id: u32,
    /// `GroupCount` (highest group number).
    pub group_count: u32,
    pub loots: Vec<Loot>,
    /// `Groups` (group no -> group).
    pub groups: Vec<(u32, LootGroups)>,
    /// `ActabilityGroups` (loot group id -> actability group).
    pub actability_groups: Vec<(u32, LootActabilityGroups)>,
    /// `LootsByGroupNo` (group no -> loots, in loot order).
    pub loots_by_group_no: Vec<(u32, Vec<Loot>)>,
}

impl LootPack {
    /// `Groups.Values.FirstOrDefault(g => g.GroupNo == groupNo)`.
    pub fn group(&self, group_no: u32) -> Option<&LootGroups> {
        self.groups
            .iter()
            .map(|(_, g)| g)
            .find(|g| g.group_no == group_no)
    }

    /// `ActabilityGroups.Values.FirstOrDefault(g => g.GroupId == group)`.
    pub fn actability_group(&self, group: u32) -> Option<&LootActabilityGroups> {
        self.actability_groups
            .iter()
            .map(|(_, g)| g)
            .find(|g| g.group_id == group)
    }
}

/// `LootGameData`.
#[derive(Debug, Clone, Default)]
pub struct LootData {
    /// `_lootPacks`.
    pub packs: HashMap<u32, LootPack>,
    /// `_loots` / `_lootGroups` / `_lootActabilityGroups` (row counts, for diagnostics).
    pub loot_count: usize,
    pub group_count: usize,
    pub actability_group_count: usize,
}

fn dup(what: &str, id: u32) -> rusqlite::Error {
    rusqlite::Error::InvalidParameterName(format!("duplicate {what} {id} (Dictionary.Add)"))
}

impl LootData {
    /// `LootGameData.Load`.
    pub fn load(conn: &Connection) -> rusqlite::Result<Self> {
        let mut loot_ids = std::collections::HashSet::new();
        let mut loots_by_pack: Vec<(u32, Vec<Loot>)> = Vec::new();
        let mut pack_index: HashMap<u32, usize> = HashMap::new();
        for_each_row(
            conn,
            "SELECT id, `group`, item_id, CASE WHEN drop_rate > 1 THEN drop_rate WHEN drop_rate <= 1 THEN 10000000 END as drop_rate, min_amount, max_amount, loot_pack_id, grade_id, always_drop FROM loots ORDER BY `group` ASC, loot_pack_id ASC, drop_rate DESC",
            [],
            |r| {
                let l = Loot {
                    id: r.u32("id")?,
                    group: r.u32("group")?,
                    item_id: r.u32("item_id")?,
                    drop_rate: r.u32("drop_rate")?,
                    min_amount: r.i32("min_amount")?,
                    max_amount: r.i32("max_amount")?,
                    loot_pack_id: r.u32("loot_pack_id")?,
                    grade_id: r.u8("grade_id")?,
                    always_drop: r.bool_str("always_drop")?,
                };
                if !loot_ids.insert(l.id) {
                    return Err(dup("loot", l.id));
                }
                let idx = *pack_index.entry(l.loot_pack_id).or_insert_with(|| {
                    loots_by_pack.push((l.loot_pack_id, Vec::new()));
                    loots_by_pack.len() - 1
                });
                loots_by_pack[idx].1.push(l);
                Ok(())
            },
        )?;

        let mut group_ids = std::collections::HashSet::new();
        let mut groups_by_pack: HashMap<u32, Vec<LootGroups>> = HashMap::new();
        for_each_row(
            conn,
            "SELECT id, pack_id, group_no, CASE WHEN drop_rate > 1 THEN drop_rate WHEN drop_rate <= 1 THEN 10000000 END as drop_rate, item_grade_distribution_id FROM loot_groups ORDER BY pack_id ASC, group_no ASC, drop_rate DESC",
            [],
            |r| {
                let g = LootGroups {
                    id: r.u32("id")?,
                    pack_id: r.u32("pack_id")?,
                    group_no: r.u32("group_no")?,
                    drop_rate: r.u32("drop_rate")?,
                    item_grade_distribution_id: r.u8("item_grade_distribution_id")?,
                };
                if !group_ids.insert(g.id) {
                    return Err(dup("loot group", g.id));
                }
                groups_by_pack.entry(g.pack_id).or_default().push(g);
                Ok(())
            },
        )?;

        let mut act_ids = std::collections::HashSet::new();
        let mut act_by_pack: HashMap<u32, Vec<LootActabilityGroups>> = HashMap::new();
        for_each_row(conn, "SELECT * FROM loot_actability_groups", [], |r| {
            let g = LootActabilityGroups {
                id: r.u32("id")?,
                loot_pack_id: r.u32("loot_pack_id")?,
                group_id: r.u32("loot_group_id")?,
                max_dice: r.u32("max_dice")?,
                min_dice: r.u32("min_dice")?,
            };
            if !act_ids.insert(g.id) {
                return Err(dup("loot actability group", g.id));
            }
            act_by_pack.entry(g.loot_pack_id).or_default().push(g);
            Ok(())
        })?;

        let mut data = LootData {
            loot_count: loot_ids.len(),
            group_count: group_ids.len(),
            actability_group_count: act_ids.len(),
            ..Default::default()
        };
        // Generate packs
        for (pack_id, loots) in loots_by_pack {
            let mut pack = LootPack {
                id: pack_id,
                ..Default::default()
            };
            if let Some(groups) = groups_by_pack.get(&pack_id) {
                for g in groups {
                    if pack.groups.iter().any(|(k, _)| *k == g.group_no) {
                        return Err(dup("loot pack group no", g.group_no));
                    }
                    pack.groups.push((g.group_no, *g));
                }
            }
            if let Some(groups) = act_by_pack.get(&pack_id) {
                for g in groups {
                    if pack.actability_groups.iter().any(|(k, _)| *k == g.group_id) {
                        return Err(dup("loot pack actability group", g.group_id));
                    }
                    pack.actability_groups.push((g.group_id, *g));
                }
            }
            for l in &loots {
                match pack
                    .loots_by_group_no
                    .iter_mut()
                    .find(|(g, _)| *g == l.group)
                {
                    Some((_, v)) => v.push(*l),
                    None => pack.loots_by_group_no.push((l.group, vec![*l])),
                }
                if pack.group_count < l.group {
                    pack.group_count = l.group;
                }
            }
            pack.loots = loots;
            data.packs.insert(pack.id, pack);
        }
        Ok(data)
    }

    /// `GetPack(id)`.
    pub fn pack(&self, id: u32) -> Option<&LootPack> {
        self.packs.get(&id)
    }
}
