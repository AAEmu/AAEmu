//! Slot 78 `CheckAdditionalGeometry(dt)` of `CWheeledVehicleEntity` (0x3510C940; x86 1.2
//! `CWheeledVehicleEntity::CheckAdditionalGeometry` 0x3511AFB0): the wheel contacts.
//!
//! Called by `CRigidEntity::Step` with the step's `dt` after the integration (new pose in
//! `pos_new`/`q_new`, `+448`/`+460`) and in the asleep branch.
//!
//! 1. `GetEntitiesInBox(bbox_new, 0x147)`; neighbours with `Minv ≥ 5·Minv(vehicle)` are
//!    "light": while the vehicle is awake they are woken (`Awake(1, 5)`) when their part boxes
//!    (in the vehicle's frame) overlap the steering-swept box of a wheel, and dropped from the
//!    list; the others are the ground candidates.
//! 2. With the entity flag 8 and a max idle time > 0.001 (or any max idle time > 0.001 — the
//!    test is the sign of `(0.001 − maxTimeIdle)·(flags & 8)`, and `−x·0 = −0`) only the
//!    rearmost and the frontmost wheel per side are tested; the others copy the contact of
//!    the nearer one and interpolate their length (step 6).
//! 3. Per wheel (part `n_hull + i`): part flags `= orig | 0x40000000`, collider flags 0; the
//!    wheel record pose (`q = rot(−z, steer) ⊗ q0`, the position at the rest length); the
//!    world box of the wheel swept down by `lenMax` (for the candidate part culling).
//! 4. Cast against every candidate part with a matching collision mask:
//!    * `lenMax ≤ 0`: the wheel shape at its pose (no sweep; velocity of the hull at the
//!      wheel, `time_interval = 1.4·dt`) — the contacts give penetrations `−(n·down)·t`;
//!    * `bRayCast`: a ray from `lenMax + rest` above the wheel bottom, `2·lenMax + r` long,
//!      against the candidate (`t −= r`);
//!    * otherwise the wheel shape swept down by `lenMax` from its raised pose.
//!
//!    A contact whose normal is within 30° (`+11232`) of the forward axis, or whose point is
//!    not below the wheel centre, is not a wheel contact (it may only raise the "hull"
//!    penetration `v480`); the deepest accepted one is the wheel contact (point moved onto the
//!    wheel's mid plane, normal, ground entity / part / body, surfaces, terrain surface by an
//!    extra ray), and both entities become colliders of each other.
//! 5. If the hull penetration exceeds 1.01× the wheel's, the wheel part collides normally
//!    this step (original flags) and loses the contact unless `+11236` bit 0. In contact:
//!    the slip velocity, the rigid contacts of the wheel part with the ground are removed on
//!    both sides (slot 72), the push-out velocity `vreq` of the solver contact; the
//!    suspension length `min(len + 7·dt, lenMax − depth)` and the record position.
//! 6. (step 2) the interpolated wheels.

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
use super::{n_wheels, wd, wflags, wm};
use crate::collision::contact::IntersectionParams;
use crate::collision::prim_intersect::scratch::{c, ThreadScratch};
use crate::collision::prim_intersect::IntersectOut;
use crate::geometry::primitives::RayPrim;
use crate::geometry::shapes::RayGeom;
use crate::geometry::{GeomWorldData, Geometry};
use crate::math::{crt, Mat33, Quat, Vec3};
use crate::world::{EntIdx, World};

/// Run a geometry's slot 10 on the thread's arena (primitive `Intersect`, or the generic one).
fn geom_intersect(
    s: &mut ThreadScratch,
    a: &dyn Geometry,
    b: &dyn Geometry,
    gwd_a: &mut GeomWorldData,
    gwd_b: &GeomWorldData,
    ip: &mut IntersectionParams,
) -> IntersectOut {
    let env = crate::collision::prim_intersect::env_with_generic();
    if a.is_a_primitive() {
        crate::collision::prim_intersect::intersect(&env, s, a, b, gwd_a, gwd_b, ip)
    } else {
        match env.generic {
            Some(f) => f(s, &env, a, b, gwd_a, gwd_b, ip),
            None => IntersectOut::default(),
        }
    }
}

/// `min_idiom(a, b)` written `((a + b) − |b − a|)·0.5` with the operands as given.
#[inline]
fn mn(a: f32, b: f32) -> f32 {
    ((a + b) - (b - a).abs()) * 0.5
}

/// `max_idiom` written `(|a − b| + (a + b))·0.5`.
#[inline]
fn mx(a: f32, b: f32) -> f32 {
    ((a - b).abs() + (a + b)) * 0.5
}

/// `((i − x) ^ (i − x − 1)) >> 31` negated: 1 when `i == x`.
#[inline]
fn eq1(i: i32, x: i32) -> i32 {
    let d = i.wrapping_sub(x);
    ((d ^ d.wrapping_sub(1)) >> 31).wrapping_neg()
}

