//! Collider gathering and narrow-phase contact detection of `CRigidEntity`:
//! `GetPotentialColliders` (slot 81, 0x350B2B90), `CheckForNewContacts` (0x350B3440),
//! `CheckContactConflicts` (0x350B9880) and their helpers.
//!
//! The narrow-phase results live in the thread's intersection arena
//! ([`ThreadScratch`], one per physics thread, `World::tscratch`) as raw 136-byte
//! `geom_contact` records, exactly as the original keeps them (`ip+128` = the arena's contact
//! base); the per-thread table `qword_35218710[tid]` that pairs each record with its collider
//! and part indices is [`ContactScratch`].

use super::contacts::ContactScratch;
use crate::collision::contact::IntersectionParams;
use crate::collision::prim_intersect::scratch::{c, ThreadScratch};
use crate::collision::prim_intersect::{intersect, shared_env, IntersectOut};
use crate::entities::base::Part;
use crate::geometry::{GeomWorldData, Geometry};
use crate::math::{Mat33, Quat, Vec3};
use crate::world::events::Event;
use crate::world::{EntIdx, World};

/// `sub_35038180`: strict overlap of two `[min, max]` boxes written with the max idiom:
/// `max(max(dx, dy), dz) < 0` with `d = |c_a − c_b|·2 − size_a − size_b` per axis (the
/// operands kept in the binary's order).
#[must_use]
pub fn boxes_overlap(a: &[Vec3; 2], b: &[Vec3; 2]) -> bool {
    let (a0, a1, b0, b1) = (a[0], a[1], b[0], b[1]);
    let v2 = (((a0.y + a1.y) - b0.y) - b1.y).abs() - (a1.y - a0.y) - (b1.y - b0.y);
    let v3 = (((a0.x + a1.x) - b0.x) - b1.x).abs() - (a1.x - a0.x) - (b1.x - b0.x);
    let v4 = ((v3 - v2).abs() + (v3 + v2)) * 0.5;
    let v5 = (((a0.z + a1.z) - b0.z) - b1.z).abs() - (a1.z - a0.z) - (b1.z - b0.z);
    (((v4 - v5).abs() + (v5 + v4)) * 0.5) < 0.0
}

/// `sub_35037930(ent, id, ipart)`: map a geometry material id through the part's material
/// table. Negative ids take the part's surface index (`+96` low 10 bits, sign-extended); with
/// a table (`+104`, the part's own or the `phys_geometry`'s) ids below the table size
/// (`+100` bits 0–30) are looked up and larger ids use entry 0. Parts past the end return
/// `id` unchanged.
#[must_use]
pub fn map_material(w: &World, e: EntIdx, id: i32, ipart: i32) -> i32 {
    let ent = w.ent(e);
    if ipart >= ent.parts.len() as i32 {
        return id;
    }
    let p = &ent.parts[ipart as usize];
    let surf = ((p.surface_bits << 22) as i32) >> 22;
    let v7 = ((id >> 31) & surf.wrapping_sub(id)).wrapping_add(id);
    let table: Option<&[i32]> = match &p.mat_mapping {
        Some(m) => Some(m.as_slice()),
        None if !p.pgeom.mat_mapping.is_empty() => Some(p.pgeom.mat_mapping.as_slice()),
        None => None,
    };
    match table {
        None => v7,
        Some(t) => {
            let n = (p.nmats_bits & 0x7FFF_FFFF) as i32;
            let idx = v7 & (v7.wrapping_sub(n) >> 31);
            // a negative index reads before the table in the original (not reachable with
            // the surface index range 0..1023 and a non-negative id)
            t.get(idx as usize).copied().unwrap_or(0)
        }
    }
}

/// Slot 52 `HasCollisionContactsWith(pent)` (0x350A9C80): the collider exists and has a
/// first contact. Base classes return 0.
#[must_use]
pub fn has_collision_contacts_with(w: &World, e: EntIdx, other: EntIdx) -> bool {
    let ent = w.ent(e);
    let Some(r) = ent.rigid() else {
        return false;
    };
    match ent.colliders.iter().position(|&c| c == other) {
        Some(i) => !r.collider_contacts[i].is_none(),
        None => false,
    }
}

/// `sub_3507A140(world, ent, dt)`: queue `ent` for the post-step awake-list job with step
/// `dt` (flag `0x40000000` of `+92` marks membership; a queued entity keeps the smaller dt in
/// `+252`).
pub fn queue_awake(w: &mut World, e: EntIdx, dt: f32) {
    let head = w.awake_list;
    let ent = w.ent_mut(e);
    if ent.flags & 0x4000_0000 != 0 {
        let t = ent.time_idle;
        ent.time_idle = ((t + dt) - (t - dt).abs()) * 0.5;
    } else {
        ent.flags |= 0x4000_0000;
        ent.job_next = head;
        ent.time_idle = dt;
        w.awake_list = e;
    }
}

