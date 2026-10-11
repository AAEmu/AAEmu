//! Slot 9 `GetStatus`: `CPhysicalEntity::GetStatus` 0x350724C0 and
//! `CRigidEntity::GetStatus` 0x350B1B80 (rigid: id 7 first; then the base; when the base
//! returns 0 the rigid ids 26, 8, 9, 19; a base `pe_status_pos` with `timeBack > 0` is
//! overridden by the previous-step pose).
//!
//! | id | result / written |
//! |---|---|
//! | 1 pos | see [`status_pos`] |
//! | 7 awake | base: slot 56 `IsAwake(-1)`; rigid: the awake byte, or with `lag` the recent external-contact test |
//! | 8 dynamics (rigid) | see [`rigid_dynamics`] |
//! | 9 collisions (rigid) | the contact list is not empty |
//! | 10 id | `+24` the material id of primitive `+12` (slot 16; < 0 → the part's surface index) |
//! | 12 nparts | the part count |
//! | 13 contains point | slot 70: the point is inside a part with flags `& 0xFDFF` |
//! | 19 contact patch (rigid) | `sub_350AF550` over all contacts (`+4` = CoM, `+16` = gravity are **inputs**; nothing is written) |
//! | 20 | `+4 = 0`, returns 1 |
//! | 26 constraint (rigid) | 0 (the port has no constraints, so no mask bit is ever set) |
//!
//! Not ported: ids 24 (extent, slot 35) and 25 (random position, slot 36) return 0; id 27
//! (copy of the `+344` block, returns 0 in the binary) writes nothing. The out-matrices of
//! `pe_status_pos` are not written through (use [`pos_matrices`]).

use super::{
    fits, is_entity, is_rigid, rd_i32, rd_u32, wr_f32, wr_i32, wr_q, wr_u32, wr_u64, wr_v3,
};
use crate::entities::base::geometry::rotate_inv;
use crate::entities::rigid::patch::fit_contact_patch;
use crate::entities::rigid::velocity::l_from_w;
use crate::ids::GeomType;
use crate::math::{Mat33, Vec3};
use crate::params::{StatusDynamics, StatusPos};
use crate::world::{EntIdx, World};

/// The port's stand-in for an `IGeometry*`.
fn addr<T: ?Sized>(p: *const T) -> u64 {
    p.cast::<u8>() as usize as u64
}

/// Raw `GetStatus(status)`: `buf` starts with the status type id and receives the answer.
/// Dispatches on the entity class.
pub fn get_status(w: &mut World, e: EntIdx, buf: &mut [u8]) -> i32 {
    if buf.len() < 4 || !is_entity(w, e) {
        return 0;
    }
    match &w.ent(e).kind {
        crate::entities::base::EntityKind::Living(_) => {
            return crate::entities::living::get_status(w, e, buf)
        }
        crate::entities::base::EntityKind::Particle(_) => {
            return crate::entities::particle::get_status(w, e, buf)
        }
        crate::entities::base::EntityKind::Rigid(r) if r.wheeled.is_some() => {
            return crate::entities::wheeled::get_status(w, e, buf)
        }
        _ => {}
    }
    class_get_status(w, e, buf)
}

