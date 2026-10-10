//! Serialization of the primitive geometries: slots 25 `Save` / 27 `Load` (through a
//! `CMemStream`: `+8` buffer, `+20` position, `+24` capacity, `+32` count-only flag) and the
//! CGF physics blob (`MeshPhysicsData` chunk payload) as written by
//! `CGeomManager::SavePhysGeometry` (slot 15, 0x3514B1A0) and read by `LoadPhysGeometry`
//! (slot 16, 0x3514B340) → `LoadGeometry` (slot 14, 0x3514B070).
//!
//! Payloads (little-endian words, floats as raw bits):
//!
//! | class | slot 25 | bytes |
//! |---|---|---|
//! | box (0x3513B240) | 64-byte primitive (`bOriented` as stored) + tree | 64 + 68 |
//! | sphere (0x351AFBF0) | 16-byte primitive + tree | 16 + 68 |
//! | cylinder / capsule (0x35144CD0) | 32-byte primitive, int `+80` (18) + tree | 36 + 68 |
//! | ray | base nop | 0 |
//!
//! The tree part is `CSingleBoxTree` slot 21 (0x351AE320): its 64-byte box and `nPrims`.
//!
//! Blob (`CGeomManager` slot 15): `u32 1`, 64 bytes of `phys_geometry` (word `k` = byte
//! `4 + 4k` of the struct for k = 1..13: Ibody, q, origin, V, `+52`, surface index), `u32`
//! geometry type, the slot-25 payload. Words 0, 14 and 15 of the header are **uninitialised
//! stack** in the original writer (garbage in the shipped data); the port writes zeros.

use super::cylinder::CylinderData;
use super::{BoxGeom, CapsuleGeom, CylinderGeom, SingleBoxBv, SphereGeom};
use crate::geometry::bvtree::MemReader;
use crate::geometry::primitives::{BoxPrim, CylinderPrim, SpherePrim};
use crate::geometry::trimesh::stream::RenderArrays;
use crate::geometry::trimesh::TriMesh;
use crate::geometry::{Geometry, PhysGeometry};
use crate::ids::GeomType;
use crate::math::{max_idiom, Mat33, Quat, Vec3};
use std::sync::Arc;

fn put_u32(out: &mut Vec<u8>, v: u32) {
    out.extend_from_slice(&v.to_le_bytes());
}
fn put_f32(out: &mut Vec<u8>, v: f32) {
    put_u32(out, v.to_bits());
}
fn put_vec(out: &mut Vec<u8>, v: Vec3) {
    put_f32(out, v.x);
    put_f32(out, v.y);
    put_f32(out, v.z);
}
fn put_box(out: &mut Vec<u8>, b: &BoxPrim) {
    for f in b.basis.to_array() {
        put_f32(out, f);
    }
    put_u32(out, b.oriented as u32);
    put_vec(out, b.center);
    put_vec(out, b.size);
}

/// A read cursor over a serialized stream (the `CMemStream` read position `+20`).
#[derive(Clone, Debug)]
pub struct Reader<'a> {
    pub data: &'a [u8],
    pub pos: usize,
}

impl<'a> Reader<'a> {
    #[must_use]
    pub fn new(data: &'a [u8]) -> Self {
        Self { data, pos: 0 }
    }

    /// Next 32-bit word; `None` past the end (the original reads past the buffer).
    pub fn u32(&mut self) -> Option<u32> {
        let b = self.data.get(self.pos..self.pos + 4)?;
        self.pos += 4;
        Some(u32::from_le_bytes([b[0], b[1], b[2], b[3]]))
    }

    pub fn f32(&mut self) -> Option<f32> {
        self.u32().map(f32::from_bits)
    }

    pub fn vec(&mut self) -> Option<Vec3> {
        Some(Vec3::new(self.f32()?, self.f32()?, self.f32()?))
    }

