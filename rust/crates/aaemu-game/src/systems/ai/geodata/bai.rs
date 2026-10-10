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
//! * duplicate node ids in a net mission keep the first node (`TryAdd`).
//!
//! Engine fixes (10.0.2.13 `cryaisystem_dedicate.dll`, see `PORTING.md` "geodata deviations"):
//! * areas: path sector files must be v21 (1.2) or v22 (10.x) (sector reader 0x31149764, same
//!   layout); the nav-modifier header is `u64 type, u64 waypointConnections, u8 altered` and the
//!   `ver <= 16` / `ver >= 18` branches use the file's version;
//! * net must be v55 (0x311030A8) and verts v2 (0x311A2AC4), plus the 1.2 pak's older net v54 /
//!   verts v1 sectors (same layouts; deliberate deviation for 1.2 data compatibility);
//! * links (0x310FB7E8): byte +42 bit 0 is the link flag 0x800000, byte +43 and the floats at
//!   +28..+36 are not read by the engine;
//! * `hidemission*.bai` (stale v54 graphs) is never loaded by the engine and is not loaded here.

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
    /// `u64` at header +0 (engine area +24).
    pub mission_type: i64,
    /// `u64` at header +8 (engine area +40, observed 2).
    pub waypoint_connections: i64,
    /// `u8` bool at header +16 (engine area +44).
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
    /// The file's version.
    pub version: i32,
    /// Bytes left after the last record of a path sector file (0 = the layout fits).
    pub trailing_bytes: usize,
}

