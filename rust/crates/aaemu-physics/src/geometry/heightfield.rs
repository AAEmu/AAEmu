//! **P1c — heightfield**: `CHeightfield` : `CTriMesh` (x64 vtable 0x351F2148, object 0x320
//! bytes) and its embedded `CHeightfieldBV` (vtable 0x351F2068, at `+728`). `geometry.md` §1.3,
//! §3; `world.md` slot 17.
//!
//! A heightfield is a grid of `nx × ny` cells of size `step`, with vertex heights from the
//! `getHeight(ix, iy)` callback (× `heightscale`) and per-cell surface types from
//! `getSurfType(ix, iy)` (`(type & typemask) >> typepower`, `typehole` = no collision). Cells are
//! split on the `u + v = 1` diagonal. The geometry never collides as a primitive (the pair tables
//! have no heightfield rows): before every intersection test, slot 53 rebuilds the `CTriMesh`
//! base arrays as a small triangle mesh over the cells under the other geometry's bounding box
//! (the *region*, [`region`]), and the BV emits those triangles to the mesh's slot 56. Rays take
//! a dedicated grid march ([`ray`]).
//!
//! | module | content |
//! |---|---|
//! | (this file) | the object, construction (`CGeomManager::CreatePrimitive(2)` 0x3514B9F0 → ctor 0x35103E50 + `SetData` 0x35154F40), slots 1, 6, 8, 17, 21, 22, 24, 28, 29, 32, 46, 52 |
//! | [`bv`] | `CHeightfieldBV` slots 1–4, 9, 11, 23, 25 (+ [`BvTree`](crate::geometry::bvtree::BvTree)), the collider cell range sub_35154220 |
//! | [`region`] | the region mesh build of slot 53 (0x35155180) up to the `CTriMesh` slot-53 tail call |
//! | [`ray`] | the ray branch of slot 10 (0x35156E20): grid march sub_35158BA0, cell test sub_35158080, contact |
//!
//! ## Object layout (x64)
//!
//! `+0..+608` the `CTriMesh` base ([`TriMesh`]; the region mesh lives in its arrays: `+56`
//! indices, `+64` materials (= cell surface type, `-1` for holes), `+80` vertices, `+96` normals,
//! `+112` topology, `+120` nTris, `+124` nVertices, `+168` max valency = 6), `+608` the
//! heightfield primitive (120 bytes, [`HeightfieldPrim`]), `+728` the BV ([`HeightfieldBv`]),
//! `+784`/`+788` two floats `SetData` sets to −1000 / 1000 that nothing in the geometry code
//! reads, `+792` vertex capacity, `+796` triangle capacity of the base arrays.
//!
//! The CTriMesh base's own slots (7 `CalcPhysicalProperties`, 13/14 buoyancy and drag, 16/18/19
//! per-triangle accessors, 20, 25–27, 31, 33–45, 48–50, 54–61) run unchanged on the region mesh;
//! they belong to the trimesh modules. `+48` (the mesh's tree pointer) points at the embedded BV;
//! [`TriMesh::tree`] stays `None` here and the BV is [`Heightfield::bv`].
//!
//! ## What the lead wires
//!
//! * `impl Geometry for Heightfield`: slot 1 → [`GeomType::Heightfield`], 6 → [`Heightfield::bbox`],
//!   8 → [`Heightfield::point_inside_status`], 17 → [`Heightfield::get_primitive`], 21 → 0,
//!   22 → 0, 28 → [`Heightfield::primitive_count`], 29 → [`Heightfield::prim`], 32 →
//!   [`Heightfield::center`], 46 → [`Heightfield::bv`], 52 → 0, `+16` → `mesh.min_vtx_dist`,
//!   `+20` → `mesh.priority` (1).
//! * Slot 10: other type 3 (ray) → [`Heightfield::intersect_ray`]; every other type → the
//!   **mesh** slot 10 (`CTriMesh::Intersect` 0x351C9910, which itself falls back to the generic
//!   `CGeometry::Intersect`) with this heightfield as the mesh.
//! * Slot 53 = [`Heightfield::prepare_region`] followed, when it returns
//!   [`RegionOutcome::CallMeshSlot53`], by the `CTriMesh` slot 53 (0x351D3620) on the region mesh.
//! * BV slot 23 = [`HeightfieldBv::node_contents`] (a list of mesh slot-56 calls; the
//!   [`BvTree::get_node_contents`](crate::geometry::bvtree::BvTree::get_node_contents) form cannot
//!   express it), slot 25 = [`BvTree::mark_used_triangle`](crate::geometry::bvtree::BvTree::mark_used_triangle).
//!
//! ## Original bugs and quirks kept (documented at their sites)
//!
//! * `SetData` ignores the incoming basis/origin (identity / zero), **writes `stepr = 1/step`
//!   back into the caller's primitive**, and derives `typepower` from the lowest set bit of
//!   `typemask`; a zero `typemask` loops forever in the original (the port returns
//!   [`HeightfieldError::ZeroTypeMask`]). `heightmask` is copied but never used.
//! * `GetCenter` (slot 32) returns the grid centre in **cell units** (`nx/2, ny/2`), not metres.
//! * `GetPrimitive` (slot 17) copies the primitive from `step` on; basis, `bOriented` and origin
//!   of the output are left as the caller had them.
//! * `PointInsideStatus` accepts cells `0..=nx−2` only (the last row/column is outside) and
//!   interpolates the upper triangle with a formula that is not the plane of that triangle.
//! * BV slot 23 passes `2·min(ix+w, W)` as the triangle count of a row instead of
//!   `2·(min(ix+w, W) − ix)` (and loops `min(iy+h, H)` rows), so it over-reports cells when the
//!   collider leaf does not start at the region's corner (mesh slot 56 clamps to nTris).
//! * The collider cell range (sub_35154220) uses the box basis without checking `bOriented`
//!   when the collider box is already local.
//! * The ray branch's unprojection path only runs when the ray's priority is ≥ the
//!   heightfield's (never with the engine's priorities 0 and 1); the normal path writes a
//!   distance-along-ray contact with `iUnprojMode = −1`.

