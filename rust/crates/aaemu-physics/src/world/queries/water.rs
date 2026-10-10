//! The water-plane ray of RWI (`sub_351A9540`) and the area ray test it uses
//! (`sub_3504E5E0` → `sub_3504CF00`), for plane-only areas.

use super::rwi::{RayCtx, RayHit};
use crate::collision::pairs::box_pairs::{overlap_box_ray, overlap_box_sphere};
use crate::entities::base::EntityKind;
use crate::geometry::primitives::{BoxPrim, SpherePrim};
use crate::math::{Mat33, Vec3};
use crate::world::{EntIdx, World};

/// The fields of an area hit the callers read (`ray_hit` layout).
#[derive(Clone, Copy, Debug)]
pub(crate) struct AreaHit {
    pub dist: f32,
    pub pt: Vec3,
    pub n: Vec3,
}

impl World {
    /// `sub_3504E5E0(area, org, dir, hit, 0)`: the ray against an area. Ported for areas
    /// without geometry (`+224`), points (`+304`) and circle (`+540 > 0`): the water plane
    /// (`+428` n, `+440` origin) when the area's medium (`+460`) is 0:
    /// `t = ((o − org)·n) / (dir·n)`, accepted when `|dir·n − 2·(o−org)·n| < |dir·n|`
    /// (i.e. `0 < t < 1`), `dist = |dir|·t`, `pt = org + dir·t`, `n` = the plane normal.
    /// Other areas: `None` (not ported, see [`super`]).
    pub(crate) fn area_ray(&self, a: EntIdx, org: Vec3, dir: Vec3) -> Option<AreaHit> {
        let EntityKind::Area(ad) = &self.ent(a).kind else {
            return None;
        };
        if ad.circle_radius > 0.0 || ad.geom.is_some() || ad.npoints > 0 {
            return None;
        }
        if ad.buoyancy.i_medium != 0 {
            return None;
        }
        let n = ad.buoyancy.water_plane_n;
        let o = ad.buoyancy.water_plane_origin;
        let v88 = o.x - org.x;
        let v89 = o.y - org.y;
        let v90 = o.z - org.z;
        let v91 = n.x * v88;
        let v92 = n.z * dir.z;
        let v93 = ((v89 * n.y) + v91) + (v90 * n.z);
        let v94 = ((dir.x * n.x) + (n.y * dir.y)) + v92;
        let v126 = (v94 - (v93 * 2.0)).abs() - (-v94).abs();
        if !(v126 < 0.0) {
            return None;
        }
        let v95 = v93 / v94;
        let dist = (((dir.y * dir.y) + (dir.x * dir.x)) + (dir.z * dir.z)).sqrt() * v95;
        let pt = Vec3::new((dir.x * v95) + org.x, (dir.y * v95) + org.y, (dir.z * v95) + org.z);
        Some(AreaHit { dist, pt, n })
    }

