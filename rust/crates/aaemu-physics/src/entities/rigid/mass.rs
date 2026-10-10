//! `CRigidEntity::RecomputeMassDistribution` (slot 74, 0x350B1130): rebuild the body from the
//! parts, keeping the momenta.

use crate::entities::base::geometry::{body_add, body_create};
use crate::math::{Quat, Vec3};
use crate::world::{EntIdx, World};

/// Slot 74: zero the body (`sub_35098F60`), add every part with mass (first: Create,
/// further: Add), restore v/w and recompute `P = M·v`, `L = q·(Ibody ∘ (q⁻¹·w))` when the body
/// has positive mass and inertia, else zero mass/inertia; snapshot the previous pose.
pub fn recompute_mass(w: &mut World, e: EntIdx) {
    let body_id = w.ent(e).rigid().expect("rigid").body;
    let (v_old, w_old) = {
        let b = w.bodies.get(body_id);
        (b.v, b.w)
    };
    let q0 = w.ent(e).q;
    let inv = 1.0 / (((q0.w * q0.w + q0.x * q0.x) + q0.y * q0.y) + q0.z * q0.z).sqrt();
    let qn = Quat::new(q0.x * inv, q0.y * inv, q0.z * inv, q0.w * inv);
    let epos = w.ent(e).pos;
    {
        let b = w.bodies.get_mut(body_id);
        b.m = 0.0;
        b.minv = 0.0;
        b.ibody = Vec3::ZERO;
        b.ibody_inv = Vec3::ZERO;
        b.p = Vec3::ZERO;
        b.l = Vec3::ZERO;
        b.w = Vec3::ZERO;
        b.v = Vec3::ZERO;
    }
    let n = w.ent(e).parts.len();
    for k in 0..n {
        let part = w.ent(e).parts[k].clone();
        let v11 = ((part.scale * part.scale) * part.scale) * part.pgeom.volume;
        if v11 <= 0.0 {
            w.ent_mut(e).parts[k].mass = 0.0;
        }
        let ps = part.scale;
        let o = part.q.rotate(part.pgeom.origin);
        let p = Vec3::new(
            o.x * ps + part.pos.x,
            o.y * ps + part.pos.y,
            o.z * ps + part.pos.z,
        );
        let r = qn.rotate(p);
        let com = Vec3::new(r.x + epos.x, r.y + epos.y, r.z + epos.z);
        let q78 = qn.product(part.q);
        let qb = q78.product(part.pgeom.q);
        let inv = 1.0 / (((qb.w * qb.w + qb.x * qb.x) + qb.y * qb.y) + qb.z * qb.z).sqrt();
        let qb = Quat::new(qb.x * inv, qb.y * inv, qb.z * inv, inv * qb.w);
        let mass = w.ent(e).parts[k].mass;
        if mass > 0.0 {
            let i = part.pgeom.inertia;
            let s2 = ps * ps;
            let s3 = s2 * ps;
            let b = w.bodies.get_mut(body_id);
            if b.m == 0.0 {
                let ib = Vec3::new((i.x * s3) * s2, (i.y * s3) * s2, (i.z * s3) * s2);
                body_create(b, com, ib, qb, v11, mass, qn, epos);
            } else {
                let ib = Vec3::new((i.x * s2) * s3, (i.y * s2) * s3, (i.z * s2) * s3);
                body_add(b, com, ib, qb, v11, mass);
            }
        }
    }
    let b = w.bodies.get_mut(body_id);
    // min(M, Ibody_inv) >= 0
    let v30 = ((b.m + b.ibody_inv.x) - (b.m - b.ibody_inv.x).abs()) * 0.5;
    let v31 = ((b.ibody_inv.y + v30) - (v30 - b.ibody_inv.y).abs()) * 0.5;
    if ((b.ibody_inv.z + v31) - (v31 - b.ibody_inv.z).abs()) * 0.5 >= 0.0 {
        b.v = v_old;
        b.p = Vec3::new(b.v.x * b.m, b.v.y * b.m, b.v.z * b.m);
        b.w = w_old;
        // L = q·(Ibody ∘ (q⁻¹·w)) with the inline rotations of the binary
        let q = b.q;
        let (qx, qy, qz, qw) = (-q.x, -q.y, -q.z, q.w);
        let (v33, v34, v35) = (w_old.x, w_old.y, w_old.z);
        let v44 = ((qy * v35) - (qz * v34)) + (qw * v33);
        let v46 = ((qz * v33) - (v35 * qx)) + (qw * v34);
        let v47 = ((v34 * qx) - (qy * v33)) + (qw * v35);
        let v48 = (((qy * v47) - (qz * v46)) + (((qy * v47) - (qz * v46)) + v33)) * b.ibody.x;
        let v49 = (((qz * v44) - (v47 * qx)) + (((qz * v44) - (v47 * qx)) + v34)) * b.ibody.y;
        let v50 = (((v46 * qx) - (qy * v44)) + (((v46 * qx) - (qy * v44)) + v35)) * b.ibody.z;
        let (x, y, z, ww) = (q.x, q.y, q.z, q.w);
        let v53 = ((v50 * y) - (v49 * z)) + (v48 * ww);
        let v54 = (((v48 * z) - (v50 * x)) + (v49 * ww)) * z;
        let v55 = ((v49 * x) - (v48 * y)) + (v50 * ww);
        let v56 = ((((v48 * z) - (v50 * x)) + (v49 * ww)) * x) - (v53 * y);
        b.l = Vec3::new(
            ((v55 * y) - v54) + (((v55 * y) - v54) + v48),
            ((v53 * z) - (v55 * x)) + (((v53 * z) - (v55 * x)) + v49),
            v56 + (v56 + v50),
        );
    } else {
        b.m = 0.0;
        b.minv = 0.0;
        b.ibody = Vec3::ZERO;
        b.ibody_inv = Vec3::ZERO;
    }
    let b = *w.bodies.get(body_id);
    let r = w.ent_mut(e).rigid_mut().expect("rigid");
    r.prev_pos = b.pos;
    r.prev_q = b.q;
}
