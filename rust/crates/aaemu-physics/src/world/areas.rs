//! **P3 — areas** (`CPhysArea`, 0x228 bytes, GetType 9; vtable 0x351EF598; ctor 0x35042150).
//! `world.md` §7.
//!
//! An area is stored in the world's object store like an entity ([`EntityKind::Area`]); the
//! placeholder header (bbox, grid cells, simclass 5, `+64` lock word, `+80` deleted state) is
//! the [`Entity`] part, the area members are [`AreaData`]. The area lists use their own links
//! (`+88` all areas — rooted at the global area —, `+96` big areas, `+104` per-medium list).
//!
//! | function | address | port |
//! |---|---|---|
//! | ctor | 0x35042150 | [`AreaData::new`] |
//! | dtor | 0x350415F0 | [`World::cleanup_deleted_areas`] |
//! | AddGlobalArea (world 80) | 0x3504A1A0 | [`World::add_global_area`] |
//! | AddArea spline / points (world 81) | 0x35049C60 | [`World::add_area_spline`] |
//! | AddArea geometry (world 82) | 0x35049790 | [`World::add_area_geometry`] |
//! | AddArea 2D polygon (world 83) | 0x35047C80 | not ported (see below) |
//! | RemoveArea (world 84) | 0x35041BE0 | [`World::remove_area`] |
//! | GetNextArea (world 85) | 0x350418A0 | [`World::get_next_area`] |
//! | IsAreaValid (world 86) | 0x35041910 | [`World::is_area_valid`] |
//! | CheckAreas (world 87) | 0x3504E000 | [`World::check_areas`] ([`check`]) |
//! | SetParams (area slot 7) | 0x35045B50 | [`World::area_set_params_pos`] (0), [`World::area_set_params_simulation`] (10), [`World::area_set_params_buoyancy`] (12), [`World::area_set_params_area`] (18) |
//! | GetParams (area slot 8) | 0x35041740 | [`World::area_get_params_simulation`], [`World::area_get_params_area`], [`World::area_get_params_buoyancy`] |
//! | GetStatus pos (area slot 9, type 1) | 0x3504EE60 | [`World::area_get_status_pos`] |
//! | grid registration | 0x35041A30 | [`World::area_register_grid`] |
//! | event 12 | 0x3504FE20 / 0x350419C0 | [`World::area_signal_change`] |
//! | gravity/buoyancy counter test | 0x350416A0 | [`World::area_counts_21080`] |
//!
//! **Not ported:** the 2D-polygon areas of world slot 83 (with the triangulation helpers
//! 0x35122FA0 / 0x35113200, the sorted edge lists `+256/+264/+280` and the water-plane
//! triangle `+336 … +384`): they need a trimesh geometry (P1c). The heightfield water-surface
//! fit of the per-area evaluation (geometry type 2) needs a heightfield geometry (P1c). Both
//! paths are documented at their branch in [`check`]. The SetParams command queue
//! (`sub_35023200`: deferral while the world is locked by another thread) does not exist in
//! the single-threaded port: area SetParams always applies immediately.

// Bit-exact port: operand order and NaN-aware negated comparisons follow the binary.
#![allow(
    clippy::assign_op_pattern,
    clippy::neg_cmp_op_on_partial_ord,
    clippy::needless_range_loop
)]

pub mod check;
pub mod spline;

use super::{EntIdx, World};
use crate::entities::base::{Entity, EntityKind};
use crate::geometry::Geometry;
use crate::math::{Mat33, Quat, Vec3};
use crate::params::{ParamsArea, ParamsBuoyancy, ParamsPos};
use crate::sentinel::{is_unused, UNUSED_F32};
use std::sync::Arc;

pub use check::{AreaCallback, AreaCallbackEvent};

