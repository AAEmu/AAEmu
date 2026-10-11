//! Rigid-body contact bookkeeping: the entity contact list (`E+536`, per-collider runs at
//! `E+520`), `sub_350AA6E0` (insert), `sub_350AA810` (remove), `sub_350AA9A0` (drop all), and
//! the per-thread scratch table of CheckForNewContacts (`qword_35218710[tid]`).

use crate::solver::ContactId;
use crate::world::{EntIdx, World};

/// The per-thread table of `CheckForNewContacts` (`qword_35218710 + 2056·tid`): for every
/// geometric contact `i` of the last call, the collider entity (`[i]`) and the part pair
/// (`[128 + i]`, low = own part, high = collider part), plus `[256]` (the index of the first
/// contact of the current contact-area run, -1 when none).
#[derive(Clone, Debug)]
pub struct ContactScratch {
    pub collider: Vec<EntIdx>,
    pub parts: Vec<(i32, i32)>,
    pub area_start: i32,
    /// The upper half of `[256]`: the number of records of the last call.
    pub count: i32,
}

impl Default for ContactScratch {
    fn default() -> Self {
        Self {
            collider: vec![EntIdx::NONE; 128],
            parts: vec![(0, 0); 128],
            area_start: -1,
            count: 0,
        }
    }
}

/// `sub_350AA9A0`: return every contact of `e` to the world pool, reset the list and the
/// per-collider first-contact pointers, and drop the colliders that have neither contacts nor
/// constraints with `e` (in both directions).
pub fn drop_contacts(w: &mut World, e: EntIdx) {
    let Some(r) = w.ent(e).rigid() else {
        return;
    };
    let head = r.contacts_head;
    let mut c = w.contacts.get(head).next;
    while c != head {
        let next = w.contacts.get(c).next;
        w.contacts.free_push_front(c);
        c = next;
    }
    let hc = w.contacts.get_mut(head);
    hc.next = head;
    hc.prev = head;
    let n = w.ent(e).colliders.len() as i32;
    {
        let r = w.ent_mut(e).rigid_mut().expect("rigid");
        r.n_contacts = 0;
    }
    let mut i = n - 1;
    while i >= 0 {
        let iu = i as usize;
        w.ent_mut(e).rigid_mut().expect("rigid").collider_contacts[iu] = ContactId::NONE;
        let mask = w.ent(e).rigid().expect("rigid").collider_masks[iu];
        if mask == 0 {
            let c = w.ent(e).colliders[iu];
            if !crate::entities::has_constraint_contacts_with(w, c, e, 0) {
                crate::entities::remove_collider(w, c, e, true);
                crate::entities::remove_collider(w, e, c, true);
            }
        }
        i -= 1;
    }
}

/// `sub_350AA6E0(ent, contact, icollider, collider)`: link a new contact at the front of
/// its collider's run (the first contact of a run gets flag 0x80000 = last of the run).
pub fn insert_contact(w: &mut World, e: EntIdx, c: ContactId, i: i32, collider: EntIdx) {
    let ent = w.ent(e);
    let mut i = i;
    if !(i >= 0 && (i as usize) < ent.colliders.len() && ent.colliders[i as usize] == collider) {
        match ent.colliders.iter().position(|&x| x == collider) {
            Some(k) => i = k as i32,
            None => return,
        }
    }
    let iu = i as usize;
    let head = ent.rigid().expect("rigid").contacts_head;
    let first = ent.rigid().expect("rigid").collider_contacts[iu];
    let at = if first.is_none() {
        // the run starts at the list head: the contact goes before the current first
        w.ent_mut(e).rigid_mut().expect("rigid").collider_contacts[iu] = w.contacts.get(head).next;
        w.contacts.get_mut(c).flags = 0x8_0000;
        w.contacts.get(head).next
    } else {
        w.contacts.get_mut(c).flags = 0;
        first
    };
    w.contacts.insert_before(c, at);
    let r = w.ent_mut(e).rigid_mut().expect("rigid");
    r.collider_contacts[iu] = c;
    r.n_contacts += 1;
}

/// `sub_350AA810(ent, contact, icollider, bCheckColliders)`: unlink a contact from the
/// entity's list. `icollider < 0` searches the collider. When the contact was the first of
/// its run, the run start moves to the next contact (or none if it was also the last); with
/// `check_colliders` a collider left without contacts and constraints is removed in both
/// directions. Otherwise the run's last-flag moves to the previous contact.
pub fn remove_contact(
    w: &mut World,
    e: EntIdx,
    c: ContactId,
    icollider: i32,
    check_colliders: bool,
) {
    w.contacts.unlink(c);
    let mut i = icollider;
    if i < 0 {
        let other = w.contacts.get(c).pent[1];
        i = w
            .ent(e)
            .colliders
            .iter()
            .position(|&x| x == other)
            .map_or(w.ent(e).colliders.len() as i32, |k| k as i32);
    }
    let iu = i as usize;
    let first = w
        .ent(e)
        .rigid()
        .expect("rigid")
        .collider_contacts
        .get(iu)
        .copied();
    if first == Some(c) {
        let rec = *w.contacts.get(c);
        let nf = if rec.flags & 0x8_0000 != 0 {
            ContactId::NONE
        } else {
            rec.next
        };
        w.ent_mut(e).rigid_mut().expect("rigid").collider_contacts[iu] = nf;
        if check_colliders {
            let col = w.ent(e).colliders[iu];
            if !crate::entities::has_contacts_with(w, e, col)
                && !crate::entities::has_contacts_with(w, col, e)
            {
                crate::entities::remove_collider(w, col, e, true);
                crate::entities::remove_collider(w, e, col, true);
            }
        }
    } else {
        let rec = *w.contacts.get(c);
        let p = rec.prev;
        w.contacts.get_mut(p).flags |= rec.flags & 0x8_0000;
    }
    w.ent_mut(e).rigid_mut().expect("rigid").n_contacts -= 1;
}
