//! `Core/Managers/AiGeodataManager` (`AiGeoDataManager`, one per world template) and the
//! navmesh part of `WorldTemplate` / `WorldCell` (`LoadZoneBaiFiles`, `WorldCell.LoadBaiFiles`,
//! `GetBaiByPos`).
//!
//! Every folder that loads is also merged into the template's [`NavGraph`] (engine-style
//! cross-sector graph, see [`super::graph`]); A* and `GetHeight` use it.

use std::collections::{HashMap, VecDeque};
use std::sync::{Arc, Mutex, OnceLock, RwLock, RwLockReadGuard};

use crate::data::world_templates::{WorldTemplate, CELL_SIZE, SECTORS_PER_CELL};
use crate::systems::skills::controllers::{calculate_distance, Vec3};

use super::bai::{
    length, sub, BaiFiles, BaiWorld, BaseBaiLoader, LinkDescriptor, NodeRef, UsedAreaNames,
};
use super::graph::NavGraph;

/// navType of a triangle node.
pub const NAV_TRIANGULAR: u16 = 2;

/// `Vector3.ToCellIndex()`.
pub fn to_cell_index(p: Vec3) -> (i32, i32) {
    (
        (f64::from(p.x / 1024.0)).floor() as i32,
        (f64::from(p.y / 1024.0)).floor() as i32,
    )
}

/// `Vector3.ToPathsIndex()`.
pub fn to_paths_index(p: Vec3) -> (i32, i32) {
    (
        (f64::from(p.x / 256.0)).floor() as i32,
        (f64::from(p.y / 256.0)).floor() as i32,
    )
}

/// An exception thrown inside a geodata lookup (`IndexOutOfRangeException`,
/// `NullReferenceException`).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct GeoError;

impl std::fmt::Display for GeoError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str("geodata lookup failed")
    }
}

impl std::error::Error for GeoError {}

/// The raw heightmap height lookup the geodata falls back to
/// (`WorldTemplate.GetRawHeightMapHeight(x, y)` / `GetHeight(x, y)`), provided by the
/// heightmap owner.
pub trait TerrainHeights: Send + Sync {
    /// `GetRawHeightMapHeight((int)x, (int)y)` (`Err` = the C# exception).
    fn raw_height(&self, x: i32, y: i32) -> Result<f32, GeoError>;
    /// `WorldTemplate.GetHeight(x, y)` (bilinear).
    fn height(&self, x: f32, y: f32) -> Result<f32, GeoError>;
}

/// A loaded bai set shared by a zone or a path block.
pub type BaiRef = Arc<BaseBaiLoader>;

/// `AiGeoDataManager` + the bai data of its `WorldTemplate`.
pub struct AiGeoData {
    pub template_id: u32,
    pub world_name: String,
    /// `CellX` / `CellY`.
    pub cell_x: i32,
    pub cell_y: i32,
    /// `ZoneKeyByRegions[cellX * 16, cellY * 16]` for every cell (`x * cell_y + y`).
    cell_zone_keys: Vec<u32>,
    zone_keys: Vec<u32>,
    zone_origins: HashMap<u32, (i32, i32)>,
    /// `GeoDataMode` (bai files are only read when set).
    pub enabled: bool,
    files: Arc<dyn BaiFiles + Send + Sync>,
    used_names: Arc<UsedAreaNames>,
    /// `ZoneBaiLoader` (zone key -> loader), in `ZoneKeys` order.
    zone_bai: OnceLock<Vec<(u32, BaiRef)>>,
    /// `WorldCell.BaiLoader[4, 4]` per loaded cell (`x * cell_y + y`).
    cells: Vec<OnceLock<[[Option<BaiRef>; 4]; 4]>>,
    /// `PathBaiLoader`.
    path_bai: Mutex<HashMap<(u32, u32), BaiRef>>,
    terrain: Option<Arc<dyn TerrainHeights>>,
    /// Every loaded folder merged into one graph (never take a loading call while holding it).
    graph: RwLock<NavGraph>,
}

impl std::fmt::Debug for AiGeoData {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("AiGeoData")
            .field("template_id", &self.template_id)
            .field("world_name", &self.world_name)
            .field("enabled", &self.enabled)
            .finish()
    }
}

