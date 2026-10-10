//! `SetParams` (entity slot 7): `CPhysicalEntity::SetParams` 0x3506EF20 and the rigid
//! post-processing of `CRigidEntity::SetParams` 0x350C4CC0, per params id.

use super::{compute_bbox, Coords, EntityKind};
use crate::math::{Quat, Vec3};
use crate::params::ParamsPos;
use crate::sentinel::is_unused;
use crate::world::{EntIdx, World};

/// `pe_params_pos` (id 0): base handler 0x3506EF20 (+ rigid post-processing 0x350C4CC0 for
/// rigid entities). Matrices (`pMtx3x4`/`pMtx3x3`) are not supported (pass null). Returns 1.
pub fn set_params_pos(w: &mut World, e: EntIdx, p: &ParamsPos, _thread_safe: bool) -> i32 {
    let is_rigid = matches!(w.ent(e).kind, EntityKind::Rigid(_));
    // rigid: remember the old com / body q before the base runs
    let (old_com, old_qb, old_bbox) = if is_rigid {
        let b = w.bodies.get(w.ent(e).rigid().expect("rigid").body);
        (b.pos, b.q, w.ent(e).bbox)
    } else {
        (Vec3::ZERO, Quat::IDENTITY, w.ent(e).bbox)
    };
    base_set_params_pos(w, e, p);
    if is_rigid {
        rigid_post_params_pos(w, e, p, old_com, old_qb, old_bbox);
    }
    1
}

/// The id-0 branch of `CPhysicalEntity::SetParams`.
fn base_set_params_pos(w: &mut World, e: EntIdx, p: &ParamsPos) {
    // validator 0x350741C0 rejects only when vars +312 is set.
    let ent = w.ent(e);
    let mut pos = ent.pos;
    let mut q = ent.q;
    let mut changed = false;
    if !is_unused(p.pos.x) {
        pos = p.pos;
        changed = true;
    }
    if !is_unused(p.q.w) {
        q = p.q;
        changed = true;
    }
    if p.sim_class != i32::MIN && (ent.sim_class() as u32) <= 6 {
        w.ent_mut(e).set_sim_class(p.sim_class);
        w.reposition_entity(e, 2, None);
    }
    let old_scale = w.ent(e).scale;
    let mut bbox_done = false;
    let mut new_bbox = [Vec3::ZERO; 2];
    if !is_unused(p.scale) {
        w.ent_mut(e).scale = p.scale;
        changed = true;
        if p.recalc_bounds != 0 {
            new_bbox = scaled_bbox(w, e, pos, q, p.scale);
            bbox_done = true;
        }
    }
    if !changed {
        return;
    }
    if p.recalc_bounds != 0 {
        if w.ent(e).flags & 0x8000 == 0 {
            // GetEntitiesInBox(bbox, objtypes 64, petitioner = e): bbox-overlap events
            let bb = w.ent(e).bbox;
            let caller = w.tls_thread.min(4) as usize;
            w.get_entities_in_box(bb[0], bb[1], 64, Some(e), caller);
        }
        if !bbox_done {
            new_bbox = compute_bbox(w, e, 0, -1, Some(Coords { pos, q }));
        }
        if p.ent_grid_use_obb == 0 || w.ent(e).has_structure {
            w.reposition_entity(e, 1, Some(new_bbox));
        }
    }
    {
        let ent = w.ent_mut(e);
        if let EntityKind::Rigid(r) = &mut ent.kind {
            r.pos_new = pos;
            r.q_new = q;
        }
        ent.pos = pos;
        ent.q = q;
    }
    if !is_unused(p.scale) {
        let ent = w.ent_mut(e);
        let k = ent.scale / old_scale;
        for part in &mut ent.parts {
            part.pos = Vec3::new(k * part.pos.x, k * part.pos.y, k * part.pos.z);
            part.scale *= k;
        }
    }
    if p.recalc_bounds != 0 {
        let b = compute_bbox(w, e, 2, -1, None);
        w.ent_mut(e).bbox = b;
        if p.ent_grid_use_obb != 0 && !w.ent(e).has_structure {
            w.reposition_entity(e, 5, Some(new_bbox));
        }
        if w.ent(e).flags & 0x8000 == 0 {
            let bb = w.ent(e).bbox;
            let caller = w.tls_thread.min(4) as usize;
            w.get_entities_in_box(bb[0], bb[1], 64, Some(e), caller);
        }
    }
}

