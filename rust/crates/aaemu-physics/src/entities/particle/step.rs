//! `CParticleEntity::DoStep(dt, iCaller)` (slot 25, 0x3503C210; x86 0x35042C60).
//!
//! Variable names in comments are the Hex-Rays locals of the x64 decompile. Every
//! operation is single precision except the two compares the binary does in double
//! (`|v_t| <= 1e-4` of the contact friction and the area-gravity test).
//!
//! # Outline
//!
//! 1. The acceleration `g` (`v459..v461`) and resistance `k` (`v490`): air values, or the
//!    water gravity scaled by `min(−depth/r, 1)` and the water resistance while submerged.
//! 2. Time slice: `dt = 1e-4` when the world step is (almost) done; `performed += dt`. A
//!    collider-to-ignore in the deleted list (simclass 7) is dropped (without Release). Not
//!    awake → `timeAsleep += dt`, return.
//! 3. `v_mid = v + g·dt/2` (not in contact mode), `newpos = pos + v_mid·dt`; the bounce
//!    counter decays.
//! 4. Contact mode: a probe ray `−1.1·r·contactNormal` from `newpos`. No hit → leave contact
//!    mode (rolling spin from `g × heading` unless flag 2). Hit → `newpos` = surface + n·th,
//!    the surface velocity (GetStatus(8) of a moving collider) is subtracted, then either the
//!    friction branch (flag 4 or `n.z < 0.5`: Coulomb friction against the normal load, spin
//!    and q0 reset, q aligned to the surface) or the rolling branch (damping
//!    `1 − dt·friction`, `w = n × v / r`, q0 rolled about the roll axis). The acceleration
//!    loses its normal part (all of it with flag 1); state 2 on static/rigid surfaces or after
//!    40 s, else 1.
//! 5. Free flight (always): `v += (thrust·h + g + k·(flow − v) + lift)·dt` with the lift
//!    direction `h × (h × gravity)` (old heading), heading = normalised `v`; unless flag 2 (or
//!    the friction branch): q0 spins with `w` (exact rotation, or the first-order update for
//!    `|w·dt|² < 0.01`; flag 0x10 zeroes `w`), and `q` = q0 (flag 8) or `q0 ⊗ basis(heading)`.
//! 5. Collision: (a) only in the awake-list job (`world+1744 == 2`, `iCaller < 4`): the old
//!    position carried along with each active rigid entity in its box since the previous
//!    step (GetStatus(pos) with `timeBack = 1`), ray-traced against it (RayTraceEntity,
//!    `geomFlagsAny` 3, back faces ignored) and confirmed by an RWI against static geometry
//!    and terrain; (b) otherwise one RWI from `pos` to `newpos + dir·r` (objtypes `collTypes
//!    | 0x200`, flags `pierce | 0xA0001500`, 8 hits).
//! 6. Per hit (`iCaller < 4`): a collision event (flags 0x2000 immediate, 0x2000000 logged);
//!    for moving, non-particle colliders an impulse `m₁m₂/(m₁+m₂)·k·v_rel` (pierceable hits
//!    scaled by the pierceability difference) when approaching faster than `minBounceVel`
//!    (flag 0x200: reported only); water hits go to the splash handler (not ported), others
//!    set the collided bit and reload the bounce counter when the acceleration points into
//!    the surface.
//! 7. Response to the solid hit (not on areas): restitution (avg bounciness, 0 below
//!    `minBounceVel` or for "flat" particles), Coulomb friction, the mass ratio from the
//!    collider's GetMassInv; stop and enter contact mode when the result is slower than
//!    `minVel` against the acceleration (or flag 1), or when the predicted next-step normal
//!    velocity is below `minVel + 0.001`; else bounce with `w = v_t × n / r` (|w| ≤ 20).
//! 8. Commit (pos, q, bbox — grid with flag 0x400 —, v, contact normal, heading), area check
//!    every `period` steps (gravity, depth, flow), PostStep event.
//!
//! # Kept quirks
//!
//! * The first-order velocity uses the **start** velocity for the free-flight update while
//!   the position used `v_mid`.
//! * The bounce response and the rolling branch overwrite `hits[0].n.z` with 1 before the
//!   state is chosen from it (so those paths never pick state 1 from a steep normal).
//! * The dynamics buffer of the collider (`v518`) is reused: the response reads the
//!   collider's GetStatus(8) with the `ipart` of the contact-mode probe (or the unused
//!   sentinel when there was none).
//! * Path (a) continues with the transformed point of the **last** entity tested, not of the
//!   one that was hit.
//! * An area gravity close to the world gravity restores `gravity0`, and both gravity and
//!   water gravity take the same value.

#![allow(
    clippy::similar_names,
    clippy::many_single_char_names,
    clippy::too_many_lines
)]

use super::{normalized_21020, pd, quat_normalized_20600, ParticleData};
use crate::entities::interface::{rd_i32, wr_i32, wr_u32};
use crate::geometry::shapes::{orthogonal, quat_rotation_arc};
use crate::math::{crt, Quat, Vec3};
use crate::params::ParamsBuoyancy;
use crate::world::events::Event;
use crate::world::queries::{RayHit, RwiParams};
use crate::world::{EntIdx, World};

const EPS20: f32 = f32::from_bits(0x1E3C_E508); // 1e-20

#[inline]
fn rd_f(b: &[u8], o: usize) -> f32 {
    f32::from_bits(rd_i32(b, o) as u32)
}

#[inline]
fn wr_f(b: &mut [u8], o: usize, v: f32) {
    wr_u32(b, o, v.to_bits());
}

/// `(int)(id_bits & 0xFF000000)` (signed simclass compare of the binary).
#[inline]
fn sc_word(w: &World, e: EntIdx) -> i32 {
    (w.ent(e).id_bits & 0xFF00_0000) as i32
}

/// The world's `RayWorldIntersection` as DoStep calls it (slot 104, no hit-last cache).
#[allow(clippy::too_many_arguments)]
fn rwi(
    w: &mut World,
    org: Vec3,
    dir: Vec3,
    objtypes: u32,
    flags: u32,
    hits: &mut [RayHit],
    skip: &[EntIdx],
    caller: i32,
) -> i32 {
    let p = RwiParams {
        org,
        dir,
        objtypes,
        flags,
        skip: skip.to_vec(),
        ..RwiParams::default()
    };
    w.rwi(&p, hits, None, caller)
}

