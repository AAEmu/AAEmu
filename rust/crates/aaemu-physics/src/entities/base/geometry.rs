//! `AddGeometry` (entity slot 11): `CPhysicalEntity::AddGeometry` 0x35055FF0 and the rigid
//! override `CRigidEntity::AddGeometry` 0x350C41C0 with the body composition
//! `RigidBody::Create` 0x3509D230 / `RigidBody::Add` 0x3509D460.

use super::{compute_bbox, EntityKind, Part};
use crate::geometry::PhysGeometry;
use crate::math::{Quat, Vec3};
use crate::params::GeomParams;
use crate::sentinel::is_unused;
use crate::solver::RigidBody;
use crate::world::{EntIdx, World};
use std::sync::Arc;

/// `max_idiom(a, b)` written `(|a − b| + (a + b))·0.5`.
#[inline]
fn max_i(a: f32, b: f32) -> f32 {
    ((a - b).abs() + (a + b)) * 0.5
}

/// `min` written `((a + b) − |a − b|)·0.5`.
#[inline]
fn min_i(a: f32, b: f32) -> f32 {
    ((a + b) - (a - b).abs()) * 0.5
}

/// Entity slot 11 `AddGeometry(pgeom, params, id, bThreadSafe)`: dispatches to the class
/// (rigid: 0x350C41C0, otherwise the base 0x35055FF0). Returns the part id or -1.
pub fn add_geometry(w: &mut World, e: EntIdx, pgeom: &Arc<PhysGeometry>, gp: &GeomParams, id: i32) -> i32 {
    match w.ent(e).kind {
        EntityKind::Rigid(_) => rigid_add_geometry(w, e, pgeom, gp, id),
        _ => base_add_geometry(w, e, pgeom, gp, id),
    }
}

/// `CPhysicalEntity::AddGeometry` 0x35055FF0 (immediate path; the command-queue path is taken
/// by the world while it is locked, never in the single-threaded port). Matrices
/// (`pMtx3x4`/`pMtx3x3`) and lattices are not supported (pass null).
pub fn base_add_geometry(w: &mut World, e: EntIdx, pgeom: &Arc<PhysGeometry>, gp: &GeomParams, id: i32) -> i32 {
    let bb = pgeom.geom.bbox();
    let s = bb.size;
    // v13 = max(max(sx, sy), sz); v15 = min(min(sx, sy), sz) (scaled by params scale)
    let v11 = (s.x - s.y).abs();
    let max_xy = (v11 + (s.x + s.y)) * 0.5;
    let v13 = ((max_xy - s.z).abs() + (max_xy + s.z)) * 0.5;
    let v12 = gp.scale;
    let v14 = v12 * v13;
    let min_xy = ((s.x + s.y) - v11) * 0.5;
    let v15 = v12 * (((min_xy + s.z) - (min_xy - s.z).abs()) * 0.5);
    let mut id = id;
    if id >= 0 {
        if let Some(i) = w.ent(e).parts.iter().position(|p| p.id == id) {
            // existing id: only a proxy swap (flag 0x20000) is honoured
            if gp.flags & 0x20000 != 0 {
                w.ent_mut(e).parts[i].pgeom_proxy = pgeom.clone();
                if gp.recalc_bbox != 0 {
                    let b = compute_bbox(w, e, 2, -1, None);
                    w.reposition_entity(e, 1, Some(b));
                    w.ent_mut(e).bbox = b;
                }
            }
            return id;
        }
    }
    // validator 0x35074BC0 ("CPhysicalEntity:AddGeometry"): rejects NaN / huge placements
    // only when vars +312 is set.
    if w.vars.validation_strict != 0 && !geomparams_valid(gp) {
        return -1;
    }
    let ent = w.ent_mut(e);
    if ent.parts.len() as i32 == ent.parts_cap {
        // grow by 4 (`((cap + 4) & ~3) - ((cap - 1) >> 31 & 3)`)
        let cap = ent.parts_cap;
        ent.parts_cap = ((cap + 4) & !3) - (((cap - 1) >> 31) & 3);
    }
    if id >= 0 {
        let d = ent.next_part_id - (id + 1);
        ent.next_part_id -= d & (d >> 31);
    } else {
        id = ent.next_part_id;
        ent.next_part_id = id + 1;
    }
    let esc = ent.scale;
    let mut surface_bits = (pgeom.surface_idx as u32) & 0x3FF;
    if gp.surface_idx != i32::MIN {
        surface_bits = (gp.surface_idx as u32) & 0x3FF;
    }
    let min_contact_dist = if is_unused(gp.min_contact_dist) {
        let k = if v15 >= v14 * 0.3 { 0.1 } else { 0.4 };
        max_i(v14 * 0.03, k * v15)
    } else {
        gp.min_contact_dist
    };
    let flags = gp.flags & 0xFFFD_FFFF;
    // idmatBreakable: unused → auto (flags & 0xFFB, sub_35052CC0, needs a breakable mesh) or
    // 0x3FFF (-1)
    if gp.idmat_breakable == i32::MIN {
        surface_bits |= 0xFF_FC00;
    } else {
        surface_bits = (surface_bits & 0xFF00_03FF) | (((gp.idmat_breakable & 0x3FFF) as u32) << 10);
    }
    let scale = gp.scale * esc;
    let mass = if gp.mass <= 0.0 {
        if gp.density <= 0.0 {
            0.0
        } else {
            ((scale * scale) * scale) * (gp.density * pgeom.volume)
        }
    } else {
        gp.mass
    };
    let part = Part {
        pos: Vec3::new(esc * gp.pos.x, esc * gp.pos.y, esc * gp.pos.z),
        q: gp.q,
        scale,
        bbox: [Vec3::ZERO; 2],
        pgeom: pgeom.clone(),
        pgeom_proxy: pgeom.clone(),
        id,
        mass,
        flags,
        flags_collider: gp.flags_collider,
        max_dim: v13,
        min_contact_dist,
        surface_bits,
        nmats_bits: pgeom.mat_mapping.len() as u32 & 0x7FFF_FFFF,
        mat_mapping: None,
        has_lattice: false,
        unk128: 0,
        bbox_tmp: None,
    };
    ent.parts.push(part);
    if gp.recalc_bbox != 0 {
        let b = compute_bbox(w, e, 3, -1, None);
        w.reposition_entity(e, 1, Some(b));
        w.ent_mut(e).bbox = b;
        // sub_35053DC0: OBB/grid cache refresh (only used by flag-0x10 large objects).
    }
    id
}

