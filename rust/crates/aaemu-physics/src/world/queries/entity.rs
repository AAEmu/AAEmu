//! Entity-level queries: `RayTraceEntity` (world slot 61, 0x3508AA10),
//! `CollideEntityWithPrimitive` (slot 60, 0x350874B0) and `CollideEntityWithBeam` (slot 59,
//! 0x35086DB0), plus the oriented point-in-box test 0x350974A0.

use super::pwi::{part_gwd, query_box, query_geometry};
use super::rwi::RayHit;
use super::{aabb_overlap6, idmat_org, map_surface, QueryIntersectionParams};
use crate::collision::contact::IntersectionParams;
use crate::collision::pairs::box_pairs::overlap_box_ray;
use crate::collision::prim_intersect::scratch::ThreadScratch;
use crate::collision::prim_intersect::{env_with_generic, intersect, with_thread_scratch};
use crate::geometry::primitives::{BoxPrim, Primitive, RayPrim, SpherePrim};
use crate::geometry::shapes::RayGeom;
use crate::geometry::{GeomWorldData, Geometry};
use crate::math::{Mat33, Quat, Vec3};
use crate::world::{EntIdx, World};

/// The placement override of `RayTraceEntity` (`pe_params_pos*`; matrices not supported):
/// position, orientation and scale (`None` = 1, the unused sentinel).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct PosOverride {
    pub pos: Vec3,
    pub q: Quat,
    pub scale: Option<f32>,
}

/// `sub_350974A0`: `pt` inside the box (strictly; oriented boxes rotate `pt − centre` by the
/// basis rows).
#[must_use]
pub fn point_in_box(b: &BoxPrim, pt: Vec3) -> bool {
    let mut v3 = pt.x - b.center.x;
    let mut v4 = pt.y - b.center.y;
    let mut v5 = pt.z - b.center.z;
    if b.oriented != 0 {
        let m = b.basis.to_array();
        let v6 = ((v4 * m[1]) + (v3 * m[0])) + (v5 * m[2]);
        let v7 = (v3 * m[3]) + (v4 * m[4]);
        let v8 = v5 * m[5];
        let v9 = v5 * m[8];
        let v10 = (v3 * m[6]) + (v4 * m[7]);
        v3 = v6;
        v5 = v10 + v9;
        v4 = v7 + v8;
    }
    let v11 = v4.abs() - b.size.y;
    let v12 = v3.abs() - b.size.x;
    let v13 = v5.abs() - b.size.z;
    let v14 = ((v12 - v11).abs() + (v12 + v11)) * 0.5;
    ((((v14 - v13).abs() + (v13 + v14)) * 0.5).to_bits() >> 31) != 0
}

/// `R·v·s + off` with `((R[i][0]·x + R[i][1]·y) + R[i][2]·z)·s + off[i]`.
fn to_world(g: &GeomWorldData, v: Vec3) -> Vec3 {
    let r = &g.r.m;
    Vec3::new(
        (((r[0][0] * v.x) + (r[0][1] * v.y)) + (r[0][2] * v.z)) * g.scale + g.offset.x,
        (((r[1][0] * v.x) + (r[1][1] * v.y)) + (r[1][2] * v.z)) * g.scale + g.offset.y,
        (((r[2][0] * v.x) + (r[2][1] * v.y)) + (r[2][2] * v.z)) * g.scale + g.offset.z,
    )
}

/// `Rᵀ·(p − off)·(1/s)` (`1/s` only when `s != 1`), as `((dy·R[1][k]) + (dx·R[0][k])) +
/// (dz·R[2][k])`.
fn to_local(g: &GeomWorldData, p: Vec3) -> Vec3 {
    let inv = if g.scale != 1.0 { 1.0 / g.scale } else { 1.0 };
    let r = &g.r.m;
    let (dx, dy, dz) = (p.x - g.offset.x, p.y - g.offset.y, p.z - g.offset.z);
    Vec3::new(
        (((dy * r[1][0]) + (dx * r[0][0])) + (dz * r[2][0])) * inv,
        (((dy * r[1][1]) + (dx * r[0][1])) + (dz * r[2][1])) * inv,
        (((dy * r[1][2]) + (dx * r[0][2])) + (dz * r[2][2])) * inv,
    )
}

