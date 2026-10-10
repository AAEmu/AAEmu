//! Ray and primitive queries on a locked physics world: `RayWorldIntersection`
//! (IPhysicalWorld vt+336 → cryphysics 0x351AA730) and `PrimitiveWorldIntersection`
//! (vt+512 → 0x350964F0), with the object-type masks and ray flags the zone's game code uses
//! (`ZONE-PHYSICS-USAGE.md` §2.6, `sub_x2game.md` §5).

use aaemu_physics::geometry::primitives::{CapsulePrim, Primitive, SpherePrim};
use aaemu_physics::math::Vec3;
use aaemu_physics::world::queries::{PwiParams, RayHit, RwiParams};
use aaemu_physics::world::EntIdx;

use super::world::{Owner, WorldState};

/// Object-type masks (`ent_*`) of the zone's queries.
pub mod mask {
    /// static + terrain: vehicle slope `sub_39248100`, wheels `sub_39283780`, transfer path
    /// `sub_3973EF70`.
    pub const STATIC_TERRAIN: u32 = 0x101;
    /// static + sleeping + rigid + terrain: unmount check `sub_3972C690`, building
    /// `sub_398351E0`.
    pub const SOLID_TERRAIN: u32 = 0x107;
    /// all entities + terrain: tornado `sub_3920FE10`, surface id `sub_394B6660`.
    pub const ALL_TERRAIN: u32 = 0x11F;
    /// all + terrain + water: `WZRayCasting` `sub_3936F340`.
    pub const ALL_TERRAIN_WATER: u32 = 0x31F;
    /// static + terrain + water: glide start `sub_392E36B0`.
    pub const STATIC_TERRAIN_WATER: u32 = 0x301;
    /// static + sleeping + rigid + living (`WZBlinkUnit` capsule sweep, entTypes 15).
    pub const ENTITIES_15: u32 = 0xF;
    pub const TERRAIN: u32 = 0x100;
    pub const WATER: u32 = 0x200;
}

/// Ray flags.
pub mod ray_flags {
    /// `rwi_stop_at_pierceable | rwi_ignore_back_faces` (0x8F): most x2game rays.
    pub const DEFAULT: u32 = 0x8F;
    /// `WZRayCasting` (0xFFFE048F: colltype-any with every class but 0).
    pub const WZ_RAYCASTING: u32 = 0xFFFE_048F;
}

/// A ray hit.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Hit {
    pub pos: Vec3,
    pub normal: Vec3,
    /// Distance from the ray origin.
    pub dist: f32,
    /// What was hit (`None` for a water-plane hit).
    pub owner: Option<Owner>,
    /// Hit the water surface.
    pub water: bool,
    /// Mapped surface index (-1 without a material mapping).
    pub surface: i16,
    /// The physical entity.
    pub ent: EntIdx,
}

/// A sweep hit.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct SweepHit {
    /// Distance travelled along the sweep before the contact.
    pub dist: f32,
    pub point: Vec3,
    pub normal: Vec3,
}

impl WorldState {
    /// `RayWorldIntersection` from `from` to `to` (caller 4, one hit slot: the closest solid
    /// hit).
    pub fn raycast(
        &mut self,
        from: Vec3,
        to: Vec3,
        objtypes: u32,
        flags: u32,
        skip: &[EntIdx],
    ) -> Option<Hit> {
        let dir = to - from;
        if dir.len2() <= 0.0 {
            return None;
        }
        let p = RwiParams {
            org: from,
            dir,
            objtypes,
            flags,
            skip: skip.to_vec(),
            ..RwiParams::default()
        };
        let mut hits = [RayHit::default()];
        let n = self.world.rwi(&p, &mut hits, None, 4);
        let h = hits[0];
        if n <= 0 || h.dist < 0.0 || h.dist >= 1e9 {
            return None;
        }
        // water-plane hits carry the global area (or no entity)
        let water = h.collider.is_none() || self.world.is_area(h.collider);
        Some(Hit {
            pos: h.pt,
            normal: h.n,
            dist: h.dist,
            owner: if water { None } else { self.owner(h.collider) },
            water,
            surface: h.surface_idx,
            ent: h.collider,
        })
    }

    /// `PrimitiveWorldIntersection` sweep of `prim` along `dir` (the length of `dir` is the
    /// sweep length). `None` = no contact on the way.
    pub fn sweep(
        &mut self,
        prim: Primitive,
        dir: Vec3,
        ent_types: u32,
        geom_flags_any: u32,
        skip: &[EntIdx],
    ) -> Option<SweepHit> {
        let p = PwiParams {
            prim,
            sweep_dir: dir,
            ent_types,
            geom_flags_any,
            skip: skip.to_vec(),
            ..PwiParams::default()
        };
        let r = self.world.pwi(&p);
        let c = r.contacts.first()?;
        if r.dist <= 0.0 && c.t <= 0.0 {
            // contact at the start position
            return Some(SweepHit {
                dist: 0.0,
                point: c.pt,
                normal: c.n,
            });
        }
        Some(SweepHit {
            dist: r.dist,
            point: c.pt,
            normal: c.n,
        })
    }

    /// Static overlap test of `prim` (zero sweep: stops at the first intersecting part).
    pub fn overlaps(
        &mut self,
        prim: Primitive,
        ent_types: u32,
        geom_flags_any: u32,
        skip: &[EntIdx],
    ) -> bool {
        let p = PwiParams {
            prim,
            sweep_dir: Vec3::ZERO,
            ent_types,
            geom_flags_any,
            skip: skip.to_vec(),
            ..PwiParams::default()
        };
        self.world.pwi(&p).dist > 0.0
    }
}

/// A vertical capsule (`r`, half height of the cylindrical part `hh`) centred at `c`.
pub fn capsule(c: Vec3, r: f32, hh: f32) -> Primitive {
    Primitive::Capsule(CapsulePrim {
        center: c,
        axis: Vec3::new(0.0, 0.0, 1.0),
        r,
        hh,
    })
}

/// A sphere.
pub fn sphere(c: Vec3, r: f32) -> Primitive {
    Primitive::Sphere(SpherePrim { center: c, r })
}
