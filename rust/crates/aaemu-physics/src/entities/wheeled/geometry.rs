//! Slot 11 `AddGeometry` (0x3510BBB0) of `CWheeledVehicleEntity` and the suspension setup
//! `sub_3510B1B0` ([`auto_suspension`]).
//!
//! Slot 12 `RemoveGeometry` (0x351072F0: shift the wheel records down, re-link the wheel
//! parts' new-coords pointers, `+500 = +11156`, then `CRigidEntity::RemoveGeometry`) is not
//! ported: the port has no `RemoveGeometry` of the base / rigid classes (entity slot 12, queue
//! op 3) for it to wrap.

// The arithmetic is written in the binary's operand order and with its single-precision
// constants (`a = b + a`, `x * -1.0`, `3.1415927`, NaN-aware negated comparisons).
#![allow(
    clippy::assign_op_pattern,
    clippy::neg_multiply,
    clippy::excessive_precision,
    clippy::approx_constant,
    clippy::neg_cmp_op_on_partial_ord,
    clippy::too_many_lines
)]
use super::{n_wheels, wd, wm, MAX_WHEELS};
use crate::entities::interface::{rd_i32, rd_u32, wr_i32, wr_u32};
use crate::geometry::PhysGeometry;
use crate::math::{Mat33, Quat, Vec3};
use crate::params::{GeomParams, PeStruct};
use crate::sentinel::{is_unused, UNUSED_F32_BITS, UNUSED_I32 as UI};
use crate::world::{EntIdx, World};
use std::sync::Arc;

#[inline]
fn rd_f(b: &[u8], o: usize) -> f32 {
    f32::from_bits(rd_u32(b, o))
}

/// `sub_35110840`: a plain `pe_geomparams` image (type 0) as a car geomparams image (type 1,
/// 184 bytes): density, mass, pos, q, flags, collider flags, surface, the matrix / material
/// pointers and +80/+104 are copied; scale becomes 1, minContactDist unused, `bDriving` unused
/// (a hull part), `bCanBrake` 1, the wheel fields unused.
#[must_use]
pub fn car_geomparams(a2: &[u8]) -> Vec<u8> {
    let mut a1 = vec![0u8; 512];
    let cp = |a1: &mut [u8], o: usize, n: usize| a1[o..o + n].copy_from_slice(&a2[o..o + n]);
    wr_u32(&mut a1, 36, 1.0f32.to_bits());
    wr_i32(&mut a1, 68, 102_399);
    wr_i32(&mut a1, 72, 1);
    wr_i32(&mut a1, 64, UI);
    wr_i32(&mut a1, 80, UI);
    wr_i32(&mut a1, 108, 1);
    wr_u32(&mut a1, 40, 1.0f32.to_bits());
    wr_u32(&mut a1, 76, UNUSED_F32_BITS);
    wr_i32(&mut a1, 0, 1);
    cp(&mut a1, 4, 36); // +4 … +36: density, mass, pos, q
    cp(&mut a1, 64, 4);
    cp(&mut a1, 80, 4);
    cp(&mut a1, 88, 8);
    cp(&mut a1, 96, 8);
    cp(&mut a1, 104, 4);
    cp(&mut a1, 48, 8);
    cp(&mut a1, 56, 8);
    cp(&mut a1, 68, 4);
    let v2 = rd_i32(a2, 72);
    wr_i32(&mut a1, 112, UI);
    wr_i32(&mut a1, 72, v2);
    for o in [160, 164, 176, 168, 180, 172] {
        wr_u32(&mut a1, o, UNUSED_F32_BITS);
    }
    wr_i32(&mut a1, 124, UI);
    wr_i32(&mut a1, 120, 1);
    a1
}

