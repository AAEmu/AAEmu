//! Physical entities.
//!
//! * [`base`] — **P4a**: `CPhysicalEntity` (parts, SetParams/GetParams/GetStatus/Action base
//!   ids, AddGeometry, bbox).
//! * [`rigid`] — **P4a**: `CRigidEntity` (body integration, Step/RegisterContacts/Update,
//!   ApplyBuoyancy, air resistance).
//! * [`living`], [`wheeled`], [`particle`] — **P5**.
//!
//! Articulated, rope and soft entities are deferred (P6).
//!
//! The functions in this module are the virtual slots shared by every class, dispatched on
//! [`base::EntityKind`] (the vtable).

pub mod base;
pub mod interface;
pub mod living;
pub mod particle;
pub mod rigid;
pub mod wheeled;

use crate::world::{EntIdx, World};
use base::EntityKind;

/// An entity handle (the engine's 23-bit id, `+0x44` low bits). The world (P3) owns the
/// entity store; everything else refers to entities by id.
#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct EntityId(pub i32);

/// Slot 69 `GetMassInv`: rigid `0x35020A10` (`Minv`, 0 when flag 2 is set); base 0.
#[must_use]
pub fn mass_inv(w: &World, e: EntIdx) -> f32 {
    let ent = w.ent(e);
    match &ent.kind {
        EntityKind::Rigid(r) => {
            if ent.flags & 2 != 0 {
                0.0
            } else {
                w.bodies.get(r.body).minv
            }
        }
        _ => 0.0,
    }
}

/// Slot 55 `Awake(bAwake, iSource)`: rigid `0x350AB170`; base: `return 0`.
pub fn awake(w: &mut World, e: EntIdx, b_awake: i32, i_source: i32) -> i32 {
    match w.ent(e).kind {
        EntityKind::Rigid(_) => rigid::awake::awake(w, e, b_awake, i_source),
        _ => 0,
    }
}

/// Slot 56 `IsAwake(ipart)`: rigid 0x350CCD30 (`+508` byte); base 0.
#[must_use]
pub fn is_awake(w: &World, e: EntIdx) -> i32 {
    match &w.ent(e).kind {
        EntityKind::Rigid(r) => (r.awake_bits & 0xFF) as i32,
        _ => 0,
    }
}

/// Slot 47 `AddCollider(pent)`: base `0x35053890` (insert sorted by `GetMassInv`
/// ascending; nothing for simclass 0), rigid `0x350ACF10` (also the per-collider contact
/// and constraint arrays; clears the saved collider count in `+504` bits 1–8). Returns the
/// collider index.
pub fn add_collider(w: &mut World, e: EntIdx, c: EntIdx) -> i32 {
    let i = base_add_collider(w, e, c);
    if let EntityKind::Rigid(_) = w.ent(e).kind {
        let n = w.ent(e).colliders.len();
        let r = w.ent_mut(e).rigid_mut().expect("rigid");
        if r.collider_contacts.len() < n {
            r.collider_contacts
                .insert(i as usize, crate::solver::ContactId::NONE);
            r.collider_masks.insert(i as usize, 0);
        }
        r.flags2 &= 0xFFFF_FE01;
    }
    i
}

fn base_add_collider(w: &mut World, e: EntIdx, c: EntIdx) -> i32 {
    if w.ent(e).id_bits & 0xFF00_0000 == 0 {
        return 1;
    }
    if let Some(i) = w.ent(e).colliders.iter().position(|&x| x == c) {
        return i as i32;
    }
    let mc = mass_inv(w, c);
    let cols = w.ent(e).colliders.clone();
    let mut i = 0usize;
    while i < cols.len() {
        if mc <= mass_inv(w, cols[i]) {
            break;
        }
        i += 1;
    }
    w.ent_mut(e).colliders.insert(i, c);
    i as i32
}

/// Slot 48 `RemoveCollider(pent, bAlways)`: base `0x35052980`; rigid `0x350AADB0` (unless
/// `bAlways`, only when no contact or constraint refers to the collider; frees that
/// collider's contacts). Returns the former index or -1.
pub fn remove_collider(w: &mut World, e: EntIdx, c: EntIdx, always: bool) -> i32 {
    if let EntityKind::Rigid(_) = w.ent(e).kind {
        if !always {
            let ent = w.ent(e);
            if let Some(i) = ent.colliders.iter().position(|&x| x == c) {
                let r = ent.rigid().expect("rigid");
                if !r.collider_contacts[i].is_none() || r.collider_masks[i] != 0 {
                    return i as i32;
                }
            }
        }
        let i = base_remove_collider(w, e, c);
        if i >= 0 {
            let first = w.ent(e).rigid().expect("rigid").collider_contacts[i as usize];
            if !first.is_none() {
                let mut ci = first;
                loop {
                    let next = w.contacts.get(ci).next;
                    rigid::contacts::remove_contact(w, e, ci, i, false);
                    w.contacts.free_push_front(ci);
                    if w.contacts.get(ci).flags & 0x8_0000 != 0 {
                        break;
                    }
                    ci = next;
                }
            }
            let r = w.ent_mut(e).rigid_mut().expect("rigid");
            r.collider_contacts.remove(i as usize);
            r.collider_masks.remove(i as usize);
        }
        return i;
    }
    base_remove_collider(w, e, c)
}

