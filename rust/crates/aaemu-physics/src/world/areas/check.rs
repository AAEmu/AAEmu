//! `CheckAreas` (world slot 87, 0x3504E000), the per-area evaluation (`sub_3504A2E0`), the
//! inside test (`sub_3504C770`) and the area callback (event 11, `sub_3504FD40`).

use super::super::events::Event;
use super::super::{EntIdx, World};
use crate::collision::pairs::PairTables;
use crate::geometry::primitives::{Primitive, SpherePrim};
use crate::geometry::GeomWorldData;
use crate::ids::GeomType;
use crate::math::Vec3;
use crate::params::ParamsBuoyancy;
use crate::sentinel::{is_unused, UNUSED_F32};
use std::sync::OnceLock;

/// The area callback event (event 11, 176 bytes in the binary): built for areas with
/// `bUseCallback` (`+520`) instead of the internal evaluation. Clients may change
/// `buoyancy` and `gravity`; the evaluation reads them back.
#[derive(Clone, Debug)]
pub struct AreaCallbackEvent {
    /// `+40` the query point (world frame).
    pub pt: Vec3,
    /// `+52` spline areas: the closest spline point (area frame); other areas: the area
    /// position.
    pub closest: Vec3,
    /// `+64` spline areas: the spline tangent at the closest point (area frame); else 0.
    pub dir: Vec3,
    /// `+76` a copy of the area's buoyancy record.
    pub buoyancy: ParamsBuoyancy,
    /// `+156` a copy of the area gravity.
    pub gravity: Vec3,
    /// `+168` the entity the query is for.
    pub ent: Option<EntIdx>,
    /// The area (not part of the binary's event; added for the game's convenience).
    pub area: EntIdx,
}

impl PartialEq for AreaCallbackEvent {
    fn eq(&self, o: &Self) -> bool {
        use crate::params::PeStruct;
        self.pt == o.pt
            && self.closest == o.closest
            && self.dir == o.dir
            && self.buoyancy.to_bytes() == o.buoyancy.to_bytes()
            && self.gravity == o.gravity
            && self.ent == o.ent
            && self.area == o.area
    }
}

/// An immediate event-11 client: may edit the event, returns a value the binary sums and
/// ignores.
pub type AreaCallback = Box<dyn FnMut(&mut AreaCallbackEvent) -> i32 + Send>;

/// The immediate event-11 clients (`world+21408`), sorted by priority (descending).
#[derive(Default)]
pub struct AreaHooks {
    clients: Vec<(f32, u64, AreaCallback)>,
    next: u64,
}

impl std::fmt::Debug for AreaHooks {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("AreaHooks").field("clients", &self.clients.len()).finish()
    }
}

fn tables() -> &'static PairTables {
    static T: OnceLock<PairTables> = OnceLock::new();
    T.get_or_init(PairTables::build)
}

/// `(bits & 0xFFA00000) == 0xFFA00000`.
#[inline]
fn unused(x: f32) -> bool {
    is_unused(x)
}

/// `sub_35038180`: AABB overlap with the binary's arithmetic (`a` = area bbox).
#[must_use]
pub fn bbox_overlap_38180(a: &[Vec3; 2], b: &[Vec3; 2]) -> bool {
    let (a0, a1, b0, b1) = (a[0], a[1], b[0], b[1]);
    let v2 = ((((a0.y + a1.y) - b0.y) - b1.y).abs() - (a1.y - a0.y)) - (b1.y - b0.y);
    let v3 = ((((a0.x + a1.x) - b0.x) - b1.x).abs() - (a1.x - a0.x)) - (b1.x - b0.x);
    let v4 = ((v3 - v2).abs() + (v3 + v2)) * 0.5;
    let v5 = ((((a0.z + a1.z) - b0.z) - b1.z).abs() - (a1.z - a0.z)) - (b1.z - b0.z);
    (((v4 - v5).abs() + (v5 + v4)) * 0.5) < 0.0
}

impl World {
    /// Register an immediate area-callback client (world slot 75 for event type 11). The
    /// binary passes the event by pointer and reads it back; here the client gets `&mut`.
    /// Returns a handle for [`World::remove_area_callback`].
    pub fn add_area_callback(&mut self, prio: f32, f: AreaCallback) -> u64 {
        let h = &mut self.area_hooks;
        h.next += 1;
        let id = h.next;
        let pos = h.clients.iter().position(|c| c.0 < prio).unwrap_or(h.clients.len());
        h.clients.insert(pos, (prio, id, f));
        id
    }

