//! `CRigidEntity::Step` (slot 34, 0x350C9DF0) and the pose commit helpers
//! `ComputeBBoxRE` (0x350ABF20), `UpdatePosition` (0x350AC080) and `VerifyExistingContacts`
//! (0x350ADB90).

// NaN-aware comparisons and float literals are written as in the binary (the negated
// comparisons keep its NaN behaviour; the literals are the decompile's single-precision
// constants).
#![allow(clippy::neg_cmp_op_on_partial_ord, clippy::excessive_precision)]

use super::colliders::{check_contact_conflicts, check_for_new_contacts, get_potential_colliders};
use super::register::register_contact_point;
use crate::collision::contact::IntersectionParams;
use crate::collision::prim_intersect::scratch::{a as ar, c, ThreadScratch};
use crate::geometry::GeomWorldData;
use crate::math::{Mat33, Quat, Vec3};
use crate::solver::contact::flags as cf;
use crate::world::{EntIdx, World};

fn rd(w: &World, e: EntIdx) -> &super::RigidData {
    w.ent(e).rigid().expect("rigid")
}
fn rm(w: &mut World, e: EntIdx) -> &mut super::RigidData {
    w.ent_mut(e).rigid_mut().expect("rigid")
}

/// `min(min(a, b), c)` / `max` with the binary's idioms.
#[inline]
fn max_i(a: f32, b: f32) -> f32 {
    ((a - b).abs() + (a + b)) * 0.5
}

/// `sub_350ABF20` ComputeBBoxRE: point the first `min(min(nParts, +500), 2)` parts' new-coords
/// pointers (`part+120`) at temporary copies, then `ComputeBBox(+476, flags, -1)` with flag 2
/// (store per-part boxes into those copies) when fewer than 3 parts are covered. The copies
/// are committed by [`update_position`].
pub fn compute_bbox_re(w: &mut World, e: EntIdx) {
    let (np, thr) = {
        let ent = w.ent(e);
        (ent.parts.len() as i32, rd(w, e).part_bbox_threshold)
    };
    let v5 = thr + ((np - thr) & ((np - thr) >> 31));
    let n = v5 + ((2 - v5) & ((2 - v5) >> 31));
    for i in 0..n.max(0) as usize {
        let p = &mut w.ent_mut(e).parts[i];
        p.bbox_tmp = Some(p.bbox);
    }
    let flags = (((v5 - 3) >> 31) & 2) as u32;
    let bb = crate::entities::base::bbox::compute_bbox(w, e, flags, -1, None);
    rm(w, e).bbox_new = bb;
}

/// `sub_350AC080(ent, nCells)` UpdatePosition: commit the new coords (`+200`) to the entity
/// pose, the parts' coords copies, and the boxes: with more than 2 covered parts the pointers
/// are reset and the bbox recomputed into `+8` with per-part boxes; otherwise the per-part
/// boxes come from the copies and the entity box from `+476`. Subtracts `nCells` from the
/// world grid counter (`+22472`, a lock word — no state in the port).
pub fn update_position(w: &mut World, e: EntIdx, _cells: i32) {
    let nc = w.ent(e).new_coords();
    {
        let ent = w.ent_mut(e);
        ent.pos = nc.pos;
        ent.q = nc.q;
    }
    // P5: wheel parts take their pose from the wheel record their `+120` points at
    wheel_coords_to_parts(w, e, false);
    let (np, thr) = (w.ent(e).parts.len() as i32, rd(w, e).part_bbox_threshold);
    let v = thr + ((np - thr) & ((np - thr) >> 31));
    if v > 2 {
        for p in &mut w.ent_mut(e).parts {
            p.bbox_tmp = None;
        }
        let bb = crate::entities::base::bbox::compute_bbox(w, e, 2, -1, None);
        w.ent_mut(e).bbox = bb;
        wheel_coords_to_parts(w, e, true);
    } else {
        for p in &mut w.ent_mut(e).parts {
            if let Some(b) = p.bbox_tmp.take() {
                p.bbox = b;
            }
        }
        wheel_coords_to_parts(w, e, true);
        let bb = rd(w, e).bbox_new;
        w.ent_mut(e).bbox = bb;
    }
}

/// **P5**: the copies UpdatePosition makes from a wheel part's new coords (the wheel record of
/// a `CWheeledVehicleEntity`, `part+120`): the pose (`boxes == false`) or the box.
fn wheel_coords_to_parts(w: &mut World, e: EntIdx, boxes: bool) {
    let ent = w.ent_mut(e);
    let Some(wd) = ent.rigid().and_then(|r| r.wheeled.as_deref()) else {
        return;
    };
    let recs: Vec<(usize, Vec3, Quat, [Vec3; 2])> = ent
        .parts
        .iter()
        .enumerate()
        .filter_map(|(i, p)| {
            p.coords_wheel.map(|k| {
                let wh = &wd.wheels[k];
                (i, wh.pos, wh.q, wh.bbox)
            })
        })
        .collect();
    for (i, pos, q, b) in recs {
        let p = &mut ent.parts[i];
        if boxes {
            p.bbox = b;
        } else {
            p.pos = pos;
            p.q = q;
        }
    }
}

