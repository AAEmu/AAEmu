//! `Models/Game/World/WaterBodies` + `WaterBodyArea`: the water of a world (`WorldInstance.Water`):
//! the template ocean level plus the lakes / rivers ingested from the `object.dat` water volumes
//! of every loaded cell (`WorldCell.LoadCell` → `Water.AddFromCellData`).
//!
//! The C# keeps one `WaterBodies` per world instance and fills it for every instance of the
//! template when a cell loads; the content only depends on the template, so one store per
//! template is kept here. Cells are ingested when the physics (or a caller) syncs the store with
//! the heightmap cells loaded so far ([`WaterBodies::sync_loaded_cells`]), in cell order instead
//! of load order — area ids (`Areas` indices) can differ from the C#, nothing observable depends
//! on them.

use std::collections::{HashMap, HashSet};
use std::sync::Mutex;

use crate::data::client_files::ClientFileManager;
use crate::data::world_templates::CELL_SIZE;
use crate::systems::ai::numerics::Vec3Ext;
use crate::systems::npcs::heightmap::WorldHeightmap;
use crate::systems::skills::controllers::Vec3;

use super::objects_file::{ObjectsFile, Prefab, WaterPrefab, WaterVolumeType};

/// `WaterBodyAreaType`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum WaterBodyAreaType {
    Polygon,
    LineArray,
}

/// `System.Drawing.RectangleF`.
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct RectF {
    pub x: f32,
    pub y: f32,
    pub width: f32,
    pub height: f32,
}

impl RectF {
    /// `RectangleF.Contains(x, y)`.
    pub fn contains(&self, x: f32, y: f32) -> bool {
        self.x <= x && x < self.x + self.width && self.y <= y && y < self.y + self.height
    }
}

/// `WaterBodyArea`.
#[derive(Debug, Clone, PartialEq)]
pub struct WaterBodyArea {
    pub id: u32,
    pub area_type: WaterBodyAreaType,
    pub depth: f32,
    pub name: String,
    pub speed: f32,
    /// Unit flow axis in XY.
    pub flow_axis: (f32, f32),
    pub flow_speed_abs: f32,
    pub flow_speed_signed: f32,
    pub river_width: f32,
    pub points: Vec<Vec3>,
    pub bounding_box: RectF,
    pub lowest: f32,
    pub highest: f32,
}

/// Extra half-width (m) beyond `RiverWidth` for the corridor test.
const RIVER_CORRIDOR_EDGE_PADDING_METERS: f32 = 1.0;

impl WaterBodyArea {
    pub fn new(name: &str, area_type: WaterBodyAreaType) -> Self {
        Self {
            id: 0,
            area_type,
            depth: 0.0,
            name: name.to_string(),
            speed: 0.0,
            flow_axis: (1.0, 0.0),
            flow_speed_abs: 0.0,
            flow_speed_signed: 0.0,
            river_width: 0.0,
            points: Vec::new(),
            bounding_box: RectF::default(),
            lowest: 0.0,
            highest: 0.0,
        }
    }

    /// `GetSurface(point, out surfacePoint, out flowDirection)`.
    pub fn get_surface(&self, point: Vec3) -> Option<(Vec3, Vec3)> {
        let flow = self.contains(point.x, point.y)?;
        let surface = match self.area_type {
            WaterBodyAreaType::Polygon => Vec3::new(point.x, point.y, self.highest),
            WaterBodyAreaType::LineArray => {
                let mut closest = self.points[0];
                let mut closest_distance = 1_000_000_000f32;
                for p in &self.points {
                    let d = (point - *p).length_squared();
                    if d < closest_distance {
                        closest_distance = d;
                        closest = *p;
                    }
                }
                Vec3::new(point.x, point.y, closest.z)
            }
        };
        Some((surface, flow))
    }

