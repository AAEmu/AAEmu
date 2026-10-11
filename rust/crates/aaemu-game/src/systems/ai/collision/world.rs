//! One physics world per world instance (`aaemu_physics::world::World`): the terrain
//! heightfield, the ocean (global buoyancy area), the static brushes and vegetation of the
//! loaded cells, and the colliders of world objects (doodads, houses, ships).
//!
//! What the zone does (`ZONE-PHYSICS-USAGE.md` §2, `sub_x2game.md` §1):
//!
//! * `SeamlessSystem::LoadZone` (crysystem 0x365704D0) loads the terrain and the objects of
//!   every cell of the zone. AAEmu has one process for the whole world, so cells are loaded
//!   lazily around players / ships / queries instead ([`super::residency`]), the way the
//!   heightmap is.
//! * Terrain: `SetHeightfieldData` (cryphysics world slot 17) with the heightmap samples
//!   (`ent_terrain`, objtypes 0x100).
//! * Brushes: `CBrush::Physicalize` (cry3dengine 0x30111970): a static entity at the brush
//!   matrix (position = translation, scale = |column 0|, rotation = the matrix divided by the
//!   scale), parts `0x1000` (flags 0x18FFF, 0x10FFF when the model also has a `0x1001`
//!   geometry, `| 0xA` with render flag 0x400000), `0x1001` (flags 0x8000: rays only),
//!   `0x1002` (flags 0x4000: obstruct), entity flags `|= 0x8000` (`pe_params_flags`).
//! * Vegetation: `CVegetation::Physicalize` (0x30103DC0): a static entity at the instance
//!   matrix (0x30102840), part `0x1000` with density 800, flags 0x04018FFF, collider 1,
//!   `0x1002` (0x4000) when present; `0x1001` only with `e_phys_veg…` ≥ 2 (not on the server).
//! * Ocean: the global area as a water plane at `GetWaterLevel` = `oceanLevel`
//!   (`C3DEngine::Update` 0x30143AA0; `e_phys_ocean_cell = 0` on the server unless the zone
//!   simulates waves, which is out of scope).
//! * The zone's `gEnv` byte 289 is set on the server, so RWI's "DrawRayOnGrid" ray-extent limit
//!   does not apply.

use std::collections::HashMap;
use std::sync::{Arc, Mutex, MutexGuard};
use std::time::Instant;

use aaemu_physics::entities::base::add_geometry;
use aaemu_physics::geometry::primitives::HeightfieldPrim;
use aaemu_physics::geometry::PhysGeometry;
use aaemu_physics::math::{Mat33, Quat, Vec3};
use aaemu_physics::params::{GeomParams, ParamsFlags, ParamsPos};
use aaemu_physics::world::{EntIdx, World};

use super::cells::{self, BrushRecord, VegetationGroup};
use super::models::{phys_slot, ModelCache, ModelPhys};
use super::terrain::{TerrainSlot, TerrainSource};

/// The grid's z origin (entities below it are invisible to the broad phase; the quantised z
/// range is 4096 m from here).
pub const GRID_Z0: f32 = -256.0;
/// The TimeStep flags of the zone (`World::step` uses the same).
pub const STEP_FLAGS: i32 = 0x9F;

/// The surface index used for the water material (`SetWaterMat`). *(inferred)*: the zone
/// passes the id of `mat_water` from its surface-type table, which is not loaded here; any
/// index ≥ 0 enables the water-plane ray hits, and hits report it as their surface.
pub const WATER_SURFACE_IDX: i32 = 1;

/// Who owns a physical entity.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum Owner {
    Terrain,
    Brush {
        cell: (i32, i32),
    },
    Vegetation {
        cell: (i32, i32),
    },
    /// A world object (doodad, house, gimmick, ship, unit) by obj id.
    Object {
        kind: ObjKind,
        obj_id: u32,
    },
    /// The warm-up entity (far below the world, never hit).
    Sentinel,
}

/// Kinds of world-object colliders.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum ObjKind {
    Doodad,
    House,
    Gimmick,
    Ship,
    Unit,
}

