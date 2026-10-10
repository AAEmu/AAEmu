//! The group-step slots of `CRigidEntity` called by the world's island step: StartStep (26),
//! StepBack (27), GetMaxTimeStep (37), GetLastTimeStep (38), GetContactCount (39),
//! RegisterContacts (40), Update (41), CalcEnergy (42), GetDamping (43), GetMaxFriction (44).

// NaN-aware comparisons and float literals are written as in the binary (the negated
// comparisons keep its NaN behaviour; the literals are the decompile's single-precision
// constants).
#![allow(clippy::neg_cmp_op_on_partial_ord, clippy::excessive_precision)]

use super::colliders::boxes_overlap;
use super::patch::{fit_contact_patch, patch_next};
use super::step::{compute_bbox_re, update_position};
use crate::collision::contact::IntersectionParams;
use crate::geometry::GeomWorldData;
use crate::math::{Mat33, Quat, Vec3};
use crate::solver::contact::flags as cf;
use crate::solver::{ContactId, ContactOwner};
use crate::world::{EntIdx, World};

fn rd(w: &World, e: EntIdx) -> &super::RigidData {
    w.ent(e).rigid().expect("rigid")
}
fn rm(w: &mut World, e: EntIdx) -> &mut super::RigidData {
    w.ent_mut(e).rigid_mut().expect("rigid")
}
#[inline]
fn min_i(a: f32, b: f32) -> f32 {
    ((a + b) - (a - b).abs()) * 0.5
}
#[inline]
fn max_i(a: f32, b: f32) -> f32 {
    ((a - b).abs() + (a + b)) * 0.5
}
#[inline]
fn max0(x: f32) -> f32 {
    ((-x).abs() + x) * 0.5
}

/// Slot 26 `StartStep(dt)` (0x350CCB20): `timeStepFull = dt`, `timeStepPerformed = 0`.
pub fn start_step(w: &mut World, e: EntIdx, dt: f32) {
    let r = rm(w, e);
    r.time_step_full = dt;
    r.time_step_performed = 0.0;
}

/// Slot 38 `GetLastTimeStep` (0x35020A40): `+604`.
#[must_use]
pub fn last_time_step(w: &World, e: EntIdx) -> f32 {
    rd(w, e).last_dt
}

/// Slot 44 `GetMaxFriction` (0x35020A50): `+704`.
#[must_use]
pub fn max_friction(w: &World, e: EntIdx) -> f32 {
    rd(w, e).max_friction
}

/// Slot 43 `GetDamping(dt)` (0x350AC490): `max(0, 1 − dt·max(damping | dampingFreefall,
/// waterDamping))` (damping with colliders, the free-fall value without).
#[must_use]
pub fn get_damping(w: &World, e: EntIdx, dt: f32) -> f32 {
    let r = rd(w, e);
    let v2 = if w.ent(e).colliders.is_empty() {
        r.damping_freefall
    } else {
        r.damping
    };
    let v3 = max_i(v2, r.water_damping_eff) * dt;
    max0(1.0 - v3)
}

/// Slot 39 `GetContactCount(nMax)` (0x350AC3E0): the per-collider number of non-ignored
/// contacts, each clamped to `nMax`, summed. **Bug kept:** the per-collider count is only
/// reset for colliders with contacts, so a collider without contacts adds the previous
/// collider's count again.
#[must_use]
pub fn contact_count(w: &World, e: EntIdx, nmax: i32) -> i32 {
    if w.ent(e).flags & 1 != 0 {
        return 0;
    }
    let r = rd(w, e);
    let mut j = 0i32;
    let mut v6 = 0i32;
    for &first in &r.collider_contacts {
        if !first.is_none() {
            let mut k = first;
            let f = |c: ContactId| ((!w.contacts.get(c).flags >> 7) & 1) as i32;
            j = f(k);
            while w.contacts.get(k).flags & cf::LAST_OF_COLLIDER == 0 {
                k = w.contacts.get(k).next;
                j += f(k);
            }
        }
        v6 += nmax + ((j - nmax) & ((j - nmax) >> 31));
    }
    v6
}

