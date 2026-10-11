//! Slot 7 `SetParams`: `CPhysicalEntity::SetParams` 0x3506EF20 and
//! `CRigidEntity::SetParams` 0x350C4CC0.
//!
//! Rigid: lock-or-queue, then the base with `bThreadSafe = 1`; when the base handled the id,
//! the rigid post-processing for ids 0, 6 and 15 runs; otherwise the rigid ids 10 and 12.
//!
//! | id | handler | notes |
//! |---|---|---|
//! | 0 pos | [`set_params_pos`] (base + rigid post) | matrices not ported (must be null) |
//! | 6 part | [`set_params_part`] (+ rigid post) | see below |
//! | 9 outer entity | null pointers only (the port stores no outer entity) | |
//! | 10 simulation | [`set_params_simulation`] (rigid) | |
//! | 11 foreign data | [`set_params_foreign_data`] | |
//! | 12 buoyancy | [`buoyancy::set_params_buoyancy`] (rigid) | |
//! | 14 bbox | [`set_params_bbox`] | |
//! | 15 flags | [`set_params_flags`] (+ rigid post) | |
//! | 20 ground plane | [`set_params_ground_plane`] | |
//! | 21 structural joint | `idx == -1` only (returns 1: no structure) | |
//! | 23 timeout | [`set_params_timeout`] | |
//! | 24 skeleton, 25 part velocity | return 0 (they need a breakable structure, never present) | |
//! | 26 byte flag | `+332` | |
//!
//! Not ported (when executed, the raw entry returns 0 without further side effects — the
//! lock-or-queue step, including deferral, is ported; a replayed unported record is
//! dropped): `pe_params_pos` / `pe_params_part`
//! matrices (`pMtx3x4`/`pMtx3x3` must be null), `pe_params_part` geometry / proxy / lattice
//! pointers other than null or `-1`, a `pMatMapping` other than `-1`, `idSkeleton` naming an
//! existing part (skeleton creation `sub_3505CC30`), `idParent` (creates a breakable
//! structure), a negative `ipart` other than "unused", `pe_params_outer_entity` with real
//! pointers, `pe_params_structural_joint` with `idx != -1`. The validators
//! (`sub_350741C0` & co.) only reject while the world variable `+312` is set, which the port
//! never sets. The OBB/grid cache refresh `sub_35053DC0` only acts for flag-0x10 entities with
//! per-part placeholders, which the port does not create.

use super::{caller, is_entity, is_rigid, params_gate, rd_i32, rd_u64, Gate};
use crate::entities::base::params::set_params_pos;
use crate::entities::base::{compute_bbox, EntityKind};
use crate::entities::rigid::{buoyancy, contacts, mass};
use crate::math::Vec3;
use crate::params::{
    ParamsBbox, ParamsBuoyancy, ParamsByteFlag, ParamsFlags, ParamsForeignData, ParamsGroundPlane,
    ParamsPart, ParamsPos, ParamsSimulation, ParamsTimeout, PeStruct,
};
use crate::sentinel::{is_unused, UNUSED_I32 as UI};
use crate::world::events::Event;
use crate::world::{EntIdx, World};

/// Raw `SetParams(params, bThreadSafe)`: `buf` starts with the params type id. Returns the
/// slot's result (1 handled, 0 not handled, `ipart + 1` for `pe_params_part`). Dispatches on
/// the entity class (living, particle and wheeled vehicles have their own slot 7).
pub fn set_params(w: &mut World, e: EntIdx, buf: &mut [u8], thread_safe: i32) -> i32 {
    if buf.len() < 4 || !is_entity(w, e) {
        return 0;
    }
    match &w.ent(e).kind {
        EntityKind::Living(_) => {
            return crate::entities::living::set_params(w, e, buf, thread_safe)
        }
        EntityKind::Particle(_) => {
            return crate::entities::particle::set_params(w, e, buf, thread_safe)
        }
        EntityKind::Rigid(r) if r.wheeled.is_some() => {
            return crate::entities::wheeled::set_params(w, e, buf, thread_safe)
        }
        _ => {}
    }
    class_set_params(w, e, buf, thread_safe)
}