/// `sub_350ADB90` VerifyExistingContacts: contacts (other than new ones) between two
/// sleeping bodies get flag `0x100000`; all contacts lose `0x200000 | 0xC0` (`&= 0xFFDFFF3F`);
/// flagged contacts are removed and returned to the pool.
pub fn verify_contacts(w: &mut World, e: EntIdx) {
    let mut n_drop = 0;
    let cols = w.ent(e).colliders.clone();
    for (i, &col) in cols.iter().enumerate() {
        let mut k = rd(w, e).collider_contacts[i];
        if k.is_none() {
            continue;
        }
        loop {
            let rec = *w.contacts.get(k);
            if rec.flags & cf::NEW == 0 {
                let a = crate::entities::is_awake(w, col);
                let b = crate::entities::is_awake(w, e);
                if b + a > 0 {
                    w.contacts.get_mut(k).flags |= 0x10_0000;
                    n_drop += 1;
                }
            }
            w.contacts.get_mut(k).flags &= 0xFFDF_FF3F;
            if rec.flags & cf::LAST_OF_COLLIDER != 0 {
                break;
            }
            k = rec.next;
        }
    }
    if n_drop != 0 {
        let head = rd(w, e).contacts_head;
        let mut k = w.contacts.get(head).next;
        while k != head {
            let next = w.contacts.get(k).next;
            if w.contacts.get(k).flags & 0x10_0000 != 0 {
                super::contacts::remove_contact(w, e, k, -1, true);
                w.contacts.free_push_front(k);
            }
            k = next;
        }
    }
}