/// `BaiAreasFileVersion` (the 1.2 client's areas files).
pub const BAI_AREAS_FILE_VERSION: i32 = 21;
/// The 10.x areas files (same layout as v21; sector reader 0x31149764 accepts `>= 21`).
pub const BAI_AREAS_FILE_VERSION_MAX: i32 = 22;
/// `BaiTriangulationFileVersion`.
pub const BAI_TRIANGULATION_FILE_VERSION: i32 = 55;
/// The older net version of 2 299 sectors of the 1.2 pak (same node / link layout; accepted for
/// 1.2 data compatibility, the 10.x engine wants exactly 55).
pub const BAI_TRIANGULATION_FILE_VERSION_12: i32 = 54;
/// `BaiVertexFileVersion`.
pub const BAI_VERTEX_FILE_VERSION: i32 = 2;
/// The older verts version paired with the v54 nets of the 1.2 pak (same 32-byte records).
pub const BAI_VERTEX_FILE_VERSION_12: i32 = 1;

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
        m.version = version;
        // Path sector files: the engine's sector reader (0x31149764) wants `>= 21` ("expected
        // 22"); v21 (1.2) and v22 (10.x) share the layout, later versions are unknown. Zone
        // folder files go through the whole-level reader (0x3107AB58), which still has the
        // `<= 16` / `>= 18` branches, so older versions stay accepted there (as in the C#).
        let too_old = zone_id == 0 && version < BAI_AREAS_FILE_VERSION;
        if too_old || version > BAI_AREAS_FILE_VERSION_MAX {
            return Err(BaiError(format!(
                "Wrong Areas BAI file version {version}, expected {BAI_AREAS_FILE_VERSION}..={BAI_AREAS_FILE_VERSION_MAX}"
            )));
        }
        let n = r.u32()?;
        for _ in 0..n {
            m.read_forbidden_area(&mut r, offset, ForbiddenKind::Forbidden, used)?;
        }
        let n = r.u32()?;
        for _ in 0..n {
            m.read_navigation_modifier(&mut r, offset, version, used)?;
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
            m.trailing_bytes = r.len() - r.pos;
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

    /// A navigation modifier (`SpecialArea`), engine layout (sector reader 0x31149764):
    /// `name; u64 type; u64 waypointConnections; u8 altered; f32 height; [ver <= 16: u32];
    /// f32 nodeAutoConnectDistance; f32 maxZ; f32 minZ; u32 buildingId; [ver >= 18: u8
    /// lightLevel]; points`.
    fn read_navigation_modifier(
        &mut self,
        r: &mut Reader<'_>,
        offset: Vec3,
        version: i32,
        used: &UsedAreaNames,
    ) -> BResult<()> {
        let name = read_name(r)?;
        let mission_type = r.i64()?;
        let waypoint_connections = r.i64()?;
        let altered = r.u8()? != 0;
        let height = r.f32()?;
        if version <= 16 {
            let _ = r.u32()?;
        }
        let node_auto_connect_distance = r.f32()?;
        let max_z = r.f32()?;
        let min_z = r.f32()?;
        let building_id = r.i32()?;
        let ai_light_level = if version >= 18 { r.u8()? } else { 0 };
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

/// `NodeDescriptor` (60 bytes).
#[derive(Debug, Clone, PartialEq)]
pub struct NodeDescriptor {
    pub id: i32,
    pub dir: Vec3,
    pub up: Vec3,
    /// World position (file position + the reader offset; the triangle centroid).
    pub pos: Vec3,
    /// The position as stored in the file (sector / zone local).
    pub local_pos: Vec3,
    pub index: i32,
    /// The triangle's corner indices into the same folder's verts file (-1 = none).
    pub obstacle: [i32; 3],
    /// +56: low byte of the u16 navType (2 = triangular, 4 = human waypoint).
    pub node_type: u8,
    /// +57: high byte of the u16 navType.
    pub unk1: u8,
    /// +58: node flags (low nibble = waypoint type; bits 4/5 = node flag bits 0/1).
    pub bit_field0: u8,
    pub bit_field1: u8,
}

impl NodeDescriptor {
    /// The u16 navType at +56.
    pub fn nav_type(&self) -> u16 {
        u16::from(self.node_type) | (u16::from(self.unk1) << 8)
    }
}

/// `LinkDescriptor` (44 bytes; `source` / `target` are indices into the reader's nodes).
#[derive(Debug, Clone, PartialEq)]
pub struct LinkDescriptor {
    pub source_node: i64,
    pub target_node: i64,
    pub edge_center: Vec3,
    /// +20: the widest agent that can pass (-1 = blocked / boundary edge).
    pub max_pass_radius: f32,
    /// +24: exposure-like cost factor (engine link +24).
    pub exposure: f32,
    /// +28, +32, +36: not read by the engine's link reader (0x310FB7E8); the C# called them
    /// length / maxWaterDepth / minWaterDepth (unverified). Observed (0, 0, 327.66).
    pub unread_28: [f32; 3],
    /// +40: the edge's first corner (0..2) -> engine link bits 28-29.
    pub start_index: u8,
    /// +41: the edge's second corner (0..2) -> engine link bits 30-31.
    pub end_index: u8,
    /// +42 raw (10.x: 9 / 165, 1.2: 1).
    pub flags_42: u8,
    /// +42 bit 0 -> engine link flag 0x800000 (the C# tested `== 1`, false for every 10.x
    /// link).
    pub is_pure_triangular_link: bool,
    /// +43 raw (49 / 2): not used by the engine.
    pub unused_43: u8,
    pub source: Option<usize>,
    pub target: Option<usize>,
}

/// `NetMissionReader` (`netmission*.bai`, CGraph v55).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct NetMission {
    pub zone_id: u32,
    /// The reader offset added to the file positions.
    pub offset: Vec3,
    /// The mission suffix of the file name (`netmission0.bai` -> "0"), pairs with the verts.
    pub mission: String,
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
    /// The file's version (55, or 54 in the stale 1.2 sectors).
    pub version: i32,
    /// Bytes left after the last link (0 = the layout fits).
    pub trailing_bytes: usize,
}

impl NetMission {
    /// `new NetMissionReader(stream, zoneId) { ReaderPointOffset }.ReadFile()`.
    pub fn read(data: &[u8], zone_id: u32, offset: Vec3) -> BResult<Self> {
        let mut r = Reader::new(data);
        let version = r.i32()?;
        // The 10.x engine (CGraph::ReadNodes) wants exactly 55. 2 299 sectors of the 1.2 pak
        // are v54 with the same node / link layout (as the v54 hide graphs): accepted for 1.2
        // data compatibility (deliberate deviation). The hide files themselves are still not
        // loaded (by file name, see `load_from_folder`).
        if version != BAI_TRIANGULATION_FILE_VERSION && version != BAI_TRIANGULATION_FILE_VERSION_12
        {
            return Err(BaiError(format!(
                "Wrong triangulation BAI file version {version} expected {BAI_TRIANGULATION_FILE_VERSION}"
            )));
        }
        let mut m = Self {
            zone_id,
            offset,
            version,
            ..Self::default()
        };
        m.bbox_min = read_vec3(&mut r, offset, true)?;
        m.bbox_max = read_vec3(&mut r, offset, true)?;
        let node_count = r.i32()?;
        for _ in 0..node_count.max(0) {
            let id = r.i32()?;
            let dir = read_vec3(&mut r, offset, false)?;
            let up = read_vec3(&mut r, offset, false)?;
            let local_pos = read_vec3(&mut r, offset, false)?;
            let n = NodeDescriptor {
                id,
                dir,
                up,
                pos: add(local_pos, offset),
                local_pos,
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
            let edge_center = read_vec3(&mut r, offset, true)?;
            let max_pass_radius = r.f32()?;
            let exposure = r.f32()?;
            let unread_28 = [r.f32()?, r.f32()?, r.f32()?];
            let start_index = r.u8()?;
            let end_index = r.u8()?;
            let flags_42 = r.u8()?;
            let unused_43 = r.u8()?;
            let l = LinkDescriptor {
                source_node,
                target_node,
                edge_center,
                max_pass_radius,
                exposure,
                unread_28,
                start_index,
                end_index,
                flags_42,
                is_pure_triangular_link: flags_42 & 1 != 0,
                unused_43,
                source: m.node_by_id.get(&source_node).copied(),
                target: m.node_by_id.get(&target_node).copied(),
            };
            m.links_by_source
                .entry(source_node)
                .or_default()
                .push(m.links.len());
            m.links.push(l);
        }
        m.trailing_bytes = r.len() - r.pos;
        Ok(m)
    }
}

/// `ObstacleDataDescriptor` (32 bytes; a triangle corner vertex).
#[derive(Debug, Clone, PartialEq)]
pub struct ObstacleDataDescriptor {
    /// World position (file position + the reader offset).
    pub pos: Vec3,
    /// The position as stored in the file.
    pub local_pos: Vec3,
    pub dir: Vec3,
    pub approx_radius: f32,
    pub flags: u8,
    pub approx_height: u8,
    pub unk1: u8,
    pub unk2: u8,
}

/// `VertexMissionReader` (`vertsmission*.bai`, CVertexList v2).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct VertexMission {
    pub zone_id: u32,
    /// The reader offset added to the file positions.
    pub offset: Vec3,
    /// The mission suffix of the file name (`vertsmission0.bai` -> "0").
    pub mission: String,
    pub obstacles: Vec<ObstacleDataDescriptor>,
    /// The file's version (2, or 1 in the stale 1.2 sectors).
    pub version: i32,
    /// Bytes left after the last record (0 = the layout fits).
    pub trailing_bytes: usize,
}

impl VertexMission {
    pub fn read(data: &[u8], zone_id: u32, offset: Vec3) -> BResult<Self> {
        let mut r = Reader::new(data);
        let version = r.i32()?;
        // The 10.x engine's verts reader (0x311A2AC4) wants exactly 2; the v54 sectors of the
        // 1.2 pak have v1 verts with the same 32-byte records (1.2 data compatibility).
        if version != BAI_VERTEX_FILE_VERSION && version != BAI_VERTEX_FILE_VERSION_12 {
            return Err(BaiError(format!(
                "Wrong vertex list BAI file version {version} expected {BAI_VERTEX_FILE_VERSION}"
            )));
        }
        let mut m = Self {
            zone_id,
            offset,
            version,
            ..Self::default()
        };
        let count = r.i32()?;
        for _ in 0..count.max(0) {
            let local_pos = read_vec3(&mut r, offset, false)?;
            m.obstacles.push(ObstacleDataDescriptor {
                pos: add(local_pos, offset),
                local_pos,
                dir: read_vec3(&mut r, offset, false)?,
                approx_radius: r.f32()?,
                flags: r.u8()?,
                approx_height: r.u8()?,
                unk1: r.u8()?,
                unk2: r.u8()?,
            });
        }
        m.trailing_bytes = r.len() - r.pos;
        Ok(m)
    }
}

/// A node reference inside a [`BaseBaiLoader`] (reader index, node index).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct NodeRef {
    pub reader: usize,
    pub node: usize,
}