/// Slot 72 `RemoveContacts(ipart, iside, pentOther, iotherside, bWake)`: `CRigidEntity`
/// 0x350AAAE0 frees the contacts whose part index on side `side` is `ipart` and whose entity
/// on side `other_side` is `other` (`sub_350AA810` with `bWake`); the base class has no slot.
pub fn remove_contacts(
    w: &mut World,
    e: EntIdx,
    ipart: i32,
    side: usize,
    other: EntIdx,
    other_side: usize,
    wake: bool,
) {
    let Some(r) = w.ent(e).rigid() else {
        return;
    };
    let head = r.contacts_head;
    let mut k = w.contacts.get(head).next;
    while k != head {
        let rec = *w.contacts.get(k);
        let next = rec.next;
        if rec.ipart[side] == ipart && rec.pent[other_side] == other {
            crate::entities::rigid::contacts::remove_contact(w, e, k, -1, wake);
            w.contacts.free_push_front(k);
        }
        k = next;
    }
}

/// Slot 64 `GetRigidBody` of an entity: its body id (the static dummy, reset, for the base
/// class).
fn rigid_body_id(w: &mut World, e: EntIdx) -> crate::solver::BodyId {
    crate::entities::rigid::colliders::body_id_of(w, e)
}

/// Slot 78 0x3510C940 (see the module docs).
pub fn check_additional_geometry(w: &mut World, e: EntIdx, dt: f32) {
    let v633 = dt;
    let tid = w.tls_thread.clamp(0, 4) as usize;
    // the two geom_world_data on the stack (identity, zero motion); the wheel's one keeps the
    // fields an iteration does not write (v, w, com) from the previous wheel
    let mut gwd1 = GeomWorldData::default(); // v581
    let mut gwd2 = GeomWorldData::default(); // v597
    let mut ip = IntersectionParams::default(); // v553
    let mut ray = RayGeom::new(&RayPrim {
        origin: Vec3::ZERO,
        dir: Vec3::ZERO,
    });
    let caller = crate::entities::interface::caller(w);
    let (bb_lo, bb_hi) = {
        let r = w.ent(e).rigid().expect("rigid");
        (r.bbox_new[0], r.bbox_new[1])
    };
    let list = w.get_entities_in_box(bb_lo, bb_hi, 327, Some(e), caller);
    let self_minv = w.bodies.get(w.ent(e).rigid().expect("rigid").body).minv;
    let mut kept: Vec<EntIdx> = Vec::new();
    for &o in &list {
        if o == e {
            continue;
        }
        if crate::entities::mass_inv(w, o) >= self_minv * 5.0 {
            if w.ent(e).rigid().expect("rigid").awake() {
                wake_light(w, e, o);
            }
        } else {
            kept.push(o);
        }
    }
    let v172 = kept.len();
    let (qn, pn) = {
        let r = w.ent(e).rigid().expect("rigid");
        (r.q_new, r.pos_new)
    };
    // down = q_new·(0, 0, −1) (v527, v517, v512), forward = q_new·(0, 1, 0) (v537, v513, v514)
    let (v527, v517, v512) = {
        let (qx, qy, qz, qw) = (qn.x, qn.y, qn.z, qn.w);
        let v119 = qz * 0.0;
        let v121 = qw * 1.0;
        let v122 = qw * 0.0;
        let v123 = ((qy * -1.0) - v119) + v122;
        let v124 = (v119 - (qx * -1.0)) + v122;
        let v126 = ((qx * 0.0) - (qy * 0.0)) - v121;
        let v127 = (qx * v124) - (qy * v123);
        let v129 = (qz * v123) - (qx * v126);
        (
            ((qy * v126) - (qz * v124)) * 2.0,
            v129 * 2.0,
            v127 + (v127 - 1.0),
        )
    };
    let (v537, v513, v514) = {
        let (qx, qy, qz, qw) = (qn.x, qn.y, qn.z, qn.w);
        let v130 = qy * 0.0;
        let v134 = (v130 - qz) + (qw * 0.0);
        let v135 = ((qz * 0.0) - (qx * 0.0)) + qw;
        let v136 = (qx - v130) + (qw * 0.0);
        let v513 = (qz * v134) - (qx * v136);
        (
            ((qy * v136) - (qz * v135)) * 2.0,
            v513 + (v513 + 1.0),
            ((qx * v135) - (qy * v134)) * 2.0,
        )
    };
    ip.vrel_min = f32::from_bits(1_343_554_297); // 1e10
    ip.max_surface_gap_angle = f32::from_bits(1_032_780_341);
    let n_hull = wd(w, e).n_hull;
    let n_parts = w.ent(e).parts.len() as i32;
    let nw = n_parts - n_hull;
    // v476: the "sleepy" mode (see the module docs)
    let v632 = (0.001 - w.ent(e).max_time_idle) * ((w.ent(e).flags & 8) as f32);
    let v476 = v632.to_bits() >> 31 != 0;
    let mut v468 = [-1i32; 2]; // rearmost per side
    let mut v467 = [-1i32; 2]; // frontmost per side
    if v476 {
        for i in 0..nw.max(0) {
            let wh = &wd(w, e).wheels[i as usize];
            if wh.len_max > 0.0 {
                let v151 = wh.pivot.y;
                let side = (wh.pivot.x.to_bits() >> 31) as usize;
                let v153 = ((i + 1) & (v468[side] >> 31)) + v468[side];
                let v154 = ((i + 1) & (v467[side] >> 31)) + v467[side];
                let v634 = v151 - wd(w, e).wheels[v153 as usize].pivot.y;
                v468[side] = v153 + ((i - v153) & ((v634.to_bits() >> 31) as i32).wrapping_neg());
                let v635 = wd(w, e).wheels[v154 as usize].pivot.y - v151;
                v467[side] = v154 + ((i - v154) & ((v635.to_bits() >> 31) as i32).wrapping_neg());
            }
        }
    }
    // the hull contacts and the wheels in contact (v147 → v496)
    let v496 = {
        let d = wd(w, e);
        let mut n = d.n_hull_contacts;
        for i in 0..nw.max(0) as usize {
            n += i32::from(d.wheels[i].flags & wflags::CONTACT != 0);
        }
        n
    };
    let mut v483: u32 = 0;
    // `v487` / `v486`: the direction of the last accepted wheel contact (a function-level
    // local of the binary; only read for a wheel that accepted a contact)
    let mut best_dir = Vec3::ZERO;
    for v174 in n_hull..n_parts {
        let v176 = v174 - n_hull;
        let iw = v176 as usize;
        let pi = v174 as usize;
        {
            let pf = wd(w, e).wheels[iw].part_flags;
            let p = &mut w.ent_mut(e).parts[pi];
            p.flags = pf | 0x4000_0000;
            p.flags_collider = 0;
        }
        let wh0 = wd(w, e).wheels[iw].clone();
        let mut v554 = wh0.len_max * 2.0;
        let v504 = if wh0.steer == 0.0 {
            Quat::new(0.0, 0.0, 0.0, 1.0)
        } else {
            let v181 = wh0.steer * 0.5;
            let v182 = crt::sinf(v181);
            Quat::new(v182 * 0.0, v182 * 0.0, v182 * -1.0, crt::cosf(v181))
        };
        // the axle direction q_new ⊗ steer · (sgnnz(pivot.x), 0, 0)
        let v186 = ((((wh0.pivot.x.to_bits() as i32) >> 30) & !1) + 1) as f32;
        let q = qn.product(v504);
        let v187 = ((q.z * v186) - (q.x * 0.0)) + (q.w * 0.0);
        let v188 = v187 * q.x;
        let tz = ((q.x * 0.0) - (q.y * v186)) + (q.w * 0.0);
        let ux = (tz * q.y) - (v187 * q.z);
        let v471 = ux + (ux + v186);
        let v189 = ((q.y * 0.0) - (q.z * 0.0)) + (q.w * v186);
        let v470 = ((v189 * q.z) - (tz * q.x)) * 2.0;
        let v469 = (v188 - (v189 * q.y)) * 2.0;
        // wheel orientation: steer ⊗ q0
        let wq = v504.product(wh0.q0);
        wm(w, e).wheels[iw].q = wq;
        gwd1.r = Mat33::from_quat(qn.product(wq));
        let (origin, wgeom, part_scale) = {
            let p = &w.ent(e).parts[pi];
            (p.pgeom.origin, p.pgeom_proxy.geom.clone(), p.scale)
        };
        let v199 = wq.rotate(origin);
        let v203 = v199.y + wh0.pos0.y;
        let v204 = v199.z + wh0.pos0.z;
        let v205 = v199.x + wh0.pos0.x;
        let c0r = qn.rotate(Vec3::new(v205, v203, v204));
        let v488 = c0r.x + pn.x;
        let v493 = c0r.y + pn.y;
        let v490 = c0r.z + pn.z;
        let v214 = (wh0.center0.z + wh0.pos0.z) - v204;
        let v216 = (wh0.pos0.x + wh0.center0.x) - v205;
        let v219 = wh0.center0.y + wh0.pos0.y;
        let v220 = wh0.len_max - wh0.len_rest;
        let wpos = Vec3::new(v216, v219 - v203, v214 - (((-v220).abs() + v220) * 0.5));
        wm(w, e).wheels[iw].pos = wpos;
        let v221 = qn.rotate(wpos);
        let v492 = v221.x + pn.x;
        let v491 = pn.y + v221.y;
        let v489 = pn.z + v221.z;
        let v609 = Mat33::from_quat(qn.product(v504)).to_array();
        let v223 = wh0.r;
        let v224 = v223 + wh0.len_max;
        let v225 = v609[2].abs() * v224;
        let v228 = v609[8].abs() * v224;
        let v230 = v609[5].abs() * v224;
        let v233 = ((v609[0].abs() * wh0.width) + (v609[1].abs() * v223)) + v225;
        let v234 = ((wh0.width * v609[6].abs()) + (v609[7].abs() * v223)) + v228;
        let si = ((wh0.width * v609[3].abs()) + (v609[4].abs() * v223)) + v230;
        let bc = qn.rotate(Vec3::new(
            wh0.center0.x,
            wh0.center0.y,
            wh0.center0.z - wh0.len_rest,
        ));
        let cx = bc.x + pn.x;
        let cy = bc.y + pn.y;
        let cz = bc.z + pn.z;
        let wbox = [
            Vec3::new(cx - v233, cy - si, cz - v234),
            Vec3::new(cx + v233, cy + si, cz + v234),
        ];
        gwd1.scale = part_scale;
        let epos = w.ent(e).pos;
        let mut v245 = 0.0f32;
        let mut v246 = 0.0f32;
        let mut v247 = 0.0f32;
        let mut v480 = 0.0f32;
        let mut v632 = 0.0f32;
        let mut v479 = 0.0f32;
        let mut v481 = 0.0f32;
        let mut v484 = 0.0f32;
        {
            let wh = &mut wm(w, e).wheels[iw];
            wh.pt = epos;
            wh.flags &= !wflags::CONTACT;
            wh.contact_body = None;
        }
        let side = (wh0.pivot.x.to_bits() >> 31) as usize;
        let process =
            !v476 || ((eq1(v176, v468[side]) | eq1(v176, v467[side])) as f32) * wh0.len_max != 0.0;
        if !process {
            continue;
        }
        let v250 = wh0.width;
        let (v251, v252, v253) = (v517, v527, v512);
        let mut ray_mode = false; // v635 != 0
        let v254 = wh0.len_max;
        let v255 = v254 + wh0.len_rest;
        ray.ndir = Vec3::new(v527, v517, v512);
        let v256 = v554 + wh0.r;
        ray.prim.origin = Vec3::new(
            (v488 - (v255 * v527)) + (v250 * v471),
            (v493 - (v255 * v517)) + (v250 * v470),
            (v490 - (v255 * v512)) + (v250 * v469),
        );
        ray.prim.dir = Vec3::new(v256 * v527, v256 * v517, v256 * v512);
        gwd1.offset = Vec3::new(v492, v491, v489);
        if v254 <= 0.0 {
            ip.sweep_test = false;
            let v258 = w.ent(e).q.rotate(wpos);
            let b = *w.bodies.get(w.ent(e).rigid().expect("rigid").body);
            let v262 = pn.y + v258.y;
            let v263 = pn.z + v258.z;
            let v264 = v258.x + pn.x;
            gwd1.w = b.w;
            let v265 = v263 - b.pos.z;
            let v266 = ((v265 * b.w.y) - ((v262 - b.pos.y) * b.w.z)) + b.v.x;
            let v267 = (((v262 - b.pos.y) * b.w.x) - ((v264 - b.pos.x) * b.w.y)) + b.v.z;
            let v268 = (((v264 - b.pos.x) * b.w.z) - (v265 * b.w.x)) + b.v.y;
            gwd1.v = Vec3::new(v266, v268, v267);
            gwd1.center_of_mass = b.pos;
            v554 = v633 * 1.4;
        } else if wh0.flags & wflags::RAYCAST == 0 {
            ip.sweep_test = true;
            v554 = v254;
            gwd1.v = Vec3::new(v527, v517, v512);
            gwd1.offset = Vec3::new(
                v492 - (v254 * v527),
                v491 - (v517 * v254),
                v489 - (v512 * v254),
            );
        } else {
            ray_mode = true;
            ip.sweep_test = false;
        }
        ip.time_interval = v554;
        for v271 in 0..v172 {
            let o = kept[v271];
            let mut v273 = 0i32;
            while v273 < w.ent(o).parts.len() as i32 {
                let cpart = w.ent(o).parts[v273 as usize].clone();
                let v279 = cpart.flags;
                let pb = cpart.bbox;
                let deg = ((pb[1].x - pb[0].x) * (pb[1].x - pb[0].x)
                    + (pb[1].y - pb[0].y) * (pb[1].y - pb[0].y))
                    + (pb[1].z - pb[0].z) * (pb[1].z - pb[0].z)
                    == 0.0;
                let mask = wd(w, e).wheels[iw].part_flags_collider;
                if (v279 & mask) == 0 || !(deg || crate::world::queries::aabb_overlap6(&pb, &wbox))
                {
                    v483 |= v279 & 0x200_0000;
                    v273 += 1;
                    continue;
                }
                let (opos, oq) = (w.ent(o).pos, w.ent(o).q);
                let r = oq.rotate(cpart.pos);
                gwd2.offset = Vec3::new(opos.x + r.x, r.y + opos.y, r.z + opos.z);
                gwd2.r = Mat33::from_quat(oq.product(cpart.q));
                gwd2.scale = cpart.scale;
                let og = cpart.pgeom_proxy.geom.clone();
                let out = if !ray_mode {
                    let s = &mut w.tscratch[tid];
                    geom_intersect(s, wgeom.as_ref(), og.as_ref(), &mut gwd1, &gwd2, &mut ip)
                } else {
                    ip.sweep_test = false;
                    let ident = GeomWorldData::default();
                    let s = &mut w.tscratch[tid];
                    let out = geom_intersect(s, og.as_ref(), &ray, &mut gwd2, &ident, &mut ip);
                    let first = out.first.unwrap_or(0);
                    for j in 0..out.ret.max(0) {
                        let off = ThreadScratch::contact_off(first + j) + i64::from(c::N);
                        let n = s.rd_v(off);
                        s.wr_v(off, Vec3::new(-n.x, -n.y, -n.z));
                    }
                    ip.sweep_test = true;
                    out
                };
                let first = out.first.unwrap_or(0);
                let v311 = if ray_mode { 1.0f32 } else { 0.0 };
                for j in 0..out.ret.max(0) {
                    let o_c = ThreadScratch::contact_off(first + j);
                    let (t, pt, n, dir, vel, ids) = {
                        let s = &mut w.tscratch[tid];
                        let t = s.rd_d(o_c) - f64::from(v311 * wh0.r);
                        s.wr_d(o_c, t);
                        (
                            t,
                            s.rd_v(o_c + i64::from(c::PT)),
                            s.rd_v(o_c + i64::from(c::N)),
                            s.rd_v(o_c + i64::from(c::DIR)),
                            s.rd_f(o_c + i64::from(c::VEL)),
                            [
                                s.rd_i(o_c + i64::from(c::ID)),
                                s.rd_i(o_c + i64::from(c::ID) + 4),
                            ],
                        )
                    };
                    let mut n = n;
                    let v318: f32 = if !ip.sweep_test {
                        let mut use84 = true;
                        if vel > 0.0 {
                            if v496 == 0
                                && t * t
                                    > f64::from(((v246 * v246) + (v247 * v247)) + (v245 * v245))
                            {
                                let v313 = t as f32;
                                v247 = dir.x * v313;
                                v246 = dir.y * v313;
                                v245 = dir.z * v313;
                            }
                        } else if !(((dir.y * n.y) + (dir.x * n.x)) + (dir.z * n.z)
                            <= -0.800_000_01)
                        {
                            n = Vec3::new(-dir.x, -dir.y, -dir.z);
                            let s = &mut w.tscratch[tid];
                            s.wr_v(o_c + i64::from(c::N), n);
                            // `if (!bSweep) goto LABEL_84` — always taken here
                            use84 = true;
                        }
                        if use84 {
                            (f64::from(-(((v251 * dir.y) + (v252 * dir.x)) + (v253 * dir.z))) * t)
                                as f32
                        } else {
                            (f64::from(v554) - t) as f32
                        }
                    } else {
                        (f64::from(v554) - t) as f32
                    };
                    let v319 = v318;
                    v484 += -v319 * n.x;
                    v481 += -v319 * n.y;
                    let v320 = v513 * n.y;
                    v479 += -v319 * n.z;
                    let fwd = ((v320 + (v537 * n.x)) + (v514 * n.z)).abs();
                    let below =
                        (((pt.y - v491) * v251) + (v252 * (pt.x - v492))) + ((pt.z - v489) * v253);
                    if fwd >= wd(w, e).min_contact_cos || below <= 0.0 {
                        if ((v251 * n.y) + (v252 * n.x)) + (v253 * n.z) > -0.009_999_999_8 {
                            let ob = rigid_body_id(w, o);
                            let ominv = w.bodies.get(ob).minv;
                            let d = wd(w, e);
                            if self_minv * 10.0 > ominv
                                && (d.k_steer_tank == 0.0 || d.wheels[iw].len_max == 0.0)
                                && v480 <= v319
                            {
                                v480 = v319;
                            }
                        }
                    } else {
                        if v319 > v632 {
                            v632 = v319;
                            best_dir = dir;
                            let body = rigid_body_id(w, o);
                            let s0 =
                                crate::entities::rigid::colliders::map_material(w, e, ids[0], v174);
                            let s1 =
                                crate::entities::rigid::colliders::map_material(w, o, ids[1], v273);
                            {
                                let wh = &mut wm(w, e).wheels[iw];
                                wh.flags |= wflags::CONTACT;
                                wh.contact_part = v273;
                                wh.contact_ent = o;
                                wh.contact_body = Some(body);
                                wh.surf = [s0, s1];
                                let v333 = wh.width
                                    - (((v471 * (pt.x - v488)) + ((pt.y - v493) * v470))
                                        + ((pt.z - v490) * v469));
                                let z = (v469 * v333) + pt.z;
                                let x = (v471 * v333) + pt.x;
                                wh.pt = Vec3::new(x, (v470 * v333) + pt.y, z);
                                wh.n = Vec3::new(-n.x, -n.y, -n.z);
                            }
                            let terrain = (w.ent(o).id_bits & 0x7F_FFFF) == 0x7F_FFFF;
                            if v483 != 0 && terrain {
                                area_surface(w, e, iw, &kept, v172);
                            }
                            if terrain {
                                ip.sweep_test = false;
                                let og = w.ent(o).parts[v273 as usize].pgeom_proxy.geom.clone();
                                let ident = GeomWorldData::default();
                                let out2 = {
                                    let s = &mut w.tscratch[tid];
                                    geom_intersect(s, og.as_ref(), &ray, &mut gwd2, &ident, &mut ip)
                                };
                                if out2.ret > 0 {
                                    let f0 = out2.first.unwrap_or(0);
                                    let id = w.tscratch[tid]
                                        .rd_i(ThreadScratch::contact_off(f0) + i64::from(c::ID));
                                    // (`v482`: the part index of the candidate loop)
                                    let s1 = crate::entities::rigid::colliders::map_material(
                                        w, o, id, v273,
                                    );
                                    wm(w, e).wheels[iw].surf[1] = s1;
                                }
                                ip.sweep_test = true;
                            }
                        }
                        crate::entities::add_collider(w, e, o);
                        crate::entities::add_collider(w, o, e);
                    }
                }
                v273 += 1;
            }
        }
        let mut v364 = v632;
        if v480 > v632 * 1.01 {
            let (pf, pfc, keep) = {
                let d = wd(w, e);
                (
                    d.wheels[iw].part_flags,
                    d.wheels[iw].part_flags_collider,
                    d.keep_contact,
                )
            };
            {
                let p = &mut w.ent_mut(e).parts[pi];
                p.flags = pf;
                p.flags_collider = pfc;
            }
            wm(w, e).wheels[iw].flags &= (((keep as u8) as u32) << 6) | 0xFFFF_FFBF;
        }
        {
            let bid = w.ent(e).rigid().expect("rigid").body;
            let r = w.ent_mut(e).rigid_mut().expect("rigid");
            let v365 = v246 + r.pos_new.y;
            r.pos_new.x = v247 + r.pos_new.x;
            r.pos_new.y = v365;
            r.pos_new.z = v245 + r.pos_new.z;
            let b = w.bodies.get_mut(bid);
            let v366 = v245 + b.pos.z;
            let v367 = v246 + b.pos.y;
            b.pos.x = v247 + b.pos.x;
            b.pos.z = v366;
            b.pos.y = v367;
        }
        let mut v368 = wd(w, e).wheels[iw].len_max;
        if wd(w, e).wheels[iw].flags & wflags::CONTACT != 0 {
            let b = *w.bodies.get(w.ent(e).rigid().expect("rigid").body);
            let (pt, cb, cent) = {
                let wh = &wd(w, e).wheels[iw];
                (wh.pt, wh.contact_body, wh.contact_ent)
            };
            let v374 = pt.y - b.pos.y;
            let v375 = pt.z - b.pos.z;
            let v376 = pt.x - b.pos.x;
            let mut vs = Vec3::new(
                ((b.w.y * v375) - (b.w.z * v374)) + b.v.x,
                ((b.w.z * v376) - (b.w.x * v375)) + b.v.y,
                ((b.w.x * v374) - (b.w.y * v376)) + b.v.z,
            );
            if let Some(cbid) = cb {
                let ob = *w.bodies.get(cbid);
                let v386 = pt.y - ob.pos.y;
                let v387 = pt.z - ob.pos.z;
                let v388 = pt.x - ob.pos.x;
                let v389 = ((ob.w.y * v387) - (ob.w.z * v386)) + ob.v.x;
                let v390 = ob.w.x * v387;
                let v392 = ((ob.w.z * v388) - v390) + ob.v.y;
                let v393 = ((ob.w.x * v386) - (ob.w.y * v388)) + ob.v.z;
                vs = Vec3::new(vs.x - v389, vs.y - v392, vs.z - v393);
            }
            wm(w, e).wheels[iw].vslip = vs;
            remove_contacts(w, e, v174, 0, cent, 1, v364 == 0.0);
            remove_contacts(w, cent, v174, 1, e, 1, true);
            let cid = wd(w, e).wheels[iw].contact;
            if ip.sweep_test {
                if v364 > 0.0 {
                    v364 += w.vars.contact_gap * 0.5;
                }
                let v395 = wd(w, e).wheels[iw].len_max;
                v368 = ((-(v395 - v364)).abs() + (v395 - v364)) * 0.5;
                if v364 <= v395 {
                    w.contacts.get_mut(cid).vreq = Vec3::ZERO;
                } else {
                    let v397 = ((v364 - v395) * w.vars.unproject_vel_gain) * 2.0;
                    let k = w.vars.max_unproject_vel * 0.699_999_99;
                    let v398 = -(((v397 + k) - (v397 - k).abs()) * 0.5);
                    w.contacts.get_mut(cid).vreq = Vec3::new(v398 * v252, v251 * v398, v253 * v398);
                }
            } else {
                let v399 = ((v481 * v481) + (v484 * v484)) + (v479 * v479);
                let nn = if v399 <= 9.999_999_7e-21 {
                    Vec3::new(0.0, 0.0, 1.0)
                } else {
                    let v400 = 1.0 / v399.sqrt();
                    Vec3::new(v484 * v400, v481 * v400, v479 * v400)
                };
                wm(w, e).wheels[iw].n = nn;
                let dir = best_dir;
                let gap = w.vars.contact_gap;
                let v411 = ((((-(v364 - gap)).abs() + (v364 - gap)) * 0.5)
                    * w.vars.unproject_vel_gain)
                    * 2.0;
                let k = w.vars.max_unproject_vel * 0.699_999_99;
                let v412 = ((v411 + k) - (k - v411).abs()) * 0.5;
                let v413 = ((v471 * dir.x) + (dir.y * v470)) + (dir.z * v469);
                let v414 = (dir.z - (v469 * v413)) * v412;
                w.contacts.get_mut(cid).vreq = Vec3::new(
                    (dir.x - (v471 * v413)) * v412,
                    (dir.y - (v470 * v413)) * v412,
                    v414,
                );
            }
        }
        let wh = &mut wm(w, e).wheels[iw];
        let v415 = (v633 * 7.0) + wh.len;
        let v416 = v415 + v368;
        let v417 = v368 - v415;
        let v418 = wh.len_max;
        let v419 = (v416 - v417.abs()) * 0.5;
        wh.len = v419;
        wh.pos.z = (v418 - v419) + wh.pos.z;
    }
    if v476 {
        interpolate(w, e, &v468, &v467);
    }
}

