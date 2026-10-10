//! Contact registration of `CRigidEntity`: `RegisterContactPoint` (0x350B5E10) and the
//! collision-event poster `sub_350AE0D0`.

// NaN-aware comparisons and float literals are written as in the binary (the negated
// comparisons keep its NaN behaviour; the literals are the decompile's single-precision
// constants).
#![allow(clippy::neg_cmp_op_on_partial_ord, clippy::excessive_precision)]

use super::colliders::{body_id_of, body_of};
use crate::collision::prim_intersect::scratch::{c, ThreadScratch};
use crate::entities::base::EntityKind;
use crate::math::Vec3;
use crate::params::ActionImpulse;
use crate::sentinel::{UNUSED_F32, UNUSED_I32};
use crate::solver::contact::flags as cf;
use crate::solver::{Contact, ContactId};
use crate::world::events::Event;
use crate::world::{EntIdx, World};

/// `sub_35098FA0(body, r, K)`: `K += Minv·I − [r]ₓ·Iinv·[r]ₓ` (the collision matrix of one
/// body at offset `r`), with the binary's evaluation order (`sub_35020320` products, the
/// diagonal `Minv` added last).
fn add_collision_matrix(b: &crate::solver::RigidBody, r: Vec3, k: &mut [f32; 9]) {
    let s = [0.0, -r.z, r.y, r.z, 0.0, -r.x, -r.y, r.x, 0.0];
    let ii = b.iinv.to_array();
    let m1 = mat_mul(&s, &ii);
    let m2 = mat_mul(&m1, &s);
    for i in 0..9 {
        k[i] -= m2[i];
    }
    k[0] += b.minv;
    k[4] += b.minv;
    k[8] += b.minv;
}

/// `sub_35020320(A, M)`: `A·M` with element `(A[i][1]·M[1][j] + A[i][0]·M[0][j]) +
/// A[i][2]·M[2][j]`.
fn mat_mul(a: &[f32; 9], m: &[f32; 9]) -> [f32; 9] {
    let mut o = [0.0; 9];
    for i in 0..3 {
        for j in 0..3 {
            o[3 * i + j] = (a[3 * i + 1] * m[3 + j] + a[3 * i] * m[j]) + a[3 * i + 2] * m[6 + j];
        }
    }
    o
}

/// Slot 10 `Action(action_impulse)` dispatched on the entity class (the base Action does
/// nothing for the ported classes).
fn action_impulse(w: &mut World, e: EntIdx, a: &ActionImpulse) {
    if let EntityKind::Rigid(_) = w.ent(e).kind {
        super::action::impulse(w, e, a);
    }
}