impl AiGeoData {
    /// `new AiGeoDataManager(worldTemplate)` (+ the template's bai state).
    pub fn new(
        template: &WorldTemplate,
        enabled: bool,
        files: Arc<dyn BaiFiles + Send + Sync>,
        used_names: Arc<UsedAreaNames>,
        terrain: Option<Arc<dyn TerrainHeights>>,
    ) -> Self {
        let mut cell_zone_keys = Vec::new();
        for cx in 0..template.cell_x {
            for cy in 0..template.cell_y {
                cell_zone_keys.push(
                    template
                        .zone_key_at(cx * SECTORS_PER_CELL, cy * SECTORS_PER_CELL)
                        .unwrap_or(0),
                );
            }
        }
        let count = (template.cell_x.max(0) * template.cell_y.max(0)) as usize;
        Self {
            template_id: template.id,
            world_name: template.name.clone(),
            cell_x: template.cell_x,
            cell_y: template.cell_y,
            cell_zone_keys,
            zone_keys: template.zone_keys.clone(),
            zone_origins: template
                .xml_world
                .zones
                .iter()
                .map(|(k, z)| (*k, (z.origin_x, z.origin_y)))
                .collect(),
            enabled,
            files,
            used_names,
            zone_bai: OnceLock::new(),
            cells: (0..count).map(|_| OnceLock::new()).collect(),
            path_bai: Mutex::new(HashMap::new()),
            terrain,
            graph: RwLock::new(NavGraph::default()),
        }
    }

