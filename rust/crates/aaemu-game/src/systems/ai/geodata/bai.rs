//! `Models/CryEngine/**` navigation data: the `.bai` readers (`BaiReader`,
//! `AreasMissionReader`, `NetMissionReader`, `VertexMissionReader`), their entities
//! (`NodeDescriptor`, `LinkDescriptor`, `ObstacleDataDescriptor`, `BBox`) and missions
//! (`AiShape`, `SpecialArea`, `PolygonArea`), plus `Loaders/BaseBaiLoader`.
//!
//! All numbers are little-endian (`BinaryReader`). Every point except the direction vectors
//! gets the reader's `ReaderPointOffset` (zone origin or path block offset) added.
//!
//! C# quirks mirrored:
//! * `AreasMissionReader.UsedAreaNames` is a process-wide bag: an area whose name was already
//!   read (in any file, any world, earlier in the process) is dropped, and the names of a file
//!   that fails half-way stay registered;
//! * `ExtraLink` shapes are added to `ExtraLinkCostsList` twice (once in `ReadForbiddenArea`,
//!   once by its caller) unless the name was a duplicate;
//! * `ReadNavigationModifiersArea` tests the class constant `BaiAreasFileVersion` (21), not the
//!   file's version;
//! * duplicate node ids in a net mission keep the first node (`TryAdd`).

use std::collections::{HashMap, HashSet};
use std::sync::Mutex;

use crate::systems::skills::controllers::Vec3;

/// `GameException` from a reader (`Exception Pos: ...`).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BaiError(pub String);

type BResult<T> = Result<T, BaiError>;

/// `BinaryReader` over a byte slice.
pub struct Reader<'a> {
    data: &'a [u8],
    pub pos: usize,
}

impl<'a> Reader<'a> {
    pub fn new(data: &'a [u8]) -> Self {
        Self { data, pos: 0 }
    }

    fn take(&mut self, n: usize) -> BResult<&'a [u8]> {
        let end = self
            .pos
            .checked_add(n)
            .filter(|e| *e <= self.data.len())
            .ok_or_else(|| BaiError("Unable to read beyond the end of the stream.".into()))?;
        let s = &self.data[self.pos..end];
        self.pos = end;
        Ok(s)
    }

    pub fn u8(&mut self) -> BResult<u8> {
        Ok(self.take(1)?[0])
    }

    pub fn i32(&mut self) -> BResult<i32> {
        Ok(i32::from_le_bytes(
            self.take(4)?.try_into().expect("4 bytes"),
        ))
    }

    pub fn u32(&mut self) -> BResult<u32> {
        Ok(u32::from_le_bytes(
            self.take(4)?.try_into().expect("4 bytes"),
        ))
    }

    pub fn i64(&mut self) -> BResult<i64> {
        Ok(i64::from_le_bytes(
            self.take(8)?.try_into().expect("8 bytes"),
        ))
    }

    pub fn f32(&mut self) -> BResult<f32> {
        Ok(f32::from_le_bytes(
            self.take(4)?.try_into().expect("4 bytes"),
        ))
    }

    pub fn is_eof(&self) -> bool {
        self.pos >= self.data.len()
    }

    pub fn len(&self) -> usize {
        self.data.len()
    }

    pub fn is_empty(&self) -> bool {
        self.data.is_empty()
    }
}

/// `BaiReader.ReadVector3(doNotAddOffset)`.
fn read_vec3(r: &mut Reader<'_>, offset: Vec3, add_offset: bool) -> BResult<Vec3> {
    let v = Vec3::new(r.f32()?, r.f32()?, r.f32()?);
    Ok(if add_offset {
        Vec3::new(v.x + offset.x, v.y + offset.y, v.z + offset.z)
    } else {
        v
    })
}

