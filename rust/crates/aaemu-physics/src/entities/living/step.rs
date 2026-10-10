//! `CLivingEntity::Step` (slot 34, 0x3502DED0, ~38 KB; x86 0x35035630).
//!
//! Outline (the comments name the decompile's locals where it helps to follow it):
//!
//! 1. dt = `max(full − performed, 0.001)` (also `min(·, dt)` during the awake-list job); the
//!    inertia `k` (6 while recovering from an impulse, the acceleration inertia while a
//!    request is pending, else the inertia); the moving-ground position (`sub_3502B3F0`);
//!    the timers.
//! 2. Resting / inactive entities skip everything (an inactive one only translates by
//!    velRequested·dt).
//! 3. The desired velocity: on the ground velRequested projected on the ground plane; in the
//!    air the air-control blend; the raw request with `+1120`; clamped to `vars+368`.
//! 4. Neighbours: GetEntitiesInBox around the swept box; heavy fast rigid bodies widen the
//!    test (an expanded cylinder); the 88-byte entity and 96-byte part records.
//! 5. The overlap pass at the start position: per non-living neighbour part, the collider
//!    (and the expanded cylinder, and — **bug kept** — the extra own parts tested with the
//!    *collider* geometry at their placement) is intersected; pushes out of static and
//!    entity-flag-0x200 contacts, impulses into light rigid bodies, collision events. When
//!    the moving ground had moved the entity the pass restarts from the old position.
//! 6. Step-up / snap-down against the ground probe at the end position (`sub_3502CA20`).
//! 7. The sweep (up to 3 planes): analytic vertical-cylinder tests against living
//!    neighbours, the collider swept against the parts; contact impulses both ways; crease
//!    handling; stuck detection.
//! 8. The final static overlap check (unproject failure → stuck, back to the start).
//! 9. The ground probe with commit; flying decision; the velocity update (airborne
//!    `sub_35027BB0`, landing nod, ground acceleration with overshoot limiting, slide/climb
//!    limits); eye smoothing and the head-sphere ceiling check; bbox/grid/pos commit.
//! 10. Finish: vel, Δvel, displacement velocity, Δq, net error, the rigid proxy, the PostStep
//!     event.

#![allow(
    clippy::manual_range_contains,
    clippy::assign_op_pattern,
    clippy::collapsible_if,
    clippy::needless_late_init,
    clippy::approx_constant,
    clippy::neg_cmp_op_on_partial_ord,
    clippy::excessive_precision,
    clippy::float_cmp,
    clippy::too_many_lines,
    clippy::too_many_arguments,
    clippy::cognitive_complexity,
    unused_assignments
)]

use super::ground::{self, boxes_overlap, EntRec, PartRec};
use super::{ld, lflags, lm};
use crate::collision::contact::{GeomContact, IntersectionParams};
use crate::geometry::{GeomWorldData, Geometry};
use crate::ids::GeomType;
use crate::math::{Mat33, Quat, Vec3};
use crate::world::events::Event;
use crate::world::{EntIdx, World};
use std::sync::Arc;

/// `geometry->Intersect(other, gwd1, gwd2, params, &contacts)` with the primitive driver's
/// write-back into `gwd1` (the `-0.0 → +0.0` / sweep round trip of 0x35151F20).
pub fn intersect_wb(
    a: &dyn Geometry,
    b: &dyn Geometry,
    g1: &mut GeomWorldData,
    g2: &GeomWorldData,
    ip: &mut IntersectionParams,
    out: &mut Vec<GeomContact>,
) -> usize {
    if a.is_a_primitive() && a.geom_type() != GeomType::Ray {
        let env = crate::collision::prim_intersect::env_with_generic();
        crate::collision::prim_intersect::with_thread_scratch(|s| {
            let o = crate::collision::prim_intersect::intersect(&env, s, a, b, g1, g2, ip);
            out.clear();
            if let (Some(f), true) = (o.first, o.ret > 0) {
                out.extend(s.contacts(f, o.ret));
            }
            o.ret.max(0) as usize
        })
    } else {
        // the mesh glue appends to `out`: start from an empty list like the primitive path
        out.clear();
        a.intersect(b, g1, g2, ip, out)
    }
}

/// Debug trace of the living step (`AAEMU_LIVING_DEBUG` set): one line per stage.
fn dbg(msg: impl FnOnce() -> String) {
    if std::env::var_os("AAEMU_LIVING_DEBUG").is_some() {
        eprintln!("liv: {}", msg());
    }
}

/// `sub_35022100`: reset intersection params (`+98`/`+100` are not modelled; the primitive
/// driver does not read them).
fn reset_params(ip: &mut IntersectionParams) {
    *ip = IntersectionParams::default();
}

/// The `(min, max)` idioms of the binary.
#[inline]
fn mx(a: f32, b: f32) -> f32 {
    ((a - b).abs() + (a + b)) * 0.5
}
#[inline]
fn mn(a: f32, b: f32) -> f32 {
    ((a + b) - (a - b).abs()) * 0.5
}
#[inline]
fn dot(a: Vec3, b: Vec3) -> f32 {
    (a.x * b.x + a.y * b.y) + a.z * b.z
}
#[inline]
fn len2(a: Vec3) -> f32 {
    (a.x * a.x + a.y * a.y) + a.z * a.z
}

/// `sub_35021020`: normalise in place (`(0, 0, 1)` when `|v|² ≤ 1e-20`).
#[inline]
fn norm(v: Vec3) -> Vec3 {
    crate::entities::rigid::velocity::normalize_or_z(v)
}

/// `R·v + p` with the binary's association (`((R0·v.x + R1·v.y) + R2·v.z) + p`).
fn place(r: &[[f32; 3]; 3], v: Vec3, p: Vec3) -> Vec3 {
    Vec3::new(
        ((r[0][0] * v.x + v.y * r[0][1]) + v.z * r[0][2]) + p.x,
        ((v.x * r[1][0] + v.y * r[1][1]) + v.z * r[1][2]) + p.y,
        p.z + ((v.x * r[2][0] + v.y * r[2][1]) + v.z * r[2][2]),
    )
}

/// Per-caller part-record flags kept between steps (the per-thread array at `world+21912`
/// persists; a stale record past the current count is read through `rec+72`).
fn stale_flags(w: &World, tid: usize, idx: i32) -> u32 {
    w.living_part_flags
        .get(tid)
        .and_then(|v| v.get(idx as usize))
        .copied()
        .unwrap_or(0)
}

/// `pe_status_dynamics` (v, w, centre) of a whole entity.
fn ent_dynamics(w: &mut World, e: EntIdx) -> (Vec3, Vec3, Vec3) {
    ground::part_dynamics(w, e, crate::sentinel::UNUSED_I32)
}

/// Slot 45 against the living entity: rigid bodies only ignore through constraints (not
/// ported), every other class returns 0.
fn ignores(_w: &World, _o: EntIdx, _e: EntIdx) -> i32 {
    0
}

