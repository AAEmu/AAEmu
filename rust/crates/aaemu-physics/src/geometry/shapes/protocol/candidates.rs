//! Slot 57 `GetUnprojectionCandidates(iSide, contact, prim**, iPrim, gtest)` (inferred name):
//! from a contact point and the feature the unprojection reported on this side, list the faces
//! (`gtest+336`, count `+352`) and edges (`gtest+344`, count `+356`) the contact area code
//! should try. `contact` is the 48-byte [`UnprojResult`]: the slots read `pt` (`+16`) and
//! `iFeature[iSide]` (`+40 + 4·iSide`).
//!
//! `prim` is the caller's primitive pointer (in/out). While `gtest+160` is set, box, cylinder
//! and capsule re-transform their primitive into the `+296` buffer and redirect `*prim` there;
//! the sphere always does; the ray re-transforms but leaves `*prim` alone. The port models the
//! pointer by value: `*prim` receives the re-transformed primitive when the binary redirects it.

use super::box_row;
use super::gtest::{as_box, as_cylinder, EdgeCandidate, FaceCandidate, GeomUnderTest};
use super::prep::{xf_box, xf_cylinder, xf_ray, xf_sphere};
use crate::collision::contact::UnprojResult;
use crate::geometry::primitives::{CylinderPrim, Primitive};
use crate::geometry::shapes::cylinder::CylinderData;
use crate::geometry::shapes::{BoxGeom, RayGeom, SphereGeom};
use crate::math::{isnonneg, sgnnz, Vec3};

/// Index tables at 0x35209A50 / 0x35209A60 as the slot reads them, including the fourth entry
/// (0) that a feature with axis index 3 reaches.
const INC4: [usize; 4] = [1, 2, 0, 0];
const DEC4: [usize; 4] = [2, 0, 1, 0];

/// `(sgnnz(x) + 1) >> 1`: 1 for `x ≥ +0`, 0 for negative numbers and `-0.0`.
#[inline]
fn pos_bit(x: f32) -> i32 {
    (sgnnz(x) + 1) >> 1
}

