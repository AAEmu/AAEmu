//! `SubZoneManager` (`Core/Managers/SubZoneManager.cs`): the sub zone and housing areas of
//! every world (`level_design/zone/<key>/client/{subzone_area,housing_area}.xml`, stored in
//! `WorldTemplate.SubZones` / `HousingZones` in C#), the position lookups, `Point.IsInside`, and
//! `CSNotifySubZonePacket`.
//!
//! Each XML area retains its own id/polygon. Position lookups use database zone ids and an
//! unbounded polygon crossing test, including points on polygon edges.

use std::collections::HashMap;
use std::sync::Arc;

use aaemu_commons::PacketStream;
use futures::future::BoxFuture;
use tracing::{debug, info, warn};

use crate::data::client_files::ClientFileManager;
use crate::data::world_data::ZoneData;
use crate::data::world_templates::{WorldTemplate, WorldTemplates};
use crate::data::GameData;
use crate::error::Result;
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::{ClientPacket, PacketRegistry};
use crate::opcodes::cs;
use crate::server::GameServer;
use crate::systems::DataLoadCtx;

/// `Area` (a sub zone / housing polygon).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct Area {
    pub id: u32,
    pub name: String,
    /// `Points` (world coordinates).
    pub points: Vec<[f32; 3]>,
}

/// Areas of one world template by zone id.
pub type ZoneAreas = HashMap<u32, Vec<Area>>;

/// `WorldTemplate.SubZones` / `HousingZones` of every world template
/// (`server.game_data.systems.npcs.subzone`).
#[derive(Debug, Default)]
pub struct SubZoneData {
    /// World template id → zone id → sub zones.
    pub sub_zones: HashMap<u32, ZoneAreas>,
    /// World template id → zone id → housing areas.
    pub housing_zones: HashMap<u32, ZoneAreas>,
}

/// `float.Parse(s, CultureInfo.InvariantCulture)` (Float | AllowThousands).
fn parse_float(s: &str) -> Option<f32> {
    let t = s.trim_matches(|c: char| matches!(c, '\t'..='\r' | ' '));
    if t.is_empty() {
        return None;
    }
    t.replace(',', "").parse().ok()
}

/// `uint.Parse(s)` (Integer style: surrounding white space, leading sign).
fn parse_uint(s: &str) -> Option<u32> {
    let t = s.trim_matches(|c: char| matches!(c, '\t'..='\r' | ' '));
    let t = t.strip_prefix('+').unwrap_or(t);
    if let Some(d) = t.strip_prefix('-') {
        return (!d.is_empty() && d.bytes().all(|b| b == b'0')).then_some(0);
    }
    t.parse().ok()
}

/// One `subzone_area.xml` / `housing_area.xml` file: the `Area` entries (in C# list order)
/// for a zone whose origin cell is `origin`. `Err` = the C# throws (invalid XML, bad `Id`).
pub fn parse_area_file(
    contents: &str,
    origin: (i32, i32),
) -> std::result::Result<Vec<Area>, String> {
    parse_area_file_kind(contents, origin, false)
}

/// 1.2 housing XML puts `housing_areas.id` in `value1`; `Id` belongs to the engine trigger.
pub fn parse_housing_area_file(
    contents: &str,
    origin: (i32, i32),
) -> std::result::Result<Vec<Area>, String> {
    parse_area_file_kind(contents, origin, true)
}

