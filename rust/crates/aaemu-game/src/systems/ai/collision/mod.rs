//! The collision world and the collision query service (`server.systems.ai.collision`).
//!
//! In ArcheAge 10.x the zone server owns all world geometry and the world server only receives
//! the results (`ZONE-PHYSICS-USAGE.md` §0). AAEmu has no zone, so this module builds the
//! zone's physics in-process on the CryPhysics port (`aaemu-physics`) and answers the
//! geometric questions the game systems ask:
//!
//! | module | content |
//! |---|---|
//! | [`cgf`] | CGF chunk reader → `MeshPhysicsData` blobs (+ render arrays) |
//! | [`cells`] | `statobjs.dat`, `brush.dat`, `vegetation.dat`, `vegetation.xml` |
//! | [`models`] | the shared model cache (`LoadPhysGeometry`) |
//! | [`terrain`] | heightfield callbacks, `GetTerrainElevation` |
//! | [`world`] | [`world::PhysWorld`]: one `aaemu_physics::World` per instance (terrain, ocean, cells, object colliders) |
//! | [`query`] | RWI / PWI wrappers with the zone's masks |
//! | [`objects`] | doodad / house colliders (`ZdModel*`, `_UpdateCollisionParams`) |
//! | [`residency`] | lazy cell loading around players / ships, unloading, sync task |
//! | [`fall`] | ZWUnitFell (fall-damage cap) |
//! | [`move_verify`] | ZWReportMoveHack checks |
//!
//! The public query functions below take the server and an instance id. Without client
//! data (no heightmaps installed, tests) or with `Physics.Enable = false` they return `None`
//! and the callers keep their previous (heightmap / C#) behaviour.

pub mod cells;
pub mod cgf;
pub mod config;
pub mod data;
pub mod displacement;
pub mod fall;
pub mod models;
pub mod move_verify;
pub mod movement;
pub mod objects;
pub mod query;
pub mod residency;
pub mod terrain;
pub mod world;

#[cfg(test)]
mod flow_tests;
#[cfg(test)]
mod real_data_tests;
#[cfg(test)]
mod tests;

use std::collections::HashMap;
use std::sync::{Arc, OnceLock};

use aaemu_physics::math::Vec3;
use dashmap::DashMap;

use crate::data::client_files::ClientFileManager;
use crate::server::GameServer;
use crate::systems::npcs::heightmap::HeightmapWorlds;

use self::cells::VegetationGroup;
use self::config::GeometryLevel;
use self::models::ModelCache;
use self::query::{mask, ray_flags, Hit, SweepHit};
use self::terrain::TerrainSource;
use self::world::PhysWorld;

/// `server.systems.ai.collision`.
#[derive(Default)]
pub struct CollisionSystem {
    models: OnceLock<Arc<ModelCache>>,
    worlds: DashMap<u32, Arc<PhysWorld>>,
    veg_groups: DashMap<String, Arc<HashMap<u16, VegetationGroup>>>,
    /// ZWUnitFell trackers.
    pub fall: fall::FallTracker,
    /// ZWReportMoveHack verifiers.
    pub move_verify: move_verify::MoveVerifier,
    /// Resolved doodad model shapes (by model reference).
    shapes: std::sync::Mutex<HashMap<String, Option<objects::ColliderShape>>>,
    /// Tests: force the service on (without `Physics.Enable` / client data checks).
    pub(crate) test_files: OnceLock<Arc<HashMap<String, Vec<u8>>>>,
    /// Tests: the terrain of every world (instead of the heightmaps).
    pub(crate) test_terrain: OnceLock<Arc<dyn TerrainSource>>,
}

impl std::fmt::Debug for CollisionSystem {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("CollisionSystem")
            .field("worlds", &self.worlds.len())
            .finish()
    }
}

/// The heightmap samples of a world template as the terrain source.
pub struct HeightmapTerrain {
    pub worlds: Arc<HeightmapWorlds>,
    pub template_id: u32,
    pub size: (i32, i32),
}

impl TerrainSource for HeightmapTerrain {
    fn sample(&self, ix: i32, iy: i32) -> f32 {
        match self.worlds.template(self.template_id) {
            Some(t) => t.grid_sample(self.worlds.files(), ix, iy),
            None => 0.0,
        }
    }

    fn size(&self) -> (i32, i32) {
        self.size
    }
}

/// Files for cells / models: the client files, or the test map.
#[derive(Clone)]
pub enum Files {
    Client(Arc<ClientFileManager>),
    Map(Arc<HashMap<String, Vec<u8>>>),
}

