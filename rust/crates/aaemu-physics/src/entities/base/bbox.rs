//! `CPhysicalEntity::ComputeBBox` (slot 75, 0x35054080; x86 0x3505F880).

use super::{Coords, EntityKind};
use crate::math::{Mat33, Vec3};
use crate::world::{EntIdx, World};

/// The "infinite" clamp constant `9.9999999e14` (bits 0x58635FA9).
const BIG: f32 = crate::math::BIG_F32;

/// Slot 75 `ComputeBBox` through the vtable: living (0x35026BC0) and particle (0x35040F10)
/// entities override it; everything else uses [`base_compute_bbox`].
pub fn compute_bbox(
    w: &mut World,
    e: EntIdx,
    flags: u32,
    ipart: i32,
    coords: Option<Coords>,
) -> [Vec3; 2] {
    match w.ent(e).kind {
        EntityKind::Living(_) => crate::entities::living::compute_bbox(w, e, flags, ipart, coords),
        EntityKind::Particle(_) => {
            crate::entities::particle::compute_bbox(w, e, flags, ipart, coords)
        }
        _ => base_compute_bbox(w, e, flags, ipart, coords),
    }
}

/// `CPhysicalEntity::ComputeBBox` (slot 75, 0x35054080): the world bbox of the entity at its new
/// coords (`*(entity+200)`), or of part `ipart` only (`-1` = all parts).
///
/// * `flags & 1`: an entity without parts still runs the part loop once (reads part 0 of the
///   static dummy part array); without parts and without the flag the box is the point
///   `pos`.
/// * `flags & 2`: the per-part world boxes are stored into the parts (`+32/+44`).
///
/// With fewer than 10 parts or a non-living entity each part contributes the oriented box of
/// its geometry's `GetBBox` (and of the proxy geometry if different); living entities with
/// ≥ 10 parts use bounding spheres. `coords` overrides the entity's new-coords frame (the
/// binary points `entity+200` at a stack frame for that).
pub fn base_compute_bbox(
    w: &mut World,
    e: EntIdx,
    flags: u32,
    ipart: i32,
    coords: Option<Coords>,
) -> [Vec3; 2] {
    let ent = w.ent(e);
    let n_parts = ent.parts.len() as i32;
    let n = n_parts + (flags & 1) as i32;
    let c = coords.unwrap_or_else(|| ent.new_coords());
    if n == 0 {
        return [c.pos, c.pos];
    }
    let sc = ent.sim_class();
    let mut mn = [BIG; 3]; // v128 (x), v123 (y), v175 (z)
    let mut mx = [-BIG; 3]; // v126 (x), v127 (y), v125 (z)
    let mut part_boxes: Vec<(usize, [Vec3; 2])> = Vec::new();
    if (n_parts - 10) >> 31 != 0 || sc != 4 {
        for i in 0..n as usize {
            let (pmin, pmax);
            if ipart == -1 || ipart == i as i32 {
                let Some(part) = ent.parts.get(i) else {
                    // the static dummy part has no geometry: "(PE ComputeBBox Error)"
                    let b = [Vec3::splat(BIG), Vec3::splat(-BIG)];
                    let (lo, hi) = (b[0], b[1]);
                    accumulate(&mut mn, &mut mx, lo, hi);
                    continue;
                };
                let qp = part.q;
                let q = c.q.product(qp);
                let r = Mat33::from_quat(q);
                let m = r.m;
                let geoms: Vec<_> = if std::ptr::eq(
                    part.pgeom_proxy.geom.as_ref() as *const _ as *const u8,
                    part.pgeom.geom.as_ref() as *const _ as *const u8,
                ) {
                    vec![part.pgeom_proxy.geom.clone()]
                } else {
                    vec![part.pgeom_proxy.geom.clone(), part.pgeom.geom.clone()]
                };
                let (mut lo, mut hi) = (Vec3::splat(BIG), Vec3::splat(-BIG));
                for g in geoms {
                    let bb = g.bbox();
                    let b = bb.basis.m;
                    let s = bb.size;
                    // |(R·Bᵀ)[i][j]| = |R row i · B row j|
                    let a = |ri: usize, bj: usize, order: u8| -> f32 {
                        match order {
                            // (B[j][1]·R[i][1] + B[j][0]·R[i][0]) + B[j][2]·R[i][2]
                            0 => ((b[bj][1] * m[ri][1] + b[bj][0] * m[ri][0]) + b[bj][2] * m[ri][2]).abs(),
                            // (R[i][0]·B[j][0] + B[j][1]·R[i][1]) + B[j][2]·R[i][2]
                            _ => ((m[ri][0] * b[bj][0] + b[bj][1] * m[ri][1]) + b[bj][2] * m[ri][2]).abs(),
                        }
                    };
                    let ps = part.scale;
                    // x: ((sy·|a01| + sx·|a00|) + sz·|a02|)·s
                    let ex = ((s.y * a(0, 1, 1) + s.x * a(0, 0, 0)) + s.z * a(0, 2, 1)) * ps;
                    // y: ((|a10|·sx + sy·|a11|) + sz·|a12|)·s
                    let ey = ((a(1, 0, 1) * s.x + s.y * a(1, 1, 0)) + s.z * a(1, 2, 0)) * ps;
                    // z: ((|a20|·sx + sy·|a21|) + sz·|a22|)·s
                    let ez = ((a(2, 0, 1) * s.x + s.y * a(2, 1, 0)) + s.z * a(2, 2, 0)) * ps;
                    // centre: q_ent·(q_part·c·s + p_part) + p_ent
                    let ctr = rotate_inline(qp, bb.center);
                    let p1 = Vec3::new(ctr.x * ps + part.pos.x, ctr.y * ps + part.pos.y, ctr.z * ps + part.pos.z);
                    let p2 = rotate_inline(c.q, p1);
                    let cw = Vec3::new(p2.x + c.pos.x, p2.y + c.pos.y, p2.z + c.pos.z);
                    let (l, h) = (
                        Vec3::new(cw.x - ex, cw.y - ey, cw.z - ez),
                        Vec3::new(cw.x + ex, cw.y + ey, cw.z + ez),
                    );
                    if lo.z >= l.z {
                        lo.z = l.z;
                    }
                    if lo.y >= l.y {
                        lo.y = l.y;
                    }
                    if lo.x >= l.x {
                        lo.x = l.x;
                    }
                    if hi.z <= h.z {
                        hi.z = h.z;
                    }
                    if hi.y <= h.y {
                        hi.y = h.y;
                    }
                    if hi.x <= h.x {
                        hi.x = h.x;
                    }
                }
                if flags & 2 != 0 {
                    part_boxes.push((i, [lo, hi]));
                }
                pmin = lo;
                pmax = hi;
            } else {
                let part = &ent.parts[i];
                pmin = part.bbox[0];
                pmax = part.bbox[1];
            }
            accumulate(&mut mn, &mut mx, pmin, pmax);
        }
    } else {
        // living entity with >= 10 parts: bounding spheres (not reached by the ported classes)
        for i in 0..n as usize {
            let Some(part) = ent.parts.get(i) else {
                continue;
            };
            let bb = part.pgeom.geom.bbox();
            let r0 = max3(bb.size);
            let p = part.q.rotate(bb.center);
            let p1 = Vec3::new(p.x + part.pos.x, p.y + part.pos.y, p.z + part.pos.z);
            let p2 = c.q.rotate(p1);
            let cw = Vec3::new(c.pos.x + p2.x, p2.y + c.pos.y, p2.z + c.pos.z);
            let r = r0 * part.scale;
            let lo = Vec3::new(cw.x - r, cw.y - r, cw.z - r);
            let hi = Vec3::new(cw.x + r, cw.y + r, cw.z + r);
            if mn[2] >= lo.z {
                mn[2] = lo.z;
            }
            if mn[1] >= lo.y {
                mn[1] = lo.y;
            }
            if mn[0] >= lo.x {
                mn[0] = lo.x;
            }
            if mx[2] <= hi.z {
                mx[2] = hi.z;
            }
            if mx[1] <= hi.y {
                mx[1] = hi.y;
            }
            if mx[0] <= hi.x {
                mx[0] = hi.x;
            }
            if flags & 2 != 0 {
                part_boxes.push((i, [lo, hi]));
            }
        }
    }
    let ent = w.ent_mut(e);
    for (i, b) in part_boxes {
        let p = &mut ent.parts[i];
        match &mut p.bbox_tmp {
            Some(t) => *t = b,
            None => p.bbox = b,
        }
    }
    if let EntityKind::Area(_) = ent.kind {
        unreachable!("areas have their own bbox code");
    }
    [Vec3::new(mn[0], mn[1], mn[2]), Vec3::new(mx[0], mx[1], mx[2])]
}

