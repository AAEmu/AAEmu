//! The `geometry_under_test` record ("gtest", 392 bytes per side in the binary, inferred name)
//! and the small records the protocol slots write through it.
//!
//! The generic `CGeometry::Intersect` (0x3514FD70) keeps two of these on its stack (side A at
//! `+0`, side B at `+392`), fills the transform/motion part itself and lets each geometry fill
//! the rest in slot 53. Field offsets below are the x64 ones (`geometry.md` §2.3).
//!
//! **Arena replacement.** In the binary most pointer fields point into the per-caller scratch
//! arena (`0x35256F80 + iCaller·0x39FB8`, `geometry.md` §1.3) or into static globals; the port
//! owns those buffers in the record instead ([`GeomUnderTest::prims`], [`GeomUnderTest::ids`],
//! [`GeomUnderTest::face_cands`], …). Two consequences are documented where they matter:
//! buffers that the binary shares between records are separate here, and arena entries a call
//! does not write keep stale contents in the binary but are simply absent (or unchanged) here.

use crate::geometry::primitives::{BoxPrim, CylinderPrim, Primitive, RayPrim, SpherePrim};
use crate::ids::BvTreeType;
use crate::math::{Mat33, Vec3};

/// The used-node bookkeeping of a gtest (`+16`..`+39`), written by BV slot 6
/// (`PrepareForIntersectionTest` of the tree, called from geometry slot 53).
#[derive(Clone, Debug, PartialEq)]
pub struct UsedNodes {
    /// `+16` used-node bitmap pointer.
    pub map: UsedNodeMap,
    /// `+24` used-node index list (`None` = null). AABB/OBB trees point it into the arena.
    pub list: Option<Vec<i32>>,
    /// `+32`: the inline bitmap word of `CSingleBoxTree` (cleared to 0 by its slot 6
    /// 0x351ADF70) or the "no cached node" marker -1 of the other trees.
    pub word: i32,
    /// `+36` capacity of the index list (0 for trees without one).
    pub list_cap: i32,
}

/// Where the `+16` bitmap pointer points.
#[derive(Clone, Debug, PartialEq)]
pub enum UsedNodeMap {
    /// Null (base `CBVTree` slot 6, 0x35024C60: rays, heightfields).
    Null,
    /// `&gtest + 32` ([`UsedNodes::word`]): `CSingleBoxTree` slot 6 (0x351ADF70).
    Inline,
    /// A bitmap in the arena / heap (AABB/OBB trees, P1b).
    Words(Vec<u32>),
}

impl Default for UsedNodes {
    fn default() -> Self {
        Self {
            map: UsedNodeMap::Null,
            list: None,
            word: 0,
            list_cap: 0,
        }
    }
}

/// One entry of the face-candidate list (`gtest+336`, 20 bytes, count at `+352`) written by
/// slot 57: a face normal near the contact point.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct FaceCandidate {
    /// `+0` outward face normal (gtest frame).
    pub n: Vec3,
    /// `+12` primitive index (always 0 for the primitives).
    pub i_prim: i32,
    /// `+16` feature code of the face (`0x40 | …`).
    pub i_feature: i32,
}

/// One entry of the edge-candidate list (`gtest+344`, 44 bytes, count at `+356`) written by
/// slot 57: an edge direction and the normals of its two adjacent faces.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct EdgeCandidate {
    /// `+0` edge direction.
    pub dir: Vec3,
    /// `+12` / `+24` the two adjacent face normals (as the slot computes them; see each slot).
    pub n: [Vec3; 2],
    /// `+36` primitive index (always 0 for the primitives).
    pub i_prim: i32,
    /// `+40` feature code of the edge.
    pub i_feature: i32,
}

/// The 48-byte projection frame slots 58/59 receive as `float*` (inferred layout from the
/// slots): `+0` normal (all zero = "derive the frame from this feature"), `+12` origin,
/// `+24`/`+36` the in-plane axes the points are projected onto.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct ContactFrame {
    pub n: Vec3,
    pub origin: Vec3,
    pub axes: [Vec3; 2],
}

/// Output of slots 58/59: the feature's points projected into a [`ContactFrame`] plus their
/// feature ids. In the binary these are per-caller arena arrays (box: points `+0x2C6D4`, ids
/// `+0x2C714` / `+0x2C734`; cylinder: points `+0x2C818`, ids `+0x2C918` used for both id
/// outputs) returned through three out pointers.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct FeaturePolygon {
    /// `n + 1` points; the last repeats the first (closed polygon).
    pub pts: Vec<[f32; 2]>,
    /// First id output (vertex / feature ids, one per point).
    pub ids_a: Vec<i32>,
    /// Second id output (edge ids for the box face; the same array as `ids_a` for the cylinder).
    pub ids_b: Vec<i32>,
    /// True when the binary returns the same array for both id outputs (cylinder/capsule).
    pub ids_shared: bool,
}