/// Box slot 57 (0x3513BAF0).
///
/// 1. Optional re-transform (`+160`), `ids[0] = -1`; `l = Basis·(pt − c)` with the box `*prim`.
/// 2. A face feature `0x40 | i<<1 | s` close to one of its edges is turned into that edge: with
///    `j = inc(i)`, `k = dec(i)`, if `|l[j] − s[j]| < s[j]·0.001` the feature becomes edge `j`
///    with bits `(l[k] ≥ +0) | (feat & 2)`, else if `|l[k] − s[k]| < s[k]·0.001` edge `k` with
///    `(feat & 1) | (l[j] ≥ +0)<<1`. Quirks kept: only the positive side (`l − s`, not
///    `|l| − s`) is tested, and the first case copies bit 1 of the *face code* (the low bit of
///    the axis index) where the face sign would be expected.
///    Net effect: the new edge is coded along the axis whose boundary test just passed, so
///    step 3 always passes too and a face contact near a boundary always ends as a vertex.
/// 3. An edge feature `0x20 | a<<2 | b1 b0` close to its end (`|l[a] − s[a]| < s[a]·0.001`,
///    positive side only) becomes the vertex `(l[a] ≥ +0)<<a | b0<<inc(a) | b1<<dec(a)`.
/// 4. Output by the final feature class (low byte `& 0xE0`): face → one face candidate; edge →
///    its two faces and the edge (`n = −` the faces' normals); anything else (vertex or
///    unknown codes) → the three faces and three edges at the vertex given by bits 0–2.
///
/// Axis index 3 (codes `0x46/0x47`, `0x2C–0x2F`) makes the binary index past the vectors;
/// face rows read the primitive's words 9–11 ([`box_word`](super::box_word)) as it does, while the edge size /
/// local-coordinate reads past the record are taken as 0 here (stack/arena garbage there).
pub fn box_get_unprojection_candidates(
    g: &BoxGeom,
    i_side: i32,
    contact: &UnprojResult,
    prim: &mut Primitive,
    gt: &mut GeomUnderTest,
) -> i32 {
    if gt.transform_prims != 0 {
        let b = Primitive::Box(xf_box(&g.prim, &gt.r, gt.offset, gt.scale));
        *gt.unproj_prim_mut(b) = b;
        *prim = b;
    }
    let b = as_box(prim);
    gt.clear_id0();
    let mut feat = contact.i_feature[(i_side & 1) as usize];
    let d = contact.pt - b.center;
    let l = b.basis.mul_vec(d);
    let size = |i: usize| -> f32 {
        if i < 3 {
            b.size[i]
        } else {
            0.0
        }
    };
    let loc = |i: usize| -> f32 {
        if i < 3 {
            l[i]
        } else {
            0.0
        }
    };
    let near = |i: usize| -> bool { (loc(i) - size(i)).abs() < size(i) * 0.001 };
    if feat & 0xE0 == 0x40 {
        let i = ((feat >> 1) & 3) as usize;
        let (j, k) = (INC4[i], DEC4[i]);
        if near(j) {
            feat = ((j as i32 | 8) << 2) | pos_bit(loc(k)) | (feat & 2);
        } else if near(k) {
            feat = ((k as i32 | 8) << 2) | (sgnnz(loc(j)) + 1) | (feat & 1);
        }
    }
    if feat & 0xE0 == 0x20 {
        let a = ((feat >> 2) & 3) as usize;
        if near(a) {
            feat =
                (pos_bit(loc(a)) << a) | ((feat & 1) << INC4[a]) | (((feat >> 1) & 1) << DEC4[a]);
        }
    }
    gt.face_cands.clear();
    gt.edge_cands.clear();
    match feat & 0xE0 {
        0x40 => {
            let s = (2 * (feat & 1) - 1) as f32;
            let row = box_row(&b, ((feat >> 1) & 3) as usize);
            gt.face_cands.push(FaceCandidate {
                n: Vec3::new(s * row.x, s * row.y, s * row.z),
                i_prim: 0,
                i_feature: feat,
            });
        }
        0x20 => {
            let a = ((feat >> 2) & 3) as usize;
            let (j, k) = (INC4[a], DEC4[a]);
            let s0 = (2 * (feat & 1) - 1) as f32;
            let r0 = box_row(&b, j);
            let n0 = Vec3::new(s0 * r0.x, s0 * r0.y, s0 * r0.z);
            let s1 = ((feat & 2) - 1) as f32;
            let r1 = box_row(&b, k);
            let n1 = Vec3::new(r1.x * s1, r1.y * s1, r1.z * s1);
            gt.face_cands.push(FaceCandidate {
                n: n0,
                i_prim: 0,
                i_feature: (feat & 1) | (2 * (j as i32 | 0x20)),
            });
            gt.face_cands.push(FaceCandidate {
                n: n1,
                i_prim: 0,
                i_feature: ((feat >> 1) & 1) | (2 * (k as i32 | 0x20)),
            });
            gt.edge_cands.push(EdgeCandidate {
                dir: box_row(&b, a),
                n: [-n0, -n1],
                i_prim: 0,
                i_feature: feat,
            });
        }
        _ => {
            for i in 0..3usize {
                let bit = (feat >> i) & 1;
                let s = (2 * bit - 1) as f32;
                let row = box_row(&b, i);
                let n = Vec3::new(row.x * s, row.y * s, row.z * s);
                gt.face_cands.push(FaceCandidate {
                    n,
                    i_prim: 0,
                    i_feature: bit | (2 * (i as i32 | 0x20)),
                });
                let (j, k) = (INC4[i], DEC4[i]);
                let bj = (feat >> j) & 1;
                let bk = (feat >> k) & 1;
                let sj = (1 - 2 * bj) as f32;
                let sk = (1 - 2 * bk) as f32;
                let rj = box_row(&b, j);
                let rk = box_row(&b, k);
                gt.edge_cands.push(EdgeCandidate {
                    dir: n,
                    n: [
                        Vec3::new(rj.x * sj, rj.y * sj, rj.z * sj),
                        Vec3::new(rk.x * sk, rk.y * sk, rk.z * sk),
                    ],
                    i_prim: 0,
                    i_feature: bj | (2 * (bk | (2 * (i as i32 | 8)))),
                });
            }
        }
    }
    1
}

