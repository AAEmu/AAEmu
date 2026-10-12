//! World templates loaded from the client's `game/worlds/*/world.xml` files.
//!
//! Ports the client-data part of `WorldManager.Load()` (`#region LoadClientData`),
//! `WorldManager.CreateWorldTemplate`, the zone/world lookups (`GetZoneId`,
//! `GetWorldTemplateByName`, `GetWorldIdByZoneKey`, `GetWorldTemplateByZoneKey`,
//! `GetZoneKeysByWorldId`, `DefaultWorldTemplateId`), the data parts of `WorldTemplate`, the
//! `Xml/XmlWorld*.cs` readers, `XmlHelper.ReadAttribute`, `WorldSpawnPosition` and
//! `WorldSpawnLocation`.
//!
//! The other `WorldManager` parts live with their subsystems: heightmaps / `WorldCell`
//! (`systems::npcs::heightmap`), geodata and BAI navmesh loaders (`systems::ai::geodata`), water
//! (`systems::ai::physics::water`), sub zones / housing zones (`systems::npcs::subzone`) and the
//! `LoadServerDB` spawns (`systems::npcs::spawn_manager`).

use std::collections::{BTreeMap, HashMap};
use std::path::{Path, MAIN_SEPARATOR};

use serde_json::Value;
use tracing::{error, info};

use super::client_files::{decode_text, ClientFileManager};

/// `WorldManager.CELL_SIZE`: cell size in meters.
pub const CELL_SIZE: i32 = 1024;
/// `WorldManager.REGION_SIZE`: region (sector) size in meters.
pub const REGION_SIZE: i32 = 64;
/// `WorldManager.SECTORS_PER_CELL`.
pub const SECTORS_PER_CELL: i32 = CELL_SIZE / REGION_SIZE;

/// Upper bound on `cellXCount * cellYCount * 256` regions; C# would throw an
/// `OutOfMemoryException`/`OverflowException` long before this matters.
const MAX_REGIONS: usize = 1 << 28;

/// Errors from loading world templates. Every variant corresponds to an exception that aborts
/// `WorldManager.Load()` in C#.
#[derive(Debug, thiserror::Error)]
pub enum WorldError {
    /// `OperationCanceledException` thrown when no `world.xml` is found.
    #[error("No client worlds data has been found, please check the readme.txt file inside the ClientData folder for more info.")]
    NoClientWorlds,
    /// `XmlDocument.Load(null)` (`ArgumentNullException`): a world name was derived but its
    /// `game/worlds/<name>/world.xml` is not in any source.
    #[error("world.xml for world {world} not found ({path})")]
    WorldXmlNotFound { world: String, path: String },
    /// `XmlException` while parsing a world.xml.
    #[error("invalid XML in {path}: {source}")]
    Xml {
        path: String,
        #[source]
        source: roxmltree::Error,
    },
    /// `GameException("Duplicate zoneKey while reading world.xml")`.
    #[error("Duplicate zoneKey while reading world.xml (zoneKey {zone_key} in {world})")]
    DuplicateZoneKey { world: String, zone_key: u32 },
    /// `GameException($"Failed to add Cell {X}, {Y} in {Name}")`.
    #[error("Failed to add Cell {x}, {y} in {zone}")]
    DuplicateCell { zone: String, x: i32, y: i32 },
    /// `IndexOutOfRangeException` writing `ZoneKeyByRegions[sx, sy]` for a `<sector>`.
    #[error("sector {sector_x},{sector_y} of zone {zone_key} is outside world {world}")]
    SectorOutOfRange {
        world: String,
        zone_key: u32,
        sector_x: i32,
        sector_y: i32,
    },
    /// `OverflowException`/`OutOfMemoryException` creating the region array.
    #[error("invalid world size {cell_x}x{cell_y} cells for {world}")]
    InvalidWorldSize {
        world: String,
        cell_x: i32,
        cell_y: i32,
    },
    /// `ArgumentException` from `WorldTemplates.Add` / `WorldTemplatesById.Add`.
    #[error("world template {name} (id {id}) defined more than once")]
    DuplicateWorld { name: String, id: u32 },
    /// `ArgumentException` from `_worldIdByZoneKey.Add`: a zone key used by two worlds.
    #[error("zoneKey {zone_key} of world {world_id} already belongs to world {other_world_id}")]
    ZoneKeyInMultipleWorlds {
        zone_key: u32,
        world_id: u32,
        other_world_id: u32,
    },
    /// `NullReferenceException` in `CreateWorldTemplate` caused by a `null` entry, or a zone match
    /// with a `null` `SpawnPosition`, in `world_spawns.json`.
    #[error("world_spawns.json contains a null entry or null SpawnPosition (at {name})")]
    NullSpawnData { name: String },
}

// ---------------------------------------------------------------------------------------------
// XmlHelper
// ---------------------------------------------------------------------------------------------

/// Types readable through [`read_attribute`] (`Convert.ChangeType(val, typeof(T))`).
pub trait XmlAttributeValue: Sized {
    /// Parses like `Convert.ChangeType`; `None` when it would throw.
    fn change_type(value: &str) -> Option<Self>;
}

/// .NET `NumberStyles.AllowLeadingWhite | AllowTrailingWhite` characters.
fn trim_dotnet_white(s: &str) -> &str {
    s.trim_matches(|c: char| matches!(c, '\t'..='\r' | ' '))
}

impl XmlAttributeValue for String {
    fn change_type(value: &str) -> Option<Self> {
        Some(value.to_string())
    }
}

impl XmlAttributeValue for i32 {
    /// `Int32.Parse(s, NumberStyles.Integer)`.
    fn change_type(value: &str) -> Option<Self> {
        trim_dotnet_white(value).parse().ok()
    }
}

impl XmlAttributeValue for u32 {
    /// `UInt32.Parse(s, NumberStyles.Integer)` (accepts `+n` and `-0`).
    fn change_type(value: &str) -> Option<Self> {
        let s = trim_dotnet_white(value);
        if let Some(digits) = s.strip_prefix('-') {
            return (!digits.is_empty() && digits.bytes().all(|b| b == b'0')).then_some(0);
        }
        s.parse().ok()
    }
}

impl XmlAttributeValue for f32 {
    /// `Single.Parse(s, NumberStyles.Float | AllowThousands)`. NOTE: C# uses the *current* culture;
    /// this assumes an invariant (`.` decimal, `,` group separator) culture.
    fn change_type(value: &str) -> Option<Self> {
        let s = trim_dotnet_white(value);
        // Group separators are only allowed in the integral part.
        let int_end = s.find(['.', 'e', 'E']).unwrap_or(s.len());
        let (int_part, rest) = s.split_at(int_end);
        let cleaned: String = if int_part.contains(',') {
            let digits_start = int_part.find(|c: char| c.is_ascii_digit())?;
            if int_part[..digits_start].contains(',') {
                return None;
            }
            int_part.replace(',', "") + rest
        } else {
            s.to_string()
        };
        cleaned.parse().ok()
    }
}

