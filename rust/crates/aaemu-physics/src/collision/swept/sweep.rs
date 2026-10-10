//! Swept primitive tests (continuous collision) as the shared primitive `Intersect`
//! (`CCylinderGeom__vf10`, x64 0x35151F20, x86 0x3515A6D0 — the slot-10 implementation of the
//! box, sphere, cylinder and capsule geometries) runs them when `intersection_params.bSweepTest`
//! (+96) is set, plus the per-primitive `PreparePrimitive` (geometry slot 48) they rely on.
//!
//! This is the path behind `PrimitiveWorldIntersection` sweeps (character capsule / box sweeps for
//! movement, blink, leap, dash) and the rigid-body CCD pass (`CheckForNewContacts` with a sweep;
//! `geometry.md` §5.3–§5.4).
//!
//! # The sweep (from the disassembly of 0x35151F20)
//!
//! 1. `|v| = √((vx² + vy²) + vz²)` of A's `gwd.v`, `L = |v|·time_interval`, `v̂ = v·(1/|v|)`
//!    (no zero check: a zero `v` gives `L = 0` and a NaN `v̂`; the world-level callers only set
//!    the sweep flag for a non-zero sweep).
//! 2. OVERLAP of A's swept bounding volume against B's (BV slots 8/9 and the OVERLAP table;
//!    owned by the BV / pair phases — a conservative pre-check that the caller performs).
//! 3. `gwdA.offset += v̂·L`, prepare A (slot 48) at that end pose, prepare B, then
//!    `gwdA.offset −= v̂·L`. **Bug kept:** the restore is a float subtraction, so the caller's
//!    `gwdA.offset` can come back changed by one rounding step (the engine writes through its
//!    `const geom_world_data*`). [`sweep_primitives`] takes `gwd_a` mutably for this reason.
//! 4. B with collision priority 0 (a ray) leaves the sweep for the INTERSECT path — not handled
//!    here (callers never sweep against rays).
//! 5. `unproj_params`: mode 0, `dir = −v̂`, `vel = tmax = L`, `tmin = 0`,
//!    `minPtDist = min_idiom(A.minVtxDist, B.minVtxDist)`, `maxcos = 1 − gap²·0.5`, `flag = 1`;
//!    result `t = 0`. `UNPROJECT[0][tA][tB]` from the end pose back along `−v̂`; success needs
//!    `ret != 0` and the sign bit of `(float)(t − L)` (i.e. `t < L`).
//! 6. If `n·dir > 0` the end pose has tunnelled through B: take the axis of B's bounding box
//!    (slot 6, in B's frame: `basis · Rᵀ_B · v̂`) with the largest
//!    `|v̂_i|·size_j·size_k`, back up by `t' = t + (size_i/|v̂_i|)·0.1`, re-prepare A at
//!    `offset + v̂·(L − t')` (B is not re-prepared), unproject again with `tmax = L − t'`, and
//!    require `ret`, `t < L − t'` and now `n·dir < 0` (sign bits); `t += t'`.
//! 7. Contact: `t = L − t` (distance travelled before the hit), `pt` from the unprojection,
//!    `n` normalised (`(0,0,1)` if `|n|² ≤ 1e-20`), `dir = −v̂`, mode 0, `vel = L`, ids −1,
//!    `iPrim` 0, features from the unprojection, nodes −1. A contact area with points (only box
//!    pairs make them) becomes the border when A or B is a cylinder (or the border was empty —
//!    always the case here), with the centre = mean of the area points; otherwise border =
//!    `[pt]`, centre = `pt`.

use super::common::mat_mul;
use super::UnprojTables;
use crate::collision::contact::{
    ContactArea, GeomContact, IntersectionParams, UnprojParams, UnprojResult,
};
use crate::geometry::primitives::{BoxPrim, CylinderPrim, Primitive, RayPrim, SpherePrim};
use crate::geometry::GeomWorldData;
use crate::math::{Mat33, Vec3};