fn parse_area_file_kind(
    contents: &str,
    origin: (i32, i32),
    housing: bool,
) -> std::result::Result<Vec<Area>, String> {
    let doc = roxmltree::Document::parse(contents).map_err(|e| e.to_string())?;
    let root = doc.root_element();
    let mut out = Vec::new();
    if root.tag_name().name() != "Objects" {
        return Ok(out);
    }
    for block in root
        .children()
        .filter(|n| n.is_element() && n.tag_name().name() == "Entity")
    {
        let Some(block_name) = block.attribute("Name") else {
            continue;
        };
        let cell_x_offset = block
            .attribute("cellX")
            .and_then(|v| v.trim().parse::<i32>().ok())
            .unwrap_or(0);
        let cell_y_offset = block
            .attribute("cellY")
            .and_then(|v| v.trim().parse::<i32>().ok())
            .unwrap_or(0);
        for area_node in block
            .children()
            .filter(|n| n.is_element() && n.tag_name().name() == "Area")
        {
            let mut template = Area {
                name: block_name.to_string(),
                ..Default::default()
            };
            let id = if housing {
                area_node
                    .attribute("value1")
                    .or_else(|| area_node.attribute("Id"))
            } else {
                area_node.attribute("Id")
            };
            if let Some(id) = id {
                template.id = parse_uint(id).ok_or_else(|| format!("invalid area Id {id:?}"))?;
            }
            let mut start = [0f32; 3];
            if let Some(val_pos) = block.attribute("Pos") {
                let v: Vec<&str> = val_pos.split(',').collect();
                if v.len() != 3 {
                    continue;
                }
                match (parse_float(v[0]), parse_float(v[1]), parse_float(v[2])) {
                    (Some(x), Some(y), Some(z)) => start = [x, y, z],
                    _ => debug!("Invalid float inside Pos: {val_pos}"),
                }
            }
            let cell_offset = (
                (origin.0 + cell_x_offset) as f32 * 1024f32,
                (origin.1 + cell_y_offset) as f32 * 1024f32,
            );
            for points in area_node
                .children()
                .filter(|n| n.is_element() && n.tag_name().name() == "Points")
            {
                for point in points
                    .children()
                    .filter(|n| n.is_element() && n.tag_name().name() == "Point")
                {
                    let Some(pos) = point.attribute("Pos") else {
                        continue;
                    };
                    let v: Vec<&str> = pos.split(',').collect();
                    if v.len() != 3 {
                        debug!("Invalid number of values inside Pos: {pos}");
                        continue;
                    }
                    match (parse_float(v[0]), parse_float(v[1]), parse_float(v[2])) {
                        (Some(x), Some(y), Some(z)) => template.points.push([
                            x + cell_offset.0 + start[0],
                            y + cell_offset.1 + start[1],
                            z + start[2],
                        ]),
                        _ => debug!("Invalid float inside Pos: {pos}"),
                    }
                }
            }
            out.push(template);
        }
    }
    Ok(out)
}

fn load_areas(
    files: &ClientFileManager,
    world: &WorldTemplate,
    zone_key: u32,
    file_name: &str,
    origin: (i32, i32),
) -> std::result::Result<Vec<Area>, String> {
    let dir = format!(
        "game/worlds/{}/level_design/zone/{zone_key}/client",
        world.name
    );
    let mut out = Vec::new();
    for path in files.files_in_directory(&dir, file_name, true) {
        let contents = files.read_to_string(&path);
        if contents.trim().is_empty() {
            warn!("{path} doesn't exists or is empty.");
            continue;
        }
        out.extend(
            parse_area_file_kind(&contents, origin, file_name == "housing_area.xml")
                .map_err(|e| format!("{path}: {e}"))?,
        );
    }
    Ok(out)
}

impl SubZoneData {
    /// `SubZoneManager.Load` (`#region LoadClientData`). The world templates and zones are read
    /// again from `ctx` (the subsystem loaders do not see the other game data); no client worlds
    /// → nothing (C#: `GetAllWorldTemplates()` empty).
    pub fn load(ctx: &DataLoadCtx<'_>) -> std::result::Result<Self, crate::data::GameDataError> {
        let worlds = match WorldTemplates::load(ctx.files, ctx.app_path) {
            Ok(w) => w,
            Err(e) => {
                debug!("SubZoneManager: no world templates ({e})");
                return Ok(Self::default());
            }
        };
        let zones = ZoneData::load(ctx.conn, 0)?;
        Self::load_from(ctx.files, &worlds, &zones).map_err(crate::data::GameDataError::File)
    }