    fn bai_world(&self) -> BaiWorld<'_> {
        BaiWorld {
            world_name: &self.world_name,
            zone_origins: &self.zone_origins,
            used_area_names: &self.used_names,
        }
    }

    /// `WorldTemplate.LoadZoneBaiFiles()` (done once; nothing without `GeoDataMode`).
    pub fn zone_bai(&self) -> &[(u32, BaiRef)] {
        self.zone_bai.get_or_init(|| {
            let mut out = Vec::new();
            if !self.enabled {
                return out;
            }
            for &zone_key in &self.zone_keys {
                let folder = format!("game/worlds/{}/zone/{zone_key}", self.world_name);
                // GetFilesInDirectory(folder, "*.bai", false)
                if self.files.files_in_directory(&folder, "*.bai").is_empty() {
                    continue;
                }
                let loader = BaseBaiLoader::load_from_folder(
                    &self.bai_world(),
                    &*self.files,
                    &zone_key.to_string(),
                );
                out.push((zone_key, Arc::new(loader)));
            }
            let mut graph = self.graph.write().expect("graph poisoned");
            for (_, l) in &out {
                graph.merge(l);
            }
            out
        })
    }

    /// The merged navigation graph (read lock: do not call the loading functions —
    /// `zone_bai`, `cell_bai`, `bai_by_pos`, `get_height`, ... — while holding it).
    pub fn graph(&self) -> RwLockReadGuard<'_, NavGraph> {
        self.graph.read().expect("graph poisoned")
    }

    /// `WorldCell.LoadBaiFiles()` for one cell (once).
    pub fn cell_bai(&self, cell_x: i32, cell_y: i32) -> Option<&[[Option<BaiRef>; 4]; 4]> {
        if cell_x < 0 || cell_y < 0 || cell_x >= self.cell_x || cell_y >= self.cell_y {
            return None;
        }
        let idx = (cell_x * self.cell_y + cell_y) as usize;
        Some(self.cells[idx].get_or_init(|| {
            let mut grid: [[Option<BaiRef>; 4]; 4] = Default::default();
            if !self.enabled {
                return grid;
            }
            let zones = self.zone_bai();
            if !zones.is_empty() {
                let zone_key = self.cell_zone_keys[idx];
                match zones.iter().find(|(k, _)| *k == zone_key) {
                    Some((_, l)) => {
                        for row in grid.iter_mut() {
                            for slot in row.iter_mut() {
                                *slot = Some(l.clone());
                            }
                        }
                    }
                    None => tracing::warn!(
                        "WorldTemplate {} has Zone bai files data loaded ({} zones), but could not find a matching file for this cell {zone_key}",
                        self.world_name,
                        zones.len()
                    ),
                }
                return grid;
            }
            // Load the 4x4 grid of path folders into this cell
            for y in 0..4 {
                for x in 0..4 {
                    let path_x = (cell_x * 4 + x) as u32;
                    let path_y = (cell_y * 4 + y) as u32;
                    let folder = format!("{path_x:03}_{path_y:03}");
                    let loader =
                        Arc::new(BaseBaiLoader::load_from_folder(&self.bai_world(), &*self.files, &folder));
                    self.graph.write().expect("graph poisoned").merge(&loader);
                    grid[x as usize][y as usize] = Some(loader.clone());
                    let mut paths = self.path_bai.lock().expect("bai poisoned");
                    match paths.entry((path_x, path_y)) {
                        std::collections::hash_map::Entry::Occupied(_) => tracing::warn!(
                            "PathBaiLoader already contains key ({path_x}, {path_y}) for template {}",
                            self.world_name
                        ),
                        std::collections::hash_map::Entry::Vacant(e) => {
                            e.insert(loader);
                        }
                    }
                }
            }
            grid
        }))
    }

    /// `WorldTemplate.GetBaiByPos(pos)` (`Err` = `IndexOutOfRangeException` for a cell outside
    /// the world).
    pub fn bai_by_pos(&self, pos: Vec3) -> Result<Option<BaiRef>, GeoError> {
        let zones = self.zone_bai();
        if let Some((_, l)) = zones.first() {
            return Ok(Some(l.clone())); // TODO (C#): pick the actually correct zone
        }
        let (cx, cy) = to_cell_index(pos);
        if cx < 0 || cy < 0 || cx >= self.cell_x || cy >= self.cell_y {
            return Err(GeoError);
        }
        self.cell_bai(cx, cy);
        let (px, py) = to_paths_index(pos);
        Ok(self
            .path_bai
            .lock()
            .expect("bai poisoned")
            .get(&(px as u32, py as u32))
            .cloned())
    }

    /// `GetAvailablePoints(point)`: the links whose source is the node.
    pub fn available_points<'a>(
        &self,
        bai: &'a BaseBaiLoader,
        node: NodeRef,
    ) -> Vec<&'a LinkDescriptor> {
        let reader = &bai.net_mission_readers[node.reader];
        let id = i64::from(reader.nodes[node.node].id);
        reader
            .links_by_source
            .get(&id)
            .map(|v| v.iter().map(|i| &reader.links[*i]).collect())
            .unwrap_or_default()
    }

    /// `CheckImpossibleWalk(point)`: inside a forbidden area of the position's bai.
    pub fn check_impossible_walk(&self, point: Vec3) -> bool {
        if let Ok(Some(bai)) = self.bai_by_pos(point) {
            for area in &bai.areas_mission_readers {
                for f in &area.forbidden_areas {
                    if is_in_polygon(point, &f.points) {
                        return true;
                    }
                }
            }
        }
        false
    }

    /// `FindСlosestToTheCurrent(zoneKey, pos)`: the nearest net node of the cell's bai (the
    /// zone's when `zoneKey` has a zone bai).
    pub fn find_closest_to_the_current(
        &self,
        zone_key: u32,
        pos: Vec3,
    ) -> Option<(BaiRef, NodeRef)> {
        let (cx, cy) = to_cell_index(pos);
        // GetCell: `cellX > CellX` bound (== CellX throws in C#)
        if cx < 0 || cx > self.cell_x || cy < 0 || cy > self.cell_y {
            return None;
        }
        let zones = self.zone_bai();
        let mut check: Vec<BaiRef> = Vec::new();
        if !zones.is_empty() {
            match zones.iter().find(|(k, _)| *k == zone_key) {
                Some((_, l)) => check.push(l.clone()),
                None => check.extend(zones.iter().map(|(_, l)| l.clone())),
            }
        } else {
            let grid = self.cell_bai(cx, cy)?;
            // foreach (var bai in cell.BaiLoader): row-major over [x, y]
            for row in grid {
                for l in row.iter().flatten() {
                    check.push(l.clone());
                }
            }
        }
        let mut best: Option<(BaiRef, NodeRef)> = None;
        let mut min_dist = 99999.0f32;
        for l in &check {
            for (ri, reader) in l.net_mission_readers.iter().enumerate() {
                for (ni, n) in reader.nodes.iter().enumerate() {
                    let d = length(sub(n.pos, pos));
                    if d < min_dist {
                        best = Some((
                            l.clone(),
                            NodeRef {
                                reader: ri,
                                node: ni,
                            },
                        ));
                        min_dist = d;
                    }
                }
            }
        }
        best
    }

    /// Loads the cells a graph node's triangle touches (+0.5 m), so the copies of a border
    /// triangle in the neighbouring sectors are merged before its links are followed.
    pub fn ensure_node_cells(&self, node: u32) {
        if !self.enabled || !self.zone_bai().is_empty() {
            return;
        }
        let Some(((x0, y0), (x1, y1))) = self.graph().node_bounds(node) else {
            return;
        };
        let cell = |v: f64| (v / f64::from(CELL_SIZE)).floor() as i32;
        let (cx0, cy0) = (cell(x0 - 0.5), cell(y0 - 0.5));
        let (cx1, cy1) = (cell(x1 + 0.5).min(cx0 + 2), cell(y1 + 0.5).min(cy0 + 2));
        for cx in cx0..=cx1 {
            for cy in cy0..=cy1 {
                self.cell_bai(cx, cy);
            }
        }
    }

    /// The graph node a path search starts / ends at: the triangle enclosing the point
    /// (engine `GetEnclosing`), else the C# `FindClosestToTheCurrent` node. Returns the graph
    /// node and its position.
    ///
    /// The enclosing triangle is not used when the C# nearest node is not a triangle node: the
    /// zone instances navigate indoors on human waypoints (navType 4) under a handful of huge
    /// placeholder triangles, where the waypoint graph is the right one (the engine selects it
    /// through the indoor navigation modifiers, which are not modelled).
    pub fn resolve_nav_node(&self, zone_key: u32, pos: Vec3) -> Option<(u32, Vec3)> {
        // loads the cell like the C#
        let closest = self.find_closest_to_the_current(zone_key, pos);
        let graph = self.graph();
        let waypoints = closest
            .as_ref()
            .is_some_and(|(bai, r)| bai.node(*r).nav_type() != NAV_TRIANGULAR);
        if !waypoints {
            if let Some((n, _)) = graph.enclosing(pos) {
                return Some((n, graph.nodes[n as usize].pos));
            }
        }
        let (bai, r) = closest?;
        let n = graph.node_for(&bai.folder, r)?;
        Some((n, graph.nodes[n as usize].pos))
    }

    /// The navmesh height at a point: the plane of the enclosing triangle (`GetEnclosing`).
    /// Only looks at loaded data.
    pub fn enclosing_height(&self, pos: Vec3) -> Option<f32> {
        self.graph().enclosing(pos).map(|(_, z)| z)
    }

    /// `GetHeight(pos)`: the height of the navmesh triangle enclosing the point (interpolated
    /// on its plane, engine `GetEnclosing`) unless the nearest node of the position's bai is a
    /// waypoint (see [`Self::resolve_nav_node`]); otherwise the Z of
    /// the nearest navmesh node / obstacle vertex of the position's bai (the C#), else the raw
    /// heightmap height at the rounded position (0 on an exception).
    pub fn get_height(&self, pos: Vec3) -> f32 {
        let mut closest_point = Vec3::default();
        let mut closest_distance = f32::MAX;
        match self.bai_by_pos(pos) {
            Err(GeoError) => return 0.0,
            Ok(Some(bai)) => {
                let mut closest_nav_type = 0u16;
                let mut exact = false;
                'nodes: for net in &bai.net_mission_readers {
                    for n in &net.nodes {
                        let d = length(sub(n.pos, pos));
                        if d < closest_distance {
                            closest_distance = d;
                            closest_point = n.pos;
                            closest_nav_type = n.nav_type();
                            if closest_distance < 0.01 {
                                exact = true;
                                break 'nodes;
                            }
                        }
                    }
                }
                // no node in this sector (a neighbour's triangle may cover it) or a triangle
                if closest_distance == f32::MAX || closest_nav_type == NAV_TRIANGULAR {
                    if let Some(z) = self.enclosing_height(pos) {
                        return z;
                    }
                }
                if exact {
                    return closest_point.z;
                }
                for v in &bai.vertex_mission_readers {
                    for o in &v.obstacles {
                        let d = length(sub(o.pos, pos));
                        if d < closest_distance {
                            closest_distance = d;
                            closest_point = o.pos;
                            if closest_distance < 0.01 {
                                return closest_point.z;
                            }
                        }
                    }
                }
            }
            Ok(None) => {}
        }
        if closest_distance >= f32::MAX {
            // Fall back to raw heightmap data
            let raw = match &self.terrain {
                Some(t) => t.raw_height(
                    pos.x.round_ties_even() as i32,
                    pos.y.round_ties_even() as i32,
                ),
                None => Ok(0.0),
            };
            return raw.unwrap_or(0.0);
        }
        closest_point.z
    }

    /// `ReducePath(foundPath, maxNodeSkipCount)`.
    pub fn reduce_path(
        &self,
        found: &[Vec3],
        max_node_skip: usize,
    ) -> Result<VecDeque<Vec3>, GeoError> {
        let mut res = VecDeque::new();
        let mut start = 0usize;
        while start < found.len() {
            let start_node = found[start];
            res.push_back(start_node);
            let mut end = start + max_node_skip;
            while end > start {
                if end < found.len() {
                    let end_node = found[end];
                    let delta = sub(end_node, start_node);
                    let len = length(delta);
                    let angle_rate = if len > 0.0 { delta.z / len } else { 0.0 };
                    if !(angle_rate >= 0.2 || angle_rate <= -0.5)
                        && !self.line_passes_through_forbidden_area(start_node, end_node)?
                    {
                        res.push_back(end_node);
                        start = end;
                        break;
                    }
                }
                end -= 1;
            }
            start += 1;
        }
        Ok(res)
    }

    /// `LinePassesThroughForbiddenArea(start, end)` (the start node's bai; `Err` = the C#
    /// `NullReferenceException` when there is none).
    fn line_passes_through_forbidden_area(&self, start: Vec3, end: Vec3) -> Result<bool, GeoError> {
        let bai = self.bai_by_pos(start)?.ok_or(GeoError)?;
        for area in &bai.areas_mission_readers {
            for shape in area
                .forbidden_areas
                .iter()
                .chain(&area.forbidden_boundaries)
                .chain(&area.designer_forbidden_areas)
            {
                if line_passes_through_ai_shape(start, end, &shape.points, true, 8.0) {
                    return Ok(true);
                }
            }
        }
        Ok(false)
    }

    /// `StickToFloor(point)`.
    pub fn stick_to_floor(&self, point: Vec3) -> Vec3 {
        let floor = self
            .terrain
            .as_ref()
            .and_then(|t| t.height(point.x, point.y).ok())
            .unwrap_or(0.0);
        if floor < point.z {
            Vec3::new(point.x, point.y, floor)
        } else {
            point
        }
    }

    /// The number of loaded cells (diagnostics).
    pub fn loaded_cells(&self) -> usize {
        self.cells.iter().filter(|c| c.get().is_some()).count()
    }
}