/// `CPhysArea` members (offsets `A+x`).
#[derive(Clone, Debug)]
pub struct AreaData {
    /// `+80` deleted state (0 active, 2 removed). Mirrored in [`Entity::deleted`] (same offset
    /// in the binary).
    pub deleted: i32,
    /// `+88` next area (all-areas list rooted at the global area; for removed areas the
    /// deleted-areas list), `+96` next "big" area, `+104` next in the per-medium list.
    pub next_all: EntIdx,
    pub next_big: EntIdx,
    pub next_medium: EntIdx,
    /// `+120` position, `+132` rotation, `+168` scale, `+172` 1/scale.
    pub pos: Vec3,
    pub r: Mat33,
    pub scale: f32,
    pub inv_scale: f32,
    /// `+176` secondary offset, `+188` base rotation (`R = R(q)·R0` in SetParams type 0).
    pub offset0: Vec3,
    pub r0: Mat33,
    /// `+224` geometry (geometry areas).
    pub geom: Option<Arc<dyn Geometry>>,
    /// `+240` number of points, `+244` spline tube radius / polygon thickness.
    pub npoints: i32,
    pub radius: f32,
    /// `+304` points (spline / polygon), area frame.
    pub points: Vec<Vec3>,
    /// `+312` closest-point cache of the spline query (1e10 = unset), `+324` its segment,
    /// `+328` its parameter, `+332` its squared distance.
    pub cache_pt: Vec3,
    pub cache_seg: i32,
    pub cache_t: f32,
    pub cache_d2: f32,
    /// `+388 … +467` the buoyancy record (params type 12, copied verbatim).
    pub buoyancy: ParamsBuoyancy,
    /// `+468` gravity (`x` unused = no gravity).
    pub gravity: Vec3,
    /// `+480` falloff size, `+492` inverse (0 where the size is <= 0).
    pub size: Vec3,
    pub inv_size: Vec3,
    /// `+504` uniform mode (0 radial/spline, 1 uniform, 2 area-frame), `+508` falloff0,
    /// `+512` 1/(1−falloff0), `+516` damping.
    pub uniform: i32,
    pub falloff0: f32,
    pub inv_falloff: f32,
    pub damping: f32,
    /// `+520` bUseCallback (evaluate through event 11).
    pub use_callback: i32,
    /// `+524` refcount (a removed area is freed when it is 0).
    pub refcount: i32,
    /// `+528` centre, `+540` radius (-1), `+544` the 12-point circle built from them.
    pub center: Vec3,
    pub circle_radius: f32,
    pub circle: Vec<Vec3>,
}

impl AreaData {
    /// The ctor 0x35042150. Fields the ctor leaves alone come from a fresh heap block, which
    /// the oracle shows zeroed: the record's flow `y, z` (`+416`, `+420`), plane `n.y, n.z`,
    /// `origin.y, origin.z`, `+460`… and the gravity `y, z` are 0.
    #[must_use]
    pub fn new() -> Self {
        let u = UNUSED_F32;
        let b = ParamsBuoyancy {
            water_density: u,
            k_water_density: u,
            water_damping: u,
            water_resistance: u,
            k_water_resistance: u,
            water_flow: Vec3::new(u, 0.0, 0.0),
            flow_variance: u,
            water_plane_n: Vec3::new(u, 0.0, 0.0),
            water_plane_origin: Vec3::new(u, 0.0, 0.0),
            water_emin: u,
            k_air_resistance: u,
            i_medium: i32::MIN,
            water_level_offset: u,
        };
        Self {
            deleted: 0,
            next_all: EntIdx::NONE,
            next_big: EntIdx::NONE,
            next_medium: EntIdx::NONE,
            pos: Vec3::ZERO,
            r: Mat33::IDENTITY,
            scale: 1.0,
            inv_scale: 1.0,
            offset0: Vec3::ZERO,
            r0: Mat33::IDENTITY,
            geom: None,
            npoints: 0,
            radius: 0.0,
            points: Vec::new(),
            cache_pt: Vec3::splat(1e10),
            cache_seg: 0,
            cache_t: 0.0,
            cache_d2: 0.0,
            buoyancy: b,
            gravity: Vec3::new(u, 0.0, 0.0),
            size: Vec3::ZERO,
            inv_size: Vec3::ZERO,
            uniform: 1,
            falloff0: 0.0,
            inv_falloff: 1.0,
            damping: 0.0,
            use_callback: 0,
            refcount: 0,
            center: Vec3::ZERO,
            circle_radius: -1.0,
            circle: Vec::new(),
        }
    }
}

impl Default for AreaData {
    fn default() -> Self {
        Self::new()
    }
}

/// `(bits & 0xFFA00000) == 0xFFA00000`.
#[inline]
fn unused(x: f32) -> bool {
    is_unused(x)
}

/// A new placeholder object for an area (`operator new(0x228)` + ctor 0x35042150).
fn new_area_entity(d: AreaData) -> Entity {
    let mut ent = Entity::new_base(9, EntityKind::Area(Box::new(d)));
    // placeholder part of the ctor: id bits cleared, simclass 5, cells -2, bbox 0
    ent.id_bits = 0x0500_0000;
    ent.grid_cells = [-2, -2, -2, -2];
    ent.i_foreign_flags = 0;
    ent.bbox = [Vec3::ZERO; 2];
    ent.processed = 0;
    ent
}

impl World {
    /// The area payload of `a`.
    #[must_use]
    pub fn area(&self, a: EntIdx) -> &AreaData {
        match &self.ent(a).kind {
            EntityKind::Area(d) => d,
            _ => panic!("not an area"),
        }
    }