/// Slot 34 `Step(dt)` (0x3502DED0). Always returns 1.
pub fn step(w: &mut World, e: EntIdx, a2: f32) -> i32 {
    if a2 <= 0.0 {
        return 1;
    }
    let mut v4 = ld(w, e).step_full - ld(w, e).step_done;
    if w.flag1744 == 2 {
        let v3 = v4;
        v4 = ((v3 + a2) - (a2 - v3).abs()) * 0.5;
    }
    let dt = ((v4 - 0.001).abs() + (v4 + 0.001)) * 0.5;
    let tid = w.tls_thread.clamp(0, 4) as usize;
    let q0 = w.ent(e).q;
    let mut ip = IntersectionParams {
        no_area_contacts: true,
        ..IntersectionParams::default()
    };
    let mut v8 = 0i32; // "moved"
    let mut v948 = 0i32;
    lm(w, e).ground_mat_id = 0;
    let mut v850 = 0.0f32;
    let k = {
        let l = ld(w, e);
        if l.impulse_recover > 0.000_099_999_997 {
            6.0
        } else if l.inertia_accel != 0.0 && len2(l.vel_requested) > 0.1 {
            l.inertia_accel
        } else {
            l.inertia
        }
    };
    let pos0 = w.ent(e).pos; // v999, v1000, v1039
    let mut p = if w.flag1744 == 2 {
        w.ent(e).pos
    } else {
        ground::follow_ground(w, e, dt)
    };
    let vel0 = ld(w, e).vel; // v1035, v1034, v1038
    let mut vnew = vel0; // v22..v24 / v798, v799
    let flying0 = ld(w, e).flags & lflags::FLYING; // v998
    let mut v817 = flying0;
    if ld(w, e).flags & lflags::NO_LOCK == 0 {
        let l = lm(w, e);
        l.lock_timer -= dt;
        l.eye_change_time += dt;
        l.push_time += dt;
        let r = l.impulse_recover - dt;
        l.impulse_recover = ((-r).abs() + r) * 0.5;
        l.step_done += dt;
    }
    let q = w.ent(e).q;
    let up = q.rotate(Vec3::new(0.0, 0.0, 1.0)); // v801, v802, v797
    let mut v653: u32;
    'body: {
        let pos = w.ent(e).pos;
        let l = ld(w, e);
        let hgt = ((up.y * pos.y + pos.x * up.x) + up.z * pos.z) - l.height_pivot;
        let rest = l.flags & lflags::ACTIVE == 0
            || (len2(vnew) == 0.0
                && len2(l.vel_requested) == 0.0
                && (flying0 == 0 || len2(l.gravity) == 0.0)
                && hgt >= l.ground_height - l.size.z * 0.1
                && l.eye_vel == 0.0
                && l.eye_acc == 0.0
                && l.flags & lflags::AWAKE == 0
                && l.cos_slide <= l.ground_normal.z
                && l.ground.is_none());
        if rest {
            if ld(w, e).flags & lflags::ACTIVE == 0 {
                let vr = ld(w, e).vel_requested;
                if len2(vr) > 0.0 {
                    let ent = w.ent_mut(e);
                    ent.pos.x += vr.x * dt;
                    ent.pos.y = vr.y * dt + ent.pos.y;
                    ent.pos.z = vr.z * dt + ent.pos.z;
                    for b in &mut ent.bbox {
                        b.x = vr.x * dt + b.x;
                        b.y = vr.y * dt + b.y;
                        b.z = vr.z * dt + b.z;
                    }
                    w.reposition_entity(e, 1, None);
                    v8 = 1;
                }
                super::release_ground(w, e);
            }
            v653 = v817;
            break 'body;
        }
        let k_inertia = k;
        let f952 = ld(w, e).flags;
        lm(w, e).flags = f952 & 0xFFFF_FF7F;
        let v830_bit31 = (f952 as i32) < 0;
        // ---- 3. desired velocity ----
        let mut vdes: Vec3;
        let l = ld(w, e);
        let vr = l.vel_requested;
        let use_air = if k_inertia == 0.0 {
            if flying0 == 0 {
                if f952 & lflags::JUMP == 0 {
                    let n = l.ground_normal;
                    let d = (vr.y * n.y + vr.x * n.x) + vr.z * n.z;
                    vdes = Vec3::new(vr.x - n.x * d, vr.y - n.y * d, vr.z - n.z * d);
                    None
                } else {
                    vdes = vnew;
                    None
                }
            } else {
                vdes = vnew;
                Some(())
            }
        } else if flying0 == 0 {
            vdes = vnew;
            None
        } else {
            vdes = vnew;
            Some(())
        };
        if use_air.is_some() && l.air_control > 0.0 {
            let ac = l.air_control;
            let inertia = l.inertia;
            if inertia <= 0.0 {
                let g = l.gravity;
                let g2x = g.x * g.x;
                vdes = vr;
                if (g.y * g.y + g2x) + g.z * g.z > 0.0 {
                    let v67 = (vnew.y * g.y + g.x * vnew.x) + vnew.z * g.z;
                    let v69 = v67 - ((vdes.x * g.x + vdes.y * g.y) + vdes.z * g.z);
                    let v70 = 1.0 / ((g.y * g.y + g2x) + g.z * g.z);
                    vdes = Vec3::new(
                        vdes.x + (g.x * v69) * v70,
                        vdes.y + (g.y * v69) * v70,
                        vdes.z + (g.z * v69) * v70,
                    );
                }
            } else {
                let v51 = (inertia * dt) * ac;
                let mut s = Vec3::new(vr.x * v51, vr.y * v51, vr.z * v51);
                let d = Vec3::new(vr.x - vnew.x, vr.y - vnew.y, vr.z - vnew.z);
                if -(d.x * s.x) >= 0.0 {
                    s.x = 0.0;
                }
                if -(d.y * s.y) >= 0.0 {
                    s.y = 0.0;
                }
                if -(d.z * s.z) >= 0.0 {
                    s.z = 0.0;
                }
                let c = |d: f32, s: f32| -> f32 {
                    let r = if d >= 0.0 {
                        (d + s) - (d - s).abs()
                    } else {
                        (d - s).abs() + (d + s)
                    };
                    r * 0.5
                };
                let mut v61 = c(d.x, s.x);
                let mut v63 = c(d.y, s.y);
                let v65 = c(d.z, s.z);
                if ac - 1.0 >= 0.0 {
                    // **bug kept:** full air control replaces only x and y
                    v63 = d.y;
                    v61 = d.x;
                }
                vdes = Vec3::new(vnew.x + v61, vnew.y + v63, vnew.z + v65);
            }
        }
        if ld(w, e).use_raw_vel {
            vdes = ld(w, e).vel_requested;
        }
        let vmax = w.vars.unk_368;
        let v72 = (vdes.x * vdes.x + vdes.y * vdes.y) + vdes.z * vdes.z;
        if v72 > vmax * vmax {
            let n = if v72 <= 9.999_999_7e-21 {
                Vec3::new(0.0, 0.0, 1.0)
            } else {
                let r = 1.0 / v72.sqrt();
                Vec3::new(vdes.x * r, vdes.y * r, vdes.z * r)
            };
            vdes = Vec3::new(n.x * vmax, n.y * vmax, n.z * vmax);
        }
        vnew = vdes;
        lm(w, e).use_raw_vel = false;
        let mut v997 = 0i32;
        let mut mv = Vec3::new(vdes.x * dt, vdes.y * dt, vdes.z * dt); // v818, v816, v810
        let v78 = (mv.x * mv.x + mv.y * mv.y) + mv.z * mv.z;
        if v78.is_nan() {
            // LABEL_633: release the step lock and return without the finish
            return 1;
        }
        let mlen = v78.sqrt(); // v83
        {
            let l = lm(w, e);
            let c = l.coll_count - 1;
            l.coll_count = c - (c >> 31);
        }
        // ---- 4. neighbours ----
        let (size, hc, pivot) = {
            let l = ld(w, e);
            (l.size, l.height_collider, l.height_pivot)
        };
        let eps = w.vars.living_eps;
        let v88 = ((mlen + eps) + size.x) * 2.5;
        let v89 = (hc - size.z) * 0.5;
        let bb = w.ent(e).bbox;
        let pos = w.ent(e).pos;
        let dp = Vec3::new(p.x - pos.x, p.y - pos.y, p.z - pos.z);
        let swept = [
            Vec3::new(
                (dp.x + bb[0].x) - v88,
                (dp.y + bb[0].y) - v88,
                ((dp.z + bb[0].z) - (v89 + v88)) - 2.0,
            ),
            Vec3::new(
                (dp.x + bb[1].x) + v88,
                (dp.y + bb[1].y) + v88,
                (dp.z + bb[1].z) + v88,
            ),
        ];
        let v94 = mx(dt * 10.0, v88);
        let qmin = Vec3::new(
            (dp.x + bb[0].x) - v94,
            (dp.y + bb[0].y) - v94,
            ((dp.z + bb[0].z) - (v94 + v89)) - 2.0,
        );
        let qmax = Vec3::new(
            (dp.x + bb[1].x) + v94,
            (dp.y + bb[1].y) + v94,
            (dp.z + bb[1].z) + v94,
        );
        let coll_types = ld(w, e).coll_types;
        let list = w.get_entities_in_box(qmin, qmax, coll_types | 0x2_0050, Some(e), tid);
        let self_minv = ld(w, e).mass_inv; // v826 / v242
        let mut v803 = 0.0f32;
        let mut v844 = 0i32;
        let mut kept: Vec<EntIdx> = Vec::new();
        let fd = w.ent(e).foreign_data;
        for &o in &list {
            if fd != 0 && w.ent(o).foreign_data == fd {
                continue;
            }
            let obb = w.ent(o).bbox;
            let osz = Vec3::new(
                obb[1].x - obb[0].x,
                obb[1].y - obb[0].y,
                obb[1].z - obb[0].z,
            );
            let mut overlap_test = true;
            if w.ent(o).id_bits & 0xFF00_0000 == 0x0200_0000 {
                if w.ent(o).flags & 0x100 != 0 {
                    continue;
                }
                if !(crate::entities::mass_inv(w, o) * 0.400_000_01 >= self_minv) {
                    let (vv, ww, _) = ent_dynamics(w, o);
                    let v114 = mx(osz.x, osz.y);
                    let v115 = len2(ww).sqrt() * mx(v114, osz.z) + len2(vv).sqrt();
                    v803 = mx(v803, v115);
                    let v828 = size.x * 0.2 - v803 * dt;
                    v844 |= i32::from(v828 < 0.0) | v8;
                    if !(v828 >= 0.0) {
                        overlap_test = false;
                    }
                }
            }
            if overlap_test && !boxes_overlap(&obb, &swept) && len2(osz) > 0.0 {
                continue;
            }
            let ot = w.ent(o).etype;
            if ot == 1 {
                // flagged static entities (0x60000000) with per-caller part subsets: the port
                // has no per-caller part tables (`+320`/`+328` stay 0), so every part counts
                if w.ent(o).flags & 0x6000_0000 != 0
                    && static_moving_away(w, e, o, p, mv, mlen, dt, pos0, q)
                {
                    continue;
                }
            } else if ot == 8 || ot == 7 {
                crate::entities::awake(w, o, 1, 0);
                continue;
            }
            let osc = w.ent(o).sim_class();
            if osc >= 4 {
                continue;
            }
            let add = if ot == 4 {
                w.ent(o).parts.first().is_some_and(|pp| pp.flags & 2 != 0)
                    && o != e
                    && ld(w, e).ignore != o
                    && w.ent(o).living().is_some_and(|ol| ol.ignore != e)
            } else {
                ignores(w, o, e) == 0
            };
            if !add {
                continue;
            }
            if osc == 1 && ld(w, e).push_time < 0.2 && crate::entities::mass_inv(w, o) > 0.0 {
                let fl = w.ent(o).parts.iter().fold(0u32, |a, pp| a | pp.flags);
                if fl & 2 != 0 {
                    crate::entities::awake(w, o, 1, 0);
                }
            }
            kept.push(o);
        }
        // records
        let mut recs: Vec<EntRec> = Vec::with_capacity(kept.len());
        let mut parts: Vec<PartRec> = Vec::new();
        for &o in &kept {
            let ent = w.ent(o);
            let sz = Vec3::new(
                ent.bbox[1].x - ent.bbox[0].x,
                ent.bbox[1].y - ent.bbox[0].y,
                ent.bbox[1].z - ent.bbox[0].z,
            );
            let pstart = parts.len() as i32;
            for (j, part) in ent.parts.iter().enumerate() {
                let qp = ent.q.product(part.q);
                let r = ent.q.rotate(part.pos);
                parts.push(PartRec {
                    bbox: part.bbox,
                    q: qp,
                    pos: Vec3::new(ent.pos.x + r.x, ent.pos.y + r.y, ent.pos.z + r.z),
                    scale: part.scale,
                    flags: part.flags,
                    ipart: j as i32,
                    surface: ((part.surface_bits << 22) as i32) >> 22,
                    geom: part.pgeom_proxy.geom.clone(),
                });
            }
            let pend = parts.len() as i32;
            recs.push(EntRec {
                bbox: ent.bbox,
                size: sz,
                mass_inv: 0.0,
                has_size: i32::from(len2(sz) > 0.0),
                etype: ent.etype,
                nparts: ent.parts.len() as i32,
                simclass: ent.sim_class(),
                ignore: 0,
                pstart,
                pend,
                plast: pend,
                ent: o,
            });
        }
        for r in &mut recs {
            r.mass_inv = crate::entities::mass_inv(w, r.ent);
            r.ignore = ignores(w, r.ent, e);
        }
        // the per-thread part-record flags (for the stale read at `rec+72`)
        if w.living_part_flags.len() <= tid {
            w.living_part_flags.resize_with(tid + 1, Vec::new);
        }
        {
            let cache = &mut w.living_part_flags[tid];
            if cache.len() < parts.len() {
                cache.resize(parts.len(), 0);
            }
            for (i, pr) in parts.iter().enumerate() {
                cache[i] = pr.flags;
            }
        }
        // own orientation matrix: identity unless tilted
        let r = if q.y * q.y + q.x * q.x >= 0.000_001_000_000_1 {
            Mat33::from_quat(q).m
        } else {
            Mat33::IDENTITY.m
        };
        ip.time_interval = dt * 2.0;
        let v939 = dt * 2.0;
        ip.max_unproj = size.x * 2.5;
        ip.vrel_min = size.x;
        let collider: Arc<dyn Geometry> = ld(w, e).collider.geom.clone();
        let mut geoms: Vec<Arc<dyn Geometry>> = vec![collider.clone()];
        if v844 != 0 {
            let r6 = mn(size.x * 1.5, v803 * dt) + size.x;
            let hh = ((v803 * dt + size.z) - (size.z - v803 * dt).abs()) * 0.5 + size.z;
            let cyl = super::make_collider(false, r6, hh);
            geoms.push(cyl.geom.clone());
        }
        let pp0 = w.ent(e).parts[0].pos;
        let mut g_own = GeomWorldData {
            offset: place(&r, pp0, p),
            r: Mat33 { m: r },
            scale: 1.0,
            ..GeomWorldData::default()
        };
        let mut v805 = g_own.offset;
        ip.pt_outside_pivot[0] = g_own.offset;
        let part0_fc = w.ent(e).parts[0].flags_collider;
        let mut contacts: Vec<GeomContact> = Vec::new();
        let mut v869 = 0i32;
        let mut v870 = 0i32;
        // the reference position of the overlap pass (v832, v834, v831)
        let mut refp = p;
        // the end position (v837, v839, v820/v860) and its copy (v858..v860)
        let mut endp = Vec3::new(mv.x + p.x, p.y + mv.y, p.z + mv.z);
        let mut endp2 = endp;
        // the stored normal of the last static push (v848, v843, v849)
        let mut nlast = Vec3::ZERO;
        // the other gwd's velocities / centre (persist between records)
        let mut g_oth_vw = (Vec3::ZERO, Vec3::ZERO);
        let mut g_oth_com = Vec3::ZERO;
        let ent_flags = w.ent(e).flags; // v294
                                        // ---- 5. overlap pass ----
        if part0_fc & 2 != 0 {
            'restart: loop {
                let mut ri = 0usize;
                while ri < recs.len() {
                    let rec = recs[ri].clone();
                    if rec.etype == 4 && !v830_bit31 {
                        ri += 1;
                        continue;
                    }
                    let o = rec.ent;
                    let osc = rec.simclass;
                    let v841 = rec.mass_inv;
                    let v951 = v841 * 0.400_000_01 - self_minv;
                    let v256 = v951 < 0.0;
                    let mut v255 = 0i32;
                    if v844 != 0 && v951 < 0.0 && osc == 2 && o != ld(w, e).ground {
                        let (vv, ww, com) = ent_dynamics(w, o);
                        let v258 = rec.size.z;
                        let v259 = rec.size.x;
                        let v260 = v259 + rec.size.y;
                        let v261 = (v259 - rec.size.y).abs();
                        let h = (v261 + v260) * 0.5;
                        let v262 = len2(ww).sqrt() * (((h - v258).abs() + (v258 + h)) * 0.5)
                            + len2(vv).sqrt();
                        v803 = ((v803 - v262).abs() + (v262 + v803)) * 0.5;
                        let v940 = size.x * 0.2 - v803 * dt;
                        v255 = i32::from(v940 < 0.0);
                        g_oth_vw = (vv, ww);
                        g_oth_com = com;
                    } else {
                        g_oth_vw = (Vec3::ZERO, Vec3::ZERO);
                    }
                    let nself = w.ent(e).parts.len() as i32;
                    let v266 = v255 + nself;
                    let mut geo_idx = 0i32; // v264
                    let mut cur_geom = geoms[0].clone(); // v1115
                    while geo_idx < v266 {
                        let (mask, use_part_gwd, gpart);
                        if geo_idx <= v255 {
                            cur_geom = geoms[geo_idx as usize].clone();
                            mask = 2u32;
                            use_part_gwd = false;
                            gpart = None;
                        } else {
                            let pi = (geo_idx - v255) as usize;
                            let pt = &w.ent(e).parts[pi];
                            let qp = q.product(pt.q);
                            let rr = q.rotate(pt.pos);
                            let off = Vec3::new(p.x + rr.x, p.y + rr.y, p.z + rr.z);
                            let fc = pt.flags_collider;
                            let g2 = GeomWorldData {
                                offset: off,
                                r: Mat33::from_quat(qp),
                                scale: pt.scale,
                                center_of_mass: off,
                                ..GeomWorldData::default()
                            };
                            if fc & 0xFFB == 0 {
                                geo_idx += 1;
                                continue;
                            }
                            v948 = 1;
                            mask = fc;
                            use_part_gwd = true;
                            gpart = Some(g2);
                        }
                        let mut gpart = gpart;
                        for k in rec.pstart..rec.pend {
                            let pr = parts[k as usize].clone();
                            if mask & pr.flags == 0 {
                                continue;
                            }
                            if rec.has_size != 0 && !boxes_overlap(&swept, &pr.bbox) {
                                continue;
                            }
                            let mut g1 = pr.gwd();
                            if osc == 3 {
                                g1.scale = 1.0;
                            }
                            g1.v = g_oth_vw.0;
                            g1.w = g_oth_vw.1;
                            g1.center_of_mass = g_oth_com;
                            let v836 = pr.ipart;
                            let saved = g_own.offset; // v800[1], v800[2]
                            let mut sweep = false;
                            let d = Vec3::new(
                                g_own.offset.x - v805.x,
                                g_own.offset.y - v805.y,
                                g_own.offset.z - v805.z,
                            );
                            let v290;
                            if osc == 0 && (d.x.abs() > 0.0 || d.y.abs() > 0.0 || d.z.abs() > 0.0) {
                                g_own.offset = v805;
                                sweep = true;
                                v290 = ((d.y * d.y + d.x * d.x) + d.z * d.z).sqrt();
                                g_own.v = Vec3::new(
                                    d.x * (1.0 / v290),
                                    d.y * (1.0 / v290),
                                    d.z * (1.0 / v290),
                                );
                            } else {
                                v290 = v939;
                                g_own.v = Vec3::ZERO;
                            }
                            ip.sweep_test = sweep;
                            ip.time_interval = v290;
                            let n = if use_part_gwd {
                                let gp = gpart.as_mut().expect("part gwd");
                                gp.v = g_own.v;
                                intersect_wb(
                                    cur_geom.as_ref(),
                                    pr.geom.as_ref(),
                                    gp,
                                    &g1,
                                    &mut ip,
                                    &mut contacts,
                                )
                            } else {
                                intersect_wb(
                                    cur_geom.as_ref(),
                                    pr.geom.as_ref(),
                                    &mut g_own,
                                    &g1,
                                    &mut ip,
                                    &mut contacts,
                                )
                            };
                            let restore = |g: &mut GeomWorldData, sweep: bool| {
                                if sweep {
                                    g.offset = saved;
                                }
                            };
                            if n == 0 {
                                restore(&mut g_own, sweep);
                                continue;
                            }
                            // the first contact whose point lies behind the own offset
                            let mut ci = 0usize;
                            while ci < n {
                                let c = &contacts[ci];
                                let dd = Vec3::new(
                                    c.pt.x - g_own.offset.x,
                                    c.pt.y - g_own.offset.y,
                                    c.pt.z - g_own.offset.z,
                                );
                                if (dd.x * c.dir.x + dd.y * c.dir.y) + dd.z * c.dir.z <= 0.0 {
                                    break;
                                }
                                ci += 1;
                            }
                            if ci >= n {
                                restore(&mut g_own, sweep);
                                continue;
                            }
                            let epos = w.ent(e).pos;
                            if len2(Vec3::new(refp.x - epos.x, refp.y - epos.y, refp.z - epos.z))
                                > 0.000_001_000_000_1
                            {
                                // the moving ground moved the entity: restart from its position
                                let ent_pos = epos;
                                g_own.offset = Vec3::new(
                                    g_own.offset.x + (ent_pos.x - p.x),
                                    g_own.offset.y + (ent_pos.y - p.y),
                                    g_own.offset.z + (ent_pos.z - p.z),
                                );
                                mv = Vec3::new(
                                    mv.x - (ent_pos.x - p.x),
                                    mv.y - (ent_pos.y - p.y),
                                    mv.z - (ent_pos.z - p.z),
                                );
                                refp = ent_pos;
                                p = ent_pos;
                                let pp0 = w.ent(e).parts[0].pos;
                                g_own.offset = place(&r, pp0, p);
                                ip.pt_outside_pivot[0] = g_own.offset;
                                v805 = g_own.offset;
                                continue 'restart;
                            }
                            let c = contacts[ci].clone();
                            if geo_idx == 0 && v256 {
                                let o_flags = w.ent(o).flags;
                                let v938 = v841 * 2.0 - self_minv;
                                let v312 = o_flags | (u32::from(v938 < 0.0).wrapping_sub(1));
                                if w.flag1744 == 2 {
                                    // the awake-list path stores up to 8 contacts through
                                    // 0x35026A30, which never stores anything in the x64
                                    // build; only the collision counter is updated
                                    if !(osc <= 0 || v841 <= 0.0) {
                                        // (contact recorded: nothing to do)
                                    } else if v312 & 0x1000_0000 == 0 {
                                        let v950 = dot(nlast, c.n) + 0.990_000_01;
                                        let l = lm(w, e);
                                        let v331 = l.coll_count + 5 * i32::from(v950 < 0.0);
                                        l.coll_count = v331 + ((5 - v331) & ((5 - v331) >> 31));
                                        if osc > 0 {
                                            nlast = c.n;
                                        }
                                    }
                                } else if v312 & 0x1000_0000 == 0 {
                                    let v857 = ((nlast.y * c.n.y + nlast.x * c.n.x)
                                        + nlast.z * c.n.z)
                                        + 0.990_000_01;
                                    let l = lm(w, e);
                                    let v333 = l.coll_count + 5 * i32::from(v857 < 0.0);
                                    l.coll_count = v333 + ((5 - v333) & ((5 - v333) >> 31));
                                    if osc > 0 {
                                        nlast = c.n;
                                    }
                                }
                                if ent_flags & 0x200 != 0 {
                                    if v841 != 0.0 {
                                        v870 += 1;
                                    } else {
                                        v869 += 1;
                                    }
                                    let mut ct = c.t;
                                    if sweep {
                                        let dd = Vec3::new(
                                            saved.x - v805.x,
                                            saved.y - v805.y,
                                            saved.z - v805.z,
                                        );
                                        ct = f64::from(
                                            ((dd.x * dd.x + dd.y * dd.y) + dd.z * dd.z).sqrt(),
                                        ) - ct;
                                        contacts[ci].t = ct;
                                        g_own.offset = saved;
                                    }
                                    let v337 = (f64::from(eps) + ct) as f32;
                                    let push_x = c.dir.x * v337;
                                    let push_y = c.dir.y * v337;
                                    let mut push_z = c.dir.z * v337;
                                    if rec.etype == 4 {
                                        push_z = (push_z.abs() + push_z) * 0.5;
                                    }
                                    p = Vec3::new(p.x + push_x, p.y + push_y, p.z + push_z);
                                    // ground material through the game's foreign object
                                    // (iForeignData 2): no foreign objects in the port
                                    let _ = &mut v850;
                                    v997 = 1;
                                    g_own.offset = Vec3::new(
                                        g_own.offset.x + push_x,
                                        g_own.offset.y + push_y,
                                        g_own.offset.z + push_z,
                                    );
                                    if f64::from(size.x) < ct {
                                        ip.pt_outside_pivot[0] =
                                            Vec3::new(1e11, 1e11, 9.999_999_8e10);
                                    }
                                }
                            }
                            if osc != 2 {
                                restore(&mut g_own, sweep);
                                continue;
                            }
                            // ---- a rigid body: impulses both ways ----
                            let mut kk = [0.0f32; 9];
                            let v347 = ent_flags & 0x200 != 0;
                            let center = contacts[ci].center;
                            if v347 {
                                super::solver::add_mass_inv_3x3(w, e, &mut kk);
                            }
                            let ground_now = ld(w, e).ground;
                            let v349 = o != ground_now;
                            if v349 {
                                ground::contact_matrix(w, o, center, v836, &mut kk);
                            } else if !v347 {
                                restore(&mut g_own, sweep);
                                continue;
                            }
                            let c = contacts[ci].clone();
                            let t = c.t as f32;
                            let cx = center.x - c.dir.x * t;
                            let cy = center.y - c.dir.y * t;
                            let cz = center.z - c.dir.z * t;
                            contacts[ci].center = Vec3::new(cx, cy, cz);
                            let mut nn = Vec3::new(-c.n.x, -c.n.y, -c.n.z);
                            if nn.z.abs() < 0.5 {
                                let v357 = nn.x * nn.x + nn.y * nn.y;
                                if v357 <= 9.999_999_7e-21 {
                                    nn = Vec3::new(0.0, 0.0, 1.0);
                                } else {
                                    let rr = 1.0 / v357.sqrt();
                                    nn = Vec3::new(rr * nn.x, nn.y * rr, rr * 0.0);
                                }
                            }
                            let b = super::solver::body_ro(w, o);
                            let v364 = cx - b.pos.x;
                            let v365 = cz - b.pos.z;
                            let gv = ld(w, e).ground_vel;
                            let v366 = ((v365 * b.w.y - (cy - b.pos.y) * b.w.z) + b.v.x) - vdes.x;
                            let v367 = (v364 * b.w.z - v365 * b.w.x) + b.v.y;
                            let v368 = ((cy - b.pos.y) * b.w.x - v364 * b.w.y) + b.v.z;
                            let v369 = ((v366 - gv.x) * nn.x + ((v367 - vdes.y) - gv.y) * nn.y)
                                + ((v368 - vdes.z) - gv.z) * nn.z;
                            v803 = v369;
                            if geo_idx == 0 || v369.abs() * dt > size.x * 0.2 {
                                let v370 = v369
                                    - (((vdes.x + gv.x) * nn.x + (vdes.y + gv.y) * nn.y)
                                        + (vdes.z + gv.z) * nn.z);
                                v803 = ((-v370).abs() + v370) * 0.5;
                                let v371 = (((nn.x * kk[0] + nn.y * kk[3]) + kk[6] * nn.z) * nn.x
                                    + ((nn.y * kk[4] + kk[1] * nn.x) + kk[7] * nn.z) * nn.y)
                                    + ((nn.y * kk[5] + kk[2] * nn.x) + kk[8] * nn.z) * nn.z;
                                let e6 = f32::from_bits(0x3586_37BD);
                                let den = ((e6 - v371).abs() + (v371 + e6)) * 0.5;
                                let v372 = v803 / den;
                                let mut imp = Vec3::new(v372 * nn.x, nn.y * v372, v372 * nn.z);
                                let v377 = len2(imp);
                                if (self_minv * self_minv) * v377
                                    > ((1.0 - v803 * v803).abs() + (v803 * v803 + 1.0)) * 0.5
                                {
                                    let n2 = if v377 <= 9.999_999_7e-21 {
                                        Vec3::new(0.0, 0.0, 1.0)
                                    } else {
                                        let rr = 1.0 / v377.sqrt();
                                        Vec3::new(rr * imp.x, imp.y * rr, imp.z * rr)
                                    };
                                    let s = (v803.abs() * ld(w, e).mass) * 0.5;
                                    imp = Vec3::new(n2.x * s, n2.y * s, n2.z * s);
                                }
                                if v347 {
                                    vdes.x += self_minv * imp.x;
                                    vdes.y += imp.y * self_minv;
                                    vdes.z += imp.z * self_minv;
                                    vnew = vdes;
                                }
                                v817 = 1;
                                {
                                    let l = lm(w, e);
                                    l.time_flying = 0.0;
                                    if l.inertia == 0.0 {
                                        l.impulse_recover = l.impulse_recover_time;
                                    }
                                }
                                if v349 {
                                    let mut j = imp;
                                    if (up.y * c.dir.y + up.x * c.dir.x + up.z * c.dir.z).abs()
                                        < 0.699_999_99
                                    {
                                        let v387 = (j.x * up.x + j.y * up.y) + j.z * up.z;
                                        j = Vec3::new(
                                            j.x - v387 * up.x,
                                            j.y - v387 * up.y,
                                            j.z - v387 * up.z,
                                        );
                                    }
                                    j = Vec3::new(-j.x, -j.y, -j.z);
                                    if w.ent(o).etype < 4 {
                                        j = Vec3::new(j.x * 0.2, j.y * 0.2, j.z * 0.2);
                                    }
                                    ground::apply_impulse(
                                        w,
                                        o,
                                        j,
                                        Vec3::new(cx, cy, cz),
                                        v836,
                                        2,
                                        1,
                                    );
                                    lm(w, e).push_time = 0.0;
                                }
                                let mat = if contacts[ci].id[1] < 0 {
                                    pr.surface
                                } else {
                                    contacts[ci].id[1]
                                };
                                super::solver::collision_event(
                                    w,
                                    e,
                                    p,
                                    contacts[ci].pt,
                                    nn,
                                    o,
                                    v836,
                                    mat,
                                    v372,
                                    false,
                                    contacts[ci].i_prim[1],
                                );
                            }
                            restore(&mut g_own, sweep);
                        }
                        geo_idx += 1;
                    }
                    ri += 1;
                }
                break;
            }
        }
        dbg(|| {
            format!(
                "after overlap p={p:?} vdes={vdes:?} v817={v817} v869={v869} v870={v870} mv={mv:?}"
            )
        });
        // ---- LABEL_286 ----
        let mut endz;
        if v869 != 0 && v870 != 0 && ld(w, e).coll_count != 0 {
            p = refp;
            v870 = 0;
            v869 = 0;
            endz = endp2.z;
        } else if v870 + v869 <= 0 {
            endz = endp2.z;
        } else {
            endz = p.z + mv.z;
            endp.y = p.y + mv.y;
            endp2.y = endp.y;
            endp2.z = endz;
            endp.x = mv.x + p.x;
            endp2.x = endp.x;
        }
        let _ = endp2;
        // the ground below the end position (no commit, small parts filtered)
        let endq = Vec3::new(endp.x, endp.y, endz);
        let gh = ground::ground_probe(w, e, &recs, &parts, endq, false, false, true);
        let v405 = gh.height;
        let n_end = gh.n; // v861..v863
        let v408 = (endp.y * up.y + endp.x * up.x) + endz * up.z;
        let v410 = v408 - pivot;
        let v411 = (n_end.x * up.x + n_end.y * up.y) + n_end.z * up.z;
        let mut do_adjust = false;
        let mut cancel = false;
        if !(v411 <= ld(w, e).cos_fall) {
            let mut v412 = v410 <= v405;
            let mut check_snap = false;
            if v410 >= v405 {
                check_snap = true;
            } else if v410 <= v405 - (hc - size.z) * 1.01 {
                v412 = v410 <= v405;
                check_snap = true;
            } else {
                do_adjust = true;
            }
            if check_snap
                && !v412
                && (v410 - v405).abs() * (v410 - v405) < len2(vdes) * (dt * dt)
                && v817 == 0
                && ld(w, e).flags & (lflags::JUMP | lflags::SWIMMING) == 0
            {
                do_adjust = true;
            }
            if do_adjust && v405 > v410 {
                let l = ld(w, e);
                let gn = l.ground_normal;
                let cs = l.cos_slide;
                if (up.y * gn.y + up.x * gn.x) + up.z * gn.z < cs
                    && (n_end.y * gn.y + n_end.x * gn.x) + n_end.z * gn.z < cs
                    && v411 < cs
                    && len2(l.vel_requested) == 0.0
                {
                    cancel = true;
                }
            }
        }
        let (mut m427, mut m426, mut m428) = (mv.x, mv.y, mv.z);
        let mut v827 = mlen;
        if do_adjust {
            let (e422, e423, e424);
            if cancel {
                e422 = p.x;
                e423 = p.y;
                e424 = p.z;
                vdes = Vec3::ZERO;
                vnew = Vec3::ZERO;
            } else {
                let v425 = v405 + pivot;
                e424 = endz + (v425 - v408) * up.z;
                e423 = endp.y + (v425 - v408) * up.y;
                e422 = endp.x + (v425 - v408) * up.x;
            }
            endp = Vec3::new(e422, e423, e424);
            endz = e424;
            m427 = e422 - p.x;
            m426 = e423 - p.y;
            m428 = e424 - p.z;
            mv = Vec3::new(m427, m426, m428);
            v827 = ((m427 * m427 + m426 * m426) + m428 * m428).sqrt();
        }
        let _ = endz;
        dbg(|| {
            format!("probe v405={v405} n_end={n_end:?} v410={v410} adjust={do_adjust} cancel={cancel} mv=({m427},{m426},{m428}) len={v827}")
        });
        let p_before = p; // v841, v838, v833
        if ld(w, e).flags & lflags::JUMP != 0 {
            let gn = ld(w, e).ground_normal;
            ground::push_ground(w, e, Vec3::new(-vdes.x, -vdes.y, -vdes.z), gn, true);
        }
        lm(w, e).flags &= !lflags::STUCK;
        let mut nsum = Vec3::ZERO; // v834, v830, v832
        let mut v586;
        let mut v846 = 0usize;
        if v827 <= size.x * 0.000_099_999_997 || part0_fc & 2 == 0 {
            p = Vec3::new(p.x + m427, p.y + m426, p.z + m428);
            v586 = 0.0;
        } else {
            // ---- 7. the sweep ----
            ip = IntersectionParams::default();
            ip.sweep_test = true;
            let inv = 1.0 / v827;
            let mut dir = Vec3::new(m427 * inv, inv * m426, inv * m428); // v433, v435, v434
            let v840 = v827;
            let mut v826 = 0.0f32;
            let mut planes: [Vec3; 4] = [Vec3::ZERO; 4]; // v1133..
            let mut v430 = v827;
            v586 = 0.0;
            loop {
                let pp0 = w.ent(e).parts[0].pos;
                g_own.offset = place(&r, pp0, p);
                g_own.v = dir;
                let mut v444 = v430 + eps;
                ip.time_interval = v444;
                v444 *= 2.0;
                let mut v803s = v444;
                let mut hit_ent = EntIdx::NONE; // v1003
                let mut v432: i32 = 0;
                let mut v443: i32 = -1;
                let mut v829: i32 = 0;
                let mut hn = Vec3::ZERO; // v1200, v809, v811 (= v822..v824)
                let mut hp = Vec3::ZERO; // v852..v854
                let mut v442 = 0usize;
                while v442 < recs.len() {
                    let rec = recs[v442].clone();
                    let o = rec.ent;
                    if rec.etype == 4 {
                        let v448 = stale_flags(w, tid, rec.plast) & 2;
                        let adj = ((v448 ^ v448.wrapping_sub(1)) as i32) >> 31;
                        if rec.nparts + adj <= 1 - v948 {
                            let oq = w.ent(o).q;
                            let v450 = oq.x * oq.x + oq.y * oq.y;
                            let v451 = q.y * q.y + q.x * q.x;
                            if mx(v451, v450) <= 0.001 {
                                let (opos, ol) = {
                                    let oe = w.ent(o);
                                    (oe.pos, oe.living().expect("living").clone())
                                };
                                let v453 = ol.size.x;
                                let v452 = size.x;
                                let v940 = endp.x - opos.x;
                                let v951 = endp.y - opos.y;
                                let v454 = dir.x * v940 + v951 * dir.y;
                                let v455 = v454 * v454
                                    - ((v951 * v951 + v940 * v940) - (v453 + v452) * (v453 + v452));
                                if v455 < 0.0 {
                                    v444 = v803s;
                                    v442 += 1;
                                    continue;
                                }
                                let v456 = size.z;
                                let v457 = p.z - pivot;
                                let v458 = f32::from(u8::from(ld(w, e).flags & 8 != 0)) * v452;
                                let v459 = ((v457 + hc) + v456) + v458;
                                let v460 = (v457 - (v459 - (v458 + v456) * 2.0)).abs()
                                    + (v457 + (v459 - (v458 + v456) * 2.0));
                                let v462 = v460 * 0.5;
                                let v463 =
                                    f32::from(u8::from(ol.flags & 8 != 0)) as i32 as f32 * v453;
                                let v464 = opos.z - ol.height_pivot;
                                let v938 = ol.size.z + v463;
                                let v465 = ((ol.height_collider + v464) + ol.size.z) + v463;
                                let v466 = v455.sqrt();
                                let v467 = ((v464 - (v465 - (ol.size.z + v463) * 2.0)).abs()
                                    + (v464 + (v465 - (ol.size.z + v463) * 2.0)))
                                    * 0.5;
                                let v468 = v466 - v454;
                                let v857e = v468;
                                let v828 = -v452;
                                let zov = |t: f32| -> bool {
                                    let top = ((v465 + (v459 + dir.z * t))
                                        - ((v459 + dir.z * t) - v465).abs())
                                        * 0.5;
                                    let bot = (((v462 + dir.z * t) - v467).abs()
                                        + (v467 + (v462 + dir.z * t)))
                                        * 0.5;
                                    top < bot
                                };
                                if v468 > -v452
                                    && v468 < v803s
                                    && dir.x * ((dir.x * v468 + p.x) - opos.x)
                                        + (((v468 * dir.y) + p.y) - opos.y) * dir.y
                                        >= 0.0
                                    && zov(v468)
                                {
                                    v444 = v803s;
                                    v442 += 1;
                                    continue;
                                }
                                let mut v471 = -v454 - v466;
                                let mut v472 = dir.x * v471;
                                let mut v473 = v471 * dir.y;
                                let mut v474 = (v472 + p.x) - opos.x;
                                let mut v475 = (v473 + p.y) - opos.y;
                                let use_exit = if v471 < v828 {
                                    true
                                } else {
                                    zov(v471) || (v474 * dir.x + v475 * dir.y) >= 0.0
                                };
                                if use_exit {
                                    v471 = v857e;
                                    v472 = dir.x * v857e;
                                    v473 = v857e * dir.y;
                                    v474 = (v472 + p.x) - opos.x;
                                    v475 = (v473 + p.y) - opos.y;
                                }
                                let v476;
                                let v857z = dir.z * v471;
                                if v471 <= v828
                                    || v471 >= v803s
                                    || (v475 * dir.y + v474 * dir.x) >= 0.0
                                    || {
                                        let t = v471;
                                        let top = ((v465 + (v459 + dir.z * t))
                                            - ((v459 + dir.z * t) - v465).abs())
                                            * 0.5;
                                        let bot = (((v462 + dir.z * t) - v467).abs()
                                            + (v467 + (v462 + dir.z * t)))
                                            * 0.5;
                                        top <= bot
                                    }
                                {
                                    v476 = v803s;
                                } else {
                                    v476 = v471;
                                    hn = norm(Vec3::new(v474, v475, 0.0));
                                    v432 = 0;
                                    hit_ent = o;
                                    v443 = -1;
                                    hp = Vec3::new(
                                        (v472 + p.x) - v452 * hn.x,
                                        (v473 + p.y) - v452 * hn.y,
                                        (p.z + v857z) - v452 * hn.z,
                                    );
                                    v829 = ld(w, e).surface_idx;
                                    v803s = v471;
                                }
                                let v477 = size.z;
                                if dir.z.abs() > v477 * 0.000_009_999_999_7 {
                                    let v478 = size.x;
                                    let v479 = f32::from(u8::from(ld(w, e).flags & 8 != 0));
                                    let v480 = (((dir.z.to_bits() as i32) >> 30) & !1) + 1;
                                    let v481 = v480 as f32;
                                    let v482 = (((opos.z - ol.height_pivot) + ol.height_collider)
                                        - v481 * v938)
                                        - ((v479 * v478 + v477) * v481 + ((p.z - pivot) + hc));
                                    let v939b = ((dir.z * v476 - dir.z * v478) - v482 * 2.0).abs()
                                        - (-(dir.z * v478) - dir.z * v476).abs();
                                    let a = dir.z * v940 + dir.x * v482;
                                    let b = dir.z * v951 + v482 * dir.y;
                                    if v939b < 0.0
                                        && a * a + b * b
                                            < ((ol.size.x + v478) * dir.z)
                                                * ((ol.size.x + v478) * dir.z)
                                    {
                                        hit_ent = o;
                                        v432 = -1;
                                        v443 = -1;
                                        hn = Vec3::new(0.0, 0.0, -v480 as f32);
                                        v444 = v482 / dir.z;
                                        v803s = v444;
                                        hp = Vec3::new(
                                            dir.x * v482 + p.x,
                                            v482 * dir.y + p.y,
                                            v481 * ld(w, e).size.z + (p.z + dir.z * v482),
                                        );
                                        v829 = ld(w, e).surface_idx;
                                        v442 += 1;
                                        continue;
                                    }
                                }
                                v444 = v803s;
                                v442 += 1;
                                continue;
                            }
                        }
                    }
                    // the parts of this record
                    let has_size = rec.has_size;
                    for kp in rec.pstart..rec.pend {
                        let pr = parts[kp as usize].clone();
                        let v490 = pr.flags;
                        if v490 & 0x20_0002 == 0 {
                            continue;
                        }
                        if has_size != 0 && !boxes_overlap(&swept, &pr.bbox) {
                            continue;
                        }
                        if v490 & 0x20_0000 != 0 {
                            w.signal_event(w.ent(e).flags, Event::BBoxOverlap { ent: [e, o] });
                            if v490 & 2 == 0 {
                                continue;
                            }
                        }
                        let g1 = pr.gwd();
                        let mut nhit = intersect_wb(
                            collider.as_ref(),
                            pr.geom.as_ref(),
                            &mut g_own,
                            &g1,
                            &mut ip,
                            &mut contacts,
                        );
                        let mut v493 = 1usize;
                        loop {
                            if nhit != 0 {
                                let c = contacts[nhit - 1].clone();
                                if !(c.t >= f64::from(v444)) {
                                    if (dir.y * c.n.y + dir.x * c.n.x) + dir.z * c.n.z > 0.0 {
                                        hit_ent = o;
                                        v444 = c.t as f32;
                                        hn = Vec3::new(-c.n.x, -c.n.y, -c.n.z);
                                        hp = c.pt;
                                        v803s = v444;
                                        v443 = c.i_prim[1];
                                        v432 = pr.ipart;
                                        let id1 = c.id[1];
                                        v829 = ((id1 >> 31) & pr.surface)
                                            | (id1 - ((id1 >> 31) & id1));
                                    }
                                }
                            }
                            // the other own parts (only with v948)
                            let nself = if v948 != 0 { w.ent(e).parts.len() } else { 0 };
                            nhit = 0;
                            while v493 < nself {
                                let (fc, qp, rr, scale, g) = {
                                    let pt = &w.ent(e).parts[v493];
                                    (
                                        pt.flags_collider,
                                        q.product(pt.q),
                                        q.rotate(pt.pos),
                                        pt.scale,
                                        pt.pgeom_proxy.geom.clone(),
                                    )
                                };
                                if v490 & fc != 0 {
                                    let off = Vec3::new(p.x + rr.x, p.y + rr.y, p.z + rr.z);
                                    let mut g2 = GeomWorldData {
                                        offset: off,
                                        r: Mat33::from_quat(qp),
                                        scale,
                                        v: dir,
                                        w: Vec3::ZERO,
                                        center_of_mass: off,
                                    };
                                    nhit = intersect_wb(
                                        g.as_ref(),
                                        pr.geom.as_ref(),
                                        &mut g2,
                                        &g1,
                                        &mut ip,
                                        &mut contacts,
                                    );
                                    v493 += 1;
                                    if nhit != 0 {
                                        break;
                                    }
                                } else {
                                    v493 += 1;
                                }
                            }
                            if nhit == 0 {
                                break;
                            }
                        }
                    }
                    v442 += 1;
                }
                let _ = v803s;
                dbg(|| {
                    format!("sweep pass {v846} hit={hit_ent:?} t={v444} lim={} hn={hn:?} hp={hp:?} dir={dir:?} p={p:?}", ip.time_interval)
                });
                if !hit_ent.is_none() && v444 <= ip.time_interval {
                    let o = hit_ent;
                    let v522 = v444 - eps;
                    let v523 = ((-v522).abs() + v522) * 0.5;
                    p = Vec3::new(p.x + dir.x * v523, p.y + v523 * dir.y, p.z + dir.z * v523);
                    let mut n = hn;
                    let osc = w.ent(o).sim_class();
                    if v817 == 0 {
                        let v856 = (0.75 - ((n.x * up.x + n.y * up.y) + up.z * n.z) * 2.0).abs()
                            - 0.950_000_05;
                        if v856 < 0.0
                            && ((osc - 1) as u32) < 2
                            && crate::entities::mass_inv(w, o) > ld(w, e).mass_inv * 0.25
                        {
                            n = norm(Vec3::new(n.x, n.y, 0.0));
                            hn = n;
                        }
                    }
                    let v535;
                    if osc > 0 {
                        v535 = 1;
                    } else {
                        let l = ld(w, e);
                        let nd = (n.x * up.x + n.y * up.y) + up.z * n.z;
                        let v530 = l.cos_climb - nd;
                        let v531 = nd + l.cos_fall;
                        let v533 = mn(v530, v531);
                        let v534 = ((up.x * vdes.x + up.y * vdes.y) + up.z * vdes.z) + 1.0;
                        v850 = mn(v533, v534);
                        v535 = i32::from(!(v850 >= 0.0));
                    }
                    let sf = w.ent(e).flags;
                    let mut kk = [0.0f32; 9];
                    let v537 = osc > 0 && sf & 0x200 == 0;
                    let mut v539 = i32::from(!v537) & v535;
                    let of = w.ent(o).flags;
                    let mut v541 = false;
                    if (u32::from(osc == 3) + 1) & sf != 0
                        && of & 0x200 != 0
                        && (osc as u32 | (of & 0x40_0000) | w.vars.unk_416 as u32) != 0
                    {
                        v541 = true;
                    }
                    let v542 = o != ld(w, e).ground && v541;
                    let v543 = v537 && of & 0x200 != 0;
                    let v544 = v543 || v542;
                    if !v544 {
                        super::solver::add_mass_inv_3x3(w, e, &mut kk);
                    } else {
                        ground::contact_matrix(w, o, hp, v432, &mut kk);
                        if sf & 0x200 != 0 {
                            super::solver::add_mass_inv_3x3(w, e, &mut kk);
                        } else {
                            v539 = 0;
                        }
                    }
                    let gv = ld(w, e).ground_vel;
                    let v548 = (vdes.z + gv.z) * n.z;
                    let v549 = (vdes.y + gv.y) * n.y + n.x * (vdes.x + gv.x);
                    let v550 = ((v549 + v548) - (-(v549 + v548)).abs()) * 0.5;
                    let mut idir = n; // v926..v928
                    let mut v552;
                    if w.ent(o).id_bits & 0xFF00_0000 == 0x0300_0000 {
                        let ovel = w.ent(o).living().expect("living").vel;
                        v552 = v550 - ((n.y * ovel.y + n.x * ovel.x) + n.z * ovel.z);
                        if (n.x * up.x + n.y * up.y) + up.z * n.z > 0.949_999_99 {
                            let opos = w.ent(o).pos;
                            let d = Vec3::new(p.x - opos.x, p.y - opos.y, p.z - opos.z);
                            let v553 = (up.x * d.x + d.y * up.y) + d.z * up.z;
                            let hd =
                                Vec3::new(d.x - up.x * v553, d.y - up.y * v553, d.z - up.z * v553);
                            let v558 = (hd.y * hd.y + hd.x * hd.x) + hd.z * hd.z;
                            let h = if v558 <= 9.999_999_7e-21 {
                                Vec3::new(0.0, 0.0, 1.0)
                            } else {
                                let rr = 1.0 / v558.sqrt();
                                Vec3::new(hd.x * rr, hd.y * rr, hd.z * rr)
                            };
                            idir = Vec3::new(n.x + h.x, h.y + n.y, h.z + n.z);
                            if (v552 * -2.0).abs() - 2.0 < 0.0 {
                                v552 = -1.0;
                            }
                        }
                    } else {
                        let b = super::solver::body_ro(w, o);
                        let v566 = n.x
                            * (((hp.z - b.pos.z) * b.w.y - (hp.y - b.pos.y) * b.w.z) + b.v.x)
                            + ((b.w.z * (hp.x - b.pos.x) - (hp.z - b.pos.z) * b.w.x) + b.v.y) * n.y;
                        v552 = v550
                            - (v566
                                + (((hp.y - b.pos.y) * b.w.x - b.w.y * (hp.x - b.pos.x)) + b.v.z)
                                    * n.z);
                    }
                    v432 -= v432 >> 31;
                    let v567 = n.x * ((kk[3] * n.y + n.x * kk[0]) + kk[6] * n.z);
                    let v568 = ((kk[4] * n.y + kk[1] * n.x) + kk[7] * n.z) * n.y;
                    let v569 = ((kk[5] * n.y + kk[2] * n.x) + kk[8] * n.z) * n.z;
                    let s = (v567 + v568) + v569;
                    let e10 = f32::from_bits(0x2EDB_E6FF);
                    let den = ((e10 - s).abs() + (s + e10)) * 0.5;
                    let v570 = (v552 * -1.01) / den;
                    let part_brk = w
                        .ent(o)
                        .parts
                        .get(v432 as usize)
                        .is_some_and(|pt| pt.flags & 0x80_0000 != 0);
                    if v539 != 0 || sf & 0x10 != 0 || part_brk {
                        super::solver::collision_event(
                            w,
                            e,
                            p,
                            hp,
                            n,
                            o,
                            v432,
                            v829,
                            v539 as f32 * v570,
                            false,
                            v443,
                        );
                    }
                    let mut imp = Vec3::new(idir.x * v570, idir.y * v570, idir.z * v570);
                    if len2(imp) * (self_minv * self_minv)
                        > ((1.0 - v552 * v552).abs() + (v552 * v552 + 1.0)) * 0.5
                    {
                        let s = (v552.abs() * ld(w, e).mass) * 0.5;
                        let nn = norm(imp);
                        imp = Vec3::new(s * nn.x, s * nn.y, s * nn.z);
                    }
                    if v539 != 0 {
                        let m = self_minv;
                        vdes =
                            Vec3::new(vdes.x + imp.x * m, vdes.y + imp.y * m, vdes.z + imp.z * m);
                        vnew = vdes;
                        let l = lm(w, e);
                        if l.inertia == 0.0 && ((sf & 0x200) as i32 | (osc - 3)) != 0 {
                            l.impulse_recover = l.impulse_recover_time;
                        }
                    }
                    if v544 {
                        let mut j = Vec3::new(-imp.x, -imp.y, -imp.z);
                        if (hn.x * up.x + hn.y * up.y + up.z * hn.z).abs() < 0.699_999_99 {
                            let v580 = (j.x * up.x + j.y * up.y) + j.z * up.z;
                            j = Vec3::new(j.x - v580 * up.x, j.y - up.y * v580, j.z - up.z * v580);
                        }
                        let at = 2 * i32::from(osc < 3);
                        if w.ent(o).etype < 4 {
                            j = Vec3::new(j.x * 0.2, j.y * 0.2, j.z * 0.2);
                        }
                        ground::apply_impulse(w, o, j, hp, v432, at, 0);
                        if w.ent(o).sim_class() < 3 {
                            lm(w, e).push_time = 0.0;
                        }
                    }
                    // creases
                    let v581 = v846;
                    let mut v582 = 0usize;
                    while v582 < v846 {
                        let pl = planes[v582];
                        if (hn.y * pl.y + hn.x * pl.x) + hn.z * pl.z >= 0.949_999_99 {
                            break;
                        }
                        v582 += 1;
                    }
                    if v582 == v846 {
                        nsum = Vec3::new(nsum.x + hn.x, nsum.y + hn.y, nsum.z + hn.z);
                    }
                    planes[v846] = hn;
                    v586 = v826 + v523;
                    v827 -= v523;
                    v826 = v586;
                    if v581 == 1 && v586 == 0.0 {
                        let (a, b) = (planes[0], planes[1]);
                        let c = Vec3::new(
                            a.y * b.z - a.z * b.y,
                            a.z * b.x - b.z * a.x,
                            b.y * a.x - a.y * b.x,
                        );
                        let cn = norm(c);
                        hn = cn;
                        let v601 = (dir.x * cn.x + cn.y * dir.y) + dir.z * cn.z;
                        dir = Vec3::new(v601 * cn.x, cn.y * v601, cn.z * v601);
                    } else {
                        let v602 = (dir.x * hn.x + hn.y * dir.y) + dir.z * hn.z;
                        let d = Vec3::new(
                            dir.x - v602 * hn.x,
                            dir.y - hn.y * v602,
                            dir.z - hn.z * v602,
                        );
                        let v606 = len2(d).sqrt() * 0.02;
                        dir = Vec3::new(
                            d.x * 0.999_800_03 + v606 * hn.x,
                            d.y * 0.999_800_03 + hn.y * v606,
                            d.z * 0.999_800_03 + v606 * hn.z,
                        );
                    }
                    let v607 = len2(dir).sqrt();
                    let mut v608 = v827 * v607;
                    v827 = v608;
                    if v607 > 0.0 {
                        let rr = 1.0 / v607;
                        dir = Vec3::new(dir.x * rr, dir.y * rr, dir.z * rr);
                    }
                    if (m427 * dir.x + dir.y * m426) + dir.z * m428 < 0.0 {
                        v608 = 0.0;
                        v827 = 0.0;
                    }
                    if v608 <= eps * 0.1 {
                        break;
                    }
                    v846 = v581 + 1;
                    if v846 < 3 {
                        v430 = v827;
                        continue;
                    }
                    break;
                }
                p = Vec3::new(p.x + dir.x * v827, p.y + dir.y * v827, p.z + dir.z * v827);
                v586 = v826 + v827;
                break;
            }
            let _ = v430;
            let v609 = (nsum.x * nsum.x + nsum.y * nsum.y) + nsum.z * nsum.z;
            if v586 < v840 * 0.001
                && (nsum.z.abs() * nsum.z > v609 * 0.160_000_01 || v609 < 0.990_000_01)
            {
                lm(w, e).flags |= lflags::STUCK;
            }
            // ---- 8. the final static overlap check ----
            if !(w.ent(e).parts[0].flags_collider & 2 == 0
                || (v997 == 0 && w.ent(e).flags & 8 != 0))
            {
                reset_params(&mut ip);
                let pp0 = w.ent(e).parts[0].pos;
                g_own.offset = place(&r, pp0, p);
                g_own.v = Vec3::new(-up.x, -up.y, -up.z);
                ip.time_interval = size.z * 10.0;
                let mut found: Option<f64> = None;
                'recs: for rec in &recs {
                    if rec.simclass != 0 {
                        continue;
                    }
                    for kp in rec.pstart..rec.pend {
                        let pr = &parts[kp as usize];
                        if pr.flags & 2 == 0 {
                            continue;
                        }
                        let g1 = pr.gwd();
                        if intersect_wb(
                            collider.as_ref(),
                            pr.geom.as_ref(),
                            &mut g_own,
                            &g1,
                            &mut ip,
                            &mut contacts,
                        ) != 0
                        {
                            found = Some(contacts[0].t);
                            break 'recs;
                        }
                    }
                }
                if let Some(t) = found {
                    if f64::from(eps) < t {
                        lm(w, e).flags |= lflags::STUCK;
                        vdes = Vec3::ZERO;
                        vnew = Vec3::ZERO;
                    }
                    p = p_before;
                    lm(w, e).lock_timer = 1.0;
                }
            }
        }
        let _ = v586;
        dbg(|| {
            format!(
                "after sweep p={p:?} vdes={vdes:?} stuck={}",
                ld(w, e).flags & 0x100
            )
        });
        // ---- 9. ground ----
        let gv_save = if ld(w, e).ground.is_none() {
            Vec3::ZERO
        } else {
            ld(w, e).ground_vel
        };
        let gh = ground::ground_probe(w, e, &recs, &parts, p, false, true, true);
        let v630 = gh.height;
        let mut gn = gh.n;
        lm(w, e).ground_height = v630;
        dbg(|| format!("ground v630={v630} n={gn:?} g={:?}", ld(w, e).ground));
        if (gn.x * up.x + gn.y * up.y) + gn.z * up.z >= 0.086_999_997 {
            lm(w, e).ground_normal = gn;
        } else {
            gn = ld(w, e).ground_normal;
        }
        let g = ld(w, e).ground;
        if !g.is_none() {
            let upq = q.rotate(Vec3::new(0.0, 0.0, 1.0));
            let (gip, s0, s2) = {
                let l = ld(w, e);
                (l.ground_ipart, l.ground_surface[0], l.ground_surface[2])
            };
            super::solver::collision_event(w, e, endp, endp, upq, g, gip, s0, 0.0, true, s2);
            if w.ent(g).id_bits & 0xFF00_0000 == 0 {
                super::release_ground(w, e);
            }
        }
        if v817 != 0 {
            lm(w, e).time_flying += dt;
        }
        let l = ld(w, e);
        let v648 = l.cos_fall - ((gn.x * up.x + gn.y * up.y) + gn.z * up.z);
        let v650 = (up.x * p.x + up.y * p.y) + p.z * up.z;
        let v651 = (v650 - pivot)
            - ((((0.004_000_000_2 - size.z * 0.01).abs() + (size.z * 0.01 + 0.004_000_000_2))
                * 0.5)
                + v630);
        let v652 = (mx(v651, v648).to_bits() >> 31) != 0;
        let gdot = (up.y * l.gravity.y + up.x * l.gravity.x) + up.z * l.gravity.z;
        v653 = if gdot > 0.0
            || l.flags & lflags::SWIMMING != 0
            || !(v652 || l.flags & lflags::STUCK != 0)
        {
            1
        } else {
            0
        };
        {
            let l = lm(w, e);
            let f = l.flags;
            l.flags = f ^ ((f ^ (f >> 1)) & 0x80);
        }
        v817 = v653;
        if v653 != 0 {
            vnew = airborne_vel(w, e, vdes, gv_save, flying0 != 0, k, dt);
        } else {
            let lf = ld(w, e).flags;
            let v670;
            let mut v658 = vdes.y;
            let mut v642 = vdes.x;
            let mut vz = vdes.z;
            let mut skip_to_532 = flying0 == 0 || lf & lflags::SWIMMING != 0;
            if !skip_to_532 {
                let v659 = v650 - pivot;
                let v660 = (up.x * v642 + up.y * v658) + up.z * vz;
                if v660 < -4.0 && lf & lflags::NO_LOCK == 0 && ld(w, e).nod_speed > 0.0 {
                    let l = lm(w, e);
                    l.eye_vel = v660;
                    let v661 = (v660 * v660) / (l.size.z * 2.0);
                    l.eye_acc = ((v661 - l.nod_speed).abs() + (v661 + l.nod_speed)) * 0.5;
                }
                let l = ld(w, e);
                let v664 = l.cos_fall;
                let n = l.ground_normal;
                let v665 = (up.x * n.x + up.y * n.y) + up.z * n.z;
                let mut path_530 = false;
                let mut path_522 = false;
                if v665 >= v664 {
                    path_522 = true;
                } else if v660 >= -4.0 || lf & lflags::STUCK != 0 {
                    if v665 < v664 {
                        path_530 = true;
                    } else {
                        path_522 = true;
                    }
                } else {
                    let v667 = (n.x * v642 + n.y * v658) + n.z * vz;
                    v642 -= n.x * v667;
                    v658 -= n.y * v667;
                    vz -= n.z * v667;
                    let v669 = (up.x * v642 + up.y * v658) + up.z * vz;
                    if v669 > 0.0 {
                        v642 -= v669 * up.x;
                        v658 -= v669 * up.y;
                        vz -= v669 * up.z;
                    }
                    v653 = 1;
                    v817 = 1;
                    skip_to_532 = true;
                }
                if !skip_to_532 {
                    if path_522 {
                        if (v652 && len2(l.vel_requested) <= 0.0)
                            || (v665 <= l.cos_climb && v660 >= 0.0)
                        {
                            v642 = 0.0;
                            v658 = 0.0;
                            vz = 0.0;
                        } else {
                            path_530 = true;
                        }
                    }
                    if path_530 {
                        let v673 = (n.x * v642 + n.y * v658) + n.z * vz;
                        v642 -= n.x * v673;
                        v658 -= n.y * v673;
                        vz -= n.z * v673;
                    }
                    if v659 < v630 && v659 > v630 - (hc - size.z) {
                        let v671 = (v630 + pivot) - v650;
                        p = Vec3::new(p.x + v671 * up.x, p.y + v671 * up.y, p.z + v671 * up.z);
                    }
                }
            }
            v670 = vz;
            vdes = Vec3::new(v642, v658, v670);
            // LABEL_533: the ground acceleration
            let l = ld(w, e);
            let n = l.ground_normal;
            let mut vr = l.vel_requested;
            if l.flags & lflags::SWIMMING == 0 {
                let v679 = (vr.x * n.x + n.y * vr.y) + n.z * vr.z;
                vr = Vec3::new(vr.x - n.x * v679, vr.y - n.y * v679, vr.z - n.z * v679);
            }
            let v680 = if k * dt <= 1.0 { k } else { 1.0 / dt };
            let v690 = ((up.y * n.y) + up.x * n.x) + up.z * n.z;
            let d = Vec3::new(vr.x - v642, vr.y - v658, vr.z - v670);
            let mut st = Vec3::new(d.x * v680, d.y * v680, d.z * v680);
            let v689 = st.x * dt;
            let v1201 = st.z * dt;
            let v691 = (d.y * d.y + d.x * d.x) + d.z * d.z;
            let v696 = dt;
            if !((d.y * (st.y * dt) + d.x * v689) + d.z * v1201 <= v691) {
                let v692 = v691.sqrt();
                if v692 > 0.0 {
                    let rr = 1.0 / v692;
                    let u = Vec3::new(d.x * rr, d.y * rr, d.z * rr);
                    let v697 = (u.y * (st.y * dt) + u.x * v689) + u.z * v1201;
                    st = Vec3::new(
                        ((v689 - v697 * u.x) + d.x) * (1.0 / dt),
                        (((st.y * dt) - v697 * u.y) + d.y) * (1.0 / dt),
                        ((v1201 - v697 * u.z) + d.z) * (1.0 / dt),
                    );
                }
            }
            if v690 < l.cos_slide && l.flags & lflags::SWIMMING == 0 {
                let g = l.gravity;
                let v702 = (n.x * g.x + n.y * g.y) + n.z * g.z;
                st = Vec3::new(
                    st.x + (g.x - n.x * v702),
                    st.y + (g.y - n.y * v702),
                    st.z + (g.z - n.z * v702),
                );
            }
            let mut v = Vec3::new(st.x * v696 + v642, st.y * v696 + v658, st.z * v696 + v670);
            if (v.x * v642 + v.y * v658) + v.z * v670 < 0.0
                && (l.vel_requested.y * v.y + l.vel_requested.x * v.x) + l.vel_requested.z * v.z
                    <= 0.0
            {
                v = Vec3::ZERO;
            }
            if v690 < l.cos_climb {
                let v703 = (up.x * v.x + up.y * v.y) + up.z * v.z;
                if v703 > 0.0 && (st.x * up.x + st.y * up.y) + st.z * up.z > 0.0 {
                    v = Vec3::new(v.x - v703 * up.x, v.y - v703 * up.y, v.z - v703 * up.z);
                }
                let dz = ((p.y - p_before.y) * up.y + (p.x - p_before.x) * up.x)
                    + (p.z - p_before.z) * up.z;
                if dz > size.z * 0.001 {
                    let v704 = (up.x * v.x + up.y * v.y) + up.z * v.z;
                    v = Vec3::new(v.x - v704 * up.x, v.y - v704 * up.y, v.z - v704 * up.z);
                }
            }
            if v690 < l.cos_fall && l.flags & lflags::STUCK == 0 {
                v653 = 1;
                v817 = 1;
                v = Vec3::new(
                    v.x + (n.x - v690 * up.x),
                    v.y + (n.y - v690 * up.y),
                    v.z + (n.z - v690 * up.z),
                );
            }
            let s = (v.x * v.x + v.y * v.y) + v.z * v.z;
            if (len2(l.vel_requested) == 0.0 && s < 0.001) || s < 0.000_099_999_997 {
                v = Vec3::ZERO;
            }
            vnew = v;
        }
        // LABEL_563
        if v653 == 0 {
            lm(w, e).time_flying = 0.0;
        }
        if w.ent(e).flags & 4 != 0 {
            vnew = super::quantize_vel(vnew);
        }
        if ld(w, e).flags & lflags::NO_LOCK == 0 {
            let l = lm(w, e);
            if v653 == 0 {
                let v708 = ((p.y - p_before.y) * up.y + (p.x - p_before.x) * up.x)
                    + (p.z - p_before.z) * up.z;
                if v708 > l.size.z * 0.01 {
                    let a = v708 / l.eye_timer;
                    l.eye_vel = ((l.eye_vel - a).abs() + (a + l.eye_vel)) * 0.5;
                    let v709 = v708 + l.eye_offset;
                    l.eye_offset = v709;
                    if v709 > l.size.z {
                        l.eye_offset = l.size.z;
                    }
                    l.eye_timer = 0.0;
                }
            }
            let t = dt + l.eye_timer;
            l.eye_timer = ((t + 0.5) - (t - 0.5).abs()) * 0.5;
            let v711 = dt * l.eye_acc + l.eye_vel;
            l.eye_vel = v711;
            let v712 = l.eye_acc;
            let v713 = l.eye_offset;
            if (v712 == 0.0 && v711 * v713 < 0.0)
                || v713 * v712 < 0.0
                || (v713 - v711 * dt) * v713 < 0.0
            {
                l.eye_vel = 0.0;
                l.eye_acc = 0.0;
                l.eye_offset = 0.0;
            } else {
                l.eye_offset = v713 - v711 * dt;
            }
        }
        if ld(w, e).head_r > 0.0 {
            head_check(w, e, &kept, &r, p, up, tid);
        }
        let bb = super::bbox_at(w, e, p);
        w.reposition_entity(e, 1, Some(bb));
        super::commit_pos(w, e, p, bb);
        v8 = 1;
        let _ = v850;
    }
    // ---- LABEL_607: finish ----
    let pos = w.ent(e).pos;
    {
        let l = lm(w, e);
        l.dvel = Vec3::new(l.vel.x - vel0.x, l.vel.y - vel0.y, l.vel.z - vel0.z);
        l.vel = Vec3::new(
            (vnew.x + l.vel.x) - vel0.x,
            (vnew.y + l.vel.y) - vel0.y,
            (vnew.z + l.vel.z) - vel0.z,
        );
        let inv = 1.0 / dt;
        l.disp_vel = Vec3::new(
            inv * (pos.x - pos0.x),
            (pos.y - pos0.y) * inv,
            (pos.z - pos0.z) * inv,
        );
        l.flags = (l.flags & !1) | (v653 & 1);
    }
    let qn = w.ent(e).q;
    {
        let l = lm(w, e);
        l.dq = qn.product(q0.conj());
        if !(-dt >= 0.0) {
            l.last_dt = dt;
        }
    }
    if w.flagged_step != 0 {
        let l = lm(w, e);
        l.net_err = Vec3::new(
            l.net_target.x - pos.x,
            l.net_target.y - pos.y,
            l.net_target.z - pos.z,
        );
        let s = (l.net_err.x * l.net_err.x + l.net_err.y * l.net_err.y) + l.net_err.z * l.net_err.z;
        if s < 0.000_099_999_997 || s > 4.0 {
            l.net_err = Vec3::ZERO;
        }
    }
    if let Some(pb) = ld(w, e).proxy {
        if !w.ent(e).colliders.is_empty() {
            let (hc, mass, mass_inv, vel) = {
                let l = ld(w, e);
                (l.height_collider, l.mass, l.mass_inv, l.vel)
            };
            let rr = qn.rotate(Vec3::new(0.0, 0.0, hc));
            let b = w.bodies.get_mut(pb);
            b.pos = Vec3::new(rr.x + pos.x, rr.y + pos.y, rr.z + pos.z);
            b.q = qn;
            b.m = mass;
            b.v = vel;
            let m = b.m;
            b.p = Vec3::new(vel.x * m, vel.y * m, vel.z * m);
            b.minv = mass_inv;
            b.l = Vec3::ZERO;
            b.w = Vec3::ZERO;
        } else {
            w.bodies.release(pb);
            lm(w, e).proxy = None;
        }
    }
    if ld(w, e).flags & lflags::NO_LOCK == 0 {
        let ef = w.ent(e).flags;
        if ef & 0x808_0000 != 0 {
            let eps2 = w.vars.living_eps2;
            let ent = w.ent(e);
            let (p2, q2) = (ent.pos2, ent.q2);
            if (p2.x - pos.x).abs() > eps2
                || (p2.y - pos.y).abs() > eps2
                || (p2.z - pos.z).abs() > eps2
                || !quat_close(q2, qn, eps2 * eps2)
            {
                let ent = w.ent_mut(e);
                ent.pos2 = pos;
                ent.q2 = qn;
                let step = w.step_counter;
                w.signal_event(
                    ef,
                    Event::PostStep {
                        ent: e,
                        dt,
                        pos,
                        q: qn,
                        step,
                    },
                );
            }
        }
    }
    let _ = v8;
    1
}

