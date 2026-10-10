//! Water and air for rigid bodies (`dynamics.md` §4): the area gathering `sub_350C4880`
//! (CheckAreas at the bbox centre + the post-step event), `ApplyBuoyancy` 0x350C0E50,
//! `ApplyAirResistance` 0x350C2790 and the rigid `SetParams(pe_params_buoyancy)` (id 12).
//!
//! The rigid Step calls, per substep, after integrating the new pose:
//! ```ignore
//! let buoys = gather_areas_n(w, e, dt, 4);
//! apply_buoyancy(w, e, dt, Vec3::new(0.0, 0.0, -9.81), &buoys);
//! if w.ent(e).flags & 8 != 0 { apply_air_resistance(w, e, dt); }
//! ```

use crate::entities::base::Part;
use crate::geometry::primitives::Plane;
use crate::geometry::GeomWorldData;
use crate::math::{Mat33, Quat, Vec3};
use crate::params::{ActionImpulse, ParamsBuoyancy};
use crate::sentinel::{is_unused, UNUSED_F32, UNUSED_VEC3};
use crate::world::events::Event;
use crate::world::{EntIdx, World};

/// `sub_350C4880(ent, dt, buoy[] out, nMax)`: CheckAreas (world slot 87) at the entity's bbox
/// centre (skipped without a global area or with flag 0x800000; medium -1, the body velocity,
/// `r = 0`, the calling thread), the sleep-prevention timer (`+608 = 0.2` when the area
/// gravity differs from gravityFreefall) and the post-step event (type 4) when the entity has
/// flag 0x80000 / 0x8000000 and moved (otherwise `+1104 = 1`). Returns the buoyancy records
/// (the CheckAreas count; none for 0 / -1).
pub fn gather_areas(w: &mut World, e: EntIdx, dt: f32) -> Vec<ParamsBuoyancy> {
    gather_areas_n(w, e, dt, 4)
}

/// [`gather_areas`] with room for `nmax` buoyancy records.
pub fn gather_areas_n(w: &mut World, e: EntIdx, dt: f32, nmax: usize) -> Vec<ParamsBuoyancy> {
    let mut buf = vec![ParamsBuoyancy::default(); nmax];
    let n = gather_areas_raw(w, e, dt, &mut buf);
    buf.truncate(n.max(0) as usize);
    buf
}

/// [`gather_areas`] writing into a caller buffer (`buf.len()` = nMax); returns the raw
/// CheckAreas result (`-1` = none).
pub fn gather_areas_raw(w: &mut World, e: EntIdx, dt: f32, buf: &mut [ParamsBuoyancy]) -> i32 {
    let caller = w.tls_thread;
    let ent = w.ent(e);
    let mut gravity = UNUSED_VEC3;
    let n = if w.global_area.is_none() || ent.flags & 0x80_0000 != 0 {
        0
    } else {
        let b = ent.bbox;
        let c = Vec3::new((b[0].x + b[1].x) * 0.5, (b[0].y + b[1].y) * 0.5, (b[0].z + b[1].z) * 0.5);
        let vel = w.bodies.get(ent.rigid().expect("rigid").body).v;
        w.check_areas(c, &mut gravity, buf, -1, vel, Some(e), 0.0, caller, &mut [])
    };
    let gf = w.ent(e).rigid().expect("rigid").gravity_freefall;
    if dt > 0.0
        && n > 0
        && !is_unused(gravity.x)
        && ((gf.y - gravity.y) * (gf.y - gravity.y) + (gf.x - gravity.x) * (gf.x - gravity.x))
            + (gf.z - gravity.z) * (gf.z - gravity.z)
            != 0.0
    {
        w.ent_mut(e).rigid_mut().expect("rigid").min_awake_time = f32::from_bits(0x3E4C_CCCD); // 0.2
    }
    let ent = w.ent(e);
    if ent.flags & 0x808_0000 != 0 {
        let eps = w.vars.rigid_eps;
        let moved = (ent.pos2.x - ent.pos.x).abs() > eps
            || (ent.pos2.y - ent.pos.y).abs() > eps
            || (ent.pos2.z - ent.pos.z).abs() > eps
            || !quat_close(ent.q2, ent.q, eps * eps);
        if moved {
            let flags = ent.flags;
            let r = ent.rigid().expect("rigid");
            let (pos_new, q_new, stamp) = (r.pos_new, r.q_new, r.last_ext_contact_step);
            let step = w.step_counter;
            let entm = w.ent_mut(e);
            entm.pos2 = entm.pos;
            entm.q2 = entm.q;
            let ev = Event::PostStep {
                ent: e,
                dt,
                pos: pos_new,
                q: q_new,
                step,
            };
            // immediate clients (flag 0x80000)
            w.signal_event(flags & 0x8_0000, ev.clone());
            // logged (flag 0x8000000): merged into this entity's event of the current pump
            // cycle when there is one
            let merged = if stamp == w.pump_counter && w.vars.thread_bias == 0 {
                let mut hit = false;
                if let Some(Event::PostStep { dt: d, pos, q, step: s, .. }) =
                    w.events.logged.iter_mut().rev().find(|x| matches!(x, Event::PostStep { ent, .. } if *ent == e))
                {
                    *d += dt;
                    *s = step;
                    *pos = pos_new;
                    *q = q_new;
                    hit = true;
                }
                hit
            } else {
                false
            };
            if merged {
                let entm = w.ent_mut(e);
                if entm.processed & 0x1_0000 != 0 {
                    entm.processed -= 0x1_0000;
                }
            } else {
                w.signal_event(flags & 0x800_0000, ev);
                if flags & 0x800_0000 != 0 {
                    let entm = w.ent_mut(e);
                    if entm.processed & 0x1_0000 != 0 {
                        entm.processed -= 0x1_0000;
                    }
                }
                let pc = w.pump_counter;
                w.ent_mut(e).rigid_mut().expect("rigid").last_ext_contact_step = pc;
            }
        }
    } else {
        w.ent_mut(e).rigid_mut().expect("rigid").last_ext_contact_step = 1;
    }
    n
}

