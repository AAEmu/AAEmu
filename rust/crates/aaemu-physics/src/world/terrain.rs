//! **P3 — terrain (world side)**: the per-caller terrain entities (`world+1568 + 8·i`,
//! i = 0..4) created by `SetHeightfieldData`, the terrain placement (`world+1616` basis,
//! `+1652` origin) and the terrain ray of RWI.
//!
//! | function | address | port |
//! |---|---|---|
//! | SetTerrainOrigin (world slot 4) | 0x35078CA0 | [`World::set_terrain_origin`] |
//! | GetTerrainOrigin (slot 5) | 0x35096660 | [`World::terrain_origin`] (field) |
//! | SetHeightfieldData (slot 17) | 0x3508B860 | [`World::set_heightfield_data_with`] |
//! | GetHeightfieldData (slot 18) | 0x35078D60 | [`World::get_heightfield_data`] |
//! | SetHeightfieldMatMapping (slot 19) | 0x35078E20 | [`World::set_heightfield_mat_mapping`] |
//! | ray vs terrain (slot 44) | 0x351A9BA0 | [`World::terrain_ray`] |
//!
//! The heightfield geometry itself (`CHeightfield`, its `Intersect` and the ray march
//! 0x35158BA0) belongs to P1c and plugs in through [`Geometry`]: `SetHeightfieldData` takes a
//! factory that builds one heightfield geometry per caller (the binary calls the geometry
//! manager's `CreatePrimitive(2, hf)` five times). [`World::set_heightfield_data`] uses the
//! world's [`crate::world::geoman::create_primitive`], which has no heightfield yet (P1c), and
//! then creates no terrain.
//!
//! Gap: slot 4 also writes the new origin into the heightfield geometries of callers 0 and 1
//! (`CHeightfield +648`); that needs a P1c setter and is not done here (the world origin and
//! the parts' positions are updated). The heightfield ray march's "DrawRayOnGrid"-style limit
//! (0x35158BA0) is P1c's; the world setting is [`super::queries::QueryState::ray_dir_limit`].

use super::queries::rwi::RayHit;
use super::{EntIdx, World, MAX_CALLERS};
use crate::collision::contact::IntersectionParams;
use crate::entities::base::{Entity, EntityKind, Part};
use crate::geometry::primitives::{HeightfieldPrim, Primitive, RayPrim};
use crate::geometry::shapes::RayGeom;
use crate::geometry::{GeomWorldData, Geometry, PhysGeometry};
use crate::math::{Mat33, Quat, Vec3};
use std::sync::Arc;

impl World {
    /// World slot 17 `SetHeightfieldData(hf, matMapping, nMats)` (0x3508B860) with the
    /// geometry manager of the port (no heightfield before P1c: returns `NONE` and creates
    /// nothing when the factory fails).
    pub fn set_heightfield_data(&mut self, hf: Option<&HeightfieldPrim>, mat_mapping: Option<&[i32]>) -> EntIdx {
        self.set_heightfield_data_with(hf, mat_mapping, |p| {
            crate::world::geoman::create_primitive(&Primitive::Heightfield(p.clone()))
        })
    }

    /// World slot 17 with an explicit heightfield factory (`CreatePrimitive(2, hf)`, called
    /// once per caller slot).
    ///
    /// `hf = None` destroys the terrain entities. Otherwise, per caller `i = 0..4`: a new
    /// heightfield geometry; the existing terrain entity is reused (its old geometry and
    /// mapping are released) or a fresh `CPhysicalEntity` is made (not in the grid or any
    /// list, id −1) with one part: `id 0`, `scale 1`, `mass 0`, `flags 0xFDFF`,
    /// `minContactDist = hf.step.x`, idmatBreakable −1, no mapping; then the mapping (if any)
    /// is copied, the world placement (`+1616` basis, `+1652` origin) is set, and the part
    /// gets `pos = origin`, `q = conj(quat(basis))` (sub_35023EB0, negated vector part) and a
    /// zero bbox. Returns the terrain entity of caller 0.
    pub fn set_heightfield_data_with<F>(
        &mut self,
        hf: Option<&HeightfieldPrim>,
        mat_mapping: Option<&[i32]>,
        mut factory: F,
    ) -> EntIdx
    where
        F: FnMut(&HeightfieldPrim) -> Option<Arc<dyn Geometry>>,
    {
        let Some(hf) = hf else {
            for i in 0..MAX_CALLERS {
                let t = self.terrain[i];
                if !t.is_none() {
                    self.terrain[i] = EntIdx::NONE;
                    self.ents[t.0 as usize] = None;
                }
            }
            return EntIdx::NONE;
        };
        for i in 0..MAX_CALLERS {
            let Some(geom) = factory(hf) else {
                return self.terrain[0];
            };
            let t = self.terrain[i];
            self.terrain[i] = EntIdx::NONE;
            let pgeom = Arc::new(PhysGeometry {
                geom: geom.clone(),
                inertia: Vec3::ZERO,
                q: Quat::new(0.0, 0.0, 0.0, 0.0),
                origin: Vec3::ZERO,
                volume: 0.0,
                surface_idx: 0,
                mat_mapping: Vec::new(),
            });
            let e = if !t.is_none() && self.ent_alive(t) {
                let ent = self.ent_mut(t);
                let p = &mut ent.parts[0];
                p.pgeom = pgeom.clone();
                p.pgeom_proxy = pgeom;
                p.mat_mapping = None;
                t
            } else {
                let mut ent = Entity::new_base(1, EntityKind::Static);
                ent.parts_cap = 1;
                ent.id_bits |= 0x7F_FFFF;
                ent.parts.push(Part {
                    pos: Vec3::ZERO,
                    q: Quat::IDENTITY,
                    scale: 1.0,
                    bbox: [Vec3::ZERO; 2],
                    pgeom: pgeom.clone(),
                    pgeom_proxy: pgeom,
                    id: 0,
                    mass: 0.0,
                    flags: 0xFDFF,
                    flags_collider: 0,
                    max_dim: 0.0,
                    min_contact_dist: hf.step[0],
                    surface_bits: 0xFF_FC00,
                    nmats_bits: 0,
                    mat_mapping: None,
                    has_lattice: false,
                    unk128: 0,
                });
                self.store(ent)
            };
            {
                let p = &mut self.ent_mut(e).parts[0];
                if let Some(m) = mat_mapping {
                    p.nmats_bits = (p.nmats_bits & 0x8000_0000) | (m.len() as u32 & 0x7FFF_FFFF);
                    p.mat_mapping = Some(m.to_vec());
                } else {
                    p.mat_mapping = None;
                }
            }
            self.queries.hf_basis = hf.basis;
            self.terrain_origin = hf.origin;
            let q = Quat::from_mat33(&hf.basis);
            let p = &mut self.ent_mut(e).parts[0];
            p.pos = hf.origin;
            p.q = Quat::new(-q.x, -q.y, -q.z, q.w);
            p.bbox = [Vec3::ZERO; 2];
            self.terrain[i] = e;
        }
        self.terrain[0]
    }