impl Files {
    pub fn read(&self, path: &str) -> Option<Vec<u8>> {
        match self {
            Files::Client(f) => f.read_file(path),
            Files::Map(m) => m.get(path).cloned(),
        }
    }
}

impl world::CellFiles for Files {
    fn read(&self, path: &str) -> Option<Vec<u8>> {
        Files::read(self, path)
    }
}

impl models::ModelSource for Files {
    fn read(&self, path: &str) -> Option<Vec<u8>> {
        Files::read(self, path)
    }
}

impl CollisionSystem {
    /// The physics world of an instance, if it exists already.
    pub fn existing(&self, instance_id: u32) -> Option<Arc<PhysWorld>> {
        self.worlds.get(&instance_id).map(|w| w.clone())
    }

    /// Every physics world.
    pub fn all(&self) -> Vec<Arc<PhysWorld>> {
        self.worlds.iter().map(|w| w.clone()).collect()
    }

    /// Drops an instance's physics world.
    pub fn remove(&self, instance_id: u32) -> bool {
        self.worlds.remove(&instance_id).is_some()
    }

    /// The resolved doodad shapes.
    pub fn shape_cache(
        &self,
    ) -> std::sync::MutexGuard<'_, HashMap<String, Option<objects::ColliderShape>>> {
        self.shapes.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// The model cache (created with the first world).
    pub fn models(&self) -> Option<&Arc<ModelCache>> {
        self.models.get()
    }
}

/// The client / test files, `None` when the service is off.
pub fn files(server: &GameServer) -> Option<Files> {
    if let Some(m) = server.systems.ai.collision.test_files.get() {
        return Some(Files::Map(m.clone()));
    }
    if !server.config.physics.enable {
        return None;
    }
    let hm = server.systems.npcs.heightmaps.get()?;
    if !hm.config.heightmaps_enable {
        return None;
    }
    Some(Files::Client(hm.files().clone()))
}

pub(crate) fn model_cache(server: &GameServer, files: &Files) -> Arc<ModelCache> {
    server
        .systems
        .ai
        .collision
        .models
        .get_or_init(|| Arc::new(ModelCache::new(Arc::new(files.clone()))))
        .clone()
}

/// The `vegetation.xml` groups of a world.
pub fn vegetation_groups(
    server: &GameServer,
    files: &Files,
    world_name: &str,
) -> Arc<HashMap<u16, VegetationGroup>> {
    let sys = &server.systems.ai.collision;
    if let Some(g) = sys.veg_groups.get(world_name) {
        return g.clone();
    }
    let xml = files
        .read(&format!("game/worlds/{world_name}/vegetation.xml"))
        .map(|b| crate::data::client_files::decode_text(&b))
        .unwrap_or_default();
    let groups: HashMap<u16, VegetationGroup> = cells::read_vegetation_groups(&xml)
        .into_iter()
        .map(|g| (g.id, g))
        .collect();
    let g = Arc::new(groups);
    sys.veg_groups.insert(world_name.to_string(), g.clone());
    g
}

/// The physics world of an instance, created on first use (`None` when the service is off or
/// the instance does not exist).
pub fn phys_world(server: &GameServer, instance_id: u32) -> Option<Arc<PhysWorld>> {
    let sys = &server.systems.ai.collision;
    if let Some(w) = sys.existing(instance_id) {
        return Some(w);
    }
    files(server)?;
    let inst = server.world.world(instance_id)?;
    let t = &inst.template;
    let terrain: Option<Arc<dyn TerrainSource>> = sys.test_terrain.get().cloned().or_else(|| {
        server.systems.npcs.heightmaps.get().and_then(|hm| {
            hm.template(t.id)?;
            Some(Arc::new(HeightmapTerrain {
                worlds: hm.clone(),
                template_id: t.id,
                size: (
                    t.cell_x * crate::systems::npcs::heightmap::CELL_HMAP_RESOLUTION,
                    t.cell_y * crate::systems::npcs::heightmap::CELL_HMAP_RESOLUTION,
                ),
            }) as Arc<dyn TerrainSource>)
        })
    });
    let entry = sys.worlds.entry(instance_id).or_insert_with(|| {
        tracing::info!(
            "collision: physics world for instance {instance_id} ({}, {}×{} cells)",
            t.name,
            t.cell_x,
            t.cell_y
        );
        Arc::new(PhysWorld::new(
            instance_id,
            t.id,
            &t.name,
            t.cell_x,
            t.cell_y,
            t.ocean_level,
            terrain,
        ))
    });
    Some(entry.clone())
}