/// `sub_35039510(a, b, eps)`: quaternions equal within `eps` per component (or opposite).
fn quat_close(a: Quat, b: Quat, eps: f32) -> bool {
    let same = (a.x - b.x).abs() <= eps
        && (a.y - b.y).abs() <= eps
        && (a.z - b.z).abs() <= eps
        && (a.w - b.w).abs() <= eps;
    let opp = (a.x - -b.x).abs() <= eps
        && (a.y - -b.y).abs() <= eps
        && (a.z - -b.z).abs() <= eps
        && (a.w - -b.w).abs() <= eps;
    same || opp
}

/// `sub_35027BB0(&v, groundVel, bWasFlying, k, dt)`: the airborne velocity: the ground
/// velocity is added when the entity was on the ground and has no ground collider; gravity
/// (plus the swim drag towards velRequested, scaled by `k`), then air resistance (dropped when
/// it would overshoot past zero); tiny velocities are zeroed.
pub fn airborne_vel(
    w: &World,
    e: EntIdx,
    v: Vec3,
    gv: Vec3,
    was_flying: bool,
    k: f32,
    dt: f32,
) -> Vec3 {
    let l = ld(w, e);
    let mut a = v;
    if !was_flying && l.ground.is_none() {
        a = Vec3::new(gv.x + a.x, gv.y + a.y, gv.z + a.z);
    }
    let mut g = l.gravity;
    if l.flags & lflags::SWIMMING != 0 {
        g.x += (l.vel_requested.x - a.x) * k;
        g.z += (l.vel_requested.z - a.z) * k;
        g.y += (l.vel_requested.y - a.y) * k;
    }
    let v12 = g.x * dt + a.x;
    let v13 = g.y * dt + a.y;
    let v14 = g.z * dt + a.z;
    a = Vec3::new(v12, v13, v14);
    let kr = l.air_resistance;
    let v16 = (-v12 * kr) * dt;
    let v17 = (-v13 * kr) * dt;
    let v18 = (-v14 * kr) * dt;
    if (v17 * v17 + v16 * v16) + v18 * v18 >= ((v13 * v13 + v12 * v12) + v14 * v14) * 4.0 {
        if (v17 * a.y + a.x * v16) + v18 * a.z < 0.0 {
            a = Vec3::ZERO;
        }
    } else {
        a = Vec3::new(v12 + v16, v13 + v17, v14 + v18);
    }
    if (a.y * a.y + a.x * a.x) + a.z * a.z < 1.0e-10 {
        a = Vec3::ZERO;
    }
    a
}