/// `sub_35039510`: every component of `a` within `eps` of `b` or of `-b`.
fn quat_close(a: Quat, b: Quat, eps: f32) -> bool {
    let near = |a: Quat, b: Quat| {
        (a.x - b.x).abs() <= eps && (a.y - b.y).abs() <= eps && (a.z - b.z).abs() <= eps && (a.w - b.w).abs() <= eps
    };
    near(a, b) || near(a, Quat::new(-b.x, -b.y, -b.z, -b.w))
}

/// The world placement of a part under the entity's NEW pose (`E+448`/`+460`): `q_new ⊗
/// part.q`, `pos_new + q_new·part.pos`, `part.scale`, with the given motion.
fn part_gwd(q_new: Quat, pos_new: Vec3, p: &Part, v: Vec3, wv: Vec3, com: Vec3) -> GeomWorldData {
    let q = q_new.product(p.q);
    let o = q_new.rotate(p.pos);
    GeomWorldData {
        offset: Vec3::new(o.x + pos_new.x, o.y + pos_new.y, o.z + pos_new.z),
        r: Mat33::from_quat(q),
        scale: p.scale,
        v,
        w: wv,
        center_of_mass: com,
    }
}

/// `ApplyBuoyancy(ent, dt, g, buoy[], n)` (0x350C0E50, `dynamics.md` §4.2): buoyancy and
/// medium resistance impulses per floating part for every record with `iMedium < 2`
/// (unsigned: an unset medium is skipped), the water damping blend `E+692`, the submerged
/// fraction `E+1136` and the "floats" flag (`flags2 0x4000000`).
///
/// Kept from the binary (all verified against the oracle):
/// * `g` is the caller's constant `(0, 0, −9.81)`, not the world gravity.
/// * The impulse accumulators are **not reset between parts or records**: the medium
///   resistance of cylinders/capsules (slot 14 accumulates) adds the previous part's
///   impulse, and the first part's angular accumulator starts as the unused sentinel (its
///   `y, z` are uninitialised stack words in the binary, 0 here) — an unused angular impulse
///   is ignored by `action_impulse`.
/// * The flow drag without resistance has no `dt` and the opposite sign convention.
/// * `Vtot` sums the **unscaled** geometry volumes.
/// * The whole-entity-submerged shortcut uses the scaled volume and no torque.
///
/// Impulses go through [`super::action::impulse`] (`iApplyTime = 0`, `iSource = 1`: applied
/// at once, `|Δv|, |Δω| ≤ 5`, no wake).
///
/// Not ported: the water-manager hook (`world+20848`, absent on the zone server) and the
/// splash events (`vars.max_splash_events` > 0 only; see [`splash_gate`]).
pub fn apply_buoyancy(w: &mut World, e: EntIdx, dt: f32, g: Vec3, buoy: &[ParamsBuoyancy]) {
    let r = w.ent(e).rigid().expect("rigid");
    let body_id = r.body;
    let k_rho = r.k_water_density;
    if k_rho == 0.0 || w.bodies.get(body_id).minv == 0.0 {
        w.ent_mut(e).rigid_mut().expect("rigid").submerged_fraction = 0.0;
        return;
    }
    w.ent_mut(e).rigid_mut().expect("rigid").flags2 &= !0x400_0000;
    let mut frac = 0.0f32; // v275
    // the action_impulse accumulators (v197.. / v200..)
    let mut imp_p = Vec3::ZERO;
    let mut imp_l = Vec3::new(UNUSED_F32, 0.0, 0.0);
    let mut fwater = Vec3::ZERO; // v165, v169, v166
    let mut iwater = -1i32; // v168
    for (irec, a) in buoy.iter().enumerate() {
        if (a.i_medium as u32) >= 2 {
            continue;
        }
        let ent = w.ent(e);
        let rd = ent.rigid().expect("rigid");
        let n = a.water_plane_n;
        let mut o = a.water_plane_origin;
        o.z -= rd.water_level_offset;
        if !(n.z != 1.0) {
            o.x = ent.pos.x;
            o.y = ent.pos.y;
        }
        let plane = Plane { n, origin: o };
        let bb = rd.bbox_new;
        let cx = (bb[1].x + bb[0].x) * 0.5;
        let cy = (bb[1].y + bb[0].y) * 0.5;
        let cz = (bb[1].z + bb[0].z) * 0.5;
        let hx = (bb[1].x - bb[0].x) * 0.5;
        let hy = (bb[1].y - bb[0].y) * 0.5;
        let hz = (bb[1].z - bb[0].z) * 0.5;
        let d = ((cx - o.x) * n.x + (cy - o.y) * n.y) + (cz - o.z) * n.z;
        let ext = ((hx * n.x).abs() + (hy * n.y).abs()) + (hz * n.z).abs();
        if d > ext {
            continue;
        }
        let rho = a.water_density;
        let res = a.water_resistance;
        let k_res = rd.k_water_resistance;
        let (pos_new, q_new) = (rd.pos_new, rd.q_new);
        let mut vtot = 0.0f32; // v35
        let mut vsub = 0.0f32; // v36
        let nparts = ent.parts.len();
        for ip in 0..nparts {
            let p = w.ent(e).parts[ip].clone();
            if p.flags & 0x1_0000 == 0 {
                continue;
            }
            let pg = &p.pgeom_proxy;
            let s = p.scale;
            let go = (((s * s) * s) * pg.volume) * rho * k_rho > p.mass * f32::from_bits(0x3DCC_CCCD)
                || {
                    let sz = pg.geom.bbox().size;
                    let m1 = ((sz.y * sz.x - sz.y * sz.z).abs() + (sz.y * sz.x + sz.y * sz.z)) * 0.5;
                    let amax = ((m1 - sz.x * sz.z).abs() + (sz.x * sz.z + m1)) * 0.5;
                    ((amax * (s * s)) * res) * k_res > p.mass * f32::from_bits(0x3C4C_CCCD)
                }
                || !(0.0 >= a.water_damping);
            if !go {
                continue;
            }
            let b = *w.bodies.get(body_id);
            let flow = a.water_flow;
            let u = Vec3::new(b.v.x - flow.x, b.v.y - flow.y, b.v.z - flow.z);
            let gwd = part_gwd(q_new, pos_new, &p, u, b.w, b.pos);
            let (mut px, mut py, mut pz);
            let (mut lx, mut ly, mut lz);
            if !(0.0 >= res) {
                let mut dp = imp_p;
                let mut dl = imp_l;
                pg.geom.calculate_medium_resistance_acc(&plane, &gwd, &mut dp, &mut dl);
                let k = k_res * res;
                let fx = dp.x * k;
                let fy = dp.y * k;
                let fz = dp.z * k;
                let kdt = k * dt;
                if a.i_medium == 0 {
                    iwater = irec as i32;
                }
                let f = if a.i_medium == 0 { 1.0f32 } else { 0.0 };
                fwater.x += f * fx;
                fwater.y += f * fy;
                fwater.z += f * fz;
                lx = dl.x * kdt;
                ly = dl.y * kdt;
                lz = dl.z * kdt;
                px = fx * dt;
                py = fy * dt;
                pz = fz * dt;
            } else {
                if (flow.x * flow.x + flow.y * flow.y) + flow.z * flow.z > 0.0 {
                    let k = ((((hz + hy) * hx) + hz * hy) * rho) * k_rho;
                    px = (flow.x - b.v.x) * k;
                    py = k * (flow.y - b.v.y);
                    pz = k * (flow.z - b.v.z);
                } else {
                    px = 0.0;
                    py = 0.0;
                    pz = 0.0;
                }
                lx = 0.0;
                ly = 0.0;
                lz = 0.0;
            }
            let vs;
            if !(d > -ext) {
                // whole entity under water: the scaled volume at the CoM, no torque
                vs = ((s * s) * s) * pg.volume;
                let k = ((rho * k_rho) * vs) * dt;
                px -= g.x * k;
                py -= g.y * k;
                pz -= g.z * k;
            } else {
                let (v, cob) = pg.geom.calculate_buoyancy(&plane, &gwd);
                vs = v;
                let k = -((((rho * k_rho) * vs) * dt));
                let (jx, jy, jz) = (g.x * k, g.y * k, g.z * k);
                px += jx;
                py += jy;
                pz += jz;
                let rx = cob.x - b.pos.x;
                let ry = cob.y - b.pos.y;
                let rz = cob.z - b.pos.z;
                lx += (ry * jz) - (rz * jy);
                ly += (jx * rz) - (rx * jz);
                lz += (rx * jy) - (jx * ry);
            }
            imp_p = Vec3::new(px, py, pz);
            imp_l = Vec3::new(lx, ly, lz);
            if ((lx * lx + ly * ly) + lz * lz) + ((px * px + py * py) + pz * pz) > 0.0 {
                let act = ActionImpulse {
                    impulse: imp_p,
                    ang_impulse: imp_l,
                    ipart: ip as i32,
                    i_apply_time: 0,
                    i_source: 1,
                    ..ActionImpulse::default()
                };
                super::action::impulse(w, e, &act);
            }
            vsub += vs;
            vtot += pg.volume;
        }
        if !(vtot * vsub > 0.0) {
            continue;
        }
        let rr = if vsub >= vtot { 1.0f32 } else { vsub / vtot };
        let rd = w.ent_mut(e).rigid_mut().expect("rigid");
        let wd = rd.water_damping;
        let ad = a.water_damping;
        let v85 = (((wd - ad).abs() + (ad + wd)) * 0.5) * rr + (1.0 - rr) * rd.damping;
        rd.water_damping_eff = ((rd.water_damping_eff - v85).abs() + (rd.water_damping_eff + v85)) * 0.5;
        if a.i_medium == 0 {
            frac = ((frac - rr).abs() + (rr + frac)) * 0.5;
            let b = *w.bodies.get(body_id);
            if (rho * k_rho) * b.volume > b.m {
                w.ent_mut(e).rigid_mut().expect("rigid").flags2 |= 0x400_0000;
            }
        }
    }
    w.ent_mut(e).rigid_mut().expect("rigid").submerged_fraction = frac;
    let _ = splash_gate(w, e, iwater, fwater);
}