/// A static model instance ready to be added.
#[derive(Debug, Clone)]
pub struct StaticInstance {
    pub model: Arc<ModelPhys>,
    pub pos: Vec3,
    pub q: Quat,
    pub scale: f32,
    /// Part flags of the default (`0x1000`) geometry.
    pub flags_default: u32,
    /// Density of the parts (vegetation 800; brushes 0).
    pub density: f32,
    /// `pe_params_flags.flagsOR` applied after creation (brushes 0x8000).
    pub ent_flags_or: u32,
    pub vegetation: bool,
}

/// Everything a cell adds, prepared outside the world lock.
#[derive(Debug, Default)]
pub struct CellContent {
    pub cell: (i32, i32),
    pub instances: Vec<StaticInstance>,
    /// Brush / vegetation records in the files.
    pub brush_records: usize,
    pub vegetation_records: usize,
    /// Records whose model is missing / has no physics.
    pub skipped: usize,
    pub prepare_micros: u64,
}

/// A loaded cell.
#[derive(Debug)]
pub struct LoadedCell {
    pub entities: Vec<EntIdx>,
    pub brushes: usize,
    pub vegetation: usize,
    pub last_used: Instant,
    /// Keeps the models referenced (cache eviction).
    pub models: Vec<Arc<ModelPhys>>,
}

/// A world-object collider.
#[derive(Debug, Clone)]
pub struct ObjCollider {
    pub ent: EntIdx,
    pub kind: ObjKind,
    /// What it was built from (rebuilt when it changes).
    pub key: ColliderKey,
    /// Keeps the models referenced (cache eviction).
    pub models: Vec<Arc<ModelPhys>>,
}

/// The inputs of an object collider.
#[derive(Debug, Clone, PartialEq)]
pub struct ColliderKey {
    pub model: String,
    pub pos: [f32; 3],
    pub q: [f32; 4],
    pub scale: f32,
    pub flags_or: u32,
    pub flags_and: u32,
}

/// Brush part flags (`CBrush::Physicalize`).
pub fn brush_default_flags(model: &ModelPhys, rnd_flags: u32) -> u32 {
    let mut f = if model.slot(phys_slot::NO_COLLIDE).is_some() {
        0x10FFF
    } else {
        0x18FFF
    };
    if rnd_flags & 0x40_0000 != 0 {
        f |= 0xA;
    }
    f
}

/// `CBrush::Physicalize`: placement from the brush matrix (`pos`, `scale = |col 0|`,
/// `q = quat(M / scale)`).
pub fn brush_instance(model: Arc<ModelPhys>, origin: Vec3, b: &BrushRecord) -> StaticInstance {
    let m = &b.rot_scale;
    let c0 = m.col(0);
    let s = ((c0.y * c0.y + c0.x * c0.x) + c0.z * c0.z).sqrt();
    let inv = 1.0 / s;
    let a = m.to_array();
    let n = Mat33::from_array([
        inv * a[0],
        a[1] * inv,
        a[2] * inv,
        a[3] * inv,
        a[4] * inv,
        a[5] * inv,
        a[6] * inv,
        a[7] * inv,
        a[8] * inv,
    ]);
    StaticInstance {
        flags_default: brush_default_flags(&model, b.rnd_flags),
        model,
        pos: b.pos + origin,
        q: Quat::from_mat33(&n),
        scale: s,
        density: 0.0,
        ent_flags_or: 0x8000,
        vegetation: false,
    }
}

/// `CVegetation::Physicalize`: placement from the instance (`pos`, uniform `scale`,
/// rotation from the byte angles).
pub fn vegetation_instance(
    model: Arc<ModelPhys>,
    origin: Vec3,
    v: &cells::VegetationRecord,
) -> StaticInstance {
    let r = cells::vegetation_rotation(v);
    StaticInstance {
        model,
        pos: v.pos + origin,
        q: Quat::from_mat33(&r),
        scale: v.scale,
        flags_default: 0x0401_8FFF,
        density: 800.0,
        ent_flags_or: 0,
        vegetation: true,
    }
}

