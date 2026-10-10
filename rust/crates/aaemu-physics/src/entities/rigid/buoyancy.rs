//! Water and air for rigid bodies (`dynamics.md` §4): the area gathering `sub_350C4880`
//! (CheckAreas at the bbox centre + the post-step event), `ApplyBuoyancy` 0x350C0E50 and
//! `ApplyAirResistance` 0x350C2790.

use crate::params::ParamsBuoyancy;
use crate::world::events::Event;
use crate::world::{EntIdx, World};

/// `sub_350C4880(ent, dt, buoy[] out, nMax)`: CheckAreas (world slot 87) at the entity's bbox
/// centre (skipped without a global area or with flag 0x800000), the sleep-prevention timer
/// (`+608 = 0.2` when the area gravity differs from gravityFreefall), and the post-step event
/// (type 4) when the entity has flag 0x80000 / 0x8000000 (otherwise `+1104 = 1`). Returns the
/// number of buoyancy records.
pub fn gather_areas(w: &mut World, e: EntIdx, dt: f32) -> Vec<ParamsBuoyancy> {
    gather_areas_n(w, e, dt, 0)
}

/// [`gather_areas`] with room for `nmax` buoyancy records.
pub fn gather_areas_n(w: &mut World, e: EntIdx, dt: f32, nmax: usize) -> Vec<ParamsBuoyancy> {
    let caller = w.tls_thread;
    let ent = w.ent(e);
    let mut out = Vec::new();
    let mut gravity = crate::sentinel::UNUSED_VEC3;
    let n = if w.global_area.is_none() || ent.flags & 0x80_0000 != 0 {
        0
    } else {
        let c = crate::math::Vec3::new(
            (ent.bbox[0].x + ent.bbox[1].x) * 0.5,
            (ent.bbox[0].y + ent.bbox[1].y) * 0.5,
            (ent.bbox[1].z + ent.bbox[0].z) * 0.5,
        );
        let vel = w.bodies.get(ent.rigid().expect("rigid").body).v;
        let r = w.check_areas(c, &mut gravity, &mut out, nmax, -1, vel, Some(e), 0.0, caller);
        r
    };
    let r = w.ent(e).rigid().expect("rigid");
    let gf = r.gravity_freefall;
    if dt > 0.0
        && n > 0
        && !crate::sentinel::is_unused(gravity.x)
        && ((gf.y - gravity.y) * (gf.y - gravity.y) + (gf.x - gravity.x) * (gf.x - gravity.x))
            + (gf.z - gravity.z) * (gf.z - gravity.z)
            != 0.0
    {
        w.ent_mut(e).rigid_mut().expect("rigid").min_awake_time = f32::from_bits(1_045_220_557); // 0.2
    }
    let ent = w.ent(e);
    if ent.flags & 0x808_0000 != 0 {
        let eps = w.vars.rigid_eps;
        let moved = (ent.pos2.x - ent.pos.x).abs() > eps
            || (ent.pos2.y - ent.pos.y).abs() > eps
            || (ent.pos2.z - ent.pos.z).abs() > eps
            || !quat_close(ent.q2, ent.q, eps * eps);
        if moved {
            let r = ent.rigid().expect("rigid");
            let ev = Event::PostStep {
                ent: e,
                dt,
                pos: r.pos_new,
                q: r.q_new,
                step: w.step_counter,
            };
            let flags = ent.flags;
            let entm = w.ent_mut(e);
            entm.pos2 = entm.pos;
            entm.q2 = entm.q;
            w.signal_event(flags, ev);
            w.ent_mut(e).rigid_mut().expect("rigid").last_ext_contact_step = w.pump_counter;
        }
    } else {
        w.ent_mut(e).rigid_mut().expect("rigid").last_ext_contact_step = 1;
    }
    let _ = n;
    out
}

/// `sub_35039510`: every component of `a` within `eps` of `b` or of `-b`.
fn quat_close(a: crate::math::Quat, b: crate::math::Quat, eps: f32) -> bool {
    let near = |a: crate::math::Quat, b: crate::math::Quat| {
        (a.x - b.x).abs() <= eps && (a.y - b.y).abs() <= eps && (a.z - b.z).abs() <= eps && (a.w - b.w).abs() <= eps
    };
    near(a, b) || near(a, crate::math::Quat::new(-b.x, -b.y, -b.z, -b.w))
}