/// Slot 11 `AddGeometry(pgeom, params, id)` 0x3510BBB0 with a raw geomparams image.
///
/// * Not a car image (`+0 != 1`): converted by [`car_geomparams`] and added as a hull part.
/// * `bDriving` (`+112`) set: a wheel. Its density (`mass / V`, or the density) must be > 0
///   (else "trying to add a weightless wheel", -1); it is added to the rigid body as a
///   **massless** part with collider flags 0 and part flag `0x40000000`.
/// * `CRigidEntity::AddGeometry`; the new part gets flag `0x400000`.
/// * A hull part (`bDriving < 0`, or more than 18 wheels) is moved in front of the wheel parts
///   (the wheel parts' new-coords pointers re-linked), `+11156` counts it, and the suspension
///   is recomputed ([`auto_suspension`]).
/// * A wheel fills its record: driving / axle / brake / ray-cast flags, the suspension pivot
///   and lengths, the initial pose, the friction terms (defaults for unused fields), the
///   radius and half width (the bbox axis whose half size differs most from the other two is
///   the axle), `1/(density·Iy)`, the effective mass at the pivot (`1 / (rᵀ·Iinv·r + Minv)`
///   in the body frame, when `Minv > 0`), and the spring (stiffness 0 → [`auto_suspension`]).
///   The wheel part's new-coords pointer is set to the record, which copies the part's pose,
///   scale and box.
///
/// `+500` (the rigid per-part box threshold) is set to `+11156`.
pub fn add_geometry(w: &mut World, e: EntIdx, g: &Arc<PhysGeometry>, buf: &[u8], id: i32) -> i32 {
    if rd_i32(buf, 0) != 1 {
        let img = car_geomparams(buf);
        return add_geometry(w, e, g, &img, id);
    }
    let mut a3 = buf.to_vec();
    if a3.len() < 184 {
        a3.resize(184, 0);
    }
    let v10 = rd_f(&a3, 8);
    let v11 = if v10 == 0.0 {
        rd_f(&a3, 4)
    } else {
        v10 / g.volume
    };
    let v12 = rd_u32(&a3, 68);
    let v102 = rd_u32(&a3, 72);
    if rd_i32(&a3, 112) != UI {
        if v11 <= 0.0 {
            // "CWheeledVehicleEntity::AddGeometry: trying to add a weightless wheel"
            return -1;
        }
        wr_u32(&mut a3, 72, 0);
        wr_u32(&mut a3, 4, 0);
        wr_u32(&mut a3, 8, 0);
        wr_u32(&mut a3, 68, v12 | 0x4000_0000);
    }
    let v13 = w.ent(e).parts.len() as i32;
    let gp = GeomParams::read(&a3);
    let result = crate::entities::base::geometry::rigid_add_geometry(w, e, g, &gp, id);
    if result < 0 {
        return result;
    }
    if let Some(p) = w.ent_mut(e).parts.last_mut() {
        p.flags |= 0x40_0000;
    }
    let v15 = rd_i32(&a3, 112);
    let nw = n_wheels(w, e);
    if v15 < 0 || nw > MAX_WHEELS as i32 {
        let n_parts = w.ent(e).parts.len() as i32;
        if n_parts > v13 {
            let n_hull = wd(w, e).n_hull;
            if n_hull < n_parts - 1 {
                let ent = w.ent_mut(e);
                let last = ent.parts.pop().expect("part");
                ent.parts.insert(n_hull as usize, last);
                for k in (n_hull + 1) as usize..ent.parts.len() {
                    ent.parts[k].coords_wheel = Some(k - n_hull as usize - 1);
                }
                ent.parts[n_hull as usize].coords_wheel = None;
            }
            wm(w, e).n_hull += 1;
            auto_suspension(w, e);
        }
    } else {
        add_wheel(w, e, g, &a3, v11, v12, v102);
    }
    let n_hull = wd(w, e).n_hull;
    w.ent_mut(e).rigid_mut().expect("rigid").part_bbox_threshold = n_hull;
    result
}