/// `IsInPolygon(point, polygon)` (2D, edges and vertices count as inside).
pub fn is_in_polygon(point: Vec3, polygon: &[Vec3]) -> bool {
    let Some(&last) = polygon.last() else {
        // polygon.Last() throws on an empty list; no forbidden area has no points
        return false;
    };
    let mut result = false;
    let mut a = last;
    for &b in polygon {
        if b.x == point.x && b.y == point.y {
            return true;
        }
        if b.y == a.y && point.y == a.y {
            if a.x <= point.x && point.x <= b.x {
                return true;
            }
            if b.x <= point.x && point.x <= a.x {
                return true;
            }
        }
        if ((b.y < point.y && a.y >= point.y) || (a.y < point.y && b.y >= point.y))
            && b.x + (point.y - b.y) / (a.y - b.y) * (a.x - b.x) <= point.x
        {
            result = !result;
        }
        a = b;
    }
    result
}

/// `TriangleCenter(p1, p2, p3)`.
pub fn triangle_center(a: Vec3, b: Vec3, c: Vec3) -> Vec3 {
    Vec3::new(
        (a.x + b.x + c.x) / 3.0,
        (a.y + b.y + c.y) / 3.0,
        (a.z + b.z + c.z) / 3.0,
    )
}

/// `DouglasPeuckerReduction(points, tolerance)`.
pub fn douglas_peucker_reduction(points: &[Vec3], tolerance: f64) -> Vec<Vec3> {
    if points.len() < 3 {
        return points.to_vec();
    }
    let first = 0usize;
    let mut last = points.len() - 1;
    // The first and the last point cannot be the same
    while points[first] == points[last] {
        if last == 0 {
            // C#: index -1 throws; only a list of identical points gets here
            return vec![points[0]];
        }
        last -= 1;
    }
    let mut keep = vec![first, last];
    dp_reduce(points, first, last, tolerance, &mut keep);
    keep.sort_unstable();
    keep.into_iter().map(|i| points[i]).collect()
}

