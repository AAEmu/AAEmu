//! The solver-facing slots of `CLivingEntity`: RegisterContacts (40), Update (41), CalcEnergy
//! (42), OnContact (59), GetRigidBody (64), GetRigidBodyData (65), the contact-matrix
//! diagonals (67/68), and the collision event `sub_35026300`.

#![allow(
    clippy::neg_cmp_op_on_partial_ord,
    clippy::excessive_precision,
    clippy::float_cmp,
    clippy::too_many_lines,
    clippy::too_many_arguments
)]

use super::{ld, lm};
use crate::math::{crt, Quat, Vec3};
use crate::solver::{BodyId, ContactId, RigidBody};
use crate::world::events::Event;
use crate::world::{EntIdx, World};

/// Slot 64 `GetRigidBody(ipart, bWillModify)` (0x35027700): without `bWillModify` the base
/// static dummy; otherwise the proxy body (created on first use with `Ibody = (1,1,1)`,
/// volume `r·r·hh·8` and the living mass), refreshed every call: CoM `pos + up·heightCollider`,
/// `q`, `M`, `v = vel`, `P = v·M`, `Minv`, zero inverse inertia (no rotation), zero `w`/`L`.
/// The impulse-recover timer restarts when inertia is 0.
pub fn get_rigid_body(w: &mut World, e: EntIdx, will_modify: bool) -> BodyId {
    if !will_modify {
        w.bodies.get_mut(BodyId::STATIC).reset_static_dummy();
        return BodyId::STATIC;
    }
    let (pos, q) = (w.ent(e).pos, w.ent(e).q);
    let id = match ld(w, e).proxy {
        Some(id) => id,
        None => {
            let id = w.bodies.alloc(u64::from(e.0) + 1);
            let l = ld(w, e);
            let v = ((l.size.y * l.size.x) * l.size.z) * 8.0;
            let m = l.mass;
            let b = w.bodies.get_mut(id);
            *b = RigidBody::default();
            crate::entities::base::geometry::body_create(
                b,
                pos,
                Vec3::new(1.0, 1.0, 1.0),
                q,
                v,
                m,
                q,
                pos,
            );
            lm(w, e).proxy = Some(id);
            id
        }
    };
    let up = q.rotate(Vec3::new(0.0, 0.0, 1.0));
    let l = ld(w, e);
    let hc = l.height_collider;
    let (mass, mass_inv, vel) = (l.mass, l.mass_inv, l.vel);
    let b = w.bodies.get_mut(id);
    b.pos = Vec3::new(up.x * hc + pos.x, up.y * hc + pos.y, up.z * hc + pos.z);
    b.q = q;
    b.m = mass;
    b.v = vel;
    let mm = b.m;
    b.p = Vec3::new(vel.x * mm, vel.y * mm, vel.z * mm);
    b.minv = mass_inv;
    b.ibody_inv = Vec3::ZERO;
    b.iinv = crate::math::Mat33::ZERO;
    b.w = Vec3::ZERO;
    b.l = Vec3::ZERO;
    let l = lm(w, e);
    if l.inertia == 0.0 {
        l.impulse_recover = l.impulse_recover_time;
    }
    id
}

/// Slot 65 (0x35037D40): a body record with pos, vel, mass and inverse mass (the rest of the
/// record zeroed by `sub_35098F60`).
#[must_use]
pub fn rigid_body_data(w: &World, e: EntIdx) -> RigidBody {
    let l = ld(w, e);
    RigidBody {
        pos: w.ent(e).pos,
        v: l.vel,
        m: l.mass,
        minv: l.mass_inv,
        ..RigidBody::default()
    }
}

/// Slot 67 (0x35037DB0): add `Minv` to the diagonal of a 3×3 contact matrix.
pub fn add_mass_inv_3x3(w: &World, e: EntIdx, k: &mut [f32; 9]) {
    let mi = ld(w, e).mass_inv;
    k[0] += mi;
    k[4] += mi;
    k[8] += mi;
}

/// Slot 68 (0x35037C60): the same for the 7-wide layout (elements 18, 25, 32).
pub fn add_mass_inv_7(w: &World, e: EntIdx, k: &mut [f32]) {
    let mi = ld(w, e).mass_inv;
    k[18] += mi;
    k[25] += mi;
    k[32] += mi;
}

