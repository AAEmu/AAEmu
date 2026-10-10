//! Ground handling of `CLivingEntity`: the per-step neighbour records, the ground probe
//! (`sub_3502CA20`), moving-ground following (`sub_3502B3F0`), the ground collider
//! (`sub_3502C700` / `sub_35024CF0`) and the jump reaction impulse (`sub_35025DF0`).

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
    clippy::too_many_arguments
)]

use super::{ld, lm, release_ground};
use crate::collision::contact::{GeomContact, IntersectionParams};
use crate::entities::interface::{rd_u32, wr_i32, wr_v3};
use crate::geometry::primitives::RayPrim;
use crate::geometry::shapes::RayGeom;
use crate::geometry::{GeomWorldData, Geometry};
use crate::math::{Mat33, Quat, Vec3};
use crate::world::{EntIdx, World};
use std::sync::Arc;

/// An 88-byte neighbour entity record of the living step (per-thread array at `world+21904`).
#[derive(Clone, Debug)]
pub struct EntRec {
    /// `+0` / `+12` bbox.
    pub bbox: [Vec3; 2],
    /// `+24` bbox size.
    pub size: Vec3,
    /// `+40` GetMassInv.
    pub mass_inv: f32,
    /// `+44` 1 if the bbox has a non-zero size.
    pub has_size: i32,
    /// `+48` GetType.
    pub etype: i32,
    /// `+52` number of parts.
    pub nparts: i32,
    /// `+56` simclass.
    pub simclass: i32,
    /// `+60` slot 45 (`IgnoreCollision`-like) result against the living entity.
    pub ignore: i32,
    /// `+64` / `+68` first / end part record, `+72` the running part record counter.
    pub pstart: i32,
    pub pend: i32,
    pub plast: i32,
    /// `+80` the entity.
    pub ent: EntIdx,
}

/// A 96-byte neighbour part record (per-thread array at `world+21912`).
#[derive(Clone, Debug)]
pub struct PartRec {
    /// `+0` / `+12` part bbox.
    pub bbox: [Vec3; 2],
    /// `+24` `q_ent ⊗ q_part` (stored as doubles; exact copies of the floats).
    pub q: Quat,
    /// `+56` part position in the world.
    pub pos: Vec3,
    /// `+68` part scale.
    pub scale: f32,
    /// `+72` part flags.
    pub flags: u32,
    /// `+76` part index.
    pub ipart: i32,
    /// `+80` surface index (sign-extended 10 bits).
    pub surface: i32,
    /// `+88` the proxy geometry.
    pub geom: Arc<dyn Geometry>,
}

impl PartRec {
    /// The part placement (`sub_35039050` matrix of the double quaternion).
    #[must_use]
    pub fn gwd(&self) -> GeomWorldData {
        GeomWorldData {
            offset: self.pos,
            r: Mat33::from_quat(self.q),
            scale: self.scale,
            ..GeomWorldData::default()
        }
    }
}

/// `sub_35038180(a, b)`: do the boxes overlap (strictly, through the abs-idiom of the binary)?
#[must_use]
pub fn boxes_overlap(a: &[Vec3; 2], b: &[Vec3; 2]) -> bool {
    let v2 = (((a[0].y + a[1].y) - b[0].y) - b[1].y).abs() - (a[1].y - a[0].y) - (b[1].y - b[0].y);
    let v3 = (((a[0].x + a[1].x) - b[0].x) - b[1].x).abs() - (a[1].x - a[0].x) - (b[1].x - b[0].x);
    let v4 = ((v3 - v2).abs() + (v3 + v2)) * 0.5;
    let v5 = (((a[0].z + a[1].z) - b[0].z) - b[1].z).abs() - (a[1].z - a[0].z) - (b[1].z - b[0].z);
    (((v4 - v5).abs() + (v5 + v4)) * 0.5) < 0.0
}