/// `gwd.R · c · scale + offset` with the binary's order:
/// `((R[i][0]·c.x + R[i][1]·c.y) + R[i][2]·c.z)·scale + offset[i]`.
fn place_point(c: Vec3, g: &GeomWorldData) -> Vec3 {
    let r = &g.r.m;
    let s = g.scale;
    Vec3::new(
        ((r[0][0] * c.x + r[0][1] * c.y) + r[0][2] * c.z) * s + g.offset.x,
        ((r[1][0] * c.x + r[1][1] * c.y) + r[1][2] * c.z) * s + g.offset.y,
        ((r[2][0] * c.x + r[2][1] * c.y) + r[2][2] * c.z) * s + g.offset.z,
    )
}

/// `gwd.R · a` (no scale), `(R[i][0]·a.x + R[i][1]·a.y) + R[i][2]·a.z`.
fn rotate(a: Vec3, g: &GeomWorldData) -> Vec3 {
    let r = &g.r.m;
    Vec3::new(
        (r[0][0] * a.x + r[0][1] * a.y) + r[0][2] * a.z,
        (r[1][0] * a.x + r[1][1] * a.y) + r[1][2] * a.z,
        (r[2][0] * a.x + r[2][1] * a.y) + r[2][2] * a.z,
    )
}

/// `CBoxGeom` slot 48 (0x3513B580): the world-space box. Centre placed by `gwd`; basis
/// `box.basis · Rᵀ` (sub_35022380) if the box is oriented, else `Rᵀ` copied; `oriented = 1`;
/// `size·scale`.
#[must_use]
pub fn prepare_box(b: &BoxPrim, g: &GeomWorldData) -> BoxPrim {
    let center = place_point(b.center, g);
    let rt = g.r.transpose();
    let basis = if b.oriented != 0 {
        Mat33::from_array(mat_mul(&b.basis.to_array(), &rt.to_array()))
    } else {
        rt
    };
    let s = g.scale;
    BoxPrim {
        basis,
        oriented: 1,
        center,
        size: Vec3::new(b.size.x * s, b.size.y * s, b.size.z * s),
    }
}

/// `CSphereGeom` slot 48 (0x351AF380): centre placed by `gwd`, `r·scale`.
#[must_use]
pub fn prepare_sphere(sp: &SpherePrim, g: &GeomWorldData) -> SpherePrim {
    SpherePrim {
        center: place_point(sp.center, g),
        r: sp.r * g.scale,
    }
}

/// `CCylinderGeom` slot 48 (0x35142240; `CCapsuleGeom` 0x35038B60 calls it and returns type 6):
/// centre placed by `gwd`, axis rotated (not renormalised), `r·scale`, `hh·scale`.
#[must_use]
pub fn prepare_cylinder(c: &CylinderPrim, g: &GeomWorldData) -> CylinderPrim {
    CylinderPrim {
        center: place_point(c.center, g),
        axis: rotate(c.axis, g),
        r: c.r * g.scale,
        hh: c.hh * g.scale,
    }
}

/// `CRayGeom` slot 48 (0x3519A020): origin placed by `gwd`, `dir` rotated and scaled.
#[must_use]
pub fn prepare_ray(r: &RayPrim, g: &GeomWorldData) -> RayPrim {
    let d = rotate(r.dir, g);
    let s = g.scale;
    RayPrim {
        origin: place_point(r.origin, g),
        dir: Vec3::new(d.x * s, d.y * s, d.z * s),
    }
}

/// Geometry slot 48 `PreparePrimitive` for the primitive geometries: the primitive in world
/// space (`None` for primitives without a primitive geometry: triangles, heightfields).
#[must_use]
pub fn prepare_primitive(p: &Primitive, g: &GeomWorldData) -> Option<Primitive> {
    Some(match p {
        Primitive::Box(b) => Primitive::Box(prepare_box(b, g)),
        Primitive::Sphere(s) => Primitive::Sphere(prepare_sphere(s, g)),
        Primitive::Cylinder(c) => Primitive::Cylinder(prepare_cylinder(c, g)),
        Primitive::Capsule(c) => Primitive::Capsule(prepare_cylinder(c, g)),
        Primitive::Ray(r) => Primitive::Ray(prepare_ray(r, g)),
        Primitive::Triangle(_) | Primitive::Heightfield(_) => return None,
    })
}