    /// `UpdateBounds()` (the river `BorderPoints` only feed debug output; not built).
    pub fn update_bounds(&mut self) {
        self.highest = 0.0;
        self.lowest = 0.0;
        let mut first = true;
        let (mut x_min, mut y_min, mut x_max, mut y_max) = (0f32, 0f32, 0f32, 0f32);
        let mut pad = self.river_width;
        if self.area_type == WaterBodyAreaType::LineArray {
            pad += RIVER_CORRIDOR_EDGE_PADDING_METERS;
        }
        for p in &self.points {
            if first {
                first = false;
                x_min = p.x - pad;
                y_min = p.y - pad;
                x_max = p.x + pad;
                y_max = p.y + pad;
                self.lowest = p.z;
                self.highest = p.z;
            } else {
                if p.x - pad < x_min {
                    x_min = p.x - pad;
                }
                if p.x + pad > x_max {
                    x_max = p.x + pad;
                }
                if p.y - pad < y_min {
                    y_min = p.y - pad;
                }
                if p.y + pad > y_max {
                    y_max = p.y + pad;
                }
                if p.z > self.highest {
                    self.highest = p.z;
                }
                if p.z < self.lowest {
                    self.lowest = p.z;
                }
            }
        }
        self.bounding_box = RectF {
            x: x_min,
            y: y_min,
            width: x_max - x_min,
            height: y_max - y_min,
        };
    }

    /// `Contains(x, y, out flowVector)`: `Some(flow)` when inside.
    fn contains(&self, x: f32, y: f32) -> Option<Vec3> {
        if !self.bounding_box.contains(x, y) {
            return None;
        }
        if self.area_type == WaterBodyAreaType::LineArray {
            return self.contains_river_corridor(x, y);
        }
        if !self.point_inside_polygon_2d(x, y) {
            return None;
        }
        let zero = Vec3::new(0.0, 0.0, 0.0);
        if self.flow_speed_abs <= 0.0 || self.flow_speed_signed == 0.0 {
            return Some(zero);
        }
        let (ax, ay) = self.flow_axis;
        let len_sq = ax * ax + ay * ay;
        if len_sq <= 1e-12 {
            return Some(zero);
        }
        let l = len_sq.sqrt();
        Some(Vec3::new(ax / l, ay / l, 0.0) * self.flow_speed_signed)
    }

    /// `PointInsidePolygon2D` (odd-even ray along +X over `BorderPoints` = `Points`).
    fn point_inside_polygon_2d(&self, x: f32, y: f32) -> bool {
        let pts = &self.points;
        if pts.len() < 3 {
            return false;
        }
        let mut inside = false;
        for i in 0..pts.len() - 1 {
            let (yi, yj) = (pts[i].y, pts[i + 1].y);
            if (yi > y) == (yj > y) {
                continue;
            }
            let (xi, xj) = (pts[i].x, pts[i + 1].x);
            let denom = yj - yi;
            if denom.abs() < 1e-20 {
                continue;
            }
            let xinters = xi + (y - yi) / denom * (xj - xi);
            if x < xinters {
                inside = !inside;
            }
        }
        inside
    }

    fn contains_river_corridor(&self, x: f32, y: f32) -> Option<Vec3> {
        if self.points.len() < 2 {
            return None;
        }
        let p = Vec3::new(x, y, self.highest);
        let mut best = f32::INFINITY;
        let mut best_flow = Vec3::new(0.0, 0.0, 0.0);
        let half_width = self.river_width + RIVER_CORRIDOR_EDGE_PADDING_METERS;
        for side in 0..self.points.len() - 1 {
            let d = minimum_distance_to_line(self.points[side], self.points[side + 1], p);
            if d >= best {
                continue;
            }
            best = d;
            let seg = self.points[side + 1] - self.points[side];
            best_flow = if seg.length_squared() > 1e-12 {
                seg.normalized() * self.flow_speed_signed
            } else {
                Vec3::new(0.0, 0.0, 0.0)
            };
        }
        if best > half_width {
            return None;
        }
        Some(best_flow)
    }
}