/// Slot 81 `GetPotentialColliders(&list, dt)` (0x350B2B90) → the number of colliders kept
/// at the front of the returned list.
///
/// The query box is the new bbox (`+476`) swept by `v·timeStepFull` and inflated by
/// `4·contact_gap` when the entity had an external contact recently (`+1104 < 0`). The
/// entities found are filtered **in place** in the caller's cached `GetEntitiesInBox` list
/// (the original compacts the array the cache points at, so a later identical query of the
/// same caller returns the compacted prefix followed by the stale tail; mirrored by writing
/// the prefix back into [`World::box_cache`]).
///
/// * simclass > 2 neighbours (living, independent, …) are woken and queued for the awake list
///   job (`sub_3507A140`) unless their simclass is 3; articulated entities (type 6) would be
///   collided with `sub_350B2220` instead — articulated entities are not ported, so that
///   branch is unreachable.
/// * a static body (`Minv == 0`) keeps nothing and wakes its neighbours.
/// * colliders of the same island processed later (`W+848` order) are skipped; when either
///   body is fast the original spin-waits for the other thread (`sub_350CCB40`, a no-op
///   single-threaded).
///
/// Mirrored quirk: the `flags92 & 1` pre-pass adds `unk_380 & ((3 − n) >> 31) & …` to the
/// colliders' `+64` words; `(3 − n) >> 31` (unsigned) is always 0 there, so the pass adds
/// nothing and is omitted.
pub fn get_potential_colliders(w: &mut World, e: EntIdx, dt: f32) -> (Vec<EntIdx>, i32) {
    let (bv, bw, minv) = {
        let b = w.bodies.get(w.ent(e).rigid().expect("rigid").body);
        (b.v, b.w, b.minv)
    };
    if !(((((bv.x * bv.x) + (bv.y * bv.y)) + (bv.z * bv.z)) + minv)
        + (((bw.x * bw.x) + (bw.y * bw.y)) + (bw.z * bw.z))
        > 0.0)
    {
        return (Vec::new(), 0);
    }
    let tid = w.tls_thread;
    let (lo, hi) = {
        let ent = w.ent(e);
        let r = ent.rigid().expect("rigid");
        let v8 = r.time_step_full;
        let v11 = ((r.last_ext_contact_step as u32) >> 31) as f32 * (w.vars.contact_gap * 4.0);
        let s = |x: f32| (x * v8, (-(x * v8)).abs());
        let (sx, ax) = s(bv.x);
        let (sy, ay) = s(bv.y);
        let (sz, az) = s(bv.z);
        let b = r.bbox_new;
        let hi = Vec3::new(
            (b[1].x + (ax + sx) * 0.5) + v11,
            (b[1].y + (ay + sy) * 0.5) + v11,
            (b[1].z + (az + sz) * 0.5) + v11,
        );
        let lo = Vec3::new(
            (b[0].x + (sx - ax) * 0.5) - v11,
            (b[0].y + (sy - ay) * 0.5) - v11,
            (b[0].z + (sz - az) * 0.5) - v11,
        );
        (lo, hi)
    };
    let mut list = w.get_entities_in_box(lo, hi, 131_423, Some(e), tid as usize);
    let n = list.len();
    let bit = 1u32 << tid;
    let mask = mark_constraint_buddies(w, e, tid);
    {
        let r = w.ent_mut(e).rigid_mut().expect("rigid");
        r.flags2 &= 0xFC03_FFFF;
    }
    let swept = [lo, hi];
    let mut kept = 0usize;
    for i in 0..n {
        let c = list[i];
        if w.ent(c).processed & bit != 0 {
            continue;
        }
        if (w.ent(c).id_bits & 0xFF00_0000) as i32 > 0x0200_0000 {
            // simclass > 2: wake it (articulated entities would be collided instead)
            crate::entities::awake(w, c, 1, 0);
            if w.ent(c).id_bits & 0xFF00_0000 != 0x0300_0000 {
                let last_dt = w.ent(e).rigid().expect("rigid").last_dt;
                queue_awake(w, c, last_dt);
                let r = w.ent_mut(e).rigid_mut().expect("rigid");
                r.flags2 ^= (r.flags2 ^ ((r.flags2 & 0xFFFC_0000).wrapping_add(0x40000))) & 0x3FC_0000;
            }
            continue;
        }
        if minv > 0.0 {
            let cb = w.ent(c).bbox;
            let d = |a: f32, b: f32| (a - b) * (a - b);
            if (d(cb[1].y, cb[0].y) + d(cb[1].x, cb[0].x)) + d(cb[1].z, cb[0].z) != 0.0
                && !boxes_overlap(&swept, &cb)
            {
                continue;
            }
            if c == e {
                continue;
            }
            let (cg, eg) = (w.ent(c).group_idx, w.ent(e).group_idx);
            let same = i32::from(cg == eg);
            if (i32::from(w.ent(c).group_mark) & same) == 0 && cg != -1 {
                let f1 = (((-same) as u8) & ((w.ent(e).flags | w.ent(c).flags) as u8) & 1) as i32;
                if (f1 | crate::entities::is_awake(w, c)) != 0 {
                    let ord = &w.group_848;
                    if ord[cg as usize] >= ord[eg as usize] {
                        continue;
                    }
                    // fast bodies: wait for the other thread (sub_350CCB40 → 1)
                }
            }
            list[kept] = c;
            kept += 1;
            if w.ent(e).rigid().expect("rigid").last_ext_contact_step < 0 {
                crate::entities::awake(w, c, 1, 0);
            }
        } else if c != e {
            crate::entities::awake(w, c, 1, 0);
        }
    }
    if w.ent(e).flags & 1 != 0 {
        let cols = w.ent(e).colliders.clone();
        for c in cols {
            let p = &mut w.ent_mut(c).processed;
            *p = p.wrapping_sub(bit & *p);
        }
    }
    unmark_constraint_buddies(w, e, mask, tid);
    // the compaction happened in the cached array
    let cache = &mut w.box_cache[tid as usize].list;
    if cache.len() == n {
        cache[..kept].copy_from_slice(&list[..kept]);
    }
    (list, kept as i32)
}

