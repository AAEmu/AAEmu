//! Geometry: primitives, geometry objects, BV trees, meshes, heightfields.
//!
//! Ownership (see the crate README): [`primitives`] P0 (frozen); [`shapes`] P1a;
//! [`bvtree`] P1b; [`trimesh`], [`heightfield`] P1c. The shared interfaces every phase codes
//! against are defined here: [`Geometry`] (the `IGeometry`/`CGeometry` vtable, by slot),
//! [`PhysGeometry`] (`phys_geometry`, what parts reference), [`GeomWorldData`]
//! (`geom_world_data`, a geometry's placement and motion for queries) and [`PhysProperties`].

pub mod bvtree;
pub mod heightfield;
pub mod primitives;
pub mod shapes;
pub mod trimesh;

use crate::collision::contact::{GeomContact, IntersectionParams};
use crate::ids::GeomType;
use crate::math::{Mat33, Quat, Vec3};
use primitives::{BoxPrim, Plane, Primitive};
use std::sync::Arc;

/// `geom_world_data` (inferred names): placement of a geometry in the world plus the motion
/// used by unprojection and medium resistance.
/// `{Vec3 offset @0; Matrix33 R @12; float scale @48; Vec3 v @52; Vec3 w @64; Vec3 centerOfMass @76}`.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct GeomWorldData {
    pub offset: Vec3,
    /// Row-major rotation (geometry frame → world).
    pub r: Mat33,
    pub scale: f32,
    pub v: Vec3,
    pub w: Vec3,
    pub center_of_mass: Vec3,
}

impl Default for GeomWorldData {
    fn default() -> Self {
        Self {
            offset: Vec3::ZERO,
            r: Mat33::IDENTITY,
            scale: 1.0,
            v: Vec3::ZERO,
            w: Vec3::ZERO,
            center_of_mass: Vec3::ZERO,
        }
    }
}

/// Result of `CalcPhysicalProperties` (slot 7), written into `phys_geometry`:
/// volume, principal inertia **per unit density**, principal-axes orientation and origin.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct PhysProperties {
    pub volume: f32,
    pub inertia: Vec3,
    pub q: Quat,
    pub origin: Vec3,
}

/// `phys_geometry` (88 bytes in the binary): a registered geometry plus its mass properties
/// (`CGeomManager::RegisterGeometry`, 0x3514AE40). Parts reference it (`geom_part +0x38/+0x40`).
///
/// `RegisterGeometry` also inflates the inertia with
/// `max(Ix, Iy, Iz)·0.01` floors: `I_k = max_idiom(I_k, 0.01·max_idiom(max_idiom(Ix, Iy), Iz))`.
#[derive(Clone, Debug)]
pub struct PhysGeometry {
    pub geom: Arc<dyn Geometry>,
    /// `+8` principal inertia per unit density.
    pub inertia: Vec3,
    /// `+20`.
    pub q: Quat,
    /// `+36`.
    pub origin: Vec3,
    /// `+48` volume at unit scale.
    pub volume: f32,
    /// `+56` default surface index.
    pub surface_idx: i32,
    /// `+64` / `+72` material mapping.
    pub mat_mapping: Vec<i32>,
}

/// The geometry interface, by vtable slot (`geometry.md` §2.1). Methods are added by the
/// phase that implements the slot; the ones declared here are the stable core.
///
/// Implementations: `CBoxGeom`, `CSphereGeom`, `CCylinderGeom`, `CCapsuleGeom`, `CRayGeom`
/// (P1a, [`shapes`]), `CTriMesh` (P1c, [`trimesh`]), `CHeightfield` (P1c, [`heightfield`]).
/// `CVoxelGeom` is deferred (P6).
pub trait Geometry: Send + Sync + std::fmt::Debug {
    /// Slot 1 `GetType`.
    fn geom_type(&self) -> GeomType;

    /// Slot 6 `GetBBox`: the (possibly oriented) bounding box in the geometry frame.
    fn bbox(&self) -> BoxPrim;

    /// Slot 7 `CalcPhysicalProperties`. `None` for geometries that return 0 (ray).
    fn calc_physical_properties(&self) -> Option<PhysProperties>;

    /// Slot 8 `PointInsideStatus`: 1 inside, 0 outside, -1 not supported.
    fn point_inside_status(&self, pt: Vec3) -> i32;

    /// Slot 13 `CalculateBuoyancy`: submerged volume and its centre (world frame) below
    /// `plane` for the geometry placed by `gwd`. Copy the binary's quirks (`dynamics.md` §4.3).
    fn calculate_buoyancy(&self, plane: &Plane, gwd: &GeomWorldData) -> (f32, Vec3);

    /// Slot 14 `CalculateMediumResistance`: `(dP, dL)` accumulated as `-=` terms, linear in the
    /// velocity of the submerged, forward-facing surface (`dynamics.md` §4.4).
    fn calculate_medium_resistance(&self, plane: &Plane, gwd: &GeomWorldData) -> (Vec3, Vec3);

    /// Slot 17 `GetPrimitive(iPrim)`.
    fn primitive(&self, i_prim: i32) -> Option<Primitive>;

    /// Slot 28 `GetPrimitiveCount` (1 for primitives, nTris for meshes).
    fn primitive_count(&self) -> i32 {
        1
    }

    /// Slot 31 `GetVolume`.
    fn volume(&self) -> f32;

    /// Slot 32 `GetCenter`.
    fn center(&self) -> Vec3;

    /// Slot 47 `IsAPrimitive` (box, sphere, cylinder, capsule, ray).
    fn is_a_primitive(&self) -> bool;

    /// `CGeometry +16` (m_minVtxDist, inferred): `0.0001·(sx+sy+sz)` for a box, 1.0 for a ray.
    fn min_vtx_dist(&self) -> f32;

    /// `CGeometry +20` collision priority: ray 0, mesh/heightfield 1, primitives 3.
    fn priority(&self) -> i32;

    /// Slot 10 `Intersect(other, gwd1, gwd2, params, contacts)` → number of contacts (P2a/P2b).
    fn intersect(
        &self,
        other: &dyn Geometry,
        gwd1: &GeomWorldData,
        gwd2: &GeomWorldData,
        params: &mut IntersectionParams,
        contacts: &mut Vec<GeomContact>,
    ) -> usize;
}
