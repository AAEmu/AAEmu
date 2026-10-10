//! Slot 10 `Action`: `CPhysicalEntity::Action` 0x350583C0 and `CRigidEntity::Action`
//! 0x350C5D30, with slot 76 (queued / ignored actions: base 0x35052C10, rigid 0x350AAD50).
//!
//! | id | static (base) | rigid |
//! |---|---|---|
//! | 2 impulse | 1 when the part selection is valid, else 0 (the breakable-structure and lattice work needs a structure, never present) | [`crate::entities::rigid::action::impulse`], returns 1 |
//! | 4 reset | 0 | [`crate::entities::rigid::action::reset`] (+ `update_constraint` remove-all) |
//! | 6 update_constraint | 0 | [`update_constraint`] (no constraints exist in the port) |
//! | 8 awake | `Awake(bAwake, 1)` (a no-op for the base class), 1; 0 for simclass outside 0..6 | `Awake(bAwake, 1)`, `minAwakeTime`, 1 |
//! | 10 set_velocity | 0 | [`set_velocity`] |
//! | 13 reset part matrix | [`reset_part_matrix`] (also for rigid) | |
//!
//! Not ported (when executed, the raw entry returns 0 without side effects; the deferral of
//! such calls is ported, and their replay from the queue drops them): 5 add_constraint and 7 register
//! collision event and 18 net sync (rigid), 9 remove_all_parts (needs RemoveGeometry), 15
//! auto part detachment and 16 move_parts (breakable structures / AddGeometry into another
//! entity), id 13 with a part index outside the part array (the binary reads outside it).
//! The rigid impulse validator `sub_35021B10` only rejects while the world variable `+312`
//! is set (never in the port).

use super::{action_gate, fits, is_entity, is_rigid, rd_i32, Gate};
use crate::entities::rigid::velocity::{cap_velocities, l_from_w};
use crate::math::Vec3;
use crate::params::{
    ActionAwake, ActionImpulse, ActionReset, ActionSetVelocity, ActionUpdateConstraint, ParamsPart,
    ParamsPos, PeStruct,
};
use crate::sentinel::{is_unused, UNUSED_I32 as UI};
use crate::world::queue::Command;
use crate::world::{EntIdx, World};

/// Raw `Action(action, bThreadSafe)`: `buf` starts with the action type id. Dispatches on the
/// entity class.
pub fn action(w: &mut World, e: EntIdx, buf: &mut [u8], thread_safe: i32) -> i32 {
    if buf.len() < 4 || !is_entity(w, e) {
        return 0;
    }
    match &w.ent(e).kind {
        crate::entities::base::EntityKind::Living(_) => {
            return crate::entities::living::action(w, e, buf, thread_safe)
        }
        crate::entities::base::EntityKind::Particle(_) => {
            return crate::entities::particle::action(w, e, buf, thread_safe)
        }
        crate::entities::base::EntityKind::Rigid(r) if r.wheeled.is_some() => {
            return crate::entities::wheeled::action(w, e, buf, thread_safe)
        }
        _ => {}
    }
    class_action(w, e, buf, thread_safe)
}

/// `CRigidEntity::Action` 0x350C5D30 for rigid entities, `CPhysicalEntity::Action`
/// 0x350583C0 otherwise (what the derived classes call after their own ids).
pub fn class_action(w: &mut World, e: EntIdx, buf: &mut [u8], thread_safe: i32) -> i32 {
    if buf.len() < 4 || !is_entity(w, e) {
        return 0;
    }
    if let Gate::Skip { queued } = action_gate(w, e, buf, thread_safe) {
        return slot76(w, e, buf, queued);
    }
    if !supported(w, e, buf) {
        return 0;
    }
    if is_rigid(w, e) {
        rigid_action(w, e, buf)
    } else {
        base_action(w, e, buf)
    }
}

fn supported(w: &World, e: EntIdx, buf: &[u8]) -> bool {
    let rigid = is_rigid(w, e);
    let ty = rd_i32(buf, 0);
    match ty {
        2 => fits::<ActionImpulse>(buf),
        4 => fits::<ActionReset>(buf),
        5 | 7 | 18 => !rigid,
        6 => fits::<ActionUpdateConstraint>(buf),
        8 => fits::<ActionAwake>(buf),
        9 | 15 | 16 => false,
        10 => fits::<ActionSetVelocity>(buf),
        13 => buf.len() >= 12 && reset_part_index(w, e, rd_i32(buf, 4), rd_i32(buf, 8)).is_some(),
        _ => true,
    }
}