/// `CRigidEntity::GetStatus` 0x350B1B80 for rigid entities, `CPhysicalEntity::GetStatus`
/// 0x350724C0 otherwise (what the derived classes call first).
pub fn class_get_status(w: &mut World, e: EntIdx, buf: &mut [u8]) -> i32 {
    if buf.len() < 4 || !is_entity(w, e) {
        return 0;
    }
    let ty = rd_i32(buf, 0);
    let rigid = is_rigid(w, e);
    if rigid && ty == 7 {
        if buf.len() < 8 {
            return 0;
        }
        let lag = rd_i32(buf, 4);
        let r = w.ent(e).rigid().expect("rigid");
        let aw = (r.awake_bits & 0xFF) as i32;
        if lag == 0 {
            return aw;
        }
        if r.last_ext_contact_step < 0 {
            return 1;
        }
        return i32::from(aw != 0 || r.last_ext_contact_step >= w.pump_counter.wrapping_sub(lag));
    }
    let res = base_status(w, e, buf);
    if !rigid {
        return res;
    }
    if res == 0 {
        return match ty {
            8 if fits::<StatusDynamics>(buf) => rigid_dynamics(w, e, buf),
            9 => {
                let head = w.ent(e).rigid().expect("rigid").contacts_head;
                i32::from(w.contacts.get(head).next != head)
            }
            19 if buf.len() >= 28 => contact_patch(w, e, buf),
            // 26: no constraint mask bits in the port
            _ => 0,
        };
    }
    if ty == 1 && buf.len() >= 124 && f32::from_bits(rd_u32(buf, 120)) > 0.0 {
        // timeBack > 0: the pose of the previous step
        let ent = w.ent(e);
        let r = ent.rigid().expect("rigid");
        let b = w.bodies.get(r.body);
        let q = r.prev_q.product(b.qfb);
        wr_q(buf, 60, q);
        let o = q.rotate(b.offsfb);
        wr_v3(
            buf,
            24,
            Vec3::new(r.prev_pos.x - o.x, r.prev_pos.y - o.y, r.prev_pos.z - o.z),
        );
    }
    res
}

fn base_status(w: &mut World, e: EntIdx, buf: &mut [u8]) -> i32 {
    match rd_i32(buf, 0) {
        1 if buf.len() >= 120 => status_pos(w, e, buf),
        10 if buf.len() >= 28 => status_id(w, e, buf),
        12 => w.ent(e).parts.len() as i32,
        7 => crate::entities::is_awake(w, e),
        13 if buf.len() >= 16 => {
            let pt = Vec3::new(
                f32::from_bits(rd_u32(buf, 4)),
                f32::from_bits(rd_u32(buf, 8)),
                f32::from_bits(rd_u32(buf, 12)),
            );
            contains_point(w, e, pt)
        }
        20 if buf.len() >= 8 => {
            wr_i32(buf, 4, 0);
            1
        }
        _ => 0,
    }
}

/// Id 1 `pe_status_pos` (base). In: `+4` partid, `+8` ipart, `+12` flags.
///
/// * `ipart == -1` with `partid >= 0`: the part with that id (missing → 0).
/// * `ipart >= 0` (out of range → 0): that part — `flags & 1` gives the part pose in the
///   entity frame, otherwise `q_ent·pos_part + pos_ent` and `q_ent ⊗ q_part`; scale = part
///   scale; `+4` = part id, `+16`/`+20` = part flags; bbox = the part's world box minus the
///   reported position.
/// * otherwise (any negative `ipart`): the entity pose (`+408`/`+420`, not the stepped
///   coords), scale 1, flagsOR / flagsAND over all parts (0 / -1 without parts), the geometry
///   of part 0, bbox = world box minus position.
///
/// Always: `+80` simclass. Geometry pointers (`+104`/`+112`) are the port's object addresses.
pub fn status_pos(w: &World, e: EntIdx, buf: &mut [u8]) -> i32 {
    let ent = w.ent(e);
    let n = ent.parts.len() as i32;
    let flags = rd_u32(buf, 12);
    let mut ip = rd_i32(buf, 8);
    if ip == -1 {
        let pid = rd_i32(buf, 4);
        if pid >= 0 {
            ip = ent
                .parts
                .iter()
                .position(|q| q.id == pid)
                .map_or(n, |i| i as i32);
            if ip == n {
                return 0;
            }
        }
    }
    let (pos, q, scale, bbox);
    if ip >= 0 {
        if ip >= n {
            return 0;
        }
        let part = &ent.parts[ip as usize];
        if flags & 1 != 0 {
            pos = part.pos;
            q = part.q;
        } else {
            let r = ent.q.rotate(part.pos);
            pos = Vec3::new(r.x + ent.pos.x, r.y + ent.pos.y, r.z + ent.pos.z);
            q = ent.q.product(part.q);
        }
        scale = part.scale;
        wr_i32(buf, 4, part.id);
        wr_u32(buf, 20, part.flags);
        wr_u32(buf, 16, part.flags);
        wr_u64(buf, 104, addr(std::sync::Arc::as_ptr(&part.pgeom.geom)));
        wr_u64(
            buf,
            112,
            addr(std::sync::Arc::as_ptr(&part.pgeom_proxy.geom)),
        );
        bbox = part.bbox;
    } else {
        pos = ent.pos;
        q = ent.q;
        scale = 1.0;
        let (mut or, mut and) = (0u32, u32::MAX);
        for p in &ent.parts {
            or |= p.flags;
            and &= p.flags;
        }
        wr_u32(buf, 20, and);
        wr_u32(buf, 16, or);
        match ent.parts.first() {
            Some(p) => {
                wr_u64(buf, 104, addr(std::sync::Arc::as_ptr(&p.pgeom.geom)));
                wr_u64(buf, 112, addr(std::sync::Arc::as_ptr(&p.pgeom_proxy.geom)));
            }
            None => {
                wr_u64(buf, 112, 0);
                wr_u64(buf, 104, 0);
            }
        }
        bbox = ent.bbox;
    }
    wr_v3(
        buf,
        36,
        Vec3::new(bbox[0].x - pos.x, bbox[0].y - pos.y, bbox[0].z - pos.z),
    );
    wr_v3(
        buf,
        48,
        Vec3::new(bbox[1].x - pos.x, bbox[1].y - pos.y, bbox[1].z - pos.z),
    );
    wr_v3(buf, 24, pos);
    wr_q(buf, 60, q);
    wr_f32(buf, 76, scale);
    wr_i32(buf, 80, ent.sim_class());
    1
}