    /// Remove an area-callback client.
    pub fn remove_area_callback(&mut self, handle: u64) -> bool {
        let h = &mut self.area_hooks;
        if let Some(i) = h.clients.iter().position(|c| c.1 == handle) {
            drop(h.clients.remove(i));
            return true;
        }
        false
    }

    /// `sub_3504FD40`: deliver event 11 to the immediate clients (which may edit it), then
    /// queue it for the logged clients.
    fn signal_area_callback(&mut self, ev: &mut AreaCallbackEvent) {
        let mut hooks = std::mem::take(&mut self.area_hooks.clients);
        for c in &mut hooks {
            (c.2)(ev);
        }
        // clients registered from inside a callback go after the existing ones
        let added = std::mem::take(&mut self.area_hooks.clients);
        hooks.extend(added);
        self.area_hooks.clients = hooks;
        self.signal_event(u32::MAX, Event::AreaCallback(Box::new(ev.clone())));
    }

    /// World slot 87 `CheckAreas(pt, &gravity, buoy[], nMaxBuoys, iMedium, &vel, pent, r,
    /// iCaller, areas[], nMaxAreas)` (0x3504E000). `nMaxBuoys = buoy.len()`,
    /// `nMaxAreas = areas.len()`; records and areas are written in place (untouched slots
    /// keep their contents). Returns the number of records, or **-1** when there are none
    /// (`n | ((n − 1) >> 31)`); 0 without a global area or when nothing can apply (no record
    /// room and no non-global gravity area, all-media query).
    ///
    /// * `gravity.x` is set to the unused sentinel first; the global area's gravity is
    ///   evaluated into a local and copied out at the end if no other area set `gravity.x`.
    /// * `iMedium < 0` from a physics thread (`iCaller < 4`) with no gravity/buoyancy area
    ///   (`+21080 == 0`) becomes medium 0.
    /// * With an entity and `r == 0`, the query box is the entity's bbox and `r` its largest
    ///   half extent around `pt`; otherwise the box is the point itself.
    /// * All media (`iMedium` not in 0..16): the big-area list when every area is big, else
    ///   GetEntitiesInBox(areas). One medium: the per-medium list (only then are the areas
    ///   reported in `areas`; the global area is reported when it produced records).
    #[allow(clippy::too_many_arguments)]
    pub fn check_areas(
        &mut self,
        pt: Vec3,
        gravity: &mut Vec3,
        buoy: &mut [ParamsBuoyancy],
        imedium: i32,
        vel: Vec3,
        ent: Option<EntIdx>,
        r: f32,
        caller: i32,
        areas: &mut [EntIdx],
    ) -> i32 {
        if self.global_area.is_none() {
            return 0;
        }
        let nmax = buoy.len() as i32;
        let nmax_areas = areas.len() as i32;
        let mut bx = [pt, pt];
        let mut gl = Vec3::new(UNUSED_F32, 0.0, 0.0); // v67 (y, z uninitialised in the binary)
        let mut seen = -1i32;
        gravity.x = UNUSED_F32;
        let n21080 = self.n_gravity_areas;
        let im = imedium - ((imedium >> 31) & ((caller - 4) >> 31) & ((n21080 - 1) >> 31));
        let mut r = r;
        if let Some(e) = ent {
            if r == 0.0 {
                let b = self.ent(e).bbox;
                bx = b;
                let v34 = (b[0].x - pt.x).abs();
                let v37 = (b[1].x - pt.x).abs();
                let v38 = (b[0].z - pt.z).abs();
                let v39 = (b[0].y - pt.y).abs();
                let v42 = (b[1].y - pt.y).abs();
                let v43 = (b[1].z - pt.z).abs();
                let v44 = ((v37 - v34).abs() + (v37 + v34)) * 0.5;
                let v45 = ((v42 - v39).abs() + (v42 + v39)) * 0.5;
                let v46 = ((v43 - v38).abs() + (v43 + v38)) * 0.5;
                let v47 = ((v44 - v45).abs() + (v44 + v45)) * 0.5;
                r = ((v47 - v46).abs() + (v47 + v46)) * 0.5;
            }
        }
        let g = self.global_area;
        let n0 = self.eval_area(g, pt, &mut gl, vel, buoy, 0, nmax, &mut seen, ent);
        let mut n = n0;
        if n0 != 0 {
            seen = buoy.first().map_or(seen, |b| b.i_medium);
            if n0 > 0 && n0 < nmax_areas + 1 {
                areas[(n0 - 1) as usize] = g;
            }
        }
        if (im as u32) >= 16 {
            if nmax + self.n_gravity_areas_nonglobal == 0 {
                *gravity = gl;
                return 0;
            }
            let mut c = caller;
            if caller + self.vars.thread_bias > 4 {
                c = self.tls_thread;
            }
            if self.n_big_areas == self.n_areas {
                let mut a = self.area(g).next_big;
                while !a.is_none() {
                    if self.area(a).deleted == 0
                        && bbox_overlap_38180(&self.ent(a).bbox, &bx)
                        && self.area_inside(a, pt, 0.0, c)
                    {
                        n += self.eval_area(a, pt, gravity, vel, buoy, n, nmax, &mut seen, ent);
                    }
                    a = self.area(a).next_big;
                }
            } else {
                let list = self.get_entities_in_box(bx[0], bx[1], 32, None, c.clamp(0, 4) as usize);
                for &a in list.iter().rev() {
                    if self.area_inside(a, pt, r, c) {
                        n += self.eval_area(a, pt, gravity, vel, buoy, n, nmax, &mut seen, ent);
                    }
                }
            }
        } else if nmax != 0 {
            let mut a = self.medium_lists[im as usize];
            while !a.is_none() {
                if self.area(a).deleted == 0
                    && bbox_overlap_38180(&self.ent(a).bbox, &bx)
                    && self.area_inside(a, pt, r, caller)
                {
                    n += self.eval_area(a, pt, gravity, vel, buoy, n, nmax, &mut seen, ent);
                    if n > 0 && n < nmax_areas + 1 {
                        areas[(n - 1) as usize] = a;
                    }
                }
                a = self.area(a).next_medium;
            }
        }
        if unused(gravity.x) {
            *gravity = gl;
        }
        n | ((n - 1) >> 31)
    }