/// One side of a swept primitive test: the primitive in its geometry frame (what the geometry
/// object stores) and the geometry's `minVtxDist` (`CGeometry +16`).
#[derive(Clone, Copy, Debug)]
pub struct SweepGeom<'a> {
    pub prim: &'a Primitive,
    pub min_vtx_dist: f32,
}

/// Result of [`sweep_primitives`]: the return value of the engine routine (number of contacts,
/// 0 or 1) and the contact.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct SweepHit {
    pub contact: GeomContact,
}

/// The sweep path of the primitive `Intersect` 0x35151F20 after its OVERLAP pre-check (see the
/// module docs): sweep A (moving with `gwd_a.v` for `ip.time_interval`) against the static B.
///
/// * `bbox_b` is B's slot-6 bounding box (geometry frame), used for the tunnelling back-up.
/// * `area` is the contact-area buffer the engine takes from its per-thread pool (`None` when
///   `ip.no_area_contacts` is set or the pool is exhausted); it should come in with `npt = 0`,
///   `min_edge = 0` and `n = 0` like the pool's.
/// * `gwd_a.offset` may be changed by the float round trip of step 3 (kept bug).
///
/// Returns `None` where the engine returns 0.
#[allow(clippy::too_many_arguments)]
pub fn sweep_primitives(
    tables: &UnprojTables,
    a: SweepGeom<'_>,
    gwd_a: &mut GeomWorldData,
    b: SweepGeom<'_>,
    gwd_b: &GeomWorldData,
    bbox_b: &BoxPrim,
    ip: &IntersectionParams,
    mut area: Option<&mut ContactArea>,
) -> Option<SweepHit> {
    // 1. sweep length and direction
    let v = gwd_a.v;
    let vlen = ((v.x * v.x + v.y * v.y) + v.z * v.z).sqrt();
    let inv = 1.0 / vlen;
    let l = vlen * ip.time_interval;
    let vh = Vec3::new(v.x * inv, inv * v.y, inv * v.z);

    // 3. prepare A at the end pose, B as is, restore A's offset
    gwd_a.offset = Vec3::new(
        vh.x * l + gwd_a.offset.x,
        vh.y * l + gwd_a.offset.y,
        vh.z * l + gwd_a.offset.z,
    );
    let pa = prepare_primitive(a.prim, gwd_a)?;
    let pb = prepare_primitive(b.prim, gwd_b)?;
    gwd_a.offset = Vec3::new(
        gwd_a.offset.x - vh.x * l,
        gwd_a.offset.y - vh.y * l,
        gwd_a.offset.z - vh.z * l,
    );

    // 5. unprojection parameters
    let ma = a.min_vtx_dist;
    let mb = b.min_vtx_dist;
    let gap = ip.max_surface_gap_angle;
    let mut p = UnprojParams {
        mode: 0,
        dir: Vec3::new(-vh.x, -vh.y, -vh.z),
        center: Vec3::ZERO,
        vel: l,
        tmax: l,
        tmin: 0.0,
        min_pt_dist: ((ma + mb) - (ma - mb).abs()) * 0.5,
        max_cos: 1.0 - (gap * gap) * 0.5,
        flag: 1,
    };
    let mut y = UnprojResult {
        t: 0.0,
        ..UnprojResult::default()
    };
    let ret = tables.unproject(0, &mut p, &pa, -1, &pb, -1, &mut y, area.as_deref_mut());
    let diff = (y.t - f64::from(p.tmax)) as f32;
    let mut ok = ret != 0 && diff.to_bits() >> 31 != 0;
    if !ok {
        return None;
    }
    let dot = |n: Vec3, d: Vec3| (n.y * d.y + d.x * n.x) + n.z * d.z;
    if dot(y.n, p.dir) > 0.0 {
        // 6. tunnelled: back up along the sweep
        let t0 = y.t;
        let r = &gwd_b.r.m;
        let w = Vec3::new(
            (vh.y * r[1][0] + vh.x * r[0][0]) + vh.z * r[2][0],
            (vh.y * r[1][1] + vh.x * r[0][1]) + vh.z * r[2][1],
            (vh.y * r[1][2] + vh.x * r[0][2]) + vh.z * r[2][2],
        );
        let bb = &bbox_b.basis.m;
        let loc = [
            (bb[0][1] * w.y + bb[0][0] * w.x) + bb[0][2] * w.z,
            (bb[1][1] * w.y + bb[1][0] * w.x) + bb[1][2] * w.z,
            (bb[2][1] * w.y + bb[2][0] * w.x) + bb[2][2] * w.z,
        ];
        let sz = [bbox_b.size.x, bbox_b.size.y, bbox_b.size.z];
        let ar = [
            (loc[0].abs() * sz[1]) * sz[2],
            (loc[1].abs() * sz[0]) * sz[2],
            (loc[2].abs() * sz[0]) * sz[1],
        ];
        let c1 = ((ar[0] - ar[1]).to_bits() >> 31) as i32;
        let c2 = ((ar[c1 as usize] - ar[2]).to_bits() >> 31) as i32;
        let m = c1 | (c2 << 1);
        let idx = (((m ^ 2) | 4) >> 1) & m;
        let i = idx as usize;
        let tb = t0 + f64::from((sz[i] / loc[i].abs()) * 0.1);
        let lb = (f64::from(l) - tb) as f32;
        p.tmax = lb;
        gwd_a.offset = Vec3::new(
            vh.x * lb + gwd_a.offset.x,
            lb * vh.y + gwd_a.offset.y,
            lb * vh.z + gwd_a.offset.z,
        );
        let pa2 = prepare_primitive(a.prim, gwd_a)?;
        gwd_a.offset = Vec3::new(
            gwd_a.offset.x - vh.x * lb,
            gwd_a.offset.y - vh.y * lb,
            gwd_a.offset.z - vh.z * lb,
        );
        let ret2 = tables.unproject(0, &mut p, &pa2, -1, &pb, -1, &mut y, area.as_deref_mut());
        let diff2 = (y.t - f64::from(lb)) as f32;
        let d2 = dot(y.n, p.dir);
        y.t += tb;
        p.tmax = l;
        ok = ret2 != 0 && (diff2.to_bits() >> 31) & (d2.to_bits() >> 31) != 0;
        if !ok {
            return None;
        }
    }

    // 7. the contact
    let mut c = GeomContact {
        t: y.t,
        ..GeomContact::default()
    };
    // sweep: distance travelled before the hit
    c.t = f64::from(p.tmax) - c.t;
    c.pt = y.pt;
    let n = y.n;
    let l2 = (n.y * n.y + n.x * n.x) + n.z * n.z;
    c.n = if l2 > 1e-20 {
        let k = 1.0 / l2.sqrt();
        Vec3::new(k * n.x, n.y * k, n.z * k)
    } else {
        Vec3::new(0.0, 0.0, 1.0)
    };
    c.dir = p.dir;
    c.i_unproj_mode = p.mode;
    c.vel = p.vel;
    c.id = [-1, -1];
    c.i_prim = [0, 0];
    c.i_feature = y.i_feature;
    c.i_node = [-1, -1];
    c.border = Vec::new();
    c.idx_border = Vec::new();
    c.center = Vec3::ZERO;
    let ta = pa.geom_type().id();
    let tbt = pb.geom_type().id();
    if let Some(ar) = area.filter(|ar| ar.npt != 0) {
        // the border starts empty in the sweep path, so the area always becomes the border
        // (the engine also takes it over a non-empty border when A or B is a cylinder)
        let cyl = ta == 5 || tbt == 5;
        let nb = c.border.len() as i32;
        if nb < ar.npt && (nb == 0 || cyl) {
            let n = ar.npt as usize;
            c.border = ar.pt[..n].to_vec();
            let mut s = Vec3::ZERO;
            for q in &c.border {
                s = Vec3::new(q.x + s.x, q.y + s.y, q.z + s.z);
            }
            let k = 1.0 / ar.npt as f32;
            c.center = Vec3::new(k * s.x, k * s.y, k * s.z);
        }
        c.area = Some(ar.clone());
    }
    if c.border.is_empty() {
        c.center = c.pt;
        c.border = vec![c.pt];
    }
    c.border_consecutive = false;
    Some(SweepHit { contact: c })
}

