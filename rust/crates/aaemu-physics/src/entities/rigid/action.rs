//! `CRigidEntity::Action` (slot 10, 0x350C5D30): the ids the port supports — 2
//! `action_impulse`, 4 reset, 8 awake, 10 set_velocity.

// NaN-aware comparisons and float literals are written as in the binary (the negated
// comparisons keep its NaN behaviour; the literals are the decompile's single-precision
// constants).
#![allow(clippy::neg_cmp_op_on_partial_ord, clippy::excessive_precision)]

use crate::math::Vec3;
use crate::params::{ActionImpulse, ActionReset};
use crate::sentinel::is_unused;
use crate::world::{EntIdx, World};

/// Action id 2 `action_impulse`: only for bodies with `Minv > 0`.
///
/// `angImpulse` (optional) + `(point − com) × impulse` (optional point) + `Ibody ∘ dw`
/// (optional); `impulse` + `M·dv` (optional dv). `iSource == 1` (buoyancy, area forces) caps
/// `|Minv·P| ≤ 5` and `|Iinv·L| ≤ 5` and does not wake the body; otherwise the body is woken
/// (simclass 1 → 2) and its idle timer reset unless `iSource > 0`. `iApplyTime != 0`
/// accumulates into the deferred momenta (`E+956/968`, applied in the next Step), otherwise
/// `P`, `L`, `v = Minv·P`, `w = Iinv·L` and the velocity clamp are applied immediately.
/// The base Action (breakable structures) is a no-op for the ported entities. Returns 1.
pub fn impulse(w: &mut World, e: EntIdx, a: &ActionImpulse) -> i32 {
    let body_id = w.ent(e).rigid().expect("rigid").body;
    let b = *w.bodies.get(body_id);
    if b.minv.partial_cmp(&0.0) != Some(std::cmp::Ordering::Greater) {
        return 1;
    }
    let (mut v9, mut v10, mut v11) = (0.0f32, 0.0f32, 0.0f32);
    if !is_unused(a.ang_impulse.x) {
        v9 = a.ang_impulse.x;
        v10 = a.ang_impulse.y;
        v11 = a.ang_impulse.z;
    }
    if !is_unused(a.point.x) {
        let (ix, iy, iz) = (a.impulse.x, a.impulse.y, a.impulse.z);
        let v17 = a.point.y - b.pos.y;
        let v18 = a.point.z - b.pos.z;
        let v19 = a.point.x - b.pos.x;
        v9 += iz * v17 - iy * v18;
        v10 += ix * v18 - iz * v19;
        v11 += iy * v19 - ix * v17;
    }
    let (mut v20, mut v21, mut v22) = (a.impulse.x, a.impulse.y, a.impulse.z);
    if !is_unused(a.dv.x) {
        v20 = a.impulse.x + a.dv.x * b.m;
        v21 = a.impulse.y + a.dv.y * b.m;
        v22 = a.impulse.z + a.dv.z * b.m;
    }
    if !is_unused(a.dw.x) {
        v9 += a.dw.x * b.ibody.x;
        v10 += b.ibody.y * a.dw.y;
        v11 += b.ibody.z * a.dw.z;
    }
    if a.i_source == 1 {
        let v26 = ((v20 * v20 + v21 * v21) + v22 * v22) * (b.minv * b.minv);
        if v26 > 25.0 {
            let k = 5.0 / v26.sqrt();
            v20 *= k;
            v21 *= k;
            v22 *= k;
        }
        let m = &b.iinv.m;
        let v28 = (v10 * m[0][1] + v9 * m[0][0]) + v11 * m[0][2];
        let v29 = (v9 * m[1][0] + v10 * m[1][1]) + v11 * m[1][2];
        let v30 = (v9 * m[2][0] + v10 * m[2][1]) + v11 * m[2][2];
        let v31 = (v28 * v28 + v29 * v29) + v30 * v30;
        if v31 > 25.0 {
            let k = 5.0 / v31.sqrt();
            v9 *= k;
            v10 *= k;
            v11 *= k;
        }
    } else {
        let r = w.ent_mut(e).rigid_mut().expect("rigid");
        r.set_awake(true);
        if w.ent(e).id_bits & 0xFF00_0000 == 0x0100_0000 {
            w.ent_mut(e).set_sim_class(2);
            w.reposition_entity(e, 2, None);
        }
        let ent = w.ent_mut(e);
        ent.time_idle *= if a.i_source > 0 { 1.0 } else { 0.0 };
    }
    if a.i_apply_time != 0 {
        let r = w.ent_mut(e).rigid_mut().expect("rigid");
        r.p_deferred = Vec3::new(
            v20 + r.p_deferred.x,
            v21 + r.p_deferred.y,
            v22 + r.p_deferred.z,
        );
        r.l_deferred = Vec3::new(
            v9 + r.l_deferred.x,
            v10 + r.l_deferred.y,
            v11 + r.l_deferred.z,
        );
    } else {
        let b = w.bodies.get_mut(body_id);
        b.p = Vec3::new(v20 + b.p.x, v21 + b.p.y, v22 + b.p.z);
        b.l = Vec3::new(v9 + b.l.x, v10 + b.l.y, v11 + b.l.z);
        b.v = Vec3::new(b.p.x * b.minv, b.p.y * b.minv, b.p.z * b.minv);
        let m = &b.iinv.m;
        let l = b.l;
        b.w = Vec3::new(
            (l.y * m[0][1] + l.x * m[0][0]) + l.z * m[0][2],
            (l.x * m[1][0] + l.y * m[1][1]) + l.z * m[1][2],
            (l.x * m[2][0] + l.y * m[2][1]) + l.z * m[2][2],
        );
        super::velocity::cap_velocities(w, e);
    }
    1
}

/// Action id 4 (reset): zero `P`, `L`, `w`, `v` and the deferred momenta; with
/// `clear_contacts` also drop the contacts and remove every constraint (update_constraint
/// {all, bRemove}); clear the external force/torque, the logged-collision counter, the stamps
/// (`+1096 = +1104 = -2`) and the awake history.
pub fn reset(w: &mut World, e: EntIdx, a: &ActionReset) -> i32 {
    let body_id = w.ent(e).rigid().expect("rigid").body;
    {
        let b = w.bodies.get_mut(body_id);
        b.v = Vec3::ZERO;
        b.w = Vec3::ZERO;
        b.p = Vec3::ZERO;
        b.l = Vec3::ZERO;
    }
    {
        let r = w.ent_mut(e).rigid_mut().expect("rigid");
        r.p_deferred = Vec3::ZERO;
        r.l_deferred = Vec3::ZERO;
    }
    if a.clear_contacts != 0 {
        super::contacts::drop_contacts(w, e);
        // Action(update_constraint {id unused, bRemove 1, flags 2}): no constraints in the
        // port, so only its slot-80 part-flag refresh remains
        crate::entities::interface::action::update_constraint(
            w,
            e,
            &crate::params::ActionUpdateConstraint {
                remove: 1,
                flags: 2,
                ..Default::default()
            },
        );
    }
    let r = w.ent_mut(e).rigid_mut().expect("rigid");
    r.f_ext = Vec3::ZERO;
    r.t_ext = Vec3::ZERO;
    r.awake_bits &= 0xFFC1_FFFF;
    r.stamp1096 = -2;
    r.last_ext_contact_step = -2;
    r.awake_history = 0;
    1
}