    /// `sub_3504C770(area, pt, r)`: is `pt` (world) inside the area. The point goes to the
    /// area frame (`Rᵀ·(pt − pos)·(1/scale)`, scaled first); `r` only counts for areas
    /// without gravity.
    ///
    /// * geometry areas: `r <= 0` → the geometry's PointInsideStatus (slot 8; -1 "not
    ///   supported" counts as inside, as in the binary); `r > 0` → the sphere check
    ///   (`sub_3514FBD0`: OVERLAP[type][sphere] on the prepared primitive; meshes not ported).
    /// * spline / point areas: closest squared distance below the tube radius².
    /// * 2D polygon areas: not ported (never inside).
    pub fn area_inside(&mut self, a: EntIdx, pt: Vec3, r: f32, _caller: i32) -> bool {
        let d = self.area(a);
        let inv = d.inv_scale;
        let v8 = (pt.x - d.pos.x) * inv;
        let v10 = (pt.y - d.pos.y) * inv;
        let v13 = (pt.z - d.pos.z) * inv;
        let m = &d.r.m;
        let lx = ((v10 * m[1][0]) + (v8 * m[0][0])) + (v13 * m[2][0]);
        let ly = ((v10 * m[1][1]) + (v8 * m[0][1])) + (v13 * m[2][1]);
        let lz = ((v10 * m[1][2]) + (v8 * m[0][2])) + (v13 * m[2][2]);
        let local = Vec3::new(lx, ly, lz);
        let r = if unused(d.gravity.x) { r } else { 0.0 };
        if let Some(g) = d.geom.clone() {
            if r <= 0.0 {
                return g.point_inside_status(local) != 0;
            }
            if !g.is_a_primitive() {
                return false;
            }
            let Some(prim) = g.prepare_primitive(&GeomWorldData::default()) else {
                return false;
            };
            let f = tables().overlap_fn(prim.geom_type(), GeomType::Sphere);
            return f(&prim, &Primitive::Sphere(SpherePrim { center: local, r }));
        }
        if !d.points.is_empty() {
            let rr = d.radius * d.radius;
            let (d2, _, _) = self.area_spline_closest(a, local);
            return (d2 - rr).to_bits() >> 31 != 0;
        }
        false
    }

