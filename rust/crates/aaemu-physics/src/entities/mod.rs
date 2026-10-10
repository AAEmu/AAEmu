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

/// Slot 69 `GetMassInv`: rigid `0x35020A10` (`Minv`, 0 when flag 2 is set); living
/// `0x35037C50` (`+584`); base 0.
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
        EntityKind::Living(l) => l.mass_inv,
        _ => 0.0,
    }
}

/// Slot 55 `Awake(bAwake, iSource)`: rigid `0x350AB170` (wheeled vehicles inherit it), living
/// `0x35024F90`, particle `0x35039B60`; base: `return 0`.
pub fn awake(w: &mut World, e: EntIdx, b_awake: i32, i_source: i32) -> i32 {
    match w.ent(e).kind {
        EntityKind::Rigid(_) => rigid::awake::awake(w, e, b_awake, i_source),
        EntityKind::Living(_) => living::awake(w, e, b_awake, i_source),
        EntityKind::Particle(_) => particle::awake(w, e, b_awake, i_source),
        _ => 0,
    }
}

/// Slot 56 `IsAwake(ipart)`: rigid 0x350CCD30 (`+508` byte), particle 0x35039BB0; base 0
/// (living entities inherit it).
#[must_use]
pub fn is_awake(w: &World, e: EntIdx) -> i32 {
    match &w.ent(e).kind {
        EntityKind::Rigid(r) => (r.awake_bits & 0xFF) as i32,
        EntityKind::Particle(_) => particle::is_awake(w, e),
        _ => 0,
    }
}

// ---------------------------------------------------------------------------------------------
// the step slots the world calls, dispatched on the class (base defaults from vtable 0x351EFE38)

/// Slot 26 `StartStep(dt)`: rigid 0x350CCB20, living 0x35024DC0, particle 0x35039850; base
/// nothing.
pub fn start_step(w: &mut World, e: EntIdx, dt: f32) {
    match w.ent(e).kind {
        EntityKind::Rigid(_) => rigid::update::start_step(w, e, dt),
        EntityKind::Living(_) => living::start_step(w, e, dt),
        EntityKind::Particle(_) => particle::start_step(w, e, dt),
        _ => {}
    }
}

/// Slot 37 `GetMaxTimeStep(dt)`: rigid 0x350BCAA0, wheeled 0x35104790, living 0x35025390,
/// particle 0x35039CE0; base `return dt` (0x35037C10).
pub fn get_max_time_step(w: &mut World, e: EntIdx, dt: f32) -> f32 {
    match &w.ent(e).kind {
        EntityKind::Rigid(r) if r.wheeled.is_some() => wheeled::get_max_time_step(w, e, dt),
        EntityKind::Rigid(_) => rigid::update::get_max_time_step(w, e, dt),
        EntityKind::Living(_) => living::get_max_time_step(w, e, dt),
        EntityKind::Particle(_) => particle::get_max_time_step(w, e, dt),
        _ => dt,
    }
}

/// Slot 34 `Step(dt)`: rigid 0x350C9DF0 (also wheeled vehicles, which hook slots 78/79),
/// living 0x3502DED0, particle 0x350407E0; base `return 1`.
pub fn step(w: &mut World, e: EntIdx, dt: f32) -> i32 {
    match w.ent(e).kind {
        EntityKind::Rigid(_) => i32::from(rigid::step::step(w, e, dt)),
        EntityKind::Living(_) => living::step(w, e, dt),
        EntityKind::Particle(_) => particle::step(w, e, dt),
        _ => 1,
    }
}

/// Slot 27 `StepBack(dt)`: rigid 0x350BBD60; base nothing.
pub fn step_back(w: &mut World, e: EntIdx, dt: f32) {
    if let EntityKind::Rigid(_) = w.ent(e).kind {
        rigid::update::step_back(w, e, dt);
    }
}

/// Slot 41 `Update(dt, damping)`: rigid 0x350BEB40, wheeled 0x351050A0, living 0x3502C240;
/// base `return 1`.
pub fn update(w: &mut World, e: EntIdx, dt: f32, damping: f32) -> u32 {
    match &w.ent(e).kind {
        EntityKind::Rigid(r) if r.wheeled.is_some() => wheeled::update(w, e, dt, damping),
        EntityKind::Rigid(_) => rigid::update::update(w, e, dt, damping),
        EntityKind::Living(_) => living::update(w, e, dt, damping),
        _ => 1,
    }
}

/// Slot 40 `RegisterContacts(dt, nMaxPlaneContacts)`: rigid 0x350C1520, wheeled 0x35105E50,
/// living 0x35027190; base `return 0`.
pub fn register_contacts(w: &mut World, e: EntIdx, dt: f32, n_max: i32) -> i32 {
    match &w.ent(e).kind {
        EntityKind::Rigid(r) if r.wheeled.is_some() => {
            wheeled::register_contacts(w, e, dt, n_max)
        }
        EntityKind::Rigid(_) => rigid::update::register_contacts(w, e, dt, n_max),
        EntityKind::Living(_) => living::register_contacts(w, e, dt, n_max),
        _ => 0,
    }
}