/// `WaterBodyArea.MinimumDistanceToLine(v, w, p)` (2D).
pub fn minimum_distance_to_line(v3: Vec3, w3: Vec3, p3: Vec3) -> f32 {
    let (vx, vy, wx, wy, px, py) = (v3.x, v3.y, w3.x, w3.y, p3.x, p3.y);
    let l2 = (wx - vx) * (wx - vx) + (wy - vy) * (wy - vy);
    if l2 == 0.0 {
        return ((vx - px) * (vx - px) + (vy - py) * (vy - py)).sqrt();
    }
    let dot = (px - vx) * (wx - vx) + (py - vy) * (wy - vy);
    let t = 0f32.max(1f32.min(dot / l2));
    let (qx, qy) = (vx + t * (wx - vx), vy + t * (wy - vy));
    ((qx - px) * (qx - px) + (qy - py) * (qy - py)).sqrt()
}

/// XY grid size for the area index.
const SPATIAL_CELL_SIZE: f32 = 256.0;
/// Cry Ocean rows within this height above the template sea are skipped.
const TEMPLATE_SEA_DUPLICATE_SURFACE_MARGIN_METERS: f32 = 1.0;
/// `MinWaterBboxAreaSquareMeters`.
pub const MIN_WATER_BBOX_AREA_SQUARE_METERS: f32 = 5000.0;
const HEURISTIC_RIVER_FLOW_SPEED: f32 = 1.0;
const HEURISTIC_FLOW_SIGN_HACK: f32 = -1.0;
const RIVER_LIKE_MIN_LENGTH_METERS: f32 = 120.0;
const RIVER_LIKE_MIN_ASPECT_RATIO: f32 = 2.5;
const RIVER_LIKE_MAX_HALF_WIDTH_METERS: f32 = 130.0;

/// `TryGetRiverLikePolygonMetrics` results.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct RiverMetrics {
    pub length: f32,
    pub max_half_width: f32,
    pub mean_full_width: f32,
    pub area: f32,
    pub aspect: f32,
    pub axis: (f32, f32),
}

fn mean_xy(points: &[Vec3], n: usize) -> (f32, f32) {
    let (mut sx, mut sy) = (0f64, 0f64);
    for p in &points[..n] {
        sx += f64::from(p.x);
        sy += f64::from(p.y);
    }
    let inv = 1f32 / n as f32;
    ((sx * f64::from(inv)) as f32, (sy * f64::from(inv)) as f32)
}

fn mean_xy_open(points: &[Vec3]) -> (f32, f32) {
    let mut n = points.len();
    if n >= 2 && points[0] == points[n - 1] {
        n -= 1;
    }
    mean_xy(points, n)
}

fn covariance_xy(points: &[Vec3], n: usize, mean: (f32, f32)) -> (f32, f32, f32) {
    let (mut sxx, mut sxy, mut syy) = (0f64, 0f64, 0f64);
    for p in &points[..n] {
        let dx = p.x - mean.0;
        let dy = p.y - mean.1;
        sxx += f64::from(dx * dx);
        sxy += f64::from(dx * dy);
        syy += f64::from(dy * dy);
    }
    let inv = f64::from(1f32 / n.max(1) as f32);
    ((sxx * inv) as f32, (sxy * inv) as f32, (syy * inv) as f32)
}

fn normalize2(x: f32, y: f32) -> (f32, f32) {
    let l = (x * x + y * y).sqrt();
    (x / l, y / l)
}