/// `geometry_under_test` (392 bytes, inferred name). Field order and offsets as in the
/// binary; `+0` (the geometry pointer) is not stored: the caller keeps the `&dyn Geometry` next
/// to its record. `+368`/`+376` (contact array and the shared `int* pnContacts`) are passed to
/// slot 54 as one `&mut Vec<GeomContact>` whose length is `*pnContacts`.
#[derive(Clone, Debug)]
pub struct GeomUnderTest {
    /// `+8` the BV tree (slot 53 stores `&embedded tree`); the port records its type.
    pub bv_type: Option<BvTreeType>,
    /// `+16`..`+36` used-node bookkeeping (BV slot 6).
    pub used: UsedNodes,
    /// `+40` stop flag: set when the contact array is full (slot 54) and checked by the tree
    /// traversal.
    pub stop: i32,
    /// `+48` rotation into the frame the primitive lists are produced in (row-major).
    pub r: Mat33,
    /// `+84` rotation relative to the other side (`R_otherᵀ·R`, Intersect driver).
    pub r_rel: Mat33,
    /// `+120` offset (with `r` and `scale`: the transform used by slots 53/56/57/58/59).
    pub offset: Vec3,
    /// `+132` offset relative to the other side.
    pub offset_rel: Vec3,
    /// `+144` scale.
    pub scale: f32,
    /// `+148` `1/scale`.
    pub scale_r: f32,
    /// `+152` scale relative to the other side.
    pub scale_rel: f32,
    /// `+156` its reciprocal (inferred).
    pub scale_rel_r: f32,
    /// `+160` (inferred "primitives must be re-transformed"): slot 56 clears it; slot 57
    /// re-transforms the primitive into the `+296` buffer while it is set.
    pub transform_prims: i32,
    /// `+164` linear velocity.
    pub v: Vec3,
    /// `+176` angular velocity.
    pub w: Vec3,
    /// `+188` centre of mass.
    pub center_of_mass: Vec3,
    /// `+200` rotation centre (`intersection_params+4`).
    pub center_of_rotation: Vec3,
    /// `+216` is the `intersection_params*`; the only field the primitive slots read through it
    /// is byte `+98` (ray slot 54: stop after the first contact), mirrored here.
    pub params_stop_at_first: bool,
    /// `+224` preferred unprojection direction (`±params+56`, sign by side).
    pub unproj_dir: Vec3,
    /// `+260` (zeroed by the Intersect driver, meaning not decoded).
    pub field_260: i32,
    /// `+268` "outside pivot" of this side (`params+72` / `+84`); read by the *other* side's
    /// box slot 56.
    pub pt_outside_pivot: Vec3,
    /// `+280` primitive type of the lists (geometry type id; 1 = triangles for meshes).
    pub prim_type: i32,
    /// `+288` primitive buffer (slot 56 output; primitives: one entry).
    pub prims: Vec<Primitive>,
    /// `+296` unprojection primitive buffer (slot 57). `None` means "the same buffer as
    /// `+288`", which is what every primitive's slot 53 sets.
    pub unproj_prims: Option<Vec<Primitive>>,
    /// `+304` number of primitives in the lists (1 for box/sphere/cylinder/capsule; the ray's
    /// slot 53 leaves it alone).
    pub n_prims: i32,
    /// `+308` capacity of the `+296` buffer (meshes; untouched by the primitives).
    pub n_unproj_prims: i32,
    /// `+312` per-primitive index buffer (meshes; untouched by the primitives).
    pub aux_idx: Vec<i32>,
    /// `+320` per-primitive id bytes (slots 56/57 write `-1` to entry 0).
    pub ids: Vec<i8>,
    /// `+328` byte stride of the primitive records (box 64, sphere 16, cylinder/capsule 32,
    /// ray 24, triangle 52).
    pub prim_stride: i32,
    /// True when `+336`/`+344` are non-null (box, cylinder, capsule; null for sphere and ray).
    pub cand_bufs: bool,
    /// `+336` face candidates; the length is the count at `+352`.
    pub face_cands: Vec<FaceCandidate>,
    /// `+344` edge candidates; the length is the count at `+356`.
    pub edge_cands: Vec<EdgeCandidate>,
    /// `+360` minimum distance between contact-area points (box 0, sphere 1e10,
    /// cylinder/capsule `2πr/n`).
    pub min_pt_dist: f32,
    /// `+384` maximum number of contacts (`64 − contacts already in the arena`).
    pub n_max_contacts: i32,
    /// `+388` iCaller (arena slot 0–4).
    pub i_caller: i32,
}