    /// `SubZoneManager.Load` over explicit world templates / zones.
    pub fn load_from(
        files: &ClientFileManager,
        worlds: &WorldTemplates,
        zones: &ZoneData,
    ) -> std::result::Result<Self, String> {
        let mut d = Self::default();
        for world in worlds.templates() {
            for &zone_key in worlds.get_zone_keys_by_world_id(world.id) {
                let Some(zone) = zones.zone_by_key(zone_key) else {
                    let zone_name = world
                        .xml_world_zones()
                        .get(&zone_key)
                        .map(|z| z.name.clone())
                        .unwrap_or_default();
                    debug!(
                        "XML ZoneKey {zone_key} ({zone_name}) in {} does not exist in database (unused area)",
                        world.name
                    );
                    continue;
                };
                // ZoneManager.GetZoneOriginCell(zone.ZoneKey)
                let origin = zone_origin_cell(worlds, zone.zone_key);
                for (file, target) in [
                    ("subzone_area.xml", &mut d.sub_zones),
                    ("housing_area.xml", &mut d.housing_zones),
                ] {
                    let areas = load_areas(files, world, zone.zone_key, file, origin)?;
                    if !areas.is_empty() {
                        target
                            .entry(world.id)
                            .or_default()
                            .entry(zone.id)
                            .or_default()
                            .extend(areas);
                    }
                }
            }
        }
        let count = |m: &HashMap<u32, ZoneAreas>| {
            m.values()
                .flat_map(|z| z.values())
                .map(Vec::len)
                .sum::<usize>()
        };
        info!(
            "Loaded {} sub zone areas, {} housing areas",
            count(&d.sub_zones),
            count(&d.housing_zones)
        );
        Ok(d)
    }

    /// `worldTemplate.SubZones[zoneId]`.
    pub fn sub_zones_of(&self, world_template_id: u32, zone_id: u32) -> &[Area] {
        self.sub_zones
            .get(&world_template_id)
            .and_then(|z| z.get(&zone_id))
            .map_or(&[], Vec::as_slice)
    }
}

/// `ZoneManager.GetZoneOriginCell(zoneKey)`: the zone's origin cell, (0, 0) when unknown.
pub fn zone_origin_cell(worlds: &WorldTemplates, zone_key: u32) -> (i32, i32) {
    worlds
        .get_world_template_by_zone_key(zone_key)
        .and_then(|w| w.xml_world_zones().get(&zone_key))
        .map_or((0, 0), |z| (z.origin_x, z.origin_y))
}

// ---------------------------------------------------------------------------------------------
// Point.IsInside
// ---------------------------------------------------------------------------------------------

/// Polygon crossing test. World coordinates commonly exceed x=1000; a fixed ray endpoint
/// can sit inside the polygon and invert its answer. Horizontal edges are handled separately.
pub fn is_inside(polygon: &[[f32; 3]], p: [f32; 3]) -> bool {
    if polygon.len() < 3
        || !p[0].is_finite()
        || !p[1].is_finite()
        || polygon
            .iter()
            .any(|v| !v[0].is_finite() || !v[1].is_finite())
    {
        return false;
    }
    let (x, y) = (f64::from(p[0]), f64::from(p[1]));
    let mut inside = false;
    for (a, b) in polygon
        .iter()
        .zip(polygon.iter().cycle().skip(1))
        .take(polygon.len())
    {
        let (ax, ay, bx, by) = (
            f64::from(a[0]),
            f64::from(a[1]),
            f64::from(b[0]),
            f64::from(b[1]),
        );
        if (x - ax) * (by - ay) == (y - ay) * (bx - ax)
            && x >= ax.min(bx)
            && x <= ax.max(bx)
            && y >= ay.min(by)
            && y <= ay.max(by)
        {
            return true;
        }
        if (ay > y) != (by > y) && x < ax + (y - ay) * (bx - ax) / (by - ay) {
            inside = !inside;
        }
    }
    inside
}

// ---------------------------------------------------------------------------------------------
// Lookups
// ---------------------------------------------------------------------------------------------

