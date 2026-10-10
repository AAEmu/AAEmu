//! Slot 79 of `CWheeledVehicleEntity` (0x351074A0; x86 1.2 0x351142F0) — drivetrain,
//! suspension and tyre impulses — and the engine `sub_351057C0` ([`engine`]).
//!
//! Called by `CRigidEntity::Step` with the step's `dt` once the momenta of the step are final
//! (after gravity, buoyancy and the external forces; `UpdateState` already ran), before the
//! collision accumulators are cleared.
//!
//! 1. Colliders: every wheel's ground entity is (re)added as a collider; colliders that are
//!    not the ground of a wheel, have no contact or constraint with the vehicle and report no
//!    contact back (slot 50) are removed on both sides (the per-thread `+64` bit marks the
//!    wheel grounds meanwhile).
//! 2. `+11212 = 0`; the engine; "braking" = the pedal pushes against the gear (or any pedal
//!    in neutral).
//! 3. Per wheel: brake torque = −sgn(ω)·axleFriction (+ `|pedal|`·−sgn(ω)·brakeTorque when
//!    braking); driving wheels add the drive torque × drive scale (tank steering: the inner
//!    side's torque `max(|T|, brakeTorque)·sgnnz(T)·max(1 − |steer·k|, −1)·sgn(ωmax − ωeng)`
//!    when the pedal does not oppose the rotation); blocked / handbraked wheels stop; the
//!    rotation angle integrates (wrapped at ±2π in double); "free" flag 0x20 = not in contact.
//! 4. Suspension (wheels in contact on an axle): `N = max(0, ((lenMax − max(len, rest −
//!    minOff))·k − v_up·c − (len − len_partner)·k·stabilizer)·dt)`.
//! 5. Tyres (in contact, or tank-driven): the suspension impulse along the hull's up axis
//!    plus a longitudinal impulse along `n × axle`: grip `μ` from the surfaces (mean of the
//!    two static frictions, clamped by the wheel's min/max and — braking — the min/max
//!    braking friction), ×k_dyn when slipping (`|v_slip − ω·r·roll| > slipThreshold`), a load
//!    factor, a low-speed boost below 3 m/s; the impulse cap `N·μ` and the wheel ω update
//!    (slip: the cap with the sign of the drive impulse or of `ω_roll − ω`, ω chases the
//!    rolling speed limited by the engine's maximum or the brake; grip: ω moves towards the
//!    rolling speed by at most `cap·r·Iinv`). The lateral friction goes through the inline
//!    solver contact (axis, impulse limit `pn`, friction). Light grounds get the reaction
//!    impulse (`Action(impulse)` with `iApplyTime 2`, source 3).
//! 6. Tank steering copies ω / free flag from the side's driving wheel.
//! 7. `P += ΣF`, `v = P·Minv` (clamped to the max speed, `P = v·M`), `L += Στ`, `w = Iinv·L`;
//!    the load display values.

#![allow(clippy::neg_cmp_op_on_partial_ord, clippy::too_many_lines, clippy::excessive_precision)]

use super::{n_wheels, wd, wflags, wm};
use crate::math::{crt, Mat33, Vec3};
use crate::world::{EntIdx, World};

/// `sgn(x)` of the bits as the binary computes it: `((b − 1) >> 31) + (b >> 31) + 1`
/// (arithmetic shifts): 1, 0 (for ±0... `+0` only; `−0` gives 0 too), −1.
#[inline]
fn sgn_bits(x: f32) -> i32 {
    let b = x.to_bits() as i32;
    (b.wrapping_sub(1) >> 31) + (b >> 31) + 1
}

/// `−sgn` form `−1 − ((b − 1) >> 31) − (b >> 31)`.
#[inline]
fn neg_sgn_bits(x: f32) -> i32 {
    let b = x.to_bits() as i32;
    -1 - (b.wrapping_sub(1) >> 31) - (b >> 31)
}

/// `sgnnz(x)`: `((b >> 30) & ~1) + 1` (arithmetic shift): ±1.
#[inline]
fn sgnnz_bits(x: f32) -> i32 {
    (((x.to_bits() as i32) >> 30) & !1) + 1
}

