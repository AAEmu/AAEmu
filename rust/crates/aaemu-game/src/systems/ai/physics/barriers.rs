//! Ship static barriers (`GeoDataMode` only): `ShipStaticBarrierZones` / `ShipStaticBarrier`,
//! `ShipStaticBarrierBaiIngestor` (coastal `.bai` forbidden-area polygons → polyline walls, per
//! cell, lazily), `ShipStaticBarrierSpatialGrid` and `ShipStaticBarrierInteraction` (hull vs
//! wall segment XZ SAT).

use std::collections::{HashMap, HashSet};
use std::sync::Arc;

use crate::systems::ai::geodata::bai::AiShape;
use crate::systems::ai::geodata::manager::BaiRef;
use crate::systems::skills::controllers::Vec3;

use super::contacts::{
    apply_separation_and_surface_damp_xz, obb_xz_min_penetration, pair, static_contact,
};
use super::ship::PhysShip;
use super::PhysEnv;

/// `ShipStaticBarrier`.
#[derive(Debug, Clone, PartialEq)]
pub struct ShipStaticBarrier {
    pub name: String,
    pub zone_key: u32,
    pub z_min: f32,
    pub z_max: f32,
    pub half_thickness_meters: f32,
    pub enabled: bool,
    /// (x0, y0, x1, y1) in world X/Y.
    pub segments: Vec<(f32, f32, f32, f32)>,
}

/// `ShipStaticBarrierEntryDto`.
#[derive(Debug, Clone, PartialEq)]
pub struct BarrierEntry {
    pub name: String,
    pub zone_key: u32,
    pub z_min: f32,
    pub z_max: f32,
    pub half_thickness_meters: f32,
    pub enabled: bool,
    pub points_xy: Vec<(f64, f64)>,
}

impl ShipStaticBarrier {
    /// `ShipStaticBarrier.TryCreate(entry)`.
    pub fn try_create(e: &BarrierEntry) -> Result<Self, String> {
        if e.points_xy.len() < 2 {
            return Err(format!("{}: need at least 2 PointsXY", e.name));
        }
        if e.z_max < e.z_min {
            return Err(format!("{}: ZMax < ZMin", e.name));
        }
        let half_t = if e.half_thickness_meters > 0.0 {
            e.half_thickness_meters
        } else {
            1.05
        };
        let pts: Vec<(f32, f32)> = e
            .points_xy
            .iter()
            .map(|(x, y)| (*x as f32, *y as f32))
            .collect();
        let segments = pts
            .windows(2)
            .map(|w| (w[0].0, w[0].1, w[1].0, w[1].1))
            .collect();
        Ok(Self {
            name: e.name.clone(),
            zone_key: e.zone_key,
            z_min: e.z_min,
            z_max: e.z_max,
            half_thickness_meters: half_t,
            enabled: e.enabled,
            segments,
        })
    }
}

/// `ShipStaticBarrierSpatialGrid`.
#[derive(Debug, Default)]
pub struct SpatialGrid {
    cells: HashMap<(i32, i32), Vec<(usize, usize)>>,
}

pub const CELL_SIZE_METERS: f32 = 128.0;
pub const SEGMENT_INSERT_EXPAND_METERS: f32 = 200.0;

impl SpatialGrid {
    /// `Rebuild(barriers)`.
    pub fn rebuild(&mut self, barriers: &[ShipStaticBarrier]) {
        self.cells.clear();
        for (bi, b) in barriers.iter().enumerate() {
            if !b.enabled {
                continue;
            }
            let half_t = b.half_thickness_meters;
            for (si, s) in b.segments.iter().enumerate() {
                let min_x = s.0.min(s.2) - half_t - SEGMENT_INSERT_EXPAND_METERS;
                let max_x = s.0.max(s.2) + half_t + SEGMENT_INSERT_EXPAND_METERS;
                let min_z = s.1.min(s.3) - half_t - SEGMENT_INSERT_EXPAND_METERS;
                let max_z = s.1.max(s.3) + half_t + SEGMENT_INSERT_EXPAND_METERS;
                let (gx0, gx1) = (
                    (min_x / CELL_SIZE_METERS).floor() as i32,
                    (max_x / CELL_SIZE_METERS).floor() as i32,
                );
                let (gy0, gy1) = (
                    (min_z / CELL_SIZE_METERS).floor() as i32,
                    (max_z / CELL_SIZE_METERS).floor() as i32,
                );
                for gx in gx0..=gx1 {
                    for gy in gy0..=gy1 {
                        self.cells.entry((gx, gy)).or_default().push((bi, si));
                    }
                }
            }
        }
    }

