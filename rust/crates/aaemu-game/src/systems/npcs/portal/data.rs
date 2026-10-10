//! `PortalManager.Load`: the parts not already loaded by `data::world_data::PortalData`
//! (`recalls.json` + `district_return_points`, phase 2): the recall id index, `respawns.json`,
//! `worldgates.json`, `open_portal_inland_reagents`, `open_portal_outland_reagents`.

use std::collections::HashMap;
use std::path::Path;

use crate::data::sqlite::for_each_row;
use crate::data::world_data::{parse_lenient_json, Portal};
use crate::data::GameDataError;
use crate::systems::DataLoadCtx;

/// `OpenPortalReagents`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct OpenPortalReagents {
    pub id: u32,
    pub open_portal_effect_id: u32,
    pub item_id: u32,
    pub amount: i32,
    pub priority: i32,
}

/// A `Dictionary<uint, Portal>` keyed by sub zone plus its `id -> sub zone` key index.
#[derive(Debug, Clone, Default)]
pub struct PortalIndex {
    /// `.Values` (insertion order).
    pub portals: Vec<Portal>,
    by_sub_zone: HashMap<u32, usize>,
    /// `_respawnsKey` / `_worldGatesKey`.
    key: HashMap<u32, u32>,
}

impl PortalIndex {
    /// `_x.Add(portal.SubZoneId, portal); _xKey.Add(portal.Id, portal.SubZoneId)` (both throw on
    /// duplicates, which aborts `PortalManager.Load`).
    fn add(&mut self, what: &str, portal: Portal) -> Result<(), GameDataError> {
        if self.by_sub_zone.contains_key(&portal.sub_zone_id) {
            return Err(GameDataError::File(format!(
                "PortalManager: duplicate {what} sub zone {}",
                portal.sub_zone_id
            )));
        }
        if self.key.contains_key(&portal.id) {
            return Err(GameDataError::File(format!(
                "PortalManager: duplicate {what} id {}",
                portal.id
            )));
        }
        self.by_sub_zone
            .insert(portal.sub_zone_id, self.portals.len());
        self.key.insert(portal.id, portal.sub_zone_id);
        self.portals.push(portal);
        Ok(())
    }

    /// `GetXBySubZoneId`.
    pub fn by_sub_zone(&self, sub_zone_id: u32) -> Option<&Portal> {
        self.by_sub_zone
            .get(&sub_zone_id)
            .map(|&i| &self.portals[i])
    }

    /// `GetXById`: `_xKey[id]` -> `_x.GetValueOrDefault(key)`.
    pub fn by_id(&self, id: u32) -> Option<&Portal> {
        self.key.get(&id).and_then(|sz| self.by_sub_zone(*sz))
    }

    pub fn len(&self) -> usize {
        self.portals.len()
    }

    pub fn is_empty(&self) -> bool {
        self.portals.is_empty()
    }
}

/// Game-data tables of `PortalManager` (the recall portals themselves and the district return
/// points live in `server.game_data.portals`).
#[derive(Debug, Clone, Default)]
pub struct PortalTables {
    /// `_recallsKey` (return point id -> sub zone; the first recall with an id wins).
    pub recalls_key: HashMap<u32, u32>,
    /// `_respawns` / `_respawnsKey`. `ZoneId` is filled at lookup time (`GetZoneId(main_world,
    /// X, Y)` needs the world templates, see `portal::respawn_zone`).
    pub respawns: PortalIndex,
    /// `_worldGates` / `_worldGatesKey`.
    pub world_gates: PortalIndex,
    /// `_openPortalInlandReagents.Values` (insertion order).
    pub open_portal_inland_reagents: Vec<OpenPortalReagents>,
    /// `_openPortalOutlandReagents.Values`.
    pub open_portal_outland_reagents: Vec<OpenPortalReagents>,
}

/// Reads one `Data/Portal/*.json` like `PortalManager.Load`: missing / empty files and parse
/// failures are fatal.
fn read_portal_file(app_path: &Path, name: &str) -> Result<Vec<Portal>, GameDataError> {
    let path = app_path.join("Data").join("Portal").join(name);
    let text = std::fs::read_to_string(&path).map_err(|e| {
        GameDataError::File(format!("File {} doesn't exists ! ({e})", path.display()))
    })?;
    if text.trim().is_empty() {
        return Err(GameDataError::File(format!(
            "File {} is empty !",
            path.display()
        )));
    }
    parse_lenient_json(&text).map_err(|e| {
        GameDataError::File(format!(
            "PortalManager: Parse {} file ({e})",
            path.display()
        ))
    })
}

fn load_reagents(
    conn: &rusqlite::Connection,
    table: &str,
) -> Result<Vec<OpenPortalReagents>, GameDataError> {
    let mut out: Vec<OpenPortalReagents> = Vec::new();
    let mut dup = None;
    for_each_row(conn, &format!("SELECT * FROM {table}"), [], |r| {
        let t = OpenPortalReagents {
            id: r.u32("id")?,
            open_portal_effect_id: r.u32("open_portal_effect_id")?,
            item_id: r.u32("item_id")?,
            amount: r.i32("amount")?,
            priority: r.i32("priority")?,
        };
        if out.iter().any(|x| x.id == t.id) {
            dup.get_or_insert(t.id);
        }
        out.push(t);
        Ok(())
    })?;
    if let Some(id) = dup {
        // `Dictionary.Add` throws.
        return Err(GameDataError::File(format!(
            "PortalManager: duplicate {table} id {id}"
        )));
    }
    Ok(out)
}

impl PortalTables {
    /// `PortalManager.Load` (the parts not in `PortalData`).
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, GameDataError> {
        let mut d = PortalTables::default();
        for recall in read_portal_file(ctx.app_path, "recalls.json")? {
            d.recalls_key.entry(recall.id).or_insert(recall.sub_zone_id);
        }
        for respawn in read_portal_file(ctx.app_path, "respawns.json")? {
            d.respawns.add("respawn", respawn)?;
        }
        tracing::info!("Loaded {} Respawn Portals", d.respawns.len());
        for gate in read_portal_file(ctx.app_path, "worldgates.json")? {
            d.world_gates.add("worldgate", gate)?;
        }
        tracing::info!("Loaded {} Worldgate Portals", d.world_gates.len());
        d.open_portal_inland_reagents = load_reagents(ctx.conn, "open_portal_inland_reagents")?;
        d.open_portal_outland_reagents = load_reagents(ctx.conn, "open_portal_outland_reagents")?;
        tracing::info!("Loaded Portal Info");
        Ok(d)
    }

    /// Synthetic tables (tests).
    pub fn from_parts(
        respawns: Vec<Portal>,
        world_gates: Vec<Portal>,
        inland: Vec<OpenPortalReagents>,
        outland: Vec<OpenPortalReagents>,
    ) -> Result<Self, GameDataError> {
        let mut d = PortalTables::default();
        for p in respawns {
            d.respawns.add("respawn", p)?;
        }
        for p in world_gates {
            d.world_gates.add("worldgate", p)?;
        }
        d.open_portal_inland_reagents = inland;
        d.open_portal_outland_reagents = outland;
        Ok(d)
    }
}