/// `XmlHelper.ReadAttribute<T>(attribs, field, defaultValue)`: missing attribute or a failed
/// conversion yields `default`.
pub fn read_attribute<T: XmlAttributeValue>(node: roxmltree::Node, field: &str, default: T) -> T {
    match node.attribute(field) {
        Some(v) => T::change_type(v).unwrap_or(default),
        None => default,
    }
}

/// `XmlNode.SelectNodes("a/b")` for a simple relative child path.
fn select_nodes<'a, 'i>(
    node: roxmltree::Node<'a, 'i>,
    path: &[&'static str],
) -> Vec<roxmltree::Node<'a, 'i>> {
    let mut current = vec![node];
    for step in path {
        current = current
            .into_iter()
            .flat_map(|n| n.children())
            .filter(|c| {
                c.is_element() && c.tag_name().namespace().is_none() && c.tag_name().name() == *step
            })
            .collect();
    }
    current
}

// ---------------------------------------------------------------------------------------------
// WorldSpawnPosition / WorldSpawnLocation
// ---------------------------------------------------------------------------------------------

/// `WorldSpawnPosition` (only the data members).
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct WorldSpawnPosition {
    /// Initialised from `WorldManager.DefaultWorldTemplateId` (0) in C#; when deserialized and
    /// absent from the JSON it is `1` (`[DefaultValue(1)]` + `IgnoreAndPopulate`).
    pub world_id: u32,
    pub zone_id: u32,
    pub x: f32,
    pub y: f32,
    pub z: f32,
    /// Rotation around Z-Axis in radians.
    pub yaw: f32,
    /// Rotation around Y-Axis in radians.
    pub pitch: f32,
    /// Rotation around X-Axis in radians.
    pub roll: f32,
}

/// `WorldSpawnLocation`: one `Data/Worlds/world_spawns.json` entry.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct WorldSpawnLocation {
    pub name: Option<String>,
    pub spawn_position: Option<WorldSpawnPosition>,
}

/// Newtonsoft property lookup: exact match first, then case-insensitive.
fn json_prop<'a>(obj: &'a serde_json::Map<String, Value>, name: &str) -> Option<&'a Value> {
    obj.get(name).or_else(|| {
        obj.iter()
            .find(|(k, _)| k.to_lowercase() == name.to_lowercase())
            .map(|(_, v)| v)
    })
}

fn json_f32(v: &Value) -> Result<f32, ()> {
    match v {
        Value::Number(n) => n.as_f64().map(|f| f as f32).ok_or(()),
        Value::String(s) => trim_dotnet_white(s).parse::<f32>().map_err(|_| ()),
        Value::Bool(b) => Ok(if *b { 1.0 } else { 0.0 }),
        _ => Err(()),
    }
}

fn json_u32(v: &Value) -> Result<u32, ()> {
    match v {
        Value::Number(n) => {
            if let Some(u) = n.as_u64() {
                u32::try_from(u).map_err(|_| ())
            } else {
                // Float tokens go through Convert.ToUInt32(double): banker's rounding.
                let f = n.as_f64().ok_or(())?.round_ties_even();
                if (0.0..=f64::from(u32::MAX)).contains(&f) {
                    Ok(f as u32)
                } else {
                    Err(())
                }
            }
        }
        Value::String(s) => <u32 as XmlAttributeValue>::change_type(s).ok_or(()),
        Value::Bool(b) => Ok(u32::from(*b)),
        _ => Err(()),
    }
}

fn json_spawn_position(v: &Value) -> Result<Option<WorldSpawnPosition>, ()> {
    let obj = match v {
        Value::Null => return Ok(None),
        Value::Object(o) => o,
        _ => return Err(()),
    };
    // IgnoreAndPopulate: absent members get their [DefaultValue].
    let mut p = WorldSpawnPosition {
        world_id: 1,
        ..Default::default()
    };
    let u = |name: &str, target: &mut u32| -> Result<(), ()> {
        if let Some(v) = json_prop(obj, name) {
            *target = json_u32(v)?;
        }
        Ok(())
    };
    u("WorldId", &mut p.world_id)?;
    u("ZoneId", &mut p.zone_id)?;
    for (name, target) in [
        ("X", &mut p.x),
        ("Y", &mut p.y),
        ("Z", &mut p.z),
        ("Yaw", &mut p.yaw),
        ("Pitch", &mut p.pitch),
        ("Roll", &mut p.roll),
    ] {
        if let Some(v) = json_prop(obj, name) {
            *target = json_f32(v)?;
        }
    }
    Ok(Some(p))
}

/// Removes `//` and `/* */` comments and trailing commas, which Newtonsoft.Json accepts
/// (the shipped `world_spawns.json` contains `//` comments).
pub(crate) fn strip_json_extensions(src: &str) -> String {
    let chars: Vec<char> = src.chars().collect();
    let mut out = String::with_capacity(src.len());
    let mut i = 0;
    let mut in_string = false;
    while i < chars.len() {
        let c = chars[i];
        if in_string {
            out.push(c);
            if c == '\\' && i + 1 < chars.len() {
                out.push(chars[i + 1]);
                i += 1;
            } else if c == '"' {
                in_string = false;
            }
            i += 1;
            continue;
        }
        match c {
            '"' => {
                in_string = true;
                out.push(c);
                i += 1;
            }
            '/' if chars.get(i + 1) == Some(&'/') => {
                while i < chars.len() && chars[i] != '\n' {
                    i += 1;
                }
            }
            '/' if chars.get(i + 1) == Some(&'*') => {
                i += 2;
                while i < chars.len() && !(chars[i] == '*' && chars.get(i + 1) == Some(&'/')) {
                    i += 1;
                }
                i += 2;
                out.push(' ');
            }
            ',' => {
                // Only scan to the next token. Recursively stripping the entire remainder
                // for each comment repeats work exponentially on the shipped kits.json.
                let mut j = i + 1;
                loop {
                    match chars.get(j) {
                        Some(c) if c.is_whitespace() => j += 1,
                        Some('/') if chars.get(j + 1) == Some(&'/') => {
                            j += 2;
                            while j < chars.len() && chars[j] != '\n' {
                                j += 1;
                            }
                        }
                        Some('/') if chars.get(j + 1) == Some(&'*') => {
                            j += 2;
                            while j < chars.len()
                                && !(chars[j] == '*' && chars.get(j + 1) == Some(&'/'))
                            {
                                j += 1;
                            }
                            j = (j + 2).min(chars.len());
                        }
                        _ => break,
                    }
                }
                if !matches!(chars.get(j), Some(']' | '}')) {
                    out.push(c);
                }
                i += 1;
            }
            _ => {
                out.push(c);
                i += 1;
            }
        }
    }
    out
}