/// `n1 = (c − pt) × axis` as the cylinder/capsule slots 57 compute it, component by component.
fn side_normal(c: &CylinderPrim, pt: Vec3) -> Vec3 {
    let a = c.axis;
    let e = Vec3::new(c.center.x - pt.x, c.center.y - pt.y, c.center.z - pt.z);
    Vec3::new(
        a.z * e.y - a.y * e.z,
        a.x * e.z - a.z * e.x,
        a.y * e.x - a.x * e.y,
    )
}

/// `(|(pt − c) × axis|² − r², h = (pt − c)·axis)` with the slot's association:
/// `((cy² + cx²) + cz²) − r·r` where `cx = az·dy − ay·dz`, `cy = ax·dz − az·dx`,
/// `cz = ay·dx − ax·dy`.
fn side_test(c: &CylinderPrim, pt: Vec3) -> (f32, f32) {
    let a = c.axis;
    let d = pt - c.center;
    let cx = a.z * d.y - a.y * d.z;
    let cy = a.x * d.z - a.z * d.x;
    let cz = a.y * d.x - a.x * d.y;
    let rr = ((cy * cy + cx * cx) + cz * cz) - c.r * c.r;
    let h = (d.y * a.y + a.x * d.x) + d.z * a.z;
    (rr, h)
}

/// Cylinder slot 57 (0x35142410).
///
/// After the optional re-transform and `ids[0] = -1`: unless the feature is already an edge,
/// a point within `m_minVtxDist` (geometry `+16`) of the rim — `||(pt−c)×a|² − r²| < mvd²` and
/// `||h| − hh| < mvd` — turns the feature into the rim edge `0x20 | isnonneg(h)`. Then
/// * edge or any code `> 0x40` (signed): one face candidate, the cap `v = (f & 3) − (f >> 6)`
///   with normal `axis·(2v − 1)` and code `(v + 1) | 0x40`;
/// * edge or exactly `0x40` (the side): one edge candidate, direction `axis`, normals
///   `±(c − pt) × axis`, code = the feature.
pub fn cylinder_get_unprojection_candidates(
    d: &CylinderData,
    capsule: bool,
    i_side: i32,
    contact: &UnprojResult,
    prim: &mut Primitive,
    gt: &mut GeomUnderTest,
) -> i32 {
    retransform_cylinder(d, capsule, prim, gt);
    let c = as_cylinder(prim);
    gt.clear_id0();
    let mut feat = contact.i_feature[(i_side & 1) as usize];
    let mvd = d.hdr.min_vtx_dist;
    if feat & 0xE0 != 0x20 {
        let (rr, h) = side_test(&c, contact.pt);
        if rr.abs() < mvd * mvd && (h.abs() - c.hh).abs() < mvd {
            feat = isnonneg(h) as i32 | 0x20;
        }
    }
    let class = feat & 0xE0;
    gt.face_cands.clear();
    gt.edge_cands.clear();
    if class == 0x20 || feat > 0x40 {
        let v = (feat & 3).wrapping_sub(feat >> 6);
        let s = (2i32.wrapping_mul(v).wrapping_sub(1)) as f32;
        let a = c.axis;
        gt.face_cands.push(FaceCandidate {
            n: Vec3::new(a.x * s, a.y * s, a.z * s),
            i_prim: 0,
            i_feature: v.wrapping_add(1) | 0x40,
        });
    }
    if class == 0x20 || feat == 0x40 {
        let n1 = side_normal(&c, contact.pt);
        gt.edge_cands.push(EdgeCandidate {
            dir: c.axis,
            n: [n1, -n1],
            i_prim: 0,
            i_feature: feat,
        });
    }
    1
}