fn dp_reduce(points: &[Vec3], first: usize, last: usize, tolerance: f64, keep: &mut Vec<usize>) {
    let mut max_distance = 0f64;
    let mut farthest = 0usize;
    if last - first > 1 {
        for (i, p) in points.iter().enumerate().take(last).skip(first) {
            let d = perpendicular_distance(points[first], points[last], *p);
            if d > max_distance {
                max_distance = d;
                farthest = i;
            }
        }
        if max_distance > tolerance && farthest != first {
            keep.push(farthest);
            dp_reduce(points, first, farthest, tolerance, keep);
            dp_reduce(points, farthest, last, tolerance, keep);
        }
    }
}

/// `PerpendicularDistance(p1, p2, target)` (2D, double precision).
fn perpendicular_distance(p1: Vec3, p2: Vec3, t: Vec3) -> f64 {
    let (x1, y1, x2, y2, x3, y3) = (
        f64::from(p1.x),
        f64::from(p1.y),
        f64::from(p2.x),
        f64::from(p2.y),
        f64::from(t.x),
        f64::from(t.y),
    );
    let area = (0.5 * (x1 * y2 + x2 * y3 + x3 * y1 - x2 * y1 - x3 * y2 - x1 * y3)).abs();
    let bottom = ((x1 - x2).powi(2) + (y1 - y2).powi(2)).sqrt();
    area / bottom * 2.0
}