/// `JsonHelper.TryDeserializeObject<List<WorldSpawnLocation>>`, with Newtonsoft semantics
/// (case-insensitive members, comments, unknown members ignored, `null` array entries kept as
/// `None`). Returns `None` on failure or a `null` document.
pub fn parse_world_spawns(json: &str) -> Option<Vec<Option<WorldSpawnLocation>>> {
    if json.trim().is_empty() {
        return None;
    }
    let value: Value = serde_json::from_str(&strip_json_extensions(json)).ok()?;
    let arr = match value {
        Value::Array(a) => a,
        _ => return None,
    };
    let mut res = Vec::with_capacity(arr.len());
    for item in &arr {
        let obj = match item {
            Value::Null => {
                res.push(None);
                continue;
            }
            Value::Object(o) => o,
            _ => return None,
        };
        let name = match json_prop(obj, "Name") {
            None | Some(Value::Null) => None,
            Some(Value::String(s)) => Some(s.clone()),
            Some(Value::Number(n)) => Some(n.to_string()),
            Some(Value::Bool(b)) => Some(if *b { "True" } else { "False" }.to_string()),
            Some(_) => return None,
        };
        let spawn_position = match json_prop(obj, "SpawnPosition") {
            None => None,
            Some(v) => json_spawn_position(v).ok()?,
        };
        res.push(Some(WorldSpawnLocation {
            name,
            spawn_position,
        }));
    }
    Some(res)
}

// ---------------------------------------------------------------------------------------------
// Xml world data
// ---------------------------------------------------------------------------------------------

/// `XmlWorldSector`: `<sector x="0" y="2"/>` (coordinates inside the parent cell).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct XmlWorldSector {
    pub x: i32,
    pub y: i32,
}

/// `XmlWorldCell`: `<cell x="9" y="14">`. C# discards the sectors after applying them; they are
/// kept here for convenience.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct XmlWorldCell {
    pub x: i32,
    pub y: i32,
    pub sectors: Vec<XmlWorldSector>,
}

/// `XmlWorldZone`: `<Zone name="w_gweonid_forest_1" id="129" originX="9" originY="14">`.
#[derive(Debug, Clone, PartialEq)]
pub struct XmlWorldZone {
    /// Stored in the XML as `id`.
    pub zone_key: u32,
    pub name: String,
    pub origin_x: i32,
    pub origin_y: i32,
    /// Cells by (x, y).
    pub cells: BTreeMap<(i32, i32), XmlWorldCell>,
    /// Spawn coordinates for this zone (from `world_spawns.json`, matched by zone name).
    pub spawn_position: WorldSpawnPosition,
}

/// `XmlWorld`: attributes of the `<World>` root element plus its zones.
#[derive(Debug, Clone, PartialEq)]
pub struct XmlWorld {
    pub name: String,
    pub resolution: u32,
    pub unit_size: u32,
    pub cell_x_count: u32,
    pub cell_y_count: u32,
    pub is_instance: u32,
    pub next_layer_id: u32,
    pub next_surface_id: u32,
    pub ocean_level: f32,
    pub max_terrain_height: f32,
    pub is_release_branch: u32,
    /// Zones by zoneKey (also `WorldTemplate.XmlWorldZones`, which holds the same objects in C#).
    pub zones: BTreeMap<u32, XmlWorldZone>,
}

impl Default for XmlWorld {
    fn default() -> Self {
        XmlWorld {
            name: String::new(),
            resolution: 1024,
            unit_size: 2,
            cell_x_count: 0,
            cell_y_count: 0,
            is_instance: 0,
            next_layer_id: 0,
            next_surface_id: 0,
            ocean_level: 100.0,
            max_terrain_height: 4096.0,
            is_release_branch: 0,
            zones: BTreeMap::new(),
        }
    }
}

// ---------------------------------------------------------------------------------------------
// WorldTemplate
// ---------------------------------------------------------------------------------------------

/// `WorldTemplate` (data members only).
#[derive(Debug, Clone, PartialEq)]
pub struct WorldTemplate {
    /// TemplateId for this world (index in the world names list).
    pub id: u32,
    /// World name (the `name` attribute of world.xml).
    pub name: String,
    /// Max height for this world's map data.
    pub max_height: f32,
    /// `HeightMaxCoefficient = ushort.MaxValue / (MaxHeight / 4.0)`.
    pub height_max_coefficient: f64,
    /// Height of the ocean surface for this world.
    pub ocean_level: f32,
    /// World X size in cells (1024m).
    pub cell_x: i32,
    /// World Y size in cells (1024m).
    pub cell_y: i32,
    /// Default spawn location for this world.
    pub spawn_position: WorldSpawnPosition,
    /// `ZoneKeyByRegions[x, y]` flattened as `x * regions_y + y` (C# row-major layout).
    zone_key_by_regions: Vec<u32>,
    /// List of zone keys inside this world, in XML order.
    pub zone_keys: Vec<u32>,
    /// Xml data for this world.
    pub xml_world: XmlWorld,
}

impl WorldTemplate {
    /// Empty template as created by `new WorldTemplate { Id = id }`.
    fn empty(id: u32) -> Self {
        WorldTemplate {
            id,
            name: String::new(),
            max_height: 0.0,
            height_max_coefficient: 0.0,
            ocean_level: 100.0,
            cell_x: 0,
            cell_y: 0,
            spawn_position: WorldSpawnPosition::default(),
            zone_key_by_regions: Vec::new(),
            zone_keys: Vec::new(),
            xml_world: XmlWorld::default(),
        }
    }

    /// Allocates `ZoneKeyByRegions` for `cell_x` x `cell_y` cells.
    fn alloc_regions(&mut self) -> Result<(), WorldError> {
        let invalid = || WorldError::InvalidWorldSize {
            world: self.name.clone(),
            cell_x: self.cell_x,
            cell_y: self.cell_y,
        };
        let rx = self
            .cell_x
            .checked_mul(SECTORS_PER_CELL)
            .ok_or_else(invalid)?;
        let ry = self
            .cell_y
            .checked_mul(SECTORS_PER_CELL)
            .ok_or_else(invalid)?;
        let (rx, ry) = (
            usize::try_from(rx).map_err(|_| invalid())?,
            usize::try_from(ry).map_err(|_| invalid())?,
        );
        let total = rx.checked_mul(ry).ok_or_else(invalid)?;
        if total > MAX_REGIONS {
            return Err(invalid());
        }
        self.zone_key_by_regions = vec![0; total];
        Ok(())
    }

