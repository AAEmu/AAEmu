//! Slot 40 `RegisterContacts(dt, nMaxPlaneContacts)` of `CWheeledVehicleEntity`
//! (0x35105E50).

use super::{n_wheels, wd, wflags, wm};
use crate::math::Vec3;
use crate::world::events::Event;
use crate::world::{EntIdx, World};

/// Slot 40 0x35105E50: per wheel the logged-event link (`+240`) is cleared; a wheel in
/// contact on an axle registers its inline solver contact (`wheel+336`) with the island's
/// solver (`RegisterContact` 0x350994B0). A wheel in contact whose ground part has flag
/// `0x800000`, or whose suspension is fully compressed (`len ≤ 0`), logs a collision event
/// straight into the world's logged queue (no event-flag test): vehicle / ground, the contact
/// point and normal, the vehicle's mass, the wheel part id, the two surfaces, the suspension
/// impulse as the normal impulse and as velocity `−N/m_share·n` — fully compressed wheels add
/// the vehicle's point velocity along `n` and keep the event so that Update adds the solver
/// impulse to it. Then the rigid slot.
pub fn register_contacts(w: &mut World, e: EntIdx, dt: f32, n_max: i32) -> i32 {
    let tid = w.tls_thread.clamp(0, 4) as usize;
    let nw = n_wheels(w, e);
    let n_hull = wd(w, e).n_hull;
    let body = *w.bodies.get(w.ent(e).rigid().expect("rigid").body);
    for i in 0..nw.max(0) as usize {
        wm(w, e).wheels[i].event = None;
        let wh = wd(w, e).wheels[i].clone();
        if wh.flags & wflags::CONTACT == 0 {
            continue;
        }
        if wh.axle >= 0 {
            let (sol, pool) = w.solver_and_pool(tid);
            sol.register_contact(pool, wh.contact);
        }
        let ce = wh.contact_ent;
        let cflags = w
            .ent(ce)
            .parts
            .get(wh.contact_part as usize)
            .map_or(0, |p| p.flags);
        if cflags & 0x80_0000 == 0 && wh.len > 0.0 {
            continue;
        }
        let v17 = wh.n_impulse;
        let v20 = (-1.0 / wh.mass_share) * v17;
        let n = wh.n;
        let part_id = w.ent(e).parts[i + n_hull as usize].id;
        let mut v0 = Vec3::new(n.x * v20, n.y * v20, n.z * v20);
        let keep = wh.len <= 0.0;
        if keep {
            let p = wh.pt;
            let (dx, dy, dz) = (p.x - body.pos.x, p.y - body.pos.y, p.z - body.pos.z);
            let v23 = ((((body.w.z * dx) - (body.w.x * dz)) + body.v.y) * n.y
                + (((body.w.y * dz) - (body.w.z * dy)) + body.v.x) * n.x)
                + (((body.w.x * dy) - (body.w.y * dx)) + body.v.z) * n.z;
            v0 = Vec3::new(
                (v23 * n.x) + (n.x * v20),
                (n.y * v23) + (n.y * v20),
                (n.z * v23) + (n.z * v20),
            );
        }
        let ev = Event::Collision {
            ent: [e, ce],
            pt: wh.pt,
            n,
            v: [v0, Vec3::ZERO],
            mass: [body.m, 0.0],
            partid: [part_id, 0],
            idmat: [i32::from(wh.surf[0] as i16), i32::from(wh.surf[1] as i16)],
            penetration: 0.0,
            normimpulse: v17,
            radius: 0.0,
        };
        let idx = w.post_logged_event(ev);
        if keep {
            wm(w, e).wheels[i].event = Some(idx);
        }
    }
    crate::entities::rigid::update::register_contacts(w, e, dt, n_max)
}