    /// The area payload of `a` (mutable).
    pub fn area_mut(&mut self, a: EntIdx) -> &mut AreaData {
        match &mut self.ent_mut(a).kind {
            EntityKind::Area(d) => d,
            _ => panic!("not an area"),
        }
    }

    /// True when `a` is a live area object.
    #[must_use]
    pub fn is_area(&self, a: EntIdx) -> bool {
        !a.is_none()
            && self.ents.get(a.0 as usize).is_some_and(|o| {
                o.as_ref()
                    .is_some_and(|e| matches!(e.kind, EntityKind::Area(_)))
            })
    }

    /// Next area in the all-areas list (`+88`).
    #[must_use]
    pub fn area_next(&self, a: EntIdx) -> EntIdx {
        self.area(a).next_all
    }

    /// Create the global area object if there is none (the inline block of world slots 80–83:
    /// ctor + gravity = world gravity; bbox stays 0 and no event when it is created by 81–83).
    fn ensure_global_area(&mut self) -> bool {
        if !self.global_area.is_none() {
            return false;
        }
        let mut d = AreaData::new();
        d.gravity = self.gravity();
        let a = self.store(new_area_entity(d));
        self.global_area = a;
        true
    }

    /// World slot 80 `AddGlobalArea` (0x3504A1A0): create the global area once (bbox
    /// ±1e10, gravity = world gravity; not in the grid, not counted in `+20936`) and signal
    /// event 12.
    pub fn add_global_area(&mut self) -> EntIdx {
        if self.ensure_global_area() {
            let a = self.global_area;
            self.ent_mut(a).bbox = [Vec3::splat(-1e10), Vec3::splat(1e10)];
            self.area_signal_change(a);
        }
        self.global_area
    }

    /// Insert a new area after the global area in the all-areas list and register it in the
    /// grid (the common tail of world slots 81–83).
    fn link_new_area(&mut self, a: EntIdx) {
        let g = self.global_area;
        let next = self.area(g).next_all;
        self.area_mut(a).next_all = next;
        self.area_mut(g).next_all = a;
        self.area_register_grid(a);
    }

    /// World slot 81 `AddArea(pts, npt, r, pos, q, scale)` (0x35049C60): a tube of radius `r`
    /// around the points (`+304`, area frame). The bbox is the transformed points inflated by
    /// `r`; falloff0 = 0.8, damping = 1. Returns `None` for `npt <= 0`.
    ///
    /// The area is a spline flow tube only after `SetParams(pe_params_area{bUniform = 0})`
    /// (the ctor sets the uniform mode 1).
    pub fn add_area_spline(
        &mut self,
        pts: &[Vec3],
        r: f32,
        pos: Vec3,
        q: Quat,
        scale: f32,
    ) -> Option<EntIdx> {
        if pts.is_empty() {
            return None;
        }
        self.ensure_global_area();
        let mut d = AreaData::new();
        d.pos = pos;
        d.r = Mat33::from_quat(q);
        d.scale = scale;
        d.inv_scale = 1.0 / scale;
        d.offset0 = Vec3::ZERO;
        d.r0 = Mat33::IDENTITY;
        d.radius = r;
        d.npoints = pts.len() as i32;
        d.points = pts.to_vec();
        let mut ent = new_area_entity(d);
        let t = |p: Vec3| {
            let v = q.rotate(p);
            Vec3::new(
                v.x * scale + pos.x,
                v.y * scale + pos.y,
                v.z * scale + pos.z,
            )
        };
        let p0 = t(pts[0]);
        let (mut lo, mut hi) = (p0, p0);
        for &p in pts {
            let w = t(p);
            lo.x = ((lo.x + w.x) - (lo.x - w.x).abs()) * 0.5;
            lo.y = ((w.y + lo.y) - (lo.y - w.y).abs()) * 0.5;
            lo.z = ((w.z + lo.z) - (lo.z - w.z).abs()) * 0.5;
            let hz = ((hi.z - w.z).abs() + (w.z + hi.z)) * 0.5;
            let hy = ((hi.y - w.y).abs() + (w.y + hi.y)) * 0.5;
            hi.x = ((hi.x - w.x).abs() + (hi.x + w.x)) * 0.5;
            hi.y = hy;
            hi.z = hz;
        }
        ent.bbox = [
            Vec3::new(lo.x - r, lo.y - r, lo.z - r),
            Vec3::new(r + hi.x, r + hi.y, r + hi.z),
        ];
        if let EntityKind::Area(d) = &mut ent.kind {
            d.damping = 1.0;
            d.falloff0 = f32::from_bits(0x3F4C_CCCD); // 0.8
        }
        let a = self.store(ent);
        self.n_areas += 1;
        self.ent_count_by_type[9] += 1;
        self.link_new_area(a);
        Some(a)
    }