/// The matrices `pe_status_pos` writes through `pMtx3x4` (`+88`) and `pMtx3x3` (`+96`):
/// `scale·R(q)` with the position as the fourth column of the 3×4 one.
#[must_use]
pub fn pos_matrices(s: &StatusPos) -> ([f32; 12], Mat33) {
    let r = Mat33::from_quat(s.q).m;
    let k = s.scale;
    let m = [
        [k * r[0][0], k * r[0][1], k * r[0][2]],
        [k * r[1][0], k * r[1][1], k * r[1][2]],
        [k * r[2][0], k * r[2][1], k * r[2][2]],
    ];
    (
        [
            m[0][0], m[0][1], m[0][2], s.pos.x, m[1][0], m[1][1], m[1][2], s.pos.y, m[2][0],
            m[2][1], m[2][2], s.pos.z,
        ],
        Mat33 { m },
    )
}

/// Id 10 (material id). `ipart < 0` searches `partid` among the first `nParts − 1` parts only
/// (the last part is never matched by id, but is the fallback index — binary behaviour).
fn status_id(w: &World, e: EntIdx, buf: &mut [u8]) -> i32 {
    let ent = w.ent(e);
    let n = ent.parts.len() as i32;
    let mut ip = rd_i32(buf, 4);
    if ip < 0 {
        ip = 0;
        let pid = rd_i32(buf, 8);
        while ip < n - 1 {
            if ent.parts[ip as usize].id == pid {
                break;
            }
            ip += 1;
        }
    }
    if ip >= n {
        return 0;
    }
    let part = &ent.parts[ip as usize];
    let geom = if rd_i32(buf, 20) == 0 {
        &part.pgeom.geom
    } else {
        &part.pgeom_proxy.geom
    };
    let iprim = rd_i32(buf, 12);
    let ifeature = rd_i32(buf, 16);
    if iprim as u32 >= geom.primitive_count() as u32
        || (geom.geom_type() == GeomType::TriMesh && ifeature > 2)
    {
        return 0;
    }
    let id = geom.primitive_mat_id(iprim);
    let surf = ((part.surface_bits << 22) as i32) >> 22;
    wr_i32(buf, 24, if id >= 0 { id } else { surf });
    1
}