/// The bbox of the scale branch of SetParams(pos): per part and geometry the OBB of the
/// geometry box at the new pose with the new scale (initial bounds ±3.3e38).
fn scaled_bbox(w: &World, e: EntIdx, pos: Vec3, q: Quat, scale: f32) -> [Vec3; 2] {
    let ent = w.ent(e);
    if ent.parts.is_empty() {
        return [pos, pos];
    }
    let mut lo = Vec3::splat(3.3e38);
    let mut hi = Vec3::splat(-3.3e38);
    for part in &ent.parts {
        let geoms: Vec<_> = if std::ptr::eq(
            part.pgeom_proxy.geom.as_ref() as *const _ as *const u8,
            part.pgeom.geom.as_ref() as *const _ as *const u8,
        ) {
            vec![part.pgeom_proxy.geom.clone()]
        } else {
            vec![part.pgeom_proxy.geom.clone(), part.pgeom.geom.clone()]
        };
        for g in geoms {
            let bb = g.bbox();
            let r = crate::math::Mat33::from_quat(q.product(part.q));
            // basis · Rᵀ-ish product (sub_35020320 with the transposed rotation), then |·|
            let rt = r.transpose();
            let m = bb.basis.mul_mat(&rt);
            let a = |i: usize, j: usize| m.m[i][j].abs();
            let s = bb.size;
            let v46 = scale;
            let ey = v46 * ((s.y * a(1, 1) + s.x * a(0, 1)) + s.z * a(2, 1));
            let ex = ((s.y * a(1, 0) + s.x * a(0, 0)) + s.z * a(2, 0)) * v46;
            let ez = v46 * ((s.y * a(1, 2) + s.x * a(0, 2)) + s.z * a(2, 2));
            let c = part.q.rotate(bb.center);
            let k = v46 / part.scale;
            let p1 = Vec3::new(part.pos.x * k + v46 * c.x, part.pos.y * k + v46 * c.y, part.pos.z * k + v46 * c.z);
            let p2 = q.rotate(p1);
            let cw = Vec3::new(pos.x + p2.x, p2.y + pos.y, p2.z + pos.z);
            if lo.z >= cw.z - ez {
                lo.z = cw.z - ez;
            }
            if lo.y >= cw.y - ey {
                lo.y = cw.y - ey;
            }
            if lo.x >= cw.x - ex {
                lo.x = cw.x - ex;
            }
            if hi.z <= cw.z + ez {
                hi.z = cw.z + ez;
            }
            if hi.y <= cw.y + ey {
                hi.y = cw.y + ey;
            }
            if hi.x <= cw.x + ex {
                hi.x = cw.x + ex;
            }
        }
    }
    [lo, hi]
}