    /// World slot 82 `AddArea(IGeometry*, pos, q, scale)` (0x35049790): a geometry area. The
    /// bbox is the geometry's box (slot 6) rotated by `R(q)` (`Basis·Rᵀ`, absolute values)
    /// and centred at `R(q)·center·scale + pos` (quaternion rotation here, the matrix in
    /// SetParams type 0).
    pub fn add_area_geometry(
        &mut self,
        geom: Arc<dyn Geometry>,
        pos: Vec3,
        q: Quat,
        scale: f32,
    ) -> EntIdx {
        self.ensure_global_area();
        let mut d = AreaData::new();
        d.pos = pos;
        d.r = Mat33::from_quat(q);
        d.scale = scale;
        d.inv_scale = 1.0 / scale;
        let bb = geom.bbox();
        let (ext, _) = rotated_box_extent(&bb, &d.r, scale);
        let c = q.rotate(bb.center);
        let cx = c.x * scale + pos.x;
        let cy = c.y * scale + pos.y;
        let cz = c.z * scale + pos.z;
        d.geom = Some(geom);
        let mut ent = new_area_entity(d);
        ent.bbox = [
            Vec3::new(cx - ext.x, cy - ext.y, cz - ext.z),
            Vec3::new(cx + ext.x, cy + ext.y, cz + ext.z),
        ];
        let a = self.store(ent);
        self.n_areas += 1;
        self.ent_count_by_type[9] += 1;
        self.link_new_area(a);
        a
    }

    /// `sub_35041A30`: register the area in the entity grid (RepositionEntity with flag 1).
    /// An area covering too many cells (`-1`) goes into the big-area list (`+96` from the
    /// global area, count `+20940`); an area that fits again leaves it. Always signals event
    /// 12 with the area's bbox.
    pub fn area_register_grid(&mut self, a: EntIdx) {
        // predecessor of `a` in the big list (or none)
        let mut prev = self.global_area;
        loop {
            let n = self.area(prev).next_big;
            if n == a {
                break;
            }
            prev = n;
            if n.is_none() {
                break;
            }
        }
        let r = self.reposition_entity(a, 1, None);
        if r != 0 {
            if r == -1 {
                if prev.is_none() {
                    let g = self.global_area;
                    let gn = self.area(g).next_big;
                    self.area_mut(a).next_big = gn;
                    self.area_mut(g).next_big = a;
                    self.n_big_areas += 1;
                }
            } else {
                if !prev.is_none() {
                    let an = self.area(a).next_big;
                    self.area_mut(prev).next_big = an;
                    self.n_big_areas -= 1;
                }
                self.area_mut(a).next_big = EntIdx::NONE;
            }
        }
        self.area_signal_change(a);
    }

    /// Event 12 (`sub_350419C0` → `sub_3504FE20`): area added / changed / removed, with the
    /// area's bbox. Delivered to the immediate clients and always queued for the logged ones.
    pub fn area_signal_change(&mut self, a: EntIdx) {
        let bbox = self.ent(a).bbox;
        self.signal_event(u32::MAX, super::events::Event::AreaChange { area: a, bbox });
    }

    /// `sub_350416A0`: does the area count in `+21080` (non-global, with gravity, or with a
    /// water plane, a medium > 0 and `(damping + density) + resistance > 0`).
    #[must_use]
    pub fn area_counts_21080(&self, a: EntIdx) -> bool {
        let d = self.area(a);
        let b = &d.buoyancy;
        a != self.global_area
            && (!unused(d.gravity.x)
                || (!unused(b.water_plane_origin.x)
                    && b.i_medium > 0
                    && (b.water_damping + b.water_density) + b.water_resistance > 0.0))
    }

    /// Does the area count in `+21084` (non-global with gravity).
    fn area_counts_21084(&self, a: EntIdx) -> bool {
        a != self.global_area && !unused(self.area(a).gravity.x)
    }

    /// Unlink `a` from the per-medium list it is in (`+460` in 0..16).
    fn unlink_medium(&mut self, a: EntIdx) {
        let m = self.area(a).buoyancy.i_medium;
        if (m as u32) < 16 {
            let head = self.medium_lists[m as usize];
            if head == a {
                self.medium_lists[m as usize] = self.area(a).next_medium;
            } else if !head.is_none() {
                let mut p = head;
                loop {
                    let n = self.area(p).next_medium;
                    if n == a {
                        let an = self.area(a).next_medium;
                        self.area_mut(p).next_medium = an;
                        break;
                    }
                    p = n;
                    if n.is_none() {
                        break;
                    }
                }
            }
            self.area_mut(a).next_medium = EntIdx::NONE;
        }
    }