/// `BaiReader.ReadName()` (`Encoding.Default` = UTF-8).
fn read_name(r: &mut Reader<'_>) -> BResult<String> {
    let n = r.i32()?;
    let n = usize::try_from(n).map_err(|_| BaiError("Non-negative number required.".into()))?;
    Ok(String::from_utf8_lossy(r.take(n)?).into_owned())
}

fn read_points(r: &mut Reader<'_>, offset: Vec3) -> BResult<Vec<Vec3>> {
    let n = r.u32()?;
    let mut v = Vec::new();
    for _ in 0..n {
        v.push(read_vec3(r, offset, true)?);
    }
    Ok(v)
}

/// `AiShape`.
#[derive(Debug, Clone, PartialEq)]
pub struct AiShape {
    pub zone_id: u32,
    pub name: String,
    pub points: Vec<Vec3>,
}

/// `SpecialArea` (navigation modifier).
#[derive(Debug, Clone, PartialEq)]
pub struct SpecialArea {
    pub zone_id: u32,
    pub name: String,
    pub mission_type: i32,
    pub waypoint_connections: i32,
    pub altered: bool,
    pub height: f32,
    pub node_auto_connect_distance: f32,
    pub min_z: f32,
    pub max_z: f32,
    pub building_id: i32,
    pub ai_light_level: u8,
    pub points: Vec<Vec3>,
}

/// `PolygonArea` (designer path).
#[derive(Debug, Clone, PartialEq)]
pub struct PolygonArea {
    pub zone_id: u32,
    pub name: String,
    pub points: Vec<Vec3>,
    pub navigation_type: i64,
    pub area_type: i32,
    pub height: f32,
    pub ai_light_level: i32,
}

/// `AreasMissionReader`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct AreasMission {
    pub zone_id: u32,
    pub forbidden_areas: Vec<AiShape>,
    pub navigation_modifiers: Vec<SpecialArea>,
    pub designer_forbidden_areas: Vec<AiShape>,
    pub forbidden_boundaries: Vec<AiShape>,
    pub extra_link_costs: Vec<AiShape>,
    pub designer_paths: Vec<PolygonArea>,
}

/// `BaiAreasFileVersion`.
pub const BAI_AREAS_FILE_VERSION: i32 = 21;
/// `BaiTriangulationFileVersion`.
pub const BAI_TRIANGULATION_FILE_VERSION: i32 = 55;
/// `BaiVertexFileVersion`.
pub const BAI_VERTEX_FILE_VERSION: i32 = 2;

/// `AreasMissionReader.UsedAreaNames` (+ `IgnoreDuplicateAreaNames = true`).
#[derive(Debug, Default)]
pub struct UsedAreaNames(Mutex<HashSet<String>>);

impl UsedAreaNames {
    fn contains(&self, n: &str) -> bool {
        self.0.lock().expect("names poisoned").contains(n)
    }

    fn add(&self, n: &str) {
        self.0.lock().expect("names poisoned").insert(n.to_string());
    }
}

#[derive(Clone, Copy)]
enum ForbiddenKind {
    Forbidden,
    Designer,
    Boundaries,
    ExtraLink,
}