/// Slot 42 `CalcEnergy(dt)` (0x350AB8F0).
///
/// * `dt ≤ 0`: `m·|v|² + w·L` (the `max(|v|², |v|²)` of the binary is exact).
/// * entities with flag 1 when `vars.unk_384` is set: the cached `+1032`.
/// * otherwise the velocity the contacts' required separation speeds would add (`dv`, summed
///   along the normals), `max(|v + dv|², |dv|² + |v|²)·m + w·L`, clamped to
///   `2·m·maxVel²` (`2·m·100` when a contact is with a static part of a non-static entity).
///   Constraint terms are not ported (no constraints).
#[must_use]
pub fn calc_energy(w: &World, e: EntIdx, dt: f32) -> f32 {
    let r = rd(w, e);
    let b = w.bodies.get(r.body);
    let v7 = b.m;
    let wl = (b.w.y * b.l.y + b.w.x * b.l.x) + b.w.z * b.l.z;
    let mut v9 = ((w.vars.max_velocity * w.vars.max_velocity) * v7) * 2.0;
    if dt <= 0.0 {
        let v38 = (b.v.x * b.v.x + b.v.y * b.v.y) + b.v.z * b.v.z;
        #[allow(clippy::eq_op)]
        return (((v38 - v38).abs() + (v38 + v38)) * 0.5) * v7 + wl;
    }
    let f92 = w.ent(e).flags;
    if (f92 as u8) & (w.vars.unk_384 as u8).wrapping_neg() & 1 != 0 {
        return r.e_cached;
    }
    let (mut v5, mut v6, mut v8) = (0.0f32, 0.0f32, 0.0f32);
    if f92 & 1 == 0 {
        let head = r.contacts_head;
        let mut i = w.contacts.get(head).next;
        while i != head {
            let c = w.contacts.get(i);
            let v17 = ((c.n.y * c.vreq.y + c.n.x * c.vreq.x) + c.n.z * c.vreq.z) - max0(c.vn);
            let v18 = max0(v17) - ((v6 * c.n.y + v5 * c.n.x) + v8 * c.n.z);
            let k = max0(v18);
            v5 += c.n.x * k;
            v6 += c.n.y * k;
            v8 += c.n.z * k;
            if w.bodies.get(c.pbody[1]).minv == 0.0
                && (w.ent(c.pent[1]).id_bits & 0xFF00_0000) as i32 > 0x0100_0000
            {
                v9 = (v7 * 100.0) * 2.0;
            }
            i = c.next;
        }
    }
    let v33 = b.v.x;
    let v34 = v33 * v33 + b.v.y * b.v.y;
    let v35 = ((v33 + v5) * (v33 + v5) + (v6 + b.v.y) * (v6 + b.v.y)) + (v8 + b.v.z) * (v8 + b.v.z);
    let v36 = ((v6 * v6 + v5 * v5) + v8 * v8) + (v34 + b.v.z * b.v.z);
    let v37 = (max_i(v35, v36) * v7 + wl) + 0.0;
    if v37 >= v9 {
        v9
    } else {
        v37
    }
}

/// Slot 27 `StepBack(dt)` (0x350BE4A0): `dt ≤ 0` only forces the next step to 0.001 s;
/// otherwise the body is restored to the pre-step CoM and orientation (`Iinv` rebuilt, the
/// new pose re-derived without renormalisation, flag `0x1000`, bbox recomputed and the pose
/// committed). Always: `v`/`w` from the step snapshot, `P = m·v`, `L = q·(Ibody ∘ q⁻¹·w)`;
/// logged collision events of this step against entities with flag `0x80000000` get their
/// second mass set to 0.01.
pub fn step_back(w: &mut World, e: EntIdx, dt: f32) {
    let body_id = rd(w, e).body;
    if dt <= 0.0 {
        rm(w, e).forced_dt = f32::from_bits(981_668_463);
    } else {
        let (pp, pq) = (rd(w, e).prev_pos, rd(w, e).prev_q);
        let b = w.bodies.get_mut(body_id);
        b.pos = pp;
        b.q = pq;
        let r = Mat33::from_quat(b.q);
        b.iinv = Mat33::sandwich_diag(&r, b.ibody_inv);
        let b = *b;
        let qn = b.q.product(b.qfb);
        let off = qn.rotate(b.offsfb);
        {
            let r = rm(w, e);
            r.q_new = qn;
            r.pos_new = Vec3::new(b.pos.x - off.x, b.pos.y - off.y, b.pos.z - off.z);
            r.flags2 |= 0x1000;
        }
        compute_bbox_re(w, e);
        let bb = rd(w, e).bbox_new;
        let cells = w.reposition_entity(e, 1, Some(bb));
        update_position(w, e, cells);
    }
    let (pv, pw) = (rd(w, e).prev_v, rd(w, e).prev_w);
    let b = w.bodies.get_mut(body_id);
    b.v = pv;
    b.w = pw;
    let m = b.m;
    b.p = Vec3::new(b.v.x * m, b.v.y * m, b.v.z * m);
    let lw = b.q.conj().rotate(b.w);
    let s = Vec3::new(b.ibody.x * lw.x, b.ibody.y * lw.y, b.ibody.z * lw.z);
    b.l = b.q.rotate(s);
    // the per-step logged collision slots
    let r = rd(w, e);
    if !r.coll_log.is_empty() && w.pump_counter == r.stamp1096 && r.awake_bits & 0x3E_0000 != 0 {
        let used = (r.awake_bits >> 17) & 0x1F;
        let slots: Vec<usize> = r.coll_log.iter().copied().take(used as usize).collect();
        for idx in slots {
            if let Some(crate::world::events::Event::Collision { ent, mass, .. }) =
                w.events.logged.get(idx).cloned().as_mut()
            {
                if w.ent(ent[1]).flags & 0x8000_0000 != 0 {
                    mass[1] = 0.01;
                    let ev = w.events.logged[idx].clone();
                    if let crate::world::events::Event::Collision {
                        ent,
                        pt,
                        n,
                        v,
                        mass: _,
                        partid,
                        idmat,
                        penetration,
                        normimpulse,
                        radius,
                    } = ev
                    {
                        w.events.logged[idx] = crate::world::events::Event::Collision {
                            ent,
                            pt,
                            n,
                            v,
                            mass: *mass,
                            partid,
                            idmat,
                            penetration,
                            normimpulse,
                            radius,
                        };
                    }
                }
            }
        }
    }
}