/// `CRigidEntity::SetParams` 0x350C4CC0 for rigid entities (wheeled vehicles included),
/// `CPhysicalEntity::SetParams` 0x3506EF20 for every other class: the entry the derived
/// classes call (with `bThreadSafe = 1`) before their own ids.
pub fn class_set_params(w: &mut World, e: EntIdx, buf: &mut [u8], thread_safe: i32) -> i32 {
    if buf.len() < 4 || !is_entity(w, e) {
        return 0;
    }
    if let Gate::Skip { .. } = params_gate(w, e, buf, thread_safe) {
        return 1;
    }
    if !supported(w, e, buf) {
        return 0;
    }
    let ty = rd_i32(buf, 0);
    if ty == 0 {
        // base id 0 + the rigid post-processing
        return set_params_pos(w, e, &ParamsPos::read(buf), true);
    }
    let flags_before = w.ent(e).flags;
    let r = base_set_params(w, e, buf);
    if !is_rigid(w, e) {
        return r;
    }
    if r != 0 {
        match ty {
            6 => rigid_post_part(w, e, &ParamsPart::read(buf), r),
            15 => rigid_post_flags(w, e, &ParamsFlags::read(buf), flags_before),
            _ => {}
        }
        return r;
    }
    match ty {
        10 if super::fits::<ParamsSimulation>(buf) => {
            set_params_simulation(w, e, &ParamsSimulation::read(buf))
        }
        12 if super::fits::<ParamsBuoyancy>(buf) => {
            buoyancy::set_params_buoyancy(w, e, &ParamsBuoyancy::read(buf))
        }
        _ => 0,
    }
}

/// The checks for features the port cannot execute (see the module docs).
fn supported(w: &World, e: EntIdx, buf: &[u8]) -> bool {
    let ty = rd_i32(buf, 0);
    let size_ok = |n: usize| buf.len() >= n;
    match ty {
        0 => size_ok(ParamsPos::SIZE) && rd_u64(buf, 40) == 0 && rd_u64(buf, 48) == 0,
        6 => {
            if !size_ok(ParamsPart::SIZE) {
                return false;
            }
            let p = ParamsPart::read(buf);
            let none = |x: u64| x == 0 || x == u64::MAX;
            if !none(p.phys_geom.0) || !none(p.phys_geom_proxy.0) || p.mat_mapping.0 != u64::MAX {
                return false;
            }
            if p.ipart == UI && p.part_id == UI {
                return true; // all parts: matrices, lattice, skeleton and parent are ignored
            }
            if p.ipart != UI && p.ipart < 0 {
                return false; // reads before the part array in the binary
            }
            if !p.mtx3x4.is_null() || !p.mtx3x3.is_null() || !none(p.lattice.0) || p.id_parent != UI
            {
                return false;
            }
            p.id_skeleton == UI
                || p.id_skeleton < 0
                || !w.ent(e).parts.iter().any(|q| q.id == p.id_skeleton)
        }
        9 => size_ok(24) && rd_u64(buf, 8) == 0 && rd_u64(buf, 16) == 0,
        21 => size_ok(12) && rd_i32(buf, 8) == -1,
        10 => size_ok(ParamsSimulation::SIZE),
        11 => size_ok(ParamsForeignData::SIZE),
        12 => size_ok(ParamsBuoyancy::SIZE),
        14 => size_ok(ParamsBbox::SIZE),
        15 => size_ok(ParamsFlags::SIZE),
        20 => size_ok(ParamsGroundPlane::SIZE),
        23 => size_ok(ParamsTimeout::SIZE),
        24 => size_ok(48),
        25 => size_ok(32),
        26 => size_ok(ParamsByteFlag::SIZE),
        _ => true,
    }
}

/// `CPhysicalEntity::SetParams` 0x3506EF20 after its lock-or-queue (ids other than 0).
fn base_set_params(w: &mut World, e: EntIdx, buf: &[u8]) -> i32 {
    match rd_i32(buf, 0) {
        6 => set_params_part(w, e, &ParamsPart::read(buf)),
        // pOuterEntity / pBoundingGeometry: null in, null stored (Release/AddRef of nothing)
        9 => 1,
        11 => set_params_foreign_data(w, e, &ParamsForeignData::read(buf)),
        14 => set_params_bbox(w, e, &ParamsBbox::read(buf)),
        15 => set_params_flags(w, e, &ParamsFlags::read(buf)),
        20 => set_params_ground_plane(w, e, &ParamsGroundPlane::read(buf)),
        // idx == -1: "update the next-joint id" of the structure, which never exists
        21 => 1,
        23 => set_params_timeout(w, e, &ParamsTimeout::read(buf)),
        26 => {
            w.ent_mut(e).byte332 = ParamsByteFlag::read(buf).value;
            1
        }
        // 24 skeleton / 25 part velocity need the breakable structure: not handled
        _ => 0,
    }
}

