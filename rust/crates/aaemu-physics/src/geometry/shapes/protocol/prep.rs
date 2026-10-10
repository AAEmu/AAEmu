//! Slot 48 `PreparePrimitive`, slot 53 `PrepareForIntersectionTest` and slot 56
//! `GetPrimitiveList` of the primitives.
//!
//! All three produce "the primitive under a transform": slot 48 under a `geom_world_data`,
//! slots 53 (ray only) and 56 under the gtest's `(R +48, offset +120, scale +144)`. The binary
//! has one inlined copy per site; they all evaluate the same single-precision expressions, which
//! are the `xf_*` helpers here.
//!
//! **Ring buffers (slot 48).** The binary writes the world-space primitive into the per-caller
//! arena and returns a pointer: box entries at arena `+0x346C` (64 B), sphere `+0x3448` (16 B),
//! cylinder/capsule `+0x3404` (32 B), each a ring of two entries whose index word follows the
//! ring (`+0x34EC`, `+0x3468`, `+0x3444`). Slot 48 takes entry `idx` and stores
//! `idx' = ((idx as u8) − 1) & 1`, so consecutive calls alternate and the previous result stays
//! valid for exactly one more call. The ray has a single buffer (`+0x34F0`), overwritten by
//! every call. Slot 53 hands out the *same* rings with `idx++` and no mask (the Intersect driver
//! resets the index words to 0 first; a third slot-53 call on one caller without a reset would
//! alias the index word itself). The port returns the primitive by value and keeps the slot-53
//! buffers inside the [`GeomUnderTest`], so neither the alternation nor the aliasing exists.

use super::gtest::GeomUnderTest;
use super::sgnnz_f;
use crate::geometry::primitives::{BoxPrim, CylinderPrim, Primitive, RayPrim, SpherePrim};
use crate::geometry::shapes::cylinder::CylinderData;
use crate::geometry::shapes::{BoxGeom, RayGeom, SphereGeom, DEC_MOD3, INC_MOD3};
use crate::geometry::GeomWorldData;
use crate::ids::BvTreeType;
use crate::math::{max_idiom, min_idiom, Mat33, Vec3};

use super::gtest::UsedNodeMap;

/// A box under `(r, off, s)`: centre `(R·c)·s + off`; basis `Basis·Rᵀ` when oriented
/// (`sub_35022380`, [`Mat33::mul_mat`]), else `Rᵀ`; `bOriented = 1`; size `size·s`.
#[must_use]
pub fn xf_box(b: &BoxPrim, r: &Mat33, off: Vec3, s: f32) -> BoxPrim {
    let center = r.mul_vec(b.center) * s + off;
    let basis = if b.oriented != 0 {
        b.basis.mul_mat(&r.transpose())
    } else {
        r.transpose()
    };
    BoxPrim {
        basis,
        oriented: 1,
        center,
        size: b.size * s,
    }
}

/// A sphere under `(r, off, s)`: `((R·c)·s + off, r·s)`.
#[must_use]
pub fn xf_sphere(p: &SpherePrim, r: &Mat33, off: Vec3, s: f32) -> SpherePrim {
    SpherePrim {
        center: r.mul_vec(p.center) * s + off,
        r: p.r * s,
    }
}

/// A cylinder/capsule under `(r, off, s)`: centre `(R·c)·s + off`, axis `R·axis` (unscaled),
/// `r·s`, `hh·s`.
#[must_use]
pub fn xf_cylinder(p: &CylinderPrim, r: &Mat33, off: Vec3, s: f32) -> CylinderPrim {
    CylinderPrim {
        center: r.mul_vec(p.center) * s + off,
        axis: r.mul_vec(p.axis),
        r: p.r * s,
        hh: p.hh * s,
    }
}

/// A ray under `(r, off, s)`: origin `(R·o)·s + off`, dir `(R·d)·s`.
#[must_use]
pub fn xf_ray(p: &RayPrim, r: &Mat33, off: Vec3, s: f32) -> RayPrim {
    RayPrim {
        origin: r.mul_vec(p.origin) * s + off,
        dir: r.mul_vec(p.dir) * s,
    }
}

// ------------------------------------------------------------------------------- slot 48