/// `sub_350B5E10(ent, i, pt, contacts, iPrim0, iFeature0, iPrim1, iFeature1, flags, depth,
/// tid, axis)` RegisterContactPoint: turn arena contact record `i` into an entity contact.
///
/// * Contacts of parts with flag `0x1000000` (either side) are aggregated into the static
///   dummy contact (`qword_3521AF50`), only reported as collision events (area runs are
///   averaged into one point) and never become solver contacts → `None`.
/// * Otherwise an existing contact with the same collider and body within
///   `min(minContactDist₀, minContactDist₁)` (flag `0x100000` parts: 15 % of the smallest
///   bbox size) of `pt` that is new (`0x40`) or resting (`penetration == 0`) is reused —
///   unless it is resting/new and deeper than `depth` or the call is an ignored one
///   (`flags & 0x80`) → `None`. Without a match the first non-new penetrating contact is
///   recycled (removed and re-inserted), or a pool record is allocated while the list holds
///   fewer than `max_contacts_per_entity` → `None` at the cap.
/// * Fills point, normal (`−n` of the record), entities, parts, bodies, depth, part id
///   bits, relative normal velocity, material ids, friction (static or dynamic table by
///   `|vrel| > 5·contact_gap`, `max(minFriction)`, `min(island maxFriction)`, 0 against parts
///   with `0x40000000`), flags (`|= flags & ~0x80000`).
/// * Bounce: with surface bounciness > 0 and `vn < −min_bounce_speed` an impulse
///   `−(1 + e)·vn / (n·K·n)` is applied to both entities (if not excessive) and a collision
///   event posted.
/// * Resting contacts (`depth ≤ 0`): `iNormal = (iFeature0 & 0xE0) < (iFeature1 & 0xE0)`,
///   `nloc = q⁻¹·n` of that body, `vreq = 0`. Penetrating ones: deep contacts (> 6 % of the
///   bbox size sum) use the unprojection direction as normal, `vreq = n·min(maxUnprojVel,
///   (depth − contact_gap)·gain)`, `nloc = axis`.
#[allow(clippy::too_many_arguments, clippy::too_many_lines)]
pub fn register_contact_point(
    w: &mut World,
    e: EntIdx,
    i: i32,
    pt: Vec3,
    _iprim0: i32,
    ifeat0: i32,
    _iprim1: i32,
    ifeat1: i32,
    flags: u32,
    depth: f32,
    tid: i32,
    axis: Vec3,
) -> Option<ContactId> {
    let tidu = tid as usize;
    let (col, (own_part, cpart)) = {
        let cs = &w.ccd_scratch[tidu];
        (cs.collider[i as usize], cs.parts[i as usize])
    };
    let pbody1 = body_id_of(w, col);
    let own_flags = w.ent(e).parts[own_part as usize].flags;
    let cflags = w.ent(col).parts[cpart as usize].flags;
    let o = ThreadScratch::contact_off(i);
    let aggregate = (own_flags | cflags) & 0x100_0000 != 0;
    let ci: ContactId;
    if aggregate {
        ci = w.agg_contact();
    } else {
        let v27 = if own_flags & 0x10_0000 != 0 {
            let bb = w.ent(e).bbox;
            let (dx, dy, dz) = (bb[1].x - bb[0].x, bb[1].y - bb[0].y, bb[1].z - bb[0].z);
            let m = ((dx + dy) - (dx - dy).abs()) * 0.5;
            (((m + dz) - (m - dz).abs()) * 0.5) * 0.150_000_01
        } else {
            let a = w.ent(e).parts[own_part as usize].min_contact_dist;
            let b = w.ent(col).parts[cpart as usize].min_contact_dist;
            ((a + b) - (a - b).abs()) * 0.5
        };
        let v34 = v27 * v27;
        let head = w.ent(e).rigid().expect("rigid").contacts_head;
        let first = w.contacts.get(head).next;
        let mut it = first;
        while it != head {
            let k = w.contacts.get(it);
            if (k.flags & cf::NEW != 0 || k.penetration == 0.0) && k.pent[1] == col && k.pbody[1] == pbody1 {
                let d = k.pt[0];
                let dist = (((d.x - pt.x) * (d.x - pt.x)) + ((d.y - pt.y) * (d.y - pt.y))) + ((d.z - pt.z) * (d.z - pt.z));
                if dist < v34 {
                    break;
                }
            }
            it = k.next;
        }
        if it == head {
            let mut v40 = 0;
            let mut j = first;
            while j != head {
                let k = w.contacts.get(j);
                if k.flags & cf::NEW == 0 && k.penetration != 0.0 {
                    break;
                }
                j = k.next;
                v40 += 1;
            }
            let rec = if j != head {
                super::contacts::remove_contact(w, e, j, -1, true);
                j
            } else if v40 < w.vars.max_contacts_per_entity {
                w.contacts.alloc()
            } else {
                return None;
            };
            let idx = crate::entities::add_collider(w, e, col);
            super::contacts::insert_contact(w, e, rec, idx, col);
            crate::entities::add_collider(w, col, e);
            ci = rec;
        } else {
            let k = w.contacts.get(it);
            if (k.penetration == 0.0 || k.flags & cf::NEW != 0) && (k.penetration >= depth || flags & 0x80 != 0) {
                return None;
            }
            ci = it;
        }
    }
    // L30: fill the record
    let (n_rec, iprim1_rec, id0, id1) = {
        let s = &w.tscratch[tidu];
        (
            s.rd_v(o + i64::from(c::N)),
            s.rd_i(o + i64::from(c::IPRIM) + 4),
            s.rd_i(o + i64::from(c::ID)),
            s.rd_i(o + i64::from(c::ID) + 4),
        )
    };
    let pbody0 = body_id_of(w, e);
    let b1 = body_of(w, col);
    let b0 = *w.bodies.get(pbody0);
    let vn;
    let rel;
    {
        let k = w.contacts.get_mut(ci);
        k.pt[1] = pt;
        k.pt[0] = pt;
        k.n = Vec3::new(-n_rec.x, -n_rec.y, -n_rec.z);
        k.pent = [e, col];
        k.ipart = [own_part, cpart];
        k.pbody = [pbody0, pbody1];
        k.penetration = depth;
        k.idx_bits = (k.idx_bits & 0x8000_FFFF) | (((iprim1_rec as u32) & 0x3FFF) << 16);
        let p = k.pt[0];
        let (d1x, d1y, d1z) = (p.x - b1.pos.x, p.y - b1.pos.y, p.z - b1.pos.z);
        let v59 = (d1z * b1.w.y) - (d1y * b1.w.z);
        let v60 = (d1x * b1.w.z) - (d1z * b1.w.x);
        let v61 = (d1y * b1.w.x) - (d1x * b1.w.y);
        let (d0x, d0y, d0z) = (p.x - b0.pos.x, p.y - b0.pos.y, p.z - b0.pos.z);
        let v64 = d0x * b0.w.y;
        let v65 = d0x * b0.w.z;
        let v67 = d0y * b0.w.x;
        let v68 = d0y * b0.w.z;
        let v71 = d0z * b0.w.y;
        let v72 = d0z * b0.w.x;
        let v70 = (v67 - v64) + b0.v.z;
        let v73 = ((v65 - v72) + b0.v.y) - b1.v.y;
        let v74 = ((v71 - v68) + b0.v.x) - b1.v.x;
        let v75 = (v70 - b1.v.z) - v61;
        let v76 = v73 - v60;
        let v77 = v74 - v59;
        let nn = k.n;
        vn = ((v76 * nn.y) + (nn.x * v77)) + (v75 * nn.z);
        k.vn = vn;
        k.id_bits ^= (k.id_bits ^ ((id0 as u32) << 2)) & 0x1FFFC;
        k.id_bits = ((id1 as u32) << 17) | (k.id_bits & 0x1FFFF);
        rel = ((v76 * v76) + (v77 * v77)) + (v75 * v75);
    }
    let gap5 = w.vars.contact_gap * 5.0;
    let table = if rel > gap5 * gap5 { &w.surfaces.dyn_friction } else { &w.surfaces.friction };
    let v84 = table[(id1 & 0x1FF) as usize] + table[(id0 & 0x1FF) as usize];
    let v86 = (((-v84).abs() + v84) * 0.5) * 0.5;
    let x = cflags & 0x4000_0000;
    let v87 = (-(((x ^ x.wrapping_sub(1)) as i32) >> 31)) as f32 * v86;
    let minf = w.ent(e).rigid().expect("rigid").min_friction;
    let v88 = ((minf - v87).abs() + (minf + v87)) * 0.5;
    let v89 = w.island[tidu].max_friction;
    {
        let k = w.contacts.get_mut(ci);
        k.friction = v88;
        k.flags &= !0x20_0000;
        k.flags |= flags & 0xFFF7_FFFF;
        k.friction = ((v89 + v88) - (v89 - v88).abs()) * 0.5;
    }
    if aggregate {
        aggregate_area(w, e, ci, i, flags, tid);
        return None;
    }
    w.contacts.get_mut(ci).pn = 0.0;
    let v155 = (w.surfaces.bounciness[(id1 & 0x1FF) as usize] + w.surfaces.bounciness[(id0 & 0x1FF) as usize]) * 0.5;
    if v155 > 0.0 && vn < -w.vars.min_bounce_speed {
        let cpart_rec = &w.ent(col).parts[cpart as usize];
        if b0.m < cpart_rec.mass || cpart_rec.flags & 0x40_0000 == 0 {
            bounce(w, e, col, ci, v155, own_part, cpart);
        }
    }
    if depth <= 0.0 {
        let k = w.contacts.get_mut(ci);
        k.id_bits &= 0xFFFF_FFFC;
        k.id_bits |= u32::from(((ifeat0 as u32) & 0xE0) < ((ifeat1 as u32) & 0xE0));
        let bq = w.bodies.get(k.pbody[(k.id_bits & 3) as usize]).q;
        let nn = k.n;
        let k = w.contacts.get_mut(ci);
        k.nloc = crate::entities::base::geometry::rotate_inv(bq, nn);
        k.vreq = Vec3::ZERO;
    } else {
        let bb = w.ent(e).bbox;
        if depth > (((bb[1].y - bb[0].y) + (bb[1].x - bb[0].x)) + (bb[1].z - bb[0].z)) * 0.059_999_999 {
            let dir = w.tscratch[tidu].rd_v(o + i64::from(c::DIR));
            w.contacts.get_mut(ci).n = dir;
        }
        let v = &w.vars;
        let v193 = (depth - v.contact_gap) * v.unproject_vel_gain;
        let v192 = v.max_unproject_vel;
        let v195 = ((v192 + v193) - (v192 - v193).abs()) * 0.5;
        let k = w.contacts.get_mut(ci);
        let nn = k.n;
        k.vreq = Vec3::new(nn.x * v195, nn.y * v195, nn.z * v195);
        k.nloc = axis;
    }
    Some(ci)
}