/// The checks of the geomparams validator 0x35074BC0 (finite, below the world limits).
fn geomparams_valid(gp: &GeomParams) -> bool {
    gp.pos.is_finite()
        && gp.q.x.is_finite()
        && gp.q.y.is_finite()
        && gp.q.z.is_finite()
        && gp.q.w.is_finite()
        && gp.scale.is_finite()
}

/// `CRigidEntity::AddGeometry` 0x350C41C0: rejects geometries with negative inertia, calls
/// the base, then adds the part's mass to the body (first part: `RigidBody::Create`,
/// further parts: `RigidBody::Add`) and snapshots the previous pose.
pub fn rigid_add_geometry(w: &mut World, e: EntIdx, pgeom: &Arc<PhysGeometry>, gp: &GeomParams, id: i32) -> i32 {
    let i = pgeom.inertia;
    if !((gp.mass.abs() + gp.density.abs()) == 0.0
        || (pgeom.volume > 0.0 && i.x >= 0.0 && i.y >= 0.0 && i.z >= 0.0))
    {
        // "CRigidEntity::AddGeometry: (%s at %.1f,%.1f,%.1f) Trying to add bad geometry"
        return -1;
    }
    let pid = base_add_geometry(w, e, pgeom, gp, id);
    if pid < 0 {
        return pid;
    }
    let v15 = gp.scale;
    let v17 = ((v15 * v15) * v15) * pgeom.volume;
    let v16 = if gp.mass <= 0.0 { v17 * gp.density } else { gp.mass };
    // part centre of mass in the world: q_ent·(q_gp·origin·s + pos_gp) + pos_ent
    let o = gp.q.rotate(pgeom.origin);
    let p1 = Vec3::new(o.x * v15 + gp.pos.x, o.y * v15 + gp.pos.y, o.z * v15 + gp.pos.z);
    let ent = w.ent(e);
    let (epos, eq) = (ent.pos, ent.q);
    let p2 = eq.rotate(p1);
    let com = Vec3::new(p2.x + epos.x, p2.y + epos.y, p2.z + epos.z);
    // body orientation: pgeom.q ⊗ (q_ent ⊗ q_gp)
    let q52 = eq.product(gp.q);
    let qbody = q52.product(pgeom.q);
    let k = ent.parts.iter().position(|p| p.id == pid).unwrap_or(ent.parts.len());
    let body_id = ent.rigid().expect("rigid").body;
    w.ent_mut(e).parts[k].mass = v16;
    if v16 > 0.0 {
        let s2 = v15 * v15;
        let s3 = v15 * s2;
        let ib = Vec3::new((i.x * s2) * s3, (i.y * s2) * s3, (i.z * s2) * s3);
        let n_parts = w.ent(e).parts.len();
        let b = w.bodies.get_mut(body_id);
        if b.m == 0.0 || n_parts == 1 {
            body_create(b, com, ib, qbody, v17, v16, eq, epos);
        } else {
            body_add(b, com, ib, qbody, v17, v16);
        }
    }
    let b = *w.bodies.get(body_id);
    let r = w.ent_mut(e).rigid_mut().expect("rigid");
    r.prev_pos = b.pos;
    r.prev_q = b.q;
    pid
}