/// Slot 76 for a call that was queued (`Some(record)`) or ignored (simclass >= 7): base —
/// id 9 marks every part `0x10000000` (removed) and resets the next part id, id 16 AddRefs
/// its target (no objects to count in the port); rigid — id 5 returns the constraint id
/// (auto-assigned into the queued copy when unused; 0 without a record). Returns 1 otherwise.
pub(crate) fn slot76(w: &mut World, e: EntIdx, buf: &[u8], queued: Option<usize>) -> i32 {
    let ty = rd_i32(buf, 0);
    if is_rigid(w, e) && ty == 5 {
        let Some(i) = queued else {
            return 0;
        };
        let Some(Command::Action { buf: rec, .. }) = w.queue.cmds.get(i) else {
            return 0;
        };
        if rec.len() < 8 {
            return 0;
        }
        if rd_i32(rec, 4) == UI {
            let r = w.ent_mut(e).rigid_mut().expect("rigid");
            let id = r.next_constraint_id;
            r.next_constraint_id += 1;
            if let Some(Command::Action { buf: rec, .. }) = w.queue.cmds.get_mut(i) {
                super::wr_i32(rec, 4, id);
            }
        }
        return match w.queue.cmds.get(i) {
            Some(Command::Action { buf: rec, .. }) => rd_i32(rec, 4),
            _ => 0,
        };
    }
    if ty == 9 {
        let ent = w.ent_mut(e);
        for p in &mut ent.parts {
            p.flags |= 0x1000_0000;
        }
        ent.next_part_id = 0;
    }
    1
}

/// `CPhysicalEntity::Action` after its lock-or-queue.
fn base_action(w: &mut World, e: EntIdx, buf: &[u8]) -> i32 {
    let ty = rd_i32(buf, 0);
    match ty {
        2 => {
            let a = ActionImpulse::read(buf);
            let n = w.ent(e).parts.len() as i32;
            let mut ip = a.ipart;
            if ip == UI {
                if a.part_id == UI {
                    return 1;
                }
                ip = w
                    .ent(e)
                    .parts
                    .iter()
                    .position(|q| q.id == a.part_id)
                    .map_or(n.max(0), |i| i as i32);
            }
            // in range: (point → part lookup, structure / lattice work: none) → 1
            i32::from((ip as u32) < n as u32)
        }
        8 if (w.ent(e).sim_class() as u32) <= 6 => {
            let a = ActionAwake::read(buf);
            crate::entities::awake(w, e, a.awake, 1);
            1
        }
        13 => reset_part_matrix(w, e, rd_i32(buf, 4), rd_i32(buf, 8)),
        _ => 0,
    }
}

/// `CRigidEntity::Action` after its lock-or-queue; unhandled ids go to the base.
fn rigid_action(w: &mut World, e: EntIdx, buf: &[u8]) -> i32 {
    match rd_i32(buf, 0) {
        2 => crate::entities::rigid::action::impulse(w, e, &ActionImpulse::read(buf)),
        4 => crate::entities::rigid::action::reset(w, e, &ActionReset::read(buf)),
        6 => update_constraint(w, e, &ActionUpdateConstraint::read(buf)),
        8 if (w.ent(e).sim_class() as u32) <= 6 => {
            let a = ActionAwake::read(buf);
            crate::entities::awake(w, e, a.awake, 1);
            if !is_unused(a.min_awake_time) {
                w.ent_mut(e).rigid_mut().expect("rigid").min_awake_time = a.min_awake_time;
            }
            1
        }
        10 => set_velocity(w, e, &ActionSetVelocity::read(buf)),
        _ => base_action(w, e, buf),
    }
}

/// Rigid id 6 `update_constraint` with no constraint slots: no mask bit matches, so a given
/// id returns 0 and "all" returns 1; `bRemove` still runs slot 80 (0x350AA050), which clears
/// the part flag `0x400000` of every part without a lattice (no breakable structure).
pub fn update_constraint(w: &mut World, e: EntIdx, a: &ActionUpdateConstraint) -> i32 {
    if a.remove != 0 {
        slot80(w, e);
    }
    i32::from(a.id == UI)
}

/// Rigid slot 80 (0x350AA050) without constraints: parts without a lattice (all, in the
/// port) and without a breakable structure lose `0x400000` (skipped for GetType 3,
/// wheeled, not ported).
fn slot80(w: &mut World, e: EntIdx) {
    let ent = w.ent_mut(e);
    if ent.etype != 3 && !ent.has_structure {
        for p in &mut ent.parts {
            if !p.has_lattice {
                p.flags &= !0x40_0000;
            }
        }
    }
}

