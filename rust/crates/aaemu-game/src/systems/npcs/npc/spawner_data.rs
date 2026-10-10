//! `NpcGameData` (`GameData/NpcGameData.cs`), spawner part: `npc_spawners`
//! ([`NpcSpawnerTemplate`], `Models/Game/NPChar/NpcSpawnerTemplate.cs`), `npc_spawner_npcs`
//! ([`NpcSpawnerNpc`]) and the member -> spawner template ids index (`GetSpawnerIds`).
//! `np_skills` / `np_passive_buffs` belong to the NPC templates (`npc::data`).
//!
//! The C# singleton is mutated at runtime: `SpawnManager.AddNpcSpawner` registers generated
//! ("fake") spawner templates (`AddNpcSpawner`, `AddNpcSpawnerNpc`,
//! `AddMemberAndSpawnerTemplateIds`) and `SpawnManager.GetNpcSpawner(unitId, unit)` rewrites a
//! template's member list. Game data is immutable behind `Arc<GameData>`, so the tables live
//! behind an `RwLock` here (writes are rare: load time and summon effects).
//!
//! Also holds the template id sets of `TransferGameData` / `SlaveGameData`
//! (`Exist`), which `SpawnManager.Load` filters the spawn files with; those game-data classes
//! are not ported yet (fallback until the owning modules expose `exist`).

use std::collections::{HashMap, HashSet};
use std::sync::{Arc, RwLock};

use rusqlite::Connection;

use crate::data::sqlite::for_each_row;
use crate::systems::DataLoadCtx;

/// `NpcSpawnerCategory` (`npc_spawner_category_id`; the C# enum cast keeps any value).
pub mod npc_spawner_category {
    pub const NORMAL: u32 = 0;
    pub const AUTOCREATED: u32 = 1;
}

/// `NpcSpawnerNpc` as game data (one `npc_spawner_npcs` row). The spawning behaviour
/// (`NpcSpawnerNpc.Spawn`) lives in `npc::spawner`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct NpcSpawnerNpc {
    /// `Spawner<Npc>.Id` (the row id).
    pub id: u32,
    /// `NpcSpawnerTemplateId`.
    pub npc_spawner_template_id: u32,
    /// `MemberId` (NPC template id).
    pub member_id: u32,
    /// `MemberType` (`"Npc"` / `"NpcGroup"`; C# `null` is `""`).
    pub member_type: String,
    /// `Weight`.
    pub weight: f32,
    /// `Spawner<Npc>.UnitId` (only written by `SpawnManager.GetNpcSpawner`).
    pub unit_id: u32,
}

impl NpcSpawnerNpc {
    /// `new NpcSpawnerNpc(spawnerTemplateId)`.
    pub fn new(spawner_template_id: u32) -> Self {
        Self {
            npc_spawner_template_id: spawner_template_id,
            ..Self::default()
        }
    }

    /// `new NpcSpawnerNpc(spawnerTemplateId, npcTemplateId)`.
    pub fn with_member(spawner_template_id: u32, npc_template_id: u32) -> Self {
        Self {
            npc_spawner_template_id: spawner_template_id,
            member_id: npc_template_id,
            member_type: "Npc".into(),
            ..Self::default()
        }
    }
}

/// `NpcSpawnerTemplate`.
#[derive(Debug, Clone, PartialEq)]
pub struct NpcSpawnerTemplate {
    pub id: u32,
    /// `NpcSpawnerCategoryId` ([`npc_spawner_category`]).
    pub npc_spawner_category_id: u32,
    pub name: String,
    pub comment: String,
    pub max_population: u32,
    pub start_time: f32,
    pub end_time: f32,
    pub destroy_time: f32,
    pub spawn_delay_min: f32,
    pub activation_state: bool,
    pub save_indun: bool,
    pub min_population: u32,
    pub test_radius_npc: f32,
    pub test_radius_pc: f32,
    pub suspend_spawn_count: u32,
    pub spawn_delay_max: f32,
    /// `Npcs` (`null` for a parameterless C# instance is an empty list here).
    pub npcs: Vec<NpcSpawnerNpc>,
}

impl Default for NpcSpawnerTemplate {
    /// `new NpcSpawnerTemplate()` (the C# property initializers).
    fn default() -> Self {
        Self {
            id: 0,
            npc_spawner_category_id: npc_spawner_category::NORMAL,
            name: String::new(),
            comment: String::new(),
            max_population: 1,
            start_time: 0.0,
            end_time: 0.0,
            destroy_time: 0.0,
            spawn_delay_min: 5.0,
            activation_state: false,
            save_indun: false,
            min_population: 0,
            test_radius_npc: 3.0,
            test_radius_pc: 0.0,
            suspend_spawn_count: 0,
            spawn_delay_max: 10.0,
            npcs: Vec::new(),
        }
    }
}

