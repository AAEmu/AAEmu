//! The parameter interface of `CWheeledVehicleEntity`: slots 7 `SetParams` (0x35106740),
//! 8 `GetParams` (0x35104B50), 9 `GetStatus` (0x3510A090) and 10 `Action` (0x351094A0), on
//! raw x64 byte images like the rest of [`crate::entities::interface`].
//!
//! | id | struct | handled |
//! |---|---|---|
//! | params 2 | car params ([`crate::params::ParamsCar`], 144 B) | Set / Get (after the rigid slot returned 0) |
//! | params 16 | wheel params ([`crate::params::ParamsWheel`], 80 B) | Set / Get |
//! | params 10 / 6 | rigid simulation / part | rigid slot, then the wheeled post-processing |
//! | status 1 | position of a wheel part with its suspension and rotation | wheeled wrapper around the rigid slot |
//! | status 4 / 5 / 15 | vehicle / wheel / abilities | after the rigid slot returned 0 |
//! | action 3 | drive (pedal, steering with Ackermann geometry, clutch, brakes, gear) | after the rigid slot returned 0 |
//! | action 4 | reset (after the rigid reset): wheel ω, pedal, hull-contact timer | |
//!
//! **The gear-ratio pointer** (`pe_params_car::gearRatios`, `float*` at `+88`). The port has no
//! object addresses, so a pointer is accepted in one representation only: [`BUF_PTR_TAG`]`|
//! off` designates `&image[off]`, i.e. the ratios travel inside the same byte image (the
//! interpreter / oracle field syntax `p88=buf:400` plus `f400=…`; the oracle passes the real
//! address of its buffer at that offset, so both sides read and write the same bytes). `-1`
//! is "unused" as in the binary; any other pointer value (which the binary would dereference)
//! makes SetParams / GetParams of id 2 return 0 without side effects. The typed API
//! [`set_params_car`] / [`get_params_car`] takes and returns the ratios as slices.

// The arithmetic is written in the binary's operand order and with its single-precision
// constants (`a = b + a`, `x * -1.0`, `3.1415927`, NaN-aware negated comparisons).
#![allow(
    clippy::assign_op_pattern,
    clippy::neg_multiply,
    clippy::excessive_precision,
    clippy::approx_constant,
    clippy::neg_cmp_op_on_partial_ord,
    clippy::too_many_lines
)]
use super::{n_wheels, wd, wflags, wm, MAX_WHEELS};
use crate::entities::interface::{
    action_gate, params_gate, rd_i32, rd_u32, rd_u64, wr_f32, wr_i32, wr_u32, wr_u64, Gate,
};
use crate::math::{crt, Quat, Vec3};
use crate::params::{ParamsCar, PeStruct};
use crate::sentinel::{is_unused, UNUSED_I32 as UI};
use crate::world::{EntIdx, World};

/// Tag of an in-image pointer: `BUF_PTR_TAG | off` = `&image[off]` (see the module docs).
pub const BUF_PTR_TAG: u64 = 0xB0F0_0000_0000_0000;

/// ×π/30 (rpm → rad/s), the double constant of SetParams 2.
const RPM2W: f64 = 0.104_719_755_119_659_77;
/// ×30/π (rad/s → rpm), the double constant of GetParams 2 / GetStatus 4.
const W2RPM: f64 = 9.549_296_585_513_721;

#[inline]
fn rd_f(b: &[u8], o: usize) -> f32 {
    f32::from_bits(rd_u32(b, o))
}

#[inline]
fn set_f(b: &[u8], o: usize) -> Option<f32> {
    let x = rd_f(b, o);
    (!is_unused(x)).then_some(x)
}

#[inline]
fn set_i(b: &[u8], o: usize) -> Option<i32> {
    let x = rd_i32(b, o);
    (x != UI).then_some(x)
}

/// Resolve an in-image pointer at `+off` of `b`: `Ok(None)` = unused (-1), `Ok(Some(o))` =
/// `&b[o]`, `Err(())` = a pointer the port cannot follow.
fn image_ptr(b: &[u8], off: usize) -> Result<Option<usize>, ()> {
    let p = rd_u64(b, off);
    if p == u64::MAX {
        return Ok(None);
    }
    if p & !0xFFFF_FFFF == BUF_PTR_TAG {
        return Ok(Some((p & 0xFFFF_FFFF) as usize));
    }
    Err(())
}

// ---------------------------------------------------------------------------------------------
// slot 7 SetParams