/// Box slot 48 (0x3513B580): the box in world space ([`xf_box`] under the gwd). Returns type 0
/// in the binary. See the module docs for the ring-buffer semantics.
#[must_use]
pub fn box_prepare_primitive(g: &BoxGeom, gwd: &GeomWorldData) -> Primitive {
    Primitive::Box(xf_box(&g.prim, &gwd.r, gwd.offset, gwd.scale))
}

/// Sphere slot 48 (0x351AF380): [`xf_sphere`] under the gwd; type 4.
#[must_use]
pub fn sphere_prepare_primitive(g: &SphereGeom, gwd: &GeomWorldData) -> Primitive {
    Primitive::Sphere(xf_sphere(&g.prim, &gwd.r, gwd.offset, gwd.scale))
}

/// Cylinder slot 48 (0x35142240): [`xf_cylinder`] under the gwd; type 5. The capsule's slot 48
/// (0x35038B60) calls it and returns type 6.
#[must_use]
pub fn cylinder_prepare_primitive(d: &CylinderData, gwd: &GeomWorldData) -> CylinderPrim {
    xf_cylinder(&d.prim, &gwd.r, gwd.offset, gwd.scale)
}

/// Ray slot 48 (0x3519A020): [`xf_ray`] under the gwd into the single per-caller buffer
/// (arena `+0x34F0`); type 3.
#[must_use]
pub fn ray_prepare_primitive(g: &RayGeom, gwd: &GeomWorldData) -> RayPrim {
    xf_ray(&g.prim, &gwd.r, gwd.offset, gwd.scale)
}

// ------------------------------------------------------------------------------- slot 53

/// `CSingleBoxTree` slot 6 (0x351ADF70): `+16 = &gtest+32`, `+32 = 0` (`+24`/`+36` untouched).
fn single_box_bv_prepare(gt: &mut GeomUnderTest) {
    gt.bv_type = Some(BvTreeType::SingleBox);
    gt.used.map = UsedNodeMap::Inline;
    gt.used.word = 0;
}

/// Keep exactly one entry in the `+288` buffer (the ring entry slot 53 hands out holds stale
/// data in the binary: the port keeps the previous entry when it has the right variant).
fn one_prim(gt: &mut GeomUnderTest, default: Primitive) {
    gt.prims.truncate(1);
    let ok = matches!(
        (gt.prims.first(), &default),
        (Some(Primitive::Box(_)), Primitive::Box(_))
            | (Some(Primitive::Sphere(_)), Primitive::Sphere(_))
            | (Some(Primitive::Cylinder(_)), Primitive::Cylinder(_))
            | (Some(Primitive::Capsule(_)), Primitive::Capsule(_))
            | (Some(Primitive::Ray(_)), Primitive::Ray(_))
    );
    if !ok {
        gt.prims.clear();
        gt.prims.push(default);
    }
    gt.unproj_prims = None;
}

fn ensure_ids(gt: &mut GeomUnderTest) {
    if gt.ids.is_empty() {
        gt.ids.push(0);
    }
}

/// Box slot 53 (0x3513A330): binds the embedded `CSingleBoxTree` (`+8`, its slot 6), takes the
/// next box ring entry for both `+288` and `+296` (`idx++`, see the module docs), `+304 = 1`,
/// `+280 = 0`, `+328 = 64`, ids `+320` = arena `+0x2C754`, face/edge candidate buffers
/// `+336`/`+344` = arena `+0x2C758`/`+0x2C794`, `+360 = 0`. Returns 1.
pub fn box_prepare_for_intersection_test(_g: &BoxGeom, gt: &mut GeomUnderTest) -> i32 {
    single_box_bv_prepare(gt);
    one_prim(gt, Primitive::Box(BoxPrim::default()));
    gt.n_prims = 1;
    gt.prim_type = 0;
    gt.prim_stride = 64;
    ensure_ids(gt);
    gt.min_pt_dist = 0.0;
    gt.cand_bufs = true;
    1
}

