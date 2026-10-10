//! The capsule cells of the UNPROJECT table: compositions of the cylinder and sphere cells
//! (capsule = cylinder of the same `hh` + spheres of radius `r` at `c ± axis·hh`). The capsule
//! primitive has the cylinder layout and is passed to the cylinder cells as is, with feature id
//! 0x43 (67) for the capsule side.
//!
//! | cell | x64 | x86 | composition |
//! |---|---|---|---|
//! | `[0][box][cap]` | 0x351870D0 | 0x3518C0D0 | box–cyl 0x351801A0 + 2× box–sph 0x35185B80 ([`compose_full`]) |
//! | `[0][sph][cap]` | 0x351916F0 | 0x3519D920 | sph–cyl 0x35189260 + 2× sph–sph 0x35178110 ([`compose_full`]) |
//! | `[0][cyl][cap]` | 0x35190570 | 0x3519C9D0 | cyl–cyl 0x351893C0 + 2× cyl–sph 0x35187780 ([`compose_full`]) |
//! | `[0][tri][cap]` | 0x351767C0 | 0x3517EE80 | tri–cyl 0x351716D0 + 2× tri–sph 0x35175490 ([`compose_simple`]) |
//! | `[0][ray][cap]` | 0x3517C9E0 | 0x351915D0 | ray–cyl 0x35179650 + 2× ray–sph 0x3517BDA0 ([`compose_simple`]) |
//! | `[0][cap][cap]` | 0x35190C20 | 0x3519CF90 | cyl–cyl + cyl–sph per end + sph–sph per end pair ([`unproject_cap_cap`]) |
//! | `[0][cap][box]` | 0x351876A0 | 0x3518C5E0 | 215-byte area swap of box–cap |
//! | `[0][cap][cyl]` | 0x35190B40 | 0x3519CEE0 | 215-byte area swap of cyl–cap |
//! | `[0][cap][tri]` | 0x351769E0 | 0x3517F050 | 335-byte swap of tri–cap |
//! | `[0][cap][ray]` | 0x3517CC00 | 0x351917A0 | 335-byte swap of ray–cap |
//! | `[0][cap][sph]` | 0x35191CB0 | 0x3519E020 | 449-byte swap of sph–cap with its own area fix-up ([`unproject_cap_sph`]) |
//!
//! # [`compose_full`] (box/sph/cyl–cap)
//!
//! `tmin` is saved and `out.t = 0`.
//! * `|dir|² == 0` (`ucomiss`, NaN counts as zero) — least-penetration mode: run the cylinder
//!   cell (it writes the chosen `dir` back; box–cap additionally sets `out.t = 0` when it fails);
//!   remember `t0 = out.t` and the cylinder's `dir`. Then for each end sphere (−hh first) zero
//!   `dir`, unproject into a temporary result (whose `cos` slot starts at 0 once) and take it over
//!   (all 48 bytes, plus the `dir` it chose) when it succeeds and either `t > out.t` or the
//!   normal points away from the capsule middle (`n·axis > 0` for the −hh sphere, `n·axis < 0`
//!   — NaN included — for the +hh one). `dir` ends as the last accepted direction (0 if none).
//! * otherwise — fixed direction: cylinder cell into `out`, `t0 = out.t`, and
//!   `tmin = (float)((out.t − tmin)·ret + tmin)` in double after it and after the first sphere,
//!   so each later cell only succeeds beyond the best `t` so far; the spheres write `out`
//!   directly and add `ret` (−hh end) or `2·ret` (+hh end) to `out.i_feature[1]`.
//!
//! `tmin` is restored. If an area is present and `|out.t − t0| > (float)(r·0.02f)` its `npt` is
//! set to 0 (the cylinder's patch no longer belongs to the final contact). Returns the OR of the
//! three cell results (cyl–cap: in the zero-direction mode a sphere that succeeds but is not taken
//! over counts as 0).
//!
//! **Uninitialised `cos` (deviation by necessity).** The temporary result of the zero-direction
//! mode is copied whole into `out`, but only box–cap zeroes its `cos` slot first; sph–cap and
//! cyl–cap copy whatever is on the stack (the sphere cells never write `cos`). The port writes 0;
//! linear callers never read `cos`. Tests compare these cells with `cos` masked.