// ------------------------------------------------------------------------------------------
// RegisterIntersection (CGeometry slot 54)

/// The fields of one `geometry_under_test` record that `RegisterIntersection` reads (offsets of
/// the x64 record): motion of the geometry at the contact and its `minVtxDist` copy.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct GeomTestMotion {
    /// `+164` linear velocity.
    pub v: Vec3,
    /// `+176` angular velocity.
    pub w: Vec3,
    /// `+188` centre of mass.
    pub com: Vec3,
    /// `+200` rotation centre candidate (mode 1 takes the longer of the two sides').
    pub rot_center: Vec3,
    /// `+360` `minVtxDist` of the geometry (only used for the next contact's area).
    pub min_vtx_dist: f32,
}

/// Values the engine reads from **uninitialised stack slots** of `RegisterIntersection`'s
/// `unproj_params` (kept bug): `minPtDist` (+40) is never written, and `tmax` (+32) is only
/// written on the velocity / rotation path, so the penetration fallback after a too-slow relative
/// velocity uses whatever is on the stack. The cells that read them see these values instead.
/// (The oracle test fills the stack with a known word before the call and passes the same word
/// here; with that, every output matches — so these two slots are the only stale reads.)
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct StaleUnprojSlots {
    pub tmax: f32,
    pub min_pt_dist: f32,
}