    /// Builds a template without client files: `cell_x` x `cell_y` cells, every region set to
    /// `zone_key`, with one XML zone named after the world. Intended for tests.
    pub fn synthetic(id: u32, name: &str, cell_x: i32, cell_y: i32, zone_key: u32) -> Self {
        let mut t = WorldTemplate::empty(id);
        t.name = name.to_string();
        t.cell_x = cell_x.max(0);
        t.cell_y = cell_y.max(0);
        t.max_height = 4096.0;
        t.height_max_coefficient = f64::from(u16::MAX) / (f64::from(t.max_height) / 4.0);
        t.alloc_regions()
            .expect("synthetic world size must be reasonable");
        t.zone_key_by_regions.fill(zone_key);
        t.zone_keys.push(zone_key);
        t.xml_world.name = name.to_string();
        t.xml_world.cell_x_count = t.cell_x as u32;
        t.xml_world.cell_y_count = t.cell_y as u32;
        t.xml_world.zones.insert(
            zone_key,
            XmlWorldZone {
                zone_key,
                name: name.to_string(),
                origin_x: 0,
                origin_y: 0,
                cells: BTreeMap::new(),
                spawn_position: WorldSpawnPosition::default(),
            },
        );
        t
    }

    /// Parses a world.xml document (`CreateWorldTemplate` up to `XmlWorld.ReadNode`). Returns
    /// `Ok(None)` when there is no `/World` root element (C# returns `null`). Spawn positions are
    /// not applied here. `path` is only used for error messages.
    pub fn from_xml(id: u32, xml: &str, path: &str) -> Result<Option<WorldTemplate>, WorldError> {
        let doc = roxmltree::Document::parse_with_options(
            xml,
            roxmltree::ParsingOptions {
                allow_dtd: true,
                ..Default::default()
            },
        )
        .map_err(|source| WorldError::Xml {
            path: path.to_string(),
            source,
        })?;
        let root = doc.root_element();
        if root.tag_name().name() != "World" || root.tag_name().namespace().is_some() {
            // Couldn't find world XML?
            return Ok(None);
        }
        let mut t = WorldTemplate::empty(id);
        t.read_world_node(root)?;
        Ok(Some(t))
    }

    /// `XmlWorld.ReadNode(node, worldTemplate)`.
    fn read_world_node(&mut self, node: roxmltree::Node) -> Result<(), WorldError> {
        let xw = XmlWorld {
            name: read_attribute(node, "name", String::new()),
            resolution: read_attribute(node, "resolution", 1024u32),
            unit_size: read_attribute(node, "unitSize", 2u32),
            cell_x_count: read_attribute(node, "cellXCount", 0u32),
            cell_y_count: read_attribute(node, "cellYCount", 0u32),
            is_instance: read_attribute(node, "isInstance", 0u32),
            next_layer_id: read_attribute(node, "nextLayerId", 0u32),
            next_surface_id: read_attribute(node, "nextSurfaceId", 0u32),
            ocean_level: read_attribute(node, "oceanLevel", 100f32),
            max_terrain_height: read_attribute(node, "maxTerrainHeight", 4096f32),
            is_release_branch: read_attribute(node, "isReleaseBranch", 0u32),
            zones: BTreeMap::new(),
        };

        // Apply Data to world
        self.name = xw.name.clone();
        // C# `(int)CellXCount` (unchecked cast).
        self.cell_x = xw.cell_x_count as i32;
        self.cell_y = xw.cell_y_count as i32;
        self.ocean_level = xw.ocean_level;
        self.max_height = xw.max_terrain_height;
        // pre-create region data
        self.alloc_regions()?;
        self.height_max_coefficient = f64::from(u16::MAX) / (f64::from(self.max_height) / 4.0);
        self.xml_world = xw;

        // Read Zone XML
        for zone_node in select_nodes(node, &["ZoneList", "Zone"]) {
            let zone = self.read_zone_node(zone_node)?;
            if self.xml_world.zones.contains_key(&zone.zone_key) {
                return Err(WorldError::DuplicateZoneKey {
                    world: self.name.clone(),
                    zone_key: zone.zone_key,
                });
            }
            self.xml_world.zones.insert(zone.zone_key, zone);
        }
        Ok(())
    }

    /// `XmlWorldZone.ReadNode`.
    fn read_zone_node(&mut self, node: roxmltree::Node) -> Result<XmlWorldZone, WorldError> {
        let mut zone = XmlWorldZone {
            zone_key: read_attribute(node, "id", 0u32),
            name: read_attribute(node, "name", String::new()),
            origin_x: read_attribute(node, "originX", 0i32),
            origin_y: read_attribute(node, "originY", 0i32),
            cells: BTreeMap::new(),
            spawn_position: WorldSpawnPosition::default(),
        };

        // Apply Data to world
        if !self.zone_keys.contains(&zone.zone_key) {
            self.zone_keys.push(zone.zone_key);
        }

        for cell_node in select_nodes(node, &["cellList", "cell"]) {
            // XmlWorldCell.ReadNode
            let mut cell = XmlWorldCell {
                x: read_attribute(cell_node, "x", 0i32),
                y: read_attribute(cell_node, "y", 0i32),
                sectors: Vec::new(),
            };
            for sector_node in select_nodes(cell_node, &["sectorList", "sector"]) {
                // XmlWorldSector.ReadNode
                let sector = XmlWorldSector {
                    x: read_attribute(sector_node, "x", 0i32),
                    y: read_attribute(sector_node, "y", 0i32),
                };
                // WorldSectorX/Y (C# int arithmetic, unchecked)
                let sx = sector.x.wrapping_add(cell.x.wrapping_mul(SECTORS_PER_CELL));
                let sy = sector.y.wrapping_add(cell.y.wrapping_mul(SECTORS_PER_CELL));
                if !self.set_zone_key_at(sx, sy, zone.zone_key) {
                    return Err(WorldError::SectorOutOfRange {
                        world: self.name.clone(),
                        zone_key: zone.zone_key,
                        sector_x: sx,
                        sector_y: sy,
                    });
                }
                cell.sectors.push(sector);
            }
            if zone.cells.contains_key(&(cell.x, cell.y)) {
                return Err(WorldError::DuplicateCell {
                    zone: zone.name.clone(),
                    x: cell.x,
                    y: cell.y,
                });
            }
            zone.cells.insert((cell.x, cell.y), cell);
        }
        Ok(zone)
    }

    /// Number of regions along X (`CellX * SECTORS_PER_CELL`).
    pub fn regions_x(&self) -> i32 {
        self.cell_x * SECTORS_PER_CELL
    }

    /// Number of regions along Y (`CellY * SECTORS_PER_CELL`).
    pub fn regions_y(&self) -> i32 {
        self.cell_y * SECTORS_PER_CELL
    }