/// `DistanceBetweenPoints` (squared 2D).
fn distance_between_points(p: Vec3, c: Vec3) -> f32 {
    (c.x - p.x) * (c.x - p.x) + (c.y - p.y) * (c.y - p.y)
}

/// `FindClosestIndexPoint(searchIn, compareTo)` (C# quirk mirrored: the running minimum starts
/// at 0, so only exact matches move the index).
pub fn find_closest_index_point(search: &[Vec3], compare_to: Vec3) -> u32 {
    let mut min_distance = 0f32;
    let mut point_n = 0u32;
    for (i, p) in search.iter().enumerate() {
        let d = distance_between_points(*p, compare_to);
        if d > min_distance {
            continue;
        }
        point_n = i as u32;
        min_distance = d;
    }
    point_n
}

/// `LinePassesThroughAiShape(start, end, shape, closedLoop, maxHeightOffset)`.
fn line_passes_through_ai_shape(
    start: Vec3,
    end: Vec3,
    pts: &[Vec3],
    closed: bool,
    max_h: f32,
) -> bool {
    let n = pts.len() as i64 + if closed { 0 } else { -1 };
    for index in 0..n.max(0) as usize {
        let ls = pts[index];
        let le = if index < pts.len() - 1 {
            pts[index + 1]
        } else {
            pts[0]
        };
        let ip = find_line_intersection(start, end, ls, le);
        if ip != Vec3::default()
            && (max_h == 0.0 || (ip.z - start.z).abs() <= max_h || (ip.z - end.z).abs() <= max_h)
        {
            return true;
        }
    }
    false
}

