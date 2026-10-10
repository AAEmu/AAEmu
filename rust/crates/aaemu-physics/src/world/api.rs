//! A small convenience layer over the ported world interface for the server: create a world
//! with an entity grid, add static and rigid entities from primitive geometries, terrain,
//! an ocean (the global area as a water plane), step, read body states, ray and primitive
//! queries, events. Everything here forwards to the ported slots; it adds no behaviour.
//!
//! | operation | ported function |
//! |---|---|
//! | world + grid | `CreatePhysicalWorld` ([`World::new`]), `SetupEntityGrid` 0x3507E5A0 |
//! | geometry | `CGeomManager::CreatePrimitive` / `RegisterGeometry` 0x3514AE40 |
//! | entity | `CreatePhysicalEntity` 0x35082F00, `AddGeometry` (base 0x35055FF0, rigid 0x350C41C0) |
//! | terrain | `SetHeightfieldData` (world slot 17) |
//! | ocean | `AddGlobalArea` 0x3504A1A0 + area `SetParams(pe_params_buoyancy)` 0x35045B50 |
//! | step | `TimeStep` 0x3508BD10 |
//! | rays / primitives | `RayWorldIntersection`, `PrimitiveWorldIntersection` |
//! | events | `AddEventClient` 0x35077AE0, `PumpLoggedEvents` 0x35094FA0 |

use super::{EntIdx, World};
use crate::geometry::primitives::Primitive;
use crate::geometry::PhysGeometry;
use crate::math::{Quat, Vec3};
use crate::params::{GeomParams, ParamsBuoyancy, ParamsPos};
use std::sync::Arc;

/// One part of an entity.
#[derive(Clone, Debug)]
pub struct PartDesc {
    pub geom: Arc<PhysGeometry>,
    /// Mass (rigid parts; 0 for static entities). Ignored when `density > 0`.
    pub mass: f32,
    pub density: f32,
    pub pos: Vec3,
    pub q: Quat,
    pub scale: f32,
    /// Part flags (`geom_flags`), default 0x1809F.
    pub flags: u32,
    /// Collider flags, default 1.
    pub flags_collider: u32,
}

impl PartDesc {
    /// A part with the oracle's defaults (flags 0x1809F, collider 1, identity placement).
    #[must_use]
    pub fn new(geom: Arc<PhysGeometry>, mass: f32) -> Self {
        Self {
            geom,
            mass,
            density: 0.0,
            pos: Vec3::ZERO,
            q: Quat::IDENTITY,
            scale: 1.0,
            flags: 0x1809F,
            flags_collider: 1,
        }
    }
}

/// The state a server needs per body (`pe_status_pos` / `pe_status_dynamics` / awake).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct BodyState {
    pub pos: Vec3,
    pub q: Quat,
    /// Zero while asleep (as `GetStatus(pe_status_dynamics)` reports it).
    pub v: Vec3,
    pub w: Vec3,
    pub awake: bool,
    pub sim_class: i32,
}

/// The TimeStep flags of the zone server and the oracle (rigid, living, independent,
/// cleanup, …).
pub const DEFAULT_STEP_FLAGS: i32 = 0x9F;

impl World {
    /// A world with an entity grid of `n × n` cells of `cell` metres starting at `origin`
    /// (z up).
    #[must_use]
    pub fn with_grid(origin: Vec3, n: i32, cell: f32) -> World {
        let mut w = World::new();
        w.setup_entity_grid(2, origin, n, n, cell, cell, 0);
        w
    }

    /// Register a primitive geometry (box, sphere, cylinder, capsule, heightfield …).
    pub fn geometry(&mut self, prim: &Primitive) -> Option<Arc<PhysGeometry>> {
        self.register_primitive(prim)
    }

    fn add_entity(&mut self, etype: i32, pos: Vec3, q: Quat, parts: &[PartDesc], id: i32) -> Option<EntIdx> {
        let pp = ParamsPos {
            pos,
            q,
            ..ParamsPos::default()
        };
        let e = self.create_physical_entity(etype, 0.0, Some(&pp), 0, 0, id)?;
        for p in parts {
            let gp = GeomParams {
                mass: p.mass,
                density: p.density,
                pos: p.pos,
                q: p.q,
                scale: p.scale,
                flags: p.flags,
                flags_collider: p.flags_collider,
                ..GeomParams::default()
            };
            crate::entities::base::add_geometry(self, e, &p.geom, &gp, -1);
        }
        Some(e)
    }

    /// Add a static entity (type 1) with the given parts; `id` -1 = automatic.
    pub fn add_static(&mut self, pos: Vec3, q: Quat, parts: &[PartDesc], id: i32) -> Option<EntIdx> {
        self.add_entity(1, pos, q, parts, id)
    }

    /// Add a rigid body (type 2) with the given parts; `id` -1 = automatic.
    pub fn add_rigid(&mut self, pos: Vec3, q: Quat, parts: &[PartDesc], id: i32) -> Option<EntIdx> {
        self.add_entity(2, pos, q, parts, id)
    }

    /// Make the global area a water plane at height `level` with the given density and
    /// resistance (the oracle's floating scenarios use 1000 / 1000); returns the area.
    pub fn set_ocean(&mut self, level: f32, density: f32, resistance: f32) -> EntIdx {
        let a = if self.global_area.is_none() {
            self.add_global_area()
        } else {
            self.global_area
        };
        let p = ParamsBuoyancy {
            water_density: density,
            water_resistance: resistance,
            water_flow: Vec3::ZERO,
            water_plane_n: Vec3::new(0.0, 0.0, 1.0),
            water_plane_origin: Vec3::new(0.0, 0.0, level),
            i_medium: 0,
            ..ParamsBuoyancy::default()
        };
        self.area_set_params_buoyancy(a, &p);
        a
    }

    /// A rigid body's buoyancy response (`SetParams(pe_params_buoyancy)` on the entity: the
    /// density and resistance it floats with, relative to the ocean's).
    pub fn set_body_buoyancy(&mut self, e: EntIdx, density: f32, resistance: f32) -> i32 {
        let p = ParamsBuoyancy {
            water_density: density,
            water_resistance: resistance,
            ..ParamsBuoyancy::default()
        };
        crate::entities::rigid::buoyancy::set_params_buoyancy(self, e, &p)
    }

    /// One server tick: `TimeStep(dt, 0x9F)` followed by `PumpLoggedEvents`.
    pub fn step(&mut self, dt: f32) {
        self.time_step(dt, DEFAULT_STEP_FLAGS);
        self.pump_logged_events();
    }

    /// Position, orientation, velocities and awake state of an entity.
    #[must_use]
    pub fn body_state(&self, e: EntIdx) -> BodyState {
        let ent = self.ent(e);
        let (v, w, awake) = match ent.rigid() {
            Some(r) if r.awake() => {
                let b = self.bodies.get(r.body);
                (b.v, b.w, true)
            }
            Some(_) => (Vec3::ZERO, Vec3::ZERO, false),
            None => (Vec3::ZERO, Vec3::ZERO, false),
        };
        BodyState {
            pos: ent.pos,
            q: ent.q,
            v,
            w,
            awake,
            sim_class: ent.sim_class(),
        }
    }
}