    /// `ValidRegion(sectorX, sectorY)`: is the sector inside this world's bounds?
    pub fn valid_region(&self, sector_x: i32, sector_y: i32) -> bool {
        sector_x >= 0 && sector_x < self.regions_x() && sector_y >= 0 && sector_y < self.regions_y()
    }

    fn region_index(&self, sector_x: i32, sector_y: i32) -> Option<usize> {
        self.valid_region(sector_x, sector_y)
            .then(|| sector_x as usize * self.regions_y() as usize + sector_y as usize)
    }

    /// `ZoneKeyByRegions[sector_x, sector_y]`; `None` when out of bounds.
    pub fn zone_key_at(&self, sector_x: i32, sector_y: i32) -> Option<u32> {
        self.region_index(sector_x, sector_y)
            .map(|i| self.zone_key_by_regions[i])
    }

    /// Sets `ZoneKeyByRegions[sector_x, sector_y]`; returns false when out of bounds.
    pub fn set_zone_key_at(&mut self, sector_x: i32, sector_y: i32, zone_key: u32) -> bool {
        match self.region_index(sector_x, sector_y) {
            Some(i) => {
                self.zone_key_by_regions[i] = zone_key;
                true
            }
            None => false,
        }
    }

    /// The flat `ZoneKeyByRegions` array (`x * regions_y + y`).
    pub fn zone_key_by_regions(&self) -> &[u32] {
        &self.zone_key_by_regions
    }

    /// `XmlWorldZones` (zoneKey -> zone).
    pub fn xml_world_zones(&self) -> &BTreeMap<u32, XmlWorldZone> {
        &self.xml_world.zones
    }

    /// `WorldManager.GetZoneId(this, x, y)`: zone key of the region containing world position
    /// (x, y); 0 (and a logged error) when out of bounds.
    pub fn zone_id_at(&self, x: f32, y: f32) -> u32 {
        // C# `(int)(x / REGION_SIZE)`: truncation, saturating with NaN -> 0 on .NET 9+ (same as
        // Rust `as`).
        let sx = (x / REGION_SIZE as f32) as i32;
        let sy = (y / REGION_SIZE as f32) as i32;
        match self.zone_key_at(sx, sy) {
            Some(zone_key) => zone_key,
            None => {
                error!(
                    "GetZoneId: Coordinates out of bounds for WorldId {} - x:{x:.1} - y: {y:.1}",
                    self.id
                );
                0
            }
        }
    }
}

// ---------------------------------------------------------------------------------------------
// WorldTemplates (WorldManager client-data part)
// ---------------------------------------------------------------------------------------------

/// `Path.GetFileName(Path.GetDirectoryName(path))`: name of the directory containing `path`.
/// Both `/` and the OS separator are accepted (as .NET does on Windows).
fn parent_directory_name(path: &str) -> String {
    let is_sep = |c: char| c == '/' || c == MAIN_SEPARATOR;
    let dir = match path.rfind(is_sep) {
        Some(pos) => path[..pos].trim_end_matches(is_sep),
        None => return String::new(),
    };
    match dir.rfind(is_sep) {
        Some(pos) => dir[pos + 1..].to_string(),
        None => dir.to_string(),
    }
}

/// World templates and the zone <-> world caches of `WorldManager`.
#[derive(Debug, Clone, Default)]
pub struct WorldTemplates {
    /// Templates, in creation (= id) order.
    templates: Vec<WorldTemplate>,
    /// `WorldTemplates` (by world.xml `name` attribute) -> index into `templates`.
    by_name: HashMap<String, usize>,
    /// `WorldTemplatesById` -> index into `templates`.
    by_id: HashMap<u32, usize>,
    /// `WorldNames`: directory names; the index is the world template id.
    world_names: Vec<String>,
    /// `WorldSpawnLookups`.
    world_spawn_lookups: Vec<WorldSpawnLocation>,
    /// `_worldIdByZoneKey`.
    world_id_by_zone_key: HashMap<u32, u32>,
    /// `_zoneKeysByWorldId`.
    zone_keys_by_world_id: HashMap<u32, Vec<u32>>,
    /// `DefaultWorldTemplateId`: the id of `main_world`.
    default_world_template_id: u32,
}

impl WorldTemplates {
    /// `WorldManager.Load()` `#region LoadClientData`: finds every `game/worlds/*/world.xml` in
    /// the client sources, reads `<app_path>/Data/Worlds/world_spawns.json` and creates a
    /// template per world. `main_world` always gets id 0; the others follow in file-list order.
    pub fn load(files: &ClientFileManager, app_path: &Path) -> Result<WorldTemplates, WorldError> {
        info!("Loading world data...");

        let world_xml_paths = files.files_in_directory("game/worlds", "world.xml", true);
        if world_xml_paths.is_empty() {
            return Err(WorldError::NoClientWorlds);
        }

        let mut world_names = vec!["main_world".to_string()];

        // Grab world_spawns.json info
        let spawn_position_file = app_path
            .join("Data")
            .join("Worlds")
            .join("world_spawns.json");
        let contents = std::fs::read(&spawn_position_file)
            .map(|b| decode_text(&b))
            .unwrap_or_default();
        let mut lookups: Vec<Option<WorldSpawnLocation>> = Vec::new();
        if contents.trim().is_empty() {
            error!(
                "File {} doesn't exists or is empty.",
                spawn_position_file.display()
            );
        } else {
            match parse_world_spawns(&contents) {
                Some(l) => lookups = l,
                None => error!("Error in {}.", spawn_position_file.display()),
            }
        }

        // Add all instance names to the worldNames list to generate world template Ids
        for world_xml_path in &world_xml_paths {
            let world_name = parent_directory_name(world_xml_path);
            if !world_names.contains(&world_name) {
                world_names.push(world_name);
            }
        }

        let mut worlds = WorldTemplates {
            world_names,
            ..Default::default()
        };

        // Load data for every instance name
        for world_template_id in 0..worlds.world_names.len() {
            let world_name = worlds.world_names[world_template_id].clone();
            worlds.create_world_template(files, &world_name, &lookups)?;
        }

        worlds.world_spawn_lookups = lookups.into_iter().flatten().collect();
        Ok(worlds)
    }