/// Light neighbours: woken when a part box overlaps a wheel's steering-swept box (all in the
/// vehicle's new frame).
fn wake_light(w: &mut World, e: EntIdx, o: EntIdx) {
    let bb = w.ent(e).bbox;
    let v17 = mx(bb[1].x - bb[0].x, bb[1].y - bb[0].y);
    let v18 = mx(v17, bb[1].z - bb[0].z);
    let (mut lo, mut hi) = ([v18; 3], [-v18; 3]);
    let (qn, pn) = {
        let r = w.ent(e).rigid().expect("rigid");
        (r.q_new, r.pos_new)
    };
    let (opos, oq) = (w.ent(o).pos, w.ent(o).q);
    let rel = qn.conj().product(oq);
    let parts = w.ent(o).parts.clone();
    for p in &parts {
        let bx = p.pgeom_proxy.geom.bbox();
        let rr = Mat33::from_quat(rel.product(p.q)).m;
        let b = bx.basis.m;
        let mut bp = [[0.0f32; 3]; 3];
        for i in 0..3 {
            for j in 0..3 {
                bp[i][j] = ((b[i][1] * rr[j][1] + b[i][0] * rr[j][0]) + b[i][2] * rr[j][2]).abs();
            }
        }
        let s = bx.size;
        let ps = p.scale;
        let ex = (((s.x * bp[0][0]) + (s.y * bp[1][0])) + (s.z * bp[2][0])) * ps;
        let ey = ps * (((s.x * bp[0][1]) + (s.y * bp[1][1])) + (s.z * bp[2][1]));
        let ez = ps * (((s.x * bp[0][2]) + (s.y * bp[1][2])) + (s.z * bp[2][2]));
        let cr = p.q.rotate(bx.center);
        let c1 = Vec3::new(
            cr.x * ps + p.pos.x,
            ps * cr.y + p.pos.y,
            ps * cr.z + p.pos.z,
        );
        let c2 = oq.rotate(c1);
        let d = Vec3::new(
            (c2.x + opos.x) - pn.x,
            (c2.y + opos.y) - pn.y,
            (c2.z + opos.z) - pn.z,
        );
        let cl = crate::entities::base::geometry::rotate_inv(qn, d);
        let c = [cl.x, cl.y, cl.z];
        let ext = [ex, ey, ez];
        for k in 0..3 {
            lo[k] = mn(c[k] - ext[k], lo[k]);
            hi[k] = mx(hi[k], c[k] + ext[k]);
        }
    }
    let obox = [
        Vec3::new(lo[0], lo[1], lo[2]),
        Vec3::new(hi[0], hi[1], hi[2]),
    ];
    let nw = n_wheels(w, e);
    for i in 0..nw.max(0) as usize {
        let wh = &wd(w, e).wheels[i];
        let v102 = wh.steer;
        let v103 = wh.center0.x;
        let v104 = wh.center0.y;
        let v105 = wh.center0.z + ((wh.len_max * 0.5) - wh.len_rest);
        let (v106, v107) = if v102 == 0.0 {
            (1.0, 0.0)
        } else {
            (crt::cosf(v102).abs(), crt::sinf(v102).abs())
        };
        let v108 = wh.r;
        let v109 = wh.width;
        let v110 = (v108 * v106) + (v109 * v107);
        let v111 = (v108 * v107) + (v109 * v106);
        let wbox = [
            Vec3::new(v103 - v111, v104 - v110, v105 - v108),
            Vec3::new(v103 + v111, v104 + v110, v105 + v108),
        ];
        if crate::world::queries::aabb_overlap6(&obox, &wbox) {
            crate::entities::awake(w, o, 1, 5);
            break;
        }
    }
}