    /// `Query(scx, scz, shipPad, ...)`: unique (barrier, segment) pairs in insertion order.
    pub fn query(&self, scx: f32, scz: f32, pad: f32) -> Vec<(usize, usize)> {
        let mut out = Vec::new();
        if self.cells.is_empty() {
            return out;
        }
        let mut seen = HashSet::new();
        let (gx0, gx1) = (
            ((scx - pad) / CELL_SIZE_METERS).floor() as i32,
            ((scx + pad) / CELL_SIZE_METERS).floor() as i32,
        );
        let (gy0, gy1) = (
            ((scz - pad) / CELL_SIZE_METERS).floor() as i32,
            ((scz + pad) / CELL_SIZE_METERS).floor() as i32,
        );
        for gx in gx0..=gx1 {
            for gy in gy0..=gy1 {
                if let Some(list) = self.cells.get(&(gx, gy)) {
                    for r in list {
                        if seen.insert(*r) {
                            out.push(*r);
                        }
                    }
                }
            }
        }
        out
    }
}

/// `ShipStaticBarrierZones` + the per-world ingest bookkeeping (`ShipBarrierBaiIngestedCells`,
/// `ShipBarrierBaiNameSerial`).
#[derive(Debug, Default)]
pub struct BarrierZones {
    pub barriers: Vec<ShipStaticBarrier>,
    grid: SpatialGrid,
    grid_built_for: Option<usize>,
    ingested_cells: HashSet<(i32, i32)>,
    name_serial: u32,
}

/// `BarrierDefaults`.
pub mod defaults {
    pub const VERTICAL_PAD_METERS: f32 = 0.35;
    pub const RESOLVE_PASSES: usize = 1;
    pub const MAX_PAIR_ITERATIONS: usize = 6;
    pub const MIN_PENETRATION_TO_ACT: f32 = 0.05;
    pub const MIN_VERTICAL_OVERLAP: f32 = 0.12;
    pub const SEPARATION_PUSH_MULTIPLIER: f32 = 1.04;
    pub const SEPARATION_SLACK_METERS: f32 = 0.01;
    pub const MAX_SEPARATION_STEP_METERS_PER_ITERATION: f32 = 0.055;
    pub const MAX_TOTAL_SEPARATION_BUDGET_PER_TICK: f32 = 0.22;
    pub const CLOSING_SPEED_DAMP: f32 = 0.38;
    pub const CEILING_CLOSING_SPEED_DAMP: f32 = 1.0;
    pub const TANGENTIAL_SLIP_DAMP: f32 = 0.91;
    pub const CEILING_SLAB_MAX_THICKNESS_METERS: f32 = 2.0;
    pub const SHIP_BOUNDS_PAD_METERS: f32 = 72.0;
    pub const SAT_SHIP_OBB_TIGHTEN_MUL: f32 = 1.05;
}

/// The ingest tunables (`ShipStaticBarrierBaiIngestor.Defaults`).
pub mod ingest {
    pub const Z_MIN_OFFSET_FROM_WATER_SURFACE: f32 = -5.0;
    pub const Z_MAX_OFFSET_FROM_WATER_SURFACE: f32 = 15.0;
    pub const HALF_THICKNESS_METERS: f32 = 1.05;
    pub const COASTAL_MAX_ABOVE_WATER: f32 = 25.0;
    pub const COASTAL_MAX_BELOW_WATER: f32 = 40.0;
    pub const MARITIME_SAMPLE_GRID: i32 = 4;
    pub const MAX_TOTAL_BARRIERS: usize = 100_000;
    pub const TERRAIN_SAMPLE_FLOOR_BELOW_WATER_MIN: f32 = 180.0;
    pub const INLAND_MIN_VERTEX_Z_ABOVE_WATER: f32 = 55.0;
    pub const PIER_BAND_BELOW_WATER: f32 = 25.0;
    pub const PIER_BAND_ABOVE_WATER: f32 = 25.0;
    pub const BRIDGE_DECK_MIN_ABOVE_WATER_METERS: f32 = 0.25;
}