impl AreasMission {
    /// `new AreasMissionReader(stream, zoneId) { ReaderPointOffset }.ReadFile()`.
    pub fn read(data: &[u8], zone_id: u32, offset: Vec3, used: &UsedAreaNames) -> BResult<Self> {
        let mut r = Reader::new(data);
        let mut m = Self {
            zone_id,
            ..Self::default()
        };
        let version = r.i32()?;
        if version > BAI_AREAS_FILE_VERSION {
            return Err(BaiError(format!(
                "Wrong Areas BAI file version {version}, expected {BAI_AREAS_FILE_VERSION}"
            )));
        }
        let n = r.u32()?;
        for _ in 0..n {
            m.read_forbidden_area(&mut r, offset, ForbiddenKind::Forbidden, used)?;
        }
        let n = r.u32()?;
        for _ in 0..n {
            m.read_navigation_modifier(&mut r, offset, used)?;
        }
        let n = r.u32()?;
        for _ in 0..n {
            m.read_forbidden_area(&mut r, offset, ForbiddenKind::Designer, used)?;
        }
        let n = r.u32()?;
        for _ in 0..n {
            m.read_forbidden_area(&mut r, offset, ForbiddenKind::Boundaries, used)?;
        }
        // A file from paths has no ExtraLinkCost / DesignerPath settings and ends here.
        if zone_id == 0 {
            if !r.is_eof() {
                tracing::debug!("Reader ended before end of file at {}/{}", r.pos, r.len());
            }
            return Ok(m);
        }
        let n = r.u32()?;
        for _ in 0..n {
            if let Some(shape) =
                m.read_forbidden_area(&mut r, offset, ForbiddenKind::ExtraLink, used)?
            {
                m.extra_link_costs.push(shape);
            }
        }
        let _ = r.i64()?; // unk data
        let n = r.u32()?;
        for _ in 0..n {
            m.read_designer_path(&mut r, offset, used)?;
        }
        Ok(m)
    }

    /// `ReadForbiddenArea(aiShape, type)`: returns the shape for the caller's own `Add`.
    fn read_forbidden_area(
        &mut self,
        r: &mut Reader<'_>,
        offset: Vec3,
        kind: ForbiddenKind,
        used: &UsedAreaNames,
    ) -> BResult<Option<AiShape>> {
        let name = read_name(r)?;
        let points = read_points(r, offset)?;
        let shape = AiShape {
            zone_id: self.zone_id,
            name,
            points,
        };
        if used.contains(&shape.name) {
            // the ExtraLink caller still adds the shape once
            return Ok(matches!(kind, ForbiddenKind::ExtraLink).then_some(shape));
        }
        used.add(&shape.name);
        match kind {
            ForbiddenKind::Forbidden => self.forbidden_areas.push(shape),
            ForbiddenKind::Designer => self.designer_forbidden_areas.push(shape),
            ForbiddenKind::Boundaries => self.forbidden_boundaries.push(shape),
            ForbiddenKind::ExtraLink => {
                self.extra_link_costs.push(shape.clone());
                return Ok(Some(shape));
            }
        }
        Ok(None)
    }

    fn read_navigation_modifier(
        &mut self,
        r: &mut Reader<'_>,
        offset: Vec3,
        used: &UsedAreaNames,
    ) -> BResult<()> {
        let name = read_name(r)?;
        let mission_type = r.i32()?;
        let waypoint_connections = r.i32()?;
        let altered = r.u8()? != 0;
        let _ = r.f32()?; // junk?
        let _ = r.f32()?; // junk?
        let height = r.f32()?;
        // BaiAreasFileVersion (21) <= 0x10 is false: no extra float
        let node_auto_connect_distance = r.f32()?;
        let max_z = r.f32()?;
        let min_z = r.f32()?;
        let building_id = r.i32()?;
        // BaiAreasFileVersion >= 0x12
        let ai_light_level = r.u8()?;
        let points = read_points(r, offset)?;
        if !used.contains(&name) {
            used.add(&name);
            self.navigation_modifiers.push(SpecialArea {
                zone_id: self.zone_id,
                name,
                mission_type,
                waypoint_connections,
                altered,
                height,
                node_auto_connect_distance,
                min_z,
                max_z,
                building_id,
                ai_light_level,
                points,
            });
        }
        Ok(())
    }

    fn read_designer_path(
        &mut self,
        r: &mut Reader<'_>,
        offset: Vec3,
        used: &UsedAreaNames,
    ) -> BResult<()> {
        let name = read_name(r)?;
        let points = read_points(r, offset)?;
        let shift = r.i32()?;
        let navigation_type = 1i64.checked_shl(shift as u32 & 31).unwrap_or(0);
        let area_type = r.i32()?;
        let height = r.f32()?;
        let ai_light_level = r.i32()?;
        if !used.contains(&name) {
            used.add(&name);
            self.designer_paths.push(PolygonArea {
                zone_id: self.zone_id,
                name,
                points,
                navigation_type,
                area_type,
                height,
                ai_light_level,
            });
        }
        Ok(())
    }
}