/// Slot 7 `SetParams` 0x35106740: the lock-or-queue, `CRigidEntity::SetParams(p, 1)`, then
/// * rigid returned 0: id 2 (car params, RPM fields ×π/30 in double; the idle RPM also resets
///   the engine ω; the gear ratios are copied for `nGears` entries; `turn width > 0` is stored
///   as `2/x`) and id 16 (wheel params; the index is clamped to `[0, nWheels−1]`; a new
///   maximal length also clamps the rest and current lengths; damping `≤ 0` is a fraction of
///   critical damping `−sqrt(4·k·m_share)·x`) → 1, other ids → 0;
/// * id 10 handled by the rigid slot: `+8` (maxTimeStep) → `+11176`, `+12` (minEnergy) →
///   `+11164`;
/// * id 6 handled: the wheel of the part re-reads its initial pose (`+104` q0, `+120` pos0,
///   `+132` centre) and part flags when a geometry pointer is given (`+96`/`+104` ≠ -1). For
///   the all-parts form every wheel copies its part flags, and then — the loop counter being
///   left at `nWheels` — the "part" `nWheels` is treated like a given part (kept from the
///   binary).
pub fn set_params(w: &mut World, e: EntIdx, buf: &mut [u8], ts: i32) -> i32 {
    if buf.len() < 4 {
        return 0;
    }
    let ty = rd_i32(buf, 0);
    if ty == 2 && (buf.len() < ParamsCar::SIZE || image_ptr(buf, 88).is_err()) {
        return 0;
    }
    if ty == 16 && buf.len() < 80 {
        return 0;
    }
    if let Gate::Skip { .. } = params_gate(w, e, buf, ts) {
        return 1;
    }
    let v7 = crate::entities::interface::set::class_set_params(w, e, buf, 1);
    if v7 == 0 {
        return match ty {
            2 => {
                set_car(w, e, buf);
                1
            }
            16 => {
                set_wheel(w, e, buf);
                1
            }
            _ => 0,
        };
    }
    if ty == 10 {
        let d = wm(w, e);
        if let Some(x) = set_f(buf, 8) {
            d.max_time_step_hull = x;
        }
        if let Some(x) = set_f(buf, 12) {
            d.min_energy_hull = x;
        }
        return v7;
    }
    if ty != 6 {
        return v7;
    }
    let n_parts = w.ent(e).parts.len() as i32;
    let n_hull = wd(w, e).n_hull;
    let v8 = rd_i32(buf, 8);
    let v3: i32 = if v8 == UI {
        if rd_i32(buf, 4) == UI {
            let nw = n_parts - n_hull;
            for i in 0..nw.max(0) {
                let f = w.ent(e).parts[(i + n_hull) as usize].flags;
                wm(w, e).wheels[i as usize].part_flags = f;
            }
            nw.max(0)
        } else {
            let id = rd_i32(buf, 4);
            let parts = &w.ent(e).parts;
            parts
                .iter()
                .position(|p| p.id == id)
                .map_or(n_parts, |i| i as i32)
        }
    } else {
        v8
    };
    if v3 < n_parts {
        if v3 >= n_hull && (rd_u64(buf, 96) != u64::MAX || rd_u64(buf, 104) != u64::MAX) {
            let part = w.ent(e).parts[v3 as usize].clone();
            let o = part.q.rotate(part.pgeom.origin);
            let wh = &mut wm(w, e).wheels[(v3 - n_hull) as usize];
            wh.q0 = part.q;
            wh.pos0 = part.pos;
            wh.center0 = Vec3::new(o.x + part.pos.x, o.y + part.pos.y, o.z + part.pos.z);
            wh.part_flags = part.flags;
        }
        return v7;
    }
    0
}

fn set_car(w: &mut World, e: EntIdx, b: &[u8]) {
    let gears = image_ptr(b, 88).ok().flatten();
    let d = wm(w, e);
    if let Some(x) = set_f(b, 4) {
        d.axle_friction = x;
    }
    if let Some(x) = set_f(b, 8) {
        d.engine_power = x;
    }
    if let Some(x) = set_f(b, 12) {
        d.max_speed = x;
    }
    let rpm = |x: f32| (f64::from(x) * RPM2W) as f32;
    if let Some(x) = set_f(b, 20) {
        d.engine_max_w = rpm(x);
    }
    if let Some(x) = set_f(b, 16) {
        d.max_steer = x;
    }
    if let Some(x) = set_i(b, 28) {
        d.int_type = x;
    }
    if let Some(x) = set_f(b, 32) {
        d.max_time_step = x;
    }
    if let Some(x) = set_f(b, 36) {
        d.min_energy = x;
    }
    if let Some(x) = set_f(b, 40) {
        d.damping = x;
    }
    if let Some(x) = set_f(b, 44) {
        d.min_brake_friction = x;
    }
    if let Some(x) = set_f(b, 48) {
        d.max_brake_friction = x;
    }
    if let Some(x) = set_f(b, 52) {
        d.k_stabilizer = x;
    }
    if let Some(x) = set_f(b, 60) {
        d.engine_min_w = rpm(x);
    }
    if let Some(x) = set_f(b, 72) {
        let v = rpm(x);
        d.engine_idle_w = v;
        d.w_engine = v;
    }
    if let Some(x) = set_f(b, 76) {
        d.engine_start_w = rpm(x);
    }
    if let Some(x) = set_f(b, 64) {
        d.engine_shift_up_w = rpm(x);
    }
    if let Some(x) = set_f(b, 68) {
        d.engine_shift_down_w = rpm(x);
    }
    if let Some(x) = set_f(b, 72) {
        d.engine_idle_w = rpm(x);
    }
    if let Some(x) = set_f(b, 80) {
        d.clutch_speed = x;
    }
    if let Some(x) = set_i(b, 84) {
        d.n_gears = x;
    }
    if let Some(o) = gears {
        // `+11080[i] = ratios[i]` for i < nGears (no bound: more than 12 would run into the
        // following members; the port stops at the array end)
        let n = d.n_gears.max(0) as usize;
        for i in 0..n {
            let v = b
                .get(o + 4 * i..o + 4 * i + 4)
                .map(|s| f32::from_le_bytes(s.try_into().unwrap_or([0; 4])));
            if i < d.gears.len() {
                if let Some(v) = v {
                    d.gears[i] = v;
                }
            }
        }
    }
    if let Some(x) = set_i(b, 96) {
        d.max_gear = x;
    }
    if let Some(x) = set_i(b, 100) {
        d.min_gear = x;
    }
    if let Some(x) = set_f(b, 112) {
        d.k_dyn_friction = x;
    }
    if let Some(x) = set_f(b, 108) {
        d.gear_dir_switch_w = rpm(x);
    }
    if let Some(x) = set_f(b, 104) {
        d.slip_threshold = x;
    }
    if let Some(x) = set_f(b, 24) {
        d.brake_torque = x;
    }
    if let Some(x) = set_f(b, 116) {
        d.k_steer_tank = if x > 0.0 { 2.0 / x } else { 0.0 };
    }
    if let Some(x) = set_f(b, 120) {
        d.angle_tank = x;
    }
    if let Some(x) = set_f(b, 124) {
        d.min_contact_cos = x;
    }
    if let Some(x) = set_i(b, 128) {
        d.keep_contact = x;
    }
    if let Some(x) = set_f(b, 132) {
        d.engine_braking = x;
    }
    if let Some(x) = set_i(b, 136) {
        d.rear_steer = x;
    }
}

