//! `SubZoneManager` (`Core/Managers/SubZoneManager.cs`): the sub zone and housing areas of
//! every world (`level_design/zone/<key>/client/{subzone_area,housing_area}.xml`, stored in
//! `WorldTemplate.SubZones` / `HousingZones` in C#), the position lookups, `Point.IsInside`, and
//! `CSNotifySubZonePacket`.
//!
//! C# quirks mirrored: one `Area` object is created per `<Entity>` and added once per `<Area>`
//! child, so every copy has the last child's `Id` and the points of all children; an `Entity`
//! whose `Pos` does not have 3 values adds nothing; `Point.IsInside` casts its ray to x = 1000
//! (not infinity); `GetHousingZoneByPosition` indexes `HousingZones` (keyed by zone id) with the
//! zone *key* and throws when absent.

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
        let mut template = Area {
            name: block_name.to_string(),
            ..Default::default()
        };
        let mut adds = 0usize;
        for area_node in block
            .children()
            .filter(|n| n.is_element() && n.tag_name().name() == "Area")
        {
            if let Some(id) = area_node.attribute("Id") {
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
            adds += 1;
        }
        // The same Area object is added once per <Area> node.
        for _ in 0..adds {
            out.push(template.clone());
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
        out.extend(parse_area_file(&contents, origin).map_err(|e| format!("{path}: {e}"))?);
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

fn on_segment(p: [f32; 3], q: [f32; 3], r: [f32; 3]) -> bool {
    q[0] <= p[0].max(r[0])
        && q[0] >= p[0].min(r[0])
        && q[1] <= p[1].max(r[1])
        && q[1] >= p[1].min(r[1])
}

/// `FindTripletOrientation`: 0 colinear, 1 clockwise, 2 counterclockwise.
fn orientation(p: [f32; 3], q: [f32; 3], r: [f32; 3]) -> i32 {
    let val = (q[1] - p[1]) * (r[0] - q[0]) - (q[0] - p[0]) * (r[1] - q[1]);
    if val == 0.0 {
        0
    } else if val > 0.0 {
        1
    } else {
        2
    }
}

fn is_line_intersection(l1: ([f32; 3], [f32; 3]), l2: ([f32; 3], [f32; 3])) -> bool {
    let o1 = orientation(l1.0, l1.1, l2.0);
    let o2 = orientation(l1.0, l1.1, l2.1);
    let o3 = orientation(l2.0, l2.1, l1.0);
    let o4 = orientation(l2.0, l2.1, l1.1);
    if o1 != o2 && o3 != o4 {
        return true;
    }
    (o1 == 0 && on_segment(l1.0, l2.0, l1.1))
        || (o2 == 0 && on_segment(l1.0, l2.1, l1.1))
        || (o3 == 0 && on_segment(l2.0, l1.0, l2.1))
        || (o4 == 0 && on_segment(l2.0, l1.1, l2.1))
}

/// `Point.IsInside(polygon, n, p)` (ray to x = 1000, as C#).
pub fn is_inside(polygon: &[[f32; 3]], p: [f32; 3]) -> bool {
    let n = polygon.len();
    if n < 3 {
        return false;
    }
    let extreme = [1000.0, p[1], 0.0];
    let mut count = 0;
    let mut i = 0;
    loop {
        let next = (i + 1) % n;
        if is_line_intersection((polygon[i], polygon[next]), (p, extreme)) {
            if orientation(polygon[i], p, polygon[next]) == 0 {
                return on_segment(polygon[i], p, polygon[next]);
            }
            count += 1;
        }
        i = next;
        if i == 0 {
            break;
        }
    }
    count % 2 == 1
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

/// `SubZoneManager.GetHousingZoneByPosition(world, x, y)`. `Err` = the C#
/// `KeyNotFoundException` (`HousingZones` is keyed by zone id but indexed with the zone key).
pub fn get_housing_zone_by_position(
    data: &GameData,
    world_template: &WorldTemplate,
    x: f32,
    y: f32,
) -> std::result::Result<Vec<u32>, String> {
    let zone_id = data.worlds.get_zone_id(Some(world_template), x, y);
    let list = data
        .systems
        .npcs
        .subzone
        .housing_zones
        .get(&world_template.id)
        .and_then(|z| z.get(&zone_id))
        .ok_or_else(|| format!("The given key '{zone_id}' was not present in the dictionary."))?;
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
    fn parses_areas_with_csharp_sharing() {
        let areas = parse_area_file(XML, (2, 3)).unwrap();
        assert_eq!(areas.len(), 2); // sub_a twice, bad_pos none
        for a in &areas {
            assert_eq!(a.name, "sub_a");
            assert_eq!(a.id, 7); // last Area Id
            assert_eq!(a.points.len(), 5); // 4 + 1 (one invalid)
        }
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
        // C# quirk: the "ray" is the segment to x = 1000; it misses this polygon's edges.
        let across = [
            [900.0, 0.0, 0.0],
            [1100.0, 0.0, 0.0],
            [1100.0, 100.0, 0.0],
            [900.0, 100.0, 0.0],
        ];
        assert!(!is_inside(&across, [1050.0, 50.0, 0.0]));
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
    fn real_subzones() {
        let (Ok(path), Ok(pak)) = (
            std::env::var("AAEMU_COMPACT_DB"),
            std::env::var("AAEMU_GAME_PAK"),
        ) else {
            return;
        };
        let conn = Connection::open_with_flags(&path, OpenFlags::SQLITE_OPEN_READ_ONLY).unwrap();
        let app = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../../AAEmu.Game");
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