    /// `sub_3504A2E0(area, pt, &gravity, &vel, buoy[], n, nMax, &mediumSeen, pent)`: one
    /// area's contribution. Returns the number of records it added (0 or 1; 0 when it
    /// replaced the record of the medium already reported, `mediumSeen`).
    #[allow(clippy::too_many_arguments)]
    pub(crate) fn eval_area(
        &mut self,
        a: EntIdx,
        pt: Vec3,
        gravity: &mut Vec3,
        vel: Vec3,
        buoy: &mut [ParamsBuoyancy],
        n: i32,
        nmax: i32,
        seen: &mut i32,
        ent: Option<EntIdx>,
    ) -> i32 {
        let d = self.area(a).clone();
        let m = d.r.m;
        let dx = pt.x - d.pos.x;
        let dy = pt.y - d.pos.y;
        let dz = pt.z - d.pos.z;
        let lx = (((dy * m[1][0]) + (dx * m[0][0])) + (dz * m[2][0])) * d.inv_scale;
        let ly = (((dy * m[1][1]) + (dx * m[0][1])) + (dz * m[2][1])) * d.inv_scale;
        let lz = (((dy * m[1][2]) + (dx * m[0][2])) + (dz * m[2][2])) * d.inv_scale;
        let local = Vec3::new(lx, ly, lz);
        let mut keep = 1i32; // v442
        let mut copied = 0i32; // v384 / v18
        let mut slot = n; // v31
        let skip_global = ent.is_some_and(|e| self.ent(e).flags & 0x2_0000 != 0) && a == self.global_area;
        if !(unused(d.buoyancy.water_plane_origin.x) || skip_global) {
            if d.buoyancy.i_medium == *seen {
                slot = 0;
                *seen = -1;
                keep = 0;
            }
            if slot < nmax {
                copied = 1;
                buoy[slot as usize] = d.buoyancy;
            }
        }
        if !unused(d.gravity.x) && unused(gravity.x) {
            *gravity = Vec3::ZERO;
        }
        let rot = |v: Vec3| {
            // R·v with the order of the flow transforms: (v.y·R[i][1] + v.x·R[i][0]) + v.z·R[i][2]
            Vec3::new(
                ((v.y * m[0][1]) + (v.x * m[0][0])) + (v.z * m[0][2]),
                ((v.y * m[1][1]) + (v.x * m[1][0])) + (v.z * m[1][2]),
                ((v.y * m[2][1]) + (v.x * m[2][0])) + (v.z * m[2][2]),
            )
        };

        // ---- spline flow tubes ----
        if !d.points.is_empty() && d.uniform == 0 {
            let (d2, seg, t) = self.area_spline_closest(a, local);
            let p = &d.points;
            let np = d.npoints;
            let i = seg;
            let p0 = p[(i - 1).max(0) as usize];
            let p1 = p[i as usize];
            let p2 = p[(i + 1).min(np - 1) as usize];
            let a_z = ((p2.z + p0.z) * 0.5) - p1.z;
            let b_x = p1.x - p0.x;
            let a_y = ((p2.y + p0.y) * 0.5) - p1.y;
            let b_y = p1.y - p0.y;
            let c_y = (p1.y + p0.y) * 0.5;
            let a_x = ((p2.x + p0.x) * 0.5) - p1.x;
            let b_z = p1.z - p0.z;
            let c_z = (p1.z + p0.z) * 0.5;
            let c_x = (p1.x + p0.x) * 0.5;
            let (mut rx, mut ry, mut rz) = (0.0f32, 0.0f32, 0.0f32);
            let tt = i as f32 + t;
            if tt > 0.0 && tt < np as f32 {
                let v57 = ((((a_y * t) + b_y) * t) + c_y) - ly;
                let v58 = ((((a_x * t) + b_x) * t) + c_x) - lx;
                let v59 = ((((a_z * t) + b_z) * t) + c_z) - lz;
                let l2 = (v57 * v57 + v58 * v58) + v59 * v59;
                if l2 <= f32::from_bits(0x1E3C_E508) {
                    rz = 1.0;
                } else {
                    let k = 1.0 / l2.sqrt();
                    rx = v58 * k;
                    ry = v57 * k;
                    rz = v59 * k;
                }
            }
            let t2 = t * 2.0;
            let v62 = ((t2 + 1.99) - (1.99 - t2).abs()) * 0.5;
            let v65 = ((0.01 - v62).abs() + (v62 + 0.01)) * 0.5;
            let tz = (a_z * v65) + b_z;
            let ty = (a_y * v65) + b_y;
            let tx = (v65 * a_x) + b_x;
            let l2 = (ty * ty + tx * tx) + tz * tz;
            let (gx, gy, gz) = if l2 <= f32::from_bits(0x1E3C_E508) {
                (0.0f32, 0.0f32, 1.0f32)
            } else {
                let k = 1.0 / l2.sqrt();
                (tx * k, ty * k, tz * k)
            };
            if d.use_callback == 0 {
                let v74 = 1.0 - (d2.sqrt() / d.radius);
                let v75 = ((v74 * v74) * (1.0 - d.falloff0)) + d.falloff0;
                if !unused(d.gravity.x) {
                    let damp = d.damping;
                    let (v78, v79, v80) = (vel.x * damp, vel.y * damp, vel.z * damp);
                    let v81 = (gy * v75) + (ry * (1.0 - v75));
                    let v82 = (gz * v75) + (rz * (1.0 - v75));
                    let v83 = (gx * v75) + (rx * (1.0 - v75));
                    let gm = d.gravity.z;
                    let v84 = (v81 * m[1][1]) + (v83 * m[1][0]);
                    let v87 = ((((v81 * m[2][1]) + (v83 * m[2][0])) + (v82 * m[2][2])) * gm) - v80;
                    gravity.x = (((((v83 * m[0][0]) + (v81 * m[0][1])) + (v82 * m[0][2])) * gm) - v78) + gravity.x;
                    let v88 = v87 + gravity.z;
                    gravity.y = (((v84 + (v82 * m[1][2])) * gm) - v79) + gravity.y;
                    gravity.z = v88;
                }
                if copied != 0 {
                    let fz = d.buoyancy.water_flow.z;
                    let v92 = (rx * (1.0 - v75)) + (gx * v75);
                    let v93 = (gy * v75) + (ry * (1.0 - v75));
                    let v94 = (gz * v75) + (rz * (1.0 - v75));
                    let w = rot(Vec3::new(v92, v93, v94));
                    let b = &mut buoy[slot as usize];
                    b.water_flow = Vec3::new(fz * w.x, fz * w.y, fz * w.z);
                }
                return copied & keep;
            }
            // callback with the closest spline point and the tangent (area frame)
            let closest = Vec3::new(
                c_x + (((b_x) + (a_x * t)) * t),
                (((a_y * t) + b_y) * t) + c_y,
                (((a_z * t) + b_z) * t) + c_z,
            );
            self.area_callback_eval(a, &d, pt, closest, Vec3::new(gx, gy, gz), gravity, buoy, slot, nmax, ent);
            return copied & keep;
        }

        // ---- ellipsoid / uniform / geometry areas ----
        let iv = d.inv_size;
        let mut v138 = (((ly * iv.y) * (ly * iv.y)) + ((lx * iv.x) * (lx * iv.x))) + ((lz * iv.z) * (lz * iv.z));
        if !(v138 <= 1.0) {
            return 0;
        }
        if v138 >= 1.0 {
            return copied & keep;
        }
        if d.use_callback != 0 {
            self.area_callback_eval(a, &d, pt, d.pos, Vec3::ZERO, gravity, buoy, slot, nmax, ent);
            return copied & keep;
        }
        if v138 > 0.0 {
            v138 = v138.sqrt();
        }
        let x = v138 - d.falloff0;
        let v141 = 1.0 - ((((-x).abs() + x) * 0.5) * d.inv_falloff);
        if d.uniform != 0 {
            if !unused(d.gravity.x) {
                let gv = d.gravity;
                if d.uniform == 2 {
                    let v142 = ((gv.x * m[1][0]) + (gv.y * m[1][1])) + (gv.z * m[1][2]);
                    let v143 = ((gv.x * m[2][0]) + (gv.y * m[2][1])) + (gv.z * m[2][2]);
                    gravity.x = (v141 * (((gv.x * m[0][0]) + (gv.y * m[0][1])) + (gv.z * m[0][2]))) + gravity.x;
                    let v144 = (v143 * v141) + gravity.z;
                    gravity.y = (v142 * v141) + gravity.y;
                    gravity.z = v144;
                } else {
                    gravity.x = (v141 * gv.x) + gravity.x;
                    gravity.y = (v141 * gv.y) + gravity.y;
                    gravity.z = (v141 * gv.z) + gravity.z;
                }
            }
        } else {
            let v150 = pt.x - d.pos.x;
            let v152 = pt.y - d.pos.y;
            let v153 = pt.z - d.pos.z;
            let l2 = (v152 * v152 + v150 * v150) + v153 * v153;
            let (ux, uy, uz) = if l2 <= f32::from_bits(0x1E3C_E508) {
                (0.0f32, 0.0f32, 1.0f32)
            } else {
                let k = 1.0 / l2.sqrt();
                (v150 * k, v152 * k, v153 * k)
            };
            if !unused(d.gravity.x) {
                let v159 = v141 * d.gravity.z;
                let v160 = (uy * v159) + gravity.y;
                let v161 = (uz * v159) + gravity.z;
                gravity.x = (ux * v159) + gravity.x;
                gravity.y = v160;
                gravity.z = v161;
            }
            if copied == 0 {
                return self.area_damping_record(&d, buoy, slot, nmax, keep);
            }
            let f = d.buoyancy.water_flow.z;
            buoy[slot as usize].water_flow = Vec3::new(ux * f, uy * f, uz * f);
        }
        if copied == 0 {
            return self.area_damping_record(&d, buoy, slot, nmax, keep);
        }
        // geometry-specific water planes:
        // * heightfield geometry (type 2): the water surface is fitted to the heightfield
        //   cells under the entity's bbox (least squares, `sub_3504A2E0` lines with the
        //   geometry's height callback) — not ported (no heightfield geometry, P1c).
        // * 2D polygon areas with a mesh: the plane of the triangle found by the inside test
        //   (`+336 … +384`) — not ported (slot 83).
        // Both leave the record as copied from the area here.
        let b = &mut buoy[slot as usize];
        if d.uniform == 2 {
            let f = b.water_flow;
            let v325 = (f.x * m[1][0]) + (f.y * m[1][1]);
            let v326 = (f.x * m[2][0]) + (f.y * m[2][1]);
            let v327 = f.z * m[1][2];
            let v328 = f.z * m[2][2];
            b.water_flow = Vec3::new(
                (((f.y * m[0][1]) + (f.x * m[0][0])) + (f.z * m[0][2])) * v141,
                v141 * (v325 + v327),
                v141 * (v326 + v328),
            );
        } else {
            let f = b.water_flow;
            b.water_flow = Vec3::new(v141 * f.x, v141 * f.y, v141 * f.z);
        }
        copied & keep
    }