impl BarrierZones {
    /// `ShipStaticBarrierBaiIngestor.EnsureCell(world, cellX, cellY)`: `grid` is the cell's 4x4
    /// `BaiLoader` (`[px][py]`), `None` when the cell does not exist.
    pub fn ensure_cell(
        &mut self,
        env: &dyn PhysEnv,
        cell: (i32, i32),
        grid: Option<&[[Option<BaiRef>; 4]; 4]>,
    ) {
        if self.ingested_cells.contains(&cell) {
            return;
        }
        let Some(grid) = grid else {
            return;
        };
        if !self.ingested_cells.insert(cell) {
            return;
        }
        if self.barriers.len() >= ingest::MAX_TOTAL_BARRIERS {
            return;
        }
        let mut seen: Vec<*const crate::systems::ai::geodata::bai::BaseBaiLoader> = Vec::new();
        // BaiLoader[px, py] for py, then px
        let order = (0..4).flat_map(|py| (0..4).map(move |px| (px, py)));
        for (px, py) in order {
            let Some(loader) = grid
                .get(px)
                .and_then(|c: &[Option<BaiRef>; 4]| c[py].as_ref())
            else {
                continue;
            };
            let ptr = Arc::as_ptr(loader);
            if seen.contains(&ptr) {
                continue;
            }
            seen.push(ptr);
            for reader in &loader.areas_mission_readers {
                if self.barriers.len() >= ingest::MAX_TOTAL_BARRIERS {
                    return;
                }
                self.add_list(env, &reader.forbidden_boundaries, "fb");
                self.add_list(env, &reader.designer_forbidden_areas, "df");
                self.add_list(env, &reader.forbidden_areas, "fa");
            }
        }
    }

    fn add_list(&mut self, env: &dyn PhysEnv, shapes: &[AiShape], tag: &str) {
        for shape in shapes {
            if self.barriers.len() >= ingest::MAX_TOTAL_BARRIERS {
                return;
            }
            if shape.points.len() < 2 {
                continue;
            }
            let Some((water_ref, deck)) = classify_for_ship_barriers(env, &shape.points) else {
                continue;
            };
            let points = closed_polyline_points_xy(&shape.points);
            if points.len() < 2 {
                continue;
            }
            let name = format!(
                "bai_{tag}_{}_{}",
                self.name_serial,
                sanitize_name(&shape.name)
            );
            self.name_serial += 1;
            let (z_min, z_max) = match deck {
                Some(bottom) => (bottom, bottom + 0.5),
                None => (
                    water_ref + ingest::Z_MIN_OFFSET_FROM_WATER_SURFACE,
                    water_ref + ingest::Z_MAX_OFFSET_FROM_WATER_SURFACE,
                ),
            };
            let entry = BarrierEntry {
                name,
                zone_key: 0,
                z_min,
                z_max,
                half_thickness_meters: ingest::HALF_THICKNESS_METERS,
                enabled: true,
                points_xy: points,
            };
            if let Ok(b) = ShipStaticBarrier::try_create(&entry) {
                self.barriers.push(b);
            }
        }
    }