use super::common::{
    as_box, as_cap, as_ray, as_sph, as_tri, swap_linear, swap_linear_area, ucomiss_eq,
};
use super::{UnprojTables, MODE_LINEAR};
use crate::collision::contact::{ContactArea, UnprojParams, UnprojResult};
use crate::geometry::primitives::{BoxPrim, CapsulePrim, Primitive, RayPrim, SpherePrim, Triangle};
use crate::ids::GeomType;
use crate::math::Vec3;

/// Feature id the compositions pass for the capsule side to the cylinder cells.
pub const CAPSULE_FEATURE: i32 = 0x43;

/// The capsule's end spheres: `(c − axis·hh, r)` and `(axis·hh + c, r)`.
#[must_use]
pub fn end_spheres(b: &CapsulePrim) -> [SpherePrim; 2] {
    let h = b.hh;
    let a = b.axis;
    [
        SpherePrim {
            center: Vec3::new(
                b.center.x - a.x * h,
                b.center.y - a.y * h,
                b.center.z - a.z * h,
            ),
            r: b.r,
        },
        SpherePrim {
            center: Vec3::new(
                a.x * h + b.center.x,
                a.y * h + b.center.y,
                a.z * h + b.center.z,
            ),
            r: b.r,
        },
    ]
}

/// `tmin = (float)((t − tmin)·ret + tmin)` in double (the branch-free "raise tmin on success").
#[inline]
fn bump_tmin(p: &mut UnprojParams, t: f64, ret: i32) {
    let m = f64::from(p.tmin);
    p.tmin = ((t - m) * f64::from(ret) + m) as f32;
}

/// `(n.y·a.y + a.x·n.x) + n.z·a.z`.
#[inline]
fn dot_axis(n: Vec3, a: Vec3) -> f32 {
    (n.y * a.y + a.x * n.x) + n.z * a.z
}

/// Variant switches of [`compose_full`].
#[derive(Clone, Copy, Debug)]
pub struct ComposeFlags {
    /// box–cap: `out.t = 0` when the cylinder cell fails in least-penetration mode.
    pub zero_t_on_cyl_fail: bool,
    /// cyl–cap: a sphere result that is not taken over counts as 0 in the return value.
    pub ret_only_taken: bool,
}

/// The box/sph/cyl–capsule composition (see the module docs). `cyl` runs the cylinder cell
/// against the capsule (feature 0x43), `sph` the sphere cell against one end sphere (feature −1).
pub fn compose_full(
    p: &mut UnprojParams,
    b: &CapsulePrim,
    out: &mut UnprojResult,
    mut area: Option<&mut ContactArea>,
    flags: ComposeFlags,
    cyl: impl FnOnce(&mut UnprojParams, &mut UnprojResult, Option<&mut ContactArea>) -> i32,
    mut sph: impl FnMut(
        &mut UnprojParams,
        &SpherePrim,
        &mut UnprojResult,
        Option<&mut ContactArea>,
    ) -> i32,
) -> i32 {
    let tmin0 = p.tmin;
    out.t = 0.0;
    let ends = end_spheres(b);
    let dl2 = (p.dir.x * p.dir.x + p.dir.y * p.dir.y) + p.dir.z * p.dir.z;
    let (r1, r2, r3, t0);
    if ucomiss_eq(dl2, 0.0) {
        let mut best_dir = Vec3::ZERO;
        r1 = cyl(p, out, area.as_deref_mut());
        if r1 != 0 {
            best_dir = p.dir;
        } else if flags.zero_t_on_cyl_fail {
            out.t = 0.0;
        }
        t0 = out.t;
        let mut tmp = UnprojResult { cos_t: 0.0, ..*out };
        let mut rs = [0i32; 2];
        for (k, s) in ends.iter().enumerate() {
            p.dir = Vec3::ZERO;
            let r = sph(p, s, &mut tmp, area.as_deref_mut());
            rs[k] = r;
            if r != 0 {
                let d = dot_axis(tmp.n, b.axis);
                let away = if k == 0 {
                    d > 0.0
                } else {
                    super::common::lt_or_nan(d, 0.0)
                };
                if tmp.t > out.t || away {
                    *out = tmp;
                    best_dir = p.dir;
                } else if flags.ret_only_taken {
                    rs[k] = 0;
                }
            }
        }
        p.dir = best_dir;
        r2 = rs[0];
        r3 = rs[1];
    } else {
        r1 = cyl(p, out, area.as_deref_mut());
        t0 = out.t;
        bump_tmin(p, out.t, r1);
        r2 = sph(p, &ends[0], out, area.as_deref_mut());
        bump_tmin(p, out.t, r2);
        out.i_feature[1] = out.i_feature[1].wrapping_add(r2);
        r3 = sph(p, &ends[1], out, area.as_deref_mut());
        out.i_feature[1] = out.i_feature[1].wrapping_add(2 * r3);
        p.tmin = tmin0;
    }
    if let Some(a) = area {
        if (out.t - t0).abs() > f64::from(b.r * 0.02) {
            a.npt = 0;
        }
    }
    r1 | r2 | r3
}