    /// `WorldManager.CreateWorldTemplate(worldName)`. Returns the template id, or `None` when the
    /// name is unknown or the XML has no `/World` root.
    fn create_world_template(
        &mut self,
        files: &ClientFileManager,
        world_name: &str,
        lookups: &[Option<WorldSpawnLocation>],
    ) -> Result<Option<u32>, WorldError> {
        let Some(world_template_id) = self.world_names.iter().position(|n| n == world_name) else {
            return Ok(None); // instance name not defined
        };
        if let Some(t) = self.get_world_template_by_name(world_name) {
            return Ok(Some(t.id));
        }

        // Open XML file
        let sep = MAIN_SEPARATOR;
        let path = format!("game{sep}worlds{sep}{world_name}{sep}world.xml");
        let Some(bytes) = files.read_file(&path) else {
            return Err(WorldError::WorldXmlNotFound {
                world: world_name.to_string(),
                path,
            });
        };
        let Some(mut template) =
            WorldTemplate::from_xml(world_template_id as u32, &decode_text(&bytes), &path)?
        else {
            return Ok(None);
        };

        // FirstOrDefault(w => w.Name == worldTemplate.Name): a null entry throws.
        let mut spawn = None;
        for l in lookups {
            let Some(l) = l else {
                return Err(WorldError::NullSpawnData {
                    name: template.name.clone(),
                });
            };
            if l.name.as_deref() == Some(template.name.as_str()) {
                spawn = Some(l.spawn_position);
                break;
            }
        }
        template.spawn_position = spawn.flatten().unwrap_or_default();
        template.spawn_position.world_id = template.id;

        // Add coordinates for zones
        for zone in template.xml_world.zones.values_mut() {
            for l in lookups.iter().flatten() {
                if l.name.as_deref() == Some(zone.name.as_str()) {
                    let Some(pos) = l.spawn_position else {
                        return Err(WorldError::NullSpawnData {
                            name: zone.name.clone(),
                        });
                    };
                    zone.spawn_position = pos;
                    zone.spawn_position.world_id = template.id;
                    break;
                }
            }
        }

        let is_main = world_name == "main_world";
        let id = template.id;
        self.insert_template(template)?;

        // Mark "main_world" as the DefaultWorldId
        if is_main {
            self.default_world_template_id = id;
        }
        Ok(Some(id))
    }

    /// Registers a template and caches its zone keys (`WorldTemplates.Add`,
    /// `WorldTemplatesById.Add`, `_worldIdByZoneKey.Add`, `_zoneKeysByWorldId`).
    fn insert_template(&mut self, template: WorldTemplate) -> Result<(), WorldError> {
        if self.by_name.contains_key(&template.name) || self.by_id.contains_key(&template.id) {
            return Err(WorldError::DuplicateWorld {
                name: template.name,
                id: template.id,
            });
        }
        for zone_key in &template.zone_keys {
            if let Some(&other) = self.world_id_by_zone_key.get(zone_key) {
                return Err(WorldError::ZoneKeyInMultipleWorlds {
                    zone_key: *zone_key,
                    world_id: template.id,
                    other_world_id: other,
                });
            }
        }
        let idx = self.templates.len();
        self.by_name.insert(template.name.clone(), idx);
        self.by_id.insert(template.id, idx);
        for &zone_key in &template.zone_keys {
            self.world_id_by_zone_key.insert(zone_key, template.id);
            self.zone_keys_by_world_id
                .entry(template.id)
                .or_default()
                .push(zone_key);
        }
        self.templates.push(template);
        Ok(())
    }

    /// Builds the lookups from already constructed templates (e.g. [`WorldTemplate::synthetic`]).
    /// World names are taken from the template names at their ids; `main_world` (if present)
    /// becomes the default world.
    pub fn from_templates(
        templates: Vec<WorldTemplate>,
        world_spawn_lookups: Vec<WorldSpawnLocation>,
    ) -> Result<WorldTemplates, WorldError> {
        let mut worlds = WorldTemplates {
            world_spawn_lookups,
            ..Default::default()
        };
        for template in templates {
            let id = template.id as usize;
            if worlds.world_names.len() <= id {
                worlds.world_names.resize(id + 1, String::new());
            }
            worlds.world_names[id] = template.name.clone();
            if template.name == "main_world" {
                worlds.default_world_template_id = template.id;
            }
            worlds.insert_template(template)?;
        }
        Ok(worlds)
    }

    /// `DefaultWorldTemplateId`: template id of `main_world` (usually 0).
    pub fn default_world_template_id(&self) -> u32 {
        self.default_world_template_id
    }

    /// All templates, in id order.
    pub fn templates(&self) -> &[WorldTemplate] {
        &self.templates
    }

    /// `WorldNames` (index = template id).
    pub fn world_names(&self) -> &[String] {
        &self.world_names
    }

    /// `WorldSpawnLookups` (`null` entries dropped).
    pub fn world_spawn_lookups(&self) -> &[WorldSpawnLocation] {
        &self.world_spawn_lookups
    }

    /// `GetWorldTemplateByName`: lookup by the world.xml `name` attribute.
    pub fn get_world_template_by_name(&self, world_name: &str) -> Option<&WorldTemplate> {
        self.by_name.get(world_name).map(|&i| &self.templates[i])
    }

    /// `WorldTemplatesById` lookup.
    pub fn get_world_template_by_id(&self, id: u32) -> Option<&WorldTemplate> {
        self.by_id.get(&id).map(|&i| &self.templates[i])
    }

    /// `GetWorldName(worldTemplateId)` (`None` instead of an index exception).
    pub fn get_world_name(&self, world_template_id: u32) -> Option<&str> {
        self.world_names
            .get(world_template_id as usize)
            .map(String::as_str)
    }

    /// `GetWorldIdByZoneKey`: `u32::MAX` (and a logged error) when unknown.
    pub fn get_world_id_by_zone_key(&self, zone_key: u32) -> u32 {
        match self.world_id_by_zone_key.get(&zone_key) {
            Some(&id) => id,
            None => {
                error!("GetWorldByZone: No world defined for ZoneId {zone_key}");
                u32::MAX
            }
        }
    }

    /// `GetWorldTemplateByZoneKey`. Like C#, this goes through the world *directory* name
    /// (`GetWorldName(worldId)`) and then looks up by template name.
    pub fn get_world_template_by_zone_key(&self, zone_key: u32) -> Option<&WorldTemplate> {
        match self.world_id_by_zone_key.get(&zone_key) {
            Some(&world_id) => self
                .get_world_name(world_id)
                .and_then(|name| self.get_world_template_by_name(name)),
            None => {
                error!("GetWorldByZone(): No world template defined for ZoneId {zone_key}");
                None
            }
        }
    }

    /// `GetZoneKeysByWorldId`: empty when unknown.
    pub fn get_zone_keys_by_world_id(&self, world_id: u32) -> &[u32] {
        self.zone_keys_by_world_id
            .get(&world_id)
            .map(Vec::as_slice)
            .unwrap_or(&[])
    }

    /// `GetZoneId(worldTemplate, x, y)`: 0 for a missing template or out-of-bounds position.
    pub fn get_zone_id(&self, world_template: Option<&WorldTemplate>, x: f32, y: f32) -> u32 {
        world_template.map_or(0, |t| t.zone_id_at(x, y))
    }
}