/// The per-part min/max merge (`if (acc >= v) acc = v` / `if (acc <= v) acc = v`).
#[inline]
fn accumulate(mn: &mut [f32; 3], mx: &mut [f32; 3], lo: Vec3, hi: Vec3) {
    if mn[2] >= lo.z {
        mn[2] = lo.z;
    }
    if mn[1] >= lo.y {
        mn[1] = lo.y;
    }
    if mn[0] >= lo.x {
        mn[0] = lo.x;
    }
    if mx[2] <= hi.z {
        mx[2] = hi.z;
    }
    if mx[1] <= hi.y {
        mx[1] = hi.y;
    }
    if mx[0] <= hi.x {
        mx[0] = hi.x;
    }
}

/// The inlined quaternion rotation of ComputeBBox: identical products to
/// [`crate::math::Quat::rotate`] with the final sum written `u + (u + v)`, which is the same
/// IEEE result (`a + b` is commutative).
#[inline]
fn rotate_inline(q: crate::math::Quat, v: Vec3) -> Vec3 {
    q.rotate(v)
}

#[inline]
fn max3(s: Vec3) -> f32 {
    let a = ((s.x - s.y).abs() + (s.x + s.y)) * 0.5;
    ((a - s.z).abs() + (a + s.z)) * 0.5
}