/// `sub_350AB3D0`: set bit `tid` of `+64` on colliders that have a constraint with `e` and on
/// the constraint buddies; returns the OR of the per-collider constraint masks. Constraints
/// are not ported (slot 45 `HasConstraintWith` finds none), so only the mask is returned.
fn mark_constraint_buddies(w: &mut World, e: EntIdx, _tid: i32) -> u64 {
    let r = w.ent(e).rigid().expect("rigid");
    r.collider_masks.iter().fold(0, |a, &m| a | m)
}

/// `sub_350AB500`: clear the bits set by [`mark_constraint_buddies`] (`+64 += −1 << tid` on
/// colliders whose bit is set).
fn unmark_constraint_buddies(w: &mut World, e: EntIdx, _mask: u64, tid: i32) {
    let cols = w.ent(e).colliders.clone();
    for c in cols {
        let p = &mut w.ent_mut(c).processed;
        if *p & (1 << tid) != 0 {
            *p = p.wrapping_add((-1i32 << tid) as u32);
        }
    }
}

/// The geometry world data of a part at an entity frame (`offset = pos + q·part.pos`,
/// `R = q ⊗ part.q`, `scale = part.scale`).
fn part_gwd(pos: Vec3, q: Quat, p: &Part) -> (Vec3, Quat, f32) {
    let r = q.rotate(p.pos);
    let off = Vec3::new(pos.x + r.x, pos.y + r.y, pos.z + r.z);
    (off, q.product(p.q), p.scale)
}

/// Run the geometry's slot 10 on the thread's arena.
fn geom_intersect(
    s: &mut ThreadScratch,
    a: &dyn Geometry,
    b: &dyn Geometry,
    gwd_a: &mut GeomWorldData,
    gwd_b: &GeomWorldData,
    ip: &mut IntersectionParams,
) -> IntersectOut {
    let mut env = *shared_env();
    env.generic = crate::collision::prim_intersect::generic_intersect();
    if a.is_a_primitive() {
        intersect(&env, s, a, b, gwd_a, gwd_b, ip)
    } else {
        match env.generic {
            Some(f) => f(s, &env, a, b, gwd_a, gwd_b, ip),
            None => IntersectOut::default(),
        }
    }
}

/// Result of [`check_for_new_contacts`].
#[derive(Clone, Copy, Debug)]
pub struct NewContacts {
    /// Number of contact records (arena indices `0..n`).
    pub n: i32,
    /// `*a4`: the record with the largest (smallest for sweeps) `t` among those with a
    /// positive velocity, -1 if none.
    pub i_best: i32,
}