fn set_wheel(w: &mut World, e: EntIdx, b: &[u8]) {
    let nw = n_wheels(w, e);
    let v44 = rd_i32(b, 4);
    let v45 = nw.wrapping_sub(1).wrapping_sub(v44);
    let t = v44.wrapping_add(v45 & (v45 >> 31));
    let idx = (t.wrapping_neg() & (t.wrapping_neg() >> 31)).wrapping_neg();
    // (an entity without wheels writes wheel record 0, as the binary does)
    let i = idx.clamp(0, MAX_WHEELS as i32 - 1) as usize;
    let n_hull = wd(w, e).n_hull;
    if let Some(x) = set_i(b, 52) {
        let pi = (n_hull + idx) as usize;
        if let Some(p) = w.ent_mut(e).parts.get_mut(pi) {
            p.surface_bits = (p.surface_bits & 0xFFFF_FC00) | (x as u32 & 0x3FF);
        }
    }
    let wh = &mut wm(w, e).wheels[i];
    if let Some(x) = set_i(b, 8) {
        wh.flags = (wh.flags & !1) | (x as u32 & 1);
    }
    if let Some(x) = set_i(b, 12) {
        wh.axle = x;
    }
    if let Some(x) = set_i(b, 16) {
        wh.flags = (wh.flags & !2) | (2 * (x as u32 & 1));
    }
    if let Some(x) = set_i(b, 20) {
        wh.flags = (wh.flags & !4) | (4 * (x as u32 & 1));
    }
    if let Some(x) = set_f(b, 24) {
        let lt = x < wh.len_rest;
        wh.len_max = x;
        if lt {
            wh.len_rest = x;
        }
        if x < wh.len {
            wh.len = x;
        }
    }
    if let Some(x) = set_f(b, 28) {
        wh.len_rest = x;
    }
    if let Some(x) = set_f(b, 32) {
        wh.len_min_off = x;
    }
    if let Some(x) = set_f(b, 36) {
        wh.min_friction = x;
    }
    if let Some(x) = set_f(b, 40) {
        wh.max_friction = x;
    }
    if let Some(x) = set_f(b, 44) {
        wh.f164 = x;
    }
    if let Some(x) = set_f(b, 48) {
        wh.f168 = x;
    }
    if let Some(x) = set_i(b, 56) {
        wh.flags = (wh.flags & !8) | (8 * (x as u32 & 1));
    }
    if let Some(x) = set_f(b, 60) {
        wh.k = x;
    }
    if let Some(mut x) = set_f(b, 64) {
        if x <= 0.0 {
            x = -(((wh.k * 4.0) * wh.mass_share).sqrt() * x);
        }
        wh.c = x;
    }
    if let Some(x) = set_f(b, 68) {
        wh.f156 = x;
    }
    if let Some(x) = set_f(b, 72) {
        wh.drive_scale = x;
    }
    if let Some(x) = set_f(b, 76) {
        wh.w = x;
    }
}

// ---------------------------------------------------------------------------------------------
// slot 8 GetParams

/// Slot 8 `GetParams` 0x35104B50: ids 2 (RPM fields ×30/π in double; the max RPM is rounded
/// with the `+12582912` trick; the idle RPM is written twice) and 16 (index out of range → 0);
/// other ids → the rigid slot.
pub fn get_params(w: &mut World, e: EntIdx, buf: &mut [u8]) -> i32 {
    let ty = rd_i32(buf, 0);
    if ty == 2 {
        if buf.len() < ParamsCar::SIZE {
            return 0;
        }
        let Ok(gears) = image_ptr(buf, 88) else {
            return 0;
        };
        let nw = n_wheels(w, e);
        let d = wd(w, e);
        let rpm = |x: f32| (f64::from(x) * W2RPM) as f32;
        wr_f32(buf, 4, d.axle_friction);
        wr_f32(buf, 8, d.engine_power);
        let v4 = rpm(d.engine_max_w);
        wr_f32(buf, 20, crate::math::round_magic(v4) as f32);
        wr_f32(buf, 16, d.max_steer);
        wr_i32(buf, 28, d.int_type);
        wr_f32(buf, 32, d.max_time_step);
        wr_f32(buf, 36, d.min_energy);
        wr_f32(buf, 40, d.damping);
        wr_f32(buf, 44, d.min_brake_friction);
        wr_f32(buf, 48, d.max_brake_friction);
        wr_f32(buf, 52, d.k_stabilizer);
        wr_f32(buf, 60, rpm(d.engine_min_w));
        wr_f32(buf, 72, rpm(d.engine_idle_w));
        wr_f32(buf, 64, rpm(d.engine_shift_up_w));
        wr_f32(buf, 68, rpm(d.engine_shift_down_w));
        wr_f32(buf, 72, rpm(d.engine_idle_w));
        wr_f32(buf, 76, rpm(d.engine_start_w));
        wr_f32(buf, 80, d.clutch_speed);
        wr_i32(buf, 84, d.n_gears);
        if let Some(o) = gears {
            for i in 0..d.n_gears.max(0) as usize {
                if i < d.gears.len() && o + 4 * i + 4 <= buf.len() {
                    wr_f32(buf, o + 4 * i, d.gears[i]);
                }
            }
        }
        wr_i32(buf, 96, d.max_gear);
        wr_i32(buf, 100, d.min_gear);
        wr_f32(buf, 112, d.k_dyn_friction);
        wr_f32(buf, 108, rpm(d.gear_dir_switch_w));
        wr_f32(buf, 104, d.slip_threshold);
        wr_f32(buf, 24, d.brake_torque);
        let v16 = d.k_steer_tank;
        wr_f32(buf, 116, if v16 != 0.0 { 2.0 / v16 } else { 0.0 });
        wr_i32(buf, 56, nw);
        wr_f32(buf, 124, d.min_contact_cos);
        wr_i32(buf, 128, d.keep_contact);
        wr_f32(buf, 132, d.engine_braking);
        wr_i32(buf, 136, d.rear_steer);
        return 1;
    }
    if ty != 16 {
        return crate::entities::interface::get::class_get_params(w, e, buf);
    }
    if buf.len() < 80 {
        return 0;
    }
    let i = rd_i32(buf, 4);
    let nw = n_wheels(w, e);
    if (i as u32) >= nw as u32 {
        return 0;
    }
    let n_hull = wd(w, e).n_hull;
    let surf = w.ent(e).parts[(i + n_hull) as usize].surface_bits;
    let wh = &wd(w, e).wheels[i as usize];
    wr_u32(buf, 8, wh.flags & 1);
    wr_i32(buf, 12, wh.axle);
    wr_u32(buf, 16, u32::from(wh.flags & 2 != 0));
    wr_u32(buf, 20, u32::from(wh.flags & 4 != 0));
    wr_f32(buf, 24, wh.len_max);
    wr_f32(buf, 28, wh.len_rest);
    wr_f32(buf, 36, wh.min_friction);
    wr_f32(buf, 40, wh.max_friction);
    wr_i32(buf, 52, ((surf << 22) as i32) >> 22);
    wr_u32(buf, 56, u32::from(wh.flags & 8 != 0));
    wr_f32(buf, 60, wh.k);
    wr_f32(buf, 64, wh.c);
    wr_f32(buf, 68, wh.f156);
    wr_f32(buf, 72, wh.drive_scale);
    wr_f32(buf, 76, wh.w);
    1
}