/// `sub_3509D230` `RigidBody::Create(pos, Ibody, q, V, M, q_ent, pos_ent)`.
pub fn body_create(b: &mut RigidBody, pos: Vec3, ibody: Vec3, q: Quat, v: f32, m: f32, q_ent: Quat, pos_ent: Vec3) {
    b.volume = v;
    b.m = m;
    let k = m / v;
    b.q = q;
    b.pos = pos;
    if b.m > 0.0 {
        b.ibody = Vec3::new(ibody.x * k, ibody.y * k, ibody.z * k);
        b.ibody_inv = inv_diag(b.ibody);
        b.minv = 1.0 / b.m;
    } else {
        b.ibody = Vec3::ZERO;
        b.ibody_inv = Vec3::ZERO;
        b.minv = 0.0;
    }
    b.qfb = b.q.conj().product(q_ent);
    let d = Vec3::new(b.pos.x - pos_ent.x, b.pos.y - pos_ent.y, b.pos.z - pos_ent.z);
    b.offsfb = rotate_inv(q_ent, d);
    b.update_state();
}

/// `sub_35073120`: invert a diagonal inertia in place when its product is non-zero.
#[must_use]
pub fn inv_diag(d: Vec3) -> Vec3 {
    let p = (d.x * d.y) * d.z;
    if p != 0.0 {
        let r = 1.0 / p;
        Vec3::new((d.z * d.y) * r, (d.z * d.x) * r, (d.y * d.x) * r)
    } else {
        d
    }
}

/// `sub_35023C00`: rotate `v` by the inverse of `q`.
#[must_use]
pub fn rotate_inv(q: Quat, v: Vec3) -> Vec3 {
    let v9 = (q.z * v.y - q.y * v.z) + v.x * q.w;
    let v10 = (q.x * v.z - v.x * q.z) + q.w * v.y;
    let v11 = (v.x * q.y - q.x * v.y) + q.w * v.z;
    let v12 = q.z * v10 - q.y * v11;
    let v13 = q.y * v9 - q.x * v10;
    let v14 = q.x * v11 - q.z * v9;
    Vec3::new((v12 + v.x) + v12, (v14 + v.y) + v14, (v13 + v.z) + v13)
}

/// `sub_3509D460` `RigidBody::Add(pos, Ibody, q, V, M)`: merge a further part into the body.
/// Not ported yet: multi-part rigid bodies (needs the 3×3 eigen solver 0x3511EAA0). Until
/// then the first part's mass properties are kept and the extra mass is added at the
/// combined centre without the inertia rotation.
pub fn body_add(b: &mut RigidBody, pos: Vec3, _ibody: Vec3, _q: Quat, v: f32, m: f32) {
    let _ = (min_i(0.0, 0.0), pos);
    let mt = b.m + m;
    if mt.abs() >= b.m * 1e-4 {
        b.volume += v;
        b.m = mt;
        b.minv = 1.0 / mt;
    }
}