    pub fn box_prim(&mut self) -> Option<BoxPrim> {
        let mut m = [0f32; 9];
        for v in &mut m {
            *v = self.f32()?;
        }
        Some(BoxPrim {
            basis: Mat33::from_array(m),
            oriented: self.u32()? as i32,
            center: self.vec()?,
            size: self.vec()?,
        })
    }
}

/// `CSingleBoxTree` slot 21 (0x351AE320).
fn save_bv(bv: &SingleBoxBv, out: &mut Vec<u8>) {
    put_box(out, &bv.box_);
    put_u32(out, bv.n_prims as u32);
}

/// `CSingleBoxTree` slot 22 (0x351AE380): box and `nPrims` as stored (no recomputation of
/// `bOriented`).
fn load_bv(bv: &mut SingleBoxBv, r: &mut Reader) -> Option<()> {
    bv.box_ = r.box_prim()?;
    bv.n_prims = r.u32()? as i32;
    Some(())
}

/// Box slot 25 (0x3513B240).
pub fn save_box(g: &BoxGeom, out: &mut Vec<u8>) {
    put_box(out, &g.prim);
    save_bv(&g.bv, out);
}

/// Sphere slot 25 (0x351AFBF0).
pub fn save_sphere(g: &SphereGeom, out: &mut Vec<u8>) {
    put_vec(out, g.prim.center);
    put_f32(out, g.prim.r);
    save_bv(&g.bv, out);
}

/// Cylinder / capsule slot 25 (0x35144CD0).
pub fn save_cylinder(d: &CylinderData, out: &mut Vec<u8>) {
    put_vec(out, d.prim.center);
    put_vec(out, d.prim.axis);
    put_f32(out, d.prim.r);
    put_f32(out, d.prim.hh);
    put_u32(out, d.n_tess as u32);
    save_bv(&d.bv, out);
}

impl BoxGeom {
    /// Slot 27 (0x3513B280) on `self`: the 64-byte primitive as stored (including
    /// `bOriented`), `m_minVtxDist = ((sy + sx) + sz)·1e-4`, then the tree.
    pub fn load(&mut self, r: &mut Reader) -> Option<()> {
        self.prim = r.box_prim()?;
        let s = self.prim.size;
        self.hdr.min_vtx_dist = ((s.y + s.x) + s.z) * 1e-4;
        load_bv(&mut self.bv, r)
    }
}

impl SphereGeom {
    /// Slot 27 (0x351AFC60): primitive, `m_minVtxDist = r·1e-4`, tree.
    pub fn load(&mut self, r: &mut Reader) -> Option<()> {
        self.prim = SpherePrim {
            center: r.vec()?,
            r: r.f32()?,
        };
        self.hdr.min_vtx_dist = self.prim.r * 1e-4;
        load_bv(&mut self.bv, r)
    }
}

impl CylinderData {
    /// Slot 27 (0x35144D40, both classes): primitive, `+80`, `m_minVtxDist = (hh + r)·1e-4`,
    /// tree.
    pub fn load(&mut self, r: &mut Reader) -> Option<()> {
        self.prim = CylinderPrim {
            center: r.vec()?,
            axis: r.vec()?,
            r: r.f32()?,
            hh: r.f32()?,
        };
        self.n_tess = r.u32()? as i32;
        self.hdr.min_vtx_dist = (self.prim.hh + self.prim.r) * 1e-4;
        load_bv(&mut self.bv, r)
    }
}

/// A primitive geometry rebuilt from a stream (`CGeomManager::LoadGeometry` subset).
#[derive(Clone, Debug)]
pub enum LoadedGeom {
    Box(BoxGeom),
    Sphere(SphereGeom),
    Cylinder(CylinderGeom),
    Capsule(CapsuleGeom),
}

impl LoadedGeom {
    /// As a shared geometry object.
    #[must_use]
    pub fn into_arc(self) -> Arc<dyn Geometry> {
        match self {
            LoadedGeom::Box(g) => Arc::new(g),
            LoadedGeom::Sphere(g) => Arc::new(g),
            LoadedGeom::Cylinder(g) => Arc::new(g),
            LoadedGeom::Capsule(g) => Arc::new(g),
        }
    }