    /// World slot 4 `SetTerrainOrigin(v)` (0x35078CA0): the world origin (`+1652`) and the
    /// part positions of the terrain entities of callers 0 and 1 (the original also writes
    /// their heightfield geometries' origin `+648`: P1c gap, see the module docs).
    pub fn set_terrain_origin(&mut self, v: Vec3) {
        self.terrain_origin = v;
        for i in 0..2 {
            let t = self.terrain[i];
            if !t.is_none() && self.ent_alive(t) {
                self.ent_mut(t).parts[0].pos = v;
            }
        }
    }

    /// World slot 18 `GetHeightfieldData` (0x35078D60): the heightfield primitive of caller 0's
    /// terrain geometry (geometry slot 17) with the world's basis and origin; `None` without
    /// terrain.
    #[must_use]
    pub fn get_heightfield_data(&self) -> Option<HeightfieldPrim> {
        let t = self.terrain[0];
        if t.is_none() {
            return None;
        }
        let geom = &self.ent(t).parts[0].pgeom.geom;
        match geom.primitive(0) {
            Some(Primitive::Heightfield(mut p)) => {
                p.basis = self.queries.hf_basis;
                p.origin = self.terrain_origin;
                Some(p)
            }
            _ => None,
        }
    }

    /// World slot 19 `SetHeightfieldMatMapping(map, n)` (0x35078E20): replace the material
    /// mapping of the five terrain parts (`None` clears it; the count is kept then, as in the
    /// original).
    pub fn set_heightfield_mat_mapping(&mut self, mapping: Option<&[i32]>) {
        for i in 0..MAX_CALLERS {
            let t = self.terrain[i];
            if t.is_none() || !self.ent_alive(t) {
                continue;
            }
            let p = &mut self.ent_mut(t).parts[0];
            p.mat_mapping = None;
            if let Some(m) = mapping {
                p.nmats_bits ^= (m.len() as u32 ^ p.nmats_bits) & 0x7FFF_FFFF;
                p.mat_mapping = Some(m.to_vec());
            }
        }
    }

    /// World slot 44 (0x351A9BA0): the ray against caller `caller`'s terrain heightfield
    /// (geometry `Intersect` with a `CRayGeom`, gwd = world origin / basisᵀ / scale 1,
    /// default intersection params). The first contact is a hit when its material id is
    /// `>= 0` or `flags & 0x20`: `dir` is cut to the hit point and `hit` gets `dist`, the
    /// terrain entity, `ipart = partid = 0`, the mapped surface, the raw id, `pt`, `n`,
    /// `bTerrain = 1`.
    pub fn terrain_ray(&mut self, org: Vec3, dir: &mut Vec3, hit: &mut RayHit, flags: u32, caller: usize) {
        let t = self.terrain[caller];
        if t.is_none() || !self.ent_alive(t) {
            return;
        }
        let ray = RayGeom::new(&RayPrim { origin: org, dir: *dir });
        let gwd = GeomWorldData {
            offset: self.terrain_origin,
            r: self.queries.hf_basis.transpose(),
            scale: 1.0,
            ..GeomWorldData::default()
        };
        let geom = self.ent(t).parts[0].pgeom.geom.clone();
        let mut ip = IntersectionParams::default();
        let mut contacts = Vec::new();
        let n = geom.intersect(&ray, &gwd, &GeomWorldData::default(), &mut ip, &mut contacts);
        if n == 0 || contacts.is_empty() {
            return;
        }
        let c = &contacts[0];
        if c.id[0] >= 0 || flags & 0x20 != 0 {
            *dir = Vec3::new(c.pt.x - org.x, c.pt.y - org.y, c.pt.z - org.z);
            hit.dist = c.t as f32;
            hit.collider = t;
            hit.ipart = 0;
            hit.partid = 0;
            hit.surface_idx = super::queries::map_surface(&self.ent(t).parts, c.id[0], 0) as i16;
            hit.idmat_org = c.id[0] as i16;
            hit.pt = c.pt;
            hit.n = c.n;
            hit.terrain = 1;
        }
    }
}

/// The terrain basis default (identity) for [`super::queries::QueryState::hf_basis`].
#[must_use]
pub fn default_basis() -> Mat33 {
    Mat33::IDENTITY
}