/// Slot 59 `OnContact(contact, iNormal)` (0x350279B0): unless the contact has flag `0x100`,
/// remove the components of velRequested and vel that point into the contact normal (the
/// contact `n`, or for `0x400000` contacts with `iNormal ≠ 0` the alternative normal at
/// `+136`).
pub fn on_contact(w: &mut World, e: EntIdx, c: ContactId, i_normal: i32) {
    let k = *w.contacts.get(c);
    if k.flags & 0x100 != 0 {
        return;
    }
    let n = if k.flags & 0x40_0000 != 0 && i_normal != 0 {
        if i_normal == 1 {
            k.vreq
        } else {
            // the multi-normal array (`+168`) is not modelled: constraints are not ported
            k.n
        }
    } else {
        k.n
    };
    let l = lm(w, e);
    let r = l.vel_requested;
    let d = (n.x * r.x + n.y * r.y) + n.z * r.z;
    let v8 = (d - (-d).abs()) * 0.5;
    l.vel_requested.x = r.x - v8 * n.x;
    l.vel_requested.y = r.y - n.y * v8;
    l.vel_requested.z = r.z - n.z * v8;
    let v = l.vel;
    let d = (n.y * v.y + n.x * v.x) + n.z * v.z;
    let v11 = (d - (-d).abs()) * 0.5;
    l.vel.x = v.x - v11 * n.x;
    l.vel.y = v.y - n.y * v11;
    l.vel.z = v.z - v11 * n.z;
}

/// Slot 42 `CalcEnergy` (0x35026FB0): `m·|vel|²`, max'd with the proxy's `m·|v|²`; the
/// (never created) contacts add `min(10·depth, 3)²·M_other`.
#[must_use]
pub fn calc_energy(w: &World, e: EntIdx, _dt: f32) -> f32 {
    let l = ld(w, e);
    let m = l.mass;
    let v = l.vel;
    let mut en = ((v.y * v.y) + v.x * v.x + v.z * v.z) * m;
    if let Some(p) = l.proxy {
        let b = w.bodies.get(p);
        let v6 = ((b.v.x * b.v.x + b.v.y * b.v.y) + b.v.z * b.v.z) * m;
        en = ((en - v6).abs() + (v6 + en)) * 0.5;
    }
    if let Some(cs) = &l.contacts {
        for c in cs.iter().take(l.n_contacts.max(0) as usize) {
            let d = c.depth * 10.0;
            let v11 = ((d + 3.0) - (3.0 - d).abs()) * 0.5;
            let mo = body_ro(w, c.ent).m;
            en += (v11 * v11) * mo;
        }
    }
    en
}

/// Slot 40 `RegisterContacts` (0x35027190): the (never created, see the module docs of
/// [`super`]) living contacts, then — with a proxy body and not flying — one solver contact
/// between the proxy and the ground collider (or the static world entity): both points at the
/// position, the ground normal, friction 0.3.
pub fn register_contacts(w: &mut World, e: EntIdx, _dt: f32, _n_max: i32) -> i32 {
    let tid = w.tls_thread.clamp(0, 4) as usize;
    let l = ld(w, e);
    if let Some(p) = l.proxy {
        if l.flags & super::lflags::FLYING == 0 {
            let (g, gip, n) = (l.ground, l.ground_ipart, l.ground_normal);
            let pos = w.ent(e).pos;
            let (sol, pool) = w.solver_and_pool(tid);
            let c = sol.alloc_tmp_contacts(pool, 1);
            let (pent1, ip1) = if g.is_none() { (EntIdx::NONE, -1) } else { (g, gip) };
            let b1 = if g.is_none() {
                w.bodies.get_mut(BodyId::STATIC).reset_static_dummy();
                BodyId::STATIC
            } else {
                body_for_contact(w, g, false)
            };
            let k = w.contacts.get_mut(c);
            k.pent = [e, pent1];
            k.ipart = [-1, ip1];
            k.pbody = [p, b1];
            k.friction = f32::from_bits(0x3E99_999A);
            k.pt = [pos, pos];
            k.n = n;
            let (sol, pool) = w.solver_and_pool(tid);
            sol.register_contact(pool, c);
        }
    }
    1
}

/// A read-only copy of an entity's slot-64 body (`bWillModify = 0`): the rigid body, or the
/// zeroed static dummy.
#[must_use]
pub fn body_ro(w: &World, e: EntIdx) -> RigidBody {
    match w.ent(e).rigid() {
        Some(r) => *w.bodies.get(r.body),
        None => {
            let mut b = *w.bodies.get(BodyId::STATIC);
            b.reset_static_dummy();
            b
        }
    }
}

