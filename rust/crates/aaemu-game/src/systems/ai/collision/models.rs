//! The model cache: one set of physics geometries per `.cgf` path, shared by every world
//! instance and every brush / vegetation / doodad that uses the model (the engine shares one
//! `CStatObj` per file the same way).
//!
//! A model is loaded on first use: the CGF's `MeshPhysicsData` blobs ([`super::cgf`]) go
//! through `LoadPhysGeometry` (`aaemu_physics::geometry::shapes::stream::load_phys_geometry_any`,
//! cryphysics slot 16 0x3514B340) with the render arrays for blobs that share the render
//! mesh. Entries no longer referenced by any loaded cell or collider are dropped by
//! [`ModelCache::evict_unused`].

use std::collections::HashMap;
use std::sync::atomic::{AtomicU64, AtomicUsize, Ordering};
use std::sync::{Arc, Mutex, OnceLock};

use aaemu_physics::geometry::shapes::stream::load_phys_geometry_any;
use aaemu_physics::geometry::trimesh::stream::RenderArrays;
use aaemu_physics::geometry::PhysGeometry;

use crate::data::client_files::ClientFileManager;

use super::cgf;

/// `AddPhysGeom` types: `0x1000 + slot`.
pub mod phys_slot {
    /// `PHYS_GEOM_TYPE_DEFAULT` (collide + rays).
    pub const DEFAULT: usize = 0;
    /// `PHYS_GEOM_TYPE_NO_COLLIDE` (rays only).
    pub const NO_COLLIDE: usize = 1;
    /// `PHYS_GEOM_TYPE_OBSTRUCT` (AI / view obstruction).
    pub const OBSTRUCT: usize = 2;
}

/// One physics geometry of a model.
#[derive(Debug, Clone)]
pub struct ModelPart {
    /// [`phys_slot`].
    pub slot: usize,
    pub geom: Arc<PhysGeometry>,
}

/// The physics of one model file.
#[derive(Debug)]
pub struct ModelPhys {
    pub path: String,
    pub parts: Vec<ModelPart>,
    /// Size of the physics blobs (memory accounting).
    pub blob_bytes: usize,
}

impl ModelPhys {
    /// The geometry of a slot (the engine keeps one per type; the first wins).
    pub fn slot(&self, slot: usize) -> Option<&Arc<PhysGeometry>> {
        self.parts.iter().find(|p| p.slot == slot).map(|p| &p.geom)
    }

    /// Has a default (colliding) geometry.
    pub fn collides(&self) -> bool {
        self.slot(phys_slot::DEFAULT).is_some()
    }
}

/// Builds the model physics from a CGF file's bytes. `None` = no physics in the file.
pub fn load_model(path: &str, data: &[u8]) -> Result<Option<ModelPhys>, String> {
    let blobs = match cgf::physics_blobs(data) {
        Ok(b) => b,
        // `.cdf` character definitions (XML) behind `cga://` doodad models: no static physics
        Err(cgf::CgfError::Signature) => return Ok(None),
        Err(e) => return Err(format!("{path}: {e}")),
    };
    let mut parts = Vec::new();
    let mut bytes = 0;
    for b in &blobs {
        if parts.iter().any(|p: &ModelPart| p.slot == b.slot) {
            continue;
        }
        let ra = match &b.render {
            Some(r) => RenderArrays {
                vertices: Some(&r.positions),
                indices: Some(&r.indices),
                mats: Some(&r.face_mats),
            },
            None => RenderArrays::default(),
        };
        let l = load_phys_geometry_any(&b.blob, &ra)
            .map_err(|e| format!("{path}: LoadPhysGeometry slot {}: {e:?}", b.slot))?;
        bytes += b.blob.len();
        parts.push(ModelPart {
            slot: b.slot,
            geom: Arc::new(PhysGeometry {
                geom: l.geom.into_arc(),
                inertia: l.header.inertia,
                q: l.header.q,
                origin: l.header.origin,
                volume: l.header.volume,
                surface_idx: l.header.surface_idx,
                mat_mapping: Vec::new(),
            }),
        });
    }
    if parts.is_empty() {
        return Ok(None);
    }
    Ok(Some(ModelPhys {
        path: path.to_string(),
        parts,
        blob_bytes: bytes,
    }))
}