#[cfg(test)]
mod tests {
    use super::super::pak::test_util::temp_path;
    use super::*;

    const WORLD_XML: &str = r#"<?xml version="1.0" encoding="utf-8"?>
<World name="main_world" cellXCount="2" cellYCount="1" oceanLevel="99.5" maxTerrainHeight="2048" resolution="bad">
  <ZoneList>
    <Zone name="zone_a" id="129" originX="0" originY="0">
      <cellList>
        <cell x="0" y="0">
          <sectorList>
            <sector x="0" y="0"/>
            <sector x="15" y="15"/>
          </sectorList>
        </cell>
      </cellList>
    </Zone>
    <Zone name="zone_b" id="130" originX="1" originY="0">
      <cellList>
        <cell x="1" y="0">
          <sectorList>
            <sector x="0" y="1"/>
          </sectorList>
        </cell>
      </cellList>
    </Zone>
  </ZoneList>
</World>"#;

    #[test]
    fn parses_world_xml() {
        let t = WorldTemplate::from_xml(0, WORLD_XML, "world.xml")
            .unwrap()
            .unwrap();
        assert_eq!(t.name, "main_world");
        assert_eq!((t.cell_x, t.cell_y), (2, 1));
        assert_eq!(t.ocean_level, 99.5);
        assert_eq!(t.max_height, 2048.0);
        assert_eq!(
            t.xml_world.resolution, 1024,
            "bad value falls back to default"
        );
        assert_eq!(t.xml_world.unit_size, 2);
        assert_eq!(t.zone_keys, vec![129, 130]);
        assert_eq!(t.zone_key_by_regions().len(), 32 * 16);
        assert_eq!(t.zone_key_at(0, 0), Some(129));
        assert_eq!(t.zone_key_at(15, 15), Some(129));
        assert_eq!(t.zone_key_at(16, 1), Some(130));
        assert_eq!(t.zone_key_at(1, 1), Some(0));
        assert_eq!(t.zone_key_at(32, 0), None);
        assert_eq!(t.zone_key_at(0, 16), None);
        assert_eq!(t.xml_world_zones()[&130].name, "zone_b");
        assert_eq!(t.xml_world_zones()[&129].cells[&(0, 0)].sectors.len(), 2);

        let worlds = WorldTemplates::from_templates(vec![t], Vec::new()).unwrap();
        let t = worlds.get_world_template_by_name("main_world").unwrap();
        assert_eq!(worlds.get_zone_id(Some(t), 10.0, 10.0), 129);
        assert_eq!(
            worlds.get_zone_id(Some(t), 15.0 * 64.0 + 1.0, 15.0 * 64.0),
            129
        );
        assert_eq!(
            worlds.get_zone_id(Some(t), 16.0 * 64.0 + 5.0, 64.0 + 3.0),
            130
        );
        assert_eq!(worlds.get_zone_id(Some(t), -64.0, 0.0), 0);
        assert_eq!(worlds.get_zone_id(Some(t), 4096.0, 0.0), 0);
        assert_eq!(worlds.get_zone_id(None, 1.0, 1.0), 0);
        assert_eq!(worlds.get_world_id_by_zone_key(130), 0);
        assert_eq!(worlds.get_world_id_by_zone_key(999), u32::MAX);
        assert_eq!(worlds.get_zone_keys_by_world_id(0), &[129, 130]);
        assert!(worlds.get_zone_keys_by_world_id(5).is_empty());
        assert_eq!(worlds.get_world_template_by_zone_key(129).unwrap().id, 0);
        assert!(worlds.get_world_template_by_zone_key(1).is_none());
    }

    #[test]
    fn duplicate_zone_key_is_an_error() {
        let xml = r#"<World name="w" cellXCount="1" cellYCount="1"><ZoneList>
            <Zone name="a" id="5"/><Zone name="b" id="5"/></ZoneList></World>"#;
        assert!(matches!(
            WorldTemplate::from_xml(0, xml, "w"),
            Err(WorldError::DuplicateZoneKey { zone_key: 5, .. })
        ));
    }

    #[test]
    fn duplicate_cell_and_out_of_range_sector_are_errors() {
        let xml = r#"<World name="w" cellXCount="1" cellYCount="1"><ZoneList>
            <Zone name="a" id="5"><cellList><cell x="0" y="0"/><cell x="0" y="0"/></cellList></Zone>
            </ZoneList></World>"#;
        assert!(matches!(
            WorldTemplate::from_xml(0, xml, "w"),
            Err(WorldError::DuplicateCell { x: 0, y: 0, .. })
        ));
        let xml = r#"<World name="w" cellXCount="1" cellYCount="1"><ZoneList>
            <Zone name="a" id="5"><cellList><cell x="1" y="0"><sectorList><sector x="0" y="0"/>
            </sectorList></cell></cellList></Zone></ZoneList></World>"#;
        assert!(matches!(
            WorldTemplate::from_xml(0, xml, "w"),
            Err(WorldError::SectorOutOfRange { sector_x: 16, .. })
        ));
    }

    #[test]
    fn non_world_root_is_skipped() {
        assert!(WorldTemplate::from_xml(0, "<Other/>", "x")
            .unwrap()
            .is_none());
        assert!(matches!(
            WorldTemplate::from_xml(0, "<World", "x"),
            Err(WorldError::Xml { .. })
        ));
    }

    #[test]
    fn attribute_conversion() {
        assert_eq!(<u32 as XmlAttributeValue>::change_type(" 12 "), Some(12));
        assert_eq!(<u32 as XmlAttributeValue>::change_type("+12"), Some(12));
        assert_eq!(<u32 as XmlAttributeValue>::change_type("-0"), Some(0));
        assert_eq!(<u32 as XmlAttributeValue>::change_type("-1"), None);
        assert_eq!(<u32 as XmlAttributeValue>::change_type("1.0"), None);
        assert_eq!(<i32 as XmlAttributeValue>::change_type("-7"), Some(-7));
        assert_eq!(
            <f32 as XmlAttributeValue>::change_type("1,024.5"),
            Some(1024.5)
        );
        assert_eq!(<f32 as XmlAttributeValue>::change_type("1e3"), Some(1000.0));
        assert_eq!(<f32 as XmlAttributeValue>::change_type("abc"), None);
    }