/// `sub_350B3440(ent, gwd, ip, &iBest, sweep, iStartPart, nParts)`: run the narrow phase of
/// the entity's parts `[start, start+n)` (`n < 0` = all) against the potential colliders.
///
/// `gwd` is the caller's frame record: its `v`, `w`, `centerOfMass` are filled by the caller;
/// this function writes `offset`, `R`, `scale` per part (the last part's remain). `sweep`
/// extends the part box used for the per-part bbox culling.
///
/// Not ported: parts with flag `0x100000` (ray-cast contact mode: a `CRayGeom` along the
/// gravity direction intersected by the collider's geometry) produce no contacts; the
/// multi-thread re-run (`W+22424 > 1`) is skipped (the port is single-threaded); the
/// per-thread part subsets of large entities (`+320/+328`) are not built by the grid query,
/// so all parts of a collider are tested.
#[allow(clippy::too_many_lines)]
pub fn check_for_new_contacts(
    w: &mut World,
    e: EntIdx,
    gwd: &mut GeomWorldData,
    ip: &mut IntersectionParams,
    sweep: Vec3,
    start: i32,
    nparts: i32,
) -> NewContacts {
    let tid = w.tls_thread;
    let tidu = tid as usize;
    let v14: f32 = if ip.sweep_test { -1.0 } else { 1.0 };
    let saved_pivot = ip.pt_outside_pivot[0];
    let mut v361 = (1.0 - v14) * 1e10;
    let saved_vrel = ip.vrel_min;
    let mut i_best = -1;
    let n_own = w.ent(e).parts.len() as i32;
    let v21 = (n_own & (nparts >> 31)) | (nparts - (nparts & (nparts >> 31)));
    let dt = 0.0f32; // GetPotentialColliders is called with xmm2 = the caller's a3 (unused)
    let _ = dt;
    let (list, n_coll) = get_potential_colliders_for_cfnc(w, e);
    {
        let r = w.ent_mut(e).rigid_mut().expect("rigid");
        r.bits512 = (r.bits512 & 0xFFFF_FC00) | (n_coll as u32 & 0x3FF);
    }
    ip.keep_prev_contacts = false;
    let mut total: i32 = 0; // v336
    let mut v354: i32 = 0;
    let mut v349: u32 = 0;
    let mut stop = false;
    let mut ipart = start;
    while ipart < start + v21 && !stop {
        let own = w.ent(e).parts[ipart as usize].clone();
        if own.flags_collider == 0 {
            ipart += 1;
            continue;
        }
        let own_awake = crate::entities::is_awake(w, e);
        // own part frame at the entity's new coords (part coords: the part itself / its
        // ComputeBBoxRE copy — identical values)
        let nc = w.ent(e).new_coords();
        let (off, q, sc) = part_gwd(nc.pos, nc.q, &own);
        gwd.offset = off;
        gwd.r = Mat33::from_quat(q);
        gwd.scale = sc;
        let swept = swept_part_box(nc.pos, nc.q, &gwd.r, &own, sweep);
        if own.flags & 0x10_0000 != 0 {
            // ray-cast contact mode (not ported, see the docs)
            ipart += 1;
            continue;
        }
        ip.vrel_min = saved_vrel;
        let mut ci = 0usize;
        while ci < n_coll as usize && !stop {
            let col = list[ci];
            // static entities with flags 0x60000000 are only tested while the body moves
            // towards (0x40000000) / away from (0x20000000) their box centre
            if is_static_kind(w, col) && w.ent(col).flags & 0x6000_0000 != 0 {
                let cent = w.ent(col);
                let f = cent.flags;
                let sx = cent.bbox[1].x + cent.bbox[0].x;
                let sy = cent.bbox[1].y + cent.bbox[0].y;
                let sz = cent.bbox[1].z + cent.bbox[0].z;
                let r = w.ent(e).rigid().expect("rigid");
                let b = w.bodies.get(r.body);
                let dv = Vec3::new(b.pos.x - r.prev_pos.x, b.pos.y - r.prev_pos.y, b.pos.z - r.prev_pos.z);
                let dc = Vec3::new(sx * 0.5 - b.pos.x, sy * 0.5 - b.pos.y, sz * 0.5 - b.pos.z);
                let mut process = false;
                if (dv.x.abs() + dv.y.abs()) + dv.z.abs() > 0.000_300_000_01 {
                    let d = (dv.x * dc.x + dv.y * dc.y) + dv.z * dc.z;
                    process = if f & 0x2000_0000 != 0 { d <= 0.0 } else { d >= 0.0 };
                }
                if !process {
                    ci += 1;
                    continue;
                }
            }
            let cbox = w.ent(col).bbox;
            let d = |a: f32, b: f32| (a - b) * (a - b);
            let v502 = (d(cbox[1].y, cbox[0].y) + d(cbox[1].x, cbox[0].x)) + d(cbox[1].z, cbox[0].z) > 0.0;
            let n_cparts = w.ent(col).parts.len() as i32;
            let mut cp = 0;
            while cp < n_cparts && !stop {
                let cpart = w.ent(col).parts[cp as usize].clone();
                if ((own.flags_collider | 0x20_0000) & cpart.flags) == 0 {
                    v349 |= cpart.flags & 0x200_0000;
                    cp += 1;
                    continue;
                }
                let both_single = n_own + n_cparts == 2;
                let skip = (col == e) // slot 82 IsSelfCollisionEnabled: rigid stub 0
                    || (!both_single
                        && ((own_awake == 0 && crate::entities::is_awake(w, col) == 0)
                            || (v502 && !boxes_overlap(&swept, &cpart.bbox))));
                if skip {
                    v349 |= cpart.flags & 0x200_0000;
                    cp += 1;
                    continue;
                }
                if cpart.flags & 0x20_0000 != 0 {
                    w.signal_event(u32::MAX, Event::BBoxOverlap { ent: [e, col] });
                    if cpart.flags & own.flags_collider == 0 {
                        cp += 1;
                        continue;
                    }
                }
                if cpart.flags & 0x4000_0000 != 0
                    && crate::entities::mass_inv(w, col) * 5.0 > w.bodies.get(w.ent(e).rigid().expect("rigid").body).minv
                {
                    cp += 1;
                    continue;
                }
                let (cpos, cq) = (w.ent(col).pos, w.ent(col).q);
                let (coff, cqq, csc) = part_gwd(cpos, cq, &cpart);
                let cbody = body_of(w, col);
                let gwd_b = GeomWorldData {
                    offset: coff,
                    r: Mat33::from_quat(cqq),
                    scale: csc,
                    v: cbody.v,
                    w: cbody.w,
                    center_of_mass: cbody.pos,
                };
                ip.pt_outside_pivot[0] = if cpart.max_dim <= own.max_dim {
                    Vec3::splat(1e11)
                } else {
                    saved_pivot
                };
                let ret = {
                    let s = &mut w.tscratch[tidu];
                    geom_intersect(s, own.pgeom_proxy.geom.as_ref(), cpart.pgeom_proxy.geom.as_ref(), gwd, &gwd_b, ip)
                };
                let first = ret.first.unwrap_or(0);
                let mut j = 0;
                while j < ret.ret {
                    let o = ThreadScratch::contact_off(first + j);
                    let id0 = w.tscratch[tidu].rd_i(o + i64::from(c::ID));
                    let m0 = map_material(w, e, id0, ipart);
                    w.tscratch[tidu].wr(o + i64::from(c::ID), m0 as u32);
                    let id1 = w.tscratch[tidu].rd_i(o + i64::from(c::ID) + 4);
                    let m1 = map_material(w, col, id1, cp);
                    w.tscratch[tidu].wr(o + i64::from(c::ID) + 4, m1 as u32);
                    if v349 != 0 && (w.ent(col).id_bits & 0x7F_FFFF) == 0x7F_FFFF {
                        material_override(w, e, &list, n_coll, o);
                    }
                    {
                        let cs: &mut ContactScratch = &mut w.ccd_scratch[tidu];
                        cs.parts[v354 as usize] = (ipart, cp);
                        cs.collider[v354 as usize] = col;
                    }
                    fix_area_ids(&mut w.tscratch[tidu], o);
                    let s = &mut w.tscratch[tidu];
                    let dir = s.rd_v(o + i64::from(c::DIR));
                    let nn = s.rd_v(o + i64::from(c::N));
                    if ((dir.y * nn.y) + dir.x * nn.x) + dir.z * nn.z > 0.2 {
                        s.wr_d(o, -1.0);
                    }
                    if j == 0 && w.ent(e).flags & 0x200_0000 != 0 && ((own.flags | cpart.flags) & 0x180_0000) != 0 {
                        breakable_collision_event(w, e, col, ipart, cp, o);
                    }
                    let s = &w.tscratch[tidu];
                    if s.rd_f(o + i64::from(c::VEL)) > 0.0 {
                        let t = s.rd_d(o);
                        if f64::from(v14) * t > f64::from(v361 * v14) {
                            i_best = total;
                            v361 = t as f32;
                        }
                    }
                    v354 += 1;
                    total += 1;
                    if total == 128 {
                        stop = true;
                        break;
                    }
                    j += 1;
                }
                if stop {
                    break;
                }
                ip.keep_prev_contacts = ip.keep_prev_contacts || ret.ret > 0;
                cp += 1;
            }
            ci += 1;
        }
        ipart += 1;
    }
    ip.pt_outside_pivot[0] = saved_pivot;
    ip.vrel_min = saved_vrel;
    w.ccd_scratch[tidu].count = total;
    NewContacts { n: total, i_best }
}