    /// `sub_351A9540(world, org, dir, ctx, flags, nMaxHits, hits)`: the water plane of the
    /// global area (and of the "big" areas with medium 0) against the ray.
    ///
    /// Quirks kept: the hit's `ipart` is written into `hits[0]` whatever the slot;
    /// `dist = |dir|·(hitDist/cosθ)` with the area hit distance (as the binary computes it);
    /// a ray starting below the water with flag 0x10000 reports the surface at 0.001.
    #[allow(clippy::too_many_lines)]
    pub(crate) fn rwi_water(&mut self, org: Vec3, dir: Vec3, ctx: &mut RayCtx, flags: u32, hits: &mut [RayHit]) {
        let ga = self.global_area;
        if ga.is_none() {
            return;
        }
        let (gn, go, gcircle) = {
            let EntityKind::Area(ad) = &self.ent(ga).kind else {
                return;
            };
            (ad.buoyancy.water_plane_n, ad.buoyancy.water_plane_origin, ad.circle_radius)
        };
        let nd = ctx.ray_ndir();
        let v20 = ((nd.y * gn.y) + (nd.x * gn.x)) + (nd.z * gn.z);
        let mut v11 = EntIdx::NONE;
        let mut v12 = 1.0f32;
        let mut v13 = 0.0f32;
        let mut nrm;
        let f10000 = flags & 0x1_0000 != 0;
        if !f10000 || org.z >= go.z || gcircle > 0.0 {
            if let Some(h) = self.area_ray(ga, org, dir) {
                v12 = h.dist;
                nrm = h.n;
                v11 = ga;
                v13 = v20;
            } else {
                nrm = gn;
            }
        } else {
            v12 = 0.001;
            v13 = v20;
            nrm = Vec3::new(0.0, 0.0, 1.0);
            v11 = ga;
        }
        let mut a = self.area(ga).next_big;
        while !a.is_none() {
            let medium0 = self.area(a).buoyancy.i_medium == 0;
            if medium0 {
                let bb = self.ent(a).bbox;
                let bx = BoxPrim {
                    basis: Mat33::IDENTITY,
                    oriented: 0,
                    center: Vec3::new(
                        (bb[1].x + bb[0].x) * 0.5,
                        (bb[0].y + bb[1].y) * 0.5,
                        (bb[0].z + bb[1].z) * 0.5,
                    ),
                    size: Vec3::new(
                        (bb[1].x - bb[0].x) * 0.5,
                        (bb[1].y - bb[0].y) * 0.5,
                        (bb[1].z - bb[0].z) * 0.5,
                    ),
                };
                if f10000
                    && overlap_box_sphere(
                        &bx,
                        &SpherePrim {
                            center: org,
                            r: 0.001,
                        },
                    )
                {
                    let o2 = Vec3::new(org.x, org.y, org.z + 100.0);
                    if self.area_ray(a, o2, Vec3::new(0.0, 0.0, -100.0)).is_some() {
                        v12 = 0.001;
                        v13 = v20;
                        nrm = Vec3::new(0.0, 0.0, 1.0);
                        v11 = a;
                        break;
                    }
                }
                if overlap_box_ray(&bx, &ctx.ray_prim()) {
                    if let Some(h) = self.area_ray(a, org, dir) {
                        if ((v13 * v13) * h.dist) <= ((v13 * v12) * v20) {
                            nrm = h.n;
                            v12 = h.dist;
                            v13 = v20;
                            v11 = a;
                        }
                    }
                }
            }
            a = self.area(a).next_big;
        }
        if (((v13 - (v12 * 2.0)).abs() - (-v13).abs()).to_bits() >> 31) == 0 {
            return;
        }
        let n_max = hits.len() as i32;
        let v34 = self.surface_flags_raw(self.water_mat);
        let slot: usize;
        if (flags & 0xF) >= (flags & 0x1000) + (v34 as u32 & 0xF) {
            slot = 0;
        } else {
            if n_max <= 1 {
                return;
            }
            if ((v34 as u32 | !flags) & 0x200) != 0 {
                slot = 1;
                ctx.set_n_front(1);
            } else {
                slot = (n_max - 1) as usize;
                ctx.set_n_back(1);
            }
        }
        let v36 = if v13 == 0.0 { 0.0 } else { v12 / v13 };
        let v38 = ((dir.x * dir.x) + (dir.y * dir.y)) + (dir.z * dir.z);
        hits[slot].collider = v11;
        hits[slot].dist = v38.sqrt() * v36;
        hits[0].ipart = 0;
        hits[slot].partid = 0;
        hits[slot].idmat_org = 0;
        hits[slot].surface_idx = self.water_mat as i16;
        hits[slot].pt = Vec3::new((dir.x * v36) + org.x, (dir.y * v36) + org.y, (dir.z * v36) + org.z);
        hits[slot].n = nrm;
        hits[slot].terrain = 0;
    }
}