/// Slot 42 `CalcEnergy(dt)`: rigid 0x350AB8F0, living 0x35026FB0; base `0.0`.
#[must_use]
pub fn calc_energy(w: &World, e: EntIdx, dt: f32) -> f32 {
    match w.ent(e).kind {
        EntityKind::Rigid(_) => rigid::update::calc_energy(w, e, dt),
        EntityKind::Living(_) => living::calc_energy(w, e, dt),
        _ => 0.0,
    }
}

/// Slot 43 `GetDamping(dt)`: rigid 0x350AC490, wheeled 0x35104FA0; base `1.0` (0x35037C20).
#[must_use]
pub fn get_damping(w: &World, e: EntIdx, dt: f32) -> f32 {
    match &w.ent(e).kind {
        EntityKind::Rigid(r) if r.wheeled.is_some() => wheeled::get_damping(w, e, dt),
        EntityKind::Rigid(_) => rigid::update::get_damping(w, e, dt),
        _ => 1.0,
    }
}

/// Slot 39 `GetContactCount(nMax)`: rigid 0x350AC3E0; base 0.
#[must_use]
pub fn contact_count(w: &World, e: EntIdx, nmax: i32) -> i32 {
    match w.ent(e).kind {
        EntityKind::Rigid(_) => rigid::update::contact_count(w, e, nmax),
        _ => 0,
    }
}

/// Slot 38 `GetLastTimeStep(dt)`: rigid 0x35020A40 (`+604`); base `return dt` (0x35037C10).
#[must_use]
pub fn last_time_step(w: &World, e: EntIdx, dt: f32) -> f32 {
    match w.ent(e).kind {
        EntityKind::Rigid(_) => rigid::update::last_time_step(w, e),
        _ => dt,
    }
}

/// Slot 44 `GetMaxFriction`: rigid 0x35020A50 (`+704`); base `100.0` (0x35037C30).
#[must_use]
pub fn max_friction(w: &World, e: EntIdx) -> f32 {
    match w.ent(e).kind {
        EntityKind::Rigid(_) => rigid::update::max_friction(w, e),
        _ => 100.0,
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
            r.collider_contacts.insert(i as usize, crate::solver::ContactId::NONE);
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
    if w.ent(e).wheeled().is_some() {
        return wheeled::remove_collider(w, e, c, always);
    }
    class_remove_collider(w, e, c, always)
}

/// Slot 48 of `CRigidEntity` / `CPhysicalEntity` (what `CWheeledVehicleEntity` 0x35104840
/// calls after clearing its wheel contacts).
pub fn class_remove_collider(w: &mut World, e: EntIdx, c: EntIdx, always: bool) -> i32 {
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
pub fn has_constraint_contacts_with(w: &World, e: EntIdx, other: EntIdx, flags_ignore: i32) -> bool {
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
    if w.ent(e).wheeled().is_some() {
        return wheeled::has_contacts_with(w, e, other);
    }
    class_has_contacts_with(w, e, other)
}

/// Slot 50 of `CRigidEntity` (what `CWheeledVehicleEntity` 0x351047F0 falls back to).
#[must_use]
pub fn class_has_contacts_with(w: &World, e: EntIdx, other: EntIdx) -> bool {
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
    match &w.ent(e).kind {
        EntityKind::Living(_) => return living::detach_hook(w, e, mode),
        EntityKind::Particle(_) => return particle::detach_hook(w, e, mode),
        EntityKind::Rigid(r) if r.wheeled.is_some() => return wheeled::detach_hook(w, e, mode),
        _ => {}
    }
    class_detach_hook(w, e, mode);
}

/// Slot 54 of `CRigidEntity` (0x350A9DD0) / `CPhysicalEntity` (0x350530B0): what the derived
/// classes call.
pub fn class_detach_hook(w: &mut World, e: EntIdx, mode: i32) {
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
            && ((ent.pod_refs > 0 && ((ent.id_bits & 0xFF00_0000) as i32) < 0x300_0000) || ent.flags & 0x10_0000 != 0)
        {
            let v10 = w.vars.contact_gap * 4.0;
            let bb = ent.bbox;
            let v11 = (bb[1].x - bb[0].x) * 0.001 + v10;
            let v12 = (bb[1].z - bb[0].z) * 0.001 + v10;
            let v13 = (bb[1].y - bb[0].y) * 0.001 + v10;
            let v14 = if ent.flags & 0x10_0000 != 0 { 2.0 } else { 0.0 };
            let hi = crate::math::Vec3::new(0.0 + (v11 + bb[1].x), 0.0 + (v13 + bb[1].y), (v12 + bb[1].z) + v14);
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