/// Typed SetParams id 2: `p` as a byte image plus the gear ratios (`None` = keep the
/// pointer field unused).
pub fn set_params_car(w: &mut World, e: EntIdx, p: &ParamsCar, gears: Option<&[f32]>) -> i32 {
    let mut b = vec![0u8; 512];
    p.write(&mut b);
    let mut b = match gears {
        Some(g) => {
            let o = 256usize;
            b.resize(o + 4 * g.len().max(1) + 4, 0);
            for (i, &x) in g.iter().enumerate() {
                wr_f32(&mut b, o + 4 * i, x);
            }
            wr_u64(&mut b, 88, BUF_PTR_TAG | o as u64);
            b
        }
        None => {
            wr_u64(&mut b, 88, u64::MAX);
            b
        }
    };
    set_params(w, e, &mut b, 1)
}

/// Typed GetParams id 2: the car params and the gear ratios.
pub fn get_params_car(w: &mut World, e: EntIdx) -> Option<(ParamsCar, Vec<f32>)> {
    let n = wd(w, e).n_gears.clamp(0, 12) as usize;
    let mut b = vec![0u8; 512];
    wr_i32(&mut b, 0, 2);
    wr_u64(&mut b, 88, BUF_PTR_TAG | 256);
    if get_params(w, e, &mut b) == 0 {
        return None;
    }
    let g = (0..n).map(|i| rd_f(&b, 256 + 4 * i)).collect();
    wr_u64(&mut b, 88, u64::MAX);
    Some((ParamsCar::read(&b), g))
}

// ---------------------------------------------------------------------------------------------
// slot 9 GetStatus

/// Slot 9 `GetStatus` 0x3510A090.
///
/// * id 1 (pos) of a wheel part (`ip − nHull < nWheels`, an unsigned compare): the part
///   pose is temporarily replaced by the wheel pose `q = (rot(−z, steer) ⊗ rot(−x, angle)) ⊗
///   q0`, `pos = (pos0 + centre0) − (pos0 + q·origin)` lowered by `len − rest`, the rigid slot
///   runs with `flags | 2`, then the part pose and the caller's flags are restored.
/// * ids 4, 5, 15 after the rigid slot returned 0 (see [`status_vehicle`], [`status_wheel`],
///   [`status_abilities`]).
pub fn get_status(w: &mut World, e: EntIdx, buf: &mut [u8]) -> i32 {
    let ty = rd_i32(buf, 0);
    if ty != 1 {
        let r = crate::entities::interface::status::class_get_status(w, e, buf);
        if r != 0 {
            return r;
        }
        return match ty {
            4 if buf.len() >= 60 => status_vehicle(w, e, buf),
            5 if buf.len() >= 144 => status_wheel(w, e, buf),
            15 if buf.len() >= 24 => status_abilities(w, e, buf),
            _ => 0,
        };
    }
    let v4 = rd_u32(buf, 12);
    let mut v5 = rd_i32(buf, 8);
    if v5 == -1 {
        let v7 = rd_i32(buf, 4);
        if v7 >= 0 {
            let parts = &w.ent(e).parts;
            v5 = parts
                .iter()
                .position(|p| p.id == v7)
                .map_or(parts.len() as i32, |i| i as i32);
            if v5 == parts.len() as i32 {
                return 0;
            }
        }
    }
    let n_hull = wd(w, e).n_hull;
    let n_parts = w.ent(e).parts.len() as i32;
    let v145 = v5.wrapping_sub(n_hull);
    let mut saved = None;
    if (v145 as u32) < (n_parts - n_hull) as u32 {
        let (pos, q) = wheel_part_pose(w, e, v5, v145 as usize);
        let p = &mut w.ent_mut(e).parts[v5 as usize];
        saved = Some((p.pos, p.q));
        p.pos = pos;
        p.q = q;
        wr_u32(buf, 12, v4 | 2);
    }
    let r = crate::entities::interface::status::class_get_status(w, e, buf);
    if let Some((pos, q)) = saved {
        let p = &mut w.ent_mut(e).parts[v5 as usize];
        p.pos = pos;
        p.q = q;
    }
    wr_u32(buf, 12, v4);
    r
}