/// Sphere slot 53 (0x351AE910): as the box with the sphere ring, `+280 = 4`, `+328 = 16`, ids
/// = the static byte array 0x3537B220 (shared by all callers), no candidate buffers (`+336` =
/// `+344` = null), `+360 = 1e10`. Returns 1.
pub fn sphere_prepare_for_intersection_test(_g: &SphereGeom, gt: &mut GeomUnderTest) -> i32 {
    single_box_bv_prepare(gt);
    one_prim(gt, Primitive::Sphere(SpherePrim::default()));
    gt.n_prims = 1;
    gt.prim_type = 4;
    gt.prim_stride = 16;
    ensure_ids(gt);
    gt.cand_bufs = false;
    gt.min_pt_dist = f32::from_bits(0x5015_02F9); // 1e10
    1
}

/// `+360` of the cylinder and capsule: `(float)(((double)r·π)·2.0 / (double)nTess)` (all double,
/// constant π = 0x400921FB54442D18).
#[must_use]
pub fn cylinder_min_pt_dist(d: &CylinderData) -> f32 {
    let pi = f64::from_bits(0x4009_21FB_5444_2D18);
    ((f64::from(d.prim.r) * pi * 2.0) / f64::from(d.n_tess)) as f32
}

/// Cylinder slot 53 (0x35141CC0): as the box with the cylinder ring, `+280 = 5`, `+328 = 32`,
/// ids arena `+0x2C998`, candidate buffers arena `+0x2C99C`/`+0x2C9B0`, `+360` =
/// [`cylinder_min_pt_dist`]. Returns 1.
///
/// The capsule's slot 53 (0x3513F750) calls this and then sets `+280 = 6`, `+328 = 32`
/// (`capsule = true`; the port also tags the buffer entry as a capsule).
pub fn cylinder_prepare_for_intersection_test(
    d: &CylinderData,
    capsule: bool,
    gt: &mut GeomUnderTest,
) -> i32 {
    single_box_bv_prepare(gt);
    let default = if capsule {
        Primitive::Capsule(CylinderPrim::default())
    } else {
        Primitive::Cylinder(CylinderPrim::default())
    };
    one_prim(gt, default);
    gt.n_prims = 1;
    gt.prim_type = if capsule { 6 } else { 5 };
    gt.prim_stride = 32;
    ensure_ids(gt);
    gt.cand_bufs = true;
    gt.min_pt_dist = cylinder_min_pt_dist(d);
    1
}

/// Ray slot 53 (0x3519A5C0): sets the static byte 0x35378FF2 to -1 and uses it as the id
/// buffer; base `CBVTree` slot 6 (0x35024C60) on the embedded `CRayBV` (`+32 = -1`, `+16` =
/// `+24` = null, `+36 = 0`); `+288` = `+296` = arena `+0x34F0` (the slot-48 buffer), filled
/// with the ray under the gtest transform (`sub_3519A190`, [`xf_ray`]); `+280 = 3`,
/// `+328 = 24`, no candidate buffers, `+360 = 0`. `+304` (nPrims) is **not** written. Returns 1.
pub fn ray_prepare_for_intersection_test(g: &RayGeom, gt: &mut GeomUnderTest) -> i32 {
    gt.ids.clear();
    gt.ids.push(-1);
    gt.bv_type = Some(BvTreeType::Ray);
    gt.used.word = -1;
    gt.used.map = UsedNodeMap::Null;
    gt.used.list = None;
    gt.used.list_cap = 0;
    let ray = xf_ray(&g.prim, &gt.r, gt.offset, gt.scale);
    gt.prims.clear();
    gt.prims.push(Primitive::Ray(ray));
    gt.unproj_prims = None;
    gt.prim_type = 3;
    gt.prim_stride = 24;
    gt.cand_bufs = false;
    gt.min_pt_dist = 0.0;
    1
}

// ------------------------------------------------------------------------------- slot 56