/// `Loaders/BaseBaiLoader`: every `.bai` file of a zone folder or of a 256 m path block
/// (`hidemission*.bai` is not loaded: the engine never reads it).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct BaseBaiLoader {
    /// The folder the files came from (`"038_058"`, a zone key, or "" for the world root).
    pub folder: String,
    pub areas_mission_readers: Vec<AreasMission>,
    pub net_mission_readers: Vec<NetMission>,
    pub vertex_mission_readers: Vec<VertexMission>,
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

pub fn add(a: Vec3, b: Vec3) -> Vec3 {
    Vec3::new(a.x + b.x, a.y + b.y, a.z + b.z)
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

/// The mission part of a `.bai` file name (`.../netmission0.bai` -> "0").
pub fn mission_suffix(file: &str, prefix: &str) -> String {
    let name = file.rsplit(['/', '\\']).next().unwrap_or("").to_lowercase();
    name.strip_prefix(prefix)
        .and_then(|n| n.strip_suffix(".bai"))
        .unwrap_or("")
        .to_string()
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
        let mut l = Self {
            folder: folder.to_string(),
            ..Self::default()
        };
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
                Ok(mut n) => {
                    n.mission = mission_suffix(&file, "netmission");
                    l.net_mission_readers.push(n)
                }
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
                Ok(mut v) => {
                    v.mission = mission_suffix(&file, "vertsmission");
                    l.vertex_mission_readers.push(v)
                }
                Err(e) => tracing::debug!("Vertex File Exception: {}, in {file}", e.0),
            }
        }
        // HideMission*.bai: never loaded (no engine reader references it; the files are
        // stale v54 graphs)
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

    /// A node of [`net_mission_full`].
    pub struct TNode {
        pub id: i32,
        pub pos: [f32; 3],
        pub tri: [i32; 3],
        pub nav_type: u16,
        pub flags: u8,
    }

    impl TNode {
        /// A triangle node (navType 2).
        pub fn tri(id: i32, pos: [f32; 3], tri: [i32; 3]) -> Self {
            Self {
                id,
                pos,
                tri,
                nav_type: 2,
                flags: 0,
            }
        }
    }

    /// A link of [`net_mission_full`].
    pub struct TLink {
        pub src: u32,
        pub dst: u32,
        pub radius: f32,
        pub b42: u8,
        pub b43: u8,
    }

    impl TLink {
        pub fn new(src: u32, dst: u32, radius: f32) -> Self {
            Self {
                src,
                dst,
                radius,
                b42: 9,
                b43: 49,
            }
        }
    }

    /// A net mission v55 with triangle nodes and per-link pass radius / flag bytes.
    pub fn net_mission_full(nodes: &[TNode], links: &[TLink]) -> Vec<u8> {
        net_mission_versioned(55, nodes, links)
    }

    pub fn net_mission_versioned(version: i32, nodes: &[TNode], links: &[TLink]) -> Vec<u8> {
        let mut w = W::default();
        w.i32(version).v3(0.0, 0.0, 0.0).v3(8192.0, 8192.0, 0.0);
        w.i32(nodes.len() as i32);
        for n in nodes {
            w.i32(n.id)
                .v3(0.0, 0.0, 0.0)
                .v3(0.0, 0.0, 0.0)
                .v3(n.pos[0], n.pos[1], n.pos[2])
                .i32(0)
                .i32(n.tri[0])
                .i32(n.tri[1])
                .i32(n.tri[2])
                .u8(n.nav_type as u8)
                .u8((n.nav_type >> 8) as u8)
                .u8(n.flags)
                .u8(0);
        }
        w.i32(links.len() as i32);
        for l in links {
            w.u32(l.src)
                .u32(l.dst)
                .v3(0.0, 0.0, 0.0)
                .f32(l.radius)
                .f32(0.5)
                .f32(0.0)
                .f32(0.0)
                .f32(327.66)
                .u8(0)
                .u8(1)
                .u8(l.b42)
                .u8(l.b43);
        }
        w.0
    }

    /// A verts mission v2.
    pub fn verts_mission(points: &[[f32; 3]]) -> Vec<u8> {
        let mut w = W::default();
        w.i32(2).i32(points.len() as i32);
        for p in points {
            w.v3(p[0], p[1], p[2])
                .v3(0.0, 0.0, 0.0)
                .f32(0.0)
                .u8(1)
                .u8(0)
                .u8(0)
                .u8(0);
        }
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
        assert_eq!(m.links[0].unused_43, 0);
        assert_eq!(m.offset, Vec3::new(256.0, 512.0, 0.0));
        assert_eq!(m.nodes[0].local_pos, Vec3::new(1.0, 2.0, 3.0));
    }

    #[test]
    fn versions_and_truncation_fail() {
        let mut w = W::default();
        w.i32(56);
        assert!(NetMission::read(&w.0, 0, Vec3::default()).is_err());
        let data = net_mission(&[(1, [1.0, 2.0, 3.0])], &[]);
        assert!(NetMission::read(&data[..data.len() - 3], 0, Vec3::default()).is_err());
    }

    /// Net v55 / verts v2 (10.x engine) plus the 1.2 pak's older net v54 / verts v1 (same
    /// layout); anything else is rejected.
    #[test]
    fn net_and_verts_versions() {
        let node = [TNode::tri(1, [1.0, 1.0, 0.0], [0, 1, 2])];
        for (v, ok) in [
            (53, false),
            (54, true),
            (55, true),
            (56, false),
            (21, false),
        ] {
            let d = net_mission_versioned(v, &node, &[]);
            let r = NetMission::read(&d, 0, Vec3::default());
            assert_eq!(r.is_ok(), ok, "net v{v}");
            if let Ok(m) = r {
                assert_eq!((m.version, m.trailing_bytes), (v, 0));
            }
        }
        let mut d = net_mission_full(&node, &[]);
        d.push(0);
        assert_eq!(
            NetMission::read(&d, 0, Vec3::default())
                .unwrap()
                .trailing_bytes,
            1
        );
        let mut d = verts_mission(&[[0.0, 0.0, 0.0]]);
        assert!(VertexMission::read(&d, 0, Vec3::default()).is_ok());
        d[..4].copy_from_slice(&1i32.to_le_bytes());
        let v1 = VertexMission::read(&d, 0, Vec3::default()).unwrap();
        assert_eq!(
            (v1.version, v1.trailing_bytes, v1.obstacles.len()),
            (1, 0, 1)
        );
        for v in [0i32, 3] {
            d[..4].copy_from_slice(&v.to_le_bytes());
            assert!(
                VertexMission::read(&d, 0, Vec3::default()).is_err(),
                "verts v{v}"
            );
        }
        let v = VertexMission::read(
            &verts_mission(&[[1.0, 2.0, 3.0]]),
            0,
            Vec3::new(256.0, 0.0, 0.0),
        )
        .unwrap();
        assert_eq!(v.obstacles[0].pos, Vec3::new(257.0, 2.0, 3.0));
        assert_eq!(v.obstacles[0].local_pos, Vec3::new(1.0, 2.0, 3.0));
    }

    /// Areas: v21 (1.2) and v22 (10.x) share the layout; path sector files below 21 and any
    /// file above 22 are rejected.
    #[test]
    fn areas_versions_21_and_22() {
        let used = UsedAreaNames::default();
        for (v, zone, ok) in [
            (20i32, 0, false),
            (21, 0, true),
            (22, 0, true),
            (23, 0, false),
            (18, 5, true),
            (23, 5, false),
        ] {
            let mut d = areas_mission(&[]);
            d[..4].copy_from_slice(&v.to_le_bytes());
            if zone != 0 {
                // zone files carry extra link costs, an i64 and designer paths
                d.extend_from_slice(&0u32.to_le_bytes());
                d.extend_from_slice(&0i64.to_le_bytes());
                d.extend_from_slice(&0u32.to_le_bytes());
            }
            let r = AreasMission::read(&d, zone, Vec3::default(), &used);
            assert_eq!(r.is_ok(), ok, "areas v{v} zone {zone}: {r:?}");
        }
    }

    fn nav_modifier_file(version: i32) -> Vec<u8> {
        let mut w = W::default();
        w.i32(version).u32(0).u32(1);
        w.name("navmod");
        w.0.extend_from_slice(&0x1_0000_0003u64.to_le_bytes());
        w.0.extend_from_slice(&2u64.to_le_bytes());
        w.u8(0).f32(4.0);
        if version <= 16 {
            w.u32(0xdead_beef);
        }
        w.f32(8.0).f32(240.0).f32(226.0).i32(25);
        if version >= 18 {
            w.u8(7);
        }
        w.u32(1).v3(1.0, 2.0, 3.0);
        w.u32(0).u32(0);
        w.0
    }

    /// The nav-modifier header is `u64, u64, u8 bool` (17 bytes) and the version branches use
    /// the file's version.
    #[test]
    fn nav_modifier_header_layout() {
        for (version, zone) in [(22, 0), (21, 0), (17, 9), (16, 9)] {
            let mut d = nav_modifier_file(version);
            if zone != 0 {
                d.extend_from_slice(&0u32.to_le_bytes());
                d.extend_from_slice(&0i64.to_le_bytes());
                d.extend_from_slice(&0u32.to_le_bytes());
            }
            let m = AreasMission::read(
                &d,
                zone,
                Vec3::new(10.0, 0.0, 0.0),
                &UsedAreaNames::default(),
            )
            .unwrap_or_else(|e| panic!("v{version}: {e:?}"));
            let a = &m.navigation_modifiers[0];
            assert_eq!(a.mission_type, 0x1_0000_0003, "v{version}");
            assert_eq!(a.waypoint_connections, 2);
            assert!(!a.altered, "the bool is the 17th header byte");
            assert_eq!(a.height, 4.0);
            assert_eq!(a.node_auto_connect_distance, 8.0);
            assert_eq!(a.max_z, 240.0);
            assert_eq!(a.min_z, 226.0);
            assert_eq!(a.building_id, 25);
            assert_eq!(a.ai_light_level, if version >= 18 { 7 } else { 0 });
            assert_eq!(a.points, vec![Vec3::new(11.0, 2.0, 3.0)]);
        }
    }

    /// Link byte +42 bit 0 is the engine flag (10.x values 9 / 165, 1.2 value 1); +43 is kept
    /// raw and means nothing.
    #[test]
    fn link_flag_byte_42_bit0() {
        let nodes = [
            TNode::tri(1, [1.0, 1.0, 0.0], [-1, -1, -1]),
            TNode::tri(2, [2.0, 1.0, 0.0], [-1, -1, -1]),
        ];
        let mk = |b42: u8, b43: u8| TLink {
            b42,
            b43,
            ..TLink::new(1, 2, 0.5)
        };
        let d = net_mission_full(
            &nodes,
            &[mk(9, 49), mk(165, 2), mk(1, 1), mk(2, 1), mk(0, 1)],
        );
        let m = NetMission::read(&d, 0, Vec3::default()).unwrap();
        let bits: Vec<bool> = m.links.iter().map(|l| l.is_pure_triangular_link).collect();
        assert_eq!(bits, vec![true, true, true, false, false]);
        assert_eq!(m.links[0].flags_42, 9);
        assert_eq!(m.links[0].unused_43, 49);
        assert_eq!(m.links[0].unread_28, [0.0, 0.0, 327.66]);
        assert_eq!(m.links[0].max_pass_radius, 0.5);
        assert_eq!(m.nodes[0].nav_type(), 2);
    }

    /// `hidemission*.bai` is not loaded; the net / verts readers are paired by mission name.
    #[test]
    fn folder_loads_without_hide_files() {
        struct Files(HashMap<String, Vec<u8>>);
        impl BaiFiles for Files {
            fn files_in_directory(&self, dir: &str, pattern: &str) -> Vec<String> {
                let pre = pattern.split('*').next().unwrap_or("");
                let mut v: Vec<String> = self
                    .0
                    .keys()
                    .filter(|k| k.starts_with(dir))
                    .filter(|k| k.rsplit('/').next().unwrap_or("").starts_with(pre))
                    .cloned()
                    .collect();
                v.sort();
                v
            }
            fn file_exists(&self, name: &str) -> bool {
                self.0.contains_key(name)
            }
            fn read_file(&self, name: &str) -> Option<Vec<u8>> {
                self.0.get(name).cloned()
            }
        }
        let dir = "game/worlds/w/paths/001_002/";
        let node = [TNode::tri(1, [1.0, 1.0, 0.0], [0, 1, 2])];
        let mut f = HashMap::new();
        f.insert(
            format!("{dir}netmission0.bai"),
            net_mission_full(&node, &[]),
        );
        f.insert(
            format!("{dir}hidemission0.bai"),
            net_mission_versioned(54, &node, &[]),
        );
        f.insert(
            format!("{dir}vertsmission0.bai"),
            verts_mission(&[[0.0; 3], [1.0, 0.0, 0.0], [0.0, 1.0, 0.0]]),
        );
        let origins = HashMap::new();
        let used = UsedAreaNames::default();
        let world = BaiWorld {
            world_name: "w",
            zone_origins: &origins,
            used_area_names: &used,
        };
        let l = BaseBaiLoader::load_from_folder(&world, &Files(f), "001_002");
        assert_eq!(l.folder, "001_002");
        assert_eq!(
            l.net_mission_readers.len(),
            1,
            "the hide graph is not a net mission"
        );
        assert_eq!(l.net_mission_readers[0].mission, "0");
        assert_eq!(l.vertex_mission_readers[0].mission, "0");
        assert_eq!(
            l.net_mission_readers[0].nodes[0].pos,
            Vec3::new(257.0, 513.0, 0.0)
        );
        assert_eq!(
            mission_suffix("a\\b\\VertsMission12.bai", "vertsmission"),
            "12"
        );
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
