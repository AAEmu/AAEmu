//! **P3 — areas** (`CPhysArea`, 0x228 bytes, GetType 9; vtable 0x351EF598; ctor 0x35042150).
//! `world.md` §7.
//!
//! An area is stored in the world's object store like an entity ([`EntityKind::Area`]); the
//! placeholder header (bbox, grid cells, simclass 5) is the [`Entity`] part, the area members
//! are [`AreaData`]. The area lists use their own links (`+88` all areas, `+96` big areas,
//! `+104` per-medium list).

use super::{EntIdx, World};
use crate::entities::base::{Entity, EntityKind};
use crate::math::{Mat33, Vec3};
use crate::params::ParamsBuoyancy;
use crate::sentinel::UNUSED_F32;

/// `CPhysArea` members (offsets `A+x`).
#[derive(Clone, Debug)]
pub struct AreaData {
    /// `+80` deleted state (0 active, 2 removed).
    pub deleted: i32,
    /// `+88` next area (all-areas list rooted at the global area), `+96` next "big" area,
    /// `+104` next in the per-medium list.
    pub next_all: EntIdx,
    pub next_big: EntIdx,
    pub next_medium: EntIdx,
    /// `+120` position, `+132` rotation, `+168` scale, `+172` 1/scale.
    pub pos: Vec3,
    pub r: Mat33,
    pub scale: f32,
    pub inv_scale: f32,
    /// `+176` secondary offset, `+188` base rotation.
    pub offset0: Vec3,
    pub r0: Mat33,
    /// `+224` geometry (geometry areas).
    pub geom: Option<std::sync::Arc<dyn crate::geometry::Geometry>>,
    /// `+240` number of points, `+244` spline tube radius / polygon thickness.
    pub npoints: i32,
    pub radius: f32,
    /// `+304` points (spline / polygon).
    pub points: Vec<Vec3>,
    /// `+312` (1e10 = unset).
    pub v312: Vec3,
    /// `+324`.
    pub unk324: i32,
    /// `+388 … +467` the buoyancy record (params type 12, copied verbatim).
    pub buoyancy: ParamsBuoyancy,
    /// `+468` gravity (unused = no gravity).
    pub gravity: Vec3,
    /// `+480` falloff size, `+492` inverse.
    pub size: Vec3,
    pub inv_size: Vec3,
    /// `+504` uniform mode (1), `+508` falloff0, `+512` 1/(1−falloff0), `+516` damping.
    pub uniform: i32,
    pub falloff0: f32,
    pub inv_falloff: f32,
    pub damping: f32,
    /// `+520` bUseCallback.
    pub use_callback: i32,
    /// `+524` refcount.
    pub refcount: i32,
    /// `+528` centre, `+540` radius (-1).
    pub center: Vec3,
    pub circle_radius: f32,
}

impl AreaData {
    /// The ctor 0x35042150.
    #[must_use]
    pub fn new() -> Self {
        // The ctor writes the unused sentinel into every scalar and the first component of
        // every vector of the record; `ParamsBuoyancy::default()` is all-unused.
        let b = ParamsBuoyancy::default();
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
            v312: Vec3::splat(1e10),
            unk324: 0,
            buoyancy: b,
            gravity: Vec3::new(UNUSED_F32, 0.0, 0.0),
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
        }
    }
}

impl Default for AreaData {
    fn default() -> Self {
        Self::new()
    }
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

    /// Next area in the all-areas list (`+88`).
    #[must_use]
    pub fn area_next(&self, a: EntIdx) -> EntIdx {
        self.area(a).next_all
    }

    /// World slot 80 `AddGlobalArea` (0x3504A1A0): create the global area once (bbox
    /// ±1e10, gravity = world gravity; not in the grid) and signal event 12.
    pub fn add_global_area(&mut self) -> EntIdx {
        if self.global_area.is_none() {
            let mut d = AreaData::new();
            d.gravity = self.gravity();
            let mut ent = Entity::new_base(9, EntityKind::Area(Box::new(d)));
            // placeholder ctor: id bits cleared, simclass 5, cells -2
            ent.id_bits = 0x0500_0000;
            ent.grid_cells = [-2, -2, -2, -2];
            ent.i_foreign_flags = 0;
            ent.bbox = [Vec3::splat(-1e10), Vec3::splat(1e10)];
            let a = self.store(ent);
            self.global_area = a;
            let bbox = self.ent(a).bbox;
            self.signal_event(u32::MAX, super::events::Event::AreaChange { area: a, bbox });
        }
        self.global_area
    }
}

impl World {
    /// World slot 87 `CheckAreas(pt, gravity*, buoy[] out, nMaxBuoys, iMedium, vel, ent, r,
    /// iCaller, areas[] out, nMaxAreas)` (0x3504E000). Returns the number of buoyancy
    /// records written.
    ///
    /// Current coverage: the global area (gravity and its buoyancy record when the water
    /// plane is set and the entity does not ignore the global area, flag 0x20000).
    #[allow(clippy::too_many_arguments)]
    pub fn check_areas(
        &mut self,
        _pt: Vec3,
        gravity: &mut Vec3,
        buoy: &mut Vec<ParamsBuoyancy>,
        nmax: usize,
        _imedium: i32,
        _vel: Vec3,
        ent: Option<EntIdx>,
        _r: f32,
        _caller: i32,
    ) -> usize {
        *gravity = Vec3::new(UNUSED_F32, gravity.y, gravity.z);
        if self.global_area.is_none() {
            return 0;
        }
        let a = self.area(self.global_area).clone();
        if !crate::sentinel::is_unused(a.gravity.x) {
            *gravity = a.gravity;
        }
        let ignore = ent.is_some_and(|e| self.ent(e).flags & 0x2_0000 != 0);
        if !crate::sentinel::is_unused(a.buoyancy.water_plane_n.x) && !ignore && buoy.len() < nmax {
            buoy.push(a.buoyancy);
        }
        buoy.len()
    }
}