pub mod bv;
pub mod cubemap;
pub mod protocol;
pub mod ray;
pub mod region;

pub use bv::{HeightfieldBv, Slot56Call};
pub use ray::{march_grid, HfRayContext, RayGeomData, RayIntersectInput, RayMarchResult};
pub use region::{HfFrame, RegionOutcome};

use crate::geometry::primitives::{BoxPrim, HeightfieldPrim};
use crate::geometry::trimesh::TriMesh;
use crate::ids::GeomType;
use crate::math::{Mat33, Vec3};

/// Errors of [`Heightfield::set_data`].
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum HeightfieldError {
    /// `typemask == 0`: the original's typepower loop (`while !(v & 1) v >>= 1`) never ends.
    ZeroTypeMask,
}

/// `CHeightfield` (0x320 bytes).
#[derive(Debug)]
pub struct Heightfield {
    /// `+0..+608` the `CTriMesh` base. Its arrays hold the region mesh built by slot 53; their
    /// `Vec` lengths are the allocation capacities (`+792` / `+796`), the counts are
    /// `n_verts` / `n_tris`. Entries past the counts keep whatever an earlier region left there
    /// (fresh allocations are zeroed in the port, uninitialised heap in the original).
    pub mesh: TriMesh,
    /// `+728` the BV (its `+16` points at the primitive `+608`, which the port keeps inside the
    /// BV: [`HeightfieldBv::prim`]).
    pub bv: HeightfieldBv,
    /// `+784` / `+788`: −1000 / 1000 after `SetData`; not read by any geometry code
    /// (inferred: a z clamp for an external reader).
    pub z_limits: [f32; 2],
    /// `+792` vertex capacity of the base arrays (32 after `SetData`, then rounded up to 16).
    pub vtx_cap: i32,
    /// `+796` triangle capacity (64 after `SetData`).
    pub tri_cap: i32,
}

impl Clone for Heightfield {
    /// A full copy (the BV's used-triangle mask included).
    fn clone(&self) -> Self {
        Self {
            mesh: self.mesh.clone(),
            bv: self.bv.deep_clone(),
            z_limits: self.z_limits,
            vtx_cap: self.vtx_cap,
            tri_cap: self.tri_cap,
        }
    }
}

impl Default for Heightfield {
    fn default() -> Self {
        Self::new()
    }
}

impl Heightfield {
    /// The constructor 0x35103E50: the `CTriMesh` ctor (0x351BF470), then BV `+16` (primitive
    /// pointer), `+40/+44` (height range) and `+48` (used-triangle mask) zeroed. The BV region
    /// (`+24..+36`) and the CHeightfield tail are not initialised by the original (heap memory);
    /// the port zeroes them.
    #[must_use]
    pub fn new() -> Self {
        Self {
            mesh: TriMesh::default(),
            bv: HeightfieldBv::default(),
            z_limits: [0.0; 2],
            vtx_cap: 0,
            tri_cap: 0,
        }
    }