impl NpcSpawnerTemplate {
    /// `new NpcSpawnerTemplate(spawnerId)`.
    pub fn with_spawner(spawner_id: u32) -> Self {
        Self {
            id: spawner_id,
            npcs: vec![NpcSpawnerNpc::new(spawner_id)],
            ..Self::default()
        }
    }

    /// `new NpcSpawnerTemplate(spawnerId, memberId)`.
    pub fn with_member(spawner_id: u32, member_id: u32) -> Self {
        Self {
            id: spawner_id,
            npcs: vec![NpcSpawnerNpc::with_member(spawner_id, member_id)],
            ..Self::default()
        }
    }
}

#[derive(Debug, Default)]
struct Inner {
    /// `NpcSpawnerTemplates`.
    templates: HashMap<u32, Arc<NpcSpawnerTemplate>>,
    /// `NpcSpawnerTemplateNpcs` values in insertion order (C# `Dictionary` enumeration order;
    /// `GetNpcSpawnerNpc` takes the first match).
    npcs: Vec<NpcSpawnerNpc>,
    /// `NpcSpawnerTemplateNpcs` keys -> index into `npcs`.
    npc_index: HashMap<u32, usize>,
    /// `NpcMemberAndSpawnerTemplateIds`.
    member_spawners: HashMap<u32, Vec<u32>>,
}

/// `NpcGameData` spawner tables (`server.game_data.systems.npcs.npc_spawners`).
#[derive(Debug, Default)]
pub struct NpcSpawnerData {
    inner: RwLock<Inner>,
    /// `TransferGameData` template ids (`transfers.id`).
    pub transfer_template_ids: HashSet<u32>,
    /// `SlaveGameData` template ids (`slaves.id`).
    pub slave_template_ids: HashSet<u32>,
}

fn dup(table: &str, id: u32) -> rusqlite::Error {
    rusqlite::Error::InvalidParameterName(format!("{table}: duplicate key {id}"))
}