impl GeomUnderTest {
    /// A neutral record for caller `i_caller`: identity transform, unit scale, everything else
    /// empty / zero. The Intersect driver (P2b) fills the transform and motion fields; slot 53
    /// fills the geometry part.
    #[must_use]
    pub fn new(i_caller: i32) -> Self {
        Self {
            bv_type: None,
            used: UsedNodes::default(),
            stop: 0,
            r: Mat33::IDENTITY,
            r_rel: Mat33::IDENTITY,
            offset: Vec3::ZERO,
            offset_rel: Vec3::ZERO,
            scale: 1.0,
            scale_r: 1.0,
            scale_rel: 1.0,
            scale_rel_r: 1.0,
            transform_prims: 0,
            v: Vec3::ZERO,
            w: Vec3::ZERO,
            center_of_mass: Vec3::ZERO,
            center_of_rotation: Vec3::ZERO,
            params_stop_at_first: false,
            unproj_dir: Vec3::ZERO,
            field_260: 0,
            pt_outside_pivot: Vec3::ZERO,
            prim_type: 0,
            prims: Vec::new(),
            unproj_prims: None,
            n_prims: 0,
            n_unproj_prims: 0,
            aux_idx: Vec::new(),
            ids: Vec::new(),
            prim_stride: 0,
            cand_bufs: false,
            face_cands: Vec::new(),
            edge_cands: Vec::new(),
            min_pt_dist: 0.0,
            n_max_contacts: 0,
            i_caller,
        }
    }

    /// `(R·v)·scale + offset` with the record's transform: `((r0·v.x + r1·v.y) + r2·v.z)·s + o`
    /// per row, single precision (the form of `sub_3513B310`, `sub_351420D0`, `sub_3519A190`
    /// and the sphere slots).
    #[inline]
    #[must_use]
    pub fn point(&self, v: Vec3) -> Vec3 {
        self.r.mul_vec(v) * self.scale + self.offset
    }

    /// Entry 0 of the `+288` buffer, created as `default` when the buffer is empty.
    pub fn prim0_mut(&mut self, default: Primitive) -> &mut Primitive {
        if self.prims.is_empty() {
            self.prims.push(default);
        }
        &mut self.prims[0]
    }

    /// Entry 0 of the `+296` buffer (the `+288` buffer when they alias).
    #[must_use]
    pub fn unproj_prim(&self) -> Option<&Primitive> {
        match &self.unproj_prims {
            None => self.prims.first(),
            Some(v) => v.first(),
        }
    }

    /// Mutable entry 0 of the `+296` buffer, created as `default` when empty.
    pub fn unproj_prim_mut(&mut self, default: Primitive) -> &mut Primitive {
        let buf = match &mut self.unproj_prims {
            None => &mut self.prims,
            Some(v) => v,
        };
        if buf.is_empty() {
            buf.push(default);
        }
        &mut buf[0]
    }

    /// `ids[0] = -1` (`**(char**)(gtest+320) = 0xFF`, slots 56 and 57).
    pub fn clear_id0(&mut self) {
        if self.ids.is_empty() {
            self.ids.push(-1);
        } else {
            self.ids[0] = -1;
        }
    }
}

/// The primitive in a [`Primitive`] slot as a box (the binary reinterprets the buffer; the port
/// requires the matching variant).
pub fn as_box(p: &Primitive) -> BoxPrim {
    match p {
        Primitive::Box(b) => *b,
        other => panic!("protocol: expected a box primitive, got {other:?}"),
    }
}

/// See [`as_box`].
pub fn as_sphere(p: &Primitive) -> SpherePrim {
    match p {
        Primitive::Sphere(s) => *s,
        other => panic!("protocol: expected a sphere primitive, got {other:?}"),
    }
}

/// See [`as_box`]; cylinder and capsule share the layout.
pub fn as_cylinder(p: &Primitive) -> CylinderPrim {
    match p {
        Primitive::Cylinder(c) | Primitive::Capsule(c) => *c,
        other => panic!("protocol: expected a cylinder/capsule primitive, got {other:?}"),
    }
}

/// See [`as_box`].
pub fn as_ray(p: &Primitive) -> RayPrim {
    match p {
        Primitive::Ray(r) => *r,
        other => panic!("protocol: expected a ray primitive, got {other:?}"),
    }
}