/// The restitution branch of RegisterContactPoint.
fn bounce(w: &mut World, e: EntIdx, col: EntIdx, ci: ContactId, e_b: f32, own_part: i32, cpart: i32) {
    let k = *w.contacts.get(ci);
    let b0 = *w.bodies.get(k.pbody[0]);
    let b1 = *w.bodies.get(k.pbody[1]);
    let mut km = [0.0f32; 9];
    let p = k.pt[0];
    add_collision_matrix(&b0, Vec3::new(p.x - b0.pos.x, p.y - b0.pos.y, p.z - b0.pos.z), &mut km);
    add_collision_matrix(&b1, Vec3::new(p.x - b1.pos.x, p.y - b1.pos.y, p.z - b1.pos.z), &mut km);
    let n = k.n;
    let v169 = (km[3] * n.y + km[0] * n.x) + km[6] * n.z;
    let v170 = (km[1] * n.x + km[4] * n.y) + km[7] * n.z;
    let v172 = km[2] * n.x + km[5] * n.y;
    let v173 = (e_b + 1.0) * k.vn;
    let v175 = -(v173 / (((v170 * n.y) + (n.x * v169)) + ((v172 + km[8] * n.z) * n.z)));
    let v176 = (b1.v.x * b1.v.x + b1.v.y * b1.v.y) + b1.v.z * b1.v.z;
    let v177 = (b0.v.x * b0.v.x + b0.v.y * b0.v.y) + b0.v.z * b0.v.z;
    let lim = (((v177 - v176).abs() + (v176 + v177)) * 0.5) * ((e_b + 1.1) * (e_b + 1.1));
    if (v175 * b0.minv) * (v175 * b0.minv) < lim {
        collision_event(w, e, ci, v175, true, 0.0);
        let mut a = ActionImpulse {
            impulse: Vec3::new(n.x * v175, n.y * v175, n.z * v175),
            ang_impulse: Vec3::new(UNUSED_F32, 0.0, 0.0),
            dv: Vec3::new(UNUSED_F32, 0.0, 0.0),
            dw: Vec3::new(UNUSED_F32, 0.0, 0.0),
            point: k.pt[0],
            part_id: w.ent(e).parts[own_part as usize].id,
            ipart: UNUSED_I32,
            i_apply_time: 0,
            i_source: 4,
        };
        action_impulse(w, e, &a);
        a.impulse = Vec3::new(-a.impulse.x, -a.impulse.y, -a.impulse.z);
        a.part_id = w.ent(col).parts[cpart as usize].id;
        action_impulse(w, col, &a);
    }
}