/// Slot 37 `GetMaxTimeStep(dt)` (0x350BCAA0): the CCD limiter.
///
/// The part with the largest box volume (among parts with collision flags) defines the
/// thin axis: the local velocity (plus the pending position correction / dt) projected on its
/// box axes, the axis with the largest swept face area gives `+588 = |v_axis|` and `+592 =
/// size_axis`. Unless the body is light against an island with static colliders, or the CCD
/// flag `0x20000` is set (the default — then Step does the swept test), a swept-box test
/// against the neighbours decides whether the step must be limited to
/// `max(0.005, 1.7·size/|v|)`. The result is `min(min(full − performed, maxTimeStep·(1.5 if
/// the island flag and ≤ 1 collider)), dt)`.
#[allow(clippy::too_many_lines)]
pub fn get_max_time_step(w: &mut World, e: EntIdx, dt: f32) -> f32 {
    let mut dt = dt;
    if !(rd(w, e).time_step_full - 0.001 >= rd(w, e).time_step_performed) {
        return dt;
    }
    let tid = w.tls_thread as usize;
    let mut v6 = -1i32;
    let mut v7 = 0.0f32;
    let mut v10 = 0u32;
    let nparts = w.ent(e).parts.len();
    if nparts == 0 {
        return dt;
    }
    for (i, p) in w.ent(e).parts.iter().enumerate() {
        if p.flags & 0xFDFF != 0 {
            let s = p.pgeom_proxy.geom.bbox().size;
            if (s.y * s.x) * s.z > v7 {
                v7 = (s.y * s.x) * s.z;
                v6 = i as i32;
            }
            v10 |= p.flags_collider;
        }
    }
    if v6 == -1 {
        return dt;
    }
    let mut v5 = 0i32;
    for (i, &c) in w.ent(e).colliders.iter().enumerate() {
        if !rd(w, e).collider_contacts[i].is_none() {
            v5 |= w.ent(c).sim_class() - 1;
        }
    }
    let body = *w.bodies.get(rd(w, e).body);
    let island = w.island[tid];
    let v22 = (((body.m - island.dt * 0.001).to_bits() >> 31) as i32).wrapping_neg() & v5;
    let part = w.ent(e).parts[v6 as usize].clone();
    let bb = part.pgeom_proxy.geom.bbox();
    let corr = rd(w, e).pos_correction;
    let mut v = body.v;
    if (corr.x * corr.x + corr.y * corr.y) + corr.z * corr.z > 0.0 {
        let k = 1.0 / max_i(0.000_099_999_997, dt);
        v = Vec3::new(v.x + corr.x * k, v.y + k * corr.y, v.z + k * corr.z);
    }
    let q = w.ent(e).q.product(part.q);
    let lv = q.conj().rotate(v);
    let b = bb.basis.m;
    let p0 = (b[0][1] * lv.y + b[0][0] * lv.x) + b[0][2] * lv.z;
    let p1 = (b[1][0] * lv.x + b[1][1] * lv.y) + b[1][2] * lv.z;
    let p2 = (b[2][0] * lv.x + b[2][1] * lv.y) + b[2][2] * lv.z;
    let sc = part.scale;
    let sz = [sc * bb.size.x, bb.size.y * sc, bb.size.z * sc];
    let f = [
        (p0.abs() * sz[1]) * sz[2],
        (p1.abs() * sz[0]) * sz[2],
        (p2.abs() * sz[0]) * sz[1],
    ];
    let a = ((f[0] - f[1]).to_bits() >> 31) as usize;
    let b2 = ((f[a] - f[2]).to_bits() >> 31) as usize;
    let k = (a | (b2 << 1)) & ((((a | (b2 << 1)) ^ 2) | 4) >> 1);
    let v38 = [p0, p1, p2][k];
    let v39 = sz[k];
    {
        let r = rm(w, e);
        r.ccd_size = v39;
        r.ccd_vel = v38.abs();
    }
    let mut limit = v22 < 0;
    if !limit && rd(w, e).flags2 & 0x20000 == 0 {
        let v41 = rd(w, e).max_time_step;
        if min_i(v41, dt) * v38.abs() <= v39 * 0.699_999_99 {
            limit = true;
        } else {
            limit = !swept_box_clear(w, e, v41, v10, tid);
        }
    }
    if limit {
        let v88 = v38.abs();
        if v88 * dt > v39 * 0.699_999_99 {
            let x = (v39 * 1.7) / v88;
            dt = ((0.004_999_999_9 - x).abs() + (x + 0.004_999_999_9)) * 0.5;
        }
    }
    let r = rd(w, e);
    let flag = island.flag;
    let ncoll = w.ent(e).colliders.len() as i32;
    let v90 = (1.0 - ((flag.wrapping_neg() & !((1 - ncoll) >> 31)) as f32) * 0.5) * r.max_time_step;
    let v91 = min_i(r.time_step_full - r.time_step_performed, v90);
    min_i(v91, dt)
}