impl World {
    /// `RayTraceEntity(ent, org, dir, hit*, pos*, geomFlagsAny, bIgnoreBackfaces)` (slot 61,
    /// 0x3508AA10): the ray against the entity's parts with `flags & geom_flags_any`
    /// (bbox-prefiltered for parts with a non-degenerate bbox), placed by `pos` or the
    /// entity's own frame. Per part the last contact (scanning backwards) that is not farther
    /// than the current hit (and, with `ignore_back_faces`, faces the ray) becomes the hit.
    /// Areas use the area ray test. Returns 1 if `hit.dist < 1e9`.
    pub fn ray_trace_entity(
        &mut self,
        e: EntIdx,
        org: Vec3,
        dir: Vec3,
        hit: &mut RayHit,
        pos: Option<PosOverride>,
        geom_flags_any: u32,
        ignore_back_faces: bool,
    ) -> i32 {
        if ((dir.x * dir.x) + (dir.y * dir.y)) + (dir.z * dir.z) <= 0.0
            || ((org.x * org.x) + (org.y * org.y)) + (org.z * org.z) < 0.0
        {
            return 0;
        }
        hit.dist = f32::from_bits(0x5015_02F9);
        if self.ent(e).id_bits & 0xFF00_0000 == 0x0500_0000 {
            return match self.area_ray(e, org, dir) {
                Some(h) => {
                    hit.dist = h.dist;
                    hit.pt = h.pt;
                    hit.n = h.n;
                    hit.collider = e;
                    hit.partid = 0;
                    hit.surface_idx = 0;
                    hit.idmat_org = 0;
                    hit.foreign_idx = 0;
                    1
                }
                None => 0,
            };
        }
        let (epos, eq, escale) = match pos {
            Some(p) => (p.pos, p.q, p.scale.unwrap_or(1.0)),
            None => {
                let ent = self.ent(e);
                (ent.pos, ent.q, 1.0)
            }
        };
        let ray = RayGeom::new(&RayPrim { origin: org, dir });
        let ray_prim = RayPrim { origin: org, dir };
        let nparts = self.ent(e).parts.len();
        for i in 0..nparts {
            let part = &self.ent(e).parts[i];
            if geom_flags_any & part.flags == 0 {
                continue;
            }
            let b = part.bbox;
            let diag = (((b[1].x - b[0].x) * (b[1].x - b[0].x)) + ((b[1].y - b[0].y) * (b[1].y - b[0].y)))
                + ((b[1].z - b[0].z) * (b[1].z - b[0].z));
            if diag != 0.0 {
                let bx = BoxPrim {
                    basis: Mat33::IDENTITY,
                    oriented: 0,
                    center: Vec3::new(
                        (b[1].x + b[0].x) * 0.5,
                        (b[1].y + b[0].y) * 0.5,
                        (b[1].z + b[0].z) * 0.5,
                    ),
                    size: Vec3::new(
                        (b[1].x - b[0].x) * 0.5,
                        (b[1].y - b[0].y) * 0.5,
                        (b[1].z - b[0].z) * 0.5,
                    ),
                };
                if !overlap_box_ray(&bx, &ray_prim) {
                    continue;
                }
            }
            let r = eq.rotate(part.pos);
            let gwd = GeomWorldData {
                offset: Vec3::new(epos.x + r.x, epos.y + r.y, epos.z + r.z),
                r: Mat33::from_quat(eq.product(part.q)),
                scale: escale * part.scale,
                ..GeomWorldData::default()
            };
            let geom = part.pgeom.geom.clone();
            let mut ip = IntersectionParams::default();
            let mut contacts = Vec::new();
            let n = geom.intersect(&ray, &gwd, &GeomWorldData::default(), &mut ip, &mut contacts);
            let mut k = n.min(contacts.len());
            while k > 0 {
                let c = &contacts[k - 1];
                if f64::from(hit.dist) >= c.t
                    && (!ignore_back_faces || ((dir.y * c.n.y) + (dir.x * c.n.x)) + (dir.z * c.n.z) <= 0.0)
                {
                    break;
                }
                k -= 1;
            }
            if k > 0 {
                let c = &contacts[k - 1];
                let parts = &self.ent(e).parts;
                let part = &parts[i];
                hit.collider = e;
                hit.ipart = i as i32;
                hit.dist = c.t as f32;
                hit.partid = part.id;
                hit.surface_idx = map_surface(parts, c.id[0], i as i32) as i16;
                hit.idmat_org = idmat_org(c.id[0], part.surface_bits);
                hit.foreign_idx = part.pgeom.geom.foreign_idx(c.i_prim[0]);
                hit.pt = c.pt;
                hit.n = c.n;
            }
        }
        i32::from(((hit.dist - 1.0e9).to_bits() >> 31) != 0)
    }