/// Reads a cell's object files and resolves the models (no world lock held).
pub fn prepare_cell(
    files: &dyn CellFiles,
    models: &ModelCache,
    world_name: &str,
    groups: &HashMap<u16, VegetationGroup>,
    cell: (i32, i32),
    with_brushes: bool,
    with_vegetation: bool,
) -> CellContent {
    let t = Instant::now();
    let mut c = CellContent {
        cell,
        ..CellContent::default()
    };
    let dir = format!(
        "game/worlds/{world_name}/cells/{:03}_{:03}/client",
        cell.0, cell.1
    );
    let origin = Vec3::new(cell.0 as f32 * 1024.0, cell.1 as f32 * 1024.0, 0.0);
    if with_brushes {
        let names = files
            .read(&format!("{dir}/statobjs.dat"))
            .map(|d| cells::read_statobjs(&d))
            .unwrap_or_default();
        if let Some(d) = files.read(&format!("{dir}/brush.dat")) {
            match cells::read_brushes(&d) {
                Ok(list) => {
                    c.brush_records = list.len();
                    let mut resolved: HashMap<i32, Option<Arc<ModelPhys>>> = HashMap::new();
                    for b in &list {
                        let m = resolved
                            .entry(b.statobj)
                            .or_insert_with(|| {
                                names.get(b.statobj as usize).and_then(|n| models.get(n))
                            })
                            .clone();
                        match m {
                            Some(m) => c.instances.push(brush_instance(m, origin, b)),
                            None => c.skipped += 1,
                        }
                    }
                }
                Err(e) => tracing::warn!("collision: {dir}/brush.dat: {e}"),
            }
        }
    }
    if with_vegetation {
        if let Some(d) = files.read(&format!("{dir}/vegetation.dat")) {
            match cells::read_vegetation(&d) {
                Ok(list) => {
                    c.vegetation_records = list.len();
                    let mut resolved: HashMap<u16, Option<Arc<ModelPhys>>> = HashMap::new();
                    for v in &list {
                        let m = resolved
                            .entry(v.group)
                            .or_insert_with(|| {
                                groups
                                    .get(&v.group)
                                    .filter(|g| !g.model.is_empty())
                                    .and_then(|g| models.get(&g.model))
                            })
                            .clone();
                        match m {
                            Some(m) if m.collides() => {
                                c.instances.push(vegetation_instance(m, origin, v));
                            }
                            _ => c.skipped += 1,
                        }
                    }
                }
                Err(e) => tracing::warn!("collision: {dir}/vegetation.dat: {e}"),
            }
        }
    }
    c.prepare_micros = t.elapsed().as_micros() as u64;
    c
}

/// File access for the cell loader.
pub trait CellFiles: Send + Sync {
    fn read(&self, path: &str) -> Option<Vec<u8>>;
}

impl CellFiles for crate::data::client_files::ClientFileManager {
    fn read(&self, path: &str) -> Option<Vec<u8>> {
        self.read_file(path)
    }
}

impl CellFiles for HashMap<String, Vec<u8>> {
    fn read(&self, path: &str) -> Option<Vec<u8>> {
        self.get(path).cloned()
    }
}

/// Counters of a world.
#[derive(Debug, Default, Clone)]
pub struct WorldStats {
    pub cells_loaded: usize,
    pub cells_unloaded: usize,
    pub static_entities: usize,
    pub brushes: usize,
    pub vegetation: usize,
    pub object_colliders: usize,
    pub load_micros: u64,
}

/// The lock-protected part of a [`PhysWorld`].
pub struct WorldState {
    pub world: World,
    pub terrain_ent: EntIdx,
    pub cells: HashMap<(i32, i32), LoadedCell>,
    pub owners: HashMap<EntIdx, Owner>,
    pub objects: HashMap<(ObjKind, u32), ObjCollider>,
    pub stats: WorldStats,
    /// Entities destroyed since the last step (freed by the next `TimeStep`).
    pub pending_deletes: usize,
    /// Water-volume areas by volume key ([`super::water_areas`]).
    pub water_areas: HashMap<String, Vec<EntIdx>>,
    /// Living proxies of units by obj id ([`super::living`]).
    pub living: HashMap<u32, EntIdx>,
}

impl std::fmt::Debug for WorldState {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("WorldState")
            .field("cells", &self.cells.len())
            .field("owners", &self.owners.len())
            .field("stats", &self.stats)
            .finish()
    }
}