    /// World slot 84 `RemoveArea` (0x35041BE0): mark the area removed (`+80 = 2`), unlink it
    /// from the all-areas, big-area and per-medium lists, update the counters, signal event
    /// 12, detach it from the grid, move it to the deleted-areas list (freed by
    /// [`World::cleanup_deleted_areas`]) and invalidate the GetEntitiesInBox caches.
    pub fn remove_area(&mut self, a: EntIdx) {
        self.area_mut(a).deleted = 2;
        self.ent_mut(a).deleted = 2;
        if !self.global_area.is_none() {
            let mut p = self.global_area;
            loop {
                let n = self.area(p).next_all;
                if n == a {
                    let an = self.area(a).next_all;
                    self.area_mut(p).next_all = an;
                    break;
                }
                p = n;
                if n.is_none() {
                    break;
                }
            }
            let mut p = self.global_area;
            loop {
                let n = self.area(p).next_big;
                if n == a {
                    let an = self.area(a).next_big;
                    self.area_mut(p).next_big = an;
                    self.n_big_areas -= 1;
                    break;
                }
                p = n;
                if n.is_none() {
                    break;
                }
            }
        }
        self.unlink_medium(a);
        self.n_gravity_areas -= i32::from(self.area_counts_21080(a));
        self.n_gravity_areas_nonglobal -= i32::from(self.area_counts_21084(a));
        self.area_signal_change(a);
        self.grid_detach(a);
        self.n_areas -= 1;
        self.ent_count_by_type[9] -= 1;
        let head = self.deleted_areas;
        self.area_mut(a).next_all = head;
        self.deleted_areas = a;
        for c in self.box_cache.iter_mut() {
            c.objtypes = -1;
        }
    }

    /// The deleted-areas cleanup of `TimeStep` (inline at the end of the step, also
    /// `sub_35077520`): free every removed area whose refcount (`+524`) is 0 and whose lock
    /// word (`+64`) is below 0x20000; the others stay listed. **TimeStep must call this once
    /// per step** (after the entity cleanup, where the binary runs it).
    pub fn cleanup_deleted_areas(&mut self) {
        let mut prev = EntIdx::NONE;
        let mut a = self.deleted_areas;
        while !a.is_none() {
            let next = self.area(a).next_all;
            if self.area(a).refcount != 0 || self.ent(a).processed >= 0x20000 {
                prev = a;
            } else {
                if prev.is_none() {
                    self.deleted_areas = next;
                } else {
                    self.area_mut(prev).next_all = next;
                }
                self.ents[a.0 as usize] = None;
            }
            a = next;
        }
    }

    /// World slot 85 `GetNextArea(prev)` (0x350418A0): the global area for `None`, else the
    /// next non-deleted area after `prev` in the all-areas list (`None` at the end).
    #[must_use]
    pub fn get_next_area(&self, prev: Option<EntIdx>) -> Option<EntIdx> {
        match prev {
            None => (!self.global_area.is_none()).then_some(self.global_area),
            Some(p) => {
                let mut a = self.area(p).next_all;
                while !a.is_none() {
                    if self.area(a).deleted == 0 {
                        return Some(a);
                    }
                    a = self.area(a).next_all;
                }
                None
            }
        }
    }

    /// World slot 86 `IsAreaValid` (0x35041910): true only for an area that is in the
    /// per-medium list of its medium (an area without a medium is "invalid").
    #[must_use]
    pub fn is_area_valid(&self, a: EntIdx) -> bool {
        if !self.is_area(a) {
            return false;
        }
        let m = self.area(a).buoyancy.i_medium;
        if m == i32::MIN || m >= 16 {
            return false;
        }
        let mut p = self.medium_lists[m as usize];
        while !p.is_none() {
            if p == a {
                return true;
            }
            p = self.area(p).next_medium;
        }
        false
    }