/// `WaterBodies.TryGetRiverLikePolygonMetrics(points, ...)`.
pub fn river_like_polygon_metrics(points: &[Vec3]) -> Option<RiverMetrics> {
    if points.len() < 3 {
        return None;
    }
    let mut owned;
    let mut pts = points;
    if pts[0] != pts[pts.len() - 1] {
        owned = pts.to_vec();
        owned.push(pts[0]);
        pts = &owned;
    }
    let n = pts.len();
    let pca_count = if n >= 2 && pts[0] == pts[n - 1] {
        n - 1
    } else {
        n
    };
    let mut sum = 0f64;
    for i in 0..n - 1 {
        sum += f64::from(pts[i].x) * f64::from(pts[i + 1].y)
            - f64::from(pts[i + 1].x) * f64::from(pts[i].y);
    }
    let area = (sum * 0.5).abs() as f32;
    if area <= 1.0 {
        return None;
    }
    let mean = mean_xy(pts, pca_count);
    let (cxx, cxy, cyy) = covariance_xy(pts, pca_count, mean);
    if !cxx.is_finite() || !cxy.is_finite() || !cyy.is_finite() {
        return None;
    }
    let trace = cxx + cyy;
    let det = cxx * cyy - cxy * cxy;
    let mut disc = trace * trace - 4.0 * det;
    if disc < 0.0 {
        disc = 0.0;
    }
    let s = disc.sqrt();
    let lambda1 = 0.5 * (trace + s);
    let (mut vx, mut vy) = (cxy, lambda1 - cxx);
    if vx.abs() + vy.abs() < 1e-12 {
        vx = 1.0;
        vy = 0.0;
    }
    let v = normalize2(vx, vy);
    let perp = (-v.1, v.0);
    let (mut min_t, mut max_t) = (f32::INFINITY, f32::NEG_INFINITY);
    let (mut min_p, mut max_p) = (f32::INFINITY, f32::NEG_INFINITY);
    for p in pts {
        let d = (p.x - mean.0, p.y - mean.1);
        let t = d.0 * v.0 + d.1 * v.1;
        let q = d.0 * perp.0 + d.1 * perp.1;
        min_t = min_t.min(t);
        max_t = max_t.max(t);
        min_p = min_p.min(q);
        max_p = max_p.max(q);
    }
    let length = 0f32.max(max_t - min_t);
    let full_width = 0f32.max(max_p - min_p);
    let mean_full_width = area / 1e-3f32.max(length);
    Some(RiverMetrics {
        length,
        max_half_width: full_width * 0.5,
        mean_full_width,
        area,
        aspect: length / 1e-3f32.max(mean_full_width),
        axis: v,
    })
}

fn is_river_like_polygon(points: &[Vec3]) -> Option<(f32, f32)> {
    let m = river_like_polygon_metrics(points)?;
    if m.length < RIVER_LIKE_MIN_LENGTH_METERS
        || m.max_half_width > RIVER_LIKE_MAX_HALF_WIDTH_METERS
        || m.aspect < RIVER_LIKE_MIN_ASPECT_RATIO
    {
        return None;
    }
    Some(m.axis)
}

/// `TryGetHeightNoLoad` (only loaded cells with a heightmap).
fn height_no_load(hm: Option<&WorldHeightmap>, x: f32, y: f32) -> Option<f32> {
    let hm = hm?;
    let (ix, iy) = (x as i32, y as i32);
    let (cx, cy) = (ix / CELL_SIZE, iy / CELL_SIZE);
    if !hm.cell_has_height_map(cx, cy) {
        return None;
    }
    hm.get_cell_height(cx, cy, ix, iy).ok()
}

fn signed_speed_from_terrain_no_load(
    hm: Option<&WorldHeightmap>,
    a: (f32, f32),
    b: (f32, f32),
    speed_abs: f32,
) -> f32 {
    if speed_abs <= 0.0 {
        return 0.0;
    }
    let (Some(ha), Some(hb)) = (height_no_load(hm, a.0, a.1), height_no_load(hm, b.0, b.1)) else {
        return speed_abs;
    };
    if hb > ha {
        -speed_abs
    } else {
        speed_abs
    }
}

fn footprint_too_small(a: &WaterBodyArea) -> bool {
    a.bounding_box.width * a.bounding_box.height < MIN_WATER_BBOX_AREA_SQUARE_METERS
}

/// `WaterPointToWorld(cellOffset, filePoint, surfaceZ)`.
fn water_point_to_world(cell_offset: Vec3, p: [f32; 3], surface_z: f32) -> Vec3 {
    let local_band = CELL_SIZE as f32 * 2.0;
    let local = p[0] <= local_band && p[1] <= local_band && p[0] >= -512.0 && p[1] >= -512.0;
    let fp = Vec3::new(p[0], p[1], p[2]);
    let xy = if local { cell_offset + fp } else { fp };
    Vec3::new(xy.x, xy.y, surface_z)
}