/// `pe_status_dynamics` of a part of `e` (GetStatus id 8 with `ipart`): `(v, w, centre of
/// mass)`.
pub fn part_dynamics(w: &mut World, e: EntIdx, ipart: i32) -> (Vec3, Vec3, Vec3) {
    let mut b = vec![0u8; 256];
    wr_i32(&mut b, 0, 8);
    wr_i32(&mut b, 4, crate::sentinel::UNUSED_I32);
    wr_i32(&mut b, 8, ipart);
    crate::entities::interface::status::get_status(w, e, &mut b);
    let v3 = |o: usize| {
        Vec3::new(
            f32::from_bits(rd_u32(&b, o)),
            f32::from_bits(rd_u32(&b, o + 4)),
            f32::from_bits(rd_u32(&b, o + 8)),
        )
    };
    (v3(12), v3(24), v3(60))
}

/// Slot 67 of another entity: add its point inverse-mass matrix at `pt` to `k` (rigid
/// 0x350CCD50 through `sub_35098FA0`, living 0x35037DB0, nothing for the base).
pub fn contact_matrix(w: &mut World, e: EntIdx, pt: Vec3, _ipart: i32, k: &mut [f32; 9]) {
    match &w.ent(e).kind {
        crate::entities::base::EntityKind::Rigid(r) => {
            let b = w.bodies.get(r.body);
            let rr = [pt.x - b.pos.x, pt.y - b.pos.y, pt.z - b.pos.z];
            let ii = b.iinv.to_array();
            crate::solver::helpers::k_accum(k, &ii, b.minv, rr);
        }
        crate::entities::base::EntityKind::Living(l) => {
            let mi = l.mass_inv;
            k[0] += mi;
            k[4] += mi;
            k[8] += mi;
        }
        _ => {}
    }
}

/// `action_impulse` on another entity (`Action(2)` with `bThreadSafe = ts`): `impulse` at
/// `pt` on part `ipart` (`UNUSED_I32` = none), `iApplyTime` as given, angImpulse / dv / dw
/// unused, partid unused, iSource 0.
pub fn apply_impulse(
    w: &mut World,
    e: EntIdx,
    impulse: Vec3,
    pt: Vec3,
    ipart: i32,
    apply_time: i32,
    ts: i32,
) {
    let mut b = vec![0u8; 80];
    wr_i32(&mut b, 0, 2);
    wr_v3(&mut b, 4, impulse);
    for o in [16usize, 28, 40] {
        crate::entities::interface::wr_u32(&mut b, o, crate::sentinel::UNUSED_F32_BITS);
    }
    wr_v3(&mut b, 52, pt);
    wr_i32(&mut b, 64, crate::sentinel::UNUSED_I32);
    wr_i32(&mut b, 68, ipart);
    wr_i32(&mut b, 72, apply_time);
    wr_i32(&mut b, 76, 0);
    crate::entities::interface::action::action(w, e, &mut b, ts);
}

/// `sub_3502C700(ground, bAlways)`: release the old ground collider; a new one gets entity
/// flag `0x100000` and is kept (AddRef) only when `bAlways + simclass > 0` (static ground is
/// not kept unless its part has flag `0x800000`). Not ported: the rider set of `sub_35039760`
/// (only for living entities with the params-26 byte set).
pub fn set_ground(w: &mut World, e: EntIdx, g: EntIdx, always: i32) {
    release_ground(w, e);
    if g.is_none() {
        return;
    }
    w.ent_mut(g).flags |= 0x10_0000;
    if always + w.ent(g).sim_class() > 0 {
        lm(w, e).ground = g;
    }
}