/// Slot 34 `Step(dt)` (0x350C9DF0) → 1 when the entity's step is finished.
///
/// Order (see `dynamics.md` §2): drop rope colliders (simclass 7); snapshot the body; skip if
/// the full step was already performed or there are no parts; forced dt (`+612`); asleep:
/// refresh potential colliders and keep the box. Awake: integrate the body, apply the pending
/// position correction, clamp, derive the new pose (`q_new = normalise(B.q ⊗ qfb)`,
/// `pos_new = com − q_new·offsfb`), set up the intersection parameters (unprojection along
/// `−gravity`, max unprojection = largest bbox size, pivot = CoM for swept/convex single-part
/// bodies); CCD when `(dt − [counter > 4])·v_thin > 0.71·size_thin`: sweep from the old pose,
/// back up to the first hit (`t + contact_gap/2`), cross-check with beams against
/// non-island colliders (halving momenta and staying put on a hit); detect contacts at the
/// new pose (velocity unprojection off), resolve conflicts, unproject the deepest contact if
/// no other contact opposes it and the depth is below `1.1·dt·vel`, recompute the box and
/// re-detect; register the contact points (area polygons, border points); clear the
/// temporary breakable flags; verify contacts; apply a queued impulse; reposition and commit;
/// buoyancy, air resistance; deferred and external momenta, gravity, constant angular
/// acceleration; update the body twice around the step hook; clamp; snapshot `v`/`w`; the
/// "post-step zeroing" of tiny heavy bodies; counters.
#[allow(clippy::too_many_lines)]
pub fn step(w: &mut World, e: EntIdx, dt: f32) -> bool {
    let tid = w.tls_thread;
    let tidu = tid as usize;
    // rope colliders (simclass 7) are detached; others report collision contacts
    let mut v14 = rd(w, e).n_contacts > 0;
    let mut ropes: Vec<EntIdx> = Vec::new();
    let cols = w.ent(e).colliders.clone();
    for &c in cols.iter().rev() {
        if w.ent(c).id_bits & 0xFF00_0000 == 0x0700_0000 {
            ropes.insert(0, c);
        } else {
            v14 |= super::colliders::has_collision_contacts_with(w, c, e);
        }
    }
    // the chain is built by prepending while walking backwards: forward order
    for c in ropes {
        crate::entities::remove_collider(w, e, c, true);
    }
    let body_id = rd(w, e).body;
    {
        let b = *w.bodies.get(body_id);
        let r = rm(w, e);
        r.prev_pos = b.pos;
        r.prev_q = b.q;
        r.v_sleep = Vec3::ZERO;
        r.w_sleep = Vec3::ZERO;
        r.ground_contact = crate::solver::ContactId::NONE;
        r.water_damping_eff = 0.0;
    }
    let (v17, v18) = (rd(w, e).time_step_full, rd(w, e).time_step_performed);
    if v18 > v17 - 0.001 || w.ent(e).parts.is_empty() {
        return true;
    }
    let v19 = rd(w, e).awake_bits;
    let v20;
    {
        let r = rm(w, e);
        r.min_friction = f32::from_bits(1_036_831_949);
        r.time_step_performed = v18 + dt;
        v20 = r.forced_dt;
        r.forced_dt = 0.0;
        r.last_dt = dt;
    }
    let v23 = dt + (v20 - dt) * (((0.000_000_01f32 - v20).to_bits() >> 31) as f32);
    let fcoll = w.bodies.get(body_id).fcoll;
    let fl2 = (fcoll.x * fcoll.x + fcoll.y * fcoll.y) + fcoll.z * fcoll.z;
    let v25: u32 = u32::from(fl2.abs().to_bits() == 0);
    let v26 = (4u32.wrapping_sub(v25.wrapping_mul((v19 >> 13) & 0xF))) >> 31;
    {
        let ent = w.ent_mut(e);
        ent.flags &= !0x100;
    }
    {
        let r = rm(w, e);
        r.flags2 &= (v19 << 28) | 0xEFFF_FFFF;
    }
    let v27 = rd(w, e).flags2;
    w.ent_mut(e).flags |= if v27 & 0x1000_0000 != 0 { 0x100 } else { 0 };
    let mut v363 = 0u32;
    let mut deep = 0u32; // v444 low bit
    if v19 as u8 == 0 {
        if v27 & 0x3FC_0000 != 0 {
            get_potential_colliders(w, e, 0.0);
        }
        // slot 78 (`vt+0x270`, xmm1 = dt): wheeled vehicles' CheckAdditionalGeometry
        slot78(w, e, v23);
        let bb = w.ent(e).bbox;
        rm(w, e).bbox_new = bb;
    } else {
        {
            let r = rm(w, e);
            r.f_ext = Vec3::ZERO;
            r.t_ext = Vec3::ZERO;
        }
        w.bodies.get_mut(body_id).step(v23);
        {
            let corr = rd(w, e).pos_correction;
            let b = w.bodies.get_mut(body_id);
            b.pos = Vec3::new(corr.x + b.pos.x, corr.y + b.pos.y, corr.z + b.pos.z);
            rm(w, e).pos_correction = Vec3::ZERO;
        }
        super::velocity::cap_velocities(w, e);
        let b = *w.bodies.get(body_id);
        {
            let qn = b.q.product(b.qfb);
            let off = qn.rotate(b.offsfb);
            let r = rm(w, e);
            r.q_new = qn;
            r.pos_new = Vec3::new(b.pos.x - off.x, b.pos.y - off.y, b.pos.z - off.z);
        }
        let mut ip = IntersectionParams::default();
        // (v385 = clamp(0.1, |w|₁·dt, 0.19) is computed and then overwritten)
        let g = rd(w, e).gravity;
        let gl = (g.x * g.x + g.y * g.y) + g.z * g.z;
        let (gx, gy, gz) = if gl <= 9.999_999_7e-21 {
            (0.0, 0.0, 1.0)
        } else {
            let k = 1.0 / gl.sqrt();
            (k * g.x, k * g.y, k * g.z)
        };
        ip.unproj_dir = Vec3::new(-gx, -gy, -gz);
        let mut gwd = GeomWorldData {
            v: b.v,
            w: b.w,
            center_of_mass: b.pos,
            ..GeomWorldData::default()
        };
        let swept_or_convex = |w: &World| -> bool {
            rd(w, e).flags2 & 1 != 0
                || (w.ent(e).parts.len() == 1 && w.ent(e).parts[0].pgeom_proxy.geom.is_convex(0.0))
        };
        if swept_or_convex(w) {
            ip.pt_outside_pivot[0] = b.pos;
        }
        let bb = w.ent(e).bbox;
        let v61 = bb[1].z - bb[0].z;
        let v64 = max_i(bb[1].x - bb[0].x, bb[1].y - bb[0].y);
        let v393 = max_i(v64, v61);
        ip.max_unproj = v393;
        let v354 = w.vars.contact_gap * 0.5;
        ip.i_unprojection_mode = 0;
        ip.no_intersection = true;
        ip.max_surface_gap_angle = 0.061_086_524;
        {
            let r = rm(w, e);
            r.q_new = r.q_new.normalized_xwyz();
        }
        let (ccd_vel, ccd_size) = (rd(w, e).ccd_vel, rd(w, e).ccd_size);
        let mut n_ccd = Vec3::ZERO; // v89..v91
        if (v23 - v26 as f32) * ccd_vel <= ccd_size * 0.709_999_98 {
            if rd(w, e).flags2 & 0x1000_0000 != 0 {
                get_potential_colliders(w, e, v23);
            }
            ip.time_interval = v23;
        } else {
            let saved_pos_new = rd(w, e).pos_new;
            let q_stepped = rd(w, e).q_new;
            {
                let (pos, q) = (w.ent(e).pos, w.ent(e).q);
                let r = rm(w, e);
                r.pos_new = pos;
                r.q_new = q;
            }
            let bbn = crate::entities::base::bbox::compute_bbox(w, e, 0, -1, None);
            rm(w, e).bbox_new = bbn;
            ip.time_interval = v23;
            let sweep = Vec3::new(gwd.v.x * v23, gwd.v.y * v23, gwd.v.z * v23);
            ip.sweep_test = true;
            if (v393 - 1.0).to_bits() >> 31 != 0 {
                w.ent_mut(e).flags |= 0x100;
            }
            let res = check_for_new_contacts(w, e, &mut gwd, &mut ip, sweep, 0, -1);
            if res.i_best < 0 {
                rm(w, e).pos_new = saved_pos_new;
            } else {
                let o = ThreadScratch::contact_off(res.i_best);
                let s = &w.tscratch[tidu];
                // (float)(contact_gap/2 + t), then single precision
                let v78 = (f64::from(v354) + s.rd_d(o)) as f32;
                let d = s.rd_v(o + i64::from(c::DIR));
                n_ccd = s.rd_v(o + i64::from(c::N));
                {
                    let r = rm(w, e);
                    r.pos_new.x -= d.x * v78;
                    r.pos_new.z -= d.z * v78;
                    r.pos_new.y -= d.y * v78;
                }
                let (pn, qn) = (rd(w, e).pos_new, rd(w, e).q_new);
                let off = qn.rotate(b.offsfb);
                let com = Vec3::new(pn.x + off.x, pn.y + off.y, pn.z + off.z);
                w.bodies.get_mut(body_id).pos = com;
                let off2 = q_stepped.rotate(b.offsfb);
                rm(w, e).pos_new = Vec3::new(com.x - off2.x, com.y - off2.y, com.z - off2.z);
                v363 = 1;
                if !w.ent(e).colliders.is_empty() {
                    rm(w, e).min_friction = 3.0;
                }
                if swept_or_convex(w) {
                    ip.pt_outside_pivot[0] = com;
                }
            }
            // beams against colliders outside the island
            let cols = w.ent(e).colliders.clone();
            for col in cols {
                let ce = w.ent(col);
                if (ce.id_bits & 0xFF00_0000) as i32 <= 0x0200_0000
                    && col != e
                    && (ce.group_idx != w.ent(e).group_idx || ce.group_mark != 0)
                {
                    let pn = rd(w, e).pos_new;
                    let pos = w.ent(e).pos;
                    let org = rd(w, e).prev_pos;
                    let dir = Vec3::new(pn.x - pos.x, pn.y - pos.y, pn.z - pos.z);
                    let mut hit = crate::world::queries::rwi::RayHit::default();
                    if w.ray_trace_entity(col, org, dir, &mut hit, None, 3, true) != 0 {
                        let b = w.bodies.get_mut(body_id);
                        b.v = Vec3::new(b.v.x * 0.5, b.v.y * 0.5, b.v.z * 0.5);
                        b.w = Vec3::new(b.w.x * 0.5, b.w.y * 0.5, b.w.z * 0.5);
                        b.p = Vec3::new(b.p.x * 0.5, b.p.y * 0.5, b.p.z * 0.5);
                        b.l = Vec3::new(b.l.x * 0.5, b.l.y * 0.5, b.l.z * 0.5);
                        let pos = w.ent(e).pos;
                        let prev = rd(w, e).prev_pos;
                        rm(w, e).pos_new = pos;
                        w.bodies.get_mut(body_id).pos = prev;
                        break;
                    }
                }
            }
            ip.sweep_test = false;
            rm(w, e).q_new = q_stepped;
            ip.time_interval = v23 * 3.0;
        }
        let bbn = crate::entities::base::bbox::compute_bbox(w, e, 0, -1, None);
        rm(w, e).bbox_new = bbn;
        // slot 78 (`vt+0x270`, xmm1 = dt): wheeled vehicles' CheckAdditionalGeometry
        slot78(w, e, v23);
        ip.vrel_min = 1e10;
        let res = check_for_new_contacts(w, e, &mut gwd, &mut ip, Vec3::ZERO, 0, -1);
        let mut n = res.n;
        let mut i_best = res.i_best;
        if n == 0 && v363 != 0 {
            let b = w.bodies.get_mut(body_id);
            let v116 = ((b.v.x * n_ccd.x + b.v.y * n_ccd.y) + b.v.z * n_ccd.z) * 1.1;
            b.v = Vec3::new(
                b.v.x - v116 * n_ccd.x,
                b.v.y - v116 * n_ccd.y,
                b.v.z - v116 * n_ccd.z,
            );
            let m = b.m;
            b.p = Vec3::new(b.v.x * m, b.v.y * m, b.v.z * m);
        }
        if n <= 8 {
            check_contact_conflicts(w, e, n, tid);
        }
        if i_best < 0 {
            compute_bbox_re(w, e);
        } else {
            let o = ThreadScratch::contact_off(i_best);
            let (mode, cdir, ct, cvel) = {
                let s = &w.tscratch[tidu];
                (
                    s.rd_i(o + i64::from(c::MODE)),
                    s.rd_v(o + i64::from(c::DIR)),
                    s.rd_d(o),
                    s.rd_f(o + i64::from(c::VEL)),
                )
            };
            let head = rd(w, e).contacts_head;
            let mut k = w.contacts.get(head).next;
            while k != head {
                let rec = w.contacts.get(k);
                if mode != 0 {
                    let cr = ip.center_of_rotation;
                    let d = Vec3::new(rec.pt[0].x - cr.x, rec.pt[0].y - cr.y, rec.pt[0].z - cr.z);
                    let x = (cdir.y * d.z) - (cdir.z * d.y);
                    let y = (cdir.z * d.x) - (cdir.x * d.z);
                    let z = (cdir.x * d.y) - (cdir.y * d.x);
                    let l2 = (y * y + x * x) + z * z;
                    if l2 > ip.min_axis_dist * ip.min_axis_dist {
                        let dn = (rec.n.y * y + rec.n.x * x) + rec.n.z * z;
                        if dn.abs() * dn < l2 * -0.001 {
                            break;
                        }
                    }
                } else if (cdir.y * rec.n.y + cdir.x * rec.n.x) + cdir.z * rec.n.z <= -0.001 {
                    break;
                }
                k = rec.next;
            }
            if k == head
                && mode == 0
                && (!v14 || f64::from(v393 * 0.1) < ct)
                && ct < f64::from((v23 * cvel) * 1.1)
            {
                v363 = 1;
                let v140 = (ct - f64::from(v354)) as f32;
                let m = |d: f32| d * v140;
                let r = rm(w, e);
                r.pos_new = Vec3::new(
                    m(cdir.x) + r.pos_new.x,
                    m(cdir.y) + r.pos_new.y,
                    m(cdir.z) + r.pos_new.z,
                );
                let b = w.bodies.get_mut(body_id);
                b.pos = Vec3::new(
                    m(cdir.x) + b.pos.x,
                    m(cdir.y) + b.pos.y,
                    m(cdir.z) + b.pos.z,
                );
            }
            compute_bbox_re(w, e);
            ip.vrel_min = 1e10;
            ip.max_surface_gap_angle = 0.0;
            ip.i_unprojection_mode = 0;
            let res = check_for_new_contacts(w, e, &mut gwd, &mut ip, Vec3::ZERO, 0, -1);
            n = res.n;
            i_best = res.i_best;
        }
        let _ = i_best;
        {
            let r = rm(w, e);
            r.unk1044 = (r.unk1044 & !0xFF) | ((r.unk1044 as u8).wrapping_mul(2) as u32);
        }
        for i in 0..n {
            register_record(w, e, i, v23, tid, v393, v354, &mut deep);
        }
        // clear the temporary 0x1000000 of parts that also have 0x800000
        let cnt = w.ccd_scratch[tidu].count;
        for i in 0..cnt.max(0) as usize {
            let (col, (_, cp)) = (
                w.ccd_scratch[tidu].collider[i],
                w.ccd_scratch[tidu].parts[i],
            );
            let p = &mut w.ent_mut(col).parts[cp as usize];
            if p.flags & 0x180_0000 == 0x180_0000 {
                p.flags &= !0x100_0000;
            }
        }
        finish_step(w, e, v23, v25, v363, deep, tid, v393);
        return finish_ret(w, e);
    }
    // asleep: v393/v363/deep stay 0
    finish_step(w, e, v23, v25, 0, 0, tid, 0.0);
    finish_ret(w, e)
}