fn base_remove_collider(w: &mut World, e: EntIdx, c: EntIdx) -> i32 {
    let ent = w.ent_mut(e);
    if ent.id_bits & 0xFF00_0000 == 0 {
        return -1;
    }
    match ent.colliders.iter().position(|&x| x == c) {
        Some(i) => {
            ent.colliders.remove(i);
            i as i32
        }
        None => -1,
    }
}

/// Slot 53 `HasConstraintContactsWith(pent, flagsIgnore)`: rigid `0x350A9CE0`; base 0.
#[must_use]
pub fn has_constraint_contacts_with(
    w: &World,
    e: EntIdx,
    other: EntIdx,
    flags_ignore: i32,
) -> bool {
    let ent = w.ent(e);
    let EntityKind::Rigid(r) = &ent.kind else {
        return false;
    };
    let Some(i) = ent.colliders.iter().position(|&x| x == other) else {
        return false;
    };
    let m = r.collider_masks[i];
    if flags_ignore == 0 {
        return m != 0;
    }
    // constraint infos are not ported: no constraint slots exist
    false
}

/// Slot 50 `HasContactsWith(pent)`: rigid `0x350A9C20` (a contact run or a constraint mask
/// for that collider); base 0.
#[must_use]
pub fn has_contacts_with(w: &World, e: EntIdx, other: EntIdx) -> bool {
    let ent = w.ent(e);
    let EntityKind::Rigid(r) = &ent.kind else {
        return false;
    };
    match ent.colliders.iter().position(|&x| x == other) {
        Some(i) => !r.collider_contacts[i].is_none() || r.collider_masks[i] != 0,
        None => false,
    }
}

/// Slot 54, the detach/removal hook: base `0x350530B0` (remove this entity from the collider
/// lists of its colliders and wake them; wake the entities around the bbox for POD-referenced
/// or alerted entities), rigid `0x350A9DD0` (first wake the constraint buddies; flag 0x100000
/// while the external-contact stamp is negative).
pub fn detach_hook(w: &mut World, e: EntIdx, mode: i32) {
    let saved = w.ent(e).flags;
    if let EntityKind::Rigid(r) = &w.ent(e).kind {
        // constraint buddies: no constraints in the port
        if r.last_ext_contact_step < 0 && w.vars.last_dt > 0.0 {
            w.ent_mut(e).flags = saved | 0x10_0000;
        }
    }
    if (w.ent(e).id_bits & 0xFF00_0000) as i32 <= 0x300_0000 {
        w.ent_mut(e).flags |= 0x8000_0000;
        let cols = w.ent(e).colliders.clone();
        for c in cols {
            if c != e {
                remove_collider(w, c, e, true);
                awake(w, c, 1, 1);
            }
        }
        w.ent_mut(e).colliders.clear();
        let ent = w.ent(e);
        if ent.flags & 0x20 == 0
            && ((ent.pod_refs > 0 && ((ent.id_bits & 0xFF00_0000) as i32) < 0x300_0000)
                || ent.flags & 0x10_0000 != 0)
        {
            let v10 = w.vars.contact_gap * 4.0;
            let bb = ent.bbox;
            let v11 = (bb[1].x - bb[0].x) * 0.001 + v10;
            let v12 = (bb[1].z - bb[0].z) * 0.001 + v10;
            let v13 = (bb[1].y - bb[0].y) * 0.001 + v10;
            let v14 = if ent.flags & 0x10_0000 != 0 { 2.0 } else { 0.0 };
            let hi = crate::math::Vec3::new(
                0.0 + (v11 + bb[1].x),
                0.0 + (v13 + bb[1].y),
                (v12 + bb[1].z) + v14,
            );
            let lo = crate::math::Vec3::new(bb[0].x - v11, bb[0].y - v13, bb[0].z - v12);
            let caller = w.tls_thread.clamp(0, 4) as usize;
            let list = w.get_entities_in_box(lo, hi, 94, None, caller);
            for &o in list.iter().rev() {
                if o != e {
                    awake(w, o, 1, 1);
                }
            }
        }
    }
    let _ = mode;
    if let EntityKind::Rigid(_) = w.ent(e).kind {
        w.ent_mut(e).flags = saved;
    }
}

/// Action id 4 (reset): rigid `0x350C5D30` id-4 branch; other classes ignore it.
pub fn action_reset(w: &mut World, e: EntIdx, a: &crate::params::ActionReset) -> i32 {
    match w.ent(e).kind {
        EntityKind::Rigid(_) => rigid::action::reset(w, e, a),
        _ => 0,
    }
}