/// Loads a cell now (no-op when loaded; waits for another thread loading it).
pub fn load_cell(server: &GameServer, pw: &PhysWorld, cell: (i32, i32)) {
    if cell.0 < 0 || cell.1 < 0 || cell.0 >= pw.cells_x || cell.1 >= pw.cells_y {
        return;
    }
    if pw.touch_cell(cell) {
        return;
    }
    let Some(files) = files(server) else {
        return;
    };
    if !pw.begin_load(cell) {
        pw.wait_loaded(cell);
        return;
    }
    let level = server.config.physics.geometry;
    let content = if level == GeometryLevel::Terrain {
        world::CellContent {
            cell,
            ..world::CellContent::default()
        }
    } else {
        let models = model_cache(server, &files);
        let groups = vegetation_groups(server, &files, &pw.world_name);
        world::prepare_cell(
            &files,
            &models,
            &pw.world_name,
            &groups,
            cell,
            true,
            level == GeometryLevel::Full,
        )
    };
    tracing::debug!(
        "collision: instance {} cell {:?}: {} brushes, {} vegetation records, {} instances, {} µs",
        pw.instance_id,
        cell,
        content.brush_records,
        content.vegetation_records,
        content.instances.len(),
        content.prepare_micros
    );
    pw.end_load(Some(content), cell);
}

/// Loads (when `Physics.LoadOnQuery`) the cells a query box touches.
pub fn ensure_box(server: &GameServer, pw: &PhysWorld, min: Vec3, max: Vec3) {
    let c0 = (
        (min.x / 1024.0).floor() as i32,
        (min.y / 1024.0).floor() as i32,
    );
    let c1 = (
        (max.x / 1024.0).floor() as i32,
        (max.y / 1024.0).floor() as i32,
    );
    if (c1.0 - c0.0 + 1) * (c1.1 - c0.1 + 1) > 16 {
        return; // a huge query: do not load the world for it
    }
    let load = server.config.physics.load_on_query
        || server.systems.ai.collision.test_files.get().is_some();
    for cx in c0.0..=c1.0 {
        for cy in c0.1..=c1.1 {
            if load {
                load_cell(server, pw, (cx, cy));
            } else {
                pw.touch_cell((cx, cy));
            }
        }
    }
}

fn v(p: [f32; 3]) -> Vec3 {
    Vec3::new(p[0], p[1], p[2])
}

/// `GetTerrainElevation(x, y)` (I3DEngine vt+1064): terrain only, 0 where the engine returns
/// 0 (see [`terrain::terrain_elevation`]). `None` without the service.
pub fn terrain_height(server: &GameServer, instance_id: u32, x: f32, y: f32) -> Option<f32> {
    let pw = phys_world(server, instance_id)?;
    let t = pw.terrain.as_ref()?;
    Some(terrain::terrain_elevation(t.as_ref(), x, y))
}

/// `RayWorldIntersection(from → to, objtypes, flags)`; loads the touched cells first.
pub fn raycast(
    server: &GameServer,
    instance_id: u32,
    from: [f32; 3],
    to: [f32; 3],
    objtypes: u32,
    flags: u32,
) -> Option<Option<Hit>> {
    let pw = phys_world(server, instance_id)?;
    let (a, b) = (v(from), v(to));
    let min = Vec3::new(a.x.min(b.x), a.y.min(b.y), a.z.min(b.z));
    let max = Vec3::new(a.x.max(b.x), a.y.max(b.y), a.z.max(b.z));
    ensure_box(server, &pw, min, max);
    let mut st = pw.lock();
    Some(st.raycast(a, b, objtypes, flags, &[]))
}

/// The highest solid surface (static geometry or terrain, mask 0x101) under `(x, y)` between
/// `z + up` and `z - down`. `None` without the service; `Some(None)` when nothing is hit.
pub fn ground_height_probe(
    server: &GameServer,
    instance_id: u32,
    x: f32,
    y: f32,
    z: f32,
    up: f32,
    down: f32,
) -> Option<Option<f32>> {
    let r = raycast(
        server,
        instance_id,
        [x, y, z + up],
        [x, y, z - down],
        mask::STATIC_TERRAIN,
        ray_flags::DEFAULT,
    )?;
    Some(r.map(|h| h.pos.z))
}