impl WorldState {
    /// Adds one static model instance (parts per the engine's flags).
    pub fn add_static_instance(&mut self, inst: &StaticInstance, owner: Owner) -> Option<EntIdx> {
        let pp = ParamsPos {
            pos: inst.pos,
            q: inst.q,
            scale: inst.scale,
            ..ParamsPos::default()
        };
        let e = self
            .world
            .create_physical_entity(1, 0.0, Some(&pp), 0, 0, -1)?;
        for part in &inst.model.parts {
            let flags = match part.slot {
                phys_slot::DEFAULT => inst.flags_default,
                phys_slot::NO_COLLIDE if !inst.vegetation => 0x8000,
                phys_slot::OBSTRUCT => 0x4000,
                _ => continue,
            };
            add_part(&mut self.world, e, &part.geom, flags, inst.density);
        }
        if inst.ent_flags_or != 0 {
            let f = ParamsFlags {
                flags_or: inst.ent_flags_or as i32,
                ..ParamsFlags::default()
            };
            aaemu_physics::entities::interface::set::set_params_flags(&mut self.world, e, &f);
        }
        self.owners.insert(e, owner);
        Some(e)
    }

    /// Adds a prepared cell (no-op when it is loaded already).
    pub fn insert_cell(&mut self, content: CellContent) -> bool {
        if self.cells.contains_key(&content.cell) {
            return false;
        }
        let t = Instant::now();
        let mut entities = Vec::with_capacity(content.instances.len());
        let (mut nb, mut nv) = (0, 0);
        let mut models: Vec<Arc<ModelPhys>> = Vec::new();
        for inst in &content.instances {
            let owner = if inst.vegetation {
                nv += 1;
                Owner::Vegetation { cell: content.cell }
            } else {
                nb += 1;
                Owner::Brush { cell: content.cell }
            };
            if let Some(e) = self.add_static_instance(inst, owner) {
                entities.push(e);
            }
            if !models.iter().any(|m| Arc::ptr_eq(m, &inst.model)) {
                models.push(inst.model.clone());
            }
        }
        self.stats.cells_loaded += 1;
        self.stats.static_entities += entities.len();
        self.stats.brushes += nb;
        self.stats.vegetation += nv;
        self.stats.load_micros += t.elapsed().as_micros() as u64 + content.prepare_micros;
        self.cells.insert(
            content.cell,
            LoadedCell {
                entities,
                brushes: nb,
                vegetation: nv,
                last_used: Instant::now(),
                models,
            },
        );
        true
    }

    /// Removes a cell's static entities.
    pub fn remove_cell(&mut self, cell: (i32, i32)) -> bool {
        let Some(c) = self.cells.remove(&cell) else {
            return false;
        };
        for e in &c.entities {
            self.world.destroy_physical_entity(*e, 0);
            self.owners.remove(e);
        }
        self.stats.cells_unloaded += 1;
        self.stats.static_entities -= c.entities.len();
        self.stats.brushes -= c.brushes;
        self.stats.vegetation -= c.vegetation;
        self.pending_deletes += c.entities.len();
        true
    }

    /// Frees destroyed entities (a `TimeStep` runs the cleanup).
    pub fn collect_garbage(&mut self) {
        if self.pending_deletes > 0 {
            self.world.time_step(0.0001, STEP_FLAGS);
            self.world.pump_logged_events();
            self.pending_deletes = 0;
        }
    }

    /// Adds or replaces the collider of a world object (`None` shape = remove). Returns true
    /// when a collider was (re)built.
    pub fn set_object_shape(
        &mut self,
        kind: ObjKind,
        obj_id: u32,
        key: ColliderKey,
        shape: Option<&super::objects::ColliderShape>,
        rigid: bool,
    ) -> bool {
        if let Some(old) = self.objects.get(&(kind, obj_id)) {
            if old.key == key {
                return false;
            }
        }
        self.remove_object_collider(kind, obj_id);
        let Some(shape) = shape.filter(|s| !s.parts.is_empty()) else {
            return false;
        };
        let pp = ParamsPos {
            pos: Vec3::new(key.pos[0], key.pos[1], key.pos[2]),
            q: Quat::new(key.q[0], key.q[1], key.q[2], key.q[3]),
            scale: key.scale,
            ..ParamsPos::default()
        };
        let etype = if rigid { 2 } else { 1 };
        let Some(e) = self
            .world
            .create_physical_entity(etype, 0.0, Some(&pp), 0, 0, -1)
        else {
            return false;
        };
        for sp in &shape.parts {
            let m = &sp.model;
            for part in &m.parts {
                let base = match part.slot {
                    phys_slot::DEFAULT => brush_default_flags(m, 0),
                    phys_slot::NO_COLLIDE => 0x8000,
                    phys_slot::OBSTRUCT => 0x4000,
                    _ => continue,
                };
                // _UpdateCollisionParams (x2game sub_39451190): flagsOR / flagsAND on every part.
                let flags = (base & key.flags_and) | key.flags_or;
                let gp = GeomParams {
                    density: 0.0,
                    mass: 0.0,
                    pos: sp.pos,
                    q: sp.q,
                    scale: sp.scale,
                    flags,
                    flags_collider: 1,
                    ..GeomParams::default()
                };
                add_geometry(&mut self.world, e, &part.geom, &gp, -1);
            }
        }
        self.owners.insert(e, Owner::Object { kind, obj_id });
        self.stats.object_colliders += 1;
        self.objects.insert(
            (kind, obj_id),
            ObjCollider {
                ent: e,
                kind,
                key,
                models: shape.parts.iter().map(|p| p.model.clone()).collect(),
            },
        );
        true
    }