/// The head-sphere ceiling check of Step: the head sphere swept upward over
/// `heightHead − heightCollider − min(eyeOffset, 0)` against the parts (flag 2) of the static
/// neighbours; the eye offset is limited to stay under the nearest hit.
fn head_check(
    w: &mut World,
    e: EntIdx,
    kept: &[EntIdx],
    r: &[[f32; 3]; 3],
    p: Vec3,
    up: Vec3,
    _tid: usize,
) {
    let mut ip = IntersectionParams {
        sweep_test: true,
        ..IntersectionParams::default()
    };
    let pp0 = w.ent(e).parts[0].pos;
    let mut g_own = GeomWorldData {
        offset: place(r, pp0, p),
        r: Mat33 { m: *r },
        scale: 1.0,
        v: up,
        ..GeomWorldData::default()
    };
    let l = ld(w, e);
    let eo = l.eye_offset;
    let v721 = (l.height_head - l.height_collider) - (eo - eo.abs()) * 0.5;
    ip.time_interval = v721;
    let mut v722 = v721;
    let head = l.head.clone();
    let mut contacts = Vec::new();
    for &o in kept {
        if w.ent(o).id_bits & 0xFF00_0000 != 0 {
            continue;
        }
        let np = w.ent(o).parts.len();
        for j in 0..np {
            let (g1, geom) = {
                let oe = w.ent(o);
                let pt = &oe.parts[j];
                if pt.flags & 2 == 0 {
                    continue;
                }
                let qp = oe.q.product(pt.q);
                let m = Mat33::from_quat(qp);
                let rr = &m.m;
                let pp = pt.pos;
                let off = Vec3::new(
                    ((rr[0][0] * pp.x + rr[0][1] * pp.y) + rr[0][2] * pp.z) + oe.pos.x,
                    ((rr[1][0] * pp.x + rr[1][1] * pp.y) + rr[1][2] * pp.z) + oe.pos.y,
                    ((rr[2][0] * pp.x + rr[2][1] * pp.y) + rr[2][2] * pp.z) + oe.pos.z,
                );
                (
                    GeomWorldData {
                        offset: off,
                        r: m,
                        scale: pt.scale,
                        ..GeomWorldData::default()
                    },
                    pt.pgeom_proxy.geom.clone(),
                )
            };
            if intersect_wb(
                head.as_ref(),
                geom.as_ref(),
                &mut g_own,
                &g1,
                &mut ip,
                &mut contacts,
            ) != 0
            {
                let t = contacts[0].t as f32;
                v722 = ((t + v722) - (v722 - t).abs()) * 0.5;
            }
        }
    }
    let l = lm(w, e);
    let v744 = l.eye_offset;
    let lim = ((v744 - v744.abs()) * 0.5 + v721) - v722;
    if v744 < lim || (l.eye_acc.abs() + l.eye_vel.abs()) == 0.0 {
        l.eye_offset = lim;
    }
}

