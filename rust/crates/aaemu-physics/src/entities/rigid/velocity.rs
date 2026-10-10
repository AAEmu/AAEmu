//! The rigid velocity clamp `sub_350B9440` and its helpers.

use crate::math::{Quat, Vec3};
use crate::solver::RigidBody;
use crate::world::{EntIdx, World};

/// `sub_35021020`: normalise in place (`(0, 0, 1)` when `|v|² ≤ 1e-20`), `v·(1/sqrt)`.
#[must_use]
pub fn normalize_or_z(v: Vec3) -> Vec3 {
    let l2 = (v.x * v.x + v.y * v.y) + v.z * v.z;
    if l2 <= 9.999_999_7e-21 {
        Vec3::new(0.0, 0.0, 1.0)
    } else {
        let r = 1.0 / l2.sqrt();
        Vec3::new(v.x * r, v.y * r, v.z * r)
    }
}

/// `L = q·(Ibody ∘ (q⁻¹·w))` with the two `sub_35022240` rotations (`q⁻¹` = the conjugate
/// built by negating `x, y, z`).
#[must_use]
pub fn l_from_w(b: &RigidBody, w: Vec3) -> Vec3 {
    let qi = Quat::new(-b.q.x, -b.q.y, -b.q.z, b.q.w);
    let r = qi.rotate(w);
    let s = Vec3::new(r.x * b.ibody.x, b.ibody.y * r.y, b.ibody.z * r.z);
    b.q.rotate(s)
}

/// `sub_350B9440` CapVelocities: `|v| ≤ vars.max_velocity` (`P = M·v`); with exactly one
/// contact the angular speed along its normal is limited to `maxw`, otherwise `|w| ≤ wmax`
/// with `wmax = max(maxw, 300)` while in contact and `maxw` in free fall (`L` recomputed from
/// `w`).
pub fn cap_velocities(w: &mut World, e: EntIdx) {
    let vmax = w.vars.max_velocity;
    let r = w.ent(e).rigid().expect("rigid");
    let (body_id, n_contacts, max_w, head) = (r.body, r.n_contacts, r.max_w, r.contacts_head);
    let first_n = w.contacts.get(w.contacts.get(head).next).n;
    let b = w.bodies.get_mut(body_id);
    let v = b.v;
    if (v.x * v.x + v.y * v.y) + v.z * v.z > vmax * vmax {
        let nv = normalize_or_z(v);
        b.v = Vec3::new(vmax * nv.x, vmax * nv.y, vmax * nv.z);
        b.p = Vec3::new(b.v.x * b.m, b.v.y * b.m, b.v.z * b.m);
    }
    if n_contacts == 1 {
        let n = first_n;
        let v29 = (n.x * b.w.x + n.y * b.w.y) + n.z * b.w.z;
        if v29.abs() > max_w {
            let v30 = ((max_w + v29) - (max_w - v29).abs()) * 0.5;
            let v31 = (((-max_w - v30).abs() + (v30 - max_w)) * 0.5) - v29;
            b.w = Vec3::new(n.x * v31 + b.w.x, n.y * v31 + b.w.y, n.z * v31 + b.w.z);
            let wn = b.w;
            b.l = l_from_w(b, wn);
        }
    } else {
        let has = (n_contacts > 0) as i32 as f32;
        let t = has * (300.0 - max_w) + max_w;
        let v14 = ((max_w - t).abs() + (max_w + t)) * 0.5;
        let wv = b.w;
        if (wv.x * wv.x + wv.y * wv.y) + wv.z * wv.z > v14 * v14 {
            let nw = normalize_or_z(wv);
            b.w = Vec3::new(v14 * nw.x, v14 * nw.y, v14 * nw.z);
            let wn = b.w;
            b.l = l_from_w(b, wn);
        }
    }
}