/// The swept world box of a part used for the per-part culling (`v365..v367`): the part's
/// geometry box rotated by `R` (`sub_35020320(Basis, Rᵀ)`, absolute values), scaled, centred
/// at `q_ent·(q_part·c·s + p_part) + p_ent`, then extended by `sweep` on the side the sweep
/// points to (`isneg(−sweep)` picks the max side, so a zero sweep is added to the max).
fn swept_part_box(pos: Vec3, q: Quat, r: &Mat33, p: &Part, sweep: Vec3) -> [Vec3; 2] {
    let bb = p.pgeom_proxy.geom.bbox();
    let b = bb.basis.m;
    let r = r.m;
    let mut bp = [[0.0f32; 3]; 3];
    for i in 0..3 {
        for j in 0..3 {
            bp[i][j] = ((b[i][1] * r[j][1] + b[i][0] * r[j][0]) + b[i][2] * r[j][2]).abs();
        }
    }
    let s = bb.size;
    let ps = p.scale;
    let ex = ((bp[0][0] * s.x + bp[1][0] * s.y) + bp[2][0] * s.z) * ps;
    let ey = ps * ((bp[0][1] * s.x + bp[1][1] * s.y) + bp[2][1] * s.z);
    let ez = ps * ((bp[0][2] * s.x + bp[1][2] * s.y) + bp[2][2] * s.z);
    let ctr = p.q.rotate(bb.center);
    let p1 = Vec3::new(ctr.x * ps + p.pos.x, ctr.y * ps + p.pos.y, ctr.z * ps + p.pos.z);
    let p2 = q.rotate(p1);
    let cw = Vec3::new(p2.x + pos.x, p2.y + pos.y, p2.z + pos.z);
    let mut lo = [cw.x - ex, cw.y - ey, cw.z - ez];
    let mut hi = [cw.x + ex, cw.y + ey, cw.z + ez];
    for (k, sv) in [sweep.x, sweep.y, sweep.z].into_iter().enumerate() {
        if (-sv).to_bits() >> 31 != 0 {
            hi[k] += sv;
        } else {
            lo[k] += sv;
        }
    }
    [Vec3::new(lo[0], lo[1], lo[2]), Vec3::new(hi[0], hi[1], hi[2])]
}

fn is_static_kind(w: &World, e: EntIdx) -> bool {
    matches!(w.ent(e).kind, crate::entities::base::EntityKind::Static)
}

/// Slot 64 `GetRigidBody(ipart)`: the rigid body, or the static dummy (whose `P`, `L`, `w`,
/// `v` the base slot zeroes on every call).
pub fn body_of(w: &mut World, e: EntIdx) -> crate::solver::RigidBody {
    match w.ent(e).rigid() {
        Some(r) => *w.bodies.get(r.body),
        None => {
            w.bodies.get_mut(crate::solver::BodyId::STATIC).reset_static_dummy();
            *w.bodies.get(crate::solver::BodyId::STATIC)
        }
    }
}

/// Body id for a contact's `pbody`.
pub fn body_id_of(w: &mut World, e: EntIdx) -> crate::solver::BodyId {
    match w.ent(e).rigid() {
        Some(r) => r.body,
        None => {
            w.bodies.get_mut(crate::solver::BodyId::STATIC).reset_static_dummy();
            crate::solver::BodyId::STATIC
        }
    }
}

/// GetPotentialColliders as CheckForNewContacts calls it (`xmm2` holds the caller's stale
/// `a3`; only `dt > 0` matters there, for the articulated branch, which is unreachable).
fn get_potential_colliders_for_cfnc(w: &mut World, e: EntIdx) -> (Vec<EntIdx>, i32) {
    get_potential_colliders(w, e, 0.0)
}