    /// LABEL_92 of `sub_3504A2E0`: an area without a water plane but with damping > 0
    /// reports an "air" record (medium 1, plane z = 1e8 up, density 0, damping, flow 0).
    fn area_damping_record(
        &self,
        d: &super::AreaData,
        buoy: &mut [ParamsBuoyancy],
        slot: i32,
        nmax: i32,
        keep: i32,
    ) -> i32 {
        if d.damping > 0.0 && slot < nmax {
            let b = &mut buoy[slot as usize];
            b.i_medium = 1;
            b.water_plane_origin = Vec3::new(0.0, 0.0, f32::from_bits(0x4CBE_BC20)); // 1e8
            b.water_plane_n = Vec3::new(0.0, 0.0, 1.0);
            b.water_density = 0.0;
            b.water_damping = d.damping;
            b.water_resistance = 0.0;
            b.water_flow = Vec3::ZERO;
            return keep;
        }
        0
    }

    /// The callback branch of `sub_3504A2E0`: event 11 with the area's record and gravity;
    /// afterwards the gravity is added **only when the client left it unused** (`(g & mask)
    /// == mask` — inverted in the binary; the NaN sum then makes CheckAreas fall back to the
    /// global gravity), and the record is written back when it has a water plane.
    #[allow(clippy::too_many_arguments)]
    fn area_callback_eval(
        &mut self,
        a: EntIdx,
        d: &super::AreaData,
        pt: Vec3,
        closest: Vec3,
        dir: Vec3,
        gravity: &mut Vec3,
        buoy: &mut [ParamsBuoyancy],
        slot: i32,
        nmax: i32,
        ent: Option<EntIdx>,
    ) {
        let mut ev = AreaCallbackEvent {
            pt,
            closest,
            dir,
            buoyancy: d.buoyancy,
            gravity: d.gravity,
            ent,
            area: a,
        };
        self.signal_area_callback(&mut ev);
        if unused(ev.gravity.x) {
            let y = ev.gravity.y + gravity.y;
            gravity.x = ev.gravity.x + gravity.x;
            gravity.y = y;
            gravity.z = ev.gravity.z + gravity.z;
        }
        if !unused(ev.buoyancy.water_plane_origin.x) && slot < nmax {
            buoy[slot as usize] = ev.buoyancy;
        }
    }
}