/// `sub_3502B3F0(out, dt)`: follow the ground collider. Returns the new pivot position
/// (`pos` of the ground part frame + the stored anchor + the pivot offset; the current
/// position without a ground collider) and sets the ground velocity at the feet (`+1004`,
/// clamped to the max ground velocity) and the ground ω (`+1016`). When the ground point's
/// velocity changed by more than `|g|·2dt`, the living entity's mass is coupled in through the
/// ground's contact matrix and the difference pushes the ground (action_impulse,
/// `bThreadSafe = 0`) unless that would speed it up. The position is committed only during
/// the awake-list job (`world+1744 == 2`).
pub fn follow_ground(w: &mut World, e: EntIdx, dt: f32) -> Vec3 {
    let mut out = w.ent(e).pos;
    let g = ld(w, e).ground;
    if !g.is_none() && w.ent(g).id_bits & 0xFF00_0000 == 0x0700_0000 {
        release_ground(w, e);
    }
    let old_gv = ld(w, e).ground_vel;
    {
        let l = lm(w, e);
        l.ground_vel = Vec3::ZERO;
        l.ground_w = Vec3::ZERO;
    }
    let g = ld(w, e).ground;
    if !g.is_none() {
        let mut ip = ld(w, e).ground_ipart;
        let np = w.ent(g).parts.len() as i32;
        let pid = ld(w, e).ground_partid;
        if ip >= np || w.ent(g).parts[ip as usize].id != pid {
            ip = 0;
            let mut found = false;
            while ip < np {
                if w.ent(g).parts[ip as usize].id == pid {
                    found = true;
                    break;
                }
                ip += 1;
            }
            if found {
                lm(w, e).ground_ipart = ip;
            }
            if ip >= w.ent(g).parts.len() as i32 {
                return w.ent(e).pos;
            }
        }
        let ge = w.ent(g);
        let part = &ge.parts[ip as usize];
        let r = ge.q.rotate(part.pos);
        let base = Vec3::new(r.x + ge.pos.x, r.y + ge.pos.y, r.z + ge.pos.z);
        let qp = ge.q.product(part.q);
        let sc = part.scale;
        let l = ld(w, e);
        let a = qp.rotate(l.ground_anchor);
        let pv = w.ent(e).q.rotate(Vec3::new(0.0, 0.0, l.height_pivot));
        out = Vec3::new(
            (pv.x + base.x) + a.x * sc,
            (pv.y + base.y) + a.y * sc,
            ((pv.z + base.z) + a.z * sc) + l.ground_dz,
        );
        let (gv, gw, com) = part_dynamics(w, g, ip);
        let mut k = [0.0f32; 9];
        contact_matrix(w, g, out, ip, &mut k);
        let p = out;
        let v47 = (gw.y * (p.z - com.z) - gw.z * (p.y - com.y)) + gv.x;
        let v48 = gv.y + (gw.z * (p.x - com.x) - (p.z - com.z) * gw.x);
        let v49 = gv.z + ((p.y - com.y) * gw.x - gw.y * (p.x - com.x));
        let d = Vec3::new(v47 - old_gv.x, v48 - old_gv.y, v49 - old_gv.z);
        let v44 = dt * 2.0;
        let grav = ld(w, e).gravity;
        let new_gv;
        if ((d.y * d.y) + d.x * d.x) + d.z * d.z
            >= (((grav.x * grav.x) + grav.y * grav.y) + grav.z * grav.z) * (v44 * v44)
        {
            let mi = ld(w, e).mass_inv;
            let mut kk = k;
            kk[0] = mi + k[0];
            kk[4] = k[4] + mi;
            kk[8] = k[8] + mi;
            crate::solver::helpers::inv33(&mut kk);
            let v54 = (kk[1] * d.y + kk[0] * d.x) + kk[2] * d.z;
            let gx = v54 * mi + old_gv.x;
            let v56 = (kk[4] * d.y + kk[3] * d.x) + kk[5] * d.z;
            let gy = old_gv.y + v56 * mi;
            let v61 = (kk[7] * d.y + kk[6] * d.x) + kk[8] * d.z;
            let gz = old_gv.z + v61 * mi;
            if (gx * gx + gy * gy) + gz * gz <= (v48 * v48 + v47 * v47) + v49 * v49 {
                new_gv = Vec3::new(gx, gy, gz);
                lm(w, e).ground_vel = new_gv;
                apply_impulse(w, g, Vec3::new(-v54, -v56, -v61), p, ip, 0, 0);
            } else {
                new_gv = Vec3::new(v47, v48, v49);
                lm(w, e).ground_vel = new_gv;
            }
        } else {
            new_gv = Vec3::new(v47, v48, v49);
            lm(w, e).ground_vel = new_gv;
        }
        let mg = ld(w, e).max_ground_vel;
        let gvl = ld(w, e).ground_vel;
        if (gvl.x * gvl.x + gvl.y * gvl.y) + gvl.z * gvl.z > mg * mg {
            let n = crate::entities::rigid::velocity::normalize_or_z(gvl);
            lm(w, e).ground_vel = Vec3::new(mg * n.x, mg * n.y, mg * n.z);
        }
        lm(w, e).ground_w = gw;
        let _ = new_gv;
    }
    if w.flag1744 == 2 {
        w.ent_mut(e).pos = out;
    }
    out
}