/// Ground under a unit at `(x, y, z)`: the first static / terrain surface below `z + 2 m`
/// (a unit's step height, so floors above its head are ignored); the terrain elevation when
/// nothing is hit. `None` without the service.
pub fn ground_height(server: &GameServer, instance_id: u32, x: f32, y: f32, z: f32) -> Option<f32> {
    match ground_height_probe(
        server,
        instance_id,
        x,
        y,
        z,
        GROUND_PROBE_UP,
        GROUND_PROBE_DOWN,
    )? {
        Some(h) => Some(h),
        None => terrain_height(server, instance_id, x, y),
    }
}

/// [`ground_height`], or `fallback()` without the service (the previous heightmap answer).
pub fn ground_height_or(
    server: &GameServer,
    instance_id: u32,
    x: f32,
    y: f32,
    z: f32,
    fallback: impl FnOnce() -> f32,
) -> f32 {
    ground_height(server, instance_id, x, y, z).unwrap_or_else(fallback)
}

/// [`terrain_height`] (`GetTerrainElevation`), or `fallback()` without the service.
pub fn terrain_height_or(
    server: &GameServer,
    instance_id: u32,
    x: f32,
    y: f32,
    fallback: impl FnOnce() -> f32,
) -> f32 {
    terrain_height(server, instance_id, x, y).unwrap_or_else(fallback)
}

/// How far above a unit's z the ground probe starts.
pub const GROUND_PROBE_UP: f32 = 2.0;
/// How far below it ends.
pub const GROUND_PROBE_DOWN: f32 = 2048.0;

/// Line of sight between two points: no static geometry or terrain in between (mask 0x101,
/// rays 0x8F). `None` without the service.
pub fn line_of_sight(
    server: &GameServer,
    instance_id: u32,
    from: [f32; 3],
    to: [f32; 3],
) -> Option<bool> {
    let r = raycast(
        server,
        instance_id,
        from,
        to,
        mask::STATIC_TERRAIN,
        ray_flags::DEFAULT,
    )?;
    Some(r.is_none())
}

/// Sweeps a vertical capsule (radius `r`, cylinder half height `hh`) centred at `from` to `to`
/// against `ent_types` (`PrimitiveWorldIntersection`). `None` without the service;
/// `Some(None)` = free path.
pub fn sweep_capsule(
    server: &GameServer,
    instance_id: u32,
    from: [f32; 3],
    to: [f32; 3],
    r: f32,
    hh: f32,
    ent_types: u32,
) -> Option<Option<SweepHit>> {
    let pw = phys_world(server, instance_id)?;
    let (a, b) = (v(from), v(to));
    let ext = r + hh;
    let min = Vec3::new(a.x.min(b.x) - ext, a.y.min(b.y) - ext, a.z.min(b.z) - ext);
    let max = Vec3::new(a.x.max(b.x) + ext, a.y.max(b.y) + ext, a.z.max(b.z) + ext);
    ensure_box(server, &pw, min, max);
    let mut st = pw.lock();
    Some(st.sweep(query::capsule(a, r, hh), b - a, ent_types, 0xFFFF_FFFF, &[]))
}

/// `GetWaterLevel(pos)` (I3DEngine vt+568, 0x3013FCB0): the water-volume surface, else the
/// ocean level; `None` when the point is not in water (the engine's −1e6).
pub fn water_level(server: &GameServer, instance_id: u32, x: f32, y: f32, z: f32) -> Option<f32> {
    let p = crate::systems::skills::controllers::Vec3::new(x, y, z);
    let (level, _) = crate::systems::ai::physics::water_surface(server, instance_id, p)?;
    Some(level)
}

/// Is the point under water (water volumes + ocean).
pub fn is_water(server: &GameServer, instance_id: u32, x: f32, y: f32, z: f32) -> bool {
    let p = crate::systems::skills::controllers::Vec3::new(x, y, z);
    crate::systems::ai::physics::is_water(server, instance_id, p).is_some()
}

/// The surface index of the first solid surface under `(x, y)` (ray mask 0x11F like the
/// surface-id query `sub_394B6660`); -1 without a material mapping (terrain `cover.ctc` and
/// CGF material surface types are not loaded).
pub fn surface_type(server: &GameServer, instance_id: u32, x: f32, y: f32, z: f32) -> Option<i16> {
    let r = raycast(
        server,
        instance_id,
        [x, y, z + GROUND_PROBE_UP],
        [x, y, z - GROUND_PROBE_DOWN],
        mask::ALL_TERRAIN,
        ray_flags::DEFAULT,
    )?;
    r.map(|h| h.surface)
}