/// `CRigidEntity::SetParams` post-processing for id 0 (0x350C4CC0).
fn rigid_post_params_pos(w: &mut World, e: EntIdx, p: &ParamsPos, old_com: Vec3, old_qb: Quat, old_bbox: [Vec3; 2]) {
    let body_id = w.ent(e).rigid().expect("rigid").body;
    let (epos, eq) = (w.ent(e).pos, w.ent(e).q);
    {
        let b = w.bodies.get_mut(body_id);
        if !is_unused(p.pos.x) {
            let r = eq.rotate(b.offsfb);
            b.pos = Vec3::new(r.x + epos.x, r.y + epos.y, r.z + epos.z);
        }
        if !is_unused(p.q.w) {
            let r = eq.rotate(b.offsfb);
            b.pos = Vec3::new(r.x + epos.x, r.y + epos.y, r.z + epos.z);
            // body q = q_ent ⊗ conj(qfb)
            b.q = eq.product(b.qfb.conj());
        }
    }
    if p.sim_class != i32::MIN && p.sim_class <= 2 {
        let sc = w.ent(e).sim_class();
        w.ent_mut(e).rigid_mut().expect("rigid").set_awake(1 - sc < 0);
    }
    let b = *w.bodies.get(body_id);
    let dq = Vec3::new(old_qb.x - b.q.x, old_qb.y - b.q.y, old_qb.z - b.q.z);
    let v31 = (dq.y * dq.y + dq.x * dq.x) + dq.z * dq.z;
    let dp2 = ((old_com.y - b.pos.y) * (old_com.y - b.pos.y) + (old_com.x - b.pos.x) * (old_com.x - b.pos.x))
        + (old_com.z - b.pos.z) * (old_com.z - b.pos.z);
    let v33 = f32::from_bits(0x3716_FEB5) - dp2; // 9.0000003e-6
    let v30 = f32::from_bits(0x3B23_D70B) - v31; // 0.0025000002
    let v34 = ((v33 + v30) - (v33 - v30).abs()) * 0.5;
    let moved = v34 < 0.0;
    if moved && p.recalc_bounds & 0x20 == 0 {
        if w.vars.last_dt > 0.0 {
            let cols = w.ent(e).colliders.clone();
            for c in cols {
                crate::entities::awake(w, c, 1, 0);
            }
        }
        crate::entities::rigid::contacts::drop_contacts(w, e);
        if p.recalc_bounds & 2 == 0 {
            let b = w.bodies.get_mut(body_id);
            if b.m > 0.0 {
                b.p = Vec3::ZERO;
                b.l = Vec3::ZERO;
                b.w = Vec3::ZERO;
                b.v = Vec3::ZERO;
            }
            // world+420 > 0: move constraint anchors (sub_350B0940; no constraints in the port)
        }
    }
    if !is_unused(p.scale) && !w.ent(e).parts.is_empty() && (p.scale - w.ent(e).parts[0].scale).abs() > 0.001 {
        crate::entities::rigid::mass::recompute_mass(w, e);
    }
    let ent = w.ent(e);
    if !ent.parts.is_empty()
        && w.vars.last_dt > 0.0
        && ent.flags & 0x20 == 0
        && p.recalc_bounds & 0x20 == 0
        && (ent.id_bits & 0xFF00_0000) as i32 <= 0x200_0000
    {
        let b = *w.bodies.get(body_id);
        if b.minv == 0.0 {
            let fl = ent.parts.iter().fold(0u32, |a, p| a | p.flags | p.flags_collider);
            if fl & 1 != 0 {
                let bb = ent.bbox;
                let caller = w.tls_thread.min(4) as usize;
                let list = w.get_entities_in_box(bb[0], bb[1], 90, Some(e), caller);
                for &o in list.iter().rev() {
                    if o != e {
                        crate::entities::awake(w, o, 1, 0);
                    }
                }
            }
        }
        let r = w.ent(e).rigid().expect("rigid");
        if r.last_ext_contact_step < 0 && moved {
            let g = w.vars.contact_gap * 4.0;
            let hi = Vec3::new(g + old_bbox[1].x, g + old_bbox[1].y, g + old_bbox[1].z);
            let lo = Vec3::new(old_bbox[0].x - g, old_bbox[0].y - g, old_bbox[0].z - g);
            let caller = w.tls_thread.min(4) as usize;
            let list = w.get_entities_in_box(lo, hi, 26, Some(e), caller);
            for &o in list.iter().rev() {
                if o != e {
                    crate::entities::awake(w, o, 1, 0);
                }
            }
        }
    }
    let b = *w.bodies.get(body_id);
    let r = w.ent_mut(e).rigid_mut().expect("rigid");
    r.prev_pos = b.pos;
    r.prev_q = b.q;
    if p.recalc_bounds & 0x40 != 0 {
        crate::entities::rigid::buoyancy::gather_areas(w, e, 0.0);
    }
}