impl NpcSpawnerData {
    /// `NpcGameData.Load` (spawner part) + `PostLoad.LoadMemberAndSpawnerTemplateIds`.
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        Ok(Self::load_from(ctx.conn)?)
    }

    /// [`NpcSpawnerData::load`] from a compact DB connection.
    pub fn load_from(conn: &Connection) -> rusqlite::Result<Self> {
        let mut templates: Vec<NpcSpawnerTemplate> = Vec::new();
        let mut seen = HashSet::new();
        for_each_row(conn, "SELECT * FROM npc_spawners", [], |r| {
            let t = NpcSpawnerTemplate {
                id: r.u32("id")?,
                npc_spawner_category_id: r.u32("npc_spawner_category_id")?,
                name: r.string("name")?,
                comment: r.string_or("comment", "")?,
                max_population: r.u32("maxPopulation")?,
                start_time: r.f32("startTime")?,
                end_time: r.f32("endTime")?,
                destroy_time: r.f32("destroyTime")?,
                spawn_delay_min: r.f32("spawn_delay_min")?,
                activation_state: r.bool_str("activation_state")?,
                save_indun: r.bool_str("save_indun")?,
                min_population: r.u32("min_population")?,
                test_radius_npc: r.f32("test_radius_npc")?,
                test_radius_pc: r.f32("test_radius_pc")?,
                suspend_spawn_count: r.u32("suspend_spawn_count")?,
                spawn_delay_max: r.f32("spawn_delay_max")?,
                npcs: Vec::new(),
            };
            if !seen.insert(t.id) {
                return Err(dup("npc_spawners", t.id));
            }
            templates.push(t);
            Ok(())
        })?;
        let mut npcs = Vec::new();
        for_each_row(conn, "SELECT * FROM npc_spawner_npcs", [], |r| {
            npcs.push(NpcSpawnerNpc {
                id: r.u32("id")?,
                npc_spawner_template_id: r.u32("npc_spawner_id")?,
                member_id: r.u32("member_id")?,
                member_type: r.string("member_type")?,
                weight: r.f32("weight")?,
                unit_id: 0,
            });
            Ok(())
        })?;
        let mut data = Self::from_parts(templates, npcs)?;
        data.transfer_template_ids = id_set(conn, "transfers")?;
        data.slave_template_ids = id_set(conn, "slaves")?;
        Ok(data)
    }

    /// Builds the tables like `Load` + `PostLoad` from template rows (their `npcs` are ignored)
    /// and member rows. Errors like the C# `Dictionary.Add` / indexer (duplicate ids, a member
    /// row of an unknown template).
    pub fn from_parts(
        templates: Vec<NpcSpawnerTemplate>,
        npcs: Vec<NpcSpawnerNpc>,
    ) -> rusqlite::Result<Self> {
        let mut inner = Inner::default();
        let mut owned: HashMap<u32, NpcSpawnerTemplate> = HashMap::new();
        let mut order = Vec::new();
        for mut t in templates {
            t.npcs.clear();
            if owned.contains_key(&t.id) {
                return Err(dup("npc_spawners", t.id));
            }
            order.push(t.id);
            owned.insert(t.id, t);
        }
        for nsn in npcs {
            if inner.npc_index.contains_key(&nsn.id) {
                return Err(dup("npc_spawner_npcs", nsn.id));
            }
            let Some(t) = owned.get_mut(&nsn.npc_spawner_template_id) else {
                return Err(rusqlite::Error::InvalidParameterName(format!(
                    "npc_spawner_npcs {}: npc_spawner_id {} not found",
                    nsn.id, nsn.npc_spawner_template_id
                )));
            };
            t.npcs.push(nsn.clone());
            inner.npc_index.insert(nsn.id, inner.npcs.len());
            inner.npcs.push(nsn);
        }
        for id in order {
            if let Some(t) = owned.remove(&id) {
                inner.templates.insert(id, Arc::new(t));
            }
        }
        // PostLoad: LoadMemberAndSpawnerTemplateIds (NpcSpawnerTemplateNpcs order).
        for nsn in &inner.npcs {
            inner
                .member_spawners
                .entry(nsn.member_id)
                .or_default()
                .push(nsn.npc_spawner_template_id);
        }
        Ok(Self {
            inner: RwLock::new(inner),
            ..Self::default()
        })
    }

    fn read(&self) -> std::sync::RwLockReadGuard<'_, Inner> {
        self.inner.read().expect("npc spawner data poisoned")
    }

    fn write(&self) -> std::sync::RwLockWriteGuard<'_, Inner> {
        self.inner.write().expect("npc spawner data poisoned")
    }

    /// `GetSpawnerIds(memberId)` (`null` = `None`).
    pub fn get_spawner_ids(&self, member_id: u32) -> Option<Vec<u32>> {
        self.read().member_spawners.get(&member_id).cloned()
    }

    /// `GetNpcSpawnerTemplate(id)`.
    pub fn get_npc_spawner_template(&self, id: u32) -> Option<Arc<NpcSpawnerTemplate>> {
        self.read().templates.get(&id).cloned()
    }

    /// `GetNpcSpawnerNpc(spawnerId)`: the first member row of the spawner template.
    pub fn get_npc_spawner_npc(&self, spawner_id: u32) -> Option<NpcSpawnerNpc> {
        self.read()
            .npcs
            .iter()
            .find(|n| n.npc_spawner_template_id == spawner_id)
            .cloned()
    }

    /// Number of spawner templates (diagnostics / tests).
    pub fn template_count(&self) -> usize {
        self.read().templates.len()
    }

    /// Number of member rows (diagnostics / tests).
    pub fn member_count(&self) -> usize {
        self.read().npcs.len()
    }

    /// `AddNpcSpawner(template)`. `false` when the id exists (C# `Dictionary.Add` throws).
    pub fn add_npc_spawner(&self, template: NpcSpawnerTemplate) -> bool {
        let mut inner = self.write();
        if inner.templates.contains_key(&template.id) {
            return false;
        }
        inner.templates.insert(template.id, Arc::new(template));
        true
    }

    /// `AddNpcSpawnerNpc(nsn)`. `false` when the id exists (C# `Dictionary.Add` throws).
    pub fn add_npc_spawner_npc(&self, nsn: NpcSpawnerNpc) -> bool {
        let mut inner = self.write();
        if inner.npc_index.contains_key(&nsn.id) {
            return false;
        }
        let idx = inner.npcs.len();
        inner.npc_index.insert(nsn.id, idx);
        inner.npcs.push(nsn);
        true
    }

    /// `AddMemberAndSpawnerTemplateIds(nsn)`.
    pub fn add_member_and_spawner_template_ids(&self, nsn: &NpcSpawnerNpc) {
        self.write()
            .member_spawners
            .entry(nsn.member_id)
            .or_default()
            .push(nsn.npc_spawner_template_id);
    }

    /// The in-place mutation of `SpawnManager.GetNpcSpawner(unitId, unit)`:
    /// `Template.Npcs = [nsn]` where `nsn` is the shared `NpcSpawnerTemplateNpcs` entry whose
    /// `MemberId` / `UnitId` were overwritten. Updates the template and the member row (by id);
    /// spawners that already hold the old template keep their snapshot (see `PORTING.md`).
    pub fn replace_template_npcs(&self, template_id: u32, nsn: NpcSpawnerNpc) {
        let mut inner = self.write();
        if let Some(&idx) = inner.npc_index.get(&nsn.id) {
            inner.npcs[idx] = nsn.clone();
        }
        if let Some(t) = inner.templates.get_mut(&template_id) {
            Arc::make_mut(t).npcs = vec![nsn];
        }
    }
}