/// Step's return value: `(flags2 & 0x800) == 0 | (flags2 bits 13–16 > 3)`.
fn finish_ret(w: &World, e: EntIdx) -> bool {
    let f = rd(w, e).flags2;
    (f & 0x800) == 0 || ((3u32.wrapping_sub((f >> 13) & 0xF)) >> 31) != 0
}

/// Register the contact points of arena record `i` (the loop body of Step's LABEL_68).
#[allow(clippy::too_many_arguments, clippy::too_many_lines)]
fn register_record(
    w: &mut World,
    e: EntIdx,
    i: i32,
    dt: f32,
    tid: i32,
    v393: f32,
    v354: f32,
    deep: &mut u32,
) {
    let tidu = tid as usize;
    let o = ThreadScratch::contact_off(i);
    let t = w.tscratch[tidu].rd_d(o);
    if t < 0.0 {
        return;
    }
    let own_part = w.ccd_scratch[tidu].parts[i as usize].0;
    if w.ent(e).parts[own_part as usize].flags & 0x10_0000 != 0 {
        special_contact(w, e, i, dt, tid);
        return;
    }
    let _ = dt;
    let s = &w.tscratch[tidu];
    let rdi = |k: u32| s.rd_i(o + i64::from(k));
    let inode0 = rdi(c::INODE);
    let inode1 = rdi(c::INODE + 4);
    let v154 = t as f32;
    let dir = s.rd_v(o + i64::from(c::DIR));
    let v162 = dir.x * v154;
    let v371 = dir.y * v154;
    let v355 = dir.z * v154;
    // 1 when both iNode < 0 (`sar` + `neg`)
    let v167: i32 = ((inode0 >> 31) & (inode1 >> 31)).wrapping_neg();
    let iprim0 = rdi(c::IPRIM);
    let iprim1 = rdi(c::IPRIM + 4);
    let ifeat0 = rdi(c::IFEATURE);
    let ifeat1 = rdi(c::IFEATURE + 4);
    let pt = s.rd_v(o + i64::from(c::PT));
    let pa = s.rd_q(o + i64::from(c::AREA));
    let area_off = s.off_of(pa);
    let npt = area_off.map_or(0, |ao| s.rd_i(ao + i64::from(ar::NPT)));
    let mut axis = Vec3::ZERO; // v360..v362
    let mut an = Vec3::ZERO; // v159, v158, v157
    let mut v156 = 0.0f32;
    let mut v161: u32 = 0;
    if let (Some(ao), true) = (area_off, npt >= 2) {
        let s = &w.tscratch[tidu];
        let nn = s.rd_v(o + i64::from(c::N));
        an = s.rd_v(ao + i64::from(ar::N));
        axis = Vec3::new(
            (nn.z * an.y) - (nn.y * an.z),
            (nn.x * an.z) - (an.x * nn.z),
            (nn.y * an.x) - (nn.x * an.y),
        );
        let ppt = s.rd_q(ao + i64::from(ar::PT));
        let pts: Vec<Vec3> = (0..npt)
            .map(|k| s.rd_v_at(ppt.wrapping_add(12 * k as u64)))
            .collect();
        let pprim0 = s.rd_q(ao + i64::from(ar::PRIM0));
        let pprim1 = s.rd_q(ao + i64::from(ar::PRIM1));
        let pfeat0 = s.rd_q(ao + i64::from(ar::FEAT0));
        let pfeat1 = s.rd_q(ao + i64::from(ar::FEAT1));
        let ids: Vec<[i32; 4]> = (0..npt)
            .map(|k| {
                let q = 4 * k as u64;
                [
                    s.rd_at(pprim0.wrapping_add(q)) as i32,
                    s.rd_at(pfeat0.wrapping_add(q)) as i32,
                    s.rd_at(pprim1.wrapping_add(q)) as i32,
                    s.rd_at(pfeat1.wrapping_add(q)) as i32,
                ]
            })
            .collect();
        let w_ = &mut w.tscratch[tidu];
        w_.wr_v(o + i64::from(c::N), Vec3::new(-dir.x, -dir.y, -dir.z));
        let dot = |q: Vec3| ((q.x - pt.x) * an.x + (q.y - pt.y) * an.y) + (q.z - pt.z) * an.z;
        let mut v177 = v393;
        for q in &pts {
            let d = dot(*q);
            v177 = ((d + v177) - (v177 - d).abs()) * 0.5;
        }
        let mut first_flag = 0x80000u32;
        if v167.wrapping_neg() & npt > 0 {
            for (k, q) in pts.iter().enumerate() {
                let v211 = ((t as f32) - dot(*q)) + v177;
                if v211 > -v354 {
                    let p2 = Vec3::new(q.x - v162, q.y - v371, q.z - v355);
                    let id = ids[k];
                    register_contact_point(
                        w,
                        e,
                        i,
                        p2,
                        id[0],
                        id[1],
                        id[2],
                        id[3],
                        first_flag | 0x104_0040,
                        ((0.001 - v211).abs() + (v211 + 0.001)) * 0.5,
                        tid,
                        axis,
                    );
                    first_flag = 0;
                }
            }
        }
        let s = &w.tscratch[tidu];
        let ctr = s.rd_v(o + i64::from(c::CENTER));
        v156 = dot(ctr) * ((v167 ^ 1) as f32);
        let nb = s.rd_i(o + i64::from(c::NBORDER));
        let v224 = (v167 - 1) & nb;
        let pb = s.rd_q(o + i64::from(c::PTBORDER));
        for k in 0..v224.max(0) {
            let q = s.rd_v_at(pb.wrapping_add(12 * k as u64));
            let d = dot(q);
            v156 = ((d + v156) - (v156 - d).abs()) * 0.5;
        }
        let atype = s.rd_i(area_off.unwrap() + i64::from(ar::TYPE));
        v161 = ((((atype ^ atype.wrapping_sub(1)) as u32) >> 31) & 1) * 0x100_0000;
    } else if v167 != 0 {
        let p2 = Vec3::new(pt.x - v162, pt.y - v154 * dir.y, pt.z - v154 * dir.z);
        register_contact_point(
            w,
            e,
            i,
            p2,
            iprim0,
            ifeat0,
            iprim1,
            ifeat1,
            0x80040,
            v154,
            tid,
            Vec3::ZERO,
        );
    }
    if v167 == 0 {
        let s = &w.tscratch[tidu];
        let ctr = s.rd_v(o + i64::from(c::CENTER));
        let dot = |q: Vec3| ((q.x - pt.x) * an.x + (q.y - pt.y) * an.y) + (q.z - pt.z) * an.z;
        let v259 = ((t as f32) - dot(ctr)) + v156;
        register_contact_point(
            w,
            e,
            i,
            ctr,
            iprim0,
            ifeat0,
            iprim1,
            ifeat1,
            v161 | 0x80040,
            ((0.001 - v259).abs() + (v259 + 0.001)) * 0.5,
            tid,
            axis,
        );
        let s = &w.tscratch[tidu];
        let nb = s.rd_i(o + i64::from(c::NBORDER));
        let pb = s.rd_q(o + i64::from(c::PTBORDER));
        for k in 0..nb.max(0) {
            let s = &w.tscratch[tidu];
            let q = s.rd_v_at(pb.wrapping_add(12 * k as u64));
            let v268 = ((t as f32) - dot(q)) + v156;
            register_contact_point(
                w,
                e,
                i,
                q,
                iprim0,
                ifeat0,
                iprim1,
                ifeat1,
                v161 | 0x40,
                ((0.001 - v268).abs() + (v268 + 0.001)) * 0.5,
                tid,
                axis,
            );
        }
    }
    let v269 = (v393 * 0.5) as f64 - t;
    *deep |= ((v269 as f32).to_bits() >> 31) & 1;
}