/// The tri/ray–capsule composition (0x351767C0, 0x3517C9E0): always the fixed-direction form of
/// [`compose_full`] (also for `dir = 0`), with no area handling.
pub fn compose_simple(
    p: &mut UnprojParams,
    b: &CapsulePrim,
    out: &mut UnprojResult,
    cyl: impl FnOnce(&mut UnprojParams, &mut UnprojResult) -> i32,
    mut sph: impl FnMut(&mut UnprojParams, &SpherePrim, &mut UnprojResult) -> i32,
) -> i32 {
    let tmin0 = p.tmin;
    out.t = 0.0;
    let ends = end_spheres(b);
    let r1 = cyl(p, out);
    bump_tmin(p, out.t, r1);
    let r2 = sph(p, &ends[0], out);
    bump_tmin(p, out.t, r2);
    out.i_feature[1] = out.i_feature[1].wrapping_add(r2);
    let r3 = sph(p, &ends[1], out);
    out.i_feature[1] = out.i_feature[1].wrapping_add(2 * r3);
    p.tmin = tmin0;
    r1 | r2 | r3
}

/// sph–cap 0x351916F0: [`compose_full`] with sph–cyl 0x35189260 and sph–sph 0x35178110
/// (both 6-argument cells; the area is only used for the final `npt` reset).
pub fn unproject_sph_cap(
    p: &mut UnprojParams,
    a: &SpherePrim,
    fa: i32,
    b: &CapsulePrim,
    _fb: i32,
    out: &mut UnprojResult,
    area: Option<&mut ContactArea>,
) -> i32 {
    compose_full(
        p,
        b,
        out,
        area,
        ComposeFlags {
            zero_t_on_cyl_fail: false,
            ret_only_taken: false,
        },
        |p, out, _| super::ray_cyl::unproject_sph_cyl(p, a, fa, b, CAPSULE_FEATURE, out),
        |p, s, out, _| super::sph::unproject_sph_sph(p, a, -1, s, -1, out),
    )
}

/// cap–sph 0x35191CB0 (449 bytes): the 335-byte linear swap of [`unproject_sph_cap`] plus, when
/// an area pointer is present (no `npt` check for the normal), swapping the A/B feature ids of
/// the first `npt` area points and negating the area normal.
pub fn unproject_cap_sph(
    p: &mut UnprojParams,
    a: &CapsulePrim,
    fa: i32,
    b: &SpherePrim,
    fb: i32,
    out: &mut UnprojResult,
    mut area: Option<&mut ContactArea>,
) -> i32 {
    super::common::negate_dir(p);
    let r = unproject_sph_cap(p, b, fb, a, fa, out, area.as_deref_mut());
    if r != 0 {
        super::common::fix_swapped_linear(p, out);
        if let Some(ar) = area {
            let mut i = 0usize;
            while (i as i32) < ar.npt {
                let [f0, f1] = &mut ar.feature;
                std::mem::swap(&mut f0[i], &mut f1[i]);
                i += 1;
            }
            ar.n = Vec3::new(-ar.n.x, -ar.n.y, -ar.n.z);
        }
    }
    super::common::negate_dir(p);
    r
}

/// ray–cap 0x3517C9E0: [`compose_simple`] with ray–cyl 0x35179650 and ray–sph 0x3517BDA0.
pub fn unproject_ray_cap(
    p: &mut UnprojParams,
    a: &RayPrim,
    fa: i32,
    b: &CapsulePrim,
    _fb: i32,
    out: &mut UnprojResult,
) -> i32 {
    compose_simple(
        p,
        b,
        out,
        |p, out| super::ray_cyl::unproject_ray_cyl(p, a, fa, b, CAPSULE_FEATURE, out),
        |p, s, out| super::box_round::unproject_ray_sph(p, a, fa, s, -1, out),
    )
}

