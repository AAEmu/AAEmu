//! Primitive data structs (P0): what `IGeometry` slot 17 `GetPrimitive` copies out and slot 30
//! `SetData` / `CGeomManager::CreatePrimitive` accept. x64 layouts (`geometry.md` §1.3).
//!
//! These are frozen shared interfaces: P1a builds the geometry objects on top of them, P2a
//! intersects them, P1c produces [`Triangle`]s from meshes and heightfields.

use crate::ids::GeomType;
use crate::math::{Mat33, Vec3};

/// Box: `{Matrix33 Basis; int bOriented; Vec3 center; Vec3 size}` (64 bytes).
///
/// `basis` rows are the box axes in the geometry frame; `size` holds half-extents.
/// `SetData` sets `oriented = !IsIdentity(basis)`. `V = 8·sx·sy·sz`.
#[derive(Clone, Copy, Debug, PartialEq)]
#[repr(C)]
pub struct BoxPrim {
    pub basis: Mat33,
    pub oriented: i32,
    pub center: Vec3,
    pub size: Vec3,
}

impl Default for BoxPrim {
    fn default() -> Self {
        Self {
            basis: Mat33::IDENTITY,
            oriented: 0,
            center: Vec3::ZERO,
            size: Vec3::ZERO,
        }
    }
}

impl BoxPrim {
    /// Axis-aligned box of half-extents `size` at `center`.
    #[must_use]
    pub fn aligned(center: Vec3, size: Vec3) -> Self {
        Self {
            basis: Mat33::IDENTITY,
            oriented: 0,
            center,
            size,
        }
    }
}

/// Sphere: `{Vec3 center; float r}` (16 bytes). `V = 4/3·π·r³` with the constant
/// 4.188790204786391.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
#[repr(C)]
pub struct SpherePrim {
    pub center: Vec3,
    pub r: f32,
}

/// Cylinder: `{Vec3 center; Vec3 axis (unit); float r; float hh (half height)}` (32 bytes).
/// `V = 2π·r²·hh`. The geometry object also stores a tessellation count (18).
#[derive(Clone, Copy, Debug, PartialEq)]
#[repr(C)]
pub struct CylinderPrim {
    pub center: Vec3,
    pub axis: Vec3,
    pub r: f32,
    pub hh: f32,
}

impl Default for CylinderPrim {
    fn default() -> Self {
        Self {
            center: Vec3::ZERO,
            axis: Vec3::Z,
            r: 0.0,
            hh: 0.0,
        }
    }
}

/// Capsule: same layout as the cylinder; `hh` is the half-length of the cylindrical part.
/// `V = 2π·r²·hh + 4/3·π·r³`.
pub type CapsulePrim = CylinderPrim;

/// Ray (segment): `{Vec3 origin; Vec3 dir}` (24 bytes); `dir` is the full segment vector.
/// The ray geometry also caches `ndir` (normalised; zero length → +z).
#[derive(Clone, Copy, Debug, Default, PartialEq)]
#[repr(C)]
pub struct RayPrim {
    pub origin: Vec3,
    pub dir: Vec3,
}

/// Plane: `{Vec3 n; Vec3 origin}` (24 bytes), e.g. the water plane of slots 13/14.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
#[repr(C)]
pub struct Plane {
    pub n: Vec3,
    pub origin: Vec3,
}

/// Triangle: `{Vec3 pt[3]; Vec3 n}` (48 bytes) — mesh `GetPrimitive` and the triangles
/// heightfield / voxel BVs emit into the pair tests.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
#[repr(C)]
pub struct Triangle {
    pub pt: [Vec3; 3],
    pub n: Vec3,
}

/// Height callback `float(int ix, int iy)` (`primitives::getHeightCallback`).
pub type HeightFn = fn(i32, i32) -> f32;
/// Surface-type callback `u16(int ix, int iy)`.
pub type SurfTypeFn = fn(i32, i32) -> u16;

/// Heightfield primitive (120 bytes in the binary; geometry object +608).
///
/// Note: `CHeightfield`'s `SetData` (0x35154F40) **ignores** the incoming basis and origin and
/// resets them to identity / zero; it also recomputes `typepower` from `typemask` (which must be
/// non-zero, otherwise the original loops forever). The callbacks are plain function pointers.
#[derive(Clone, Copy, Debug)]
pub struct HeightfieldPrim {
    pub basis: Mat33,
    pub oriented: i32,
    pub origin: Vec3,
    /// Cell size `(x, y)`.
    pub step: [f32; 2],
    /// `1 / step`.
    pub stepr: [f32; 2],
    /// Grid size `(nx, ny)`.
    pub size: [i32; 2],
    /// *(inferred)* strides.
    pub stride: [i32; 2],
    pub height_scale: f32,
    pub type_mask: u16,
    pub height_mask: u16,
    /// Surface type that marks a hole.
    pub type_hole: i32,
    pub type_power: i32,
    pub get_height: Option<HeightFn>,
    pub get_surf_type: Option<SurfTypeFn>,
}