/// `SubZoneManager.GetSubZoneByPosition(worldTemplate, x, y)`: the sub zone ids containing the
/// position.
pub fn get_sub_zone_by_position(
    data: &GameData,
    world_template: &WorldTemplate,
    x: f32,
    y: f32,
) -> Vec<u32> {
    let zone_key = data.worlds.get_zone_id(Some(world_template), x, y);
    let mut found = Vec::new();
    let Some(zone) = data.zones.zone_by_key(zone_key) else {
        return found;
    };
    for sub_zone in data
        .systems
        .npcs
        .subzone
        .sub_zones_of(world_template.id, zone.id)
    {
        if is_inside(&sub_zone.points, [x, y, 0.0]) {
            found.push(sub_zone.id);
        }
    }
    if found.is_empty() {
        debug!(
            "No subzone found at this position! WorldId: {}, Pos: {x} , {y}",
            world_template.id
        );
    }
    found
}

/// Housing area ids containing a position. A zone without housing areas is an empty result.
pub fn get_housing_zone_by_position(
    data: &GameData,
    world_template: &WorldTemplate,
    x: f32,
    y: f32,
) -> std::result::Result<Vec<u32>, String> {
    let zone_key = data.worlds.get_zone_id(Some(world_template), x, y);
    let Some(zone) = data.zones.zone_by_key(zone_key) else {
        return Ok(Vec::new());
    };
    let zone_id = zone.id;
    let Some(list) = data
        .systems
        .npcs
        .subzone
        .housing_zones
        .get(&world_template.id)
        .and_then(|z| z.get(&zone_id))
    else {
        return Ok(Vec::new());
    };
    let mut found = Vec::new();
    for area in list {
        if is_inside(&area.points, [x, y, 0.0]) {
            debug!(
                "Is in zone {zone_id} housezone name {} ({})",
                area.name, area.id
            );
            found.push(area.id);
        }
    }
    if found.is_empty() {
        debug!("No housing zone found at this position!");
    }
    Ok(found)
}

// ---------------------------------------------------------------------------------------------
// CSNotifySubZonePacket
// ---------------------------------------------------------------------------------------------

/// `CSNotifySubZonePacket` (level 1): the client entered a sub zone.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSNotifySubZonePacket {
    pub sub_zone_id: u32,
}

impl ClientPacket for CSNotifySubZonePacket {
    const NAME: &'static str = "CSNotifySubZonePacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            sub_zone_id: s.read_u32()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            if self.sub_zone_id == 0 {
                return Ok(());
            }
            let Some(player) = conn.active_char.clone() else {
                return Ok(());
            };
            let (name, id) = {
                let mut c = player.data();
                // needed to store Memory Tome points for Recall
                c.sub_zone_id = self.sub_zone_id;
                (c.unit.name.clone(), c.id)
            };
            info!("Enter RegionId: {} by {name} ({id})", self.sub_zone_id);
            crate::systems::npcs::portal::notify_sub_zone(server, &player, self.sub_zone_id);
            Ok(())
        })
    }
}

/// Startup (nothing: the areas are game data).
pub async fn start(_server: &Arc<GameServer>) -> anyhow::Result<()> {
    Ok(())
}

/// C->S packets.
pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSNotifySubZonePacket>(LEVEL_PLAIN, cs::CSNotifySubZonePacket);
}

#[cfg(test)]
mod tests {
    use super::*;

    const XML: &str = r#"<?xml version="1.0"?>
<Objects>
  <Entity Name="sub_a" Pos="10,20,0" cellX="1" cellY="0">
    <Area Id="5"><Points><Point Pos="0,0,0"/><Point Pos="100,0,0"/><Point Pos="100,100,0"/><Point Pos="0,100,1"/></Points></Area>
    <Area Id="7"><Points><Point Pos="1,1,1"/><Point Pos="bad,1,1"/></Points></Area>
  </Entity>
  <Entity Name="bad_pos" Pos="1,2">
    <Area Id="9"><Points><Point Pos="0,0,0"/></Points></Area>
  </Entity>
  <Entity NoName="x"><Area Id="3"/></Entity>
</Objects>"#;