    /// The geometry.
    #[must_use]
    pub fn as_geom(&self) -> &dyn Geometry {
        match self {
            LoadedGeom::Box(g) => g,
            LoadedGeom::Sphere(g) => g,
            LoadedGeom::Cylinder(g) => g,
            LoadedGeom::Capsule(g) => g,
        }
    }
}

/// Any geometry `LoadGeometry` constructs: a primitive, or a `CTriMesh` (P1c,
/// [`TriMesh::load`]).
#[derive(Clone, Debug)]
pub enum AnyGeom {
    Primitive(LoadedGeom),
    TriMesh(Box<TriMesh>),
}

/// Why a blob was not loaded.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum BlobError {
    /// The first word is not 1 (`LoadPhysGeometry` returns null).
    Version(u32),
    /// Geometry type 1 (`CTriMesh`, package P1c) or 7.
    NotAPrimitive(i32),
    /// An id `LoadGeometry` does not construct (2 heightfield, 3 ray, others): null.
    UnknownType(i32),
    /// The data ended early (the original reads past the buffer).
    Truncated,
    /// `CTriMesh::Load` failed (truncated stream, unknown tree type, missing render arrays).
    Mesh(String),
}

/// `CGeomManager::LoadGeometry` (slot 14, 0x3514B070) for the primitive ids (see
/// [`load_geometry_any`]); a mesh id is [`BlobError::NotAPrimitive`].
pub fn load_geometry(r: &mut Reader) -> Result<LoadedGeom, BlobError> {
    match load_geometry_any(r, &RenderArrays::default())? {
        AnyGeom::Primitive(g) => Ok(g),
        AnyGeom::TriMesh(_) => Err(BlobError::NotAPrimitive(1)),
    }
}

/// `CGeomManager::LoadGeometry` (slot 14, 0x3514B070): reads the type word, constructs the
/// class (ctor only, no `SetData`) and calls its slot 26 with the render arrays `ra`
/// (primitives: → slot 27; a `CTriMesh`: [`TriMesh::load`]).
pub fn load_geometry_any(r: &mut Reader, ra: &RenderArrays<'_>) -> Result<AnyGeom, BlobError> {
    let t = r.u32().ok_or(BlobError::Truncated)? as i32;
    if t == 1 {
        let mut mr = MemReader::new(r.data.get(r.pos..).ok_or(BlobError::Truncated)?);
        let m = TriMesh::load(&mut mr, ra).map_err(|e| BlobError::Mesh(e.to_string()))?;
        r.pos += mr.pos;
        return Ok(AnyGeom::TriMesh(Box::new(m)));
    }
    let g = match t {
        0 => {
            let mut g = BoxGeom::empty();
            g.load(r).map(|()| LoadedGeom::Box(g))
        }
        4 => {
            let mut g = SphereGeom::empty();
            g.load(r).map(|()| LoadedGeom::Sphere(g))
        }
        5 => {
            let mut g = CylinderGeom::empty();
            g.0.load(r).map(|()| LoadedGeom::Cylinder(g))
        }
        6 => {
            let mut g = CapsuleGeom::empty();
            g.0.load(r).map(|()| LoadedGeom::Capsule(g))
        }
        _ => return Err(BlobError::UnknownType(t)),
    };
    g.map(AnyGeom::Primitive).ok_or(BlobError::Truncated)
}

/// `CGeomManager::SaveGeometry` (slot 13, 0x3514AFF0): type word + slot 25.
pub fn save_geometry(g: &dyn Geometry, out: &mut Vec<u8>) {
    put_u32(out, g.geom_type().id() as u32);
    g.save(out);
}

/// The `phys_geometry` fields a blob carries.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct PhysGeomHeader {
    /// `+8` Ibody per unit density.
    pub inertia: Vec3,
    /// `+20`.
    pub q: Quat,
    /// `+36`.
    pub origin: Vec3,
    /// `+48`.
    pub volume: f32,
    /// `+52` (LoadPhysGeometry always sets 1).
    pub unk52: i32,
    /// `+56` default surface index.
    pub surface_idx: i32,
}