/// Step's tail (LABEL_112 onwards).
#[allow(clippy::too_many_arguments, clippy::too_many_lines)]
fn finish_step(
    w: &mut World,
    e: EntIdx,
    v23: f32,
    v25: u32,
    v363: u32,
    deep: u32,
    tid: i32,
    v393: f32,
) {
    verify_contacts(w, e);
    // `+1184`: a net-sync state queued through slot 24 (`action_net_sync`) is applied here
    // by 0x350C9750 (SetParams pos + Action set_velocity, blended towards the target). The
    // port exposes no slot-24 API, so nothing is ever queued.
    let bbn = rd(w, e).bbox_new;
    let cells = w.reposition_entity(e, 1, Some(bbn));
    update_position(w, e, cells);
    let recs = super::buoyancy::gather_areas_n(w, e, v23, 4);
    let nb = recs.len();
    let g = if w.ent(e).colliders.is_empty() {
        rd(w, e).gravity_freefall
    } else {
        rd(w, e).gravity
    };
    super::buoyancy::apply_buoyancy(w, e, v23, Vec3::new(0.0, 0.0, -9.810_000_4), &recs[..nb]);
    if w.ent(e).flags & 8 != 0 {
        super::buoyancy::apply_air_resistance(w, e, v23);
    }
    let body_id = rd(w, e).body;
    let (pd, ld, fext, text, alpha) = {
        let r = rd(w, e);
        (r.p_deferred, r.l_deferred, r.f_ext, r.t_ext, r.ang_accel)
    };
    {
        let b = w.bodies.get_mut(body_id);
        b.p = Vec3::new(pd.x + b.p.x, pd.y + b.p.y, pd.z + b.p.z);
        b.l = Vec3::new(ld.x + b.l.x, ld.y + b.l.y, ld.z + b.l.z);
        let v286 = v23 * b.m;
        b.p = Vec3::new(v286 * g.x + b.p.x, v286 * g.y + b.p.y, v286 * g.z + b.p.z);
        b.p = Vec3::new(
            fext.x * v23 + b.p.x,
            fext.y * v23 + b.p.y,
            fext.z * v23 + b.p.z,
        );
        b.l = Vec3::new(
            text.x * v23 + b.l.x,
            text.y * v23 + b.l.y,
            text.z * v23 + b.l.z,
        );
        let r = Mat33::from_quat(b.q).m;
        let r2 = Mat33::from_quat(b.q).m;
        let ib = b.ibody;
        let (v310, v440, v309) = (ib.x * r2[0][0], ib.y * r2[0][1], ib.z * r2[0][2]);
        let (v313, v311, v314) = (ib.x * r2[1][0], ib.y * r2[1][1], ib.z * r2[1][2]);
        let (v315, v312, v316) = (ib.x * r2[2][0], ib.y * r2[2][1], ib.z * r2[2][2]);
        let m00 = (v310 * r[0][0] + v440 * r[0][1]) + v309 * r[0][2];
        let m01 = (v440 * r[1][1] + v310 * r[1][0]) + v309 * r[1][2];
        let m02 = (v440 * r[2][1] + v310 * r[2][0]) + v309 * r[2][2];
        let m10 = (v311 * r[0][1] + r[0][0] * v313) + v314 * r[0][2];
        let m11 = (v311 * r[1][1] + v313 * r[1][0]) + v314 * r[1][2];
        let m12 = (v311 * r[2][1] + v313 * r[2][0]) + v314 * r[2][2];
        let m20 = (v312 * r[0][1] + r[0][0] * v315) + v316 * r[0][2];
        let m21 = (v312 * r[1][1] + v315 * r[1][0]) + v316 * r[1][2];
        let m22 = (v312 * r[2][1] + v315 * r[2][0]) + v316 * r[2][2];
        let (ax, ay, az) = (alpha.x, alpha.y, alpha.z);
        b.l.x += ((m00 * v23) * ax + ay * (m10 * v23)) + az * (m20 * v23);
        b.l.y += (ay * (m11 * v23) + ax * (m01 * v23)) + az * (m21 * v23);
        b.l.z += (ay * (m12 * v23) + ax * (m02 * v23)) + az * (m22 * v23);
    }
    {
        let r = rm(w, e);
        r.p_deferred = Vec3::ZERO;
        r.l_deferred = Vec3::ZERO;
    }
    w.bodies.get_mut(body_id).update_state();
    // slot 79 (`vt+0x278`, xmm1 = dt): the wheeled vehicles' drivetrain / tyre impulses
    if w.ent(e).wheeled().is_some() {
        crate::entities::wheeled::drive::step_drive(w, e, v23);
    }
    {
        let b = w.bodies.get_mut(body_id);
        b.fcoll = Vec3::ZERO;
        b.tcoll = Vec3::ZERO;
        b.update_state();
    }
    super::velocity::cap_velocities(w, e);
    let b = *w.bodies.get(body_id);
    {
        let r = rm(w, e);
        r.prev_v = b.v;
        r.prev_w = b.w;
    }
    let island_mass = w.island[tid as usize].dt;
    let mut v332 = (((island_mass * 0.001) - b.m).to_bits() >> 31) & deep;
    if v332 & (rd(w, e).flags2 >> 11) & 1 != 0 {
        let lhs = ((((b.w.x * b.w.x + b.w.y * b.w.y) + b.w.z * b.w.z)
            * ((v393 * 0.5) * (v393 * 0.5)))
            + ((b.v.x * b.v.x + b.v.y * b.v.y) + b.v.z * b.v.z))
            * (v23 * v23);
        if lhs >= (v393 * 0.1) * (v393 * 0.1) {
            let b = w.bodies.get_mut(body_id);
            b.p = Vec3::ZERO;
            b.l = Vec3::ZERO;
            b.v = Vec3::ZERO;
            b.w = Vec3::ZERO;
        } else {
            v332 = 0;
        }
    }
    let r = rm(w, e);
    let v333 = r.awake_bits;
    r.flags2 = (r.flags2 & 0xFFFF_E7FF) | ((v332 & 1) << 11);
    let v336 = (v333 & 0xFFFF_E000).wrapping_add(v363 << 13);
    r.awake_bits =
        v333 ^ ((v333 ^ ((0u32.wrapping_sub(8192u32.wrapping_mul(v25))) & v336)) & 0x1E000);
}