/// The ground surface of a wheel standing on the terrain where a candidate has parts with flag
/// `0x2000000` (`v483`): the last such part (of any candidate) containing the contact point
/// gives the surface (its `phys_geometry`'s surface index, mapped).
fn area_surface(w: &mut World, e: EntIdx, iw: usize, kept: &[EntIdx], n: usize) {
    for &o in kept.iter().take(n) {
        let np = w.ent(o).parts.len();
        for k in 0..np {
            let part = w.ent(o).parts[k].clone();
            if part.flags & 0x200_0000 == 0 {
                continue;
            }
            let (opos, oq) = (w.ent(o).pos, w.ent(o).q);
            let pt = wd(w, e).wheels[iw].pt;
            let v577 = Vec3::new(pt.x - opos.x, pt.y - opos.y, pt.z - opos.z);
            let v350 = crate::entities::base::geometry::rotate_inv(oq, v577);
            let v502 = Vec3::new(
                v350.x - part.pos.x,
                v350.y - part.pos.y,
                v350.z - part.pos.z,
            );
            let v360 = crate::entities::base::geometry::rotate_inv(part.q, v502);
            if part.pgeom.geom.point_inside_status(v360) != 0 {
                let s = crate::entities::rigid::colliders::map_material(
                    w,
                    o,
                    part.pgeom.surface_idx,
                    k as i32,
                );
                wm(w, e).wheels[iw].surf[1] = s;
            }
        }
    }
}