impl PhysGeomHeader {
    /// The 16 header words `SavePhysGeometry` writes; words 0, 14 and 15 (uninitialised stack
    /// in the original) as 0.
    #[must_use]
    pub fn to_words(&self) -> [u32; 16] {
        let mut w = [0u32; 16];
        w[1] = self.inertia.x.to_bits();
        w[2] = self.inertia.y.to_bits();
        w[3] = self.inertia.z.to_bits();
        for (k, f) in self.q.to_array().into_iter().enumerate() {
            w[4 + k] = f.to_bits();
        }
        w[8] = self.origin.x.to_bits();
        w[9] = self.origin.y.to_bits();
        w[10] = self.origin.z.to_bits();
        w[11] = self.volume.to_bits();
        w[12] = self.unk52 as u32;
        w[13] = self.surface_idx as u32;
        w
    }
}

/// `CGeomManager::SavePhysGeometry` (slot 15, 0x3514B1A0). Header words 0, 14, 15 are
/// uninitialised in the original; written as 0 here.
pub fn save_phys_geometry(h: &PhysGeomHeader, g: &dyn Geometry, out: &mut Vec<u8>) {
    put_u32(out, 1);
    for w in h.to_words() {
        put_u32(out, w);
    }
    save_geometry(g, out);
}

/// `SavePhysGeometry` for a `CTriMesh` (P1c): version 1, the 16 header words as given (pass
/// [`LoadedPhysGeomAny::raw_header`] to reproduce a loaded blob, or
/// [`PhysGeomHeader::to_words`]), type 1 and [`TriMesh::save`].
pub fn save_phys_geometry_mesh(header: &[u32; 16], m: &TriMesh, out: &mut Vec<u8>) {
    put_u32(out, 1);
    for &w in header {
        put_u32(out, w);
    }
    put_u32(out, 1);
    out.extend_from_slice(&m.save_to_vec());
}

/// Result of [`load_phys_geometry`]: the geometry and its `phys_geometry` record.
#[derive(Clone, Debug)]
pub struct LoadedPhysGeom {
    pub geom: LoadedGeom,
    pub header: PhysGeomHeader,
    /// Bytes consumed.
    pub len: usize,
}

impl LoadedPhysGeom {
    /// As the shared [`PhysGeometry`] record (no material mapping: `+64 = null`, `+72 = 0`).
    #[must_use]
    pub fn to_phys_geometry(&self) -> PhysGeometry {
        PhysGeometry {
            geom: self.geom.clone().into_arc(),
            inertia: self.header.inertia,
            q: self.header.q,
            origin: self.header.origin,
            volume: self.header.volume,
            surface_idx: self.header.surface_idx,
            mat_mapping: Vec::new(),
        }
    }
}

/// `CGeomManager::LoadPhysGeometry` (slot 16, 0x3514B340) for primitive blobs.
///
/// 1. Version word must be 1.
/// 2. The 64-byte header fills a pooled `phys_geometry`; Ibody gets the
///    `RegisterGeometry` floor `I_k = max(I_k, 0.01·max(max(Ix, Iy), Iz))` (max idioms);
///    `+52 = 1`, `+64 = null`, `+72 = 0`.
/// 3. `LoadGeometry` reads the type and the payload (the mesh id 1 and the vertex/index
///    arguments are P1c's).
/// 4. **Quirk kept:** for a sphere the header is then overwritten by the sphere's
///    `CalcPhysicalProperties` (slot 7) — unfloored volume/inertia, identity q, its centre.
///
/// Not modelled: the original first returns null when a world flag (`GetPhysVars()+496`,
/// reached through the geometry manager's world) is set.
pub fn load_phys_geometry(data: &[u8]) -> Result<LoadedPhysGeom, BlobError> {
    let any = load_phys_geometry_any(data, &RenderArrays::default())?;
    match any.geom {
        AnyGeom::Primitive(geom) => Ok(LoadedPhysGeom {
            geom,
            header: any.header,
            len: any.len,
        }),
        AnyGeom::TriMesh(_) => Err(BlobError::NotAPrimitive(1)),
    }
}