/// The wheel pose GetStatus(pos) substitutes for part `ip` (wheel `iw`).
fn wheel_part_pose(w: &World, e: EntIdx, ip: i32, iw: usize) -> (Vec3, Quat) {
    let wh = &wd(w, e).wheels[iw];
    let v16 = wh.angle * 0.5;
    let v22 = crt::sinf(v16);
    let v23 = crt::cosf(v16);
    let v25 = wh.steer * 0.5;
    let v152 = Quat::new(v22 * -1.0, v22 * 0.0, v22 * 0.0, v23);
    let v26 = crt::sinf(v25);
    let v150 = Quat::new(v26 * 0.0, v26 * 0.0, v26 * -1.0, crt::cosf(v25));
    let v151 = v150.product(v152);
    let q = v151.product(wh.q0);
    let part = &w.ent(e).parts[ip as usize];
    let v29 = q.rotate(part.pgeom.origin);
    let (p0, c0) = (wh.pos0, wh.center0);
    let v30 = p0.x + v29.x;
    let v31 = p0.y + v29.y;
    let v32 = p0.x + c0.x;
    let v33 = p0.y + c0.y;
    let v36 = (p0.z + c0.z) - (p0.z + v29.z);
    let z = v36 - (wh.len - wh.len_rest);
    (Vec3::new(v32 - v30, v33 - v31, z), q)
}

/// GetStatus id 4 (vehicle): steering, pedal, handbrake, footbrake, the brake-pedal value
/// (`|pedal|` unless the pedal pushes in the gear's direction), the body velocity, the number
/// of wheels in contact, gear, engine RPM (×30/π in double), clutch, drive torque (`+52` as a
/// qword: `+56` = 0) and `+56` = colliders with a non-negative id byte, simclass ≠ 0 and a type
/// other than 6.
fn status_vehicle(w: &mut World, e: EntIdx, buf: &mut [u8]) -> i32 {
    let nw = n_wheels(w, e);
    let body = w.bodies.get(w.ent(e).rigid().expect("rigid").body).v;
    let d = wd(w, e);
    wr_f32(buf, 4, d.steer);
    wr_f32(buf, 8, d.pedal);
    wr_i32(buf, 12, d.handbrake);
    wr_i32(buf, 16, d.footbrake);
    let g = d.cur_gear;
    let s = ((g.wrapping_sub(2) >> 31) + (g.wrapping_sub(1) >> 31) + 1) as f32;
    wr_f32(
        buf,
        20,
        if s * d.pedal > 0.0 {
            0.0
        } else {
            d.pedal.abs()
        },
    );
    wr_f32(buf, 24, body.x);
    wr_f32(buf, 28, body.y);
    wr_f32(buf, 32, body.z);
    wr_i32(buf, 36, 0);
    let mut n = 0;
    for i in 0..nw.max(0) as usize {
        n += i32::from(d.wheels[i].flags & wflags::CONTACT != 0);
        wr_i32(buf, 36, n);
    }
    wr_f32(buf, 44, (f64::from(d.w_engine) * W2RPM) as f32);
    wr_i32(buf, 40, g);
    wr_f32(buf, 48, d.clutch);
    wr_u64(buf, 52, u64::from(d.drive_torque.to_bits()));
    let cols = w.ent(e).colliders.clone();
    for c in cols {
        let ce = w.ent(c);
        if (ce.id_bits as i32) >= 0 && ce.id_bits & 0xFF00_0000 != 0 && ce.etype != 6 {
            let v = rd_i32(buf, 56);
            wr_i32(buf, 56, v + 1);
        }
    }
    1
}