/// Id 6 `pe_params_part` (base 0x3506EF20). Returns `ipart + 1`, 1 for the all-parts form,
/// 0 when the part is not found.
///
/// * `ipart` and `partid` unused: every part with `flags & flagsCond` (unused = all) gets
///   `flags = flagsOR | flags & flagsAND` (same for the collider flags) and `mass`; a set
///   `idmatBreakable` always stores **-1** (`bits |= 0xFFFC00`, the value is not used — a
///   binary bug, mirrored). Density and minContactDist are ignored in this form.
/// * Otherwise the part (by index, or by id) gets: `partid` as its new id (only with
///   `ipart` given), `mass`; `density` → `mass = scale³·V·density` (with the part's **old**
///   scale); `minContactDist` (only with `idSkeleton` unused); `idmatBreakable` (unused with
///   `pPhysGeom != -1`, e.g. null, runs the automatic `sub_35052CC0`); the flag updates; then
///   pos / q / scale. With `bRecalcBBox` the entity bbox and the part box are recomputed
///   at the new part pose (ComputeBBox flag 2 for that part) and the entity re-gridded;
///   `bRecalcBBox & 2` then wakes every entity in the box grown by `10·contact_gap`
///   (objtypes 10, `Awake(1, 0)`, the entity itself included).
pub fn set_params_part(w: &mut World, e: EntIdx, p: &ParamsPart) -> i32 {
    let n = w.ent(e).parts.len() as i32;
    let mut ip = p.ipart;
    if ip == UI {
        if p.part_id == UI {
            let ent = w.ent_mut(e);
            for part in &mut ent.parts {
                if p.flags_cond == UI || (p.flags_cond as u32 & part.flags) != 0 {
                    part.flags = p.flags_or | (part.flags & p.flags_and);
                    part.flags_collider =
                        p.flags_collider_or | (part.flags_collider & p.flags_collider_and);
                    if !is_unused(p.mass) {
                        part.mass = p.mass;
                    }
                    if p.idmat_breakable != UI {
                        part.surface_bits |= 0xFF_FC00;
                    }
                }
            }
            return 1;
        }
        if n <= 0 {
            return 0;
        }
        ip = w
            .ent(e)
            .parts
            .iter()
            .position(|q| q.id == p.part_id)
            .map_or(n, |i| i as i32);
    }
    if ip >= n || ip < 0 {
        return 0;
    }
    let i = ip as usize;
    let (mut npos, mut nq, mut nscale) = {
        let part = &w.ent(e).parts[i];
        (part.pos, part.q, part.scale)
    };
    {
        let part = &mut w.ent_mut(e).parts[i];
        if p.ipart != UI && p.part_id != UI {
            part.id = p.part_id;
        }
        if !is_unused(p.pos.x) {
            npos = p.pos;
        }
        if !is_unused(p.q.w) {
            nq = p.q;
        }
        if !is_unused(p.scale) {
            nscale = p.scale;
        }
        if !is_unused(p.mass) {
            part.mass = p.mass;
        }
        if !is_unused(p.density) {
            let s = part.scale;
            part.mass = (((s * s) * s) * part.pgeom.volume) * p.density;
        }
        if !is_unused(p.min_contact_dist) && p.id_skeleton == UI {
            part.min_contact_dist = p.min_contact_dist;
        }
    }
    if p.idmat_breakable == UI {
        if p.mat_mapping.0 != u64::MAX || p.phys_geom.0 != u64::MAX {
            crate::entities::base::geometry::auto_idmat_breakable(w, e, i, n, -1);
        }
    } else {
        let part = &mut w.ent_mut(e).parts[i];
        part.surface_bits &= 0xFF00_03FF;
        part.surface_bits |= ((p.idmat_breakable & 0x3FFF) as u32) << 10;
    }
    {
        let part = &mut w.ent_mut(e).parts[i];
        part.flags = p.flags_or | (part.flags & p.flags_and);
        part.flags_collider = p.flags_collider_or | (part.flags_collider & p.flags_collider_and);
    }
    let mut ent_bbox = [Vec3::ZERO; 2];
    if p.recalc_bbox != 0 {
        // the part's new-coords pointer is aimed at the new pose for ComputeBBox(flag 2, ipart)
        {
            let part = &mut w.ent_mut(e).parts[i];
            part.pos = npos;
            part.q = nq;
            part.scale = nscale;
        }
        ent_bbox = compute_bbox(w, e, 2, ip, None);
        w.reposition_entity(e, 1, Some(ent_bbox));
    }
    {
        let ent = w.ent_mut(e);
        let part = &mut ent.parts[i];
        part.pos = npos;
        part.q = nq;
        part.scale = nscale;
        if p.recalc_bbox != 0 {
            ent.bbox = ent_bbox;
            let part = &mut ent.parts[i];
            let pb = part.bbox_tmp.unwrap_or(part.bbox);
            part.bbox = pb;
            if let Some(t) = &mut part.bbox_tmp {
                *t = pb;
            }
            for (k, q) in ent.parts.iter_mut().enumerate() {
                if k != i {
                    if let Some(t) = q.bbox_tmp {
                        q.bbox = t;
                    }
                }
            }
        }
    }
    if p.recalc_bbox != 0 && p.recalc_bbox & 2 != 0 {
        let g = w.vars.contact_gap * 10.0;
        let bb = w.ent(e).bbox;
        let hi = Vec3::new(g + bb[1].x, g + bb[1].y, g + bb[1].z);
        let lo = Vec3::new(bb[0].x - g, bb[0].y - g, bb[0].z - g);
        let c = caller(w);
        let list = w.get_entities_in_box(lo, hi, 10, None, c);
        for &o in list.iter().rev() {
            crate::entities::awake(w, o, 1, 0);
        }
    }
    ip + 1
}