/// `NodeDescriptor`.
#[derive(Debug, Clone, PartialEq)]
pub struct NodeDescriptor {
    pub id: i32,
    pub dir: Vec3,
    pub up: Vec3,
    pub pos: Vec3,
    pub index: i32,
    pub obstacle: [i32; 3],
    pub node_type: u8,
    pub unk1: u8,
    pub bit_field0: u8,
    pub bit_field1: u8,
}

/// `LinkDescriptor` (`Source/TargetNodeDescriptor` are indices into the reader's nodes).
#[derive(Debug, Clone, PartialEq)]
pub struct LinkDescriptor {
    pub source_node: i64,
    pub target_node: i64,
    pub edge_center: Vec3,
    pub max_pass_radius: f32,
    pub exposure: f32,
    pub length: f32,
    pub max_water_depth: f32,
    pub min_water_depth: f32,
    pub start_index: u8,
    pub end_index: u8,
    pub is_pure_triangular_link: bool,
    pub simple_passability_check: bool,
    pub source: Option<usize>,
    pub target: Option<usize>,
}

/// `NetMissionReader` (also used for `hidemission*.bai`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct NetMission {
    pub zone_id: u32,
    pub bbox_min: Vec3,
    pub bbox_max: Vec3,
    /// `NodeDescriptorList` values in read order (first of a duplicate id kept).
    pub nodes: Vec<NodeDescriptor>,
    /// Node id -> index in `nodes`.
    pub node_by_id: HashMap<i64, usize>,
    pub links: Vec<LinkDescriptor>,
    /// Source node id -> indices into `links` (`LinkDescriptorList.Where(l => l.SourceNode ==
    /// id)`, in list order).
    pub links_by_source: HashMap<i64, Vec<usize>>,
}

impl NetMission {
    /// `new NetMissionReader(stream, zoneId) { ReaderPointOffset }.ReadFile()`.
    pub fn read(data: &[u8], zone_id: u32, offset: Vec3) -> BResult<Self> {
        let mut r = Reader::new(data);
        let version = r.i32()?;
        if version > BAI_TRIANGULATION_FILE_VERSION {
            return Err(BaiError(format!(
                "Wrong triangulation BAI file version {version} expected {BAI_TRIANGULATION_FILE_VERSION}"
            )));
        }
        let mut m = Self {
            zone_id,
            ..Self::default()
        };
        m.bbox_min = read_vec3(&mut r, offset, true)?;
        m.bbox_max = read_vec3(&mut r, offset, true)?;
        let node_count = r.i32()?;
        for _ in 0..node_count.max(0) {
            let n = NodeDescriptor {
                id: r.i32()?,
                dir: read_vec3(&mut r, offset, false)?,
                up: read_vec3(&mut r, offset, false)?,
                pos: read_vec3(&mut r, offset, true)?,
                index: r.i32()?,
                obstacle: [r.i32()?, r.i32()?, r.i32()?],
                node_type: r.u8()?,
                unk1: r.u8()?,
                bit_field0: r.u8()?,
                bit_field1: r.u8()?,
            };
            let id = i64::from(n.id);
            if let std::collections::hash_map::Entry::Vacant(e) = m.node_by_id.entry(id) {
                e.insert(m.nodes.len());
                m.nodes.push(n);
            } else {
                tracing::debug!("Duplicate node ID {id}");
            }
        }
        let edge_count = r.i32()?;
        for _ in 0..edge_count.max(0) {
            let source_node = i64::from(r.u32()?);
            let target_node = i64::from(r.u32()?);
            let l = LinkDescriptor {
                source_node,
                target_node,
                edge_center: read_vec3(&mut r, offset, true)?,
                max_pass_radius: r.f32()?,
                exposure: r.f32()?,
                length: r.f32()?,
                max_water_depth: r.f32()?,
                min_water_depth: r.f32()?,
                start_index: r.u8()?,
                end_index: r.u8()?,
                is_pure_triangular_link: r.u8()? == 1,
                simple_passability_check: r.u8()? == 1,
                source: m.node_by_id.get(&source_node).copied(),
                target: m.node_by_id.get(&target_node).copied(),
            };
            m.links_by_source
                .entry(source_node)
                .or_default()
                .push(m.links.len());
            m.links.push(l);
        }
        Ok(m)
    }
}

