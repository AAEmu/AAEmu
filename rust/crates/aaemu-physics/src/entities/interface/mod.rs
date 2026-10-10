//! **The entity parameter interface** — virtual slots 7 `SetParams`, 8 `GetParams`,
//! 9 `GetStatus` and 10 `Action` of `CPhysicalEntity` (static, vtable 0x351EFE38) and
//! `CRigidEntity` (vtable 0x351F14C8), dispatched on [`EntityKind`] (`entities.md` §1–§3).
//!
//! | slot | base | rigid |
//! |---|---|---|
//! | 7 SetParams | 0x3506EF20 | 0x350C4CC0 |
//! | 8 GetParams | 0x35054E60 | 0x350AABD0 |
//! | 9 GetStatus | 0x350724C0 | 0x350B1B80 |
//! | 10 Action | 0x350583C0 | 0x350C5D30 |
//! | lock-or-queue | 0x35023200 (params), 0x35023650 (action) | — |
//! | 76 queued-action result | 0x35052C10 | 0x350AAD50 |
//!
//! Each interface has one raw entry point taking the type id at `+0` of an x64 byte image
//! (exactly what the oracle passes; see [`crate::params`] for the layouts) and returning the
//! slot's `int` result: [`set_params`], [`get_params`], [`get_status`], [`action()`].
//! GetParams / GetStatus write back **only the bytes the binary writes**; the other bytes of
//! the buffer keep the caller's values. The typed wrappers [`set()`], [`get()`], [`status()`] and
//! [`act`] go through the same byte images.
//!
//! **Pointers.** The port has no object addresses. Pointer fields are honoured where their
//! value is "no object" (null and/or `-1`, exactly as each handler tests them); a call that
//! would dereference or store a real pointer is not ported: after the lock-or-queue step
//! (which is ported, deferral included) it returns 0 without further side effects, and a
//! replayed record of it is dropped (listed per id in the [`set`](mod@set),
//! [`get`](mod@get), [`status`](mod@status) and [`action`](mod@action) module docs).
//! Out-pointers (`Matrix34*` / `Matrix33*` destinations of GetParams(part) and
//! GetStatus(pos)) are not written through; the typed functions return those matrices.
//! Geometry pointers that GetParams / GetStatus report are written as the address of the
//! port's geometry object (`Arc::as_ptr`), which is stable for the geometry's lifetime but
//! obviously not the binary's value.
//!
//! **Deferred calls** (`sub_35023200` / `sub_35023650`): a call with `bThreadSafe <= 0` is
//! queued into the world command queue ([`crate::world::queue`]) when the entity already has
//! queued commands (`processed >= 0x20000`) or `bThreadSafe < 0` (also forced for a deleted
//! placeholder: simclass -1 with deletion state 3). The binary additionally queues while
//! another thread holds the world locks (`+22468`, `+22532`, `+22492`); the single-threaded
//! port never holds them outside `TimeStep`, so that condition is never true here. Entities
//! with simclass >= 7 ignore SetParams (except ids 11 and 16) and Action (returning 1, or
//! the slot-76 value for actions). Queued records replay at the next `TimeStep` with
//! `bThreadSafe = 1`.

pub mod action;
pub mod get;
pub mod set;
pub mod status;

use crate::entities::base::EntityKind;
use crate::math::{Quat, Vec3};
use crate::params::PeStruct;
use crate::world::queue::Command;
use crate::world::{EntIdx, World};

pub use action::action;
pub use get::get_params;
pub use set::set_params;
pub use status::get_status;

// ---------------------------------------------------------------------------------------------
// typed wrappers

/// Typed `SetParams`: the byte image of `p` through [`set_params`] (`bThreadSafe = 1`).
pub fn set<T: PeStruct>(w: &mut World, e: EntIdx, p: &T) -> i32 {
    debug_assert_eq!(T::KIND, crate::params::PeKind::Params);
    let mut b = p.to_bytes();
    set_params(w, e, &mut b, 1)
}