/// The aggregate (`0x1000000` parts) branch: contacts of one area run are averaged into one
/// point (the mean of all border points of the run's records) with the largest distance of
/// a border point from it as the radius, and reported once.
fn aggregate_area(w: &mut World, e: EntIdx, ci: ContactId, i: i32, flags: u32, tid: i32) {
    let tidu = tid as usize;
    let mut v62 = true;
    let cs = &w.ccd_scratch[tidu];
    let n = cs.count;
    if flags & 0x80000 == 0
        || (i != n - 1
            && cs.collider[i as usize] == cs.collider[i as usize + 1]
            && cs.parts[i as usize].1 == cs.parts[i as usize + 1].1)
    {
        v62 = false;
    }
    let mut v92 = cs.area_start;
    if v92 < 0 {
        v92 = i;
    }
    w.ccd_scratch[tidu].area_start = v92;
    let mut radius = 0.0f32;
    if v62 {
        let s = &w.tscratch[tidu];
        let (mut sx, mut sy, mut sz) = (0.0f32, 0.0f32, 0.0f32);
        let mut cnt = 0i32;
        if v92 <= i {
            for r in v92..=i {
                let o = ThreadScratch::contact_off(r);
                let nb = s.rd_i(o + i64::from(c::NBORDER));
                let pb = s.rd_q(o + i64::from(c::PTBORDER));
                for k in 0..nb.max(0) {
                    let q = s.rd_v_at(pb.wrapping_add(12 * k as u64));
                    sx += q.x;
                    sy += q.y;
                    sz += q.z;
                }
                cnt += nb;
            }
            if cnt != 0 {
                let inv = 1.0 / cnt as f32;
                let (cx, cy, cz) = (sx * inv, sy * inv, sz * inv);
                for r in v92..=i {
                    let o = ThreadScratch::contact_off(r);
                    let nb = s.rd_i(o + i64::from(c::NBORDER));
                    let pb = s.rd_q(o + i64::from(c::PTBORDER));
                    for k in 0..nb.max(0) {
                        let q = s.rd_v_at(pb.wrapping_add(12 * k as u64));
                        let d = (((q.y - cy) * (q.y - cy)) + ((q.x - cx) * (q.x - cx))) + ((q.z - cz) * (q.z - cz));
                        let l = d.sqrt();
                        radius = ((radius - l).abs() + (l + radius)) * 0.5;
                    }
                }
                let k = w.contacts.get_mut(ci);
                k.pt[1] = Vec3::new(cx, cy, cz);
                k.pt[0] = Vec3::new(cx, cy, cz);
            }
        }
        w.ccd_scratch[tidu].area_start = -1;
    }
    collision_event(w, e, ci, 0.0, v62, radius);
}