/// `ObstacleDataDescriptor`.
#[derive(Debug, Clone, PartialEq)]
pub struct ObstacleDataDescriptor {
    pub pos: Vec3,
    pub dir: Vec3,
    pub approx_radius: f32,
    pub flags: u8,
    pub approx_height: u8,
    pub unk1: u8,
    pub unk2: u8,
}

/// `VertexMissionReader`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct VertexMission {
    pub zone_id: u32,
    pub obstacles: Vec<ObstacleDataDescriptor>,
}

impl VertexMission {
    pub fn read(data: &[u8], zone_id: u32, offset: Vec3) -> BResult<Self> {
        let mut r = Reader::new(data);
        let version = r.i32()?;
        if version > BAI_VERTEX_FILE_VERSION {
            return Err(BaiError(format!(
                "Wrong vertex list BAI file version {version} expected {BAI_VERTEX_FILE_VERSION}"
            )));
        }
        let mut m = Self {
            zone_id,
            ..Self::default()
        };
        let count = r.i32()?;
        for _ in 0..count.max(0) {
            m.obstacles.push(ObstacleDataDescriptor {
                pos: read_vec3(&mut r, offset, true)?,
                dir: read_vec3(&mut r, offset, false)?,
                approx_radius: r.f32()?,
                flags: r.u8()?,
                approx_height: r.u8()?,
                unk1: r.u8()?,
                unk2: r.u8()?,
            });
        }
        Ok(m)
    }
}

/// A node reference inside a [`BaseBaiLoader`] (reader index, node index).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct NodeRef {
    pub reader: usize,
    pub node: usize,
}

/// `Loaders/BaseBaiLoader`: every `.bai` file of a zone folder or of a 256 m path block.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct BaseBaiLoader {
    pub areas_mission_readers: Vec<AreasMission>,
    pub net_mission_readers: Vec<NetMission>,
    pub vertex_mission_readers: Vec<VertexMission>,
    pub hide_mission_readers: Vec<NetMission>,
}

impl BaseBaiLoader {
    /// `FindClosestNetMissionNode(pos)` (3D distance; the first node within 0.0001 wins).
    pub fn find_closest_net_mission_node(&self, pos: Vec3) -> Option<NodeRef> {
        let mut nearest: Option<NodeRef> = None;
        let mut nearest_distance = f32::MAX;
        for (ri, reader) in self.net_mission_readers.iter().enumerate() {
            for (ni, n) in reader.nodes.iter().enumerate() {
                let d = length(sub(n.pos, pos));
                if nearest.is_none() {
                    nearest = Some(NodeRef {
                        reader: ri,
                        node: ni,
                    });
                    nearest_distance = d;
                    continue;
                }
                if d < nearest_distance {
                    nearest = Some(NodeRef {
                        reader: ri,
                        node: ni,
                    });
                    nearest_distance = d;
                }
                if nearest_distance <= 0.0001 {
                    return nearest;
                }
            }
        }
        nearest
    }

    /// The node behind a reference.
    pub fn node(&self, r: NodeRef) -> &NodeDescriptor {
        &self.net_mission_readers[r.reader].nodes[r.node]
    }
}