/// Slot 70 (`CPhysicalEntity` 0x350576C0): the point, taken to the entity frame
/// (`q⁻¹·(pt − pos)`), then to each part frame (`q_part⁻¹·(p − pos_part)·(1/scale)`), is
/// inside the geometry of a part with flags `& 0xFDFF` (slot 8 non-zero, "not supported"
/// included).
#[must_use]
pub fn contains_point(w: &World, e: EntIdx, pt: Vec3) -> i32 {
    let ent = w.ent(e);
    let l = rotate_inv(
        ent.q,
        Vec3::new(pt.x - ent.pos.x, pt.y - ent.pos.y, pt.z - ent.pos.z),
    );
    for part in &ent.parts {
        if part.flags & 0xFDFF != 0 {
            let d = rotate_inv(
                part.q,
                Vec3::new(l.x - part.pos.x, l.y - part.pos.y, l.z - part.pos.z),
            );
            let k = 1.0 / part.scale;
            let p = Vec3::new(k * d.x, k * d.y, k * d.z);
            if part.pgeom.geom.point_inside_status(p) != 0 {
                return 1;
            }
        }
    }
    0
}

/// Rigid id 8 `pe_status_dynamics`. Awake: `+12` v, `+24` w, `+36` a = gravity +
/// Minv·Fcoll, `+48` wa = Iinv·(Tcoll − w × L(w)) with `L(w) = q·(Ibody ∘ (q⁻¹·w))`;
/// asleep: those four are zero. Always: `+60` com, `+72` Iinv, `+108` submerged fraction,
/// `+112` mass. `+4`/`+8` (part selectors), `+116`, `+120` are not touched.
pub fn rigid_dynamics(w: &World, e: EntIdx, buf: &mut [u8]) -> i32 {
    let r = w.ent(e).rigid().expect("rigid");
    let b = w.bodies.get(r.body);
    if r.awake_bits & 0xFF != 0 {
        wr_v3(buf, 12, b.v);
        wr_v3(buf, 24, b.w);
        let a = Vec3::new(
            b.fcoll.x * b.minv + r.gravity.x,
            b.fcoll.y * b.minv + r.gravity.y,
            b.fcoll.z * b.minv + r.gravity.z,
        );
        wr_v3(buf, 36, a);
        let l = l_from_w(b, b.w);
        let (wx, wy, wz) = (b.w.x, b.w.y, b.w.z);
        let v38 = b.tcoll.x - (wy * l.z - wz * l.y);
        let v39 = b.tcoll.y - (wz * l.x - wx * l.z);
        let v40 = b.tcoll.z - (wx * l.y - wy * l.x);
        let m = &b.iinv.m;
        wr_f32(buf, 48, (v39 * m[0][1] + v38 * m[0][0]) + v40 * m[0][2]);
        wr_f32(buf, 56, (v39 * m[2][1] + v38 * m[2][0]) + v40 * m[2][2]);
        wr_f32(buf, 52, (v39 * m[1][1] + v38 * m[1][0]) + v40 * m[1][2]);
    } else {
        for o in (12..60).step_by(4) {
            wr_u32(buf, o, 0);
        }
    }
    wr_v3(buf, 60, b.pos);
    wr_f32(buf, 108, r.submerged_fraction);
    wr_f32(buf, 112, b.m);
    for (k, v) in b.iinv.to_array().iter().enumerate() {
        wr_f32(buf, 72 + 4 * k, *v);
    }
    1
}

/// Rigid id 19: fit a patch through **all** contacts (no stop flag, `iCaller` 0) with the
/// tolerance `0.1·((dy + dx) + dz)` of the bbox, `+4` as the centre of mass and `+16` as the
/// gravity; returns whether the projected centre lies inside. The fitted normal goes to a
/// stack temporary in the binary, so nothing is written back.
fn contact_patch(w: &mut World, e: EntIdx, buf: &mut [u8]) -> i32 {
    let rd_v = |o: usize| {
        Vec3::new(
            f32::from_bits(rd_u32(buf, o)),
            f32::from_bits(rd_u32(buf, o + 4)),
            f32::from_bits(rd_u32(buf, o + 8)),
        )
    };
    let (com, g) = (rd_v(4), rd_v(16));
    let bb = w.ent(e).bbox;
    let tol = (((bb[1].y - bb[0].y) + (bb[1].x - bb[0].x)) + (bb[1].z - bb[0].z)) * 0.1;
    let head = w.ent(e).rigid().expect("rigid").contacts_head;
    let first = w.contacts.get(head).next;
    i32::from(fit_contact_patch(&mut w.contacts, head, first, 0, tol, 0, com, g).ok)
}