/// `sub_350AE0D0(ent, contact, impulse, bLog, radius)`: post a type-1 collision event for a
/// contact the entity reports (flags `0x2002000`) and that was not reported yet
/// (`0x200000`): immediate clients when `0x2000`; with `0x2000000` the logged event is
/// posted for `0x1000000` parts (if `bLog`), or merged into the entity's per-step slots
/// (`+508` bits 17–21 used / 22–26 max, `+1088` slot pointers, `+1100` smallest distance):
/// a new slot is taken while free, otherwise the slot with the smallest |v₀ − v₁|² is
/// overwritten when this one is larger.
pub fn collision_event(w: &mut World, e: EntIdx, ci: ContactId, impulse: f32, log: bool, radius: f32) {
    let ef = w.ent(e).flags;
    let k: Contact = *w.contacts.get(ci);
    if ef & 0x200_2000 == 0 || k.flags & 0x20_0000 != 0 {
        return;
    }
    let mut v = [Vec3::ZERO; 2];
    let mut mass = [0.0f32; 2];
    let mut partid = [0i32; 2];
    for s in 0..2 {
        let b = w.bodies.get(k.pbody[s]);
        let p = k.pt[0];
        let d = Vec3::new(p.x - b.pos.x, p.y - b.pos.y, p.z - b.pos.z);
        v[s] = Vec3::new(
            ((d.z * b.w.y) - (d.y * b.w.z)) + b.v.x,
            ((d.x * b.w.z) - (d.z * b.w.x)) + b.v.y,
            ((d.y * b.w.x) - (d.x * b.w.y)) + b.v.z,
        );
        mass[s] = b.m;
        let pid = w.ent(k.pent[s]).parts[k.ipart[s] as usize].id;
        partid[s] = if pid <= -2 { -2 - pid } else { pid };
    }
    let rad_scale = ((((7 - w.ent(e).parts.len() as i32) as u32) >> 31) as f32 * 1.5) + 1.0;
    let ev = Event::Collision {
        ent: [k.pent[0], k.pent[1]],
        pt: k.pt[0],
        n: k.n,
        v,
        mass,
        partid,
        idmat: [((k.id_bits >> 2) & 0x7FFF) as i32, (k.id_bits >> 17) as i32],
        penetration: k.penetration,
        normimpulse: impulse,
        radius: rad_scale * radius,
    };
    w.contacts.get_mut(ci).flags |= 0x20_0000;
    if ef & 0x2000 != 0 {
        w.call_immediate_clients(&ev);
    }
    if ef & 0x200_0000 == 0 {
        return;
    }
    let step = w.pump_counter;
    let r = w.ent_mut(e).rigid_mut().expect("rigid");
    let fresh = step == r.stamp1096;
    if !fresh {
        r.awake_bits &= 0xFFC1_FFFF;
        r.f1100 = 1e10;
    }
    r.stamp1096 = step;
    let both = {
        let a = w.ent(k.pent[0]).parts[k.ipart[0] as usize].flags;
        let b = w.ent(k.pent[1]).parts[k.ipart[1] as usize].flags;
        a | b
    };
    if both & 0x100_0000 != 0 {
        if log {
            w.post_logged_event(ev);
        }
        return;
    }
    let bits = w.ent(e).rigid().expect("rigid").awake_bits;
    let max = (bits >> 22) & 0x1F;
    if max == 0 {
        return;
    }
    let used = (bits >> 17) & 0x1F;
    let dv = {
        let (a, b) = (v[0], v[1]);
        (((a.x - b.x) * (a.x - b.x)) + ((a.y - b.y) * (a.y - b.y))) + ((a.z - b.z) * (a.z - b.z))
    };
    if used == max {
        let f1100 = w.ent(e).rigid().expect("rigid").f1100;
        if dv > f1100 && dv < 1e10 {
            let slot = (bits >> 27) as usize;
            let idx = w.ent(e).rigid().expect("rigid").coll_log[slot];
            w.replace_logged_event(idx, ev);
            let r = w.ent_mut(e).rigid_mut().expect("rigid");
            r.awake_bits |= 0xF800_0000;
            r.f1100 = 1e10;
            let used = (r.awake_bits >> 17) & 0x1F;
            let mut best = 1e10f32;
            for j in 0..used as usize {
                let idx = w.ent(e).rigid().expect("rigid").coll_log[j];
                let d = w.logged_collision_dv(idx);
                if d < best {
                    let r = w.ent_mut(e).rigid_mut().expect("rigid");
                    r.awake_bits &= 0x07FF_FFFF;
                    r.f1100 = d;
                    best = d;
                    r.awake_bits |= (j as u32) << 27;
                }
            }
        }
    } else {
        let r = w.ent_mut(e).rigid_mut().expect("rigid");
        r.awake_bits ^= (r.awake_bits ^ ((r.awake_bits & 0xFFFE_0000).wrapping_add(0x20000))) & 0x3E_0000;
        let idx = w.post_logged_event(ev);
        let r = w.ent_mut(e).rigid_mut().expect("rigid");
        if r.coll_log.len() <= used as usize {
            r.coll_log.resize(used as usize + 1, usize::MAX);
        }
        r.coll_log[used as usize] = idx;
        if dv < r.f1100 {
            r.f1100 = dv;
            let b = r.awake_bits;
            r.awake_bits = (b & 0x07FF_FFFF) | (((b >> 17).wrapping_sub(1)) << 27);
        }
    }
}