    /// `ShipStaticBarrierInteraction.ResolveAll(world, ships, dt)` minus the final speed resync
    /// (the caller does it for every ship).
    pub fn resolve_all(
        &mut self,
        env: &dyn PhysEnv,
        ships: &mut [&mut PhysShip],
        instance_id: u32,
    ) {
        if ships.is_empty() || self.barriers.is_empty() {
            return;
        }
        if self.grid_built_for != Some(self.barriers.len()) {
            self.grid.rebuild(&self.barriers);
            self.grid_built_for = Some(self.barriers.len());
        }
        for ship in ships.iter_mut() {
            if !ship.view.in_region || ship.view.instance_id != instance_id {
                continue;
            }
            let half_len = ship.model.mass_box_size_y * ship.spec.scale * 0.5;
            let half_beam = ship.model.mass_box_size_x * ship.spec.scale * 0.5;
            let pad = half_len + half_beam + defaults::SHIP_BOUNDS_PAD_METERS;
            let (scx, scz) = ship.mass_box_center_xz();
            let candidates = self.grid.query(scx, scz, pad);
            let mut budget = defaults::MAX_TOTAL_SEPARATION_BUDGET_PER_TICK;
            for _ in 0..defaults::RESOLVE_PASSES {
                if budget <= 0.0 {
                    break;
                }
                for (bi, si) in &candidates {
                    if budget <= 0.0 {
                        break;
                    }
                    let b = &self.barriers[*bi];
                    if !b.enabled {
                        continue;
                    }
                    if b.zone_key != 0 && ship.view.zone_id != b.zone_key {
                        continue;
                    }
                    try_resolve_ship_vs_segment(env, ship, b, b.segments[*si], &mut budget);
                }
            }
        }
    }
}

fn try_resolve_ship_vs_segment(
    env: &dyn PhysEnv,
    ship: &mut PhysShip,
    barrier: &ShipStaticBarrier,
    seg: (f32, f32, f32, f32),
    budget: &mut f32,
) -> bool {
    use defaults::*;
    let (dx, dz) = (seg.2 - seg.0, seg.3 - seg.1);
    let seg_len = (dx * dx + dz * dz).sqrt();
    if seg_len < 1e-3 {
        return false;
    }
    let (bx, bz) = ((seg.0 + seg.2) * 0.5, (seg.1 + seg.3) * 0.5);
    let bow_b = dz.atan2(dx);
    let half_len_b = seg_len * 0.5;
    let half_wid_b = barrier.half_thickness_meters;
    let ship_height = ship.model.mass_box_size_z * ship.spec.scale;
    let water = env.water_surface(ship.pos).0;
    let clearance = barrier.z_min - water;
    if ship_height > 0.0 && clearance > 0.0 && ship_height < clearance {
        return false;
    }
    let (d_min_y, d_max_y) = (
        barrier.z_min - VERTICAL_PAD_METERS,
        barrier.z_max + VERTICAL_PAD_METERS,
    );
    if (water + ship_height).min(d_max_y) - water.max(d_min_y) < MIN_VERTICAL_OVERLAP {
        return false;
    }
    let bow_a = ship.bow();
    let half_len_a = ship.model.mass_box_size_y
        * ship.spec.scale
        * 0.5
        * pair::HULL_DETECT_INFLATE_LENGTH
        * SAT_SHIP_OBB_TIGHTEN_MUL;
    let wid_a = ship.model.mass_box_size_x
        * ship.spec.scale
        * 0.5
        * pair::HULL_DETECT_INFLATE_BEAM
        * pair::BEAM_DETECT_TIGHTEN_MUL
        * SAT_SHIP_OBB_TIGHTEN_MUL;
    let is_ceiling = barrier.z_max - barrier.z_min <= CEILING_SLAB_MAX_THICKNESS_METERS;
    let closing_damp = if is_ceiling {
        CEILING_CLOSING_SPEED_DAMP
    } else {
        CLOSING_SPEED_DAMP
    };
    let hold = if is_ceiling {
        static_contact::BRIDGE_CEILING_REPLICATION_CONTACT_HOLD_TICKS
    } else {
        static_contact::DEFAULT_REPLICATION_CONTACT_HOLD_TICKS
    };
    let mut had = false;
    for _ in 0..MAX_PAIR_ITERATIONS {
        if *budget <= 0.0 {
            break;
        }
        // the transform position is not moved by the separation (C#: Transform.World.Position)
        let water = env.water_surface(ship.pos).0;
        if (water + ship_height).min(d_max_y) - water.max(d_min_y) < MIN_VERTICAL_OVERLAP {
            break;
        }
        let (ax, az) = ship.mass_box_center_xz();
        let Some((pen, mut nx, mut nz)) = obb_xz_min_penetration(
            ax, az, bow_a, half_len_a, wid_a, bx, bz, bow_b, half_len_b, half_wid_b,
        ) else {
            break;
        };
        if pen >= static_contact::MIN_PENETRATION_METERS_FOR_ENV_HULL_DAMAGE {
            ship.static_obstacle_hull_damage_contact_active = true;
        }
        if pen < MIN_PENETRATION_TO_ACT {
            break;
        }
        if nx * (bx - ax) + nz * (bz - az) < 0.0 {
            nx = -nx;
            nz = -nz;
        }
        let mv = (pen * SEPARATION_PUSH_MULTIPLIER + SEPARATION_SLACK_METERS)
            .min(MAX_SEPARATION_STEP_METERS_PER_ITERATION)
            .min(*budget);
        if mv < 1e-4 {
            break;
        }
        *budget -= mv;
        apply_separation_and_surface_damp_xz(
            ship,
            nx,
            nz,
            mv,
            closing_damp,
            TANGENTIAL_SLIP_DAMP,
            hold,
        );
        had = true;
    }
    had
}