    /// `CGeomManager::CreatePrimitive(2, prim)` (0x3514B9F0): `new CHeightfield` + `SetData`.
    /// The geometry manager's memory counter grows by slot 52 (0 for a heightfield).
    ///
    /// # Errors
    /// [`HeightfieldError::ZeroTypeMask`] (the original hangs).
    pub fn create(src: &mut HeightfieldPrim) -> Result<Self, HeightfieldError> {
        let mut h = Self::new();
        h.set_data(src)?;
        Ok(h)
    }

    /// `CHeightfield::SetData` (0x35154F40, not a vtable slot: the factory calls it directly).
    ///
    /// * `src.stepr = (1/step.x, 1/step.y)` is written **into the caller's primitive** (kept:
    ///   world slot 17 passes the same primitive to its five `CreatePrimitive` calls and keeps
    ///   it), then the primitive is copied from `step` on.
    /// * Basis = identity, `bOriented` = 0, origin = 0: the incoming placement is **ignored**
    ///   (the world puts it into the terrain entity's part instead, see [`ray`] and `world.md`).
    /// * `typepower` = index of the lowest set bit of `typemask` (the incoming value is ignored).
    /// * `+784/+788` = −1000 / 1000; BV `+16` = the primitive, BV slot 4 `Build` (cost ignored),
    ///   `+48` = the BV.
    /// * Base arrays: vertex capacity 32, triangle capacity 64 (indices, normals, materials,
    ///   topology, used-triangle mask of `(64 >> 5) + 1` words); `nTris = nVertices = 0`.
    /// * `minVtxDist (+16) = (step.y + step.x) · 0.001`.
    ///
    /// The arrays of a previous `SetData` are leaked by the original; the port replaces them.
    ///
    /// # Errors
    /// [`HeightfieldError::ZeroTypeMask`]: the original loops forever after the copy; the port
    /// rejects the primitive before touching anything.
    pub fn set_data(&mut self, src: &mut HeightfieldPrim) -> Result<(), HeightfieldError> {
        if src.type_mask == 0 {
            return Err(HeightfieldError::ZeroTypeMask);
        }
        // divss in single precision; y first in the binary, the order does not matter.
        src.stepr = [1.0 / src.step[0], 1.0 / src.step[1]];
        let mut p = *src;
        p.basis = Mat33::IDENTITY;
        p.oriented = 0;
        p.origin = Vec3::ZERO;
        p.type_power = type_power(src.type_mask);
        self.z_limits = [-1000.0, 1000.0];
        self.bv.prim = p;
        self.bv.has_prim = true;
        let _cost = self.bv.build();
        self.vtx_cap = 32;
        self.tri_cap = 64;
        self.mesh.vertices = vec![Vec3::ZERO; 32];
        self.mesh.normals = vec![Vec3::ZERO; 64];
        self.mesh.indices = vec![0; 3 * 64];
        self.mesh.mats = Some(vec![0; 64]);
        self.mesh.topology = vec![[0; 3]; 64];
        self.bv.set_used_mask_words((64 >> 5) + 1);
        self.mesh.n_tris = 0;
        self.mesh.n_verts = 0;
        self.mesh.min_vtx_dist = (p.step[1] + p.step[0]) * 0.001;
        Ok(())
    }

    /// The primitive (`+608`; slot 29 `GetData` returns its address).
    #[must_use]
    pub fn prim(&self) -> &HeightfieldPrim {
        &self.bv.prim
    }

    /// Slot 1 `GetType` (0x350FE380).
    #[must_use]
    pub fn geom_type(&self) -> GeomType {
        GeomType::Heightfield
    }

    /// Slot 6 `GetBBox` (`CTriMesh` 0x35103340 → BV slot 2): the box of the current region (an
    /// all-zero region before the first slot 53 in the port; heap garbage in the original).
    #[must_use]
    pub fn bbox(&self) -> BoxPrim {
        self.bv.bbox_impl()
    }