/// The area id fix-up: entries of the area's `piPrim[s]` that are negative take the contact's
/// `iPrim[s]`, and their `piFeature[s]` take `iFeature[s] | 0x80000000`.
fn fix_area_ids(s: &mut ThreadScratch, o: i64) {
    let pa = s.rd_q(o + i64::from(c::AREA));
    let Some(ao) = s.off_of(pa) else {
        return;
    };
    use crate::collision::prim_intersect::scratch::a;
    let npt = s.rd_i(ao + i64::from(a::NPT));
    let prim0 = s.rd_q(ao + i64::from(a::PRIM0));
    let prim1 = s.rd_q(ao + i64::from(a::PRIM1));
    let feat0 = s.rd_q(ao + i64::from(a::FEAT0));
    let feat1 = s.rd_q(ao + i64::from(a::FEAT1));
    let ip0 = s.rd(o + i64::from(c::IPRIM));
    let ip1 = s.rd(o + i64::from(c::IPRIM) + 4);
    let if0 = s.rd(o + i64::from(c::IFEATURE));
    let if1 = s.rd(o + i64::from(c::IFEATURE) + 4);
    let mut k = 0;
    while k < npt {
        let off = 4 * k as u64;
        let m = ((s.rd_at(prim0 + off) as i32) >> 31) as u32;
        s.wr_at(prim0 + off, s.rd_at(prim0 + off) & !m);
        s.wr_at(prim0 + off, s.rd_at(prim0 + off) | (m & ip0));
        s.wr_at(feat0 + off, s.rd_at(feat0 + off) & !m);
        s.wr_at(feat0 + off, s.rd_at(feat0 + off) | (m & (if0 | 0x8000_0000)));
        let m = ((s.rd_at(prim1 + off) as i32) >> 31) as u32;
        s.wr_at(prim1 + off, s.rd_at(prim1 + off) & !m);
        s.wr_at(prim1 + off, s.rd_at(prim1 + off) | (m & ip1));
        s.wr_at(feat1 + off, s.rd_at(feat1 + off) & !m);
        s.wr_at(feat1 + off, s.rd_at(feat1 + off) | (m & (if1 | 0x8000_0000)));
        k += 1;
    }
}

/// The id override for colliders without an entity id (`id & 0x7FFFFF == 0x7FFFFF`) when a
/// part of a potential collider has flag `0x2000000`: if the contact centre lies inside such
/// a part (slot 8 `PointInsideStatus` on the point brought into the part frame), the
/// contact's second material id becomes that part's `phys_geometry` surface index mapped
/// through the part.
fn material_override(w: &mut World, _e: EntIdx, list: &[EntIdx], n: i32, o: i64) {
    let tid = w.tls_thread as usize;
    let ctr = w.tscratch[tid].rd_v(o + i64::from(c::CENTER));
    let mut hits: Vec<(EntIdx, i32, i32)> = Vec::new();
    for &k in list.iter().take(n as usize) {
        let ent = w.ent(k);
        for (pi, p) in ent.parts.iter().enumerate() {
            if p.flags & 0x200_0000 == 0 {
                continue;
            }
            // world → entity frame (q⁻¹·(x − pos)), then → part frame
            let d = Vec3::new(ctr.x - ent.pos.x, ctr.y - ent.pos.y, ctr.z - ent.pos.z);
            let q = ent.q;
            let t = Vec3::new(
                ((d.y * q.z) - (d.z * q.y)) + d.x * q.w,
                ((d.z * q.x) - (d.x * q.z)) + d.y * q.w,
                ((d.x * q.y) - (d.y * q.x)) + d.z * q.w,
            );
            let u = Vec3::new(
                (t.y * q.z) - (t.z * q.y),
                (t.z * q.x) - (t.x * q.z),
                (t.x * q.y) - (t.y * q.x),
            );
            let le = Vec3::new(
                (u.x + (u.x + d.x)) - p.pos.x,
                (u.y + (u.y + d.y)) - p.pos.y,
                (u.z + (u.z + d.z)) - p.pos.z,
            );
            let pq = p.q;
            let t2 = Vec3::new(
                ((pq.z * le.y) - (pq.y * le.z)) + le.x * pq.w,
                ((pq.x * le.z) - (le.x * pq.z)) + pq.w * le.y,
                ((pq.y * le.x) - (pq.x * le.y)) + le.z * pq.w,
            );
            let u2 = Vec3::new(
                (t2.y * pq.z) - (t2.z * pq.y),
                (t2.z * pq.x) - (t2.x * pq.z),
                (t2.x * pq.y) - (t2.y * pq.x),
            );
            let lp = Vec3::new(u2.x + (u2.x + le.x), u2.y + (u2.y + le.y), u2.z + (u2.z + le.z));
            if p.pgeom.geom.point_inside_status(lp) != 0 {
                hits.push((k, p.pgeom.surface_idx, pi as i32));
            }
        }
    }
    // every hit overwrites the id (the last one wins)
    for (k, surf, pi) in hits {
        let m = map_material(w, k, surf, pi);
        w.tscratch[tid].wr(o + i64::from(c::ID) + 4, m as u32);
    }
}