/// box–cap 0x351870D0: [`compose_full`] with box–cyl 0x351801A0 (area) and box–sph 0x35185B80;
/// `out.t = 0` when box–cyl fails in least-penetration mode.
pub fn unproject_box_cap(
    p: &mut UnprojParams,
    a: &BoxPrim,
    fa: i32,
    b: &CapsulePrim,
    _fb: i32,
    out: &mut UnprojResult,
    area: Option<&mut ContactArea>,
) -> i32 {
    compose_full(
        p,
        b,
        out,
        area,
        ComposeFlags {
            zero_t_on_cyl_fail: true,
            ret_only_taken: false,
        },
        |p, out, ar| super::box_cyl::unproject_box_cyl(p, a, fa, b, CAPSULE_FEATURE, out, ar),
        |p, s, out, _| super::box_round::unproject_box_sph(p, a, fa, s, -1, out),
    )
}

fn cell_box_cap(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    area: Option<&mut ContactArea>,
) -> i32 {
    unproject_box_cap(p, as_box(a), fa, as_cap(b), fb, out, area)
}

/// cap–box 0x351876A0: 215-byte area swap of [`unproject_box_cap`].
fn cell_cap_box(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    area: Option<&mut ContactArea>,
) -> i32 {
    let (cap, bx) = (as_cap(a), as_box(b));
    swap_linear_area(p, out, area, |p, out, ar| {
        unproject_box_cap(p, bx, fb, cap, fa, out, ar)
    })
}

/// tri–cap 0x351767C0: [`compose_simple`] with tri–cyl 0x351716D0 and tri–sph 0x35175490.
pub fn unproject_tri_cap(
    p: &mut UnprojParams,
    a: &Triangle,
    fa: i32,
    b: &CapsulePrim,
    _fb: i32,
    out: &mut UnprojResult,
) -> i32 {
    compose_simple(
        p,
        b,
        out,
        |p, out| super::tri_cyl::unproject_tri_cyl(p, a, fa, b, CAPSULE_FEATURE, out),
        |p, s, out| super::tri::unproject_tri_sph(p, a, fa, s, -1, out),
    )
}

fn cell_tri_cap(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    unproject_tri_cap(p, as_tri(a), fa, as_cap(b), fb, out)
}

/// cap–tri 0x351769E0: 335-byte linear swap of [`unproject_tri_cap`].
fn cell_cap_tri(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    let (cap, tri) = (as_cap(a), as_tri(b));
    swap_linear(p, out, |p, out| unproject_tri_cap(p, tri, fb, cap, fa, out))
}

fn cell_sph_cap(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    area: Option<&mut ContactArea>,
) -> i32 {
    unproject_sph_cap(p, as_sph(a), fa, as_cap(b), fb, out, area)
}

fn cell_cap_sph(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    area: Option<&mut ContactArea>,
) -> i32 {
    unproject_cap_sph(p, as_cap(a), fa, as_sph(b), fb, out, area)
}

fn cell_ray_cap(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    unproject_ray_cap(p, as_ray(a), fa, as_cap(b), fb, out)
}

/// cap–ray 0x3517CC00: 335-byte linear swap of [`unproject_ray_cap`].
fn cell_cap_ray(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    let (cap, ray) = (as_cap(a), as_ray(b));
    swap_linear(p, out, |p, out| unproject_ray_cap(p, ray, fb, cap, fa, out))
}

/// Fill the capsule cells.
pub fn fill(t: &mut UnprojTables) {
    use GeomType::{Box, Capsule, Ray, Sphere, TriMesh};
    t.set(MODE_LINEAR, Box, Capsule, cell_box_cap);
    t.set(MODE_LINEAR, Capsule, Box, cell_cap_box);
    t.set(MODE_LINEAR, TriMesh, Capsule, cell_tri_cap);
    t.set(MODE_LINEAR, Capsule, TriMesh, cell_cap_tri);
    t.set(MODE_LINEAR, Sphere, Capsule, cell_sph_cap);
    t.set(MODE_LINEAR, Capsule, Sphere, cell_cap_sph);
    t.set(MODE_LINEAR, Ray, Capsule, cell_ray_cap);
    t.set(MODE_LINEAR, Capsule, Ray, cell_cap_ray);
}