#[allow(dead_code)]
fn _unused(_: Quat) {}

/// Slot 78: `CWheeledVehicleEntity::CheckAdditionalGeometry` 0x3510C940 for wheeled vehicles
/// (**P5**), the `nop` stub 0x35037700 for rigid bodies.
fn slot78(w: &mut World, e: EntIdx, dt: f32) {
    if w.ent(e).wheeled().is_some() {
        crate::entities::wheeled::detect::check_additional_geometry(w, e, dt);
    }
}

/// `sub_350BADE0(ent, contacts, i, dt, tid)`: the contact of a ray-mode part (`0x100000`):
/// a spring along the ray. With `+1040 ≤ 3` (driving on the ray) the compression
/// `t` gives an external force `dir·k·t` (`k = |g|·minContactDist/(K⁻¹·(sx+sy+sz)·scale)`,
/// `K⁻¹` the effective mass at the contact) and the critically damped impulse
/// `−2·sqrt(k/K)·v·dt` plus an angular damping `−3.5·dt·L` go to the deferred momenta;
/// otherwise (`+1040 > 3`, resting) the border points become ordinary resting contacts
/// (depth 0.001, `nloc` cleared) and the external force/torque are cleared. `+1040` follows
/// the sign of `t − 0.02·Σsize` (clamped to `[0, 5·[colliders]]`); `+1044` bit 0 is set. A
/// logged collision event is posted for logging entities or parts with `0x200000`.
#[allow(clippy::too_many_lines)]
fn special_contact(w: &mut World, e: EntIdx, i: i32, dt: f32, tid: i32) {
    let tidu = tid as usize;
    let (col, (own_part, cpart)) = {
        let cs = &w.ccd_scratch[tidu];
        (cs.collider[i as usize], cs.parts[i as usize])
    };
    let g = w.gravity();
    let v13 = (g.x * g.x + g.y * g.y) + g.z * g.z;
    let p = w.ent(e).parts[own_part as usize].clone();
    let bb = p.pgeom.geom.bbox();
    let (v97, v98, v99) = (bb.size.x, bb.size.y, bb.size.z);
    let v16 = p.scale;
    let rq = p.q.rotate(p.pgeom.origin);
    let v94 = Vec3::new(
        rq.x * v16 + p.pos.x,
        rq.y * v16 + p.pos.y,
        rq.z * v16 + p.pos.z,
    );
    let (pn, qn) = (rd(w, e).pos_new, rd(w, e).q_new);
    let body_id = rd(w, e).body;
    let b = *w.bodies.get(body_id);
    let v19 = qn.rotate(v94);
    let rx = (v19.x + pn.x) - b.pos.x;
    let ry = (v19.y + pn.y) - b.pos.y;
    let rz = (v19.z + pn.z) - b.pos.z;
    let o = ThreadScratch::contact_off(i);
    let (t, dir) = {
        let s = &w.tscratch[tidu];
        (s.rd_d(o), s.rd_v(o + i64::from(c::DIR)))
    };
    let v25 = (ry * dir.z) - (rz * dir.y);
    let v26 = (rz * dir.x) - (rx * dir.z);
    let v29 = (rx * dir.y) - (ry * dir.x);
    let ii = b.iinv.m;
    let v30 = ((v26 * ii[0][1]) + (v25 * ii[0][0])) + (v29 * ii[0][2]);
    let v31 = ((v26 * ii[1][1]) + (v25 * ii[1][0])) + (v29 * ii[1][2]);
    let v32 = ((v26 * ii[2][1]) + (v25 * ii[2][0])) + (v29 * ii[2][2]);
    let v102 = 1.0
        / ((((((v32 * rx) - (rz * v30)) * dir.y) + (((v31 * rz) - (v32 * ry)) * dir.x))
            + (((ry * v30) - (v31 * rx)) * dir.z))
            + b.minv);
    let v33 = (v13.sqrt() * (v102 * p.min_contact_dist)) / (((v98 + v97) + v99) * p.scale);
    let v34 = (f64::from(v33) * t) as f32;
    let (v35, v36, v37) = (dir.x * v34, dir.y * v34, dir.z * v34);
    {
        let r = rm(w, e);
        r.f_ext.y += v36;
        r.f_ext.x += v35;
        r.f_ext.z += v37;
        r.t_ext.y += (rz * v35) - (v37 * rx);
        r.t_ext.z += (v36 * rx) - (ry * v35);
        r.t_ext.x += (v37 * ry) - (v36 * rz);
    }
    let (wx, wy, wz) = (b.w.x, b.w.y, b.w.z);
    let v45 = ((rz * wy) - (ry * wz)) + b.v.x;
    let v46 = ((wz * rx) - (rz * wx)) + b.v.y;
    if rd(w, e).unk1040 <= 3.0 {
        let v51 = ((v33 * v102).sqrt() * -2.0) * dt;
        let v52 = v46 * v51;
        let v53 = (((ry * wx) - (rx * wy)) + b.v.z) * v51;
        let dx = v51 * v45;
        let r = rm(w, e);
        r.p_deferred.y += v52;
        r.p_deferred.x += dx;
        r.p_deferred.z += v53;
        let k = dt * 3.5;
        r.l_deferred.x += ((v53 * ry) - (v52 * rz)) - (b.l.x * k);
        r.l_deferred.z += ((v52 * rx) - (ry * dx)) - (b.l.z * k);
        r.l_deferred.y += ((rz * dx) - (v53 * rx)) - (b.l.y * k);
    } else {
        let (nb, pb, ip0, if0, ip1, if1) = {
            let s = &w.tscratch[tidu];
            (
                s.rd_i(o + i64::from(c::NBORDER)),
                s.rd_q(o + i64::from(c::PTBORDER)),
                s.rd_i(o + i64::from(c::IPRIM)),
                s.rd_i(o + i64::from(c::IFEATURE)),
                s.rd_i(o + i64::from(c::IPRIM) + 4),
                s.rd_i(o + i64::from(c::IFEATURE) + 4),
            )
        };
        for k in 0..nb.max(0) {
            let pt = {
                let s = &mut w.tscratch[tidu];
                let d = s.rd_v(o + i64::from(c::DIR));
                s.wr_v(o + i64::from(c::N), Vec3::new(-d.x, -d.y, -d.z));
                s.rd_v_at(pb.wrapping_add(12 * k as u64))
            };
            if let Some(ci) =
                register_contact_point(w, e, i, pt, ip0, if0, ip1, if1, 64, 0.001, tid, Vec3::ZERO)
            {
                w.contacts.get_mut(ci).nloc = Vec3::ZERO;
            }
            let r = rm(w, e);
            r.f_ext = Vec3::ZERO;
            r.t_ext = Vec3::ZERO;
        }
    }
    let v57 = ((t - f64::from(((v98 + v97) + v99) * 0.02)) as f32).to_bits() as i32;
    let v58 = (((v57.wrapping_sub(1)) >> 31) + (v57 >> 31) + 1) as f32;
    let x = v58 * dt + rd(w, e).unk1040;
    let v59 = ((-x).abs() + x) * 0.5;
    let v60 = f32::from(u8::from(!w.ent(e).colliders.is_empty()));
    {
        let r = rm(w, e);
        r.unk1044 |= 1;
        r.unk1040 = (((v60 * 5.0) + v59) - ((v60 * 5.0) - v59).abs()) * 0.5;
    }
    let log = (w.ent(e).flags & 0x200_0000 != 0 && rd(w, e).awake_bits & 0x7C0_0000 != 0)
        || p.flags & 0x20_0000 != 0;
    if log {
        let s = &w.tscratch[tidu];
        let ctr = s.rd_v(o + i64::from(c::CENTER));
        let n = s.rd_v(o + i64::from(c::N));
        let (id0, id1) = (
            s.rd_i(o + i64::from(c::ID)),
            s.rd_i(o + i64::from(c::ID) + 4),
        );
        let d = Vec3::new(ctr.x - b.pos.x, ctr.y - b.pos.y, ctr.z - b.pos.z);
        let v0 = Vec3::new(
            ((d.z * b.w.y) - (d.y * b.w.z)) + b.v.x,
            ((b.w.z * d.x) - (d.z * b.w.x)) + b.v.y,
            ((d.y * b.w.x) - (b.w.y * d.x)) + b.v.z,
        );
        let ev = crate::world::events::Event::Collision {
            ent: [e, col],
            pt: ctr,
            n,
            v: [v0, Vec3::ZERO],
            // slot 66 GetMass of the collider part
            mass: [b.m, w.ent(col).parts[cpart as usize].mass],
            partid: [p.id, w.ent(col).parts[cpart as usize].id],
            idmat: [id0 & 0xFFFF, id1 & 0xFFFF],
            penetration: t as f32,
            normimpulse: 0.0,
            radius: 0.0,
        };
        w.post_logged_event(ev);
    }
}