/// `CRigidEntity::SetParams` post-processing of id 6 (`r` = the base result, `ipart + 1`).
fn rigid_post_part(w: &mut World, e: EntIdx, p: &ParamsPart, r: i32) {
    let part_mass = |w: &World| w.ent(e).parts.get((r - 1) as usize).map_or(0.0, |q| q.mass);
    if p.part_id == UI && p.ipart == UI {
        if !is_unused(p.mass) {
            mass::recompute_mass(w, e);
        }
    } else if is_unused(p.mass)
        && is_unused(p.density)
        && p.phys_geom.0 == u64::MAX
        && p.phys_geom_proxy.0 == u64::MAX
    {
        if p.recalc_bbox != 0 && part_mass(w) > 0.0 {
            mass::recompute_mass(w, e);
        }
    } else {
        contacts::drop_contacts(w, e);
        if part_mass(w) > 0.0 || !is_unused(p.mass) || !is_unused(p.density) {
            mass::recompute_mass(w, e);
        }
    }
}

/// Id 11 foreign data (base). `pForeignData` is stored when not `-1` (the binary first
/// clears it in that case), `iForeignData` / `iForeignFlags` when set, then **always**
/// `iForeignFlags = flagsOR | flagsAND & iForeignFlags` on the low 16 bits — with both unused
/// (`0x80000000`, low word 0) this clears the flags, so `iForeignFlags` alone never sticks
/// (binary behaviour, mirrored). When a non-null pointer is replaced by a different non-null
/// one, the logged events of this entity (and of deleted entities) are purged
/// (`sub_35079B30` with the deletion counter raised).
pub fn set_params_foreign_data(w: &mut World, e: EntIdx, p: &ParamsForeignData) -> i32 {
    let ent = w.ent_mut(e);
    let old = ent.foreign_data;
    let set_ptr = p.foreign_data.0 != u64::MAX;
    ent.foreign_data = if set_ptr { 0 } else { old };
    if p.i_foreign_data != UI {
        ent.i_foreign_data = p.i_foreign_data as i16;
    }
    if set_ptr {
        ent.foreign_data = p.foreign_data.0;
    }
    if p.i_foreign_flags != UI {
        ent.i_foreign_flags = p.i_foreign_flags as i16;
    }
    ent.i_foreign_flags =
        (p.i_foreign_flags_or as i16) | ((p.i_foreign_flags_and as i16) & ent.i_foreign_flags);
    let new = ent.foreign_data;
    if old != 0 && new != 0 && old != new {
        w.ent_mut(e).deleted += 1;
        purge_logged_events(w);
        w.ent_mut(e).deleted -= 1;
    }
    1
}