/// Slot 64 of any entity for a solver contact (`GetRigidBody(ipart, bWillModify)`).
pub fn body_for_contact(w: &mut World, e: EntIdx, will_modify: bool) -> BodyId {
    match &w.ent(e).kind {
        crate::entities::base::EntityKind::Rigid(r) => r.body,
        crate::entities::base::EntityKind::Living(_) => get_rigid_body(w, e, will_modify),
        _ => {
            w.bodies.get_mut(BodyId::STATIC).reset_static_dummy();
            BodyId::STATIC
        }
    }
}

/// Slot 41 `Update(dt, damping)` (0x3502C240): collision events of the (never created)
/// contacts; colliders without constraint contacts with this entity are dropped (both ways);
/// with a proxy body the damped proxy velocity becomes the velocity and its angular velocity
/// turns the entity about its up axis by `|w|·dt` (sign of the local z component), then the
/// proxy's `w` and `L` are cleared. Returns 1.
pub fn update(w: &mut World, e: EntIdx, dt: f32, damping: f32) -> u32 {
    let cols = w.ent(e).colliders.clone();
    let mut kept = Vec::new();
    for c in cols {
        if crate::entities::has_constraint_contacts_with(w, c, e, 0) {
            kept.push(c);
        } else {
            crate::entities::remove_collider(w, c, e, true);
        }
    }
    w.ent_mut(e).colliders = kept;
    lm(w, e).n_contacts = 0;
    let Some(p) = ld(w, e).proxy else {
        return 1;
    };
    let a3 = damping;
    let (v15, v16, v17, v19, v20, v23) = {
        let b = w.bodies.get_mut(p);
        let v15 = b.v.x * a3;
        let v16 = b.v.y * a3;
        let v17 = b.v.z * a3;
        let v19 = b.w.x * a3;
        let v20 = b.w.y * a3;
        let v23 = b.w.z * a3;
        b.v = Vec3::new(v15, v16, v17);
        b.w = Vec3::new(v19, v20, v23);
        (v15, v16, v17, v19, v20, v23)
    };
    lm(w, e).vel = Vec3::new(v15, v16, v17);
    let v24 = (v20 * v20 + v19 * v19) + v23 * v23;
    if v24 > 0.0 {
        let v25 = (v24.sqrt() * dt) * 0.5;
        let q = w.ent(e).q;
        let v26 = ((v20 * q.z - v23 * q.y) + v19 * q.w) * q.y
            - ((v23 * q.x - v19 * q.z) + v20 * q.w) * q.x;
        let s = (v26 + v23) + v26;
        let v27 = ((((s.to_bits() as i32) >> 30) & !1) + 1) as f32;
        let sn = crt::sinf(v25);
        let cs = crt::cosf(v25);
        let r = Quat::new(sn * 0.0, sn * 0.0, v27 * sn, cs);
        let n = q.product(r);
        let inv = 1.0 / ((((n.x * n.x) + n.w * n.w) + n.y * n.y) + n.z * n.z).sqrt();
        w.ent_mut(e).q = Quat::new(inv * n.x, inv * n.y, inv * n.z, inv * n.w);
        let b = w.bodies.get_mut(p);
        b.w = Vec3::ZERO;
        b.l = Vec3::ZERO;
    }
    1
}