    #[test]
    fn json_comments_after_commas_keep_tokens_and_parse_the_shipped_item_kits() {
        let src = r#"["雪 // , }", /* first */ // second
            2, /* trailing */ // closing
        ]"#;
        let value: Value = serde_json::from_str(&strip_json_extensions(src)).unwrap();
        assert_eq!(value, serde_json::json!(["雪 // , }", 2]));
        let kits = include_str!("../../app/Scripts/Commands/kits.json");
        let value: Value = serde_json::from_str(&strip_json_extensions(kits)).unwrap();
        assert!(value["itemkits"].as_array().unwrap().len() > 100);
    }

    #[test]
    fn world_spawns_json() {
        let json = r#"[
            { "Name": "main_world", // comment
              "SpawnPosition": { "zoneid": 129, "X": 10.5, "Y": "20", "Z": 3, "Yaw": 1.0, },
            },
            /* block */ { "Name": "zone_b", "SpawnPosition": { "ZoneId": 130 } },
            { "Name": "no_pos" },
            null,
        ]"#;
        let l = parse_world_spawns(json).unwrap();
        assert_eq!(l.len(), 4);
        let first = l[0].as_ref().unwrap().spawn_position.unwrap();
        assert_eq!(first.zone_id, 129);
        assert_eq!(first.world_id, 1, "DefaultValue(1) when absent");
        assert_eq!(
            (first.x, first.y, first.z, first.yaw),
            (10.5, 20.0, 3.0, 1.0)
        );
        assert!(l[2].as_ref().unwrap().spawn_position.is_none());
        assert!(l[3].is_none());
        assert!(parse_world_spawns("null").is_none());
        assert!(parse_world_spawns("{}").is_none());
        assert!(parse_world_spawns(r#"[{"Name":"a","SpawnPosition":{"ZoneId":-1}}]"#).is_none());
        assert_eq!(
            parse_world_spawns(r#"[{"Name":"a // not a comment"}]"#).unwrap()[0]
                .as_ref()
                .unwrap()
                .name
                .as_deref(),
            Some("a // not a comment")
        );
    }

    fn write(root: &Path, rel: &str, content: &str) {
        let p = root.join(rel);
        std::fs::create_dir_all(p.parent().unwrap()).unwrap();
        std::fs::write(p, content).unwrap();
    }

    #[test]
    fn load_from_directory_source() {
        let root = temp_path("worlds");
        let client = root.join("client");
        write(&client, "game/worlds/main_world/world.xml", WORLD_XML);
        write(
            &client,
            "game/worlds/foo/world.xml",
            r#"<World name="foo" cellXCount="1" cellYCount="1"><ZoneList>
               <Zone name="foo_zone" id="300"><cellList><cell x="0" y="0"><sectorList>
               <sector x="1" y="2"/></sectorList></cell></cellList></Zone></ZoneList></World>"#,
        );
        write(
            &root,
            "Data/Worlds/world_spawns.json",
            r#"[ { "Name": "foo", "SpawnPosition": { "ZoneId": 300, "X": 1, "Y": 2, "Z": 3 } },
                 { "Name": "zone_b", "SpawnPosition": { "ZoneId": 130, "X": 5 } } ]"#,
        );

        let files = ClientFileManager::from_sources(&["client".to_string()], &root);
        let listed = files.files_in_directory("game/worlds", "world.xml", true);
        assert_eq!(listed.len(), 2);

        let worlds = WorldTemplates::load(&files, &root).unwrap();
        assert_eq!(worlds.world_names(), &["main_world", "foo"]);
        assert_eq!(worlds.default_world_template_id(), 0);
        let main = worlds.get_world_template_by_name("main_world").unwrap();
        assert_eq!(main.id, 0);
        assert_eq!(main.spawn_position, WorldSpawnPosition::default());
        assert_eq!(main.xml_world_zones()[&130].spawn_position.x, 5.0);
        assert_eq!(main.xml_world_zones()[&130].spawn_position.world_id, 0);
        let foo = worlds.get_world_template_by_name("foo").unwrap();
        assert_eq!(foo.id, 1);
        assert_eq!(foo.spawn_position.world_id, 1);
        assert_eq!(foo.spawn_position.z, 3.0);
        assert_eq!(foo.zone_key_at(1, 2), Some(300));
        assert_eq!(worlds.get_world_id_by_zone_key(300), 1);
        assert_eq!(
            worlds.get_world_template_by_zone_key(300).unwrap().name,
            "foo"
        );
        assert_eq!(worlds.get_world_template_by_id(1).unwrap().name, "foo");
        assert_eq!(worlds.world_spawn_lookups().len(), 2);

        std::fs::remove_dir_all(&root).ok();
    }

    #[test]
    fn load_without_worlds_fails() {
        let root = temp_path("noworlds");
        std::fs::create_dir_all(&root).unwrap();
        let files = ClientFileManager::from_sources(&[root.to_string_lossy().into_owned()], &root);
        assert!(matches!(
            WorldTemplates::load(&files, &root),
            Err(WorldError::NoClientWorlds)
        ));
        // A world list without main_world fails like `xml.Load(null)` in C#.
        write(&root, "game/worlds/foo/world.xml", "<World name=\"foo\"/>");
        assert!(matches!(
            WorldTemplates::load(&files, &root),
            Err(WorldError::WorldXmlNotFound { .. })
        ));
        std::fs::remove_dir_all(&root).ok();
    }

    #[test]
    fn synthetic_world() {
        let worlds = WorldTemplates::from_templates(
            vec![
                WorldTemplate::synthetic(0, "main_world", 2, 2, 100),
                WorldTemplate::synthetic(1, "other", 1, 1, 200),
            ],
            Vec::new(),
        )
        .unwrap();
        let main = worlds.get_world_template_by_name("main_world").unwrap();
        assert_eq!(main.zone_key_at(31, 31), Some(100));
        assert_eq!(worlds.get_zone_id(Some(main), 2047.0, 2047.0), 100);
        assert_eq!(worlds.get_world_id_by_zone_key(200), 1);
        assert!(matches!(
            WorldTemplates::from_templates(
                vec![
                    WorldTemplate::synthetic(0, "a", 1, 1, 1),
                    WorldTemplate::synthetic(1, "b", 1, 1, 1),
                ],
                Vec::new()
            ),
            Err(WorldError::ZoneKeyInMultipleWorlds { zone_key: 1, .. })
        ));
    }

    #[test]
    fn parent_dir_names() {
        assert_eq!(parent_directory_name("game/worlds/foo/world.xml"), "foo");
        let p = format!("C:{MAIN_SEPARATOR}x{MAIN_SEPARATOR}main_world{MAIN_SEPARATOR}world.xml");
        assert_eq!(parent_directory_name(&p), "main_world");
    }

    /// Parses the real `world_spawns.json` when `AAEMU_WORLD_SPAWNS` points at it.
    #[test]
    fn real_world_spawns_if_available() {
        let Ok(path) = std::env::var("AAEMU_WORLD_SPAWNS") else {
            return;
        };
        let text = decode_text(&std::fs::read(path).unwrap());
        let l = parse_world_spawns(&text).expect("shipped world_spawns.json must parse");
        assert!(l.iter().all(Option::is_some));
        assert!(l.len() > 10);
    }
}