    /// `CollideEntityWithPrimitive(ent, itype, prim, sweepDir, hit*, ip*, geomFlagsAny,
    /// bCheckInside)` (slot 60, 0x350874B0): the primitive (swept along `sweep_dir` with the
    /// default params) against the entity's parts with `flags & geom_flags_any` and a
    /// degenerate or overlapping bbox. The closest first contact of a part becomes the hit
    /// (normal pointing away from the entity). Then the containment checks: the primitive's
    /// centre inside the part (point-in-box prefilter when `check_inside` and the entity bbox
    /// is not degenerate) or the part's centre inside the primitive end the query with
    /// `dist = 0` and that centre as `pt` (a geometry answering "not supported", −1, counts as
    /// inside). Returns 1 if `hit.dist < 1e9`.
    #[allow(clippy::too_many_arguments, clippy::too_many_lines)]
    pub fn collide_entity_with_primitive(
        &mut self,
        e: EntIdx,
        prim: &Primitive,
        sweep_dir: Vec3,
        hit: &mut RayHit,
        ip: Option<&mut QueryIntersectionParams>,
        geom_flags_any: u32,
        check_inside: bool,
    ) -> i32 {
        if e.is_none() || self.ent(e).id_bits & 0xFF00_0000 == 0x0500_0000 {
            return 0;
        }
        hit.dist = f32::from_bits(0x5015_02F9);
        let Some((geom, center)) = query_geometry(prim) else {
            return 0;
        };
        let mut gwd1 = GeomWorldData {
            offset: center,
            v: sweep_dir,
            ..GeomWorldData::default()
        };
        let mut own_ip = IntersectionParams::default();
        let ipr: &mut IntersectionParams = match ip {
            Some(q) => &mut q.ip,
            None => {
                let d = sweep_dir;
                own_ip.sweep_test = ((d.y * d.y) + (d.x * d.x)) + (d.z * d.z) > 0.0;
                own_ip.time_interval = 1.0;
                &mut own_ip
            }
        };
        let qbox = query_box(geom.as_ref(), gwd1.offset, sweep_dir);
        let eb = self.ent(e).bbox;
        let v68 = (((eb[1].y - eb[0].y) * (eb[1].y - eb[0].y)) + ((eb[1].x - eb[0].x) * (eb[1].x - eb[0].x)))
            + ((eb[1].z - eb[0].z) * (eb[1].z - eb[0].z));
        if v68 < 0.0 {
            return i32::from(((hit.dist - 1.0e9).to_bits() >> 31) != 0);
        }
        let env = env_with_generic();
        let nparts = self.ent(e).parts.len();
        for i in 0..nparts {
            let (pf, pbb) = {
                let p = &self.ent(e).parts[i];
                (p.flags, p.bbox)
            };
            if geom_flags_any & pf == 0 {
                continue;
            }
            let diag = (((pbb[1].y - pbb[0].y) * (pbb[1].y - pbb[0].y)) + ((pbb[1].x - pbb[0].x) * (pbb[1].x - pbb[0].x)))
                + ((pbb[1].z - pbb[0].z) * (pbb[1].z - pbb[0].z));
            if !(diag == 0.0 || aabb_overlap6(&pbb, &qbox)) {
                continue;
            }
            let gwd2 = part_gwd(self, e, i);
            let pgeom = self.ent(e).parts[i].pgeom.geom.clone();
            let c0 = with_thread_scratch(|s| {
                let out = intersect(&env, s, geom.as_ref(), pgeom.as_ref(), &mut gwd1, &gwd2, ipr);
                (out.ret != 0).then(|| s.contact(out.first.unwrap_or(0)))
            });
            if let Some(c) = c0 {
                if c.t < f64::from(hit.dist) {
                    let parts = &self.ent(e).parts;
                    let part = &parts[i];
                    hit.collider = e;
                    hit.ipart = i as i32;
                    hit.dist = c.t as f32;
                    hit.partid = part.id;
                    hit.surface_idx = map_surface(parts, c.id[1], i as i32) as i16;
                    hit.idmat_org = idmat_org(c.id[1], part.surface_bits);
                    hit.foreign_idx = part.pgeom.geom.foreign_idx(c.i_prim[1]);
                    hit.pt = c.pt;
                    hit.n = Vec3::new(-c.n.x, -c.n.y, -c.n.z);
                }
            }
            // containment: the primitive's centre inside the part
            let pc = to_world(&gwd1, geom.center());
            let local2 = to_local(&gwd2, pc);
            let pass = v68 <= 0.0 || !check_inside || point_in_box(&pgeom.bbox(), local2);
            if pass && pgeom.point_inside_status(local2) != 0 {
                hit.dist = 0.0;
                hit.pt = pc;
                return 1;
            }
            // the part's centre inside the primitive
            let qc = to_world(&gwd2, pgeom.center());
            let local1 = to_local(&gwd1, qc);
            if geom.point_inside_status(local1) != 0 {
                hit.dist = 0.0;
                hit.pt = qc;
                return 1;
            }
        }
        i32::from(((hit.dist - 1.0e9).to_bits() >> 31) != 0)
    }