/// The swept-box neighbour test of GetMaxTimeStep: a box geometry of the entity's bbox
/// half-size swept by `v·maxTimeStep` (sweep intersection first, then a plain one) against
/// every part of the entities in the swept box (objtypes 263) whose flags match the
/// entity's collider flags. Returns `true` when nothing is hit.
fn swept_box_clear(w: &mut World, e: EntIdx, v41: f32, v192: u32, tid: usize) -> bool {
    let bbox = w.ent(e).bbox;
    let bv = w.bodies.get(rd(w, e).body).v;
    let mut lo = [bbox[0].x, bbox[0].y, bbox[0].z];
    let mut hi = [bbox[1].x, bbox[1].y, bbox[1].z];
    for (k, x) in [bv.x * v41, bv.y * v41, bv.z * v41].into_iter().enumerate() {
        if (-x).to_bits() >> 31 != 0 {
            hi[k] += x;
        } else {
            lo[k] += x;
        }
    }
    let lo = Vec3::new(lo[0], lo[1], lo[2]);
    let hi = Vec3::new(hi[0], hi[1], hi[2]);
    let list = w.get_entities_in_box(lo, hi, 263, Some(e), tid);
    let half = Vec3::new(
        (bbox[1].x - bbox[0].x) * 0.5,
        (bbox[1].y - bbox[0].y) * 0.5,
        (bbox[1].z - bbox[0].z) * 0.5,
    );
    let bx = crate::geometry::shapes::BoxGeom::new(&crate::geometry::primitives::BoxPrim {
        basis: Mat33::IDENTITY,
        center: Vec3::ZERO,
        size: half,
        ..Default::default()
    });
    let mut gwd_a = GeomWorldData {
        offset: Vec3::new(
            (bbox[0].x + bbox[1].x) * 0.5,
            (bbox[0].y + bbox[1].y) * 0.5,
            (bbox[0].z + bbox[1].z) * 0.5,
        ),
        v: bv,
        ..GeomWorldData::default()
    };
    let _ = boxes_overlap;
    for c in list {
        if c == e {
            continue;
        }
        let parts = w.ent(c).parts.clone();
        for p in &parts {
            if v192 & p.flags == 0 {
                continue;
            }
            let (pos, q) = (w.ent(c).pos, w.ent(c).q);
            let r = q.rotate(p.pos);
            let gwd_b = GeomWorldData {
                offset: Vec3::new(r.x + pos.x, r.y + pos.y, r.z + pos.z),
                r: Mat33::from_quat(q.product(p.q)),
                scale: p.scale,
                ..GeomWorldData::default()
            };
            for sweep in [true, false] {
                let mut ip = IntersectionParams {
                    time_interval: v41,
                    max_surface_gap_angle: f32::from_bits(1_016_003_125),
                    sweep_test: sweep,
                    ..IntersectionParams::default()
                };
                let env = crate::collision::prim_intersect::env_with_generic();
                let out = crate::collision::prim_intersect::intersect(
                    &env,
                    &mut w.tscratch[tid],
                    &bx,
                    p.pgeom_proxy.geom.as_ref(),
                    &mut gwd_a,
                    &gwd_b,
                    &mut ip,
                );
                if out.ret != 0 {
                    return false;
                }
            }
        }
    }
    true
}