    /// Removes a world object's collider.
    pub fn remove_object_collider(&mut self, kind: ObjKind, obj_id: u32) -> bool {
        let Some(c) = self.objects.remove(&(kind, obj_id)) else {
            return false;
        };
        self.world.destroy_physical_entity(c.ent, 0);
        self.owners.remove(&c.ent);
        self.stats.object_colliders -= 1;
        self.pending_deletes += 1;
        true
    }

    /// The owner of an entity.
    pub fn owner(&self, e: EntIdx) -> Option<Owner> {
        if e == self.terrain_ent || self.world.terrain.contains(&e) {
            return Some(Owner::Terrain);
        }
        self.owners.get(&e).copied()
    }
}

/// `AddGeometry` with `pe_geomparams` as the engine fills it for static parts.
pub fn add_part(w: &mut World, e: EntIdx, geom: &Arc<PhysGeometry>, flags: u32, density: f32) {
    let gp = GeomParams {
        density,
        mass: 0.0,
        flags,
        flags_collider: 1,
        ..GeomParams::default()
    };
    add_geometry(w, e, geom, &gp, -1);
}

/// The physics of one world instance.
pub struct PhysWorld {
    pub instance_id: u32,
    pub template_id: u32,
    pub world_name: String,
    /// World size in cells.
    pub cells_x: i32,
    pub cells_y: i32,
    pub ocean_level: f32,
    pub terrain: Option<Arc<dyn TerrainSource>>,
    _slot: Option<TerrainSlot>,
    state: Mutex<WorldState>,
    /// Cells being prepared by some thread (avoids double reads).
    loading: Mutex<Vec<(i32, i32)>>,
}

impl std::fmt::Debug for PhysWorld {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("PhysWorld")
            .field("instance_id", &self.instance_id)
            .field("world_name", &self.world_name)
            .finish()
    }
}