impl Default for StaleUnprojSlots {
    /// The port's choice: no `tmax` bound, no minimum point distance.
    fn default() -> Self {
        Self {
            tmax: 1e10,
            min_pt_dist: 0.0,
        }
    }
}

/// `CGeometry::RegisterIntersection` (slot 54, x64 0x3514F3F0, x86 0x351578C0): turn a primitive
/// contact (`inters`, from an INTERSECT cell) into an unprojected [`GeomContact`] written into
/// `contact` (the engine's next free contact slot; its `area` is the pool area the engine
/// attached, or `None`).
///
/// * `ip.i_unprojection_mode == 0`: `dir = (vB + wB×(p − comB)) − (vA + wA×(p − comA))` at
///   `p = inters.pt[1]`, summed as `((wB×rB + vB) − vA) − wA×rA`; `vel = |dir|`. If
///   `vel < ip.vrel_min` (or NaN) go to the fallback; else `dir /= vel`.
/// * mode 1: `axis = normalize(wB − wA)`, `vel = |wB − wA|`, centre = A's `rot_center` if it is
///   strictly longer than B's, else B's.
/// * `tmax = vel·time_interval`, `tmin = 0`, `maxcos = 0.1`, `flag = 0`; call
///   `UNPROJECT[mode][tA][tB]`. Failure → no contact. If `t > vel·time_interval` → fallback.
/// * Fallback: mode 0, `inters.n` normalised **in place** (sub_35021020; the caller sees it),
///   `dir = inters.n`, `vel = 0`, `UNPROJECT[0][tA][tB]`; failure → no contact.
/// * Contact: `t`, `pt`, `n` from the result; `dir`, mode, `vel` from the params; `id` from
///   `inters.id` (sign-extended), `iNode` from `inters.i_node`; border = `[pt]`. `iPrim` and
///   `iFeature` are **not** written (the caller fills them). The area is kept only if it has
///   points.
///
/// The engine then counts the contact, attaches a fresh pool area to the next slot with
/// `minedge = min_idiom(A.minVtxDist, B.minVtxDist)` (see [`next_area_min_edge`]) and raises the
/// stop flag when the contact buffer is full; that bookkeeping belongs to the caller. Returns
/// whether a contact was produced (the engine function itself always returns 0).
#[allow(clippy::too_many_arguments)]
pub fn register_intersection(
    tables: &UnprojTables,
    prim_a: &Primitive,
    a: &GeomTestMotion,
    prim_b: &Primitive,
    b: &GeomTestMotion,
    inters: &mut crate::collision::contact::PrimInters,
    ip: &IntersectionParams,
    contact: &mut GeomContact,
    stale: StaleUnprojSlots,
) -> bool {
    use super::common::{lt_or_nan, normalize_in_place};
    let mut p = UnprojParams {
        mode: 0,
        dir: Vec3::ZERO,
        center: Vec3::ZERO,
        vel: 0.0,
        tmax: stale.tmax,
        tmin: 0.0,
        min_pt_dist: stale.min_pt_dist,
        max_cos: 0.1,
        flag: 0,
    };
    let mut res = UnprojResult::default();
    let mut fallback = false;
    let mut mode = 0usize;
    match ip.i_unprojection_mode {
        0 => {
            let pt = inters.pt[1];
            let (ra, rb) = (
                Vec3::new(pt.x - a.com.x, pt.y - a.com.y, pt.z - a.com.z),
                Vec3::new(pt.x - b.com.x, pt.y - b.com.y, pt.z - b.com.z),
            );
            let (wa, wb) = (a.w, b.w);
            let x = (((wb.y * rb.z - wb.z * rb.y) + b.v.x) - a.v.x) - (wa.y * ra.z - wa.z * ra.y);
            let y = (((wb.z * rb.x - wb.x * rb.z) + b.v.y) - a.v.y) - (wa.z * ra.x - wa.x * ra.z);
            let z = (((wb.x * rb.y - wb.y * rb.x) + b.v.z) - a.v.z) - (wa.x * ra.y - wa.y * ra.x);
            let len = ((x * x + y * y) + z * z).sqrt();
            p.vel = len;
            p.dir = Vec3::new(x, y, z);
            if lt_or_nan(len, ip.vrel_min) {
                fallback = true;
            } else {
                let inv = 1.0 / len;
                p.dir = Vec3::new(x * inv, y * inv, z * inv);
            }
        }
        1 => {
            mode = 1;
            p.mode = 1;
            let la = (a.rot_center.x * a.rot_center.x + a.rot_center.y * a.rot_center.y)
                + a.rot_center.z * a.rot_center.z;
            let lb = (b.rot_center.x * b.rot_center.x + b.rot_center.y * b.rot_center.y)
                + b.rot_center.z * b.rot_center.z;
            p.center = if la > lb { a.rot_center } else { b.rot_center };
            let d = Vec3::new(b.w.x - a.w.x, b.w.y - a.w.y, b.w.z - a.w.z);
            let len = ((d.x * d.x + d.y * d.y) + d.z * d.z).sqrt();
            let inv = 1.0 / len;
            p.vel = len;
            p.dir = Vec3::new(inv * d.x, d.y * inv, d.z * inv);
        }
        // other modes read stale stack values in the engine; treat them like mode 0's fallback
        _ => fallback = true,
    }
    if !fallback {
        p.tmax = p.vel * ip.time_interval;
        let r = tables.unproject(
            mode,
            &mut p,
            prim_a,
            -1,
            prim_b,
            -1,
            &mut res,
            contact.area.as_mut(),
        );
        if r == 0 {
            return false;
        }
        if res.t > f64::from(p.vel * ip.time_interval) {
            fallback = true;
        }
    }
    if fallback {
        p.mode = 0;
        normalize_in_place(&mut inters.n);
        p.dir = inters.n;
        p.vel = 0.0;
        let r = tables.unproject(
            0,
            &mut p,
            prim_a,
            -1,
            prim_b,
            -1,
            &mut res,
            contact.area.as_mut(),
        );
        if r == 0 {
            return false;
        }
    }
    contact.t = res.t;
    contact.pt = res.pt;
    contact.n = res.n;
    contact.dir = p.dir;
    contact.vel = p.vel;
    contact.i_unproj_mode = p.mode;
    contact.id = [i32::from(inters.id[0]), i32::from(inters.id[1])];
    contact.i_node = inters.i_node;
    contact.border = vec![contact.pt];
    if contact.area.as_ref().is_none_or(|ar| ar.npt <= 0) {
        contact.area = None;
    }
    true
}

/// `min_idiom` of the two sides' `minVtxDist` (`((a + b) − |a − b|)·0.5`): the `minedge` the
/// engine gives the area it attaches to the next contact slot after `RegisterIntersection`.
#[must_use]
pub fn next_area_min_edge(a: &GeomTestMotion, b: &GeomTestMotion) -> f32 {
    ((b.min_vtx_dist + a.min_vtx_dist) - (a.min_vtx_dist - b.min_vtx_dist).abs()) * 0.5
}