/// The breakable-part collision event of CheckForNewContacts (first contact of a pair, the
/// entity logs collisions (`0x2000000`) and one of the parts has flag `0x800000`/`0x1000000`):
/// a type-1 event to the immediate clients; a part with only `0x800000` whose clients all
/// return 0 gets `0x1000000`; with `0x1000000` the contact velocity is cleared and, when the
/// body moves faster than 30× its oriented half-extent along v, the event is also logged.
fn breakable_collision_event(w: &mut World, e: EntIdx, col: EntIdx, ipart: i32, cp: i32, o: i64) {
    let tid = w.tls_thread as usize;
    let s = &w.tscratch[tid];
    let pt = s.rd_v(o + i64::from(c::PT));
    let n = s.rd_v(o + i64::from(c::N));
    let ev = Event::Collision {
        ent: [e, col],
        pt,
        n: Vec3::new(-n.x, -n.y, -n.z),
        v: [Vec3::ZERO; 2],
        mass: [0.0; 2],
        partid: [w.ent(e).parts[ipart as usize].id, w.ent(col).parts[cp as usize].id],
        idmat: [s.rd_i(o + i64::from(c::ID)), s.rd_i(o + i64::from(c::ID) + 4)],
        penetration: 0.0,
        normimpulse: 0.0,
        radius: 0.0,
    };
    if w.ent(col).parts[cp as usize].flags & 0x180_0000 == 0x80_0000 {
        let r = w.call_immediate_clients(&ev);
        if r == 0 {
            w.ent_mut(col).parts[cp as usize].flags |= 0x100_0000;
        }
    }
    let f = (w.ent(e).parts[ipart as usize].flags | w.ent(col).parts[cp as usize].flags) & 0x100_0000;
    let vel = w.tscratch[tid].rd_f(o + i64::from(c::VEL));
    if (f as f32) * vel > 0.0 {
        w.tscratch[tid].wr_f(o + i64::from(c::VEL), 0.0);
        let b = body_of(w, e);
        let v2 = (b.v.x * b.v.x + b.v.y * b.v.y) + b.v.z * b.v.z;
        let (vx, vy, vz) = if v2 <= 1e-20 {
            (0.0, 0.0, 1.0)
        } else {
            let k = 1.0 / v2.sqrt();
            (k * b.v.x, k * b.v.y, k * b.v.z)
        };
        let bb = w.ent(e).bbox;
        let mut hx = (bb[1].x - bb[0].x) * 0.5;
        let mut hy = (bb[1].y - bb[0].y) * 0.5;
        let mut hz = (bb[1].z - bb[0].z) * 0.5;
        let a = [
            (vx * vz) * hx * hz + (vy * vx) * hx * hy,
            (vy * vz) * hy * hz + (vy * vx) * hy * hx,
            (vy * vz) * hz * hy + (vx * vz) * hz * hx,
        ];
        let i1 = ((a[0] - a[1]).to_bits() >> 31) as usize;
        let i2 = ((a[i1] - a[2]).to_bits() >> 31) as usize;
        let k = (i1 | (i2 << 1)) & ((((i1 | (i2 << 1)) ^ 2) | 4) >> 1);
        match k {
            0 => hx *= -1.0,
            1 => hy *= -1.0,
            _ => hz *= -1.0,
        }
        let sg = |x: f32| ((((x.to_bits() as i32) >> 30) & !1) + 1) as f32;
        hx *= sg(vx);
        hy *= sg(vy);
        hz *= sg(vz);
        let cx = hx * vz - hz * vx;
        let cy = vy * hz - hy * vz;
        let cz = hy * vx - vy * hx;
        let rad = ((cx * cx) + (cy * cy) + (cz * cz)).sqrt();
        if w.ent(col).parts[cp as usize].flags & 0x100_0000 != 0 && v2 > (rad * 30.0) * (rad * 30.0) {
            let flags = w.ent(e).flags;
            w.signal_event(flags & 0x200_2000, ev);
        }
    }
}