impl PhysWorld {
    /// A world of `cells_x × cells_y` cells with terrain from `terrain` (none = no heightfield)
    /// and the ocean at `ocean_level`.
    pub fn new(
        instance_id: u32,
        template_id: u32,
        world_name: &str,
        cells_x: i32,
        cells_y: i32,
        ocean_level: f32,
        terrain: Option<Arc<dyn TerrainSource>>,
    ) -> Self {
        let mut w = World::new();
        let size_x = cells_x.max(1) as f32 * 1024.0;
        let size_y = cells_y.max(1) as f32 * 1024.0;
        // SetupEntityGrid: at most 1024 cells per axis.
        let step = (size_x.max(size_y) / 1024.0).ceil().max(16.0);
        let nx = (size_x / step).ceil() as i32;
        let ny = (size_y / step).ceil() as i32;
        w.setup_entity_grid(2, Vec3::new(0.0, 0.0, GRID_Z0), nx, ny, step, step, 0);
        // gEnv+289 (server): no DrawRayOnGrid limit.
        w.queries.env_byte_289 = true;
        let slot = terrain.clone().and_then(TerrainSlot::acquire);
        let mut terrain_ent = EntIdx::NONE;
        if let (Some(slot), Some(src)) = (&slot, &terrain) {
            let (sx, sy) = src.size();
            let hf = HeightfieldPrim {
                step: [2.0, 2.0],
                stepr: [0.5, 0.5],
                size: [sx, sy],
                stride: [1, sx + 1],
                height_scale: 1.0,
                get_height: Some(slot.height_fn()),
                get_surf_type: Some(slot.surf_fn()),
                ..HeightfieldPrim::default()
            };
            terrain_ent = w.set_heightfield_data(Some(&hf), None);
        } else if terrain.is_some() {
            tracing::error!(
                "collision: no free terrain slot for instance {instance_id}; terrain is not in \
                 the physics world"
            );
        }
        // SetWaterMat (world +20840): the water surface type; without it RWI never reports the
        // global water plane (objtypes 0x200).
        w.water_mat = WATER_SURFACE_IDX;
        w.set_ocean(ocean_level, 1000.0, 1000.0);
        let mut st = WorldState {
            world: w,
            terrain_ent,
            cells: HashMap::new(),
            owners: HashMap::new(),
            objects: HashMap::new(),
            stats: WorldStats::default(),
            pending_deletes: 0,
            water_areas: HashMap::new(),
            living: HashMap::new(),
        };
        // Warm-up: the temp pools exist after the first entity, and after the first step with
        // dt > 0 the caller counts as a physics thread (entities join the grid at once).
        if let Some(g) = st
            .world
            .geometry(&aaemu_physics::geometry::primitives::Primitive::Sphere(
                aaemu_physics::geometry::primitives::SpherePrim {
                    center: Vec3::ZERO,
                    r: 0.1,
                },
            ))
        {
            let pp = ParamsPos {
                pos: Vec3::new(1.0, 1.0, GRID_Z0 + 1.0),
                ..ParamsPos::default()
            };
            if let Some(e) = st.world.create_physical_entity(1, 0.0, Some(&pp), 0, 0, -1) {
                add_part(&mut st.world, e, &g, 0, 0.0);
                st.owners.insert(e, Owner::Sentinel);
            }
        }
        st.world.time_step(0.0001, STEP_FLAGS);
        st.world.pump_logged_events();
        Self {
            instance_id,
            template_id,
            world_name: world_name.to_string(),
            cells_x,
            cells_y,
            ocean_level,
            terrain,
            _slot: slot,
            state: Mutex::new(st),
            loading: Mutex::new(Vec::new()),
        }
    }

    /// Locks the world.
    pub fn lock(&self) -> MutexGuard<'_, WorldState> {
        self.state.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// The cell of a position (`None` outside the world).
    pub fn cell_of(&self, x: f32, y: f32) -> Option<(i32, i32)> {
        let cx = (x / 1024.0).floor() as i32;
        let cy = (y / 1024.0).floor() as i32;
        (cx >= 0 && cy >= 0 && cx < self.cells_x && cy < self.cells_y).then_some((cx, cy))
    }

    /// Is the cell loaded (marks it used).
    pub fn touch_cell(&self, cell: (i32, i32)) -> bool {
        let mut st = self.lock();
        match st.cells.get_mut(&cell) {
            Some(c) => {
                c.last_used = Instant::now();
                true
            }
            None => false,
        }
    }

    /// Claims a cell for loading (false = loaded or being loaded by another thread).
    pub fn begin_load(&self, cell: (i32, i32)) -> bool {
        if self.lock().cells.contains_key(&cell) {
            return false;
        }
        let mut l = self.loading.lock().unwrap_or_else(|e| e.into_inner());
        if l.contains(&cell) {
            return false;
        }
        l.push(cell);
        true
    }

    /// Ends a load claimed with [`PhysWorld::begin_load`].
    pub fn end_load(&self, content: Option<CellContent>, cell: (i32, i32)) {
        if let Some(c) = content {
            self.lock().insert_cell(c);
        }
        self.loading
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .retain(|c| *c != cell);
    }

    /// Waits until no thread is loading `cell` (another thread claimed it).
    pub fn wait_loaded(&self, cell: (i32, i32)) {
        for _ in 0..2000 {
            if !self
                .loading
                .lock()
                .unwrap_or_else(|e| e.into_inner())
                .contains(&cell)
            {
                return;
            }
            std::thread::sleep(std::time::Duration::from_millis(1));
        }
    }
}