    #[test]
    fn parses_independent_xml_areas() {
        let areas = parse_area_file(XML, (2, 3)).unwrap();
        assert_eq!(areas.len(), 2); // sub_a twice, bad_pos none
        assert_eq!(areas[0].name, "sub_a");
        assert_eq!((areas[0].id, areas[0].points.len()), (5, 4));
        assert_eq!((areas[1].id, areas[1].points.len()), (7, 1));
        // origin (2,3) + cellX 1 -> x offset 3072, y 3072; + entity Pos (10, 20, 0)
        assert_eq!(areas[0].points[0], [3082.0, 3092.0, 0.0]);
        assert_eq!(areas[0].points[3], [3082.0, 3192.0, 1.0]);
        assert!(parse_area_file(
            "<Objects><Entity Name='a'><Area Id='x'/></Entity></Objects>",
            (0, 0)
        )
        .is_err());
        assert!(parse_area_file("<Objects>", (0, 0)).is_err());
    }

    #[test]
    fn housing_uses_database_area_id_in_value1_not_engine_trigger_id() {
        let xml = "<Objects><Entity Name='Shape123' Pos='0,0,0'><Area Id='68' Group='1' value1='37'><Points><Point Pos='0,0,0'/><Point Pos='20,0,0'/><Point Pos='20,20,0'/></Points></Area></Entity></Objects>";
        assert_eq!(parse_area_file(xml, (0, 0)).unwrap()[0].id, 68);
        assert_eq!(parse_housing_area_file(xml, (0, 0)).unwrap()[0].id, 37);
    }

    #[test]
    fn point_in_polygon() {
        let square = [
            [0.0, 0.0, 0.0],
            [100.0, 0.0, 0.0],
            [100.0, 100.0, 0.0],
            [0.0, 100.0, 0.0],
        ];
        assert!(is_inside(&square, [50.0, 50.0, 0.0]));
        assert!(!is_inside(&square, [150.0, 50.0, 0.0]));
        assert!(!is_inside(&square, [-5.0, 50.0, 0.0]));
        assert!(is_inside(&square, [100.0, 50.0, 0.0])); // on an edge
        assert!(!is_inside(&square[..2], [50.0, 0.0, 0.0]));
        // Housing polygons can straddle the old fixed ray endpoint, or lie far beyond it.
        let across = [
            [900.0, 0.0, 0.0],
            [1100.0, 0.0, 0.0],
            [1100.0, 100.0, 0.0],
            [900.0, 100.0, 0.0],
        ];
        assert!(is_inside(&across, [1050.0, 50.0, 0.0]));
        let far = across.map(|[x, y, z]| [x + 15000.0, y, z]);
        assert!(is_inside(&far, [16050.0, 50.0, 0.0]));
        assert!(!is_inside(&far, [17000.0, 50.0, 0.0]));
        assert!(!is_inside(&far, [f32::NAN, 50.0, 0.0]));
    }

    #[test]
    fn notify_sub_zone_reads_u32() {
        let mut s = PacketStream::from(vec![0x2A, 0, 0, 0]);
        assert_eq!(
            CSNotifySubZonePacket::read(&mut s).unwrap(),
            CSNotifySubZonePacket { sub_zone_id: 42 }
        );
    }
}

#[cfg(test)]
mod real_data_tests {
    //! `AAEMU_COMPACT_DB=... AAEMU_GAME_PAK=... cargo test -p aaemu-game real_subzone --
    //! --ignored --nocapture`.
    use rusqlite::{Connection, OpenFlags};

    use super::*;

    fn count(m: &HashMap<u32, ZoneAreas>) -> usize {
        m.values().flat_map(|z| z.values()).map(Vec::len).sum()
    }