#[derive(Debug, Default)]
struct Inner {
    areas: Vec<WaterBodyArea>,
    index: HashMap<(i32, i32), Vec<u32>>,
    ingested_cells: HashSet<(i32, i32)>,
}

/// `WaterBodies`.
#[derive(Debug)]
pub struct WaterBodies {
    pub ocean_level: f32,
    inner: Mutex<Inner>,
}

impl WaterBodies {
    /// `InitWaterFromTemplate()`.
    pub fn new(ocean_level: f32) -> Self {
        Self {
            ocean_level,
            inner: Mutex::new(Inner::default()),
        }
    }

    fn lock(&self) -> std::sync::MutexGuard<'_, Inner> {
        self.inner.lock().unwrap_or_else(|e| e.into_inner())
    }

    pub fn area_count(&self) -> usize {
        self.lock().areas.len()
    }

    /// `GetAreasSnapshot()`.
    pub fn areas_snapshot(&self) -> Vec<WaterBodyArea> {
        self.lock().areas.clone()
    }

    fn add_area(inner: &mut Inner, mut area: WaterBodyArea) {
        area.id = inner.areas.len() as u32;
        let bb = area.bounding_box;
        let min_cx = (bb.x / SPATIAL_CELL_SIZE).floor() as i32;
        let max_cx = ((bb.x + bb.width) / SPATIAL_CELL_SIZE).floor() as i32;
        let min_cy = (bb.y / SPATIAL_CELL_SIZE).floor() as i32;
        let max_cy = ((bb.y + bb.height) / SPATIAL_CELL_SIZE).floor() as i32;
        for cx in min_cx..=max_cx {
            for cy in min_cy..=max_cy {
                inner.index.entry((cx, cy)).or_default().push(area.id);
            }
        }
        inner.areas.push(area);
    }

    /// `ClearIngestedAreas()` (+ the cells ingested so far, so the next sync re-reads them).
    pub fn clear_ingested_areas(&self) {
        let mut inner = self.lock();
        inner.areas.clear();
        inner.index.clear();
        inner.ingested_cells.clear();
    }

    /// Adds an area (tests / tools; `Areas.Add` + `RebuildSpatialIndex`).
    pub fn add(&self, area: WaterBodyArea) {
        Self::add_area(&mut self.lock(), area);
    }

    /// `IsWater(point, out flowDirection)`: `Some(flow)` in water.
    pub fn is_water(&self, point: Vec3) -> Option<Vec3> {
        let zero = Vec3::new(0.0, 0.0, 0.0);
        if point.z <= self.ocean_level {
            return Some(zero);
        }
        let inner = self.lock();
        let cell = (
            (point.x / SPATIAL_CELL_SIZE).floor() as i32,
            (point.y / SPATIAL_CELL_SIZE).floor() as i32,
        );
        let ids = inner.index.get(&cell)?;
        let mut total = zero;
        let mut targets = 0;
        for id in ids {
            let area = &inner.areas[*id as usize];
            if !area.bounding_box.contains(point.x, point.y) {
                continue;
            }
            if let Some((surface, fd)) = area.get_surface(point) {
                if point.z <= surface.z && point.z >= surface.z - area.depth {
                    total += fd;
                    targets += 1;
                }
            }
        }
        if targets > 0 {
            return Some(total / targets as f32);
        }
        None
    }

    /// `GetWaterSurface(point, out flowDirection)`.
    pub fn get_water_surface(&self, point: Vec3) -> (f32, Vec3) {
        let zero = Vec3::new(0.0, 0.0, 0.0);
        if point.z <= self.ocean_level {
            return (self.ocean_level, zero);
        }
        let inner = self.lock();
        let cell = (
            (point.x / SPATIAL_CELL_SIZE).floor() as i32,
            (point.y / SPATIAL_CELL_SIZE).floor() as i32,
        );
        let mut closest = f32::INFINITY;
        let mut chosen = self.ocean_level;
        let mut flow = zero;
        if let Some(ids) = inner.index.get(&cell) {
            for id in ids {
                let area = &inner.areas[*id as usize];
                if !area.bounding_box.contains(point.x, point.y) {
                    continue;
                }
                let Some((surface, f)) = area.get_surface(point) else {
                    continue;
                };
                if point.z < surface.z - area.depth {
                    continue;
                }
                let d = (surface.z - point.z).abs();
                if d < closest {
                    closest = d;
                    chosen = surface.z;
                    flow = f;
                }
            }
        }
        if closest < f32::INFINITY {
            return (chosen, flow);
        }
        (self.ocean_level, zero)
    }

    /// `AddFromCellData(cell)`: the water volumes of a cell's `object.dat`.
    pub fn add_from_cell_data(
        &self,
        hm: Option<&WorldHeightmap>,
        template_ocean_level: f32,
        cell_x: i32,
        cell_y: i32,
        objects: &ObjectsFile,
    ) {
        let offset = Vec3::new(
            (cell_x * CELL_SIZE) as f32,
            (cell_y * CELL_SIZE) as f32,
            0.0,
        );
        for (i, prefab) in objects.prefabs.iter().enumerate() {
            if let Prefab::Water(w) = prefab {
                self.add_water(hm, template_ocean_level, cell_x, cell_y, offset, w, i + 1);
            }
        }
    }

    #[allow(clippy::too_many_arguments)]
    fn add_water(
        &self,
        hm: Option<&WorldHeightmap>,
        template_ocean_level: f32,
        cell_x: i32,
        cell_y: i32,
        offset: Vec3,
        water: &WaterPrefab,
        prefab_idx: usize,
    ) {
        if water.volume_type == WaterVolumeType::Ocean
            && water.surface_height
                <= template_ocean_level + TEMPLATE_SEA_DUPLICATE_SURFACE_MARGIN_METERS
        {
            return;
        }
        let like_river = water.volume_type == WaterVolumeType::River;
        let like_area = matches!(
            water.volume_type,
            WaterVolumeType::Area | WaterVolumeType::Ocean | WaterVolumeType::Sector
        );
        if water.physics_contour_points.len() >= 3 && (like_area || like_river) {
            let name = if like_river {
                format!("WaterContour_C{cell_x}-{cell_y}_{prefab_idx}")
            } else {
                format!("Water_C{cell_x}-{cell_y}_{prefab_idx}")
            };
            self.add_polygon_from_physics_contour(hm, water, offset, &name);
        }
        if !like_river {
            return;
        }
        let river_points: Vec<Vec3> = if water.shape_points.len() >= 2 {
            water
                .shape_points
                .iter()
                .map(|p| water_point_to_world(offset, *p, water.surface_height))
                .collect()
        } else {
            let s = Vec3::new(water.start_pos[0], water.start_pos[1], water.start_pos[2]);
            let e = Vec3::new(water.end_pos[0], water.end_pos[1], water.end_pos[2]);
            if s.distance(e) > 0.5 {
                vec![
                    water_point_to_world(offset, water.start_pos, water.surface_height),
                    water_point_to_world(offset, water.end_pos, water.surface_height),
                ]
            } else {
                return;
            }
        };
        if river_points.len() < 2 {
            return;
        }
        let mut river = WaterBodyArea::new(
            &format!("Segment_C{cell_x}-{cell_y}_{prefab_idx}"),
            WaterBodyAreaType::LineArray,
        );
        river.depth = water.depth;
        river.river_width = (water.depth * 2.0).clamp(4.0, 40.0);
        for p in river_points {
            if !river.points.contains(&p) {
                river.points.push(p);
            }
        }
        let a = (river.points[0].x, river.points[0].y);
        let last = river.points[river.points.len() - 1];
        let b = (last.x, last.y);
        let dir = (b.0 - a.0, b.1 - a.1);
        river.flow_axis = if dir.0 * dir.0 + dir.1 * dir.1 > 1e-12 {
            normalize2(dir.0, dir.1)
        } else {
            (1.0, 0.0)
        };
        river.flow_speed_abs = HEURISTIC_RIVER_FLOW_SPEED;
        river.flow_speed_signed =
            signed_speed_from_terrain_no_load(hm, a, b, HEURISTIC_RIVER_FLOW_SPEED)
                * HEURISTIC_FLOW_SIGN_HACK;
        river.speed = river.flow_speed_signed;
        river.update_bounds();
        if footprint_too_small(&river) {
            return;
        }
        Self::add_area(&mut self.lock(), river);
    }

    fn add_polygon_from_physics_contour(
        &self,
        hm: Option<&WorldHeightmap>,
        water: &WaterPrefab,
        offset: Vec3,
        name: &str,
    ) {
        let mut lake = WaterBodyArea::new(name, WaterBodyAreaType::Polygon);
        lake.depth = water.depth;
        for v in &water.physics_contour_points {
            let p = water_point_to_world(offset, *v, water.surface_height);
            if !lake.points.contains(&p) {
                lake.points.push(p);
            }
        }
        if lake.points.is_empty() {
            return;
        }
        lake.points.push(lake.points[0]);
        lake.update_bounds();
        lake.flow_speed_abs = 0.0;
        lake.flow_speed_signed = 0.0;
        lake.speed = 0.0;
        if let Some(axis) = is_river_like_polygon(&lake.points) {
            lake.flow_axis = axis;
            lake.flow_speed_abs = HEURISTIC_RIVER_FLOW_SPEED;
            let mean = mean_xy_open(&lake.points);
            let half_len = 10f32.max(lake.bounding_box.width + lake.bounding_box.height) * 0.25;
            let p0 = (mean.0 - axis.0 * half_len, mean.1 - axis.1 * half_len);
            let p1 = (mean.0 + axis.0 * half_len, mean.1 + axis.1 * half_len);
            lake.flow_speed_signed =
                signed_speed_from_terrain_no_load(hm, p0, p1, HEURISTIC_RIVER_FLOW_SPEED)
                    * HEURISTIC_FLOW_SIGN_HACK;
            lake.speed = lake.flow_speed_signed;
        }
        if footprint_too_small(&lake) {
            return;
        }
        Self::add_area(&mut self.lock(), lake);
    }

    /// Ingests the `object.dat` of every heightmap cell loaded since the last call (the C# does
    /// it inside `WorldCell.LoadCell`; only cells whose heightmap loaded read their objects).
    pub fn sync_loaded_cells(
        &self,
        hm: &WorldHeightmap,
        files: &ClientFileManager,
        template_name: &str,
        template_ocean_level: f32,
    ) {
        let loaded = hm.loaded_cell_count();
        if self.lock().ingested_cells.len() >= loaded {
            return;
        }
        for cx in 0..hm.cell_x {
            for cy in 0..hm.cell_y {
                if !hm.is_cell_loaded(cx, cy) {
                    continue;
                }
                if !self.lock().ingested_cells.insert((cx, cy)) {
                    continue;
                }
                if !hm.cell_has_height_map(cx, cy) {
                    continue;
                }
                let file =
                    format!("game/worlds/{template_name}/cells/{cx:03}_{cy:03}/client/object.dat");
                if !files.file_exists(&file) {
                    continue;
                }
                let Some(bytes) = files.read_file(&file) else {
                    continue;
                };
                let objects = ObjectsFile::read(&bytes);
                if !objects.ok && !objects.prefabs.is_empty() {
                    tracing::error!(
                        "Error loading objects from {file}, only {} prefabs read",
                        objects.prefabs.len()
                    );
                }
                self.add_from_cell_data(Some(hm), template_ocean_level, cx, cy, &objects);
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::super::objects_file::tests::{object_dat, water_record};
    use super::*;

    fn lake(points: &[(f32, f32)], z: f32, depth: f32) -> WaterBodyArea {
        let mut a = WaterBodyArea::new("lake", WaterBodyAreaType::Polygon);
        a.depth = depth;
        for (x, y) in points {
            a.points.push(Vec3::new(*x, *y, z));
        }
        a.points.push(a.points[0]);
        a.update_bounds();
        a
    }

    #[test]
    fn ocean_and_lake_surfaces() {
        let w = WaterBodies::new(100.0);
        assert!(w.is_water(Vec3::new(5.0, 5.0, 99.0)).is_some());
        assert!(w.is_water(Vec3::new(5.0, 5.0, 101.0)).is_none());
        w.add(lake(
            &[(0.0, 0.0), (300.0, 0.0), (300.0, 300.0), (0.0, 300.0)],
            150.0,
            10.0,
        ));
        assert_eq!(w.area_count(), 1);
        assert!(w.is_water(Vec3::new(150.0, 150.0, 145.0)).is_some());
        assert!(w.is_water(Vec3::new(150.0, 150.0, 151.0)).is_none());
        assert!(w.is_water(Vec3::new(150.0, 150.0, 139.0)).is_none());
        assert!(w.is_water(Vec3::new(350.0, 150.0, 145.0)).is_none());
        assert_eq!(w.get_water_surface(Vec3::new(150.0, 150.0, 160.0)).0, 150.0);
        // below the lake body: ocean level
        assert_eq!(w.get_water_surface(Vec3::new(150.0, 150.0, 120.0)).0, 100.0);
        assert_eq!(w.get_water_surface(Vec3::new(150.0, 150.0, 50.0)).0, 100.0);
    }

    #[test]
    fn river_corridor_flows_along_the_segments() {
        let w = WaterBodies::new(0.0);
        let mut r = WaterBodyArea::new("river", WaterBodyAreaType::LineArray);
        r.depth = 5.0;
        r.river_width = 10.0;
        r.points = vec![Vec3::new(0.0, 0.0, 50.0), Vec3::new(200.0, 0.0, 48.0)];
        r.flow_speed_abs = 1.0;
        r.flow_speed_signed = -1.0;
        r.update_bounds();
        w.add(r);
        let f = w.is_water(Vec3::new(50.0, 5.0, 47.0)).expect("in river");
        assert!((f.x + 1.0).abs() < 1e-3 && f.y == 0.0 && f.z > 0.0);
        assert!(w.is_water(Vec3::new(50.0, 12.0, 47.0)).is_none());
        // the surface is the closest centre point's height
        assert_eq!(w.get_water_surface(Vec3::new(180.0, 0.0, 49.0)).0, 48.0);
    }

    #[test]
    fn river_like_metrics_and_cell_ingest() {
        // long thin polygon: river-like
        let pts: Vec<Vec3> = [(0.0, 0.0), (400.0, 0.0), (400.0, 40.0), (0.0, 40.0)]
            .iter()
            .map(|(x, y)| Vec3::new(*x, *y, 0.0))
            .collect();
        let m = river_like_polygon_metrics(&pts).expect("metrics");
        assert!((m.length - 400.0).abs() < 1e-3 && (m.area - 16000.0).abs() < 1e-2);
        assert!(is_river_like_polygon(&pts).is_some());
        // cell ingest: a lake contour in cell-local coordinates
        let contour = [
            [10.0, 10.0, 0.0],
            [210.0, 10.0, 0.0],
            [210.0, 210.0, 0.0],
            [10.0, 210.0, 0.0],
        ];
        let block = water_record(0, [0.0, 0.0, 120.0], [0.0, 0.0, 125.0], 6.0, &[], &contour);
        let objects = ObjectsFile::read(&object_dat(&block));
        let w = WaterBodies::new(100.0);
        w.add_from_cell_data(None, 100.0, 1, 0, &objects);
        let areas = w.areas_snapshot();
        assert_eq!(areas.len(), 1);
        assert_eq!(areas[0].name, "Water_C1-0_1");
        assert!(w.is_water(Vec3::new(1124.0, 100.0, 124.0)).is_some());
        // an ocean volume at sea level is skipped
        let block = water_record(3, [0.0, 0.0, 100.0], [0.0, 0.0, 100.5], 6.0, &[], &contour);
        let w = WaterBodies::new(100.0);
        w.add_from_cell_data(None, 100.0, 0, 0, &ObjectsFile::read(&object_dat(&block)));
        assert_eq!(w.area_count(), 0);
    }
}