/// `FindLineIntersection(start1, end1, start2, end2)` (2D; `Vector3.Zero` = none).
fn find_line_intersection(s1: Vec3, e1: Vec3, s2: Vec3, e2: Vec3) -> Vec3 {
    let denominator = (e1.x - s1.x) * (e2.y - s2.y) - (e1.y - s1.y) * (e2.x - s2.x);
    if denominator == 0.0 {
        return Vec3::default();
    }
    let numerator1 = (s1.y - s2.y) * (e2.x - s2.x) - (s1.x - s2.x) * (e2.y - s2.y);
    let r = numerator1 / denominator;
    let numerator2 = (s1.y - s2.y) * (e1.x - s1.x) - (s1.x - s2.x) * (e1.y - s1.y);
    let s = numerator2 / denominator;
    if !(0.0..=1.0).contains(&r) || !(0.0..=1.0).contains(&s) {
        return Vec3::default();
    }
    Vec3::new(
        s1.x + r * (e1.x - s1.x),
        s1.y + r * (e1.y - s1.y),
        s1.z + r * (e1.z - s1.z),
    )
}

/// The cell world size (`CELL_SIZE`).
pub const fn cell_size() -> i32 {
    CELL_SIZE
}

/// `MathUtil.CalculateDistance(a, b)` (2D).
pub fn distance_2d(a: Vec3, b: Vec3) -> f32 {
    calculate_distance(a, b, false)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn v(x: f32, y: f32) -> Vec3 {
        Vec3::new(x, y, 0.0)
    }

    #[test]
    fn polygon_inclusion() {
        let sq = [v(0.0, 0.0), v(10.0, 0.0), v(10.0, 10.0), v(0.0, 10.0)];
        assert!(is_in_polygon(v(5.0, 5.0), &sq));
        assert!(!is_in_polygon(v(15.0, 5.0), &sq));
        assert!(is_in_polygon(v(10.0, 10.0), &sq), "vertex");
        assert!(is_in_polygon(v(5.0, 0.0), &sq), "horizontal edge");
        assert!(!is_in_polygon(v(5.0, 5.0), &[]));
    }

    #[test]
    fn douglas_peucker() {
        let pts = [
            v(0.0, 0.0),
            v(1.0, 0.1),
            v(2.0, -0.1),
            v(3.0, 5.0),
            v(4.0, 6.0),
            v(5.0, 7.0),
        ];
        let r = douglas_peucker_reduction(&pts, 1.0);
        assert_eq!(r.first(), Some(&pts[0]));
        assert_eq!(r.last(), Some(&pts[5]));
        assert!(r.contains(&pts[2]) || r.contains(&pts[3]));
        assert!(r.len() < pts.len());
        // < 3 points unchanged
        assert_eq!(douglas_peucker_reduction(&pts[..2], 1.0), pts[..2].to_vec());
    }

    #[test]
    fn line_intersections() {
        let p = find_line_intersection(v(0.0, 0.0), v(10.0, 10.0), v(0.0, 10.0), v(10.0, 0.0));
        assert_eq!(p, v(5.0, 5.0));
        assert_eq!(
            find_line_intersection(v(0.0, 0.0), v(1.0, 1.0), v(0.0, 10.0), v(10.0, 0.0)),
            Vec3::default()
        );
        let sq = [v(0.0, 0.0), v(10.0, 0.0), v(10.0, 10.0), v(0.0, 10.0)];
        assert!(line_passes_through_ai_shape(
            v(-5.0, 5.0),
            v(5.0, 5.0),
            &sq,
            true,
            8.0
        ));
        assert!(!line_passes_through_ai_shape(
            v(-5.0, 5.0),
            v(-1.0, 5.0),
            &sq,
            true,
            8.0
        ));
    }

    #[test]
    fn closest_index_quirk() {
        let pts = [v(5.0, 5.0), v(1.0, 1.0), v(2.0, 2.0)];
        assert_eq!(find_closest_index_point(&pts, v(1.0, 1.0)), 1);
        assert_eq!(find_closest_index_point(&pts, v(1.1, 1.0)), 0);
    }

    #[test]
    fn cell_and_path_index() {
        assert_eq!(to_cell_index(Vec3::new(2048.5, 1023.9, 0.0)), (2, 0));
        assert_eq!(to_paths_index(Vec3::new(2048.5, 1023.9, 0.0)), (8, 3));
        assert_eq!(to_cell_index(Vec3::new(-1.0, 0.0, 0.0)), (-1, 0));
        assert_eq!(
            triangle_center(v(0.0, 0.0), v(3.0, 0.0), v(0.0, 3.0)),
            v(1.0, 1.0)
        );
    }
}