/// The friction table value of a surface index (`world+6504`).
#[inline]
fn friction(w: &World, idx: i32) -> f32 {
    w.surfaces.friction[(idx & 0x1FF) as usize]
}

/// Slot 25 `DoStep(dt, iCaller)` 0x3503C210. Always returns 1.
pub(super) fn do_step(w: &mut World, e: EntIdx, dt_in: f32, caller: i32) -> i32 {
    let tid = w.tls_thread.clamp(0, 4) as usize;
    let ci = caller.clamp(0, 4) as usize;
    let mut hb = w.particle_tls.hits[tid];
    let mut p: ParticleData = pd(w, e).clone();
    let flags = w.ent(e).flags;
    let ignore0 = p.collider_to_ignore; // v9 / v553
    let skip: Vec<EntIdx> = if ignore0.is_none() {
        vec![e]
    } else {
        vec![ignore0, e]
    };
    let mut q = w.ent(e).q; // v478
                            // v518: the pe_status_dynamics image used for the colliders
    let mut sd = [0u8; 128];
    wr_i32(&mut sd, 0, 8);
    wr_u32(&mut sd, 4, 0x8000_0000);
    wr_u32(&mut sd, 8, 0x8000_0000);
    // event: the normal impulse (`Src[17]`)
    let mut v8z: f32 = 1.0; // v8
    let mut dt = dt_in; // v625 / v5
    let (mut gx, mut gy, mut gz, kres) = if p.depth >= 0.0 {
        (p.gravity.x, p.gravity.y, p.gravity.z, p.k_air_resistance)
    } else {
        let t = p.depth * p.size_inv;
        let v14 = ((1.0 - t) - (1.0 - (-t)).abs()) * 0.5;
        let wg = p.water_gravity;
        (wg.x * v14, wg.y * v14, wg.z * v14, p.k_water_resistance)
    };
    let v18 = p.time_full;
    let v19 = p.time_performed;
    if v19 > v18 - f32::from_bits(0x38D1_B717) {
        dt = f32::from_bits(0x38D1_B717);
    }
    p.time_performed = v19 + dt;
    if !ignore0.is_none() {
        // a freed object counts as deleted (the binary reads freed memory)
        let deleted = match w.ents.get(ignore0.0 as usize) {
            Some(Some(o)) => o.id_bits & 0xFF00_0000 == 0x0700_0000,
            _ => true,
        };
        if deleted {
            // dropped without Release (kept)
            p.collider_to_ignore = EntIdx::NONE;
        }
    }
    if p.unk632 == 0.0 {
        p.unk632 = 1.0;
    }
    *w.ent_mut(e).particle_mut().expect("particle") = p.clone();
    if crate::entities::is_awake(w, e) == 0 {
        p.time_asleep = dt + p.time_asleep;
        *w.ent_mut(e).particle_mut().expect("particle") = p;
        return 1;
    }
    w.particle_tls.cur[ci] = e;

    let h0 = p.heading; // v477, v466, v450
    let pos = w.ent(e).pos; // v470..v472 (= v455, v439, v469)
    let (mut ox, mut oy, mut oz) = (pos.x, pos.y, pos.z); // v470, v471, v472
    let (mut cnx, mut cny, mut cnz) = (p.contact_normal.x, p.contact_normal.y, p.contact_normal.z); // v475, v628, v480
    let v29 = p.bits;
    // v33 / v620 / v37: the start velocity; (mx, my, mz) = v457 / v458: the midpoint velocity
    let (mut v33, mut v620, mut v37) = (p.vel.x, p.vel.y, p.vel.z);
    let (mut mx, mut my, mut mz) = (v33, v620, v37);
    if v29 & 0x400_0000 == 0 {
        mz += (gz * dt) * 0.5;
        mx += (gx * dt) * 0.5;
        my += (gy * dt) * 0.5;
    }
    p.bits &= 0xFFF1_FFFF;
    let c = ((v29 >> 17) & 7) as i32;
    let d = 1 - c;
    p.bits |= (((-(d & (d >> 31))) & 7) as u32) << 17;
    let mut v41 = flags;
    // newpos v462..v464
    let (mut nx, mut ny, mut nz) = ((mx * dt) + pos.x, (my * dt) + pos.y, (mz * dt) + pos.z);
    let v42 = p.bits;
    // the acceleration after the contact (v38, v36, v35)
    let (mut v38, mut v36, mut v35) = (gx, gy, gz);
    // the surface velocity of the collider (v520..v522 of the dynamics buffer)
    if v42 & 0x400_0000 != 0 {
        let v44 = p.size * f32::from_bits(0xBF8C_CCCD);
        let dir = Vec3::new(v44 * cnx, v44 * cny, v44 * cnz);
        let n = rwi(
            w,
            Vec3::new(nx, ny, nz),
            dir,
            p.coll_types,
            ((v42 >> 22) & 0xF) | 0x8000_0000,
            &mut hb[..1],
            &skip,
            caller,
        );
        if n == 0 {
            p.bits &= !0x400_0000;
            if flags & 2 == 0 {
                let g = p.gravity;
                let v101 = g.x * h0.y;
                let v102 = (g.x * h0.z) - (g.z * h0.x);
                let v103 = (g.z * h0.y) - (g.y * h0.z);
                let v104 = (g.y * h0.x) - v101;
                let v105 = (((v103 * v103) + (v102 * v102)) + (v104 * v104)).sqrt();
                if v105 <= f32::from_bits(0x3C23_D70A) {
                    p.w = Vec3::ZERO;
                } else {
                    let v107 = ((((g.x * g.x) + (g.y * g.y)) + (g.z * g.z)).sqrt()
                        * (p.size_inv * 0.5))
                        / v105;
                    p.w = Vec3::new(v103 * v107, v107 * v102, v107 * v104);
                }
            }
        } else {
            let hit = hb[0];
            let th = p.thickness;
            let (v46, v47, v49) = (hit.n.x, hit.n.y, hit.n.z);
            nx = (th * v46) + hit.pt.x;
            ny = (th * v47) + hit.pt.y;
            nz = (th * v49) + hit.pt.z;
            cnx = v46;
            cny = v47;
            cnz = v49;
            let col = hit.collider;
            let (mut v55, mut v56, mut v57) = (0.0f32, 0.0f32, 0.0f32);
            let (v58, v60, v59);
            if col.is_none() || sc_word(w, col) <= 0x100_0000 {
                (v58, v60, v59) = (mx, my, mz);
                wr_f(&mut sd, 12, 0.0);
                wr_f(&mut sd, 16, 0.0);
                wr_f(&mut sd, 20, 0.0);
            } else {
                wr_i32(&mut sd, 8, hit.ipart);
                let r = crate::entities::interface::status::get_status(w, col, &mut sd);
                v55 = rd_f(&sd, 12);
                v56 = rd_f(&sd, 16);
                v57 = rd_f(&sd, 20);
                if r == 0 {
                    (v58, v60, v59) = (mx, my, mz);
                } else {
                    let (v523, v524, v525) = (rd_f(&sd, 24), rd_f(&sd, 28), rd_f(&sd, 32));
                    let (v532, v533, v534) = (rd_f(&sd, 60), rd_f(&sd, 64), rd_f(&sd, 68));
                    v55 += (v524 * (nz - v534)) - (v525 * (ny - v533));
                    v57 += (v523 * (ny - v533)) - ((nx - v532) * v524);
                    v56 += ((nx - v532) * v525) - (v523 * (nz - v534));
                    v58 = mx - v55;
                    v59 = mz - v57;
                    v60 = my - v56;
                    wr_f(&mut sd, 12, v55);
                    wr_f(&mut sd, 16, v56);
                    wr_f(&mut sd, 20, v57);
                }
            }
            let (v68, v70);
            if flags & 4 != 0 || v49 < 0.5 {
                let v78 =
                    friction(w, p.surface_idx) + friction(w, i32::from(hit.surface_idx as u16));
                let mut v79 = ((v78.abs() + v78) * 0.5) * 0.5;
                if cnz < 0.5 {
                    v79 = ((v79 + 1.0) - (1.0 - v79).abs()) * 0.5;
                }
                let (hnx, hny, hnz) = (hb[0].n.x, hb[0].n.y, hb[0].n.z);
                let v80 = ((v60 * hny) + (v58 * hnx)) + (v59 * hnz);
                let v81 = v58 - (hnx * v80);
                let v82 = v60 - (hny * v80);
                let v83 = v59 - (hnz * v80);
                let v84 = (((v81 * v81) + (v82 * v82)) + (v83 * v83)).sqrt();
                let v621 = if f64::from(v84) <= 0.0001 {
                    0.0
                } else {
                    1.0 / v84
                };
                let g = p.gravity;
                let v85 = hnx * g.x;
                let v86 = hny * g.y;
                let v87 = hnz * g.z;
                let x = (((v86 + v85) + v87) * dt) + v80;
                let v88 = v84 - (((x.abs() - x) * 0.5) * v79);
                p.w = Vec3::ZERO;
                p.q0 = Quat::IDENTITY;
                let v89 = (v80.abs() + v80) * 0.5;
                let v90 = ((v88.abs() + v88) * 0.5) * v621;
                v37 = ((hnz * v89) + v57) + (v90 * v83);
                v620 = ((hny * v89) + v56) + (v90 * v82);
                v33 = ((hnx * v89) + v55) + (v90 * v81);
                mx = v33;
                my = v620;
                mz = v37;
                if flags & 2 == 0 {
                    let rn = q.rotate(p.normal);
                    let arc = quat_rotation_arc(rn, hb[0].n);
                    let t = arc.product(q);
                    let v92 =
                        1.0 / ((((t.w * t.w) + (t.x * t.x)) + (t.y * t.y)) + (t.z * t.z)).sqrt();
                    q = Quat::new(t.x * v92, t.y * v92, t.z * v92, t.w * v92);
                }
                v68 = cnx;
                v70 = cny;
                v41 |= 2;
            } else {
                let v61 = 1.0 - (dt * friction(w, p.surface_idx));
                let v62 = (v61.abs() + v61) * 0.5;
                let v63 = ((v47 * v60) + (v46 * v58)) + (v59 * v49);
                let v64 = p.size_inv;
                let v65 = v62 * (v60 - (v63 * v47));
                let v66 = (v58 - (v63 * v46)) * v62;
                let v67 = v62 * (v59 - (v63 * v49));
                v68 = cnx;
                v33 = v66 + v55;
                v620 = v65 + v56;
                let v69 = v33 - v55;
                v70 = cny;
                v37 = v67 + v57;
                let v71 = v64 * ((v65 + v56) - v56);
                let v72 = v69 * v64;
                mx = v33;
                my = v620;
                mz = v37;
                let v73 = v64 * (v37 - v57);
                let v74 = v73 * cnx;
                p.w.z = (v71 * cnx) - (v72 * cny);
                p.w.y = (v72 * v49) - v74;
                p.w.x = (v73 * cny) - (v71 * v49);
                let ra = p.roll_axis;
                if ((ra.x * ra.x) + (ra.y * ra.y)) + (ra.z * ra.z) > 0.0 {
                    let wn = super::normalized_210a0(p.w);
                    let rw = p.q0.rotate(ra);
                    let arc = quat_rotation_arc(rw, wn);
                    p.q0 = quat_normalized_20600(arc.product(p.q0));
                }
            }
            if flags & 1 != 0 {
                v35 = 0.0;
                v36 = 0.0;
                v38 = 0.0;
            } else {
                let v93 = ((v70 * gy) + (v68 * gx)) + (gz * v49);
                v35 = gz - (v93 * v49);
                v38 = gx - (v93 * v68);
                v36 = gy - (v93 * cny);
            }
            gz = v35;
            gy = v36;
            gx = v38;
            let col = hb[0].collider;
            let v95 = if col.is_none() || sc_word(w, col) <= 0x200_0000 || p.time_sliding > 40.0 {
                2
            } else {
                1
            };
            p.bits &= 0xFFCF_FFFF;
            p.bits |= (v95 & 3) << 20;
        }
    }

    // LABEL_51: free flight
    let v476s = (((v620 * v620) + (v33 * v33)) + (v37 * v37)).sqrt();
    let g = p.gravity;
    let (v113, v112, v114) = (g.x, g.y, g.z);
    let (v477, v466, v450) = (h0.x, h0.y, h0.z);
    let v115 = v112 * v450;
    let v116 = (v112 * v477) - (v113 * v466);
    let v117 = (v114 * v466) - v115;
    let v118 = (v113 * v450) - (v114 * v477);
    let v119 = (v117 * v450) - (v116 * v477);
    let v120 = (v116 * v466) - (v118 * v450);
    let v121 = (v118 * v477) - (v117 * v466);
    let v122 = ((v120 * v120) + (v119 * v119)) + (v121 * v121);
    let (v124, v125, v126) = if v122 <= EPS20 {
        (0.0, 0.0, 1.0)
    } else {
        let v123 = 1.0 / v122.sqrt();
        (v120 * v123, v119 * v123, v121 * v123)
    };
    let v127 = v476s * p.k_lift;
    let v128 = v124 * v127;
    let v129 = v127 * v126;
    let v130 = (v476s * p.k_lift) * v125;
    let v131 = p.acc_thrust;
    let flow = p.water_flow;
    let v133 = (flow.y - v620) * kres;
    let v134 = v33 + (((((v131 * v477) + v38) + ((flow.x - v33) * kres)) + v128) * dt);
    let v622 = v620 + (((((v131 * v466) + v36) + v133) + v130) * dt);
    let v135 = v37 + (((((v131 * v450) + v35) + ((flow.z - v37) * kres)) + v129) * dt);
    let (v486, v485) = (v134, v135);
    let v136 = ((v622 * v622) + (v134 * v134)) + (v135 * v135);
    // the new heading (v476, v466, v477)
    let (hnx, hny, hnz) = if v136 <= EPS20 {
        (0.0, 0.0, 1.0)
    } else {
        let v137 = 1.0 / v136.sqrt();
        (v137 * v134, v137 * v622, v137 * v135)
    };
    let v484 = v41 & 2;
    if v41 & 2 == 0 {
        if flags & 0x10 != 0 {
            p.w = Vec3::ZERO;
        } else {
            let pw = p.w;
            let ww = ((pw.x * pw.x) + (pw.y * pw.y)) + (pw.z * pw.z);
            if ww * (dt * dt) >= f32::from_bits(0x3C23_D70B) {
                let v141 = ww.sqrt();
                let v142 = (1.0 / v141) * pw.z;
                let v143 = (1.0 / v141) * pw.x;
                let v144 = (1.0 / v141) * pw.y;
                let v145 = (v141 * dt) * 0.5;
                let v146 = crt::sinf(v145);
                let v147 = crt::cosf(v145);
                let dq = Quat::new(v143 * v146, v144 * v146, v142 * v146, v147);
                p.q0 = dq.product(p.q0);
            } else {
                let q0 = p.q0;
                let (v434, v436, v438) = (pw.x, pw.y, pw.z);
                let v138 = q0.w - (((((q0.y * v436) + (q0.x * v434)) + (q0.z * v438)) * dt) * 0.5);
                let hdt = dt * 0.5;
                let v139 = ((((pw.y * q0.z) - (pw.z * q0.y)) + (v434 * v138)) * hdt) + q0.x;
                let v140 = (hdt * (((pw.x * q0.y) - (q0.x * pw.y)) + (v438 * v138))) + q0.z;
                let y = (hdt * (((q0.x * pw.z) - (pw.x * q0.z)) + (v436 * v138))) + q0.y;
                p.q0 = Quat::new(v139, y, v140, v138);
            }
            p.q0 = quat_normalized_20600(p.q0);
        }
        if flags & 8 != 0 {
            q = p.q0;
        } else {
            let h = p.heading;
            let g = p.gravity;
            let v152 = h.z * g.y;
            let v156 = h.x * g.y;
            let v158 = v152 - (h.y * g.z);
            let v159 = g.x * h.z;
            let v160 = g.x * h.y;
            let v162 = v160 - v156;
            let v163 = (h.x * g.z) - v159;
            let mut a = Vec3::new(v158, v163, v162);
            if ((v163 * v163) + (v158 * v158)) + (v162 * v162) < f32::from_bits(0x3C23_D70A) {
                a = orthogonal(h);
            }
            let v169 = ((a.y * a.y) + (a.x * a.x)) + (a.z * a.z);
            let a = if v169 <= EPS20 {
                Vec3::new(0.0, 0.0, 1.0)
            } else {
                let v170 = 1.0 / v169.sqrt();
                Vec3::new(a.x * v170, a.y * v170, a.z * v170)
            };
            q = p.q0.product(super::heading_basis(a, h));
        }
    }

    // collision
    let mut v180: u32 = 0;
    let mut v221: i32 = 0;
    // the normalised sweep direction (v441, v456, v453)
    let (mut dnx, mut dny, mut dnz) = (0.0f32, 0.0f32, 1.0f32);
    let mut done_a = false;
    if caller < 4 && w.flag1744 == 2 {
        let r = p.size;
        let hi = Vec3::new(pos.x + r, r + pos.y, r + pos.z);
        let lo = Vec3::new(pos.x - r, pos.y - r, pos.z - r);
        let list = w.get_entities_in_box(lo, hi, 4, None, ci);
        hb[1].dist = f32::from_bits(0x5015_02F9);
        hb[0].dist = f32::from_bits(0x5015_02F9);
        let mut last = Vec3::ZERO;
        for &o in &list {
            let mut sp = [0u8; 128];
            wr_i32(&mut sp, 0, 1);
            wr_i32(&mut sp, 4, -1);
            wr_i32(&mut sp, 8, -1);
            wr_f(&mut sp, 120, 1.0);
            crate::entities::interface::status::get_status(w, o, &mut sp);
            let spos = Vec3::new(rd_f(&sp, 24), rd_f(&sp, 28), rd_f(&sp, 32));
            let sq = Quat::new(rd_f(&sp, 60), rd_f(&sp, 64), rd_f(&sp, 68), rd_f(&sp, 72));
            let qc = w.ent(o).q;
            let v445 = qc.product(sq.conj());
            let dd = Vec3::new(ox - spos.x, oy - spos.y, oz - spos.z);
            let rot = v445.rotate(dd);
            let opos = w.ent(o).pos;
            let pp = Vec3::new(rot.x + opos.x, rot.y + opos.y, rot.z + opos.z);
            let v191 = ox - pp.x;
            let v193 = oy - pp.y;
            let v195 = oz - pp.z;
            let v196 = ((v193 * v193) + (v191 * v191)) + (v195 * v195);
            let (v198, v199, v200) = if v196 <= EPS20 {
                (0.0, 0.0, 1.0)
            } else {
                let v197 = v196.sqrt();
                (
                    v191 * (1.0 / v197),
                    v193 * (1.0 / v197),
                    v195 * (1.0 / v197),
                )
            };
            let v203 = p.size * f32::from_bits(0x3F78_51EC);
            let dir = Vec3::new(
                v191 + (v203 * v198),
                v193 + (v199 * v203),
                v195 + (v200 * v203),
            );
            let mut h1 = hb[1];
            let r1 = w.ray_trace_entity(o, pp, dir, &mut h1, None, 3, true);
            hb[1] = h1;
            if r1 != 0
                && w.ent(o)
                    .parts
                    .get(hb[1].ipart.max(0) as usize)
                    .is_some_and(|pt| pt.flags & 0x8000 != 0)
                && hb[1].ipart >= 0
                && hb[0].dist > hb[1].dist
            {
                hb[0] = hb[1];
                v180 = 1;
            }
            last = pp;
        }
        if v180 != 0 {
            let (v205, v206, v207) = (hb[0].pt.x - ox, hb[0].pt.y - oy, hb[0].pt.z - oz);
            let v208 = ((v205 * v205) + (v206 * v206)) + (v207 * v207);
            let (v210, v212, v204) = if v208 <= EPS20 {
                (0.0, 0.0, 1.0)
            } else {
                let v209 = 1.0 / v208.sqrt();
                (v209 * v205, v209 * v206, v209 * v207)
            };
            let r = p.size;
            let v549 = Vec3::new(
                (hb[0].pt.x - ox) + (v210 * r),
                (hb[0].pt.y - oy) + (v212 * r),
                (hb[0].pt.z - oz) + (v204 * r),
            );
            let n2 = rwi(
                w,
                Vec3::new(ox, oy, oz),
                v549,
                257,
                ((p.bits >> 22) & 0xF) | 0x8000_0080,
                &mut hb[2..3],
                &skip,
                caller,
            );
            v180 ^= n2 as u32;
        }
        v221 = v180 as i32;
        if v180 != 0 {
            let v222 = ox - last.x;
            let v223 = oy - last.y;
            let v224 = oz - last.z;
            let v225 = ((v222 * v222) + (v223 * v223)) + (v224 * v224);
            if v225 <= EPS20 {
                (dnx, dny, dnz) = (0.0, 0.0, 1.0);
            } else {
                let v227 = 1.0 / v225.sqrt();
                (dnx, dny, dnz) = (v222 * v227, v227 * v223, v227 * v224);
            }
            ox = last.x;
            oy = last.y;
            oz = last.z;
            done_a = true;
        }
    }
    if !done_a {
        let v231 = ny - oy;
        let v232 = nz - oz;
        let v233 = nx - ox;
        let v234 = ((v233 * v233) + (v231 * v231)) + (v232 * v232);
        let (v236, v237, v226) = if v234 <= EPS20 {
            (0.0, 0.0, 1.0)
        } else {
            let v235 = 1.0 / v234.sqrt();
            (v233 * v235, v235 * v231, v235 * v232)
        };
        let r = p.size;
        let dir = Vec3::new(v233 + (v236 * r), v231 + (r * v237), v232 + (r * v226));
        let n = rwi(
            w,
            Vec3::new(ox, oy, oz),
            dir,
            p.coll_types | 0x200,
            ((p.bits >> 22) & 0xF) | 0xA000_1500,
            &mut hb[..8],
            &skip,
            caller,
        );
        v221 = n;
        v180 = u32::from(n > 0 && ((hb[0].dist + 0.5).to_bits() as i32) >= 0);
        (dnx, dny, dnz) = (v236, v237, v226);
    }

    // LABEL_103: per-hit events and impulses
    if caller < 4 && v221 > 0 {
        let v184 = v180 as i32 - v221 - 1;
        for v243 in 1..=v221 {
            let k = (v243 & ((v243 + v184) >> 31)) as usize;
            let hit = hb[k];
            let col = hit.collider;
            let mut partid1 = hit.partid;
            let mut vo = Vec3::ZERO; // Src[12], Src[13] lo
            let mut m1 = 0.0f32; // Src[14] lo
            let mut normimpulse = 0.0f32; // Src[17] lo
            let ev_col;
            if col.is_none() || sc_word(w, col) == 0x0500_0000 {
                ev_col = EntIdx::NONE;
                hb[k].collider = EntIdx::NONE;
            } else {
                ev_col = col;
                let body = crate::entities::rigid::colliders::body_of(w, col);
                let pt = hit.pt;
                let v254 = body.w.y;
                let v437 = body.pos.y;
                let v256 = body.w.x;
                let v257 = ((v256 * (pt.y - v437)) - (v254 * (pt.x - body.pos.x))) + body.v.z;
                vo = Vec3::new(
                    ((v254 * (pt.z - body.pos.z)) - (body.w.z * (pt.y - v437))) + body.v.x,
                    ((body.w.z * (pt.x - body.pos.x)) - (v256 * (pt.z - body.pos.z))) + body.v.y,
                    v257,
                );
                m1 = body.m;
                let nparts = w.ent(col).parts.len() as i32;
                let v258 = nparts - 1;
                let dd = hit.ipart.wrapping_sub(v258);
                let v259 = v258 + (dd & (dd >> 31));
                let v260 = -((-v259) & ((-v259) >> 31));
                partid1 += (v258 >> 31) & (hit.inode.wrapping_sub(partid1));
                let part = w.ent(col).parts.get(v260 as usize);
                let (pflags, pcol) = part.map_or((0, 0), |x| (x.flags, x.flags_collider));
                let idb = w.ent(col).id_bits;
                let skip_imp = w.ent(col).etype == 5
                    || (((idb as i32) < 0 || idb & 0xFF00_0000 == 0) && pflags & 0x40_0000 == 0)
                    || pcol & 0x800_0000 != 0;
                if !skip_imp {
                    let v261 = mx - vo.x;
                    let v262 = my - vo.y;
                    let v263 = mz - vo.z;
                    // slot 66 GetMass(ipart): the part mass (unclamped index; 0 outside)
                    let v264 = w
                        .ent(col)
                        .parts
                        .get(hit.ipart.max(0) as usize)
                        .filter(|_| hit.ipart >= 0)
                        .map_or(0.0, |x| x.mass);
                    let v266 = p.mass;
                    let v267 = ((body.m - v264).abs() + (body.m + v264)) * 0.5;
                    let neg = ((v267 - f32::from_bits(0x2EDB_E6FF)).to_bits() >> 31) as f32;
                    let v268 = v267 + (neg * (v266 * 100.0));
                    let mut v265 = 1.0f32;
                    if k > 0 {
                        let s = i32::from(hit.surface_idx);
                        let m = -((-s) & ((-s) >> 31));
                        let idx = m + ((m + 511) & ((m + 511) >> 31));
                        let sf = w.surfaces.flags[(idx as usize).min(511)];
                        v265 = (1.0
                            - (((sf & 0xF) - ((p.bits >> 22) & 0xF) as i32) as f32
                                * f32::from_bits(0x3D88_8889)))
                            * 0.5;
                    }
                    let n = hit.n;
                    let dotv = ((n.x * v261) + (n.y * v262)) + (n.z * v263);
                    let vv = ((my * my) + (mx * mx)) + (mz * mz);
                    if ((dotv * v268) * v265) < 0.0 && vv > (p.min_bounce_vel * p.min_bounce_vel) {
                        let v271 = ((v266 * v268) * v265) / (v266 + v268);
                        if flags & 0x200 != 0 {
                            normimpulse = v271;
                        } else {
                            let mut b = [0u8; 80];
                            wr_i32(&mut b, 0, 2);
                            wr_f(&mut b, 4, v271 * v261);
                            wr_f(&mut b, 8, v271 * v262);
                            wr_f(&mut b, 12, v271 * v263);
                            wr_u32(&mut b, 16, 0xFFBF_FFFF);
                            wr_u32(&mut b, 28, 0xFFBF_FFFF);
                            wr_u32(&mut b, 40, 0xFFBF_FFFF);
                            wr_f(&mut b, 52, hit.pt.x);
                            wr_f(&mut b, 56, hit.pt.y);
                            wr_f(&mut b, 60, hit.pt.z);
                            wr_u32(&mut b, 64, 0x8000_0000);
                            wr_i32(&mut b, 68, hit.ipart);
                            wr_i32(&mut b, 72, 2);
                            crate::entities::interface::action::action(w, col, &mut b, 1);
                        }
                    }
                }
            }
            let ev = Event::Collision {
                ent: [e, ev_col],
                pt: hit.pt,
                n: hit.n,
                v: [Vec3::new(mx, my, mz), vo],
                mass: [p.mass, m1],
                partid: [((p.bits >> 22) & 0xF) as i32, partid1],
                idmat: [i32::from(p.surface_idx as i16), i32::from(hit.surface_idx)],
                penetration: 0.0,
                normimpulse,
                radius: 0.0,
            };
            if flags & 0x2000 != 0 {
                w.call_immediate_clients(&ev);
            }
            if flags & 0x200_0000 != 0 {
                w.post_logged_event(ev);
            }
            if i32::from(hb[k].surface_idx) == w.water_mat {
                // `world+20848` water-splash handler (sub_350FF0E0): never installed
            } else {
                p.bits |= 0x1_0000;
                let v221b = p.bits;
                let v308 = 3 - ((v221b >> 17) & 7) as i32;
                let n = hb[k].n;
                let v491 = (((gy * n.y) + (gx * n.x)) + (gz * n.z)) - f32::from_bits(0x3A83_126F);
                let add = (((v491.to_bits() >> 31) as i32).wrapping_mul(v308) << 17) as u32;
                p.bits = v221b ^ ((v221b ^ v221b.wrapping_add(add)) & 0xE_0000);
            }
        }
    }

    // the response to the solid hit
    let (v295, v296, v297, v356, v357, v358, v345);
    let mut resp: Option<(f32, f32, f32, f32, f32, f32, f32)> = None;
    if v180 != 0 {
        let col = hb[0].collider;
        if !col.is_none() && w.ent(col).id_bits & 0xFF00_0000 != 0x0500_0000 {
            let s0 = (hb[0].surface_idx as u16 & 0x1FF) as usize;
            let s1 = (p.surface_idx & 0x1FF) as usize;
            let st = &w.surfaces;
            let v287 = (st.bounciness[s0] + st.bounciness[s1]) * 0.5;
            let v288 = (v287 + 1.0) - (v287 - 1.0).abs();
            let v291 = st.friction[s0] + st.friction[s1];
            let mut v292 = ((v288 * 0.5).abs() + (v288 * 0.5)) * 0.5;
            let v293 = ((v291.abs() + v291) * 0.5) * 0.5;
            let minv = crate::entities::mass_inv(w, col);
            let (mut v295r, mut v296r, mut v297r) = (ox, oy, oz);
            let v298 = (minv * p.mass) / ((minv * p.mass) + 1.0);
            let r = p.size;
            let pt = hb[0].pt;
            if (r * r)
                < ((((pt.y - oy) * (pt.y - oy)) + ((pt.x - ox) * (pt.x - ox)))
                    + ((pt.z - oz) * (pt.z - oz)))
            {
                v295r = pt.x - (r * dnx);
                v296r = pt.y - (r * dny);
                v297r = pt.z - (r * dnz);
            }
            nz = v297r;
            ny = v296r;
            nx = v295r;
            let (v302, v303, v304);
            if sc_word(w, col) <= 0x100_0000 {
                (v304, v302, v303) = (mx, my, mz);
                wr_f(&mut sd, 12, 0.0);
                wr_f(&mut sd, 16, 0.0);
                wr_f(&mut sd, 20, 0.0);
            } else {
                crate::entities::interface::status::get_status(w, col, &mut sd);
                let (v520, v521, v522) = (rd_f(&sd, 12), rd_f(&sd, 16), rd_f(&sd, 20));
                let (v523, v524, v525) = (rd_f(&sd, 24), rd_f(&sd, 28), rd_f(&sd, 32));
                let (v532, v533, v534) = (rd_f(&sd, 60), rd_f(&sd, 64), rd_f(&sd, 68));
                let v301 = ((nx - v532) * v525) - ((nz - v534) * v523);
                v302 = my - (v521 + v301);
                let o521 = v521 + v301;
                let o522 = v522 + (((ny - v533) * v523) - ((nx - v532) * v524));
                v303 = mz - o522;
                let o520 = v520 + (((nz - v534) * v524) - ((ny - v533) * v525));
                v304 = mx - o520;
                wr_f(&mut sd, 12, o520);
                wr_f(&mut sd, 16, o521);
                wr_f(&mut sd, 20, o522);
            }
            let hn = hb[0].n;
            let v623 = v302;
            let v447 = hn.y;
            let v309 = ((v302 * v447) + (v304 * hn.x)) + (v303 * hn.z);
            let v454 = v304 - (hn.x * v309);
            let v465 = v303 - (hn.z * v309);
            let v310 = v302 - (v447 * v309);
            let v491 = v310;
            if v309 > -p.min_bounce_vel || (p.size * f32::from_bits(0x3E99_999A)) > p.thickness {
                v292 = 0.0;
            }
            let v312 = (1.0 - v298) * v309;
            let v313 = (v292 + 1.0) * v312;
            let v314 = v304 - (hn.x * v313);
            let v315 = hn.z * v313;
            let v316 = v623 - (hn.y * v313);
            let v442 = v303 - v315;
            let t = ((v298 - 1.0) * v309) * v293;
            let v317 = t.abs() * t;
            let v318 = ((v454 * v454) + (v310 * v310)) + (v465 * v465);
            let (v324, v325, v326);
            if v317 >= v318 {
                v324 = v314 - v454;
                v325 = v442 - v465;
                v326 = v316 - v310;
            } else {
                let v319 = (v312 - v312.abs()) * 0.5;
                let (v321, v322);
                if v318 <= EPS20 {
                    v321 = 0.0;
                    v322 = 0.0;
                } else {
                    let v320 = 1.0 / v318.sqrt();
                    v8z = v320 * v465;
                    v321 = v320 * v454;
                    v322 = v320 * v310;
                }
                let v323 = v319 * v322;
                v324 = ((v321 * v319) * v293) + v314;
                v325 = ((v319 * v8z) * v293) + v442;
                v326 = (v323 * v293) + v316;
            }
            let v327 = gz * hn.z;
            let v328 = (gy * hn.y) + (gx * hn.x);
            let v330 = ((v326 * v326) + (v324 * v324)) + (v325 * v325);
            let v479 = (((v328 + v327) - f32::from_bits(0x3A83_126F)).to_bits() >> 31) as f32;
            let (v340, v342, v343, v345r);
            let th = p.thickness;
            let mut to_165 = false;
            if v330 < ((p.min_vel * p.min_vel) * v479) || flags & 1 != 0 {
                v340 = 0.0;
                v343 = 0.0;
                v342 = 0.0;
                p.w = Vec3::ZERO;
                p.q0 = Quat::IDENTITY;
                if th != p.size {
                    nx = (hn.x * th) + pt.x;
                    ny = (hn.y * th) + pt.y;
                    nz = (hn.z * th) + pt.z;
                }
                p.bits |= 0x400_0000;
                v345r = hn.z;
                cnx = hn.x;
                cny = hn.y;
                if v484 == 0 {
                    let rn = q.rotate(p.normal);
                    q = quat_rotation_arc(rn, hn).product(q);
                }
            } else {
                let ga = p.gravity;
                let (v332, v333, v334) = (ga.y, ga.z, ga.x);
                let v335 = (hny * v333) - (hnz * v332);
                let v336 = (hnx * v332) - (hny * v334);
                let v337 = (hnz * v334) - (hnx * v333);
                let v544 = Vec3::new(
                    (v336 * hny) - (v337 * hnz),
                    (hnz * v335) - (v336 * hnx),
                    (v337 * hnx) - (hny * v335),
                );
                let v338 = v330.sqrt() * p.k_lift;
                let v339 = normalized_21020(v544);
                let v341 = p.acc_thrust;
                v340 = v325;
                v342 = v324;
                let v624 = v326;
                let yy = (((((hny * v341) + gy) - (v624 * kres)) + (v338 * v339.y)) * dt) + v624;
                let xx = v324 + ((((gx + (hnx * v341)) - (v324 * kres)) + (v338 * v339.x)) * dt);
                let zz = (((((hnz * v341) + gz) - (v325 * kres)) + (v339.z * v338)) * dt) + v325;
                let dotn = ((yy * hn.y) + (hn.x * xx)) + (zz * hn.z);
                let mut v345x = cnz;
                if (dotn * v479) >= ((p.min_vel + f32::from_bits(0x3A83_126F)) * v479) {
                    // keep flying (v345 = the old contact normal z)
                } else {
                    if th != p.size {
                        nx = (hn.x * th) + pt.x;
                        ny = (hn.y * th) + pt.y;
                        nz = (hn.z * th) + pt.z;
                    }
                    p.bits |= 0x400_0000;
                    v345x = hn.z;
                    cnx = hn.x;
                    cny = hn.y;
                    if flags & 4 == 0 {
                        to_165 = true;
                    } else if flags & 2 == 0 {
                        let rn = q.rotate(p.normal);
                        q = quat_rotation_arc(rn, hn).product(q);
                    }
                }
                if !to_165 && flags & 4 != 0 {
                    p.w = Vec3::ZERO;
                    hb[0].n.z = 1.0;
                } else {
                    to_165 = true;
                }
                v345r = v345x;
                v343 = v624;
            }
            if to_165 {
                // LABEL_165: spin from the tangential velocity, |w| <= 20
                let n = hb[0].n;
                let v347 = p.size_inv;
                let v348 = ((v465 * n.y) - (v491 * n.z)) * v347;
                let v349 = ((v454 * n.z) - (v465 * n.x)) * v347;
                let v350 = ((v491 * n.x) - (v454 * n.y)) * v347;
                p.w = Vec3::new(v348, v349, v350);
                if ((v348 * v348) + (v349 * v349)) + (v350 * v350) > 400.0 {
                    let u = normalized_21020(p.w);
                    p.w = Vec3::new(u.x * 20.0, u.y * 20.0, u.z * 20.0);
                }
                hb[0].n.z = 1.0;
            }
            // LABEL_172
            let o520 = rd_f(&sd, 12);
            let o521 = rd_f(&sd, 16);
            let o522 = rd_f(&sd, 20);
            let s356 = v342 + o520;
            let s357 = v343 + o521;
            let s358 = v340 + o522;
            let v359 = if hb[0].n.z <= f32::from_bits(0x3F33_3333)
                || (sc_word(w, hb[0].collider) > 0x200_0000 && p.time_sliding <= 40.0)
            {
                1
            } else {
                2
            };
            p.bits &= 0xFFCF_FFFF;
            p.bits |= (v359 & 3) << 20;
            resp = Some((nx, ny, nz, s356, s357, s358, v345r));
        }
    }
    if let Some(t) = resp {
        (v295, v296, v297, v356, v357, v358, v345) = t;
    } else {
        v297 = nz;
        v296 = ny;
        v295 = nx;
        v356 = v486;
        v358 = v485;
        v357 = v622;
        v345 = cnz;
    }

    // LABEL_179: commit
    let v360 = p.size;
    let v362 = p.bits >> 20;
    p.time_asleep = 0.0;
    let v363 = dt + p.time_sliding;
    p.time_sliding = ((v362 & 1) as f32) * v363;
    let bbox = [
        Vec3::new(v295 - v360, v296 - v360, v297 - v360),
        Vec3::new(v295 + v360, v360 + v296, v360 + v297),
    ];
    if flags & 0x400 != 0 {
        w.reposition_entity(e, 1, Some(bbox));
    }
    {
        let ent = w.ent_mut(e);
        ent.pos = Vec3::new(nx, ny, nz);
        ent.q = q;
        ent.bbox = bbox;
    }
    p.vel = Vec3::new(v356, v357, v358);
    p.contact_normal = Vec3::new(cnx, cny, v345);
    p.heading = Vec3::new(hnx, hny, hnz);

    // the area check
    let b668 = p.bits as u8;
    let mut b669 = (p.bits >> 8) as u8;
    if (i32::from(b669) | (i32::from(b668 as i8) >> 7)) == 0 {
        if !w.global_area.is_none() && flags & 0x80_0000 == 0 {
            let bb = w.ent(e).bbox;
            let center = Vec3::new(
                (bb[0].x + bb[1].x) * 0.5,
                (bb[0].y + bb[1].y) * 0.5,
                (bb[0].z + bb[1].z) * 0.5,
            );
            let mut gout = Vec3::new(gx, gy, gz);
            let mut buoy: [ParamsBuoyancy; 4] = std::array::from_fn(|_| ParamsBuoyancy {
                i_medium: 0,
                ..ParamsBuoyancy::default()
            });
            *w.ent_mut(e).particle_mut().expect("particle") = p.clone();
            let n = w.check_areas(
                center,
                &mut gout,
                &mut buoy,
                -1,
                Vec3::ZERO,
                Some(e),
                0.0,
                caller,
                &mut [],
            );
            if n != 0 {
                if gout.x.to_bits() & 0xFFA0_0000 != 0xFFA0_0000 {
                    let wg = w.gravity();
                    let d2 = (((gout.x - wg.x) * (gout.x - wg.x))
                        + ((gout.y - wg.y) * (gout.y - wg.y)))
                        + ((gout.z - wg.z) * (gout.z - wg.z));
                    let l2 = ((gout.y * gout.y) + (gout.x * gout.x)) + (gout.z * gout.z);
                    let src = if f64::from(d2) >= f64::from(l2) * 0.0001 {
                        gout
                    } else {
                        p.gravity0
                    };
                    p.gravity = src;
                    p.water_gravity = src;
                }
                p.depth = 0.0;
                // flow accumulators per medium (0 water, 1 air; larger media overwrite other
                // stack locals in the binary and are ignored here)
                let mut acc = [Vec3::ZERO; 2];
                let pos = w.ent(e).pos;
                for b in buoy.iter().take(n.clamp(0, 4) as usize) {
                    let m = b.i_medium;
                    if (0..2).contains(&m) {
                        let a = &mut acc[m as usize];
                        a.x = b.water_flow.x + a.x;
                        a.y = b.water_flow.y + a.y;
                        a.z = b.water_flow.z + a.z;
                    }
                    if m == 0 {
                        let o = b.water_plane_origin;
                        let nn = b.water_plane_n;
                        let dd = ((nn.x * (pos.x - o.x)) + ((pos.y - o.y) * nn.y))
                            + ((pos.z - o.z) * nn.z);
                        if dd < 0.0 {
                            p.depth = ((p.depth + dd) - (p.depth - dd).abs()) * 0.5;
                        }
                    }
                }
                let k = usize::from((p.depth.to_bits() as i32) >= 0);
                p.water_flow = acc[k];
            }
        }
        b669 = b668;
    }
    b669 = b669.wrapping_sub(((b669 as i8) >> 7) as u8).wrapping_sub(1);
    p.bits = (p.bits & 0xFFFF_00FF) | (u32::from(b669) << 8);

    *w.ent_mut(e).particle_mut().expect("particle") = p;
    w.particle_tls.hits[tid] = hb;

    if caller < 4 {
        // sub_35037FE0: the PostStep event (type 4)
        let ent = w.ent(e);
        let fl = ent.flags;
        let ev = Event::PostStep {
            ent: e,
            dt,
            pos: ent.pos,
            q: ent.q,
            step: w.step_counter,
        };
        if fl & 0x8_0000 != 0 {
            w.call_immediate_clients(&ev);
        }
        if fl & 0x800_0000 != 0 {
            w.post_logged_event(ev);
            let ent = w.ent_mut(e);
            if ent.processed & 0x1_0000 != 0 {
                ent.processed = ent.processed.wrapping_sub(0x1_0000);
            }
        }
    }
    w.particle_tls.cur[ci] = EntIdx::NONE;
    1
}