/// GetStatus id 5 (wheel; by index `+4`, or by part id `+8`): pose, contact point / normal,
/// ω, slip flag and the slip velocity (the contact slip velocity minus the rolling velocity
/// `ω·r` along `normalize(n × (cos steer, −sin steer, 0))` rotated into the world, projected
/// onto the contact plane; zero without contact or while asleep), surfaces, friction (`+92`
/// = the solver contact's friction), lengths, radius, torque, steering, rotation angle, the
/// ground entity (`+128`, the port writes `EntIdx + 1`, 0 for none) and the mass share.
fn status_wheel(w: &mut World, e: EntIdx, buf: &mut [u8]) -> i32 {
    let nw = n_wheels(w, e);
    let n_hull = wd(w, e).n_hull;
    let v53 = rd_i32(buf, 8);
    if v53 != UI {
        wr_i32(buf, 4, 0);
        if nw > 0 {
            loop {
                let v55 = rd_i32(buf, 4);
                if w.ent(e).parts[(v55 + n_hull) as usize].id == v53 {
                    break;
                }
                wr_i32(buf, 4, v55 + 1);
                if v55 + 1 >= nw {
                    break;
                }
            }
        }
        if rd_i32(buf, 4) >= nw {
            return 0;
        }
    }
    let i = rd_i32(buf, 4);
    if (i as u32) >= nw as u32 {
        return 0;
    }
    let ent_q = w.ent(e).q;
    let awake = w.ent(e).rigid().expect("rigid").awake();
    let wh = wd(w, e).wheels[i as usize].clone();
    let friction = w.contacts.get(wh.contact).friction;
    wr_f32(buf, 68, wh.w);
    for (k, x) in [wh.pos.x, wh.pos.y, wh.pos.z].into_iter().enumerate() {
        wr_f32(buf, 16 + 4 * k, x);
    }
    for (k, x) in wh.q.to_array().into_iter().enumerate() {
        wr_f32(buf, 28 + 4 * k, x);
    }
    let contact = wh.flags & wflags::CONTACT != 0;
    wr_u32(buf, 12, u32::from(contact));
    if contact {
        wr_f32(buf, 44, wh.pt.x);
        wr_f32(buf, 48, wh.pt.y);
        wr_f32(buf, 52, wh.pt.z);
        wr_u32(buf, 72, u32::from(wh.flags & wflags::SLIP != 0));
        wr_f32(buf, 76, wh.vslip.x);
        wr_f32(buf, 80, wh.vslip.y);
        wr_f32(buf, 84, wh.vslip.z);
        let v64 = crt::cosf(wh.steer);
        let v65 = crt::sinf(wh.steer);
        let n = wh.n;
        let v69 = -v65;
        let v70 = (-v65 * n.x) - (v64 * n.y);
        let v71 = (n.y * 0.0) - (v69 * n.z);
        let v72 = (v64 * n.z) - (n.x * 0.0);
        let v73 = (v72 * v72 + v71 * v71) + v70 * v70;
        let a = if v73 <= 9.999_999_7e-21 {
            Vec3::new(0.0, 0.0, 1.0)
        } else {
            let v74 = 1.0 / v73.sqrt();
            Vec3::new(v71 * v74, v72 * v74, v70 * v74)
        };
        let v75 = ent_q.rotate(a);
        let v76 = wh.w * wh.r;
        let v77 = v75.y * v76;
        let v78 = v75.z * v76;
        let s0 = rd_f(buf, 76) - v76 * v75.x;
        wr_f32(buf, 76, s0);
        let s1 = rd_f(buf, 80) - v77;
        wr_f32(buf, 80, s1);
        let s2 = rd_f(buf, 84) - v78;
        wr_f32(buf, 84, s2);
        wr_f32(buf, 56, n.x);
        wr_f32(buf, 60, n.y);
        wr_f32(buf, 64, n.z);
        let v82 = (n.y * s1 + n.x * s0) + n.z * s2;
        wr_f32(buf, 76, s0 - n.x * v82);
        wr_f32(buf, 84, s2 - n.z * v82);
        wr_f32(buf, 80, s1 - n.y * v82);
    } else {
        wr_u64(buf, 48, 0);
        wr_i32(buf, 44, 0);
        wr_u64(buf, 80, 0);
        wr_i32(buf, 76, 0);
        wr_u64(buf, 60, 0);
        wr_i32(buf, 56, 0);
        wr_i32(buf, 72, 0);
    }
    if !awake {
        wr_i32(buf, 68, 0);
        wr_u64(buf, 80, 0);
        wr_i32(buf, 76, 0);
    }
    wr_i32(buf, 88, wh.surf[1]);
    wr_f32(buf, 100, wh.len);
    wr_f32(buf, 104, wh.len_max);
    wr_f32(buf, 108, wh.len_rest);
    wr_f32(buf, 112, wh.r);
    wr_u64(
        buf,
        128,
        if wh.contact_ent.is_none() {
            0
        } else {
            u64::from(wh.contact_ent.0) + 1
        },
    );
    wr_f32(buf, 92, friction);
    wr_f32(buf, 96, wh.f232);
    wr_f32(buf, 116, wh.brake_torque);
    wr_f32(buf, 120, wh.steer);
    wr_f32(buf, 124, wh.angle);
    wr_f32(buf, 136, wh.mass_share);
    1
}

/// GetStatus id 15 (abilities): `+4` steering angle (set) → `+8` the turning centre (world):
/// on the rear axle line (the rearmost wheel per side, entity frame) at `Δx / tan(steer)`
/// sideways; `+20` the maximal speed `r·ω` where the engine torque balances axle friction and
/// the quadratic drag `r²ω²·damping·M` (bisection over `[0.01, 1]·ωmax`, ≤ 256 steps, until
/// the interval is below `0.005·ωmax`; 0 without driving wheels).
fn status_abilities(w: &mut World, e: EntIdx, buf: &mut [u8]) -> i32 {
    let nw = n_wheels(w, e);
    let (pos, q) = (w.ent(e).pos, w.ent(e).q);
    let b = *w.bodies.get(w.ent(e).rigid().expect("rigid").body);
    let d = wd(w, e);
    if let Some(steer) = set_f(buf, 4) {
        let v139 = Vec3::new(b.pos.x - pos.x, b.pos.y - pos.y, b.pos.z - pos.z);
        let c = crate::entities::base::geometry::rotate_inv(q, v139);
        let (v91, v90) = (c.x, c.y);
        // `{v135, v136[0..5]}`: per side (0 = pivot behind the CoM, 1 = in front) the last
        // wheel on the inner side of the turn; a side without such a wheel keeps the caller's
        // uninitialised stack in the binary (the port uses zero)
        let mut side = [[0.0f32; 3]; 2];
        for i in 0..nw.max(0) as usize {
            let p = d.wheels[i].pivot;
            if (p.x - v91) * steer > 0.0 {
                let k = ((v90 - p.y).to_bits() >> 31) as usize;
                side[k] = [p.x, p.y, p.z];
            }
        }
        let v107 = side[1][1] - side[0][1];
        let sb = steer.to_bits() as i32;
        let sgn = ((sb.wrapping_sub(1) >> 31) + (sb >> 31) + 1) as f32;
        // `v135` (side 0's x) is overwritten: the centre is (v135, side0.y, side0.z)
        let t = Vec3::new(
            (v107 / crt::tanf(steer)) * sgn + side[1][0],
            side[0][1],
            side[0][2],
        );
        let r = q.rotate(t);
        wr_f32(buf, 8, pos.x + r.x);
        wr_f32(buf, 12, r.y + pos.y);
        wr_f32(buf, 16, r.z + pos.z);
    }
    let mut v122 = 0;
    for i in 0..nw.max(0) as usize {
        v122 += (d.wheels[i].flags & 1) as i32;
    }
    if v122 <= 0 {
        wr_f32(buf, 20, 0.0);
        return 1;
    }
    let v138 = d.engine_max_w;
    let mut lim = [v138 * 0.009_999_999_8, v138]; // v137, v138
    let v123 = d.wheels[0].r;
    let v124 = d.engine_power;
    let v126 = d.axle_friction;
    let v127 = d.damping;
    let v128 = b.m;
    let v129 = v138 * 0.004_999_999_9;
    let v130 = v122 as f32;
    let v131 = (std::f64::consts::PI / f64::from(v138)) as f32;
    let mut v125 = v138;
    let mut v115 = lim[0];
    let mut v132;
    let mut it = 0;
    loop {
        v132 = (v125 + v115) * 0.5;
        let f = ((crt::sinf(v132 * v131) * v124) - v126 * v132) * v130
            - (((v123 * v132) * (v123 * v132)) * v127) * v128;
        lim[(f.to_bits() >> 31) as usize] = v132;
        v125 = lim[1];
        v115 = lim[0];
        if lim[1] - lim[0] <= v129 {
            break;
        }
        it += 1;
        if it >= 256 {
            break;
        }
    }
    wr_f32(buf, 20, v123 * v132);
    1
}