/// [`load_phys_geometry`] for any geometry, with the render-mesh arrays a `CTriMesh` blob may
/// reference (`LoadPhysGeometry(stream, pVtx, pIdx, pMats)`).
#[derive(Clone, Debug)]
pub struct LoadedPhysGeomAny {
    pub geom: AnyGeom,
    pub header: PhysGeomHeader,
    /// The 16 header words as stored (for byte-exact re-saving).
    pub raw_header: [u32; 16],
    /// Bytes consumed.
    pub len: usize,
}

/// `CGeomManager::LoadPhysGeometry` (slot 16, 0x3514B340) for any geometry type (see
/// [`load_phys_geometry`] for the steps).
pub fn load_phys_geometry_any(
    data: &[u8],
    ra: &RenderArrays<'_>,
) -> Result<LoadedPhysGeomAny, BlobError> {
    let mut r = Reader::new(data);
    let ver = r.u32().ok_or(BlobError::Truncated)?;
    if ver != 1 {
        return Err(BlobError::Version(ver));
    }
    let mut w = [0u32; 16];
    for v in &mut w {
        *v = r.u32().ok_or(BlobError::Truncated)?;
    }
    let f = |i: usize| f32::from_bits(w[i]);
    let (ix, iy, iz) = (f(1), f(2), f(3));
    let m = max_idiom(max_idiom(ix, iy), iz) * 0.01;
    let mut header = PhysGeomHeader {
        inertia: Vec3::new(max_idiom(ix, m), max_idiom(iy, m), max_idiom(iz, m)),
        q: Quat::new(f(4), f(5), f(6), f(7)),
        origin: Vec3::new(f(8), f(9), f(10)),
        volume: f(11),
        unk52: 1,
        surface_idx: w[13] as i32,
    };
    let geom = load_geometry_any(&mut r, ra)?;
    if let AnyGeom::Primitive(g) = &geom {
        if g.as_geom().geom_type() == GeomType::Sphere {
            if let Some(p) = g.as_geom().calc_physical_properties() {
                header.inertia = p.inertia;
                header.q = p.q;
                header.origin = p.origin;
                header.volume = p.volume;
            }
        }
    }
    Ok(LoadedPhysGeomAny {
        geom,
        header,
        raw_header: w,
        len: r.pos,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn round_trip_all_primitives() {
        let b = BoxGeom::new(&BoxPrim::aligned(
            Vec3::new(1.0, 2.0, 3.0),
            Vec3::splat(0.5),
        ));
        let mut out = Vec::new();
        b.save(&mut out);
        assert_eq!(out.len(), 132);
        let mut g = BoxGeom::empty();
        g.load(&mut Reader::new(&out)).unwrap();
        assert_eq!(g, b);

        let s = SphereGeom::new(&SpherePrim {
            center: Vec3::Z,
            r: 2.0,
        });
        let mut out = Vec::new();
        save_geometry(&s, &mut out);
        assert_eq!(out.len(), 4 + 84);
        match load_geometry(&mut Reader::new(&out)).unwrap() {
            LoadedGeom::Sphere(g) => assert_eq!(g, s),
            other => panic!("{other:?}"),
        }
        let c = CapsuleGeom::new(&CylinderPrim {
            center: Vec3::ZERO,
            axis: Vec3::X,
            r: 0.5,
            hh: 1.0,
        });
        let mut out = Vec::new();
        save_geometry(&c, &mut out);
        assert_eq!(out.len(), 4 + 104);
        match load_geometry(&mut Reader::new(&out)).unwrap() {
            LoadedGeom::Capsule(g) => assert_eq!(g, c),
            other => panic!("{other:?}"),
        }
        assert_eq!(
            load_geometry(&mut Reader::new(&3u32.to_le_bytes())).unwrap_err(),
            BlobError::UnknownType(3)
        );
    }
}