/// The wheel record of a new wheel part (the last part).
fn add_wheel(
    w: &mut World,
    e: EntIdx,
    g: &Arc<PhysGeometry>,
    a3: &[u8],
    v11: f32,
    v12: u32,
    v102: u32,
) {
    let nw = n_wheels(w, e);
    let v17 = (nw - 1) as usize;
    let f = |o: usize| rd_f(a3, o);
    let or_default = |o: usize, d: f32| {
        let x = rd_f(a3, o);
        if is_unused(x) {
            d
        } else {
            x
        }
    };
    let ent_pos = w.ent(e).pos;
    let ent_q = w.ent(e).q;
    let body = *w.bodies.get(w.ent(e).rigid().expect("rigid").body);
    let bb = g.geom.bbox();
    {
        let v15 = rd_i32(a3, 112);
        let wh = &mut wm(w, e).wheels[v17];
        wh.flags = (wh.flags & !1) | (v15 as u32 & 1);
        wh.axle = rd_i32(a3, 116);
        wh.flags = (wh.flags & 0xFFFF_FFF9) | (2 * (rd_u32(a3, 120) & 1));
        wh.pivot = Vec3::new(f(128), f(132), f(136));
        wh.len_max = f(140);
        let v20 = f(144);
        wh.len_rest = v20;
        wh.len = v20;
        wh.len_min_off = f(148);
        let mut v21 = rd_i32(a3, 124);
        wh.brake_impulse = 0.0;
        wh.w_prev = 0.0;
        wh.steer = 0.0;
        wh.angle = 0.0;
        wh.w = 0.0;
        wh.f216 = 0.0;
        wh.brake_torque = 0.0;
        wh.f232 = 0.0;
        if v21 == UI {
            v21 = 0;
        }
        wh.flags = (wh.flags & !8) | (8 * (v21 as u32 & 1));
        let q = Quat::new(f(24), f(28), f(32), f(36));
        let pos = Vec3::new(f(12), f(16), f(20));
        wh.q0 = q;
        wh.pos0 = pos;
        let v22 = q.rotate(g.origin);
        wh.center0 = Vec3::new(v22.x + pos.x, v22.y + pos.y, v22.z + pos.z);
        wh.flags &= 0xFFFF_FFCF;
        wh.min_friction = or_default(160, 0.0);
        wh.max_friction = or_default(164, 1.0);
        wh.f156 = or_default(168, 1.0);
        let v28 = rd_f(a3, 180);
        wh.slip_angle = if is_unused(v28) {
            0.174_532_94
        } else {
            ((v28 - 0.001).abs() + (v28 + 0.001)) * 0.5
        };
        wh.f164 = or_default(176, 0.0);
        wh.f168 = or_default(172, 1.0);
        wh.len_min_off = f(148);
        wh.flags &= !0x40;
        wh.drive_scale = 1.0;
        wh.contact_ent = EntIdx::NONE;
        wh.n = Vec3::new(0.0, 0.0, 1.0);
        wh.pt = Vec3::ZERO;
        wh.part_flags_collider = v102;
        wh.part_flags = v12;
        wh.k = 0.0;
        wh.c = 0.0;
        wh.event = None;
        // the radius is the half size of the axis whose half size differs most from the
        // other two (`min(|s_i − s_next|, |s_i − s_prev|)` maximal); the half width is the
        // other one
        let s = [bb.size.x, bb.size.y, bb.size.z];
        let (next, prev) = ([1usize, 2, 0], [2usize, 0, 1]);
        let mut v33 = -1.0f32;
        for i in 0..3 {
            let a = (s[i] - s[prev[i]]).abs();
            let r = s[next[i]];
            let b = (s[i] - r).abs();
            let m = ((b + a) - (b - a).abs()) * 0.5;
            if m > v33 {
                wh.r = r;
                wh.width = s[i];
                v33 = m;
            }
        }
        let v47 = 1.0 / wh.r;
        wh.inv_inertia = 1.0 / (v11 * g.inertia.z);
        wh.r_inv = v47;
    }
    // the contact record's friction (`wheel+460`) and impulse (`wheel+484`)
    {
        let c = wd(w, e).wheels[v17].contact;
        let rec = w.contacts.get_mut(c);
        rec.friction = 0.0;
        rec.pn = 0.0;
    }
    // effective mass at the pivot in the body frame
    let pivot = wd(w, e).wheels[v17].pivot;
    let v54 = (pivot.x - body.pos.x) + ent_pos.x;
    let v55 = (pivot.y - body.pos.y) + ent_pos.y;
    let qrel = ent_q.conj().product(body.q);
    let r = Mat33::from_quat(qrel);
    let rt = r.transpose();
    let ib = body.ibody_inv;
    let m = r.m;
    let v93 = Mat33 {
        m: [
            [m[0][0] * ib.x, m[0][1] * ib.y, m[0][2] * ib.z],
            [m[1][0] * ib.x, m[1][1] * ib.y, m[1][2] * ib.z],
            [m[2][0] * ib.x, m[2][1] * ib.y, m[2][2] * ib.z],
        ],
    };
    let v68 = v93.mul_mat(&rt).m;
    let v69 = body.minv;
    if v69 > 0.0 {
        let a = ((v68[0][0] * v55 + (-v54) * v68[0][1]) + v68[0][2] * 0.0) * v55;
        let b = ((v68[1][0] * v55 + (-v54) * v68[1][1]) + v68[1][2] * 0.0) * v54;
        wm(w, e).wheels[v17].mass_share = 1.0 / ((a - b) + v69);
    }
    {
        let wh = &mut wm(w, e).wheels[v17];
        wh.c0 = f(156);
    }
    let v70 = f(152);
    if v70 == 0.0 {
        auto_suspension(w, e);
    } else {
        let wh = &mut wm(w, e).wheels[v17];
        wh.k = v70;
        wh.c = f(156);
    }
    let n_hull = wd(w, e).n_hull as usize;
    let pi = v17 + n_hull;
    let part = w.ent(e).parts[pi].clone();
    w.ent_mut(e).parts[pi].coords_wheel = Some(v17);
    let wh = &mut wm(w, e).wheels[v17];
    wh.flags &= !0x10;
    wh.pos = part.pos;
    wh.q = part.q;
    wh.scale = part.scale;
    wh.bbox = part.bbox;
    wh.vslip = Vec3::ZERO;
}