/// `sub_351057C0(dt)` → the drive torque (`+11228`; also returned in `xmm0`, 0 without
/// gears).
///
/// * Tank steering (`+11144`): the inner side's wheels count `1/max(|1 − |steer·k||` clamped
///   to ≥ −1, ±0.05)` times.
/// * The engine target ω = max over the driving axle wheels of `side factor · ω · drive
///   scale · ratio(gear)`; the drive scale sum `Σ` gives the torque `P / max(Σ, 0.001)`.
/// * In gear: the engine follows the target by the clutch; above the stall ω and without the
///   handbrake the clutch engages at the clutch speed (fully engaged → engine = target).
/// * Clutch engaged but below the stall ω: stalls into neutral (when nearly stopped along the
///   hull's y axis and the pedal opposes the gear), engine ω = idle.
/// * Auto shift (clutch > 0.99, gear > 1, ≥ 2 wheels in contact): up above the shift-up ω
///   (≤ nGears − 1), down below the shift-down ω (when not accelerating or above 2nd gear);
///   clamped to [min, max]; a gear change disengages the clutch; in neutral / disengaged the
///   engine returns to idle.
/// * Torque `T = max(sin(min(ω, 1.5ωmax)/ωmax·π)·P', −0.1·P') / ω` (double `sin`) plus the
///   engine braking `max(0, ½·P/ωmax·k·(ω − idle)/ωmax)`, applied by `|pedal|` only when the
///   pedal pushes in the gear's direction, minus the braking, × ratio × clutch.
pub fn engine(w: &mut World, e: EntIdx, dt: f32) -> f32 {
    let nw = n_wheels(w, e);
    let (qn, bv) = {
        let r = w.ent(e).rigid().expect("rigid");
        (r.q_new, w.bodies.get(r.body).v)
    };
    let d = wm(w, e);
    let v2 = d.n_gears;
    if v2 == 0 {
        return 0.0;
    }
    let v5 = d.k_steer_tank;
    let mut v37 = [1.0f32; 2];
    let mut v6 = 0.0f32;
    if v5 != 0.0 {
        let v7 = d.steer;
        if v7.abs() > 0.009_999_999_8 {
            let v8 = (v7.to_bits() >> 31) as usize;
            let v9 = 1.0 - (v7 * v5).abs();
            let mut v10 = ((-1.0 - v9).abs() + (v9 - 1.0)) * 0.5;
            v37[v8] = v10;
            if v10.abs() < 0.050_000_001 {
                v10 = sgnnz_bits(v10) as f32 * 0.050_000_001;
                v37[v8] = v10;
            }
            v37[v8] = 1.0 / v10;
        }
    }
    let mut v11 = 0.0f32;
    let ratio = d.gears[d.cur_gear.clamp(0, 11) as usize];
    for i in 0..nw.max(0) as usize {
        let wh = &d.wheels[i];
        if wh.axle >= 0 && wh.flags & 1 != 0 {
            let k = (wh.pivot.x.to_bits() >> 31) as usize;
            let v16 = ((v37[k] * wh.w) * wh.drive_scale) * ratio;
            v6 = ((v6 - v16).abs() + (v16 + v6)) * 0.5;
            v11 += ((wh.flags & 1) as f32) * wh.drive_scale;
        }
    }
    let v18 = d.cur_gear;
    let v19 = d.engine_power;
    let v20 = (1.0 / (((0.001 - v11).abs() + (v11 + 0.001)) * 0.5)) * v19;
    if v18 != 1 {
        let v21 = d.clutch;
        if v21 > 0.0 {
            d.w_engine = ((v6 - d.w_engine) * v21) + d.w_engine;
        }
        if d.w_engine.abs() > d.engine_min_w && d.handbrake == 0 {
            let v22 = (dt * d.clutch_speed) + v21;
            d.clutch = v22;
            if v22 > 1.0 {
                d.w_engine = v6;
                d.clutch = 1.0;
            }
        }
    }
    if d.clutch > 0.0 && d.engine_min_w > d.w_engine {
        let (qx, qy, qz, qw) = (qn.x, qn.y, qn.z, qn.w);
        let a = (((qw * qw) + (qy * qy)) * 2.0 - 1.0) * bv.y;
        let b = ((qx * qy) - (qz * qw)) * 2.0 * bv.x;
        let c = ((qw * qx) + (qz * qy)) * 2.0 * bv.z;
        if ((a + b) + c).abs() < 1.0 && ((v18 - 1) as f32) * d.pedal < 0.0 {
            d.cur_gear = 1;
            d.clutch = 0.0;
        }
        d.w_engine = d.engine_idle_w;
    }
    let mut v24 = d.cur_gear;
    let v25 = (0..nw.max(0) as usize)
        .filter(|&i| d.wheels[i].flags & wflags::CONTACT != 0)
        .count() as i32;
    if d.clutch > 0.990_000_01 && v24 > 1 && v25 > 1 {
        let v29 = d.w_engine;
        if v29 <= d.engine_shift_up_w {
            if v29 < d.engine_shift_down_w && (d.pedal <= 0.0 || v24 > 2) {
                v24 = v24 - 1 - ((v24 - 2) & ((v24 - 2) >> 31));
            }
        } else {
            let t = v24 + 1 - (v2 - 1);
            v24 = v2 - 1 + (t & (t >> 31));
        }
    }
    let t = d.max_gear - v24;
    let v30 = v24 + (t & (t >> 31));
    let t = d.min_gear - v30;
    let v31 = d.min_gear - (t & (t >> 31));
    if v31 != d.cur_gear {
        d.clutch = 0.0;
    }
    if v31 == 1 || d.clutch == 0.0 || d.clutch.is_nan() {
        d.w_engine = ((d.engine_idle_w - d.w_engine) * dt) + d.w_engine;
    }
    let v32 = d.engine_max_w;
    let v33 = d.w_engine;
    d.cur_gear = v31;
    let inv = 1.0 / v32;
    let v34 = (((v19 * inv) * 0.5) * d.engine_braking) * ((v33 - d.engine_idle_w) * inv);
    let v35 = ((-v34).abs() + v34) * 0.5;
    let m15 = v32 * 1.5;
    let arg = ((((v33 + m15) - (v33 - m15).abs()) * 0.5) * inv) as f64 * std::f64::consts::PI;
    let v36 = (crt::sin(arg) * f64::from(v20)) as f32;
    let k01 = v20 * -0.1;
    let s = (-1 - ((v31 - 2) >> 31) - ((v31 - 1) >> 31)) as f32;
    let bit = ((s * d.pedal).to_bits() >> 31) as f32;
    let ratio = d.gears[v31.clamp(0, 11) as usize];
    let t = (((((v36 - k01).abs() + (k01 + v36)) * 0.5) / v33 + v35) * (d.pedal.abs() * bit) - v35)
        * (ratio * d.clutch);
    d.drive_torque = t;
    t
}