// ---------------------------------------------------------------------------------------------
// slot 10 Action

/// Slot 10 `Action` 0x351094A0.
///
/// * id 5 → the rigid slot directly (no wheeled lock-or-queue).
/// * Otherwise the lock-or-queue (a queued call returns the slot-76 value), then
///   `CRigidEntity::Action(a, 1)`; when that returned 0, id 3 (drive) is executed and the
///   call **returns 0**; other ids return 0.
/// * id 4 handled by the rigid slot: wheel ω (and `+216`, `+220`) cleared, pedal 0,
///   hull-contact timer 10.
///
/// Drive: `dpedal` adds to the pedal (clamped to ±1), `pedal`, `clutch` and `gear` overwrite;
/// steering (`steer` and/or `dsteer`) is clamped to ±maxSteer; with car steering (`+11144 ==
/// 0`) the wheel angles follow ([`ackermann`]) and every wheel part's pose is rebuilt
/// ([`rebuild_wheel_parts`]). Then handbrake / footbrake; a non-zero pedal (or a released
/// handbrake) wakes a sleeping vehicle (awake byte, `+608 ≥ 1`, simclass 2 with a
/// re-listing); the idle timer `+252` is reset.
pub fn action(w: &mut World, e: EntIdx, buf: &mut [u8], ts: i32) -> i32 {
    if buf.len() < 4 {
        return 0;
    }
    let ty = rd_i32(buf, 0);
    if ty == 5 {
        return crate::entities::interface::action::class_action(w, e, buf, ts);
    }
    if ty == 3 && buf.len() < 36 {
        return 0;
    }
    if let Gate::Skip { queued } = action_gate(w, e, buf, ts) {
        return crate::entities::interface::action::slot76(w, e, buf, queued);
    }
    let v7 = crate::entities::interface::action::class_action(w, e, buf, 1);
    if v7 == 0 {
        if ty != 3 {
            return 0;
        }
        drive(w, e, buf);
        return 0;
    }
    if ty == 4 {
        let nw = n_wheels(w, e);
        let d = wm(w, e);
        for i in 0..nw.max(0) as usize {
            let wh = &mut d.wheels[i];
            wh.w = 0.0;
            wh.f216 = 0.0;
            wh.brake_torque = 0.0;
        }
        d.pedal = 0.0;
        d.time_no_hull_contact = 10.0;
    }
    v7
}

fn drive(w: &mut World, e: EntIdx, b: &[u8]) {
    {
        let d = wm(w, e);
        if let Some(dp) = set_f(b, 8) {
            let mut v10 = d.pedal + dp;
            if v10 >= -1.0 {
                if v10 > 1.0 {
                    v10 = 1.0;
                }
            } else {
                v10 = -1.0;
            }
            d.pedal = v10;
        }
        if let Some(p) = set_f(b, 4) {
            d.pedal = p;
        }
        if let Some(c) = set_f(b, 20) {
            d.clutch = c;
        }
        if let Some(g) = set_i(b, 32) {
            d.cur_gear = g;
        }
    }
    let steer_set = set_f(b, 12);
    let dsteer = set_f(b, 16);
    if steer_set.is_some() || dsteer.is_some() {
        let d = wm(w, e);
        if let Some(s) = steer_set {
            d.steer = s;
        }
        if let Some(ds) = dsteer {
            d.steer += ds;
        }
        let mut v12 = d.max_steer;
        let mut v13 = -v12;
        if -v12 <= d.steer {
            v13 = d.steer;
        }
        if v12 >= v13 {
            v12 = v13;
        }
        let tank = d.k_steer_tank == 0.0;
        d.steer = v12;
        if tank {
            if v12 == 0.0 {
                let nw = n_wheels(w, e);
                for i in 0..nw.max(0) as usize {
                    wm(w, e).wheels[i].steer = 0.0;
                }
            } else {
                ackermann(w, e);
            }
            rebuild_wheel_parts(w, e);
        }
    }
    let (v80, pedal) = {
        let d = wm(w, e);
        let v80 = d.handbrake;
        if let Some(h) = set_i(b, 24) {
            d.handbrake = h;
        }
        if let Some(f) = set_i(b, 28) {
            d.footbrake = f;
        }
        (v80, d.pedal)
    };
    let hb = wd(w, e).handbrake;
    let awake = w.ent(e).rigid().expect("rigid").awake();
    if (pedal != 0.0 || (hb == 0 && v80 != 0)) && !awake {
        let id_bits = w.ent(e).id_bits;
        {
            let r = w.ent_mut(e).rigid_mut().expect("rigid");
            r.set_awake(true);
            let v82 = r.min_awake_time;
            r.min_awake_time = ((v82 - 1.0).abs() + (v82 + 1.0)) * 0.5;
        }
        if ((id_bits & 0xFF00_0000) as i32) < 0x200_0000 {
            w.ent_mut(e).set_sim_class(2);
            w.reposition_entity(e, 2, None);
        }
    }
    w.ent_mut(e).time_idle = 0.0;
}