    /// Area SetParams type 0 (`pe_params_pos`): position (`+120 = pos + offset0`), rotation
    /// (`R = R(q)·R0`), scale; the bbox is recomputed from the geometry, else the polygon,
    /// else the points (inflated by `+244`); then re-registered in the grid (event 12).
    /// Matrices (`pMtx3x4`/`pMtx3x3`) are not supported (pass null). Returns 1.
    pub fn area_set_params_pos(&mut self, a: EntIdx, p: &ParamsPos) -> i32 {
        self.area_signal_change(a);
        {
            let d = self.area_mut(a);
            if !unused(p.pos.x) {
                d.pos = Vec3::new(
                    p.pos.x + d.offset0.x,
                    d.offset0.y + p.pos.y,
                    d.offset0.z + p.pos.z,
                );
            }
            if !unused(p.q.w) {
                let m = Mat33::from_quat(p.q);
                d.r = mat_mul_22380(&m, &d.r0);
            }
            if !unused(p.scale) {
                d.scale = p.scale;
                d.inv_scale = 1.0 / p.scale;
            }
        }
        let d = self.area(a).clone();
        let mut bbox = self.ent(a).bbox;
        if let Some(g) = &d.geom {
            let bb = g.bbox();
            let (ext, _) = rotated_box_extent(&bb, &d.r, d.scale);
            let r = &d.r.m;
            let c = bb.center;
            let s = d.scale;
            let cz = (s * ((r[2][1] * c.y + r[2][0] * c.x) + r[2][2] * c.z)) + d.pos.z;
            let cx = (((r[0][1] * c.y) + (r[0][0] * c.x)) + (r[0][2] * c.z)) * s + d.pos.x;
            let cy = (s * ((r[1][1] * c.y + r[1][0] * c.x) + r[1][2] * c.z)) + d.pos.y;
            bbox = [
                Vec3::new(cx - ext.x, cy - ext.y, cz - ext.z),
                Vec3::new(cx + ext.x, cy + ext.y, cz + ext.z),
            ];
        } else if !d.points.is_empty() {
            let r = &d.r.m;
            let s = d.scale;
            let t = |p: Vec3| {
                Vec3::new(
                    (((p.x * r[0][0]) + (p.y * r[0][1])) + (p.z * r[0][2])) * s + d.pos.x,
                    s * (((p.x * r[1][0]) + (p.y * r[1][1])) + (p.z * r[1][2])) + d.pos.y,
                    s * (((p.x * r[2][0]) + (p.y * r[2][1])) + p.z * r[2][2]) + d.pos.z,
                )
            };
            let p0 = t(d.points[0]);
            let (mut lo, mut hi) = (p0, p0);
            for &p in d.points.iter().take(d.npoints.max(0) as usize) {
                let w = t(p);
                lo.x = ((lo.x + w.x) - (lo.x - w.x).abs()) * 0.5;
                lo.y = ((lo.y + w.y) - (lo.y - w.y).abs()) * 0.5;
                lo.z = ((lo.z + w.z) - (lo.z - w.z).abs()) * 0.5;
                let hy = ((hi.y - w.y).abs() + (hi.y + w.y)) * 0.5;
                hi.x = ((hi.x - w.x).abs() + (hi.x + w.x)) * 0.5;
                hi.y = hy;
                hi.z = ((hi.z - w.z).abs() + (hi.z + w.z)) * 0.5;
            }
            let r = d.radius;
            bbox = [
                Vec3::new(lo.x - r, lo.y - r, lo.z - r),
                Vec3::new(r + hi.x, r + hi.y, r + hi.z),
            ];
        }
        self.ent_mut(a).bbox = bbox;
        self.area_register_grid(a);
        // `sub_35023200`'s tail: a pos/q change marks the lock word (`+64 |= 0x10000`)
        if (!unused(p.pos.x) || !p.mtx3x4.is_null()) && p.recalc_bounds & 0x10 == 0 {
            self.ent_mut(a).processed |= 0x10000;
        }
        1
    }

    /// Update the `+21080` / `+21084` counters around a parameter change.
    fn area_counters_remove(&mut self, a: EntIdx) {
        self.n_gravity_areas -= i32::from(self.area_counts_21080(a));
        self.n_gravity_areas_nonglobal -= i32::from(self.area_counts_21084(a));
    }

    fn area_counters_add(&mut self, a: EntIdx) {
        self.n_gravity_areas += i32::from(self.area_counts_21080(a));
        self.n_gravity_areas_nonglobal += i32::from(self.area_counts_21084(a));
    }

    /// Set the area gravity (and the world gravity when `a` is the global area).
    fn area_set_gravity(&mut self, a: EntIdx, g: Vec3) {
        self.area_mut(a).gravity = g;
        if a == self.global_area {
            self.vars.set_gravity(g);
        }
    }

    /// Area SetParams type 10 (`pe_params_simulation`): `+16` damping → `+516`, `+20`
    /// gravity → `+468` (and the world gravity for the global area). Event 12. Returns 1.
    pub fn area_set_params_simulation(&mut self, a: EntIdx, damping: f32, gravity: Vec3) -> i32 {
        self.area_counters_remove(a);
        if !unused(gravity.x) {
            self.area_set_gravity(a, gravity);
        }
        if !unused(damping) {
            self.area_mut(a).damping = damping;
        }
        self.area_counters_add(a);
        self.area_signal_change(a);
        1
    }