/// Rigid id 10 `set_velocity`: `v` → `P = M·v`; `w` → `L = q·(Ibody ∘ (q⁻¹·w))`, and with
/// `bRotationAroundPivot` also `v += w × (com − pos)` (on a zero `v` when `v` is unused) and
/// `P = M·v`; then CapVelocities. A non-zero result wakes the body
/// (`Awake(1, 0)` when asleep) and clears the idle timer; an all-zero one puts a massless
/// body to sleep (`Awake(0, 0)`). Part selectors are ignored. Returns 1.
pub fn set_velocity(w: &mut World, e: EntIdx, a: &ActionSetVelocity) -> i32 {
    let body_id = w.ent(e).rigid().expect("rigid").body;
    let epos = w.ent(e).pos;
    {
        let b = w.bodies.get_mut(body_id);
        if !is_unused(a.v.x) {
            b.v = a.v;
            b.p = Vec3::new(b.v.x * b.m, b.v.y * b.m, b.v.z * b.m);
        }
        if !is_unused(a.w.x) {
            b.w = a.w;
            let wn = b.w;
            b.l = l_from_w(b, wn);
            if a.rotation_around_pivot != 0 {
                if is_unused(a.v.x) {
                    b.v = Vec3::ZERO;
                }
                let d = Vec3::new(b.pos.x - epos.x, b.pos.y - epos.y, b.pos.z - epos.z);
                let wv = b.w;
                b.v = Vec3::new(
                    (d.z * wv.y - d.y * wv.z) + b.v.x,
                    (d.x * wv.z - d.z * wv.x) + b.v.y,
                    (d.y * wv.x - d.x * wv.y) + b.v.z,
                );
                b.p = Vec3::new(b.v.x * b.m, b.v.y * b.m, b.v.z * b.m);
            }
        }
    }
    cap_velocities(w, e);
    let b = w.bodies.get(body_id);
    let s = ((b.w.x * b.w.x + b.w.y * b.w.y) + b.w.z * b.w.z)
        + ((b.v.x * b.v.x + b.v.y * b.v.y) + b.v.z * b.v.z);
    if s.partial_cmp(&0.0) != Some(std::cmp::Ordering::Greater) {
        // ucomiss / jne: a NaN Minv counts as zero
        if !matches!(
            b.minv.partial_cmp(&0.0),
            Some(std::cmp::Ordering::Less | std::cmp::Ordering::Greater)
        ) {
            crate::entities::awake(w, e, 0, 0);
        }
    } else {
        if !w.ent(e).rigid().expect("rigid").awake() {
            crate::entities::awake(w, e, 1, 0);
        }
        w.ent_mut(e).time_idle = 0.0;
    }
    1
}

/// The part index of action 13: by `partid` (searched from the last part down to part 1;
/// part 0 when not found), else `ipart` (unused = 0). `None` when outside the part array.
fn reset_part_index(w: &World, e: EntIdx, ipart: i32, part_id: i32) -> Option<usize> {
    let ent = w.ent(e);
    let n = ent.parts.len() as i32;
    let i = if part_id == UI {
        if ipart != UI {
            ipart
        } else {
            0
        }
    } else {
        let mut i = n - 1;
        while i > 0 {
            if ent.parts[i as usize].id == part_id {
                break;
            }
            i -= 1;
        }
        i
    };
    (0..n).contains(&i).then_some(i as usize)
}

/// Base id 13 "reset part matrix": the entity is moved to the part's world pose
/// (SetParams(pos) with `bRecalcBounds = 2`, `bThreadSafe = 1`, through the entity's own
/// slot 7), then the part pose is reset to identity (SetParams(part) `ipart`, pos 0,
/// q identity, `bRecalcBBox = 1`). Returns 1.
pub fn reset_part_matrix(w: &mut World, e: EntIdx, ipart: i32, part_id: i32) -> i32 {
    let Some(i) = reset_part_index(w, e, ipart, part_id) else {
        return 0;
    };
    let ent = w.ent(e);
    let part = &ent.parts[i];
    let r = ent.q.rotate(part.pos);
    let pp = ParamsPos {
        pos: Vec3::new(r.x + ent.pos.x, ent.pos.y + r.y, ent.pos.z + r.z),
        q: ent.q.product(part.q),
        recalc_bounds: 2,
        ..ParamsPos::default()
    };
    super::set(w, e, &pp);
    let pq = ParamsPart {
        ipart: i as i32,
        pos: Vec3::ZERO,
        q: crate::math::Quat::IDENTITY,
        ..ParamsPart::default()
    };
    super::set(w, e, &pq);
    1
}