/// What the ground probe found.
#[derive(Clone, Copy, Debug)]
pub struct GroundHit {
    /// The ground height along the up axis (`-1e10` when nothing was hit).
    pub height: f32,
    /// The ground normal (the stored one when nothing was hit).
    pub n: Vec3,
}

/// `sub_3502CA20(recs, nrecs, parts, pos, &n, unused, bFullRotation, bCommit, bFilterSmall)`:
/// cast a downward ray from the collider bottom (length `1.5·(h − size.z) + 2`) against the
/// parts of the neighbour records (not the entity itself, not living entities, not entities
/// that ignore it or that carry a living collider with constraint contacts), keeping the
/// highest hit with a front-facing normal. The ray frame is the entity orientation
/// (`bFullRotation`) or `q ⊗ yaw(q)` (`(1,0,0,0)` when the yaw part degenerates). A hit on a
/// living entity yields a 15°-tilted horizontal normal. With `bFilterSmall`, once a ground was
/// found (or for a slow entity) only parts big enough relative to the collider are tested.
/// With `bCommit` the ground collider, its part, the position in the part frame and the
/// ground surface/velocity members are set.
pub fn ground_probe(
    w: &mut World,
    e: EntIdx,
    recs: &[EntRec],
    parts: &[PartRec],
    pos: Vec3,
    full_rotation: bool,
    commit: bool,
    filter_small: bool,
) -> GroundHit {
    let q = w.ent(e).q;
    let up = q.rotate(Vec3::new(0.0, 0.0, 1.0));
    let (v59, v58, v57) = (up.x, up.y, up.z);
    let mut v16: f32 = f32::from_bits(0xD015_02F9);
    let mut v17: f32 = f32::from_bits(0xD015_02F9);
    let rq = if full_rotation {
        q
    } else {
        let v20 = q.w * q.w + q.z * q.z;
        if v20 <= 0.000_099_999_997 {
            Quat::new(1.0, 0.0, 0.0, 0.0)
        } else {
            let inv = 1.0 / v20.sqrt();
            let yaw = Quat::new(0.0, 0.0, inv * q.z, inv * q.w);
            q.product(yaw)
        }
    };
    let m = Mat33::from_quat(rq).m;
    let (m00, m01, m02) = (m[0][0], m[0][1], m[0][2]);
    let (m10, m11, m12) = (m[1][0], m[1][1], m[1][2]);
    let (m20, m21, m22) = (m[2][0], m[2][1], m[2][2]);
    let v33 = (m00 + m01) * 0.0;
    let v34 = (m11 + m10) * 0.0;
    let v35 = (m21 + m20) * 0.0;
    let ndir = Vec3::new(v33 - m02 * 1.0, v34 - m12 * 1.0, v35 - m22 * 1.0);
    let l = ld(w, e);
    let v39 = l.height_collider - l.size.z;
    let v41 = v39 - l.height_pivot;
    let v44 = (v39 * -1.5) - 2.0;
    let origin = Vec3::new(
        ((v41 * m02) + v33) + pos.x,
        ((v41 * m12) + v34) + pos.y,
        ((v41 * m22) + v35) + pos.z,
    );
    let mut n_out = l.ground_normal;
    let dir = Vec3::new((v44 * m02) + v33, (v44 * m12) + v34, (v44 * m22) + v35);
    let mut ray = RayGeom::new(&RayPrim { origin, dir });
    ray.ndir = ndir;
    let (size_x, size_z) = (l.size.x, l.size.z);
    let mut best_pt = Vec3::new(0.0, 0.0, 1.0); // v56, v51, v49
    let mut best_ent = EntIdx::NONE; // v150 / v55
    let mut best_ipart: i32 = 0; // v154
    let mut surf0: i32 = 0; // v161
    let mut surf1: i32 = -1; // v159
    let mut surf2: i32 = -1; // v160
    let mut contacts: Vec<GeomContact> = Vec::new();
    let gwd_ray = GeomWorldData::default();
    for ri in (0..recs.len()).rev() {
        let rec = &recs[ri];
        let ent = rec.ent;
        if ent == e || rec.ignore != 0 || rec.etype == 4 {
            continue;
        }
        let cols = w.ent(ent).colliders.clone();
        let mut v62 = 0usize;
        while v62 < cols.len() {
            let c = cols[v62];
            if w.ent(c).id_bits & 0xFF00_0000 == 0x0300_0000
                && crate::entities::has_constraint_contacts_with(w, ent, c, 0)
            {
                break;
            }
            v62 += 1;
        }
        if v62 < cols.len() {
            continue;
        }
        for k in rec.pstart..rec.pend {
            let pr = &parts[k as usize];
            let fl = pr.flags;
            if fl & 2 == 0 || fl & 0x1000_0000 != 0 {
                continue;
            }
            let mut test = !filter_small;
            if !test && best_ent.is_none() {
                let b = crate::entities::rigid::colliders::body_of(w, ent);
                let _ = v62; // the binary passes the collider counter as the part index
                if ((b.v.x * b.v.x) + b.v.y * b.v.y) + b.v.z * b.v.z <= 1.0 {
                    test = true;
                }
            }
            if !test {
                let s = pr.geom.bbox().size;
                let (sx, sy, sz) = (s.x, s.y, s.z);
                let v69 = ((sx - sy).abs() + (sx + sy)) * 0.5;
                let v70 = ((sx * sy - sx * sz).abs() + (sx * sy + sx * sz)) * 0.5;
                let a = (((v70 - sz * sy).abs() + (sz * sy + v70)) * 0.5) * (pr.scale * pr.scale);
                let lhs = a * 4.0;
                let rhs = (f64::from(size_x * size_x) * 3.141_592_653_589_793 * 0.25) as f32;
                if f64::from(lhs) >= f64::from(size_x * size_x) * 3.141_592_653_589_793 * 0.25 {
                    test = true;
                } else {
                    let v71 = (v69 - sz).abs() + (v69 + sz);
                    if ((v71 * 0.5) * pr.scale) * 2.0 >= size_z * 1.4 {
                        test = true;
                    }
                }
                let _ = rhs;
            }
            if !test {
                continue;
            }
            let gwd = pr.gwd();
            let mut ip = IntersectionParams::default();
            contacts.clear();
            let n = pr
                .geom
                .intersect(&ray, &gwd, &gwd_ray, &mut ip, &mut contacts);
            if std::env::var_os("AAEMU_LIVING_DEBUG").is_some() {
                eprintln!(
                    "liv:  probe rec {:?} part {} n={} pts={:?}",
                    ent,
                    pr.ipart,
                    n,
                    contacts
                        .iter()
                        .take(n)
                        .map(|c| (c.pt, c.n))
                        .collect::<Vec<_>>()
                );
            }
            if n == 0 {
                continue;
            }
            let c = &contacts[n - 1];
            if !((dir.y * c.n.y + dir.x * c.n.x) + dir.z * c.n.z < 0.0) {
                continue;
            }
            let h = (v58 * c.pt.y + v59 * c.pt.x) + v57 * c.pt.z;
            if fl & 1 != 0 && h > v17 {
                best_pt = c.pt;
                v17 = h;
                surf1 = crate::entities::rigid::colliders::map_material(w, ent, c.id[0], pr.ipart);
            }
            if h > v16 {
                best_pt = c.pt;
                best_ipart = pr.ipart;
                v16 = h;
                best_ent = ent;
                n_out = c.n;
                if rec.simclass == 3 {
                    let ep = w.ent(ent).pos;
                    let d = Vec3::new(c.pt.x - ep.x, c.pt.y - ep.y, c.pt.z - ep.z);
                    let v85 = (v59 * d.x + d.y * v58) + d.z * v57;
                    let hd = Vec3::new(d.x - v59 * v85, d.y - v85 * v58, d.z - v85 * v57);
                    let hn = if (hd.y * hd.y + hd.x * hd.x) + hd.z * hd.z <= 0.000_099_999_997 {
                        q.rotate(Vec3::new(1.0, 0.0, 0.0))
                    } else {
                        crate::entities::rigid::velocity::normalize_or_z(hd)
                    };
                    n_out = Vec3::new(
                        hn.x * 0.965_925_81 + v59 * 0.258_819_04,
                        hn.y * 0.965_925_81 + v58 * 0.258_819_04,
                        hn.z * 0.965_925_81 + v57 * 0.258_819_04,
                    );
                }
                surf0 = crate::entities::rigid::colliders::map_material(w, ent, c.id[0], pr.ipart);
                surf2 = c.i_prim[0];
            }
        }
    }
    if commit {
        release_ground(w, e);
        {
            let l = lm(w, e);
            l.ground_surface[0] = -1;
            l.ground_surface[1] = -1;
        }
        if !best_ent.is_none() {
            let g = best_ent;
            let ip = best_ipart;
            let always = i32::from(w.ent(g).parts[ip as usize].flags & 0x80_0000 != 0);
            set_ground(w, e, g, always);
            let pid = w.ent(g).parts[ip as usize].id;
            {
                let l = lm(w, e);
                l.ground_ipart = ip;
                l.ground_partid = pid;
            }
            let ge = w.ent(g);
            let part = &ge.parts[ip as usize];
            let r = ge.q.rotate(part.pos);
            let base = Vec3::new(ge.pos.x + r.x, ge.pos.y + r.y, ge.pos.z + r.z);
            let qp = ge.q.product(part.q);
            let sinv = 1.0 / part.scale;
            let rinv = |v: Vec3| crate::entities::base::geometry::rotate_inv(qp, v);
            let flying = ld(w, e).flags & super::lflags::FLYING != 0;
            let d = Vec3::new(best_pt.x - base.x, best_pt.y - base.y, best_pt.z - base.z);
            if !flying {
                let lr = rinv(d);
                let loc = Vec3::new(sinv * lr.x, sinv * lr.y, sinv * lr.z);
                let l = lm(w, e);
                l.ground_local = loc;
                l.ground_anchor = loc;
            } else {
                let dp = Vec3::new(pos.x - base.x, pos.y - base.y, pos.z - base.z);
                let lr = rinv(dp);
                let la = rinv(d);
                let l = lm(w, e);
                l.ground_local = Vec3::new(lr.x * sinv, lr.y * sinv, lr.z * sinv);
                l.ground_anchor = Vec3::new(sinv * la.x, sinv * la.y, sinv * la.z);
                l.ground_dz = ((pos.z - best_pt.z).abs() + (pos.z - best_pt.z)) * 0.5;
            }
            {
                let l = lm(w, e);
                l.ground_surface[0] = surf0;
                l.ground_surface[2] = surf2;
                l.ground_surface[1] = surf1;
            }
            let (gv, gw, com) = part_dynamics(w, g, ip);
            let v138 = best_pt.y - com.y;
            let gx = ((gw.y * (best_pt.z - com.z)) - gw.z * v138) + gv.x;
            let v144 = (gw.z * (best_pt.x - com.x)) - ((best_pt.z - com.z) * gw.x);
            let v146 = v138 * gw.x - gw.y * (best_pt.x - com.x);
            let l = lm(w, e);
            l.ground_vel = Vec3::new(gx, gv.y + v144, gv.z + v146);
            l.ground_w = gw;
        }
    }
    GroundHit {
        height: v16,
        n: n_out,
    }
}