    #[test]
    #[ignore = "needs AAEMU_COMPACT_DB and AAEMU_GAME_PAK"]
    fn real_housing_placement_polygons_resolve_original_client_area_tags() {
        let (Ok(path), Ok(pak)) = (
            std::env::var("AAEMU_COMPACT_DB"),
            std::env::var("AAEMU_GAME_PAK"),
        ) else {
            return;
        };
        let conn = Connection::open_with_flags(&path, OpenFlags::SQLITE_OPEN_READ_ONLY).unwrap();
        let app = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("app");
        let files = ClientFileManager::from_sources(&[pak], &app);
        let ctx = DataLoadCtx {
            conn: &conn,
            app_path: &app,
            files: &files,
            language: "en_us",
        };
        let mut data = GameData::empty();
        data.worlds = WorldTemplates::load(&files, &app).unwrap();
        data.zones = ZoneData::load(&conn, 0).unwrap();
        data.systems.housing = crate::systems::housing::Data::load(&ctx).unwrap();
        data.systems.npcs.subzone = SubZoneData::load(&ctx).unwrap();
        let main = data
            .worlds
            .get_world_template_by_name("main_world")
            .unwrap();
        // Original client Shape126 in zone142 uses engine Id=0 and housing database value1=52.
        let ids = get_housing_zone_by_position(&data, main, 13944.868, 14216.393).unwrap();
        assert!(ids.contains(&52), "unexpected housing ids: {ids:?}");
        assert_eq!(data.systems.housing.areas.get(&52), Some(&1));
        assert!(data.systems.housing.group_categories[&1].contains(&1));
        let count = data.systems.npcs.subzone.housing_zones[&main.id]
            .values()
            .flatten()
            .filter(|a| data.systems.housing.areas.contains_key(&a.id))
            .count();
        assert!(
            count > 200,
            "only {count} housing polygons resolve their 1.2 eligibility records"
        );
    }

    #[test]
    #[ignore = "needs AAEMU_COMPACT_DB and AAEMU_GAME_PAK"]
    fn real_subzones() {
        let (Ok(path), Ok(pak)) = (
            std::env::var("AAEMU_COMPACT_DB"),
            std::env::var("AAEMU_GAME_PAK"),
        ) else {
            return;
        };
        let conn = Connection::open_with_flags(&path, OpenFlags::SQLITE_OPEN_READ_ONLY).unwrap();
        let app = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("app");
        let files = ClientFileManager::from_sources(&[pak], &app);
        let ctx = DataLoadCtx {
            conn: &conn,
            app_path: &app,
            files: &files,
            language: "en_us",
        };
        let started = std::time::Instant::now();
        let d = SubZoneData::load(&ctx).unwrap();
        eprintln!(
            "sub zone areas {} in {} zones, housing areas {} ({:?})",
            count(&d.sub_zones),
            d.sub_zones.values().map(HashMap::len).sum::<usize>(),
            count(&d.housing_zones),
            started.elapsed()
        );
        assert!(count(&d.sub_zones) > 0);
        // The centroid of a sub zone polygon lies inside it (convex enough areas).
        let worlds = WorldTemplates::load(&files, &app).unwrap();
        let main_id = worlds.get_world_template_by_name("main_world").unwrap().id;
        let mut data = GameData::empty();
        data.zones = ZoneData::load(&conn, 0).unwrap();
        data.worlds = worlds;
        data.systems.npcs.subzone = d;
        let main = data.worlds.get_world_template_by_id(main_id).unwrap();
        let mut hits = 0;
        let mut tried = 0;
        for list in data.systems.npcs.subzone.sub_zones[&main_id].values() {
            for area in list.iter().take(3) {
                if area.points.len() < 3 {
                    continue;
                }
                let c = area
                    .points
                    .iter()
                    .fold([0f32; 2], |a, p| [a[0] + p[0], a[1] + p[1]]);
                let n = area.points.len() as f32;
                tried += 1;
                if get_sub_zone_by_position(&data, main, c[0] / n, c[1] / n).contains(&area.id) {
                    hits += 1;
                }
            }
        }
        eprintln!("centroid lookups: {hits}/{tried} found their own sub zone");
        assert!(hits * 2 > tried);
    }
}