/// Step 6 of [`check_additional_geometry`]: every wheel with `lenMax > 0` that is not the
/// rearmost / frontmost one of its side copies the contact of the nearer of the two (when that
/// one is in contact) — flag, ground entity / part / body, surfaces, normal — removes its rigid
/// contacts with the ground (slot 72; **bug kept**: with the *wheel index of the end wheel*
/// as the part index), takes the length interpolated linearly in `y` between the two, and
/// gets its contact point, record position, slip velocity and a zero push-out velocity.
fn interpolate(w: &mut World, e: EntIdx, v468: &[i32; 2], v467: &[i32; 2]) {
    let py = |w: &World, i: i32| wd(w, e).wheels[i as usize].pivot.y;
    let v421 = py(w, v467[0]) - py(w, v468[0]);
    let v422 = py(w, v467[1]) - py(w, v468[1]);
    let v575 = [
        1.0 / (((0.000_001 - v421).abs() + (v421 + 0.000_001)) * 0.5),
        1.0 / (((0.000_001 - v422).abs() + (v422 + 0.000_001)) * 0.5),
    ];
    let nw = n_wheels(w, e);
    for v420 in 0..nw.max(0) {
        let iw = v420 as usize;
        let wh = wd(w, e).wheels[iw].clone();
        let v425 = wh.len_max;
        let side = (wh.pivot.x.to_bits() >> 31) as usize;
        let v427 = v467[side];
        let v428 = v468[side];
        if !(v425 > ((2 * (eq1(v420, v428) | eq1(v420, v427))) as f32) * v425) {
            continue;
        }
        let v429 = v575[side];
        let v430 = wh.pivot.y;
        let v431 = (py(w, v427) - v430) * v429;
        let v432 = (v430 - py(w, v428)) * v429;
        let v433 = v428 + ((v427 - v428) & (((v431 - v432).to_bits() >> 31) as i32).wrapping_neg());
        let src = wd(w, e).wheels[v433 as usize].clone();
        if src.flags & wflags::CONTACT == 0 {
            continue;
        }
        {
            let d = wm(w, e);
            let t = &mut d.wheels[iw];
            t.flags |= wflags::CONTACT;
            t.contact_ent = src.contact_ent;
            t.contact_part = src.contact_part;
            t.contact_body = src.contact_body;
            t.surf = src.surf;
            t.n = src.n;
        }
        remove_contacts(w, e, v433, 0, src.contact_ent, 1, false);
        remove_contacts(w, src.contact_ent, v433, 1, e, 1, true);
        let len_rear = wd(w, e).wheels[v428 as usize].len;
        let len_front = wd(w, e).wheels[v427 as usize].len;
        let v435 = v431 * len_rear;
        let v436 = v432 * len_front;
        let v438 = v436 + v435;
        wm(w, e).wheels[iw].len = v438;
        let sgn = (1 - 2 * side as i32) as f32;
        let v498 = Vec3::new(wh.pivot.x + sgn * wh.width, wh.pivot.y, wh.pivot.z - v438);
        let (qn, pn) = {
            let r = w.ent(e).rigid().expect("rigid");
            (r.q_new, r.pos_new)
        };
        let v440 = qn.rotate(v498);
        let pt = Vec3::new(pn.x + v440.x, pn.y + v440.y, pn.z + v440.z);
        let b = *w.bodies.get(w.ent(e).rigid().expect("rigid").body);
        let v448 = pt.y - b.pos.y;
        let v449 = pt.z - b.pos.z;
        let v450 = pt.x - b.pos.x;
        let v451 = ((v449 * b.w.y) - (v448 * b.w.z)) + b.v.x;
        let v452 = ((b.w.z * v450) - (v449 * b.w.x)) + b.v.y;
        let v453 = ((v448 * b.w.x) - (b.w.y * v450)) + b.v.z;
        let mut vs = Vec3::new(v451, v452, v453);
        if let Some(cb) = src.contact_body {
            let ob = *w.bodies.get(cb);
            let v460 = pt.x - ob.pos.x;
            let v462 = pt.z - ob.pos.z;
            let dy = pt.y - ob.pos.y;
            let v463 = ((v462 * ob.w.y) - (dy * ob.w.z)) + ob.v.x;
            let v464 = ((dy * ob.w.x) - (ob.w.y * v460)) + ob.v.z;
            let vy = ((ob.w.z * v460) - (v462 * ob.w.x)) + ob.v.y;
            vs = Vec3::new(v451 - v463, vs.y - vy, v453 - v464);
        }
        let cid = {
            let t = &mut wm(w, e).wheels[iw];
            t.pt = pt;
            t.pos.z = (t.len_max - t.len) + t.pos.z;
            t.vslip = vs;
            t.contact
        };
        w.contacts.get_mut(cid).vreq = Vec3::ZERO;
    }
}