/// The thin-box extension of box slot 56 (0x3513B810, after `sub_3513B310`).
///
/// With `smax = max(max(sx, sy), sz)` and `smin = min(min(sx, sy), sz)` (the min/max idioms),
/// if `!(pivot.x >= 1e10)` (a NaN pivot passes) and `!(8·smin >= smax)`: let `i` be the
/// smallest axis (index trick `c = isneg(sz − s[isneg(sy − sx)])<<1 | isneg(sy − sx)`,
/// `i = c & (((c ^ 2) | 4) >> 1)`), `j = inc(i)`, `k = dec(i)` and `l = Basis·(pivot − c)`.
/// If `s[j] > l[j]` and `s[k] > l[k]` (signed, not `|l|`: quirk kept), the box is thickened on
/// the side away from the pivot: `c −= axis_i·(sgnnz(l[i])·smax)`, `s[i] = smax + s[i]`.
fn box_pivot_extension(b: &mut BoxPrim, pivot: Vec3) {
    let s = b.size;
    let mx01 = max_idiom(s.x, s.y);
    let smax = max_idiom(mx01, s.z);
    let mn01 = min_idiom(s.x, s.y);
    let smin = min_idiom(mn01, s.z);
    if pivot.x >= 1e10 || smin * 8.0 >= smax {
        return;
    }
    let c0 = ((s.y - s.x).to_bits() >> 31) as usize;
    let c1 = ((s.z - s[c0]).to_bits() >> 31) as usize;
    let code = (c1 << 1) | c0;
    let i = code & (((code ^ 2) | 4) >> 1);
    let d = pivot - b.center;
    let l = b.basis.mul_vec(d);
    let (j, k) = (INC_MOD3[i], DEC_MOD3[i]);
    if s[j] > l[j] && s[k] > l[k] {
        let sg = sgnnz_f(l[i]) * smax;
        let row = b.basis.row(i);
        b.center = Vec3::new(
            b.center.x - row.x * sg,
            b.center.y - row.y * sg,
            b.center.z - row.z * sg,
        );
        b.size[i] += smax; // `smax + s[i]` (commutative)
    }
}

/// Box slot 56 (0x3513B810): the box under the gtest transform (`sub_3513B310`, [`xf_box`]),
/// thickened away from the other side's outside pivot (`gtest_op+268`, see
/// [`box_pivot_extension`]), written to the `+288` buffer; `gtest+160 = 0`, `ids[0] = -1`.
/// Returns 1. The binary receives the prim/id buffers as arguments; every BV passes the gtest's
/// own `+288`/`+320`, which is what the port writes.
pub fn box_get_primitive_list(g: &BoxGeom, gt: &mut GeomUnderTest, gt_op: &GeomUnderTest) -> i32 {
    let mut b = xf_box(&g.prim, &gt.r, gt.offset, gt.scale);
    box_pivot_extension(&mut b, gt_op.pt_outside_pivot);
    *gt.prim0_mut(Primitive::Box(b)) = Primitive::Box(b);
    gt.transform_prims = 0;
    gt.clear_id0();
    1
}

/// Sphere slot 56 (0x351AF480): [`xf_sphere`] under the gtest transform into `+288`;
/// `gtest+160 = 0`, `ids[0] = -1`. Returns 1.
pub fn sphere_get_primitive_list(g: &SphereGeom, gt: &mut GeomUnderTest) -> i32 {
    let s = Primitive::Sphere(xf_sphere(&g.prim, &gt.r, gt.offset, gt.scale));
    *gt.prim0_mut(s) = s;
    gt.transform_prims = 0;
    gt.clear_id0();
    1
}

/// Cylinder slot 56 (0x351423D0, also the capsule's): `sub_351420D0` ([`xf_cylinder`] under the
/// gtest transform) into `+288`; `gtest+160 = 0`, `ids[0] = -1`. Returns 1. (`sub_351420D0`
/// preserves `r8`, which still holds the gtest when the slot clears `+160`.)
pub fn cylinder_get_primitive_list(d: &CylinderData, capsule: bool, gt: &mut GeomUnderTest) -> i32 {
    let c = xf_cylinder(&d.prim, &gt.r, gt.offset, gt.scale);
    let p = if capsule {
        Primitive::Capsule(c)
    } else {
        Primitive::Cylinder(c)
    };
    *gt.prim0_mut(p) = p;
    gt.transform_prims = 0;
    gt.clear_id0();
    1
}

/// Ray slot 56 (0x3519A340): `sub_3519A190` ([`xf_ray`] under the gtest transform) into
/// `+288`; `gtest+160 = 0`, `ids[0] = -1`. Returns 1.
pub fn ray_get_primitive_list(g: &RayGeom, gt: &mut GeomUnderTest) -> i32 {
    let r = Primitive::Ray(xf_ray(&g.prim, &gt.r, gt.offset, gt.scale));
    *gt.prim0_mut(r) = r;
    gt.transform_prims = 0;
    gt.clear_id0();
    1
}