/// `sub_35025DF0(dir, n, bWithGroundVel)`: on a jump, push the ground (simclass 1 or 2 with
/// flag 0x200; only for living entities with entity flag 1) with the impulse along `n` that
/// cancels the velocity `dir` (relative to the ground point when `bWithGroundVel`), through
/// the ground's contact matrix plus the living entity's inverse mass, applied deferred
/// (`iApplyTime = 2`). Only when the ground is light (`10·Minv_living > Minv_ground`) or has no
/// other colliders, and its contact patch status (GetStatus 19 at the feet with the gravity)
/// fails.
pub fn push_ground(w: &mut World, e: EntIdx, dir: Vec3, n: Vec3, with_ground_vel: bool) {
    let pv = w
        .ent(e)
        .q
        .rotate(Vec3::new(0.0, 0.0, ld(w, e).height_pivot));
    let p = w.ent(e).pos;
    let pt = Vec3::new(p.x - pv.x, p.y - pv.y, p.z - pv.z);
    let g = ld(w, e).ground;
    if g.is_none() || w.ent(e).flags & 1 == 0 {
        return;
    }
    if (w.ent(g).sim_class() - 1) as u32 >= 2 || w.ent(g).flags & 0x200 == 0 {
        return;
    }
    let ip = ld(w, e).ground_ipart;
    let b = crate::entities::rigid::colliders::body_of(w, g);
    let gcols = w.ent(g).colliders.clone();
    if !(ld(w, e).mass_inv * 10.0 > b.minv
        || gcols.is_empty()
        || (gcols.len() == 1 && gcols[0] == g))
    {
        return;
    }
    let mut st = vec![0u8; 64];
    wr_i32(&mut st, 0, 19);
    wr_v3(&mut st, 4, pt);
    wr_v3(&mut st, 16, ld(w, e).gravity);
    if crate::entities::interface::status::get_status(w, g, &mut st) != 0 {
        return;
    }
    let (mut a, mut bb) = (dir.x, dir.y);
    let c;
    if with_ground_vel {
        let (wy, wz, wx) = (b.w.y, b.w.z, b.w.x);
        let (bx, by, bz) = (b.pos.x, b.pos.y, b.pos.z);
        a -= (wy * (pt.z - bz) - wz * (pt.y - by)) + b.v.x;
        bb -= (wz * (pt.x - bx) - wx * (pt.z - bz)) + b.v.y;
        c = dir.z - ((wx * (pt.y - by) - wy * (pt.x - bx)) + b.v.z);
    } else {
        c = dir.z;
    }
    let mut k = [0.0f32; 9];
    contact_matrix(w, g, pt, ip, &mut k);
    let mi = ld(w, e).mass_inv;
    let v34 = n.x * (k[0] + mi);
    let v35 = (bb * n.y + n.x * a) + c * n.z;
    k[4] += mi;
    k[8] += mi;
    let v36 = (n.y * k[3] + v34) + n.z * k[6];
    let v37 = ((n.y * k[4] + n.x * k[1]) + n.z * k[7]) * n.y;
    let v38 = ((n.y * k[5] + n.x * k[2]) + n.z * k[8]) * n.z;
    let v40 = ((v35 - (-v35).abs()) * 0.5) / ((v37 + n.x * v36) + v38);
    apply_impulse(
        w,
        g,
        Vec3::new(n.x * v40, n.y * v40, n.z * v40),
        pt,
        crate::sentinel::UNUSED_I32,
        2,
        1,
    );
}
