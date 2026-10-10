//! **P3 — the geometry manager** embedded in the world (`CGeomManager`, `world+16`, 27 slots):
//! `CreatePrimitive` (slot 7, 0x3514B9F0) and `RegisterGeometry` (slot 9, 0x3514AE40).
//! Heightfields and meshes are created by P1c (`CreateMesh`, heightfield factory); this module
//! only wraps their geometry objects into `phys_geometry` records.

use super::World;
use crate::geometry::primitives::Primitive;
use crate::geometry::shapes::{BoxGeom, CapsuleGeom, CylinderGeom, RayGeom, SphereGeom};
use crate::geometry::{Geometry, PhysGeometry};
use crate::math::{Quat, Vec3};
use std::sync::Arc;

/// `CGeomManager::CreatePrimitive(type, prim*)` (0x3514B9F0) for the primitive types: the
/// class ctor + `SetData`. Heightfields go through P1c; triangles are not geometries.
#[must_use]
pub fn create_primitive(prim: &Primitive) -> Option<Arc<dyn Geometry>> {
    Some(match prim {
        Primitive::Box(b) => Arc::new(BoxGeom::new(b)),
        Primitive::Sphere(s) => Arc::new(SphereGeom::new(s)),
        Primitive::Cylinder(c) => Arc::new(CylinderGeom::new(c)),
        Primitive::Capsule(c) => Arc::new(CapsuleGeom::new(c)),
        Primitive::Ray(r) => Arc::new(RayGeom::new(r)),
        // P1c: `CHeightfield` (ctor 0x35103E50 + SetData 0x35154F40); SetData's write of
        // `stepr` into the caller's primitive goes to a copy here (world slot 17 recomputes
        // the same values for every caller).
        Primitive::Heightfield(h) => {
            let mut p = *h;
            Arc::new(
                crate::geometry::heightfield::protocol::SharedHeightfield::new(
                    crate::geometry::heightfield::Heightfield::create(&mut p).ok()?,
                ),
            )
        }
        Primitive::Triangle(_) => return None,
    })
}

/// `CGeomManager::RegisterGeometry(geom, defSurfaceIdx, matMapping, nMats)` (0x3514AE40): a
/// `phys_geometry` from the pool (zeroed), filled by the geometry's `CalcPhysicalProperties`
/// (slot 7; the ray writes nothing), the inertia floored at `0.01·max(I)` with the min/max
/// idioms, refcount 1.
#[must_use]
pub fn register_geometry(
    geom: Arc<dyn Geometry>,
    surface_idx: i32,
    mat_mapping: Option<&[i32]>,
) -> Arc<PhysGeometry> {
    let mut pg = PhysGeometry {
        geom: geom.clone(),
        inertia: Vec3::ZERO,
        q: Quat::new(0.0, 0.0, 0.0, 0.0),
        origin: Vec3::ZERO,
        volume: 0.0,
        surface_idx,
        mat_mapping: mat_mapping.map(<[i32]>::to_vec).unwrap_or_default(),
    };
    if let Some(p) = geom.calc_physical_properties() {
        pg.inertia = p.inertia;
        pg.q = p.q;
        pg.origin = p.origin;
        pg.volume = p.volume;
    }
    let (ix, iy, iz) = (pg.inertia.x, pg.inertia.y, pg.inertia.z);
    let v14 = ((ix - iy).abs() + (ix + iy)) * 0.5;
    let v15 = (((v14 - iz).abs() + (iz + v14)) * 0.5) * 0.01;
    pg.inertia = Vec3::new(
        ((ix - v15).abs() + (ix + v15)) * 0.5,
        ((iy - v15).abs() + (iy + v15)) * 0.5,
        ((iz - v15).abs() + (iz + v15)) * 0.5,
    );
    Arc::new(pg)
}

impl World {
    /// Convenience: `CreatePrimitive` + `RegisterGeometry(geom, 0, null, 0)`, what the oracle
    /// scenarios and most callers do.
    #[must_use]
    pub fn register_primitive(&self, prim: &Primitive) -> Option<Arc<PhysGeometry>> {
        create_primitive(prim).map(|g| register_geometry(g, 0, None))
    }
}