/// Where model bytes come from (the client files on the server; a map in tests).
pub trait ModelSource: Send + Sync {
    fn read(&self, path: &str) -> Option<Vec<u8>>;
}

impl ModelSource for ClientFileManager {
    fn read(&self, path: &str) -> Option<Vec<u8>> {
        self.read_file(path)
    }
}

impl ModelSource for HashMap<String, Vec<u8>> {
    fn read(&self, path: &str) -> Option<Vec<u8>> {
        self.get(path).cloned()
    }
}

type Slot = Arc<OnceLock<Option<Arc<ModelPhys>>>>;

/// Cache statistics.
#[derive(Debug, Default)]
pub struct ModelCacheStats {
    pub loaded: AtomicUsize,
    pub missing: AtomicUsize,
    pub failed: AtomicUsize,
    pub no_physics: AtomicUsize,
    pub load_micros: AtomicU64,
}

/// `path → model physics` (`None` = missing file, load error or no physics).
pub struct ModelCache {
    source: Arc<dyn ModelSource>,
    entries: Mutex<HashMap<String, Slot>>,
    pub stats: ModelCacheStats,
}

impl std::fmt::Debug for ModelCache {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("ModelCache")
            .field("entries", &self.len())
            .finish()
    }
}

impl ModelCache {
    pub fn new(source: Arc<dyn ModelSource>) -> Self {
        Self {
            source,
            entries: Mutex::new(HashMap::new()),
            stats: ModelCacheStats::default(),
        }
    }

    /// The model's physics, loaded on first use (concurrent callers wait for one load).
    pub fn get(&self, path: &str) -> Option<Arc<ModelPhys>> {
        let slot = {
            let mut e = self.entries.lock().unwrap_or_else(|e| e.into_inner());
            e.entry(path.to_string()).or_default().clone()
        };
        slot.get_or_init(|| self.load(path)).clone()
    }

    fn load(&self, path: &str) -> Option<Arc<ModelPhys>> {
        let t = std::time::Instant::now();
        let r = match self.source.read(path) {
            None => {
                self.stats.missing.fetch_add(1, Ordering::Relaxed);
                tracing::debug!("collision: model not found: {path}");
                None
            }
            Some(bytes) => match load_model(path, &bytes) {
                Ok(Some(m)) => {
                    self.stats.loaded.fetch_add(1, Ordering::Relaxed);
                    Some(Arc::new(m))
                }
                Ok(None) => {
                    self.stats.no_physics.fetch_add(1, Ordering::Relaxed);
                    None
                }
                Err(e) => {
                    self.stats.failed.fetch_add(1, Ordering::Relaxed);
                    tracing::warn!("collision: {e}");
                    None
                }
            },
        };
        self.stats
            .load_micros
            .fetch_add(t.elapsed().as_micros() as u64, Ordering::Relaxed);
        r
    }

    /// Number of cached entries (models with and without physics).
    pub fn len(&self) -> usize {
        self.entries.lock().unwrap_or_else(|e| e.into_inner()).len()
    }

    pub fn is_empty(&self) -> bool {
        self.len() == 0
    }

    /// Sum of the physics blob sizes of the cached models.
    pub fn blob_bytes(&self) -> usize {
        self.entries
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .values()
            .filter_map(|s| s.get().cloned().flatten())
            .map(|m| m.blob_bytes)
            .sum()
    }

    /// Drops the models nobody else references (loaded cells and colliders hold an `Arc`).
    /// Entries for missing / physics-less files are kept (they are cheap and avoid re-reads).
    pub fn evict_unused(&self) -> usize {
        let mut e = self.entries.lock().unwrap_or_else(|e| e.into_inner());
        let before = e.len();
        e.retain(|_, s| match s.get() {
            Some(Some(m)) => Arc::strong_count(m) > 1,
            _ => true,
        });
        before - e.len()
    }
}