/// Slot 40 `RegisterContacts(dt, nMaxPlaneContacts)` (0x350BDBA0): hand the entity's contacts
/// to the island solver of this thread.
///
/// Per collider run (skipped for flag-1 entities): collect the max friction and depth of the
/// non-ignored contacts, chain the run through `+16`; if all contacts are against one body
/// part, fit a contact patch (`fit_contact_patch`, tolerance `0.15·min(collider extent, own
/// extent)`), which reduces the run to its hull ring. A flat resting patch of a part with
/// `0x1000000` adds a pair of temporary solver contacts (a shared-impulse "patch" contact at
/// the CoM). Contacts are registered in ring order with a collision event each; `flags2` bits
/// 9–10 keep the max patch state (1 resting on a fitted patch, 2 when the collider sleeps).
/// Constraint contacts are not ported. Entities with `0x8000000` or under water disable the
/// solver's CG fast path.
#[allow(clippy::too_many_lines)]
pub fn register_contacts(w: &mut World, e: EntIdx, _dt: f32, n_max: i32) -> i32 {
    let tid = w.tls_thread as usize;
    let bb = w.ent(e).bbox;
    let v6 = bb[1].x - bb[0].x;
    let v7 = bb[1].y - bb[0].y;
    let dz = bb[1].z - bb[0].z;
    let v11 = ((dz + v7) - (v7 - dz).abs()) * 0.5;
    let v13 = ((v11 + v6) - (v6 - v11).abs()) * 0.5;
    let body_id = rd(w, e).body;
    {
        let b = w.bodies.get_mut(body_id);
        b.fcoll = Vec3::ZERO;
        b.tcoll = Vec3::ZERO;
    }
    let f92 = w.ent(e).flags;
    let ncoll = w.ent(e).colliders.len() as i32;
    {
        let r = rm(w, e);
        r.flags2 &= ((((-((f92 & 1) as i32)) >> 31) as u16 as u32) << 9) | 0xFFFF_F9FF;
        if r.n_constraints != ncoll {
            r.awake_history = 0;
        }
        r.n_constraints = ncoll;
    }
    let mut v80 = ContactId::NONE; // the last patch record (v80/v10)
    let mut v94 = 0.0f32;
    for i in 0..ncoll as usize {
        if f92 & 1 != 0 {
            continue;
        }
        let first = rd(w, e).collider_contacts[i];
        if first.is_none() {
            continue;
        }
        let mut v19 = crate::solver::BodyId(u32::MAX);
        let mut v21 = false;
        let mut v22 = 0.0f32;
        let mut v23 = 0.0f32;
        let mut v24 = 1i32;
        let mut v25: i32 = -1;
        let mut v77 = ContactId::NONE;
        let mut k = first;
        loop {
            let c = *w.contacts.get(k);
            if c.flags & 0x80 == 0 {
                if v19.0 != u32::MAX && c.pbody[1] != v19 {
                    v21 = true;
                }
                v19 = c.pbody[1];
                v77 = k;
                let d = v25.wrapping_sub(c.ipart[1]);
                let v27 =
                    (((v25 >> 31) + 1) ^ (((d ^ d.wrapping_sub(1)) >> 31).wrapping_neg())) != 0;
                v25 = c.ipart[1];
                v21 |= v27;
                let cm = w.contacts.get_mut(k);
                cm.flags &= !0x4000;
                v23 = ((v23 - c.friction).abs() + (c.friction + v23)) * 0.5;
                v22 = ((v22 - c.penetration).abs() + (c.penetration + v22)) * 0.5;
            }
            w.contacts.get_mut(k).unk16 = u64::from(c.next.0);
            if c.flags & cf::LAST_OF_COLLIDER != 0 {
                break;
            }
            k = c.next;
            v24 += 1;
        }
        let mut v34 = first;
        let mut v20 = 0i32;
        let mut v103 = false;
        let mut count = v24;
        if !v21 {
            let col = w.ent(e).colliders[i];
            let cb = w.ent(col).bbox;
            let (c81, c82, c83) = (cb[0].x, cb[0].y, cb[0].z);
            let v40 = (((cb[1].z - c83) + (cb[1].y - c82))
                - ((cb[1].y - c82) - (cb[1].z - c83)).abs())
                * 0.5;
            let v87 = ((v40 + (cb[1].x - c81)) - ((cb[1].x - c81) - v40).abs()) * 0.5;
            let v41 = v87.abs();
            // `sar ecx, 0x1f` (0x350BDF13): an *arithmetic* shift, so a collider with a zero
            // extent (the terrain entities' bbox is zero) gives -1 and the size becomes
            // `0 + v13` (the own extent), not `-v13` (P1c-world fix: the unsigned shift made the
            // patch tolerance negative and the fit always fail on terrain).
            let zero = ((v41.to_bits() ^ v41.to_bits().wrapping_sub(1)) as i32) >> 31;
            let v42 = v87 - (zero as f32 * v13);
            let v43 = ((v42 + v13) - (v13 - v42).abs()) * 0.5;
            let (head, com, g) = {
                let r = rd(w, e);
                (r.contacts_head, w.bodies.get(body_id).pos, r.gravity)
            };
            let fit = fit_contact_patch(
                &mut w.contacts,
                head,
                first,
                0x80000,
                v43 * 0.150_000_01,
                n_max,
                com,
                g,
            );
            count = fit.count;
            if let Some(f) = fit.first {
                v34 = f;
            }
            if let Some(d) = fit.dist {
                v94 = d;
            }
            if w.contacts.get(v77).flags & 0x100_0000 != 0 && fit.ok && v94 < v43 * 0.050_000_001 {
                let base = {
                    let (sol, pool) = w.solver_and_pool(tid);
                    sol.alloc_tmp_contacts(pool, 2)
                };
                let rec1 = base;
                let rec2 = ContactId(base.0 + 1);
                v80 = rec2;
                v103 = true;
                let c77 = *w.contacts.get(v77);
                let colb = w.contacts.get(v77).pbody[1];
                let com_b = w.bodies.get(colb).pos;
                let gap = w.vars.contact_gap;
                let v53 = min_i(
                    w.vars.max_unproject_vel,
                    max0(v22 - gap) * w.vars.unproject_vel_gain,
                );
                let own_vol = ((bb[1].y - bb[0].y) * (bb[1].x - bb[0].x)) * (bb[1].z - bb[0].z);
                let col_vol = ((cb[1].y - cb[0].y) * (cb[1].x - cb[0].x)) * (cb[1].z - cb[0].z);
                {
                    let r2 = w.contacts.get_mut(rec2);
                    r2.pent = [e, col];
                    r2.pbody = [body_id, v19];
                    r2.ipart = [-1, -1];
                    r2.owner = ContactOwner::Count(rec1);
                    r2.flags = 18691;
                    r2.n = c77.n;
                    r2.vreq = if c77.penetration <= 0.0 {
                        Vec3::ZERO
                    } else {
                        Vec3::new(c77.nloc.x * 10.0, c77.nloc.y * 10.0, c77.nloc.z * 10.0)
                    };
                }
                {
                    let r1 = w.contacts.get_mut(rec1);
                    r1.pent = [e, col];
                    r1.pbody = [body_id, v19];
                    r1.ipart = [-1, -1];
                    r1.flags = 1;
                    r1.friction = v23;
                    r1.pt[0] = com;
                    r1.n = c77.n;
                    r1.vreq = Vec3::new(c77.n.x * v53, c77.n.y * v53, c77.n.z * v53);
                    r1.pt[1] = if own_vol <= col_vol * 0.1 { com } else { com_b };
                }
                rm(w, e).ground_contact = v77;
            }
            let v66 = crate::entities::is_awake(w, col);
            let ok = i32::from(fit.ok);
            v20 = (ok & (v66 ^ 1)) + ok;
        }
        {
            let r = rm(w, e);
            let v79 = ((r.flags2 >> 9) & 3) as i32;
            let m = if v79 >= v20 { v79 } else { v20 };
            r.flags2 ^= (r.flags2 ^ ((m as u32) << 9)) & 0x600;
        }
        let shared = if v103 { 0x4000 } else { 0 };
        let mut kk = v34;
        for _ in 0..count.max(0) {
            {
                let c = w.contacts.get_mut(kk);
                // `+168 = v10 + 164`: the last patch record of this call (a bogus pointer
                // when none was made; only read for shared-impulse contacts)
                c.owner = if v80.is_none() {
                    ContactOwner::Null
                } else {
                    ContactOwner::Count(v80)
                };
                c.flags |= shared;
            }
            let (sol, pool) = w.solver_and_pool(tid);
            sol.register_contact(pool, kk);
            super::register::collision_event(w, e, kk, 0.0, true, 0.0);
            kk = patch_next(w.contacts.get(kk));
        }
        if v103 {
            let (sol, pool) = w.solver_and_pool(tid);
            sol.register_contact(pool, v80);
            sol.register_contact(pool, ContactId(v80.0 - 1));
        }
    }
    let r = rd(w, e);
    if (f32::from(u8::from(r.flags2 & 0x800_0000 != 0))) + r.submerged_fraction > 0.0 {
        w.solver_thread(tid).disable_cg();
    }
    1
}