    /// Slot 8 `PointInsideStatus` (0x35154D00): 1 when `pt` (heightfield frame) is under the
    /// surface, else 0.
    ///
    /// `ix = round(x·stepr.x − 0.5)` (magic-number rounding), likewise `iy`; outside unless
    /// `ix ≤ nx − 2` and `iy ≤ ny − 2` **as unsigned** (negative cells and the last row/column
    /// are outside — kept). A hole cell is outside. With `u = x·stepr.x − ix`, `v = y·stepr.y − iy`:
    /// if `u + v ≥ 1` the heights `a = H(ix, iy+1)`, `b = H(ix+1, iy+1)`, `c = H(ix+1, iy)` and
    /// `v ← 1 − v`, else `a = H(ix, iy)`, `b = H(ix+1, iy)`, `c = H(ix, iy+1)`; then
    /// `h = ((1−u)·a·hs + b·hs·u)·(1−v) + c·hs·v` and the result is the sign bit of `z − h`.
    /// (The upper-triangle formula is not the plane of that triangle; kept.)
    #[must_use]
    pub fn point_inside_status(&self, pt: Vec3) -> i32 {
        let p = &self.bv.prim;
        let fy = p.stepr[1] * pt.y;
        let fx = p.stepr[0] * pt.x;
        let ix = round_bits(fx - 0.5);
        let iy = round_bits(fy - 0.5);
        if ix > (p.size[0].wrapping_sub(2)) as u32 || iy > (p.size[1].wrapping_sub(2)) as u32 {
            return 0;
        }
        let (ix, iy) = (ix as i32, iy as i32);
        let surf = surf_type(p, ix, iy);
        if is_hole(p, surf) {
            return 0;
        }
        let u = fx - ix as f32;
        let mut v = fy - iy as f32;
        let hs = p.height_scale;
        let (a, b, c);
        if v + u >= 1.0 {
            a = height(p, ix, iy + 1) * hs;
            b = height(p, ix + 1, iy + 1) * hs;
            c = height(p, ix + 1, iy);
            v = 1.0 - v;
        } else {
            a = height(p, ix, iy) * hs;
            b = height(p, ix + 1, iy) * hs;
            c = height(p, ix, iy + 1);
        }
        let h = ((1.0 - u) * a + b * u) * (1.0 - v) + (c * hs) * v;
        ((pt.z - h).to_bits() >> 31) as i32
    }

    /// Slot 17 `GetPrimitive` (0x351036C0): copies the primitive **from `step` on** (bytes
    /// 52..120) into `out`; `out`'s basis, `bOriented` and origin are left untouched (kept).
    /// Returns 120.
    pub fn get_primitive(&self, _i_prim: i32, out: &mut HeightfieldPrim) -> i32 {
        let p = &self.bv.prim;
        out.step = p.step;
        out.stepr = p.stepr;
        out.size = p.size;
        out.stride = p.stride;
        out.height_scale = p.height_scale;
        out.type_mask = p.type_mask;
        out.height_mask = p.height_mask;
        out.type_hole = p.type_hole;
        out.type_power = p.type_power;
        out.get_height = p.get_height;
        out.get_surf_type = p.get_surf_type;
        120
    }

    /// Slot 21 `IsConvex` (shared `return 0`).
    #[must_use]
    pub fn is_convex(&self, _tolerance: f32) -> i32 {
        0
    }

    /// Slot 22 `PrepareForRayTest` (0x350FE390, `xor al, al`): no ray grid, returns 0.
    #[must_use]
    pub fn prepare_for_ray_test(&self, _raylen: f32) -> i32 {
        0
    }

    /// Slot 24 `GetMemoryStatistics` (0x35154CC0): the `CTriMesh` statistics (0x351BED10) plus
    /// 800 bytes for the object itself. Returns the latter.
    #[must_use]
    pub fn memory_size(&self) -> usize {
        800
    }

    /// Slot 28 `GetPrimitiveCount` (`CTriMesh` 0x350FE220): nTris of the current region mesh.
    #[must_use]
    pub fn primitive_count(&self) -> i32 {
        self.mesh.n_tris
    }

    /// Slot 32 `GetCenter` (0x35103420): `Basisᵀ·(nx/2, ny/2, 0) + origin` (`basis[1][k]·ny/2 +
    /// basis[0][k]·nx/2 + basis[2][k]·0`). **Kept bug:** the grid centre is in cell units — the
    /// step is not applied.
    #[must_use]
    pub fn center(&self) -> Vec3 {
        let p = &self.bv.prim;
        let b = &p.basis.m;
        let hx = p.size[0] as f32 * 0.5;
        let hy = p.size[1] as f32 * 0.5;
        Vec3::new(
            ((hy * b[1][0] + hx * b[0][0]) + b[2][0] * 0.0) + p.origin.x,
            ((hy * b[1][1] + hx * b[0][1]) + b[2][1] * 0.0) + p.origin.y,
            ((hy * b[1][2] + hx * b[0][2]) + b[2][2] * 0.0) + p.origin.z,
        )
    }

    /// Slot 52 (sizeof factor for the geometry manager's memory counter): 0.
    #[must_use]
    pub fn size_factor(&self) -> i32 {
        0
    }
}