/// Typed `GetParams`: `p` is the request (its in-fields, e.g. the part id) and receives the
/// answer (fields the binary does not write keep their values).
pub fn get<T: PeStruct>(w: &mut World, e: EntIdx, p: &mut T) -> i32 {
    debug_assert_eq!(T::KIND, crate::params::PeKind::Params);
    let mut b = p.to_bytes();
    let r = get_params(w, e, &mut b);
    *p = T::read(&b);
    r
}

/// Typed `GetStatus` (see [`get()`]).
pub fn status<T: PeStruct>(w: &mut World, e: EntIdx, s: &mut T) -> i32 {
    debug_assert_eq!(T::KIND, crate::params::PeKind::Status);
    let mut b = s.to_bytes();
    let r = get_status(w, e, &mut b);
    *s = T::read(&b);
    r
}

/// Typed `Action` (`bThreadSafe = 1`).
pub fn act<T: PeStruct>(w: &mut World, e: EntIdx, a: &T) -> i32 {
    debug_assert_eq!(T::KIND, crate::params::PeKind::Action);
    let mut b = a.to_bytes();
    action(w, e, &mut b, 1)
}

// ---------------------------------------------------------------------------------------------
// raw byte helpers (little endian, x64 offsets)

#[inline]
pub(crate) fn rd_i32(b: &[u8], o: usize) -> i32 {
    i32::from_le_bytes(b[o..o + 4].try_into().expect("4 bytes"))
}
#[inline]
pub(crate) fn rd_u32(b: &[u8], o: usize) -> u32 {
    rd_i32(b, o) as u32
}
#[inline]
pub(crate) fn rd_u64(b: &[u8], o: usize) -> u64 {
    u64::from_le_bytes(b[o..o + 8].try_into().expect("8 bytes"))
}
#[inline]
pub(crate) fn wr_i32(b: &mut [u8], o: usize, v: i32) {
    b[o..o + 4].copy_from_slice(&v.to_le_bytes());
}
#[inline]
pub(crate) fn wr_u32(b: &mut [u8], o: usize, v: u32) {
    wr_i32(b, o, v as i32);
}
#[inline]
pub(crate) fn wr_f32(b: &mut [u8], o: usize, v: f32) {
    wr_u32(b, o, v.to_bits());
}
#[inline]
pub(crate) fn wr_u64(b: &mut [u8], o: usize, v: u64) {
    b[o..o + 8].copy_from_slice(&v.to_le_bytes());
}
#[inline]
pub(crate) fn wr_v3(b: &mut [u8], o: usize, v: Vec3) {
    wr_f32(b, o, v.x);
    wr_f32(b, o + 4, v.y);
    wr_f32(b, o + 8, v.z);
}
#[inline]
pub(crate) fn wr_q(b: &mut [u8], o: usize, q: Quat) {
    wr_f32(b, o, q.x);
    wr_f32(b, o + 4, q.y);
    wr_f32(b, o + 8, q.z);
    wr_f32(b, o + 12, q.w);
}

/// `true` when `buf` holds at least the struct `T` (the raw entries return 0 otherwise).
#[inline]
pub(crate) fn fits<T: PeStruct>(buf: &[u8]) -> bool {
    buf.len() >= T::SIZE
}

// ---------------------------------------------------------------------------------------------
// lock-or-queue

/// What the lock-or-queue helper decided.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) enum Gate {
    /// Execute the call now.
    Proceed,
    /// The call is not executed (simclass >= 7); `queued` = it went into the command queue
    /// (index of the record), else it was dropped.
    Skip { queued: Option<usize> },
}

/// `bThreadSafe` as the helpers adjust it: a deleted placeholder (simclass -1, deletion
/// state 3) turns `0` into `-1` (`((a5 - 1) >> 31) | a5`).
fn adjusted_thread_safe(w: &World, e: EntIdx, thread_safe: i32) -> i32 {
    let ent = w.ent(e);
    if ent.sim_class() == -1 && ent.deleted == 3 {
        (thread_safe.wrapping_sub(1) >> 31) | thread_safe
    } else {
        thread_safe
    }
}

/// The queue decision shared by both helpers (`v9 <= 0` branch): queue unless the world can
/// be locked right away (see the module docs for what the port models).
fn must_queue(w: &World, e: EntIdx, v9: i32) -> bool {
    v9 <= 0 && !(v9 >= 0 && w.ent(e).processed < 0x2_0000 && !w.step_lock)
}