/// The flagged-static pre-check of the neighbour gathering (entity flags `0x60000000`): parts
/// with a bbox diagonal over 1 are tested at the start and swept over the move; an entity is
/// dropped when the start overlaps or the sweep hits a part whose centre the move approaches
/// (flag `0x20000000`: recedes from).
fn static_moving_away(
    w: &mut World,
    e: EntIdx,
    o: EntIdx,
    p: Vec3,
    mv: Vec3,
    mlen: f32,
    dt: f32,
    pos0: Vec3,
    q: Quat,
) -> bool {
    let collider = ld(w, e).collider.geom.clone();
    let r = if q.y * q.y + q.x * q.x >= 0.000_001_000_000_1 {
        Mat33::from_quat(q)
    } else {
        Mat33::IDENTITY
    };
    let pp0 = w.ent(e).parts[0].pos;
    let np = w.ent(o).parts.len();
    let mut contacts = Vec::new();
    for j in 0..np {
        let (pb, g1, geom) = {
            let oe = w.ent(o);
            let pt = &oe.parts[j];
            let qp = oe.q.product(pt.q);
            let rr = oe.q.rotate(pt.pos);
            (
                pt.bbox,
                GeomWorldData {
                    offset: Vec3::new(oe.pos.x + rr.x, oe.pos.y + rr.y, oe.pos.z + rr.z),
                    r: Mat33::from_quat(qp),
                    scale: pt.scale,
                    ..GeomWorldData::default()
                },
                pt.pgeom_proxy.geom.clone(),
            )
        };
        let d = Vec3::new(pb[1].x - pb[0].x, pb[1].y - pb[0].y, pb[1].z - pb[0].z);
        if !(((d.x * d.x) + d.y * d.y) + d.z * d.z > 1.0) {
            continue;
        }
        let mut ip = IntersectionParams {
            time_interval: dt * 200.0,
            ..IntersectionParams::default()
        };
        let mut g = GeomWorldData {
            offset: place(&r.m, pp0, p),
            r,
            scale: 1.0,
            ..GeomWorldData::default()
        };
        if intersect_wb(
            collider.as_ref(),
            geom.as_ref(),
            &mut g,
            &g1,
            &mut ip,
            &mut contacts,
        ) != 0
        {
            return true;
        }
        let inv = 1.0 / mlen;
        g.v = Vec3::new(mv.x * inv, inv * mv.y, inv * mv.z);
        g.offset = place(&r.m, pp0, Vec3::new(p.x + mv.x, p.y + mv.y, p.z + mv.z));
        let n = intersect_wb(
            collider.as_ref(),
            geom.as_ref(),
            &mut g,
            &g1,
            &mut ip,
            &mut contacts,
        );
        if n != 0 {
            let c = &contacts[n - 1];
            let cx = (pb[1].x - pb[0].x) * 0.5 + pb[0].x;
            let cy = (pb[1].y - pb[0].y) * 0.5 + pb[0].y;
            let v164 = (c.pt.y - cy) * (c.pt.y - cy) + (c.pt.x - cx) * (c.pt.x - cx);
            let v165 = (pos0.y - cy) * (pos0.y - cy) + (pos0.x - cx) * (pos0.x - cx);
            if w.ent(o).flags & 0x2000_0000 != 0 {
                if v164 <= v165 {
                    return true;
                }
            } else if v164 >= v165 {
                return true;
            }
        }
    }
    false
}
