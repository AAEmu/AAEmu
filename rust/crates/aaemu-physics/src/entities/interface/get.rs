//! Slot 8 `GetParams`: `CPhysicalEntity::GetParams` 0x35054E60 and
//! `CRigidEntity::GetParams` 0x350AABD0 (rigid ids 10 and 12 first, then the base).
//!
//! | id | written (x64 offsets) |
//! |---|---|
//! | 6 part | `+4` partid, `+8` ipart, `+16` pos, `+28` q, `+44` scale, `+68`/`+72` flags, `+76`/`+80` collider flags, `+84` mass, `+88` density, `+92` minContactDist, `+96`/`+104` geometries, `+112` idmatBreakable, `+120` lattice (null), `+128` idSkeleton (-1), `+136` matMapping, `+144` nMats |
//! | 9 outer entity | `+8`, `+16` (always null in the port) |
//! | 10 simulation (rigid) | everything but `+68` (angular acceleration) |
//! | 11 foreign data | `+8` pointer, `+16` iForeignData, `+20` iForeignFlags (sign-extended) |
//! | 12 buoyancy (rigid) | `+4`, `+8`, `+12`, `+16`, `+20`, `+64`, `+76` |
//! | 14 bbox, 15 flags, 20 ground plane, 23 timeout | the stored values |
//! | 21 structural joint, 24 skeleton | return 0 (no breakable structure) |
//!
//! Not ported: the out-matrices of id 6 (`+48` `Matrix34*`, `+56` `Matrix33*`) are not
//! written through (use [`part_matrices`]); `bAddrefGeoms` has nothing to AddRef. Id 20 with a
//! negative index reads before the plane array in the binary; the port returns 0.

use super::{fits, is_entity, is_rigid, rd_i32, wr_f32, wr_i32, wr_q, wr_u32, wr_u64, wr_v3};
use crate::entities::base::Part;
use crate::math::Mat33;
use crate::params::{ParamsBuoyancy, ParamsPart, ParamsSimulation};
use crate::sentinel::{is_unused, UNUSED_I32 as UI};
use crate::world::{EntIdx, World};

/// Raw `GetParams(params)`: `buf` starts with the params type id and receives the answer.
/// Dispatches on the entity class.
pub fn get_params(w: &mut World, e: EntIdx, buf: &mut [u8]) -> i32 {
    if buf.len() < 4 || !is_entity(w, e) {
        return 0;
    }
    match &w.ent(e).kind {
        crate::entities::base::EntityKind::Living(_) => {
            return crate::entities::living::get_params(w, e, buf)
        }
        crate::entities::base::EntityKind::Particle(_) => {
            return crate::entities::particle::get_params(w, e, buf)
        }
        crate::entities::base::EntityKind::Rigid(r) if r.wheeled.is_some() => {
            return crate::entities::wheeled::get_params(w, e, buf)
        }
        _ => {}
    }
    class_get_params(w, e, buf)
}

/// `CRigidEntity::GetParams` 0x350AABD0 for rigid entities, `CPhysicalEntity::GetParams`
/// 0x35054E60 otherwise (what the derived classes call first).
pub fn class_get_params(w: &mut World, e: EntIdx, buf: &mut [u8]) -> i32 {
    if buf.len() < 4 || !is_entity(w, e) {
        return 0;
    }
    let ty = rd_i32(buf, 0);
    if is_rigid(w, e) {
        match ty {
            10 if fits::<ParamsSimulation>(buf) => return rigid_simulation(w, e, buf),
            12 if fits::<ParamsBuoyancy>(buf) => return rigid_buoyancy(w, e, buf),
            _ => {}
        }
    }
    let ent = w.ent(e);
    match ty {
        14 if buf.len() >= 28 => {
            wr_v3(buf, 4, ent.bbox[0]);
            wr_v3(buf, 16, ent.bbox[1]);
            1
        }
        9 if buf.len() >= 24 => {
            wr_u64(buf, 8, 0);
            wr_u64(buf, 16, 0);
            1
        }
        11 if buf.len() >= 24 => {
            wr_i32(buf, 16, i32::from(ent.i_foreign_data));
            wr_u64(buf, 8, ent.foreign_data);
            wr_i32(buf, 20, i32::from(ent.i_foreign_flags));
            1
        }
        6 if fits::<ParamsPart>(buf) => part(w, e, buf),
        15 if buf.len() >= 8 => {
            wr_u32(buf, 4, ent.flags);
            1
        }
        20 if buf.len() >= 32 => {
            let i = rd_i32(buf, 4);
            match usize::try_from(i)
                .ok()
                .and_then(|i| ent.ground_planes.get(i))
            {
                Some(&(origin, n)) => {
                    wr_v3(buf, 20, n);
                    wr_v3(buf, 8, origin);
                    1
                }
                None => 0,
            }
        }
        23 if buf.len() >= 12 => {
            wr_f32(buf, 4, ent.time_idle);
            wr_f32(buf, 8, ent.max_time_idle);
            1
        }
        _ => 0,
    }
}

/// The port's stand-in for a `phys_geometry*` / `int*`: the address of the shared object.
fn addr<T: ?Sized>(p: *const T) -> u64 {
    p.cast::<u8>() as usize as u64
}