/// `sub_350B9880(ent, contacts, n, tid)` CheckContactConflicts: for every pair of contact
/// records `i < j` with opposing unprojection directions (`dir_i·dir_j < −0.85`), no positive
/// velocity on either (`max(vel_i, 0) + max(vel_j, 0) ≤ 0`) and the same collider part (or
/// two static colliders), one of the two is disabled (`t = −1`, the one whose centre lies
/// on the far side of the CoM along its `dir`):
/// * collider simclass ≠ 0: always;
/// * otherwise when the pair is aligned with the axis of a thin single-part body (smallest
///   box size < 10 % of the larger of the others; `|axis·n_i| > 0.94`) or the two centres are
///   behind each other's normals, and the projections of both contacts' border points
///   (relative to `centre_i`) on `u ⟂ dir_i` and `dir_i × u` overlap.
#[allow(clippy::too_many_lines)]
pub fn check_contact_conflicts(w: &mut World, e: EntIdx, n: i32, tid: i32) {
    let tidu = tid as usize;
    let com = w.bodies.get(w.ent(e).rigid().expect("rigid").body).pos;
    for i in 0..n - 1 {
        for j in i + 1..n {
            let (ci, cj) = {
                let cs = &w.ccd_scratch[tidu];
                (
                    (cs.collider[i as usize], cs.parts[i as usize].1),
                    (cs.collider[j as usize], cs.parts[j as usize].1),
                )
            };
            let s = &w.tscratch[tidu];
            let oi = ThreadScratch::contact_off(i);
            let oj = ThreadScratch::contact_off(j);
            let vi = s.rd_f(oi + i64::from(c::VEL));
            let vj = s.rd_f(oj + i64::from(c::VEL));
            let di = s.rd_v(oi + i64::from(c::DIR));
            let dj = s.rd_v(oj + i64::from(c::DIR));
            let same = (ci.0 == cj.0 && ci.1 == cj.1)
                || (w.ent(ci.0).sim_class() + w.ent(cj.0).sim_class() == 0);
            if !(same
                && ((vi.abs() + vi) * 0.5 + (vj.abs() + vj) * 0.5) <= 0.0
                && ((dj.x * di.x + dj.y * di.y) + dj.z * di.z) < -0.850_000_02)
            {
                continue;
            }
            let pi = s.rd_v(oi + i64::from(c::CENTER));
            let pj = s.rd_v(oj + i64::from(c::CENTER));
            let ni = s.rd_v(oi + i64::from(c::N));
            let nj = s.rd_v(oj + i64::from(c::N));
            let far = |k_near: i32, k_far: i32| {
                let d = ((pi.y - com.y) * di.y + di.x * (pi.x - com.x)) + (pi.z - com.z) * di.z;
                if d <= 0.0 { k_far } else { k_near }
            };
            if w.ent(ci.0).id_bits & 0xFF00_0000 != 0 {
                let k = far(i, j);
                w.tscratch[tidu].wr_d(ThreadScratch::contact_off(k), -1.0);
                continue;
            }
            let mut axis = Vec3::ZERO;
            if w.ent(e).parts.len() == 1 {
                let p = w.ent(e).parts[0].clone();
                let bb = p.pgeom_proxy.geom.bbox();
                let sz = [bb.size.x, bb.size.y, bb.size.z];
                let i1 = ((sz[1] - sz[0]).to_bits() >> 31) as usize;
                let i2 = ((sz[2] - sz[i1]).to_bits() >> 31) as usize;
                let k = (i1 | (i2 << 1)) & ((((i1 | (i2 << 1)) ^ 2) | 4) >> 1);
                let a = sz[DEC_MOD3[k]];
                let b = sz[INC_MOD3[k]];
                if (((b - a).abs() + (a + b)) * 0.5) * 0.1 > sz[k] {
                    let q = w.ent(e).q.product(p.q);
                    let row = bb.basis.m[k];
                    axis = q.rotate(Vec3::new(row[0], row[1], row[2]));
                }
            }
            let s = &w.tscratch[tidu];
            let aligned = ((axis.x * ni.x + axis.y * ni.y) + axis.z * ni.z).abs() > 0.94;
            let d1 = ((pi.y - pj.y) * ni.y + ni.x * (pi.x - pj.x)) + (pi.z - pj.z) * ni.z;
            let d2 = ((pj.y - pi.y) * nj.y + nj.x * (pj.x - pi.x)) + (pj.z - pi.z) * nj.z;
            if !(aligned || (d1 < 0.0 && d2 < 0.0)) {
                continue;
            }
            // u ⟂ dir_i on the axis picked by |dir.x| vs 0.9·|dir|
            let da = [di.x, di.y, di.z];
            let v214 = ((di.y * di.y + di.x * di.x) + di.z * di.z) * 0.809_999_94 - di.x * di.x;
            let k = (v214.to_bits() >> 31) as usize;
            let (inc, dec) = (INC_MOD3[k], DEC_MOD3[k]);
            let mut u = [0.0f32; 3];
            u[k] = 0.0;
            let t = da[inc];
            u[inc] = da[dec];
            u[dec] = -t;
            let l2 = (u[0] * u[0] + u[1] * u[1]) + u[2] * u[2];
            let (ux, uy, uz) = if l2 <= 9.999_999_7e-21 {
                (0.0, 0.0, 1.0)
            } else {
                let r = 1.0 / l2.sqrt();
                (u[0] * r, u[1] * r, u[2] * r)
            };
            // v = dir_i × u
            let vx = uz * di.y - uy * di.z;
            let vy = ux * di.z - uz * di.x;
            let vz = uy * di.x - ux * di.y;
            let proj = |b: &[Vec3]| -> ([f32; 2], [f32; 2]) {
                let f = |q: Vec3| {
                    (
                        ((q.x - pi.x) * ux + (q.y - pi.y) * uy) + (q.z - pi.z) * uz,
                        ((q.x - pi.x) * vx + (q.y - pi.y) * vy) + (q.z - pi.z) * vz,
                    )
                };
                let (a0, b0) = f(b[0]);
                let (mut umin, mut umax, mut vmin, mut vmax) = (a0, a0, b0, b0);
                for &q in &b[1..] {
                    let (a, bb) = f(q);
                    umin = ((a + umin) - (umin - a).abs()) * 0.5;
                    umax = ((umax - a).abs() + (umax + a)) * 0.5;
                    vmin = ((bb + vmin) - (vmin - bb).abs()) * 0.5;
                    vmax = ((vmax - bb).abs() + (vmax + bb)) * 0.5;
                }
                ([umin, umax], [vmin, vmax])
            };
            let bi = border_points(s, oi);
            let bj = border_points(s, oj);
            // the original reads the first border point unconditionally
            let bi = if bi.is_empty() { vec![s.rd_v_at(s.rd_q(oi + i64::from(c::PTBORDER)))] } else { bi };
            let bj = if bj.is_empty() { vec![s.rd_v_at(s.rd_q(oj + i64::from(c::PTBORDER)))] } else { bj };
            let (ui, vi_) = proj(&bi);
            let (uj, vj_) = proj(&bj);
            let g1 = (((ui[1] + ui[0]) * 0.5 - (uj[1] + uj[0]) * 0.5).abs() - (ui[1] - ui[0]) * 0.5)
                - (uj[1] - uj[0]) * 0.5;
            let g2 = (((vi_[1] + vi_[0]) * 0.5 - (vj_[1] + vj_[0]) * 0.5).abs() - (vi_[1] - vi_[0]) * 0.5)
                - (vj_[1] - vj_[0]) * 0.5;
            if (((g1 - g2).abs() + (g1 + g2)) * 0.5) >= 0.0 {
                continue;
            }
            let d = (di.x * (pi.x - com.x) + (pi.y - com.y) * di.y) + (pi.z - com.z) * di.z;
            let k = if d <= 0.0 { j } else { i };
            w.tscratch[tidu].wr_d(ThreadScratch::contact_off(k), -1.0);
        }
    }
}

/// `{1, 2, 0}` and `{2, 0, 1}`.
const INC_MOD3: [usize; 3] = [1, 2, 0];
const DEC_MOD3: [usize; 3] = [2, 0, 1];

fn border_points(s: &ThreadScratch, o: i64) -> Vec<Vec3> {
    let nb = s.rd_i(o + i64::from(c::NBORDER)).clamp(0, 4096) as usize;
    let pb = s.rd_q(o + i64::from(c::PTBORDER));
    (0..nb).map(|k| s.rd_v_at(pb.wrapping_add(12 * k as u64))).collect()
}