/// `typepower`: `v = ((m ^ (m − 1)) + 1) >> 1` (lowest set bit), then count the shifts until
/// bit 0 is set.
#[must_use]
pub(crate) fn type_power(mask: u16) -> i32 {
    debug_assert_ne!(mask, 0);
    i32::from(mask).trailing_zeros() as i32
}

/// The engine's grid floor: `bits((x) + 12582912.0f) − 0x4B400000` as an unsigned word (`x` is
/// already `value − 0.5`).
#[inline]
pub(crate) fn round_bits(x: f32) -> u32 {
    (x + 12_582_912.0f32).to_bits().wrapping_sub(0x4B40_0000)
}

/// `getHeight(ix, iy)` (unscaled). A missing callback reads as 0 (the original calls null).
#[inline]
pub(crate) fn height(p: &HeightfieldPrim, ix: i32, iy: i32) -> f32 {
    p.get_height.map_or(0.0, |f| f(ix, iy))
}

/// `(getSurfType(ix, iy) & typemask) >> typepower` (u16 arithmetic, logical shift).
#[inline]
pub(crate) fn surf_type(p: &HeightfieldPrim, ix: i32, iy: i32) -> i32 {
    let t = p.get_surf_type.map_or(0, |f| f(ix, iy));
    i32::from(p.type_mask & t) >> (p.type_power & 31)
}

/// The engine's hole test on a decoded surface type `s`:
/// `(s | ((s − hole) ^ (s − hole − 1)) >> 31) < 0`, i.e. `s == hole` (`s ≥ 0`).
#[inline]
pub(crate) fn is_hole(p: &HeightfieldPrim, s: i32) -> bool {
    surf_or_hole(p, s) < 0
}

/// `s | (((s − hole) ^ (s − hole − 1)) >> 31)`: `s`, or −1 for the hole type.
#[inline]
pub(crate) fn surf_or_hole(p: &HeightfieldPrim, s: i32) -> i32 {
    let d = s.wrapping_sub(p.type_hole);
    s | ((d ^ d.wrapping_sub(1)) >> 31)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn flat(_x: i32, _y: i32) -> f32 {
        2.0
    }
    fn surf0(_x: i32, _y: i32) -> u16 {
        0
    }

    fn prim(n: [i32; 2]) -> HeightfieldPrim {
        HeightfieldPrim {
            step: [0.5, 2.0],
            size: n,
            height_scale: 1.0,
            type_mask: 0x0700,
            type_hole: 7,
            type_power: 99,
            basis: Mat33::from_array([0.0, 1.0, 0.0, -1.0, 0.0, 0.0, 0.0, 0.0, 1.0]),
            origin: Vec3::new(5.0, 6.0, 7.0),
            get_height: Some(flat),
            get_surf_type: Some(surf0),
            ..HeightfieldPrim::default()
        }
    }

    #[test]
    fn set_data_resets_placement_and_derives_typepower() {
        let mut p = prim([8, 4]);
        let h = Heightfield::create(&mut p).unwrap();
        assert_eq!(p.stepr, [2.0, 0.5], "stepr is written back into the source");
        assert_eq!(h.prim().basis, Mat33::IDENTITY);
        assert_eq!(h.prim().origin, Vec3::ZERO);
        assert_eq!(h.prim().type_power, 8);
        assert_eq!(h.mesh.priority, 1);
        assert_eq!(h.mesh.min_vtx_dist, (2.0f32 + 0.5) * 0.001);
        assert_eq!((h.vtx_cap, h.tri_cap), (32, 64));
        assert_eq!(h.center(), Vec3::new(4.0, 2.0, 0.0));
        let mut zero = prim([8, 4]);
        zero.type_mask = 0;
        assert_eq!(
            Heightfield::create(&mut zero).unwrap_err(),
            HeightfieldError::ZeroTypeMask
        );
    }

    #[test]
    fn point_inside_flat() {
        let mut p = prim([8, 4]);
        let h = Heightfield::create(&mut p).unwrap();
        assert_eq!(h.point_inside_status(Vec3::new(1.0, 1.0, 1.0)), 1);
        assert_eq!(h.point_inside_status(Vec3::new(1.0, 1.0, 3.0)), 0);
        // last column (ix = 7 = nx − 1) is outside
        assert_eq!(h.point_inside_status(Vec3::new(3.75, 1.0, 1.0)), 0);
        assert_eq!(h.point_inside_status(Vec3::new(-0.1, 1.0, 1.0)), 0);
    }
}