/// `sub_3510B1B0` (only with `Minv > 0`): per wheel the effective mass at the pivot
/// (`1 / ((r × …)·Iinv·… + Minv)` with `r` the pivot relative to the body offset); the
/// stabilizer partners (the next wheel on the same axle; mirror-symmetric pairs — `|Δy|` and
/// `|x + x'|` below 2 % of the largest bbox size — get the first wheel's pivot and centre
/// mirrored in `x`); then the static load split between the wheels behind and in front of the
/// CoM: `k·(lenMax − rest)` per side so that the springs carry `M·g` with no pitch moment
/// (`k = −m_share·g` when one side is empty or the sums don't separate), finally divided by
/// the compression `lenMax − rest`, and the damping of wheels with `c0 ≤ 0` as a fraction of
/// critical damping `−sqrt(4·k·m_share)·c0`. Wheels with `lenMax ≤ 0` get `k = c = 0`.
pub fn auto_suspension(w: &mut World, e: EntIdx) {
    let rid = w.ent(e).rigid().expect("rigid").body;
    let b = *w.bodies.get(rid);
    if !(b.minv > 0.0) {
        return;
    }
    let ent = w.ent(e);
    let (pos, q, bb) = (ent.pos, ent.q, ent.bbox);
    let gz = ent.rigid().expect("rigid").gravity.z;
    let v101 = Vec3::new(b.pos.x - pos.x, b.pos.y - pos.y, b.pos.z - pos.z);
    let v104 = crate::entities::base::geometry::rotate_inv(q, v101);
    let v12 = bb[1].x - bb[0].x;
    let v13 = bb[1].y - bb[0].y;
    let v14 = (v12 - v13).abs();
    let t = (v14 + (v12 + v13)) * 0.5;
    let dz = bb[1].z - bb[0].z;
    let v99 = (((t - dz).abs() + (t + dz)) * 0.5) * 0.02;
    let qrel = q.conj().product(b.q);
    let r = Mat33::from_quat(qrel).to_array();
    let (ix, iy, iz) = (b.ibody_inv.x, b.ibody_inv.y, b.ibody_inv.z);
    let v23 = ix * r[0];
    let v27 = r[3] * ix;
    let v115 = r[4] * iy;
    let v29 = r[5] * iz;
    let v113 = (((r[1] * iy) * r[1]) + v23 * r[0]) + (r[2] * iz) * r[2];
    let v114 = (((r[1] * iy) * r[4]) + r[3] * v23) + (r[2] * iz) * r[5];
    let v31 = (v115 * r[1]) + v27 * r[0];
    let v107 = (((r[1] * iy) * r[7]) + r[6] * v23) + (r[2] * iz) * r[8];
    let v33 = v27 * r[6];
    let v34 = v31 + v29 * r[2];
    let v36 = v29 * r[8];
    let v37 = ((v115 * r[4]) + (v27 * r[3])) + (v29 * r[5]);
    let nw = n_wheels(w, e);
    let minv = b.minv;
    let off = b.offsfb;
    let m_total = b.m;
    let d = wm(w, e);
    if nw > 0 {
        let v38 = ((v115 * r[7] + v33) + v36) * 0.0;
        let (v40, v41) = (v34, v37);
        for i in 0..nw as usize {
            let wh = &mut d.wheels[i];
            let v42 = wh.pivot.y;
            let v43 = wh.pivot.x;
            let dx = v43 - off.x;
            let dy = v42 - off.y;
            let a = dy * (((-dx) * v114 + dy * v113) + v107 * 0.0);
            let bb2 = (((-dx) * v41 + dy * v40) + v38) * dx;
            wh.mass_share = 1.0 / ((a - bb2) + minv);
        }
    }
    for i in 0..nw.max(0) as usize {
        d.wheels[i].partner = -1;
    }
    let mut i = 0i32;
    while i + 1 < nw {
        let mut j = i + 1;
        let axle = d.wheels[i as usize].axle;
        while j < nw && d.wheels[j as usize].axle != axle {
            j += 1;
        }
        if j < nw {
            let (iu, ju) = (i as usize, j as usize);
            if (d.wheels[iu].pivot.y - d.wheels[ju].pivot.y).abs() < v99 {
                let v56 = d.wheels[iu].pivot.x;
                if (v56 + d.wheels[ju].pivot.x).abs() < v99 {
                    let (py, pz) = (d.wheels[iu].pivot.y, d.wheels[iu].pivot.z);
                    let c = d.wheels[iu].center0;
                    let wj = &mut d.wheels[ju];
                    wj.pivot.y = py;
                    wj.pivot.z = pz;
                    wj.pivot.x = -v56;
                    wj.center0 = Vec3::new(-c.x, c.y, c.z);
                }
            }
            d.wheels[iu].partner = j;
            d.wheels[ju].partner = i;
        }
        i += 1;
    }
    let v60 = v104.y;
    let mut sum = [0.0f32; 2];
    let mut cnt = [0i32; 2];
    for i in 0..nw.max(0) as usize {
        let wh = &d.wheels[i];
        if wh.axle >= 0 {
            let v = wh.pivot.y - v60;
            let k = (v.to_bits() >> 31) as usize;
            cnt[k] += 1;
            sum[k] = v + sum[k];
        }
    }
    if sum[0] - sum[1] <= 0.0 || cnt[0] * cnt[1] <= 0 {
        for i in 0..nw.max(0) as usize {
            let wh = &mut d.wheels[i];
            wh.k = -(wh.mass_share * gz);
        }
    } else {
        let v81 = (m_total * sum[1]) * gz;
        let v82 = gz * m_total;
        let k0 = v81 / ((cnt[1] as f32 * sum[0]) - (cnt[0] as f32 * sum[1]));
        let k1 = ((-v82) - (cnt[0] as f32 * k0)) / cnt[1] as f32;
        let ks = [k0, k1];
        for i in 0..nw.max(0) as usize {
            let wh = &mut d.wheels[i];
            wh.k = ks[((wh.pivot.y - v60).to_bits() >> 31) as usize];
        }
    }
    for i in 0..nw.max(0) as usize {
        let wh = &mut d.wheels[i];
        if wh.len_max <= 0.0 {
            wh.k = 0.0;
            wh.c = 0.0;
        } else {
            let v87 = wh.c0;
            let v88 = wh.k / (wh.len_max - wh.len_rest);
            wh.k = v88;
            if v87 <= 0.0 {
                let v89 = wh.mass_share;
                if v88 * v89 >= 0.0 {
                    wh.c = -(((v88 * 4.0) * v89).sqrt() * v87);
                }
            }
        }
    }
}