/// `TryClassifyForShipBarriers`: `Some((water surface ref, bridge deck bottom))`.
fn classify_for_ship_barriers(env: &dyn PhysEnv, points: &[Vec3]) -> Option<(f32, Option<f32>)> {
    use ingest::*;
    let max_above = 0f32.max(COASTAL_MAX_ABOVE_WATER);
    let extended_below = 0f32
        .max(COASTAL_MAX_BELOW_WATER)
        .max(TERRAIN_SAMPLE_FLOOR_BELOW_WATER_MIN);
    let grid = MARITIME_SAMPLE_GRID.clamp(1, 12);
    let (mut min_x, mut max_x) = (f32::INFINITY, f32::NEG_INFINITY);
    let (mut min_y, mut max_y) = (f32::INFINITY, f32::NEG_INFINITY);
    let (mut v_min_z, mut v_max_z) = (f32::INFINITY, f32::NEG_INFINITY);
    for p in points {
        min_x = min_x.min(p.x);
        max_x = max_x.max(p.x);
        min_y = min_y.min(p.y);
        max_y = max_y.max(p.y);
        v_min_z = v_min_z.min(p.z);
        v_max_z = v_max_z.max(p.z);
    }
    if min_x.is_infinite() {
        return None;
    }
    let (cx, cy) = ((min_x + max_x) * 0.5, (min_y + max_y) * 0.5);
    let water_ref = env.water_surface(Vec3::new(cx, cy, v_max_z)).0;
    if v_min_z > water_ref + INLAND_MIN_VERTEX_Z_ABOVE_WATER {
        return None;
    }
    let est_deck_bottom = estimate_deck_bottom_z(points, water_ref, v_min_z);
    let deck_candidate = est_deck_bottom > water_ref + BRIDGE_DECK_MIN_ABOVE_WATER_METERS;
    let deck = deck_candidate.then_some(est_deck_bottom);
    if v_max_z >= water_ref - PIER_BAND_BELOW_WATER && v_min_z <= water_ref + PIER_BAND_ABOVE_WATER
    {
        return Some((water_ref, deck));
    }
    for ix in 0..grid {
        let tx = if grid == 1 {
            0.5
        } else {
            ix as f32 / (grid - 1) as f32
        };
        for iy in 0..grid {
            let ty = if grid == 1 {
                0.5
            } else {
                iy as f32 / (grid - 1) as f32
            };
            let sx = min_x + (max_x - min_x) * tx;
            let sy = min_y + (max_y - min_y) * ty;
            let h = env.height(sx, sy);
            if h >= water_ref - extended_below && h <= water_ref + max_above {
                return Some((water_ref, deck));
            }
        }
    }
    None
}