/// Capsule slot 57 (0x3513F9A0): after the optional re-transform and `ids[0] = -1`, a
/// feature equal to `0x40` (the side) or a point within `m_minVtxDist` of the rim circles
/// (same test as the cylinder) yields one edge candidate (as the cylinder's, code = the raw
/// feature) and no face; otherwise both counts are 0.
pub fn capsule_get_unprojection_candidates(
    d: &CylinderData,
    i_side: i32,
    contact: &UnprojResult,
    prim: &mut Primitive,
    gt: &mut GeomUnderTest,
) -> i32 {
    retransform_cylinder(d, true, prim, gt);
    let c = as_cylinder(prim);
    gt.clear_id0();
    let feat = contact.i_feature[(i_side & 1) as usize];
    let mvd = d.hdr.min_vtx_dist;
    gt.face_cands.clear();
    gt.edge_cands.clear();
    let hit = feat == 0x40 || {
        let (rr, h) = side_test(&c, contact.pt);
        rr.abs() < mvd * mvd && (h.abs() - c.hh).abs() < mvd
    };
    if hit {
        let n1 = side_normal(&c, contact.pt);
        gt.edge_cands.push(EdgeCandidate {
            dir: c.axis,
            n: [n1, -n1],
            i_prim: 0,
            i_feature: feat,
        });
    }
    1
}

fn retransform_cylinder(
    d: &CylinderData,
    capsule: bool,
    prim: &mut Primitive,
    gt: &mut GeomUnderTest,
) {
    if gt.transform_prims != 0 {
        let c = xf_cylinder(&d.prim, &gt.r, gt.offset, gt.scale);
        let p = if capsule {
            Primitive::Capsule(c)
        } else {
            Primitive::Cylinder(c)
        };
        *gt.unproj_prim_mut(p) = p;
        *prim = p;
    }
}

/// Sphere slot 57 (0x351AF570): unconditionally redirects `*prim` to the `+296` buffer and
/// fills it with the sphere under the gtest transform; `ids[0] = -1`; both counts 0 (the
/// sphere has no candidate buffers). Returns 1.
pub fn sphere_get_unprojection_candidates(
    g: &SphereGeom,
    prim: &mut Primitive,
    gt: &mut GeomUnderTest,
) -> i32 {
    let s = Primitive::Sphere(xf_sphere(&g.prim, &gt.r, gt.offset, gt.scale));
    *gt.unproj_prim_mut(s) = s;
    *prim = s;
    gt.clear_id0();
    gt.face_cands.clear();
    gt.edge_cands.clear();
    1
}

/// Ray slot 57 (0x3519A2F0): while `+160` is set the ray is re-transformed into the `+296`
/// buffer, but `*prim` is **not** redirected (unlike the other classes); `ids[0] = -1`; both
/// counts 0. Returns 1.
pub fn ray_get_unprojection_candidates(g: &RayGeom, gt: &mut GeomUnderTest) -> i32 {
    if gt.transform_prims != 0 {
        let r = Primitive::Ray(xf_ray(&g.prim, &gt.r, gt.offset, gt.scale));
        *gt.unproj_prim_mut(r) = r;
    }
    gt.clear_id0();
    gt.face_cands.clear();
    gt.edge_cands.clear();
    1
}

/// The `CGeometry` base slot 57 (0x35037720): both counts 0 (`*(u64*)(gtest+352) = 0`),
/// returns 0.
pub fn base_get_unprojection_candidates(gt: &mut GeomUnderTest) -> i32 {
    gt.face_cands.clear();
    gt.edge_cands.clear();
    0
}
