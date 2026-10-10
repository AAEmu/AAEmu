//! `CRigidEntity::Awake` (slot 55, 0x350AB170).

use crate::world::{EntIdx, World};

/// Slot 55 `Awake(bAwake, iSource)`: simclass ≤ 2 bodies switch between sleeping (1) and
/// active (2) and re-list (RepositionEntity flag 2); higher simclasses only set the awake
/// byte. Rope colliders (simclass 7) are detached first. A massless body wakes its colliders.
/// Returns the simclass (-1 above 6).
pub fn awake(w: &mut World, e: EntIdx, b_awake: i32, i_source: i32) -> i32 {
    let sc = w.ent(e).sim_class();
    if sc as u32 > 6 {
        return -1;
    }
    if sc > 2 {
        let r = w.ent_mut(e).rigid_mut().expect("rigid");
        r.awake_bits = (r.awake_bits & !0xFF) | (b_awake as u32 & 0xFF);
        return sc;
    }
    // detach rope colliders (simclass 7), collected backwards then removed in that order
    let ropes: Vec<EntIdx> = w
        .ent(e)
        .colliders
        .iter()
        .rev()
        .copied()
        .filter(|&c| w.ent(c).id_bits & 0xFF00_0000 == 0x0700_0000)
        .collect();
    for c in ropes.into_iter().rev() {
        crate::entities::remove_collider(w, e, c, true);
    }
    let r = w.ent(e).rigid().expect("rigid");
    let b = w.bodies.get(r.body);
    let wv = ((b.w.x * b.w.x + b.w.y * b.w.y) + b.w.z * b.w.z) + ((b.v.x * b.v.x + b.v.y * b.v.y) + b.v.z * b.v.z);
    let awake_byte = (r.awake_bits & 0xFF) as i32;
    if (w.ent(e).sim_class() != b_awake + 1 || (awake_byte != b_awake && i_source == 5))
        && (b.minv > 0.0 || wv > 0.0 || b_awake == 0 || i_source == 1)
    {
        let r = w.ent_mut(e).rigid_mut().expect("rigid");
        r.awake_bits &= 0xFFFF_E0FF;
        r.awake_bits = (r.awake_bits & !0xFF) | (b_awake as u32 & 0xFF);
        r.flags2 &= ((b_awake as u32) << 28) | 0xEFFF_FFFF;
        w.ent_mut(e).set_sim_class(b_awake + 1);
        w.reposition_entity(e, 2, None);
        if b_awake != 0 {
            w.ent_mut(e).rigid_mut().expect("rigid").min_awake_time = 0.1;
        }
    }
    let minv = w.bodies.get(w.ent(e).rigid().expect("rigid").body).minv;
    if minv == 0.0 {
        let cols = w.ent(e).colliders.clone();
        for c in cols {
            if c != e && crate::entities::mass_inv(w, c) > 0.0 {
                crate::entities::awake(w, c, 1, 0);
            }
        }
    }
    w.ent(e).sim_class()
}