    /// Area SetParams type 18 (`pe_params_area`). `geometry`: `None` keeps the current one
    /// (`-1`), `Some(None)` clears it. Event 12. Returns 1.
    pub fn area_set_params_area(
        &mut self,
        a: EntIdx,
        p: &ParamsArea,
        geometry: Option<Option<Arc<dyn Geometry>>>,
    ) -> i32 {
        self.area_counters_remove(a);
        if !unused(p.gravity.x) {
            self.area_set_gravity(a, p.gravity);
        }
        let d = self.area_mut(a);
        if !unused(p.size.x) {
            d.size = p.size;
            let inv = |x: f32| if x <= 0.0 { 0.0 } else { 1.0 / x };
            let iz = inv(d.size.z);
            let iy = inv(d.size.y);
            let ix = inv(d.size.x);
            d.inv_size = Vec3::new(ix, iy, iz);
        }
        if p.uniform != i32::MIN {
            d.uniform = p.uniform;
        }
        if !unused(p.falloff0) {
            d.falloff0 = p.falloff0;
            d.inv_falloff = if p.falloff0 >= 1.0 {
                0.0
            } else {
                1.0 / (1.0 - p.falloff0)
            };
        }
        if !unused(p.damping) {
            d.damping = p.damping;
        }
        if p.use_callback != i32::MIN {
            d.use_callback = p.use_callback;
        }
        if let Some(g) = geometry {
            d.geom = g;
        }
        if !unused(p.center.x) {
            d.center = p.center;
        }
        if !unused(p.radius) {
            d.circle_radius = p.radius;
            if p.radius <= 0.0 {
                d.circle.clear();
            } else {
                // 12 points at 30° steps (`sinf`/`cosf` of MSVCR100), z = the plane origin z
                let mut ang = 0.0f32;
                d.circle.clear();
                for _ in 0..12 {
                    let x = crate::math::crt::sinf(ang) * d.circle_radius + d.center.x;
                    let c = crate::math::crt::cosf(ang);
                    ang += f32::from_bits(0x3F06_0A92); // 0.52359879
                    let y = c * d.circle_radius + d.center.y;
                    d.circle
                        .push(Vec3::new(x, y, d.buoyancy.water_plane_origin.z));
                }
            }
        }
        self.area_counters_add(a);
        self.area_signal_change(a);
        1
    }

    /// Area SetParams type 12 (`pe_params_buoyancy`): the first water plane initialises the
    /// record (density 1000, damping 0, resistance 1000, flow 0, n = (0,0,1), origin 0);
    /// `iMedium` moves the area between the per-medium lists; then density, damping, plane,
    /// flow, resistance. On the global area it refreshes `world.water_objtype` (`+20844`):
    /// 0x200 when the water material (`+20840`) is >= 0 and a plane is set, else 0. Event 12.
    /// Returns 1.
    pub fn area_set_params_buoyancy(&mut self, a: EntIdx, p: &ParamsBuoyancy) -> i32 {
        self.n_gravity_areas -= i32::from(self.area_counts_21080(a));
        {
            let d = self.area_mut(a);
            if !unused(p.water_plane_origin.x) && unused(d.buoyancy.water_plane_origin.x) {
                let b = &mut d.buoyancy;
                b.water_density = 1000.0;
                b.water_damping = 0.0;
                b.water_plane_n = Vec3::new(0.0, 0.0, 1.0);
                b.water_plane_origin = Vec3::ZERO;
                b.water_flow = Vec3::ZERO;
                b.water_resistance = 1000.0;
            }
        }
        if p.i_medium != i32::MIN {
            self.unlink_medium(a);
            self.area_mut(a).buoyancy.i_medium = p.i_medium;
            if (p.i_medium as u32) < 16 {
                let m = p.i_medium as usize;
                let head = self.medium_lists[m];
                self.area_mut(a).next_medium = head;
                self.medium_lists[m] = a;
            }
        }
        {
            let b = &mut self.area_mut(a).buoyancy;
            if !unused(p.water_density) {
                b.water_density = p.water_density;
            }
            if !unused(p.water_damping) {
                b.water_damping = p.water_damping;
            }
            if !unused(p.water_plane_n.x) {
                b.water_plane_n = p.water_plane_n;
            }
            if !unused(p.water_plane_origin.x) {
                b.water_plane_origin = p.water_plane_origin;
            }
            if !unused(p.water_flow.x) {
                b.water_flow = p.water_flow;
            }
            if !unused(p.water_resistance) {
                b.water_resistance = p.water_resistance;
            }
        }
        if a == self.global_area {
            let plane = !unused(self.area(a).buoyancy.water_plane_origin.x);
            self.water_objtype = if self.water_mat >= 0 && plane {
                0x200
            } else {
                0
            };
        }
        self.n_gravity_areas += i32::from(self.area_counts_21080(a));
        self.area_signal_change(a);
        1
    }