/// Copy the struct into the queue: `processed += 0x20000`, record `{op, size, ent, struct}`.
fn enqueue(w: &mut World, e: EntIdx, buf: &[u8], size: usize, is_action: bool) -> usize {
    let ent = w.ent_mut(e);
    ent.processed = ent.processed.wrapping_add(0x2_0000);
    let n = size.min(buf.len());
    let rec = buf[..n].to_vec();
    w.queue.push(if is_action {
        Command::Action { ent: e, buf: rec }
    } else {
        Command::SetParams { ent: e, buf: rec }
    });
    w.queue.cmds.len() - 1
}

/// `sub_35023200` (SetParams lock-or-queue), including its tail: a `pe_params_pos` with a
/// position or a `Matrix34*` and without `bRecalcBounds & 0x10` marks the entity
/// (`processed |= 0x10000`, the "moved since the last post-step event" bit), on the execute
/// and the queue paths.
pub(crate) fn params_gate(w: &mut World, e: EntIdx, buf: &[u8], thread_safe: i32) -> Gate {
    let ty = rd_i32(buf, 0);
    let v9 = adjusted_thread_safe(w, e, thread_safe);
    let sc = w.ent(e).sim_class();
    if sc >= 7 && v9 >= 0 && ty != 11 && ty != 16 {
        return Gate::Skip { queued: None };
    }
    let mut gate = Gate::Proceed;
    if must_queue(w, e, v9) {
        let size = usize::try_from(ty)
            .ok()
            .and_then(|i| crate::ids::PARAMS_SIZES.get(i))
            .map_or(buf.len(), |&s| s as usize);
        gate = Gate::Skip {
            queued: Some(enqueue(w, e, buf, size, false)),
        };
    }
    pos_tail(w, e, buf);
    gate
}

/// The tail of `sub_35023200` for `pe_params_pos` (see [`params_gate`]).
pub(crate) fn pos_tail(w: &mut World, e: EntIdx, buf: &[u8]) {
    if rd_i32(buf, 0) != 0 || buf.len() < 64 {
        return;
    }
    if rd_u32(buf, 4) & 0xFFA0_0000 == 0xFFA0_0000 {
        let m = rd_u64(buf, 40);
        if m == u64::MAX || m == 0 {
            return;
        }
    }
    if rd_u32(buf, 60) & 0x10 != 0 {
        return;
    }
    let ent = w.ent_mut(e);
    if ent.processed & 0x1_0000 == 0 {
        ent.processed = ent.processed.wrapping_add(0x1_0000);
    }
}

/// `sub_35023650` (Action lock-or-queue). No tail.
pub(crate) fn action_gate(w: &mut World, e: EntIdx, buf: &[u8], thread_safe: i32) -> Gate {
    let ty = rd_i32(buf, 0);
    let v10 = adjusted_thread_safe(w, e, thread_safe);
    if w.ent(e).sim_class() >= 7 && v10 >= 0 {
        return Gate::Skip { queued: None };
    }
    if must_queue(w, e, v10) {
        let size = usize::try_from(ty)
            .ok()
            .and_then(|i| crate::ids::ACTION_SIZES.get(i))
            .map_or(buf.len(), |&s| s as usize);
        return Gate::Skip {
            queued: Some(enqueue(w, e, buf, size, true)),
        };
    }
    Gate::Proceed
}

/// Is `e` a `CRigidEntity`?
#[inline]
pub(crate) fn is_rigid(w: &World, e: EntIdx) -> bool {
    matches!(w.ent(e).kind, EntityKind::Rigid(_))
}

/// Is `e` a physical entity (not an area)?
#[inline]
pub(crate) fn is_entity(w: &World, e: EntIdx) -> bool {
    !matches!(w.ent(e).kind, EntityKind::Area(_))
}

/// The calling thread's per-caller slot for GetEntitiesInBox (TLS value, 4 = external).
#[inline]
pub(crate) fn caller(w: &World) -> usize {
    w.tls_thread.clamp(0, 4) as usize
}