/// The splash-event gate of `ApplyBuoyancy` (`dynamics.md` §4.5): splash collision events
/// against the water are posted only when `vars.max_splash_events` (int at `+452`) times
/// (water record index + 1) is positive, the entity has no flag 4, is less than 90 %
/// submerged and its CoM is within `vars.splash_dist` (`+468`, clamped below by `+456`) of
/// the world's splash reference point (`+20856`, set by the game client; absent on the zone
/// server). Returns whether the gate passes. The events themselves are cosmetic and **not
/// ported** (they need the box-vs-part intersection with contact areas, `sub_351348D0`); with
/// the default `max_splash_events = 0` the gate never passes.
fn splash_gate(w: &World, e: EntIdx, iwater: i32, _fwater: Vec3) -> bool {
    let n = w.vars.max_splash_events.to_bits() as i32;
    if (iwater + 1).wrapping_mul(n) <= 0 {
        return false;
    }
    let ent = w.ent(e);
    if ent.flags & 4 != 0 {
        return false;
    }
    let r = ent.rigid().expect("rigid");
    !(r.submerged_fraction >= 0.9)
}

/// `ApplyAirResistance(ent, dt)` (0x350C2790, `dynamics.md` §4.6; the rigid Step calls it
/// when the entity has flag 8): CheckAreas for medium 1 at the bbox centre (4 records); for
/// every record with `iMedium == 1` and every part with a positive scaled volume, the medium
/// resistance (slot 14, plane = the record's, velocity `body.v − flow`) scaled by `dt·kAir`
/// (`E+1140`) is sent as a **deferred, waking** impulse (`iApplyTime = 2`, `iSource = 0`).
///
/// Kept: the impulse accumulators are never reset (slot-14 classes that accumulate add the
/// previous, already scaled impulse) and the angular one starts as the unused sentinel.
pub fn apply_air_resistance(w: &mut World, e: EntIdx, dt: f32) {
    let caller = w.tls_thread;
    let ent = w.ent(e);
    let b = ent.bbox;
    let c = Vec3::new((b[0].x + b[1].x) * 0.5, (b[0].y + b[1].y) * 0.5, (b[0].z + b[1].z) * 0.5);
    let body_id = ent.rigid().expect("rigid").body;
    let vel = w.bodies.get(body_id).v;
    let mut buf = [ParamsBuoyancy::default(); 4];
    let mut g = UNUSED_VEC3;
    let n = w.check_areas(c, &mut g, &mut buf, 1, vel, Some(e), 0.0, caller, &mut []);
    let mut acc_p = Vec3::ZERO; // v56..58
    let mut acc_l = Vec3::new(UNUSED_F32, 0.0, 0.0); // v59..61
    for rec in buf.iter().take(n.max(0) as usize) {
        if rec.i_medium != 1 {
            continue;
        }
        let plane = Plane {
            n: rec.water_plane_n,
            origin: rec.water_plane_origin,
        };
        let nparts = w.ent(e).parts.len();
        for ip in 0..nparts {
            let ent = w.ent(e);
            let p = ent.parts[ip].clone();
            let s = p.scale;
            if !((((s * s) * s) * p.pgeom_proxy.volume) > 0.0) {
                continue;
            }
            let rd = ent.rigid().expect("rigid");
            let (pos_new, q_new, k_air) = (rd.pos_new, rd.q_new, rd.k_air_resistance);
            let bd = *w.bodies.get(body_id);
            let f = rec.water_flow;
            let u = Vec3::new(bd.v.x - f.x, bd.v.y - f.y, bd.v.z - f.z);
            let gwd = part_gwd(q_new, pos_new, &p, u, bd.w, bd.pos);
            p.pgeom_proxy
                .geom
                .calculate_medium_resistance_acc(&plane, &gwd, &mut acc_p, &mut acc_l);
            let k = dt * k_air;
            acc_l = Vec3::new(acc_l.x * k, acc_l.y * k, acc_l.z * k);
            acc_p = Vec3::new(acc_p.x * k, acc_p.y * k, acc_p.z * k);
            let (l, pp) = (acc_l, acc_p);
            if ((l.x * l.x + l.y * l.y) + l.z * l.z) + ((pp.x * pp.x + pp.y * pp.y) + pp.z * pp.z) > 0.0 {
                let act = ActionImpulse {
                    impulse: pp,
                    ang_impulse: l,
                    ipart: ip as i32,
                    i_apply_time: 2,
                    i_source: 0,
                    ..ActionImpulse::default()
                };
                super::action::impulse(w, e, &act);
            }
        }
    }
}