impl Default for HeightfieldPrim {
    fn default() -> Self {
        Self {
            basis: Mat33::IDENTITY,
            oriented: 0,
            origin: Vec3::ZERO,
            step: [1.0, 1.0],
            stepr: [1.0, 1.0],
            size: [0, 0],
            stride: [0, 0],
            height_scale: 1.0,
            type_mask: 0x8000,
            height_mask: 0xFFFF,
            type_hole: 1,
            type_power: 15,
            get_height: None,
            get_surf_type: None,
        }
    }
}

/// Triangle-mesh data header (`CTriMesh` +56.., `geometry.md` §1.3). P1c owns the mesh; this is
/// the shape of the data every mesh consumer (BV build, buoyancy, pair tests) sees.
#[derive(Clone, Debug, Default)]
pub struct MeshData {
    /// 3 vertex indices per triangle.
    pub indices: Vec<i32>,
    /// Per-triangle material (slot 16), empty if none.
    pub mats: Vec<i8>,
    /// Per-triangle foreign index (slot 18), empty if none.
    pub foreign_idx: Vec<i32>,
    pub vertices: Vec<Vec3>,
    /// Per-triangle normal (slot 19).
    pub normals: Vec<Vec3>,
    /// *(inferred)* per-vertex map.
    pub vtx_map: Vec<i32>,
    /// Neighbour triangle across each edge, < 0 = open edge.
    pub topology: Vec<[i32; 3]>,
    /// Mesh flags (+152): 0x20 OBB tree, 0x40 AABB tree, 0x40000 AABB in principal axes, …
    pub flags: u32,
}

/// Mesh build flags (`CTriMesh +152`).
pub mod mesh_flags {
    pub const SHARED_VERTICES: u32 = 0x1;
    pub const SHARED_MATS: u32 = 0x4;
    pub const SHARED_FOREIGN_IDX: u32 = 0x8;
    pub const OBB_TREE: u32 = 0x20;
    pub const AABB_TREE: u32 = 0x40;
    pub const AABB_PRINCIPAL_AXES: u32 = 0x4_0000;
    pub const FLAT_SPLIT: u32 = 0x4000;
}

/// A primitive of any type (the `int itype, primitive*` pairs of the C API).
#[derive(Clone, Copy, Debug)]
pub enum Primitive {
    Box(BoxPrim),
    Triangle(Triangle),
    Heightfield(HeightfieldPrim),
    Ray(RayPrim),
    Sphere(SpherePrim),
    Cylinder(CylinderPrim),
    Capsule(CapsulePrim),
}

impl Primitive {
    /// The geometry type id of the primitive (triangles report [`GeomType::TriMesh`]).
    #[must_use]
    pub fn geom_type(&self) -> GeomType {
        match self {
            Primitive::Box(_) => GeomType::Box,
            Primitive::Triangle(_) => GeomType::TriMesh,
            Primitive::Heightfield(_) => GeomType::Heightfield,
            Primitive::Ray(_) => GeomType::Ray,
            Primitive::Sphere(_) => GeomType::Sphere,
            Primitive::Cylinder(_) => GeomType::Cylinder,
            Primitive::Capsule(_) => GeomType::Capsule,
        }
    }

    /// `sizeof` of the primitive in the binary (slot 17's return value).
    #[must_use]
    pub fn byte_size(&self) -> usize {
        match self {
            Primitive::Box(_) => 64,
            Primitive::Triangle(_) => 48,
            Primitive::Heightfield(_) => 120,
            Primitive::Ray(_) => 24,
            Primitive::Sphere(_) => 16,
            Primitive::Cylinder(_) | Primitive::Capsule(_) => 32,
        }
    }
}

/// Volume constants as the binary uses them.
pub mod volume {
    /// `4/3·π` (double 4.188790204786391).
    pub const SPHERE_K: f64 = 4.188_790_204_786_391;
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn prim_sizes_match_the_binary() {
        assert_eq!(std::mem::size_of::<BoxPrim>(), 64);
        assert_eq!(std::mem::size_of::<SpherePrim>(), 16);
        assert_eq!(std::mem::size_of::<CylinderPrim>(), 32);
        assert_eq!(std::mem::size_of::<RayPrim>(), 24);
        assert_eq!(std::mem::size_of::<Plane>(), 24);
        assert_eq!(std::mem::size_of::<Triangle>(), 48);
        let p = Primitive::Sphere(SpherePrim {
            center: Vec3::ZERO,
            r: 1.0,
        });
        assert_eq!(p.geom_type(), GeomType::Sphere);
        assert_eq!(p.byte_size(), 16);
        assert_eq!(BoxPrim::aligned(Vec3::ZERO, Vec3::splat(0.5)).oriented, 0);
    }
}