/// `sub_35079B30`: drop the logged events whose entity (types > 1) or entities (types 0, 1)
/// are being deleted.
fn purge_logged_events(w: &mut World) {
    let alive = |w: &World, x: EntIdx| {
        x.is_none()
            || w.ents
                .get(x.0 as usize)
                .is_some_and(|o| o.as_ref().is_some_and(|q| q.deleted == 0))
    };
    let evs = std::mem::take(&mut w.events.logged);
    let kept = evs
        .into_iter()
        .filter(|ev| match ev {
            Event::BBoxOverlap { ent } | Event::Collision { ent, .. } => {
                alive(w, ent[0]) && alive(w, ent[1])
            }
            Event::StateChange { ent, .. }
            | Event::SimClassChange { ent, .. }
            | Event::PostStep { ent, .. } => alive(w, *ent),
            Event::AreaCallback(a) => a.ent.is_none_or(|x| alive(w, x)),
            Event::AreaChange { area, .. } => alive(w, *area),
        })
        .collect();
    w.events.logged = kept;
}

/// Id 14 `pe_params_bbox` (base): the given corners replace the current ones, the entity is
/// re-gridded with the new box (RepositionEntity flag 1) and the box stored.
pub fn set_params_bbox(w: &mut World, e: EntIdx, p: &ParamsBbox) -> i32 {
    let mut bb = w.ent(e).bbox;
    if !is_unused(p.bbox_min.x) {
        bb[0] = p.bbox_min;
    }
    if !is_unused(p.bbox_max.x) {
        bb[1] = p.bbox_max;
    }
    w.reposition_entity(e, 1, Some(bb));
    w.ent_mut(e).bbox = bb;
    1
}

/// Id 15 flags (base): `flags`, then `& flagsAND`, then `| flagsOR` (each when set).
///
/// For a flag-0x10 ("large object") entity with simclass < 3, the grid cells covered by its
/// bbox decide the mode: `4·nx·ny >= nParts` keeps 0x10 and clears 0x400, otherwise 0x10 is
/// replaced by 0x400 (`n = int(extent·cellSizeInv + 0.5)` through the `+12582912` rounding).
/// Then 0x400 set on a hidden entity (cells -3) un-hides it (cells -2; re-gridded only when
/// `|pos|² > 0` — an entity at the origin stays out of the grid, binary behaviour), and
/// 0x400 clear on a gridded entity detaches it from the grid (cells -3).
pub fn set_params_flags(w: &mut World, e: EntIdx, p: &ParamsFlags) -> i32 {
    {
        let ent = w.ent_mut(e);
        if p.flags != UI {
            ent.flags = p.flags as u32;
        }
        if p.flags_and != UI {
            ent.flags &= p.flags_and as u32;
        }
        if p.flags_or != UI {
            ent.flags |= p.flags_or as u32;
        }
    }
    let ent = w.ent(e);
    let mut fl = ent.flags;
    if fl & 0x10 != 0 && ((ent.id_bits & 0xFF00_0000) as i32) < 0x300_0000 {
        let g = &w.grid;
        let ext = [
            ent.bbox[1].x - ent.bbox[0].x,
            ent.bbox[1].y - ent.bbox[0].y,
            ent.bbox[1].z - ent.bbox[0].z,
        ];
        let az = g.axisz;
        const INC: [usize; 3] = [1, 2, 0];
        const DEC: [usize; 3] = [2, 0, 1];
        let nx = ((ext[INC[az]] * g.step_r[0] + 0.5) + 12_582_912.0).to_bits() as i32;
        let ny = ((ext[DEC[az]] * g.step_r[1] + 0.5) + 12_582_912.0).to_bits() as i32;
        let cells = nx
            .wrapping_sub(0x4B40_0000)
            .wrapping_mul(ny.wrapping_sub(0x4B40_0000))
            .wrapping_mul(4);
        if cells >= ent.parts.len() as i32 {
            fl &= !0x400;
        } else {
            fl = (fl & !0x10) | 0x400;
        }
        w.ent_mut(e).flags = fl;
    }
    if w.ent(e).flags & 0x400 != 0 {
        if w.ent(e).grid_cells[0] == -3 {
            let ent = w.ent_mut(e);
            ent.grid_cells = [-2, -2, -2, -2];
            let pos = ent.pos;
            if (pos.x * pos.x + pos.y * pos.y) + pos.z * pos.z > 0.0 {
                w.reposition_entity(e, 1, None);
            }
        }
        if w.ent(e).flags & 0x400 != 0 {
            return 1;
        }
    }
    if w.ent(e).grid_cells[0] != -3 {
        w.grid_detach(e);
        w.ent_mut(e).grid_cells = [-3, -3, -3, -3];
    }
    1
}