/// Rigid `SetParams(pe_params_buoyancy)` (id 12, in `CRigidEntity::SetParams` 0x350C4CC0):
/// `kwaterDensity = waterDensity / ρ_ref` (or `kwaterDensity` directly), `kwaterResistance =
/// waterResistance / res_ref` (or directly), `waterEmin`, `waterDamping`, `kAirResistance`,
/// `waterLevelOffset`. The references are the global area's density/resistance when it has a
/// water plane, else 1000. Returns 1.
pub fn set_params_buoyancy(w: &mut World, e: EntIdx, p: &ParamsBuoyancy) -> i32 {
    let (mut rho_ref, mut res_ref) = (1000.0f32, 1000.0f32);
    let g = w.global_area;
    if !g.is_none() && !is_unused(w.area(g).buoyancy.water_plane_origin.x) {
        let b = &w.area(g).buoyancy;
        rho_ref = b.water_density;
        res_ref = b.water_resistance;
    }
    let r = w.ent_mut(e).rigid_mut().expect("rigid");
    if !is_unused(p.water_density) {
        r.k_water_density = p.water_density / rho_ref;
    }
    if !is_unused(p.k_water_density) {
        r.k_water_density = p.k_water_density;
    }
    if !is_unused(p.water_resistance) {
        r.k_water_resistance = p.water_resistance / res_ref;
    }
    if !is_unused(p.k_water_resistance) {
        r.k_water_resistance = p.k_water_resistance;
    }
    if !is_unused(p.water_emin) {
        r.min_energy_water = p.water_emin;
    }
    if !is_unused(p.water_damping) {
        r.water_damping = p.water_damping;
    }
    if !is_unused(p.k_air_resistance) {
        r.k_air_resistance = p.k_air_resistance;
    }
    if !is_unused(p.water_level_offset) {
        r.water_level_offset = p.water_level_offset;
    }
    1
}