/// The Ackermann steering of action drive: with `rear_steer == 1` the stored steering angle is
/// negated first (**every call**; kept from the binary). In the entity frame relative to the
/// centre of mass: per side (`x` of the pivot vs. the CoM) the rearmost axle wheel behind the
/// CoM defines the rear axle line; every axle wheel in front of the CoM on the inner side of
/// the turn gets the full angle, and the wheels on the other side within 5 % of its distance to
/// the rear axle (same `y`) get `atan(d·tanθ / (|(a − a₀)·tanθ| + d₀))`.
fn ackermann(w: &mut World, e: EntIdx) {
    let nw = n_wheels(w, e);
    let (pos, q) = (w.ent(e).pos, w.ent(e).q);
    let bpos = w.bodies.get(w.ent(e).rigid().expect("rigid").body).pos;
    let rear = wd(w, e).rear_steer;
    if rear == 1 {
        let d = wm(w, e);
        d.steer *= -1.0;
    }
    let v102 = Vec3::new(bpos.x - pos.x, bpos.y - pos.y, bpos.z - pos.z);
    let c = crate::entities::base::geometry::rotate_inv(q, v102);
    let steer = wd(w, e).steer;
    let v20 = crt::tanf(steer);
    let (v21, v22, _v23) = (c.x, c.y, c.z);
    // (v85, v86, v87) side 0, (v88, v89, v90) side 1
    let mut s = [[c.x, c.y, c.z], [c.x, c.y, c.z]];
    for i in 0..nw.max(0) as usize {
        let wh = &wd(w, e).wheels[i];
        let p = wh.pivot;
        let k = ((v21 - p.x).to_bits() >> 31) as usize;
        let mut v34 = s[k][1] > p.y;
        if rear == 1 {
            v34 = !v34;
        }
        if wh.axle >= 0 && v34 {
            s[k] = [p.x, p.y, p.z];
        }
    }
    let v35 = s[1][0] - s[0][0];
    let v36 = s[1][1] - s[0][1];
    let v37 = s[1][2] - s[0][2];
    let v38 = (v35 * v35 + v36 * v36) + v37 * v37;
    let (v42, v40, v41) = if v38 <= 9.999_999_7e-21 {
        (0.0, 0.0, 1.0)
    } else {
        let v39 = v38.sqrt();
        ((1.0 / v39) * v35, v36 * (1.0 / v39), v37 * (1.0 / v39))
    };
    let (v27, v28, v29) = (s[0][0], s[0][1], s[0][2]);
    for i in 0..nw.max(0) as usize {
        let wh = wd(w, e).wheels[i].clone();
        let p = wh.pivot;
        let mut v45 = v22 < p.y;
        if wd(w, e).rear_steer == 1 {
            v45 = v22 >= p.y;
        }
        if !(wh.axle >= 0 && v45) {
            continue;
        }
        let v46 = wd(w, e).steer;
        if !((p.x - v21) * v46 > 0.0) {
            continue;
        }
        wm(w, e).wheels[i].steer = v46;
        let v48 = p.y - v28;
        let v49 = p.z - v29;
        let v50 = p.x - v27;
        let cx = v48 * v41 - v49 * v40;
        let cy = v49 * v42 - v41 * v50;
        let cz = v40 * v50 - v48 * v42;
        let v51 = ((cx * cx) + (cy * cy) + (cz * cz)).sqrt();
        let v52 = (v50 * v42 + v48 * v40) + v49 * v41;
        for j in 0..nw.max(0) as usize {
            let pj = wd(w, e).wheels[j].pivot;
            let st = wd(w, e).steer;
            if (pj.y - p.y).abs() < v51 * 0.050_000_001 && (pj.x - v21) * st < 0.0 {
                let v54 = pj.x - v27;
                let v55 = pj.y - v28;
                let v56 = pj.z - v29;
                let a = v55 * v41 - v56 * v40;
                let bb = v56 * v42 - v41 * v54;
                let cc = v40 * v54 - v55 * v42;
                let num = ((a * a) + (bb * bb) + (cc * cc)).sqrt() * v20;
                let den = (((v54 * v42 + v55 * v40) + v56 * v41) - v52) * v20;
                wm(w, e).wheels[j].steer = crt::atanf(num / (den.abs() + v51));
            }
        }
    }
}

/// Every wheel part gets `q = rot(−z, steer) ⊗ q0` and `pos = (pos0 + centre0) − (pos0 +
/// q·origin)` lowered by `len − rest`; the wheel record copies both.
fn rebuild_wheel_parts(w: &mut World, e: EntIdx) {
    let nw = n_wheels(w, e);
    let n_hull = wd(w, e).n_hull;
    for i in 0..nw.max(0) as usize {
        let wh = wd(w, e).wheels[i].clone();
        let v63 = wh.steer * 0.5;
        let v64 = crt::sinf(v63);
        let v65 = crt::cosf(v63);
        let v103 = Quat::new(v64 * 0.0, v64 * 0.0, v64 * -1.0, v65);
        let q = v103.product(wh.q0);
        let pi = n_hull as usize + i;
        let origin = w.ent(e).parts[pi].pgeom.origin;
        w.ent_mut(e).parts[pi].q = q;
        let v73 = q.rotate(origin);
        let v74 = wh.pos0.x + v73.x;
        let v75 = wh.pos0.x + wh.center0.x;
        let v76 = (wh.pos0.z + wh.center0.z) - (wh.pos0.z + v73.z);
        let v77 = (wh.pos0.y + wh.center0.y) - (wh.pos0.y + v73.y);
        let pos = Vec3::new(v75 - v74, v77, v76 - (wh.len - wh.len_rest));
        w.ent_mut(e).parts[pi].pos = pos;
        let r = &mut wm(w, e).wheels[i];
        r.q = q;
        r.pos = pos;
    }
}