pub fn sub(a: Vec3, b: Vec3) -> Vec3 {
    Vec3::new(a.x - b.x, a.y - b.y, a.z - b.z)
}

pub fn length(v: Vec3) -> f32 {
    (v.x * v.x + v.y * v.y + v.z * v.z).sqrt()
}

/// Where a bai folder's files come from (the client files).
pub trait BaiFiles {
    /// `ClientFileManager.GetFilesInDirectory(dir, pattern, true)`.
    fn files_in_directory(&self, dir: &str, pattern: &str) -> Vec<String>;
    /// `ClientFileManager.FileExists(name)`.
    fn file_exists(&self, name: &str) -> bool;
    /// `ClientFileManager.GetFileStream(name)` contents.
    fn read_file(&self, name: &str) -> Option<Vec<u8>>;
}

/// `GetZoneAndOffsetsByName(folderName)` → (zoneKey, pathBlockX, pathBlockY).
pub fn zone_and_offsets_by_name(folder: &str) -> (u32, u32, u32) {
    let (mut px, mut py) = (0u32, 0u32);
    if folder.contains('_') {
        let parts: Vec<&str> = folder.split('_').collect();
        if parts.len() == 2 {
            px = parts[0].parse().unwrap_or(0);
            py = parts[1].parse().unwrap_or(0);
        }
    }
    let zone_key = folder.parse().unwrap_or(0);
    (zone_key, px, py)
}

/// `Path.GetFileName(Path.GetDirectoryName(file))`: the name of the file's parent folder.
pub fn parent_folder_name(file: &str) -> String {
    let norm = file.replace('\\', "/");
    let mut parts: Vec<&str> = norm.split('/').collect();
    parts.pop();
    parts.last().map_or(String::new(), |s| s.to_string())
}

/// The world data `BaseBaiLoader.LoadBaiFilesFromFolder` reads.
pub struct BaiWorld<'a> {
    /// `ParentWorldTemplate.Name`.
    pub world_name: &'a str,
    /// `ParentWorldTemplate.XmlWorld.Zones` origins (zone key -> (originX, originY)).
    pub zone_origins: &'a HashMap<u32, (i32, i32)>,
    pub used_area_names: &'a UsedAreaNames,
}