/// `sub_35026300(pos, pt, n, other, ipart, idmat, impulse, bGround, iPrim)`: the collision
/// event of a living entity (entity flags `0x2002000`, not in replay mode): the other side's
/// point velocity from its slot 64 body; for a ground event (`bGround`, not on a breakable
/// part) only when the relative speed exceeds √3 and the other body is heavy enough
/// (`8·r·r·hh ≤ V`), with the impulse estimated from the approach speed. Sets the
/// collision-event flag `0x200`.
pub fn collision_event(
    w: &mut World,
    e: EntIdx,
    pos: Vec3,
    pt: Vec3,
    n: Vec3,
    other: EntIdx,
    ipart: i32,
    idmat: i32,
    impulse: f32,
    ground: bool,
    iprim: i32,
) {
    let ef = w.ent(e).flags;
    if ef & 0x200_2000 == 0 || ld(w, e).flags & super::lflags::NO_LOCK != 0 {
        return;
    }
    // **Bug kept:** the binary calls the other entity's slot 64 with the *living* entity as
    // `this`: for a rigid other that returns `living+744` (inside the head-sphere BV: zero
    // pose and velocities, `v.z` = the bits 1, mass 0, and the collider pointer as Minv /
    // volume), for the other classes the zeroed static dummy. The pointer words are not
    // reproducible; the port uses 0 for them.
    let ob = if !other.is_none() && w.ent(other).rigid().is_some() {
        RigidBody {
            v: Vec3::new(0.0, 0.0, f32::from_bits(1)),
            ..RigidBody::default()
        }
    } else {
        RigidBody::default()
    };
    let d = Vec3::new(pt.x - ob.pos.x, pt.y - ob.pos.y, pt.z - ob.pos.z);
    let v25 = (ob.w.y * d.z - ob.w.z * d.y) + ob.v.x;
    let v27 = (ob.w.z * d.x - ob.w.x * d.z) + ob.v.y;
    let v28 = (ob.w.x * d.y - ob.w.y * d.x) + ob.v.z;
    let mut nn = n;
    let mut imp = impulse;
    let breakable = !other.is_none()
        && w.ent(other)
            .parts
            .get(ipart.max(0) as usize)
            .is_some_and(|p| p.flags & 0x80_0000 != 0);
    let l = ld(w, e);
    if ground && !breakable {
        let rel = Vec3::new(
            (v25 - l.vel.x) - l.ground_vel.x,
            (v27 - l.vel.y) - l.ground_vel.y,
            (v28 - l.vel.z) - l.ground_vel.z,
        );
        if (rel.x * rel.x + rel.y * rel.y) + rel.z * rel.z <= 3.0
            || ((l.size.y * l.size.x) * l.size.z) * 8.0 <= ob.volume
        {
            return;
        }
        let s = (v25 * v25 + v27 * v27) + v28 * v28;
        nn = if s <= 9.999_999_7e-21 {
            Vec3::new(0.0, 0.0, 1.0)
        } else {
            let r = 1.0 / s.sqrt();
            Vec3::new(r * v25, v27 * r, v28 * r)
        };
        imp = (((v25 - l.vel.x) - l.ground_vel.x) * nn.x
            + ((v27 - l.vel.y) - l.ground_vel.y) * nn.y)
            + (((v28 - l.vel.z) - l.ground_vel.z) * nn.z);
        imp *= (l.mass * ob.minv + 1.0) * l.mass;
    }
    let l = ld(w, e);
    let v_self = Vec3::new(
        l.ground_vel.x + l.vel.x,
        l.ground_vel.y + l.vel.y,
        l.ground_vel.z + l.vel.z,
    );
    let partid = if other.is_none() {
        0
    } else {
        w.ent(other).parts.get(ipart.max(0) as usize).map_or(0, |p| p.id)
    };
    let mat = if other.is_none() {
        idmat
    } else {
        crate::entities::rigid::colliders::map_material(w, other, idmat, ipart)
    };
    let mut ev_pt = pt;
    let mut radius = 0.0f32;
    if breakable {
        let q = w.ent(e).q;
        let p0 = w.ent(e).parts.first().map_or(Vec3::ZERO, |p| p.pos);
        let r = q.rotate(p0);
        let c = Vec3::new(pos.x + r.x, r.y + pos.y, r.z + pos.z);
        let t = (n.y * (pt.y - c.y) + n.x * (pt.x - c.x)) + n.z * (pt.z - c.z);
        ev_pt = Vec3::new(n.x * t + c.x, n.y * t + c.y, n.z * t + c.z);
        let up = q.rotate(Vec3::new(0.0, 0.0, 1.0));
        let v64 = (n.y * up.y + n.x * up.x) + n.z * up.z;
        let s = 1.0 - v64 * v64;
        let l = ld(w, e);
        let cap = if l.flags & 8 != 0 { l.size.x } else { 0.0 };
        radius = ((((-s).abs() + s) * 0.5).sqrt() * (cap + l.size.z) + v64 * l.size.x) * 1.05;
    }
    let ev = Event::Collision {
        ent: [e, other],
        pt: ev_pt,
        n: nn,
        v: [v_self, Vec3::new(v25, v27, v28)],
        mass: [ld(w, e).mass, ob.m],
        partid: [100, partid],
        idmat: [ld(w, e).surface_idx, mat],
        penetration: 0.0,
        normimpulse: ((-imp).abs() + imp) * 0.5,
        radius,
    };
    let _ = iprim;
    w.signal_event(ef, ev);
    lm(w, e).flags |= super::lflags::COLL_EVENT;
}