/// `EstimateDeckBottomZ`: the median of the vertex heights above the deck threshold.
fn estimate_deck_bottom_z(points: &[Vec3], water_ref: f32, fallback: f32) -> f32 {
    let mut zs: Vec<f32> = points
        .iter()
        .map(|p| p.z)
        .filter(|z| *z > water_ref + ingest::BRIDGE_DECK_MIN_ABOVE_WATER_METERS)
        .collect();
    if zs.is_empty() {
        return fallback;
    }
    zs.sort_by(f32::total_cmp);
    let mid = zs.len() / 2;
    if zs.len() % 2 == 1 {
        zs[mid]
    } else {
        (zs[mid - 1] + zs[mid]) * 0.5
    }
}

/// `ClosedPolylinePointsXy`.
fn closed_polyline_points_xy(pts: &[Vec3]) -> Vec<(f64, f64)> {
    let mut r: Vec<(f64, f64)> = pts
        .iter()
        .map(|p| (f64::from(p.x), f64::from(p.y)))
        .collect();
    if pts.len() >= 2 {
        let (f, l) = (pts[0], pts[pts.len() - 1]);
        if (f.x - l.x).abs() > 0.01 || (f.y - l.y).abs() > 0.01 {
            r.push((f64::from(f.x), f64::from(f.y)));
        }
    }
    r
}

fn sanitize_name(name: &str) -> String {
    if name.trim().is_empty() {
        return "unnamed".into();
    }
    let s = name.trim();
    if s.chars().count() > 64 {
        s.chars().take(64).collect()
    } else {
        s.to_string()
    }
}

#[cfg(test)]
mod tests {
    use super::super::ship::tests::{model, spec};
    use super::super::ship::{PhysShip, ShipGameView};
    use super::super::tests::FakeEnv;
    use super::*;

    #[test]
    fn coastal_polygon_becomes_a_wall_that_blocks_the_hull() {
        let env = FakeEnv::flat(100.0, 90.0);
        let mut zones = BarrierZones::default();
        let shape = AiShape {
            zone_id: 0,
            name: "pier".into(),
            points: vec![
                Vec3::new(-20.0, 3.0, 100.0),
                Vec3::new(20.0, 3.0, 100.0),
                Vec3::new(20.0, 10.0, 100.0),
            ],
        };
        zones.add_list(&env, &[shape], "fa");
        assert_eq!(zones.barriers.len(), 1);
        let b = &zones.barriers[0];
        assert_eq!(b.name, "bai_fa_0_pier");
        assert_eq!(b.segments.len(), 3); // closed
        assert_eq!((b.z_min, b.z_max), (95.0, 115.0));
        // hull at the origin, bow along +Y (game) reaches y = 7 > 3 - 1.05
        let mut s = PhysShip::new(
            spec(1, 0.0, 0.0, 99.0, 0.0),
            model(),
            ShipGameView::default(),
        );
        let y0 = s.body.position.z;
        zones.resolve_all(&env, &mut [&mut s], 0);
        assert!(s.body.position.z < y0, "pushed back along -Y");
        assert!(y0 - s.body.position.z <= defaults::MAX_TOTAL_SEPARATION_BUDGET_PER_TICK + 1e-5);
        assert!(s.static_obstacle_hull_damage_contact_active);
    }

    #[test]
    fn inland_shapes_and_deck_estimates() {
        let env = FakeEnv::flat(100.0, 300.0);
        let high = vec![Vec3::new(0.0, 0.0, 200.0), Vec3::new(10.0, 0.0, 200.0)];
        assert!(classify_for_ship_barriers(&env, &high).is_none());
        let deck = vec![
            Vec3::new(0.0, 0.0, 95.0),
            Vec3::new(10.0, 0.0, 108.0),
            Vec3::new(20.0, 0.0, 110.0),
        ];
        let (w, d) = classify_for_ship_barriers(&env, &deck).expect("pier band");
        assert_eq!(w, 100.0);
        assert_eq!(d, Some(109.0));
        assert_eq!(sanitize_name("  "), "unnamed");
    }
}