    /// Area GetParams type 10: `(damping +16, gravity +20)`.
    #[must_use]
    pub fn area_get_params_simulation(&self, a: EntIdx) -> (f32, Vec3) {
        let d = self.area(a);
        (d.damping, d.gravity)
    }

    /// Area GetParams type 18 (`geometry` is returned separately).
    #[must_use]
    pub fn area_get_params_area(&self, a: EntIdx) -> (ParamsArea, Option<Arc<dyn Geometry>>) {
        let d = self.area(a);
        let p = ParamsArea {
            gravity: d.gravity,
            size: d.size,
            falloff0: d.falloff0,
            uniform: d.uniform,
            use_callback: d.use_callback,
            damping: d.damping,
            center: d.center,
            radius: d.circle_radius,
            ..ParamsArea::default()
        };
        (p, d.geom.clone())
    }

    /// Area GetParams type 12: the record (80 bytes from `+388`).
    #[must_use]
    pub fn area_get_params_buoyancy(&self, a: EntIdx) -> ParamsBuoyancy {
        self.area(a).buoyancy
    }

    /// Area GetStatus type 1 (`pe_status_pos`) (0x3504EE60): `(pos, bbox relative to pos,
    /// q of R, scale, simclass)`. Only the fields the zone server reads are returned.
    #[must_use]
    pub fn area_get_status_pos(&self, a: EntIdx) -> (Vec3, [Vec3; 2], f32) {
        let d = self.area(a);
        let b = self.ent(a).bbox;
        (
            d.pos,
            [
                Vec3::new(b[0].x - d.pos.x, b[0].y - d.pos.y, b[0].z - d.pos.z),
                Vec3::new(b[1].x - d.pos.x, b[1].y - d.pos.y, b[1].z - d.pos.z),
            ],
            d.scale,
        )
    }

    /// Area refcount (`+524`, area slots 2/3 AddRef/Release).
    pub fn area_add_ref(&mut self, a: EntIdx, delta: i32) -> i32 {
        let d = self.area_mut(a);
        d.refcount += delta;
        d.refcount
    }
}

/// `sub_35022380(out, a, b)`: `out[i][j] = (b[0][j]·a[i][0] + b[1][j]·a[i][1]) + b[2][j]·a[i][2]`
/// (the area's `R = R(q)·R0`).
fn mat_mul_22380(a: &Mat33, b: &Mat33) -> Mat33 {
    let (a, b) = (&a.m, &b.m);
    let mut o = Mat33::ZERO;
    for i in 0..3 {
        for j in 0..3 {
            o.m[i][j] = (b[0][j] * a[i][0] + b[1][j] * a[i][1]) + b[2][j] * a[i][2];
        }
    }
    o
}

/// The world-frame half extents of a geometry box under the area rotation (slot 82 /
/// SetParams type 0): `B' = Basis·Rᵀ` (`sub_35020320`), `ext_j = Σ_i size_i·|B'[i][j]|·scale`
/// with the binary's summation order `(size.y·|B'[1][j]| + size.x·|B'[0][j]|) + size.z·|B'[2][j]|`.
fn rotated_box_extent(
    bb: &crate::geometry::primitives::BoxPrim,
    r: &Mat33,
    scale: f32,
) -> (Vec3, Mat33) {
    let rt = r.transpose();
    let a = &bb.basis.m;
    let b = &rt.m;
    let mut m = Mat33::ZERO;
    for i in 0..3 {
        for j in 0..3 {
            m.m[i][j] = ((a[i][1] * b[1][j]) + (a[i][0] * b[0][j])) + (a[i][2] * b[2][j]);
        }
    }
    let s = bb.size;
    let ab = |x: f32| x.abs();
    let mm = &m.m;
    let ex = (((s.y * ab(mm[1][0])) + (s.x * ab(mm[0][0]))) + (s.z * ab(mm[2][0]))) * scale;
    let ey = (((s.y * ab(mm[1][1])) + (ab(mm[0][1]) * s.x)) + (s.z * ab(mm[2][1]))) * scale;
    let ez = (((s.y * ab(mm[1][2])) + (ab(mm[0][2]) * s.x)) + (s.z * ab(mm[2][2]))) * scale;
    (Vec3::new(ex, ey, ez), m)
}