impl BaseBaiLoader {
    /// `LoadBaiFilesFromFolder(zoneOrPathsFolder)` (not additive).
    pub fn load_from_folder(world: &BaiWorld<'_>, files: &dyn BaiFiles, folder: &str) -> Self {
        let mut l = Self::default();
        let mut root = format!("game/worlds/{}", world.world_name);
        if !folder.trim().is_empty() {
            let kind = if folder.contains('_') {
                "paths"
            } else {
                "zone"
            };
            root = format!("{root}/{kind}/{folder}");
        }
        let offset_of = |name: &str| -> (u32, Vec3) {
            let (zone_key, px, py) = zone_and_offsets_by_name(name);
            let off = match (zone_key != 0)
                .then(|| world.zone_origins.get(&zone_key))
                .flatten()
            {
                Some((ox, oy)) => Vec3::new(*ox as f32 * 1024.0, *oy as f32 * 1024.0, 0.0),
                None => Vec3::new(px as f32 * 256.0, py as f32 * 256.0, 0.0),
            };
            (zone_key, off)
        };
        tracing::debug!("LoadBaiFilesFromFolder {folder}");
        // AreasMission*.bai
        for file in files.files_in_directory(&root, "areasmission*.bai") {
            let parent = parent_folder_name(&file);
            if parent.trim().is_empty() || !files.file_exists(&file) {
                continue;
            }
            let (zone_key, off) = offset_of(&parent);
            let Some(data) = files.read_file(&file) else {
                continue;
            };
            if data.len() <= 20 {
                continue;
            }
            match AreasMission::read(&data, zone_key, off, world.used_area_names) {
                Ok(a) => l.areas_mission_readers.push(a),
                Err(e) => tracing::debug!(
                    "Areas File Exception: {}, in {file}, area offset {off:?}, skipping the rest of this file",
                    e.0
                ),
            }
        }
        // NetMission*.bai
        for file in files.files_in_directory(&root, "netmission*.bai") {
            let parent = parent_folder_name(&file);
            if parent.trim().is_empty() {
                continue;
            }
            let (zone_key, off) = offset_of(&parent);
            let res = files
                .read_file(&file)
                .ok_or_else(|| BaiError("Value cannot be null. (Parameter 'input')".into()))
                .and_then(|d| NetMission::read(&d, zone_key, off));
            match res {
                Ok(n) => l.net_mission_readers.push(n),
                Err(e) => tracing::debug!("Net File Exception: {}, in {file}", e.0),
            }
        }
        // VertexMission*.bai
        for file in files.files_in_directory(&root, "vertsmission*.bai") {
            let parent = parent_folder_name(&file);
            if parent.trim().is_empty() {
                continue;
            }
            let (zone_key, off) = offset_of(&parent);
            let Some(data) = files.read_file(&file) else {
                continue;
            };
            match VertexMission::read(&data, zone_key, off) {
                Ok(v) => l.vertex_mission_readers.push(v),
                Err(e) => tracing::debug!("Vertex File Exception: {}, in {file}", e.0),
            }
        }
        // HideMission*.bai
        for file in files.files_in_directory(&root, "hidemission*.bai") {
            let parent = parent_folder_name(&file);
            if parent.trim().is_empty() {
                continue;
            }
            let (zone_key, off) = offset_of(&parent);
            let res = files
                .read_file(&file)
                .ok_or_else(|| BaiError("Value cannot be null. (Parameter 'input')".into()))
                .and_then(|d| NetMission::read(&d, zone_key, off));
            match res {
                Ok(n) => l.hide_mission_readers.push(n),
                Err(e) => tracing::debug!("Hide File Exception: {}, in {file}", e.0),
            }
        }
        l
    }
}

#[cfg(test)]
pub(crate) mod test_files {
    /// Writer helpers for synthetic `.bai` files.
    #[derive(Default)]
    pub struct W(pub Vec<u8>);

    impl W {
        pub fn i32(&mut self, v: i32) -> &mut Self {
            self.0.extend_from_slice(&v.to_le_bytes());
            self
        }
        pub fn u32(&mut self, v: u32) -> &mut Self {
            self.0.extend_from_slice(&v.to_le_bytes());
            self
        }
        pub fn f32(&mut self, v: f32) -> &mut Self {
            self.0.extend_from_slice(&v.to_le_bytes());
            self
        }
        pub fn u8(&mut self, v: u8) -> &mut Self {
            self.0.push(v);
            self
        }
        pub fn v3(&mut self, x: f32, y: f32, z: f32) -> &mut Self {
            self.f32(x).f32(y).f32(z)
        }
        pub fn name(&mut self, n: &str) -> &mut Self {
            self.i32(n.len() as i32);
            self.0.extend_from_slice(n.as_bytes());
            self
        }
    }

    /// A net mission with `nodes` (id, pos) and `links` (source, target).
    pub fn net_mission(nodes: &[(i32, [f32; 3])], links: &[(u32, u32)]) -> Vec<u8> {
        let mut w = W::default();
        w.i32(55).v3(0.0, 0.0, 0.0).v3(1.0, 1.0, 1.0);
        w.i32(nodes.len() as i32);
        for (id, p) in nodes {
            w.i32(*id)
                .v3(1.0, 0.0, 0.0)
                .v3(0.0, 0.0, 1.0)
                .v3(p[0], p[1], p[2])
                .i32(0)
                .i32(-1)
                .i32(-1)
                .i32(-1)
                .u8(1)
                .u8(0)
                .u8(0)
                .u8(0);
        }
        w.i32(links.len() as i32);
        for (s, t) in links {
            w.u32(*s)
                .u32(*t)
                .v3(0.0, 0.0, 0.0)
                .f32(1.0)
                .f32(0.0)
                .f32(1.0)
                .f32(0.0)
                .f32(0.0)
                .u8(0)
                .u8(1)
                .u8(1)
                .u8(0);
        }
        w.0
    }