fn id_set(conn: &Connection, table: &str) -> rusqlite::Result<HashSet<u32>> {
    let mut set = HashSet::new();
    for_each_row(conn, &format!("SELECT * FROM {table}"), [], |r| {
        let id = r.u32("id")?;
        if !set.insert(id) {
            return Err(dup(table, id));
        }
        Ok(())
    })?;
    Ok(set)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn tpl(id: u32) -> NpcSpawnerTemplate {
        NpcSpawnerTemplate {
            id,
            ..NpcSpawnerTemplate::default()
        }
    }

    fn nsn(id: u32, spawner: u32, member: u32) -> NpcSpawnerNpc {
        NpcSpawnerNpc {
            id,
            npc_spawner_template_id: spawner,
            member_id: member,
            member_type: "Npc".into(),
            weight: 1.0,
            unit_id: 0,
        }
    }

    #[test]
    fn index_and_lookups() {
        let d = NpcSpawnerData::from_parts(
            vec![tpl(1), tpl(2)],
            vec![nsn(10, 1, 500), nsn(11, 2, 500), nsn(12, 2, 501)],
        )
        .unwrap();
        assert_eq!(d.get_spawner_ids(500), Some(vec![1, 2]));
        assert_eq!(d.get_spawner_ids(501), Some(vec![2]));
        assert_eq!(d.get_spawner_ids(502), None);
        assert_eq!(d.get_npc_spawner_template(2).unwrap().npcs.len(), 2);
        assert_eq!(d.get_npc_spawner_npc(2).unwrap().id, 11);
        assert!(NpcSpawnerData::from_parts(vec![tpl(1)], vec![nsn(1, 9, 1)]).is_err());
        assert!(NpcSpawnerData::from_parts(vec![tpl(1), tpl(1)], vec![]).is_err());

        // Fake spawner registration (SpawnManager.AddNpcSpawner).
        let fake = nsn(9_000_001, 9_000_001, 777);
        assert!(d.add_npc_spawner_npc(fake.clone()));
        assert!(!d.add_npc_spawner_npc(fake.clone()));
        d.add_member_and_spawner_template_ids(&fake);
        assert!(d.add_npc_spawner(NpcSpawnerTemplate {
            npcs: vec![fake],
            ..tpl(9_000_001)
        }));
        assert_eq!(d.get_spawner_ids(777), Some(vec![9_000_001]));

        let mut n = d.get_npc_spawner_npc(2).unwrap();
        n.member_id = 999;
        d.replace_template_npcs(2, n);
        let t = d.get_npc_spawner_template(2).unwrap();
        assert_eq!(t.npcs.len(), 1);
        assert_eq!(t.npcs[0].member_id, 999);
        assert_eq!(d.get_npc_spawner_npc(2).unwrap().member_id, 999);
    }

    #[test]
    fn template_defaults() {
        let t = NpcSpawnerTemplate::with_member(5, 77);
        assert_eq!(t.max_population, 1);
        assert_eq!(t.spawn_delay_min, 5.0);
        assert_eq!(t.spawn_delay_max, 10.0);
        assert_eq!(t.test_radius_npc, 3.0);
        assert_eq!(t.npcs[0].member_type, "Npc");
        assert_eq!(t.npcs[0].member_id, 77);
        assert_eq!(NpcSpawnerTemplate::with_spawner(5).npcs[0].member_type, "");
    }

    #[test]
    #[ignore]
    fn real_compact_db_npc_spawners() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let conn = Connection::open(path).unwrap();
        let d = NpcSpawnerData::load_from(&conn).unwrap();
        assert!(d.template_count() > 10_000, "{}", d.template_count());
        assert!(d.member_count() > 10_000, "{}", d.member_count());
        assert!(
            d.get_npc_spawner_template(1).is_some(),
            "Test Warrior template"
        );
        assert!(!d.transfer_template_ids.is_empty());
        assert!(!d.slave_template_ids.is_empty());
        println!(
            "npc_spawners={} npc_spawner_npcs={} transfers={} slaves={}",
            d.template_count(),
            d.member_count(),
            d.transfer_template_ids.len(),
            d.slave_template_ids.len()
        );
    }
}

#[cfg(test)]
#[path = "npc_game_data_tests.rs"]
mod npc_game_data_tests;