/// Slot 41 `Update(dt, damping)` (0x350BEB40) → non-zero when the entity's step is finished.
///
/// Counter bookkeeping (`flags2` bits 13–16 from 0x1000, `+1044` byte/counters with the
/// `+1040` friction boost), clamp, damping of `v`, `w`, `P`, `L` (≥ 0.9 for flag-1
/// entities); bodies with an auxiliary energy (`+952`) re-derive the pose; per-step logged
/// collision impulses are accumulated; the velocity of a moving support (the ground contact,
/// or the first collider with a non-zero simclass) is subtracted while resting on it; then the
/// sleep logic: the energy against `Emin` (scaled down by 0.9 when resting on an awake body
/// or a single collider with < 3 contacts), idle-time accounting, the 2-frame sleep counter
/// with its simclass switches (re-listing with RepositionEntity flag 2) and the awake-history
/// shift register. Breakable-constraint checks are not ported (no constraints).
#[allow(clippy::too_many_lines)]
pub fn update(w: &mut World, e: EntIdx, dt: f32, damping: f32) -> u32 {
    let tid = w.tls_thread as usize;
    let body_id = rd(w, e).body;
    let mut a3 = damping;
    let v7 = rd(w, e).flags2;
    let ncoll = w.ent(e).colliders.len() as i32;
    let mut v183 = if v7 & 0x400_0000 == 0 || ncoll + rd(w, e).n_constraints != 0 {
        rd(w, e).min_energy
    } else {
        rd(w, e).min_energy_water
    };
    {
        let r = rm(w, e);
        let m = 0u32.wrapping_sub(8192 * u32::from(v7 & 0x1000 != 0));
        r.flags2 = v7 ^ ((v7 ^ ((v7 & m).wrapping_add(2 * (v7 & 0x1000)))) & 0x1E000);
    }
    if w.ent(e).flags & 1 != 0 {
        a3 = ((0.899_999_98 - a3).abs() + (a3 + 0.899_999_98)) * 0.5;
    }
    super::velocity::cap_velocities(w, e);
    {
        let r = rm(w, e);
        let x = r.unk1044;
        let b = (x as u8) as i8 as i32;
        let m = ((((b & 3) - 2) ^ ((b & 3) - 3)) >> 31) << 8;
        let v12 = (x & 0xFFFF_FF00).wrapping_sub(m as u32) ^ (x & 0xFF);
        r.unk1044 = v12;
        let n = ((v12 as i32) >> 8) & 3;
        let f = ((n - 3) ^ (n - 4)) >> 31;
        r.unk1040 -= (f as f32) * 3.0;
    }
    {
        let b = w.bodies.get_mut(body_id);
        b.v = Vec3::new(a3 * b.v.x, a3 * b.v.y, a3 * b.v.z);
        b.w = Vec3::new(a3 * b.w.x, a3 * b.w.y, a3 * b.w.z);
        b.p = Vec3::new(a3 * b.p.x, a3 * b.p.y, a3 * b.p.z);
        b.l = Vec3::new(a3 * b.l.x, a3 * b.l.y, a3 * b.l.z);
    }
    if w.bodies.get(body_id).e_aux > 0.0 {
        let b = *w.bodies.get(body_id);
        let qn = b.q.product(b.qfb);
        let off = w.ent(e).q.rotate(b.offsfb);
        {
            let r = rm(w, e);
            r.q_new = qn;
            r.pos_new = Vec3::new(b.pos.x - off.x, b.pos.y - off.y, b.pos.z - off.z);
        }
        compute_bbox_re(w, e);
        let bb = rd(w, e).bbox_new;
        let cells = w.reposition_entity(e, 1, Some(bb));
        update_position(w, e, cells);
    }
    if w.ent(e).flags & 0x200_0000 != 0 && rd(w, e).awake_bits & 0x7C0_0000 != 0 {
        let step = w.pump_counter;
        let r = rm(w, e);
        let fresh = step == r.stamp1096;
        if !fresh {
            r.awake_bits &= 0xFFC1_FFFF;
            r.f1100 = 1e10;
        }
        r.stamp1096 = step;
        // (the slots' pending impulse links are resolved by the solver's contacts; the
        // port's events carry the impulse already)
    }
    let b0 = *w.bodies.get(body_id);
    let (v_saved, w_saved) = (b0.v, b0.w);
    // velocity of the support
    let mut v48 = rd(w, e).ground_contact;
    let mut use_support = rd(w, e).flags2 & 0x600 == 0x400;
    if !use_support {
        let cols = w.ent(e).colliders.clone();
        if let Some(i) = cols
            .iter()
            .position(|&c| w.ent(c).id_bits & 0xFF00_0000 != 0)
        {
            v48 = rd(w, e).collider_contacts[i];
            use_support = true;
        }
    }
    if use_support && !v48.is_none() {
        let c = *w.contacts.get(v48);
        let other = c.pent[1];
        let ib = w.ent(other).id_bits as i32;
        if ib >= 0 && ib as u32 & 0xFF00_0000 != 0 {
            let (sv, sw) = super_vw(w, other);
            if ((sw.y * sw.y + sw.x * sw.x) + sw.z * sw.z)
                + ((sv.y * sv.y + sv.x * sv.x) + sv.z * sv.z)
                != 0.0
            {
                let pb = *w.bodies.get(c.pbody[1]);
                let b = w.bodies.get_mut(body_id);
                b.w = Vec3::new(b.w.x - sw.x, b.w.y - sw.y, b.w.z - sw.z);
                let v63 = c.pt[0].x - pb.pos.x;
                let ry = c.pt[0].y - pb.pos.y;
                let v64 = c.pt[0].z - pb.pos.z;
                b.v.x -= ((v64 * sw.y) - (ry * sw.z)) + sv.x;
                b.v.y -= ((sw.z * v63) - (v64 * sw.x)) + sv.y;
                b.v.z -= ((ry * sw.x) - (sw.y * v63)) + sv.z;
                let m = b.m;
                b.p = Vec3::new(b.v.x * m, b.v.y * m, b.v.z * m);
                b.l = l_from_w(b.q, b.ibody, b.w);
            }
        }
    }
    // resting on an awake rigid body
    let g = rd(w, e).gravity;
    let mut v91 = 0i32;
    let mut n = 0;
    let head = rd(w, e).contacts_head;
    let mut j = w.contacts.get(head).next;
    while j != head {
        if n >= 16 - 16 * v91 {
            break;
        }
        let c = *w.contacts.get(j);
        n += 1;
        let v94 = (g.y * c.n.y) + (g.x * c.n.x);
        let v95 = g.z * c.n.z;
        j = c.next;
        let other = c.pent[1];
        let sc = w.ent(other).sim_class();
        let v96 = sc == 2;
        let v97 = if v96 {
            w.ent(other).rigid().map_or(0, |r| r.awake_bits)
        } else {
            rd(w, e).awake_bits
        };
        let v182 =
            ((v94 + v95).abs() * (v94 + v95)) + (((g.x * g.x + g.y * g.y) + g.z * g.z) * 0.5);
        if v96 {
            v91 |= ((v182.to_bits() >> 31) & v97) as i32;
        }
    }
    let r = rd(w, e);
    let v98 = ncoll;
    let v99 = b0.minv;
    let v100 = r.min_awake_time;
    let few =
        (((r.n_contacts - 3) as u32) >> 31) as i32 & (((v98 - 1) ^ (v98 - 2)) >> 31).wrapping_neg();
    let v101 = v183 * (1.0 - ((v91 | few) as f32) * 0.899_999_98);
    v183 = v101;
    let v102 = max0(v100) - dt;
    rm(w, e).min_awake_time = v102;
    if v99 > 0.0 {
        let b = *w.bodies.get(body_id);
        let mut v103 = (((((b.w.x * b.l.x) + (b.w.y * b.l.y)) + (b.w.z * b.l.z)) * v99)
            + (((b.v.x * b.v.x) + (b.v.y * b.v.y)) + (b.v.z * b.v.z)))
            * 0.5
            + b.e_aux;
        if w.ent(e).flags & 1 != 0 {
            let e1036 = rd(w, e).e_clamp;
            v103 = min_i(e1036, v103);
        }
        let island = w.island[tid];
        let r = rd(w, e);
        let v105 = w.ent(e).time_idle;
        let v106 = r.n_constraints;
        let v107 = w.ent(e).max_time_idle;
        let v108 = ((r.time_step_full - dt) * island.flag as f32) + dt;
        let v182 = ((v98 + v106) as f32) + r.submerged_fraction;
        let ab = v182.to_bits() & 0x7FFF_FFFF;
        let v109 = ab ^ ab.wrapping_sub(1);
        let below = (-200.0 - w.ent(e).pos.z).to_bits() >> 31;
        let v110 = v105 - (((below & ((v109 >> 31).wrapping_neg())) as f32 * v108) * 3.0);
        let v111 = max0(v110);
        let tm = ((0.000_099_999_997 - v107).to_bits() >> 31) as f32;
        let v112 = tm * v108 + (island.flag as f32) * v111;
        w.ent_mut(e).time_idle = v112;
        let timed_mask = u32::from((0.000_099_999_997f32 - v107).to_bits() >> 31 == 0)
            | ((v112 - v107).to_bits() >> 31)
            | 0xFFFF_FF00;
        rm(w, e).awake_bits &= timed_mask;
        let v113 = rd(w, e).awake_bits;
        let simclass = w.ent(e).sim_class();
        let mut relist: Option<i32> = None;
        if v113 as u8 != 0 {
            let gz = (g.y * g.y + g.x * g.x) + g.z * g.z;
            let r = rm(w, e);
            if v103 < v101
                && ((v106 + v98 + i32::from(r.flags2 & 0x400_0000 != 0)) != 0 || gz == 0.0)
                && v102 <= 0.0
            {
                r.awake_bits &= !0xFF;
            } else {
                r.awake_bits &= 0xFFFF_E0FF;
            }
            r.v_accum = Vec3::ZERO;
            r.w_accum = Vec3::ZERO;
        } else if ((0.000_099_999_997f32 - v107).to_bits() >> 31 == 0)
            || (v112 - v107).to_bits() >> 31 != 0
        {
            let (wa, va) = {
                let r = rm(w, e);
                r.v_accum = Vec3::new(
                    b.v.x + r.v_accum.x,
                    b.v.y + r.v_accum.y,
                    b.v.z + r.v_accum.z,
                );
                r.w_accum = Vec3::new(
                    b.w.x + r.w_accum.x,
                    b.w.y + r.w_accum.y,
                    b.w.z + r.w_accum.z,
                );
                (r.w_accum, r.v_accum)
            };
            let r = rd(w, e);
            let hist = r.awake_history;
            let pc = hist.count_ones();
            let v145 = if r.flags2 & 0x600 >= 0x400 || pc >= 0x14 {
                v103
            } else {
                let lw = l_from_w(b.q, b.ibody, wa);
                (((((lw.y * wa.y) + (lw.x * wa.x)) + (lw.z * wa.z)) * b.minv)
                    + (((va.x * va.x) + (va.y * va.y)) + (va.z * va.z)))
                    * 0.5
                    + b.e_aux
            };
            if v145 <= v183 {
                if (w.ent(e).flags & 1) | u32::from(rd(w, e).flags2 & 0x400_0000 != 0) == 0 {
                    let r = rm(w, e);
                    r.v_sleep = v_saved;
                    r.w_sleep = w_saved;
                    let b = w.bodies.get_mut(body_id);
                    b.p = Vec3::ZERO;
                    b.l = Vec3::ZERO;
                    b.v = Vec3::ZERO;
                    b.w = Vec3::ZERO;
                }
                let r = rm(w, e);
                let x = r.awake_bits;
                r.awake_bits = x ^ ((x ^ ((x & 0xFF00).wrapping_add(256))) & 0x1F00);
                if r.awake_bits & 0x1F00 >= 0x200 && simclass == 2 {
                    w.ent_mut(e).set_sim_class(1);
                    w.reposition_entity(e, 2, None);
                    let r = rm(w, e);
                    r.awake_bits &= 0xFFFF_E0FF;
                    r.unk1040 = max0(r.unk1040 - 1.5);
                    r.v_accum = Vec3::ZERO;
                    r.w_accum = Vec3::ZERO;
                }
            } else {
                let r = rm(w, e);
                r.awake_bits = (r.awake_bits & !0xFF) | 1;
                r.awake_bits &= 0xFFFF_E0FF;
                r.v_accum = Vec3::ZERO;
                r.w_accum = Vec3::ZERO;
                if simclass == 1 {
                    relist = Some(2);
                }
            }
        } else if simclass == 2 {
            relist = Some(1);
        }
        if let Some(sc) = relist {
            w.ent_mut(e).set_sim_class(sc);
            w.reposition_entity(e, 2, None);
        }
        let r = rm(w, e);
        r.awake_history = (r.awake_history << 1) | ((r.awake_bits & 0xFF) ^ 1);
    }
    let r = rm(w, e);
    r.flags2 &= 0xFFFF_F9FF;
    let v155 = r.awake_bits ^ 1;
    let v182 = (r.time_step_full - r.time_step_performed) - 0.001;
    (v182.to_bits() >> 31) | v155 | (w.island[tid].flag as u32)
}

/// Slot 46 of a support entity: `(v, w)` saved when it was put to sleep (rigid `+708/+720`),
/// zero for the base class.
fn super_vw(w: &World, e: EntIdx) -> (Vec3, Vec3) {
    match w.ent(e).rigid() {
        Some(r) => (r.v_sleep, r.w_sleep),
        None => (Vec3::ZERO, Vec3::ZERO),
    }
}

/// `L = q·(Ibody ∘ (q⁻¹·w))` with the inline rotations of Update/StepBack.
fn l_from_w(q: Quat, ibody: Vec3, wv: Vec3) -> Vec3 {
    let lw = q.conj().rotate(wv);
    let s = Vec3::new(lw.x * ibody.x, lw.y * ibody.y, lw.z * ibody.z);
    q.rotate(s)
}