    /// An areas mission with forbidden areas only (path file layout, zone 0).
    pub fn areas_mission(forbidden: &[(&str, Vec<[f32; 3]>)]) -> Vec<u8> {
        let mut w = W::default();
        w.i32(21).u32(forbidden.len() as u32);
        for (n, pts) in forbidden {
            w.name(n).u32(pts.len() as u32);
            for p in pts {
                w.v3(p[0], p[1], p[2]);
            }
        }
        w.u32(0).u32(0).u32(0);
        w.0
    }
}

#[cfg(test)]
mod tests {
    use super::test_files::*;
    use super::*;

    #[test]
    fn net_mission_reads_nodes_links_and_offsets() {
        let data = net_mission(
            &[
                (1, [1.0, 2.0, 3.0]),
                (2, [4.0, 5.0, 6.0]),
                (1, [9.0, 9.0, 9.0]),
            ],
            &[(1, 2), (2, 1), (2, 7)],
        );
        let m = NetMission::read(&data, 0, Vec3::new(256.0, 512.0, 0.0)).unwrap();
        assert_eq!(m.nodes.len(), 2, "duplicate id keeps the first node");
        assert_eq!(m.nodes[0].pos, Vec3::new(257.0, 514.0, 3.0));
        assert_eq!(
            m.nodes[0].dir,
            Vec3::new(1.0, 0.0, 0.0),
            "no offset on directions"
        );
        assert_eq!(m.links.len(), 3);
        assert_eq!(m.links[0].source, Some(0));
        assert_eq!(m.links[0].target, Some(1));
        assert_eq!(m.links[2].target, None);
        assert_eq!(m.links_by_source[&2], vec![1, 2]);
        assert!(m.links[0].is_pure_triangular_link);
        assert!(!m.links[0].simple_passability_check);
    }

    #[test]
    fn versions_and_truncation_fail() {
        let mut w = W::default();
        w.i32(56);
        assert!(NetMission::read(&w.0, 0, Vec3::default()).is_err());
        let mut w = W::default();
        w.i32(22);
        assert!(AreasMission::read(&w.0, 0, Vec3::default(), &UsedAreaNames::default()).is_err());
        let data = net_mission(&[(1, [1.0, 2.0, 3.0])], &[]);
        assert!(NetMission::read(&data[..data.len() - 3], 0, Vec3::default()).is_err());
    }

    #[test]
    fn area_names_are_deduplicated_across_files() {
        let used = UsedAreaNames::default();
        let a = areas_mission(&[("Forbidden1", vec![[0.0, 0.0, 0.0], [1.0, 0.0, 0.0]])]);
        let m1 = AreasMission::read(&a, 0, Vec3::default(), &used).unwrap();
        let m2 = AreasMission::read(&a, 0, Vec3::default(), &used).unwrap();
        assert_eq!(m1.forbidden_areas.len(), 1);
        assert!(m2.forbidden_areas.is_empty());
    }

    #[test]
    fn folder_names() {
        assert_eq!(zone_and_offsets_by_name("012_034"), (0, 12, 34));
        assert_eq!(zone_and_offsets_by_name("129"), (129, 0, 0));
        assert_eq!(zone_and_offsets_by_name("1_2_3"), (0, 0, 0));
        assert_eq!(
            parent_folder_name("game/worlds/main_world/paths/012_034/netmission0.bai"),
            "012_034"
        );
    }
}