    /// `CollideEntityWithBeam(ent, org, dir, r, hit*, geomFlagsAny)` (slot 59, 0x35086DB0): a
    /// sphere of radius `r` at `org` swept along `dir` against the entity's parts with
    /// `flags & geom_flags_any` (no bbox prefilter). Returns 1 if `hit.dist < 1e9`.
    pub fn collide_entity_with_beam(
        &mut self,
        e: EntIdx,
        org: Vec3,
        dir: Vec3,
        r: f32,
        hit: &mut RayHit,
        geom_flags_any: u32,
    ) -> i32 {
        if e.is_none() {
            return 0;
        }
        let Some(geom) = crate::world::geoman::create_primitive(&Primitive::Sphere(SpherePrim {
            center: Vec3::ZERO,
            r,
        })) else {
            return 0;
        };
        let mut ip = IntersectionParams {
            sweep_test: ((dir.x * dir.x) + (dir.y * dir.y)) + (dir.z * dir.z) > 0.0,
            time_interval: 1.0,
            ..IntersectionParams::default()
        };
        let mut gwd1 = GeomWorldData {
            offset: org,
            v: dir,
            ..GeomWorldData::default()
        };
        hit.dist = f32::from_bits(0x5015_02F9);
        let env = env_with_generic();
        let nparts = self.ent(e).parts.len();
        for i in 0..nparts {
            if geom_flags_any & self.ent(e).parts[i].flags == 0 {
                continue;
            }
            let gwd2 = part_gwd(self, e, i);
            let pgeom = self.ent(e).parts[i].pgeom.geom.clone();
            let c0 = with_thread_scratch(|s| {
                let out = intersect(&env, s, geom.as_ref(), pgeom.as_ref(), &mut gwd1, &gwd2, &mut ip);
                (out.ret != 0).then(|| s.contact(out.first.unwrap_or(0)))
            });
            if let Some(c) = c0 {
                if c.t < f64::from(hit.dist) {
                    let parts = &self.ent(e).parts;
                    let part = &parts[i];
                    hit.collider = e;
                    hit.ipart = i as i32;
                    hit.dist = c.t as f32;
                    hit.partid = part.id;
                    hit.surface_idx = map_surface(parts, c.id[1], i as i32) as i16;
                    hit.idmat_org = idmat_org(c.id[1], part.surface_bits);
                    hit.foreign_idx = part.pgeom.geom.foreign_idx(c.i_prim[1]);
                    hit.pt = c.pt;
                    hit.n = Vec3::new(-c.n.x, -c.n.y, -c.n.z);
                }
            }
        }
        i32::from(((hit.dist - 1.0e9).to_bits() >> 31) != 0)
    }
}

#[allow(dead_code)]
fn _unused(_: &ThreadScratch, _: &dyn Geometry) {}