/// `CRigidEntity::SetParams` post-processing of id 15: `flags` or `flagsOR` with 0x100 latch
/// `flags2 |= 0x10000000`; clearing 0x10000 on an idle (`maxTimeIdle < timeIdle`), submerged,
/// sleeping body wakes it (`Awake(1, 0)`).
fn rigid_post_flags(w: &mut World, e: EntIdx, p: &ParamsFlags, flags_before: u32) {
    if (p.flags != UI && p.flags & 0x100 != 0) || (p.flags_or != UI && p.flags_or & 0x100 != 0) {
        w.ent_mut(e).rigid_mut().expect("rigid").flags2 |= 0x1000_0000;
    }
    let ent = w.ent(e);
    let r = ent.rigid().expect("rigid");
    if flags_before & 0x1_0000 != 0
        && ent.flags & 0x1_0000 == 0
        && ent.max_time_idle < ent.time_idle
        && r.submerged_fraction > 0.0
        && !r.awake()
    {
        crate::entities::awake(w, e, 1, 0);
    }
}

/// Id 20 ground plane (base): `iPlane >= 0` grows the plane array to `iPlane + 1`
/// (zero-filled), `iPlane < 0` (unused included) frees it; then the normal and the origin are
/// stored when set. With `iPlane < 0` and a field set the binary writes through the freed
/// (null) array — an access violation; the port skips those writes.
pub fn set_params_ground_plane(w: &mut World, e: EntIdx, p: &ParamsGroundPlane) -> i32 {
    let ent = w.ent_mut(e);
    let ip = p.i_plane;
    if ip >= 0 {
        if ip as usize >= ent.ground_planes.len() {
            ent.ground_planes
                .resize(ip as usize + 1, (Vec3::ZERO, Vec3::ZERO));
        }
    } else {
        ent.ground_planes.clear();
        return 1;
    }
    let pl = &mut ent.ground_planes[ip as usize];
    if !is_unused(p.n.x) {
        pl.1 = p.n;
    }
    if !is_unused(p.origin.x) {
        pl.0 = p.origin;
    }
    1
}

/// Id 23 timeout (base): `timeIdle` (`+252`), `maxTimeIdle` (`+256`) when set.
pub fn set_params_timeout(w: &mut World, e: EntIdx, p: &ParamsTimeout) -> i32 {
    let ent = w.ent_mut(e);
    if !is_unused(p.time_idle) {
        ent.time_idle = p.time_idle;
    }
    if !is_unused(p.max_time_idle) {
        ent.max_time_idle = p.max_time_idle;
    }
    1
}