/// Slot 79 0x351074A0 (see the module docs).
pub fn step_drive(w: &mut World, e: EntIdx, dt: f32) {
    let tls = w.tls_thread.clamp(0, 4) as u32;
    let v10 = 1u32.rotate_left(tls);
    let (qn, pn, body_id) = {
        let r = w.ent(e).rigid().expect("rigid");
        (r.q_new, r.pos_new, r.body)
    };
    let m = Mat33::from_quat(qn).to_array();
    let (v245, v246, v247, v248, v249, v250, v251, v252, v253) =
        (m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);
    let v312 = ((v246 + v245) * 0.0) + v247;
    let v310 = ((v248 + v249) * 0.0) + v250;
    let v258 = ((v251 + v252) * 0.0) + v253;
    // 1. collider housekeeping
    let nw = n_wheels(w, e);
    for i in 0..nw.max(0) as usize {
        let (fl, ce) = {
            let wh = &wd(w, e).wheels[i];
            (wh.flags, wh.contact_ent)
        };
        if fl & wflags::CONTACT != 0 {
            crate::entities::add_collider(w, e, ce);
            let c = w.ent_mut(ce);
            if c.processed & v10 == 0 {
                c.processed = c.processed.wrapping_add(v10);
            }
        }
    }
    let ncol = w.ent(e).colliders.len() as i32;
    let mut i = ncol - 1;
    while i >= 0 {
        let iu = i as usize;
        let c = w.ent(e).colliders[iu];
        let r = w.ent(e).rigid().expect("rigid");
        if w.ent(c).processed & v10 == 0
            && r.collider_contacts[iu].is_none()
            && r.collider_masks[iu] == 0
            && !crate::entities::has_contacts_with(w, c, e)
        {
            crate::entities::remove_collider(w, c, e, true);
            crate::entities::remove_collider(w, e, c, true);
        }
        i -= 1;
    }
    for i in 0..n_wheels(w, e).max(0) as usize {
        let (fl, ce) = {
            let wh = &wd(w, e).wheels[i];
            (wh.flags, wh.contact_ent)
        };
        if fl & wflags::CONTACT != 0 && w.ent(ce).processed & v10 != 0 {
            let c = w.ent_mut(ce);
            c.processed = c.processed.wrapping_add((-1i32 << tls) as u32);
        }
    }
    wm(w, e).n_hull_contacts = 0;
    // 2. engine, braking
    let v23 = engine(w, e, dt);
    let v24 = {
        let d = wd(w, e);
        let v21 = d.pedal;
        let mut v24 = true;
        if v21 == 0.0 || v21.is_nan() || d.cur_gear != 1 {
            let ratio = d.gears[d.cur_gear.clamp(0, 11) as usize];
            let p = (sgn_bits(ratio) as f32) * v21;
            if !(p < 0.0 || p.is_nan()) {
                v24 = false;
            }
        }
        v24
    };
    // 3. wheel torques and rotation
    let nw = n_wheels(w, e);
    let mut v25 = true;
    {
        let d = wm(w, e);
        let (tank, steer, pedal, bt, af, maxw, weng, hb) = (
            d.k_steer_tank,
            d.steer,
            d.pedal,
            d.brake_torque,
            d.axle_friction,
            d.engine_max_w,
            d.w_engine,
            d.handbrake,
        );
        for i in 0..nw.max(0) as usize {
            let wh = &mut d.wheels[i];
            let v28 = wh.w;
            let v29 = (neg_sgn_bits(v28) as f32) * af;
            wh.brake_torque = v29;
            if v24 {
                wh.brake_torque = (pedal.abs() * ((neg_sgn_bits(v28) as f32) * bt)) + v29;
            }
            let v31 = wh.brake_torque;
            let v32 = wh.flags;
            wh.w_prev = v28;
            wh.brake_impulse = v31 * dt;
            if v32 & 1 != 0 {
                let mut v34 = v23;
                if !(tank == 0.0 || tank.is_nan()) && steer.abs() > 0.009_999_999_8 && steer * wh.pivot.x > 0.0 {
                    let p = v28 * pedal;
                    if !(p < 0.0 || p.is_nan()) {
                        let v36 = v23.abs();
                        v34 = (((bt - v36).abs() + (bt + v36)) * 0.5) * sgnnz_bits(v23) as f32;
                    }
                    let v37 = 1.0 - (steer * tank).abs();
                    let v239 = maxw - weng;
                    v34 = ((((-1.0 - v37).abs() + (v37 - 1.0)) * 0.5) * v34) * sgn_bits(v239) as f32;
                }
                wh.brake_torque = (v34 * wh.drive_scale) + v31;
            }
            if (v32 & 4) != 0 || ((v32 >> 1) & (hb as u32) & 1) != 0 {
                wh.w = 0.0;
            }
            let v38 = dt * wh.w;
            wh.flags = v32 ^ ((v32 ^ !(v32 >> 1)) & 0x20);
            let v39 = v38 + wh.angle;
            wh.angle = v39;
            v25 &= (v32 & 1) == 0 || (v32 & 0x20) != 0;
            if f64::from(v39) > std::f64::consts::TAU {
                wh.angle = (f64::from(v39) - std::f64::consts::TAU) as f32;
            }
            let v41 = wh.angle;
            if f64::from(v41) < -std::f64::consts::TAU {
                wh.angle = (f64::from(v41) + std::f64::consts::TAU) as f32;
            }
        }
    }
    let v43 = {
        let d = wd(w, e);
        if v25 {
            d.engine_max_w
        } else {
            ((d.w_engine + d.engine_max_w) - (d.w_engine - d.engine_max_w).abs()) * 0.5
        }
    };
    let v287 = {
        let d = wd(w, e);
        let v48 = d.gears[d.cur_gear.clamp(0, 11) as usize];
        let v50 = v48.abs();
        v43 / ((((0.009_999_999_8 - v50).abs() + (v50 + 0.009_999_999_8)) * 0.5)
            * sgnnz_bits(v48) as f32)
    };
    // 4. suspension
    let b = *w.bodies.get(body_id);
    let (k_stab, gz) = (wd(w, e).k_stabilizer, w.ent(e).rigid().expect("rigid").gravity.z);
    for i in 0..nw.max(0) as usize {
        let d = wm(w, e);
        let lens: Vec<f32> = d.wheels.iter().map(|x| x.len).collect();
        let wh = &mut d.wheels[i];
        let (v56, v57, v58) = (wh.pivot.x, wh.pivot.y, wh.pivot.z);
        let v59 = ((((v246 * v57) + (v245 * v56)) + (v58 * v247)) + pn.x) - b.pos.x;
        let v60 = ((((v248 * v56) + (v249 * v57)) + (v58 * v250)) + pn.y) - b.pos.y;
        let v61 = (((v251 * v56) + (v252 * v57)) + (v58 * v253)) + pn.z;
        let v62 = v61 - b.pos.z;
        wh.rpivot = Vec3::new(v59, v60, v62);
        let vpx = ((b.w.y * v62) - (b.w.z * v60)) + b.v.x;
        let vpy = ((b.w.z * v59) - (b.w.x * v62)) + b.v.y;
        let vpz = ((b.w.x * v60) - (b.w.y * v59)) + b.v.z;
        let v64 = ((vpy * v310) + (vpx * v312)) + (vpz * v258);
        wh.v_up = v64;
        if wh.flags & wflags::CONTACT != 0 && wh.axle >= 0 {
            let v65 = wh.len;
            let v66 = wh.k;
            let v68 = wh.len_rest - wh.len_min_off;
            let mut v69 = ((wh.len_max - (((v65 - v68).abs() + (v65 + v68)) * 0.5)) * v66) - (v64 * wh.c);
            if wh.partner >= 0 && k_stab > 0.0 {
                v69 -= ((v65 - lens[wh.partner as usize]) * v66) * k_stab;
            }
            let x = v69 * dt;
            wh.n_impulse = ((-x).abs() + x) * 0.5;
        }
    }
    // 5. tyres
    let mut v79 = 0.0f32; // Px (v313)
    let mut v257 = 0.0f32; // Py
    let mut v255 = 0.0f32; // Pz
    let mut v265 = 0.0f32; // Lx
    let mut v267 = 0.0f32; // Ly
    let mut v259 = 0.0f32; // Lz
    let mut cnt = [0i32; 2]; // v274
    let mut sum_cap = [0.0f32; 2]; // v275
    let mut sum_w = [0.0f32; 2]; // v276
    let mut drv = [0i32; 2]; // v277
    let mut v82 = 0usize;
    let self_minv = b.minv;
    for v84 in 0..nw.max(0) {
        let i = v84 as usize;
        let tank = wd(w, e).k_steer_tank;
        let mut take = false;
        let flags0 = wd(w, e).wheels[i].flags;
        if !(tank == 0.0 || tank.is_nan()) {
            v82 = (wd(w, e).wheels[i].pivot.x.to_bits() >> 31) as usize;
            if cnt[v82] != 0 && flags0 & 1 != 0 {
                take = true;
            }
        }
        if !take && flags0 & wflags::CONTACT != 0 {
            take = true;
        }
        if !take || wd(w, e).wheels[i].axle < 0 {
            if v25 {
                let wh = &mut wm(w, e).wheels[i];
                wh.w = ((wh.inv_inertia * wh.brake_torque) * dt) + wh.w;
            }
            continue;
        }
        let v86 = flags0;
        let wh0 = wd(w, e).wheels[i].clone();
        let v87 = wh0.steer;
        let v88 = crt::cosf(v87);
        let v89 = crt::sinf(v87);
        let v93 = -v89;
        drv[v82] |= v84 & ((v86 & 1) as i32).wrapping_neg();
        let v94 = ((v93 * v246) + (v245 * v88)) + (v247 * 0.0);
        let v98 = ((v93 * v249) + (v248 * v88)) + (v250 * 0.0);
        let v99 = ((v93 * v252) + (v251 * v88)) + (v253 * 0.0);
        let (v254, v256, v268) = (v94, v98, v99);
        let (mut v123, mut v124, mut v125);
        let mut v128;
        let mut v137;
        let mut v269 = 0.0f32;
        let mut v270 = 0.0f32;
        let tank_dir = |w: &World| -> (f32, f32, f32) {
            let d = wd(w, e);
            let dir = if d.pedal < 0.0 {
                [0.0, 1.0, 0.0]
            } else {
                let a = d.angle_tank;
                [0.0, crt::cosf(a), -crt::sinf(a)]
            };
            (
                ((v246 * dir[1]) + (v245 * dir[0])) + (v247 * dir[2]),
                ((v248 * dir[0]) + (v249 * dir[1])) + (v250 * dir[2]),
                ((v251 * dir[0]) + (v252 * dir[1])) + (v253 * dir[2]),
            )
        };
        let div_cnt = |n: i32| (1 - ((1 - n) & ((1 - n) >> 31))) as f32;
        if v86 & wflags::CONTACT != 0 {
            let v100 = wh0.n_impulse;
            let (v102, v103) = (wh0.rpivot.y, wh0.rpivot.z);
            let v104 = wh0.surf[1];
            let hb = wd(w, e).handbrake;
            let v105 = (u32::from(v86 & 4 != 0) | ((hb as u32) & (v86 >> 1) & 1)) as i32;
            let v243 = v258 * v100;
            let v266 = v310 * v100;
            v79 += v100 * v312;
            let v264 = v100 * v312;
            v257 += v310 * v100;
            let v106 = (v310 * v100) * wh0.rpivot.x;
            v255 += v258 * v100;
            let v107 = (v310 * v100) * v103;
            let v108 = v106 - (v102 * (v100 * v312));
            let v109 = (v103 * (v100 * v312)) - ((v258 * v100) * wh0.rpivot.x);
            let v110 = v265 + (((v258 * v100) * v102) - v107);
            let (minbf, maxbf) = {
                let d = wd(w, e);
                (d.min_brake_friction, d.max_brake_friction)
            };
            let v111 = (v105 as f32) * minbf;
            v265 = v110;
            let v112 = (v105 as f32) * maxbf;
            v267 += v109;
            v259 += v108;
            let fr = |s: i32| -> f32 {
                let k = (s & 0x1FF) as usize;
                w.surfaces.friction.get(k).copied().unwrap_or(0.0)
            };
            let sum = fr(wh0.surf[0]) + fr(v104);
            let v113 = (((-sum).abs() + sum) * 0.5) * 0.5;
            let v114 = ((wh0.min_friction - v113).abs() + (wh0.min_friction + v113)) * 0.5;
            let mn1 = ((wh0.max_friction + v114) - (wh0.max_friction - v114).abs()) * 0.5;
            let nb = ((v105 ^ 1) as f32) * 10.0;
            let v115 = ((mn1 + (v112 + nb)) - ((v112 + nb) - mn1).abs()) * 0.5;
            let a = v111 - nb;
            let v240 = ((a - v115).abs() + (v115 + a)) * 0.5;
            wm(w, e).wheels[i].f232 = v240;
            let n = wh0.n;
            let v118 = (v99 * n.y) - (v98 * n.z);
            let v119 = (v98 * n.x) - (n.y * v94);
            let v120 = (n.z * v94) - (v99 * n.x);
            let v121 = ((v120 * v120) + (v118 * v118)) + (v119 * v119);
            if v121 <= 9.999_999_7e-21 {
                v123 = 0.0;
                v124 = 0.0;
                v125 = 1.0;
            } else {
                let v122 = 1.0 / v121.sqrt();
                v123 = v118 * v122;
                v124 = v120 * v122;
                v125 = v119 * v122;
            }
            let vs = wh0.vslip;
            v128 = (((v124 * vs.y) + (vs.x * v123)) + (v125 * vs.z)) * wh0.r_inv;
            let mut v129 = v240;
            let v127 = wh0.r * wh0.w;
            let (thr, kdyn) = {
                let d = wd(w, e);
                (d.slip_threshold, d.k_dyn_friction)
            };
            let sy = vs.y - (v124 * v127);
            let sx = vs.x - (v123 * v127);
            let sz = vs.z - (v125 * v127);
            let slip = (((sy * sy) + (sx * sx)) + (sz * sz)) > thr * thr;
            let v130 = v86 ^ ((v86 ^ (u32::from(slip) << 4)) & 0x10);
            wm(w, e).wheels[i].flags = v130;
            if v130 & 0x10 != 0 {
                v129 = v240 * kdyn;
            }
            let v131 = -((wh0.mass_share * gz) * dt);
            let v132 = if v131 > 0.0 {
                ((v100 - v131) / v131) * wh0.f164
            } else {
                1.0
            };
            let v133 = ((v132 + 2.0) - (v132 - 2.0).abs()) * 0.5;
            let mut v134 = ((((v133 - -0.899_999_98).abs() + (v133 - 0.899_999_98)) * 0.5 + 1.0)
                * v129)
                * wh0.f168;
            let v135 = ((vs.x * vs.x) + (vs.y * vs.y)) + (vs.z * vs.z);
            v269 = v134;
            if v135 < 9.0 {
                v134 *= (1.0 - (v135 * 0.111_111_11)) + 1.0;
                v269 = v134;
            }
            let lm = wh0.len_max.to_bits() & 0x7FFF_FFFF;
            let zf = ((lm.wrapping_sub(1) ^ lm) as i32 >> 31) as f32;
            let pn_prev = w.contacts.get(wh0.contact).pn;
            let v136 = (v100 - (zf * pn_prev)) * v134;
            v270 = v136;
            v137 = v136;
            if !(tank == 0.0 || tank.is_nan()) {
                if v130 & 1 != 0 {
                    v128 = sum_w[v82] / div_cnt(cnt[v82]);
                    v137 = ((v136 - sum_cap[v82]).abs() + (v136 + sum_cap[v82])) * 0.5;
                    let t = tank_dir(w);
                    v123 = t.0;
                    v124 = t.1;
                    v125 = t.2;
                } else {
                    cnt[v82] += 1;
                    let v138 = v136 + sum_cap[v82];
                    sum_w[v82] = v128 + sum_w[v82];
                    sum_cap[v82] = v138;
                }
            }
            let ce = wh0.contact_ent;
            let cbid = crate::entities::rigid::colliders::body_id_of(w, ce);
            let cminv = w.bodies.get(cbid).minv;
            if self_minv * 10.0 > cminv {
                let mut buf = vec![0u8; 128];
                put_i(&mut buf, 0, 2);
                put_f(&mut buf, 4, -v264);
                put_f(&mut buf, 8, -v266);
                put_f(&mut buf, 12, -v243);
                let u = crate::sentinel::UNUSED_F32_BITS;
                put_u(&mut buf, 16, u);
                put_u(&mut buf, 28, u);
                put_u(&mut buf, 40, u);
                put_f(&mut buf, 52, wh0.pt.x);
                put_f(&mut buf, 56, wh0.pt.y);
                put_f(&mut buf, 60, wh0.pt.z);
                put_i(&mut buf, 64, crate::sentinel::UNUSED_I32);
                put_i(&mut buf, 68, wh0.contact_part);
                put_i(&mut buf, 72, 2);
                put_i(&mut buf, 76, 3);
                crate::entities::interface::action::action(w, ce, &mut buf, 0);
            }
        } else {
            v137 = sum_cap[v82];
            v128 = sum_w[v82] / div_cnt(cnt[v82]);
            let t = tank_dir(w);
            v123 = t.0;
            v124 = t.1;
            v125 = t.2;
        }
        let wh1 = wd(w, e).wheels[i].clone();
        let v284 = wh1.brake_torque;
        let mut v152 = (v284 * wh1.r_inv) * dt;
        let v153 = wh1.flags;
        let hb = wd(w, e).handbrake;
        let stop = (v24 && (wh1.w.abs() * wh1.r) < 1.0)
            || ((hb as u32) & (v153 >> 1) & 1) != 0
            || (v153 & 4) != 0;
        let (v188, v191, v192, v190);
        let (v193, v194) = (v312, v310);
        let cid = wh1.contact;
        if stop {
            v192 = v268;
            v190 = v270;
            v191 = v256;
            v188 = v254;
            let lenpos = wh1.len > 0.0;
            wm(w, e).wheels[i].w = 0.0;
            let c = w.contacts.get_mut(cid);
            if lenpos {
                c.nloc = Vec3::new(v312, v310, v258);
                c.flags = 163_840;
            } else {
                c.flags = 0;
            }
        } else {
            let v154 = wh1.r;
            let v155 = wh1.w;
            let vs = wh1.vslip;
            let v292 = v154;
            let v158 = vs.y - (v124 * (v155 * v154));
            let vz_ = vs.z - (v125 * (v155 * v154));
            let vx_ = vs.x - ((v155 * v154) * v123);
            let v159 = (((vs.x * v123) + (v124 * vs.y)) + (v125 * vs.z)).abs();
            let v160 = crt::atanf(
                (((v158 * v256) + (vx_ * v254)) + (vz_ * v268)).abs()
                    / (((v159 - 0.001).abs() + (v159 + 0.001)) * 0.5),
            );
            let v161 = ((v158 * v158) + (vx_ * vx_)) + (vz_ * vz_);
            let (v165, v163, v164) = if v161 <= 0.000_000_119_209_29 {
                (0.0f32, 0.0f32, 0.0f32)
            } else {
                let v162 = 1.0 / v161.sqrt();
                (v162 * vx_, v158 * v162, vz_ * v162)
            };
            let mut v166 = (((v163 * v124) + (v165 * v123)) + (v164 * v125)).abs();
            let mut v167 = 1.0 - (v166 * v166);
            if v167 >= 0.0 {
                if v167 >= 1.0 {
                    v167 = 1.0;
                }
            } else {
                v167 = 0.0;
            }
            let v244 = v167.sqrt();
            let v168 = wh1.slip_angle;
            if v160 < v168 {
                v166 = ((v160 / v168) * (v166 - 1.0)) + 1.0;
            }
            let v169 = wh1.len;
            wm(w, e).wheels[i].f540 = v166;
            v137 *= v166;
            let v174 = wh1.center0.z - (v169 - wh1.len_rest);
            let (v172, v173) = (wh1.center0.x, wh1.center0.y);
            let v241 = ((v173 * v246) + (v245 * v172)) + (v174 * v247);
            let v175 = ((v173 * v249) + (v248 * v172)) + (v174 * v250);
            let v176 = ((v173 * v252) + (v251 * v172)) + (v174 * v253);
            let v177 = wh1.w;
            let v178 = (v176 + pn.z) - b.pos.z;
            let v242 = (v241 + pn.x) - b.pos.x;
            let v266r = (v175 + pn.y) - b.pos.y;
            let v286 = v137.abs() - v152.abs();
            let v179 = (v128 * 0.300_000_01).abs();
            let v180 = v286.to_bits() >> 31;
            let v278 = (((1.0 - v179).abs() + (v179 + 1.0)) * 0.5) - (v177.abs() - v128.abs());
            let v181 = v278.to_bits() >> 31;
            if v180 != 0 || v181 != 0 {
                let fl = v153 | 0x20;
                wm(w, e).wheels[i].flags = fl;
                let v291 = v177 - v128;
                let a = ((sgn_bits(v152) as f32) * v137) * (u32::from((v180 & (1 - v181)) > 0) as f32);
                let bb = ((sgn_bits(v291) as f32) * v137) * (u32::from(v181 > 0) as f32);
                v152 = a + bb;
                if wd(w, e).footbrake != 0 {
                    wm(w, e).wheels[i].w = v128;
                } else {
                    let x = v287.abs() * dt;
                    let y = (v284 * wh1.inv_inertia) * dt;
                    let v184 = -x;
                    let v185 = ((x + y) - (y - x).abs()) * 0.5;
                    let wh = &mut wm(w, e).wheels[i];
                    wh.w = (((v185 - v184).abs() + (v185 + v184)) * 0.5) + wh.w;
                }
            } else if (v153 & 1) != 0 || v24 {
                let v182 = (v292 * v137) * wh1.inv_inertia;
                let dd = v128 - v177;
                let v183 = v182.abs();
                let wh = &mut wm(w, e).wheels[i];
                wh.w = ((((v183 + dd.abs()) - (v183 - dd.abs()).abs()) * 0.5) * sgn_bits(dd) as f32) + v177;
            } else {
                wm(w, e).wheels[i].w = v128;
            }
            v79 += v152 * v123;
            v257 += v124 * v152;
            v255 += v125 * v152;
            v265 += ((v125 * v152) * v266r) - ((v124 * v152) * v178);
            v267 += (v178 * (v152 * v123)) - ((v125 * v152) * v242);
            v259 += ((v124 * v152) * v242) - (v266r * (v152 * v123));
            let v186 = wh1.slip_angle;
            let v187 = if v160 >= v186 {
                v244
            } else {
                v244 * crt::sinf(((v160 * 3.141_592_7) * 0.5) / v186)
            };
            v188 = v254;
            let v189 = v169 * wh1.len_max;
            v190 = v270 * v187;
            v191 = v256;
            v192 = v268;
            let c = w.contacts.get_mut(cid);
            if v189 <= 0.0 {
                c.nloc = Vec3::new(v123, v124, v125);
                c.flags = 0x2_0000;
            } else {
                c.nloc = Vec3::new(v254, v191, v192);
                c.flags = 98_304;
            }
        }
        let v197 = wh1.f156;
        if wd(w, e).wheels[i].flags & wflags::CONTACT != 0 {
            let wh = wd(w, e).wheels[i].clone();
            let own_body = body_id;
            let c = w.contacts.get_mut(cid);
            c.flags |= 0x80_0000;
            c.pt[1] = wh.pt;
            c.pt[0] = wh.pt;
            let v199 = v197 * v269;
            c.n = wh.n;
            c.friction = v199;
            let v201 = (((v194 * wh.n.y) + (wh.n.x * v193)) + (v258 * wh.n.z)) * v190;
            c.pbody[0] = own_body;
            c.pbody[1] = wh.contact_body.unwrap_or(crate::solver::BodyId::STATIC);
            c.pent[0] = e;
            c.pent[1] = wh.contact_ent;
            c.pn = (((-v201).abs() + v201) * 0.5) * v197;
            let n_hull = wd(w, e).n_hull;
            let c = w.contacts.get_mut(cid);
            c.ipart[1] = wh.contact_part;
            c.ipart[0] = v84 + n_hull;
            let v204 = c.pn;
            let t = &mut wm(w, e).wheels[i];
            t.f524 = Vec3::new(v152 * v123, v124 * v152, v125 * v152);
            t.f512 = Vec3::new(v204 * v188, v191 * v204, v192 * v204);
            t.f536 = if v137 <= 0.0 { 0.0 } else { (v152 / v137).abs() };
        }
    }
    // 6. tank steering: ω / free flag from the side's driving wheel
    if wd(w, e).k_steer_tank != 0.0 {
        let d = wm(w, e);
        for i in 0..nw.max(0) as usize {
            let k = (d.wheels[i].pivot.x.to_bits() >> 31) as usize;
            let j = drv[k] as usize;
            let (wj, fj) = (d.wheels[j].w, d.wheels[j].flags);
            let wh = &mut d.wheels[i];
            wh.w = wj;
            wh.flags ^= (wh.flags ^ fj) & 0x20;
        }
    }
    // 7. momenta
    let (max_speed, m_tot) = (wd(w, e).max_speed, b.m);
    {
        let bb = w.bodies.get_mut(body_id);
        let v211 = v257 + bb.p.y;
        bb.p.x = v79 + bb.p.x;
        bb.p.y = v211;
        bb.p.z = v255 + bb.p.z;
        let v212 = bb.minv;
        let p = bb.p;
        let v213 = p.x * v212;
        let v215 = p.y * v212;
        let v217 = p.z * v212;
        bb.v = Vec3::new(v213, v215, v217);
        if max_speed > 0.009_999_999_8 {
            let v219 = ((v215 * v215) + (v213 * v213)) + (v217 * v217);
            if v219 > max_speed * max_speed {
                let v220 = 1.0 / v219.sqrt();
                bb.v = Vec3::new((v220 * v213) * max_speed, (v220 * v215) * max_speed, (v220 * v217) * max_speed);
                let v221 = m_tot;
                bb.p = Vec3::new(bb.v.x * v221, bb.v.y * v221, bb.v.z * v221);
            }
        }
        let v224 = v267 + bb.l.y;
        let v225 = v265 + bb.l.x;
        let v226 = v259 + bb.l.z;
        bb.l = Vec3::new(v225, v224, v226);
        let ii = bb.iinv.m;
        let v227 = v224 * ii[1][1];
        let v228 = ((v224 * ii[0][1]) + (v225 * ii[0][0])) + (v226 * ii[0][2]);
        let v230 = (v224 * ii[2][1]) + (v225 * ii[2][0]);
        let v231 = v226 * ii[2][2];
        let v232 = v225 * ii[1][0];
        let v233 = v226 * ii[1][2];
        bb.w = Vec3::new(v228, (v227 + v232) + v233, v230 + v231);
    }
    let d = wm(w, e);
    d.unk11184 = [0.0; 6];
    d.load_lat = 0.0;
    let minv = b.minv;
    let mut v7 = 0.0f32;
    for i in 0..nw.max(0) as usize {
        let wh = &d.wheels[i];
        v7 += (wh.brake_torque * wh.r_inv) * minv;
        d.load_lat = v7;
    }
    d.load_long = ((((v257 * v249) + (v246 * v79)) + (v255 * v252)) / dt) * minv;
}

fn put_i(b: &mut [u8], o: usize, v: i32) {
    b[o..o + 4].copy_from_slice(&v.to_le_bytes());
}
fn put_u(b: &mut [u8], o: usize, v: u32) {
    b[o..o + 4].copy_from_slice(&v.to_le_bytes());
}
fn put_f(b: &mut [u8], o: usize, v: f32) {
    put_u(b, o, v.to_bits());
}