/// Id 6: part lookup by `ipart`, or by `partid` when `ipart` is unused or -1 (a part id
/// that is not found, or an `ipart` out of range, returns 0).
fn part(w: &World, e: EntIdx, buf: &mut [u8]) -> i32 {
    let ent = w.ent(e);
    let n = ent.parts.len() as i32;
    let mut ip = rd_i32(buf, 8);
    let pid = rd_i32(buf, 4);
    if (ip == UI || ip == -1) && pid >= 0 {
        ip = ent
            .parts
            .iter()
            .position(|q| q.id == pid)
            .map_or(n, |i| i as i32);
        if ip == n {
            return 0;
        }
    } else if ip as u32 >= n as u32 {
        return 0;
    }
    let p: &Part = &ent.parts[ip as usize];
    wr_i32(buf, 8, ip);
    wr_i32(buf, 4, p.id);
    wr_v3(buf, 16, p.pos);
    wr_q(buf, 28, p.q);
    wr_f32(buf, 44, p.scale);
    wr_u32(buf, 72, p.flags);
    wr_u32(buf, 68, p.flags);
    wr_u32(buf, 80, p.flags_collider);
    wr_u32(buf, 76, p.flags_collider);
    wr_f32(buf, 84, p.mass);
    let vp = p.pgeom_proxy.volume;
    let density = if vp > 0.0 {
        let s = p.scale;
        p.mass / (((s * s) * s) * vp)
    } else {
        0.0
    };
    wr_f32(buf, 88, density);
    wr_f32(buf, 92, p.min_contact_dist);
    wr_u64(buf, 96, addr(std::sync::Arc::as_ptr(&p.pgeom)));
    wr_u64(buf, 104, addr(std::sync::Arc::as_ptr(&p.pgeom_proxy)));
    wr_i32(buf, 112, ((p.surface_bits << 8) as i32) >> 18);
    wr_u64(buf, 120, 0);
    let mm = match &p.mat_mapping {
        Some(m) => addr(m.as_ptr()),
        None if !p.pgeom.mat_mapping.is_empty() => addr(p.pgeom.mat_mapping.as_ptr()),
        None => 0,
    };
    wr_u64(buf, 136, mm);
    wr_u32(buf, 144, p.nmats_bits.wrapping_mul(2) >> 1);
    wr_i32(buf, 128, -1);
    1
}

/// The matrices GetParams(part) writes through `pMtx3x4` (`+48`) and `pMtx3x3` (`+56`):
/// `scale·R(q)` (row-major) with the position in the fourth column of the 3×4 one.
#[must_use]
pub fn part_matrices(p: &Part) -> ([f32; 12], Mat33) {
    let s = p.scale;
    let r = Mat33::from_quat(p.q).m;
    let m33 = Mat33::from_array([
        s * r[0][0],
        s * r[0][1],
        s * r[0][2],
        s * r[1][0],
        s * r[1][1],
        s * r[1][2],
        s * r[2][0],
        s * r[2][1],
        s * r[2][2],
    ]);
    let m = m33.m;
    let m34 = [
        m[0][0], m[0][1], m[0][2], p.pos.x, m[1][0], m[1][1], m[1][2], p.pos.y, m[2][0], m[2][1],
        m[2][2], p.pos.z,
    ];
    (m34, m33)
}

/// Rigid id 10.
fn rigid_simulation(w: &World, e: EntIdx, buf: &mut [u8]) -> i32 {
    let ent = w.ent(e);
    let r = ent.rigid().expect("rigid");
    let b = w.bodies.get(r.body);
    wr_v3(buf, 20, r.gravity);
    wr_f32(buf, 8, r.max_time_step);
    wr_f32(buf, 12, r.min_energy);
    wr_f32(buf, 16, r.damping);
    wr_f32(buf, 32, r.damping_freefall);
    wr_v3(buf, 36, r.gravity_freefall);
    wr_f32(buf, 48, r.max_w);
    wr_i32(buf, 4, ent.sim_class());
    let density = if b.volume > 0.0 { b.m / b.volume } else { 0.0 };
    wr_f32(buf, 56, density);
    wr_f32(buf, 52, b.m);
    wr_u32(buf, 60, (r.awake_bits >> 22) & 0x1F);
    wr_i32(buf, 64, i32::from(r.flags2 & 0x800_0000 != 0));
    wr_f32(buf, 80, r.max_friction);
    1
}

/// Rigid id 12: the multipliers and the absolute values against the reference density /
/// resistance (the global area's when it has a water plane, else 1000).
fn rigid_buoyancy(w: &World, e: EntIdx, buf: &mut [u8]) -> i32 {
    let (mut rho, mut res) = (1000.0f32, 1000.0f32);
    let g = w.global_area;
    if !g.is_none() && !is_unused(w.area(g).buoyancy.water_plane_origin.x) {
        let b = &w.area(g).buoyancy;
        rho = b.water_density;
        res = b.water_resistance;
    }
    let r = w.ent(e).rigid().expect("rigid");
    wr_f32(buf, 4, rho * r.k_water_density);
    wr_f32(buf, 8, r.k_water_density);
    wr_f32(buf, 16, res * r.k_water_resistance);
    wr_f32(buf, 20, r.k_water_resistance);
    wr_f32(buf, 12, r.water_damping);
    wr_f32(buf, 64, r.min_energy_water);
    wr_f32(buf, 76, r.water_level_offset);
    1
}