/// Id 10 simulation params (`CRigidEntity::SetParams` 0x350C4CC0).
///
/// Gravity (also copied to the free-fall gravity unless that is given too), angular
/// acceleration, maxTimeStep, minEnergy, damping, free-fall gravity / damping, maxRotVel.
/// `density >= 0`: every part's mass becomes `scale³·V·density`. `mass >= 0` (with parts): a
/// massless body distributes it proportionally to `scale³·V`, otherwise the part masses are
/// scaled by `mass / M`. Either recomputes the body. Then `iSimClass` (refused — only a log —
/// for entities at simclass >= 7; sets the awake byte to `simclass > 1` and re-lists),
/// `maxLoggedCollisions` (re-allocates the log, resets its counter, `+1100 = 1e10`),
/// `disablePreCG` (`flags2` bit 27) and `maxFriction`. Returns 1.
pub fn set_params_simulation(w: &mut World, e: EntIdx, p: &ParamsSimulation) -> i32 {
    let mut recompute = false;
    {
        let r = w.ent_mut(e).rigid_mut().expect("rigid");
        if !is_unused(p.gravity.x) {
            r.gravity = p.gravity;
            if is_unused(p.gravity_freefall.x) {
                r.gravity_freefall = p.gravity;
            }
        }
        if !is_unused(p.ang_accel.x) {
            r.ang_accel = p.ang_accel;
        }
        if !is_unused(p.max_time_step) {
            r.max_time_step = p.max_time_step;
        }
        if !is_unused(p.min_energy) {
            r.min_energy = p.min_energy;
        }
        if !is_unused(p.damping) {
            r.damping = p.damping;
        }
        if !is_unused(p.gravity_freefall.x) {
            r.gravity_freefall = p.gravity_freefall;
        }
        if !is_unused(p.damping_freefall) {
            r.damping_freefall = p.damping_freefall;
        }
        if !is_unused(p.max_rot_vel) {
            r.max_w = p.max_rot_vel;
        }
    }
    let ent = w.ent_mut(e);
    if !is_unused(p.density) && p.density >= 0.0 && !ent.parts.is_empty() {
        for part in &mut ent.parts {
            let s = part.scale;
            part.mass = (((s * s) * s) * part.pgeom.volume) * p.density;
        }
        recompute = true;
    }
    if !(is_unused(p.mass) || p.mass < 0.0 || ent.parts.is_empty()) {
        let body_m = w.bodies.get(w.ent(e).rigid().expect("rigid").body).m;
        let ent = w.ent_mut(e);
        if body_m == 0.0 {
            let mut sum = 0.0f32;
            for part in &ent.parts {
                let s = part.scale;
                sum += ((s * s) * s) * part.pgeom.volume;
            }
            if sum > 0.0 {
                let k = p.mass / sum;
                for part in &mut ent.parts {
                    let s = part.scale;
                    part.mass = (((s * s) * s) * part.pgeom.volume) * k;
                }
            }
        } else {
            let k = p.mass / body_m;
            for part in &mut ent.parts {
                part.mass *= k;
            }
        }
        recompute = true;
    }
    if recompute {
        mass::recompute_mass(w, e);
    }
    if p.sim_class != UI {
        let id_bits = w.ent(e).id_bits;
        if ((id_bits & 0xFF00_0000) as i32) < 0x700_0000 {
            let nb = (id_bits & 0x00FF_FFFF) | ((p.sim_class as u32) << 24);
            let ent = w.ent_mut(e);
            ent.id_bits = nb;
            let awake = 1 - ((nb as i32) >> 24) < 0;
            ent.rigid_mut().expect("rigid").set_awake(awake);
            w.reposition_entity(e, 2, None);
        }
        // else: "refused" through the system log (world vt+160); nothing changes
    }
    let r = w.ent_mut(e).rigid_mut().expect("rigid");
    if p.max_logged_collisions != UI
        && p.max_logged_collisions as u32 != (r.awake_bits >> 22) & 0x1F
    {
        r.coll_log = Vec::new();
        let b =
            r.awake_bits ^ ((r.awake_bits ^ ((p.max_logged_collisions as u32) << 22)) & 0x7C0_0000);
        r.awake_bits = b;
        if b & 0x7C0_0000 != 0 {
            r.coll_log = vec![usize::MAX; ((b >> 22) & 0x1F) as usize];
        }
        r.awake_bits &= 0x07C1_FFFF;
        r.f1100 = f32::from_bits(0x5015_02F9); // 1e10
    }
    if p.disable_pre_cg != UI {
        r.flags2 &= !0x800_0000;
        r.flags2 |= ((p.disable_pre_cg & 1) as u32) << 27;
    }
    if !is_unused(p.max_friction) {
        r.max_friction = p.max_friction;
    }
    1
}

#[cfg(test)]
mod tests {
    #[test]
    fn rounding_matches_the_magic_constant() {
        // int(x) through `(x + 0.5) + 12582912` and the bit pattern minus 0x4B400000
        let f = |x: f32| (((x + 0.5) + 12_582_912.0).to_bits() as i32).wrapping_sub(0x4B40_0000);
        assert_eq!(f(2.4), 3);
        assert_eq!(f(0.0), 0);
        assert_eq!(f(7.0), 8);
    }
}
