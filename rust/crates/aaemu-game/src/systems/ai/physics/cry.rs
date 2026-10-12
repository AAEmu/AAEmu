//! Ships on the CryPhysics port (`Physics.CryPhysicsShips`, default on): each hull is a rigid
//! entity in the instance's collision world (`systems::ai::collision`), floating in the ocean
//! global area, colliding with terrain, brushes, vegetation, `collide_ship` doodads and other
//! hulls through CryPhysics' own contact solver. The ship controller of the C# port (helm,
//! wind, storms, whirlpools, harpoon tow, replication) keeps driving the hull velocities; the
//! Jitter2 integrator, the C# buoyancy force and the manual depenetration passes (shore,
//! ship–ship, doodads, barriers, steep terrain) are replaced by the engine.
//!
//! **Entity** (`X2::ShipUnitModel::InitPhysicalEntity`, x2game `sub_39286C50`), from the
//! `ship_models` row (the 10.x ship desc has the same fields; offsets noted):
//!
//! | part | geometry | flags / collider | mass |
//! |---|---|---|---|
//! | buoys | prefab brushes named `buoy*`, using their CGF physics geometry and local position, rotation and scale; otherwise a cylinder along the ship's Y at `(0, 0, tube_offset_z)`, `r = tube_radius`, `hh = tube_length` (desc +204/+208/+200; 0 → half beam / half length) | `0x10000` (floats) / 0 | 0 |
//! | keel | box at `(0, 0, keel_offset_z − keel_height)` (desc +100/+104/+108) when the keel has a size | `0x10000` / 0 | 0 |
//! | mass | box `mass_box_size / 2` at `mass_center` | 0 / 0 | `mass` (desc +112) |
//! | hull | transformed authored model collision-proxy bounds; mass-box fallback when assets are unavailable | `0x80` / `0x80` (ship collision class) | 1 |
//! | bottom | box below the hull: `x,y = max(h·0.9, h − 3)`, `z = max(hz·0.2, 0.4/2)` (`g_unit_collide_bottom_box_*`) | `0x100` / `0x100` | 0.01 |
//!
//! then `pe_params_buoyancy` (`kWaterDensity = water_density`, `waterDamping = water_damping`,
//! `kWaterResistance = water_resistance`), `pe_params_simulation` (`maxTimeStep 0.02`,
//! `minEnergy 0.16`, `damping 0.1`, `maxLoggedCollisions 2`), `pe_params_flags` (`flagsAND = ~0x200`) and
//! `pef_log_collisions` (0x2000000) for the collision events.
//!
//! **Collision damage** (`ZWUnitCollision`, zone manager 0x390AEC80, event handler 0x390AF3C0,
//! resolver 0x390AF030): closing speed `v = −dot(v0 − v1, n)` of every collision event of a
//! hull; contacts at or below the minimum are dropped; the maximum per pair (ship, ship / ship,
//! environment) over 500 ms (`g_unit_collide_report_interval`) is clamped to the maximum and
//! turned into damage by the world side. The expression constants (12, 13, 21) and the 10.x
//! `physics_collision_*_damage_mul` columns are not in the 1.2 data: the minimum / maximum are
//! the C# port's `HULL_DAMAGE_LOW_SPEED_THRESHOLD_MPS` / `HULL_DAMAGE_SPEED_INTERP_MAX_MPS` and
//! the damage is its speed-scaled hull damage (nose-cone rule for ship pairs).
//!
//! Axes: the C# ship code works in Jitter axes (y up); the physics world is in game axes
//! (z up). Positions / velocities swap y and z; orientations use the C# mapping
//! `(x, z, y, w)` (`PhysicsManager.SyncTransformWithRigidBody`).

use std::collections::HashMap;
use std::sync::{Arc, Mutex};

use aaemu_physics::entities::base::add_geometry;
use aaemu_physics::entities::interface::action::set_velocity;
use aaemu_physics::entities::interface::set::{set_params_flags, set_params_simulation};
use aaemu_physics::geometry::primitives::{BoxPrim, CylinderPrim, Primitive};
use aaemu_physics::math::{Quat as PQuat, Vec3 as PVec3};
use aaemu_physics::params::{
    ActionSetVelocity, GeomParams, ParamsBuoyancy, ParamsFlags, ParamsPos, ParamsSimulation,
};
use aaemu_physics::world::events::Event;
use aaemu_physics::world::EntIdx;

use crate::systems::ai::collision::world::{ObjKind, Owner, WorldState};
use crate::systems::ai::collision::{
    models::{phys_slot, ModelCache},
    objects::{self, ModelRef, ShapePart},
    Files,
};
use crate::systems::ai::data::ShipModel;
use crate::systems::skills::controllers::Vec3;

use super::contacts::{pair, speed_scaled_hull_damage_percent, HullDamage};
use super::ship::PhysShip;

/// `g_unit_collide_report_interval` (ms).
pub const COLLIDE_REPORT_INTERVAL_MS: i64 = 500;
/// `pef_log_collisions`.
pub const PEF_LOG_COLLISIONS: i32 = 0x200_0000;

/// Jitter (y up) → game / physics (z up).
pub fn to_phys(v: Vec3) -> PVec3 {
    PVec3::new(v.x, v.z, v.y)
}

/// Game / physics → Jitter.
pub fn from_phys(v: PVec3) -> Vec3 {
    Vec3::new(v.x, v.z, v.y)
}

/// The C# orientation mapping `(x, z, y, w)` (an involution).
pub fn quat_to_phys(q: crate::systems::ai::numerics::Quat) -> PQuat {
    PQuat::new(q.x, q.z, q.y, q.w)
}

pub fn quat_from_phys(q: PQuat) -> crate::systems::ai::numerics::Quat {
    crate::systems::ai::numerics::Quat {
        x: q.x,
        y: q.z,
        z: q.y,
        w: q.w,
    }
}

/// One part of the ship entity.
#[derive(Debug, Clone)]
pub struct ShipPart {
    pub prim: Primitive,
    pub flags: u32,
    pub collider: u32,
    pub mass: f32,
}

fn boxp(center: PVec3, half: PVec3) -> Primitive {
    Primitive::Box(BoxPrim::aligned(center, half))
}

/// `ShipUnitModel::InitPhysicalEntity` prefers prefab brushes whose names start with
/// `buoy` (10.x zone 0x39286C50, also present in the 1.2 speedboat prefab). The cylinder
/// is only the fallback when none of these authored physics parts exist.
pub fn prefab_buoys(files: &Files, models: &ModelCache, reference: &str) -> Vec<ShapePart> {
    let ModelRef::Prefab { library, name } = objects::parse_model_ref(reference) else {
        return Vec::new();
    };
    let Some(bytes) = files.read(&library) else {
        return Vec::new();
    };
    let xml = crate::data::client_files::decode_text(&bytes);
    objects::prefab_brushes_with_prefix(&xml, &name, "buoy")
        .into_iter()
        .filter_map(|(path, pos, q, scale)| {
            let model = models.get(&path)?;
            model.slot(phys_slot::DEFAULT)?;
            Some(ShapePart {
                model,
                pos,
                q,
                scale,
            })
        })
        .collect()
}

/// Model bounds for the solid hull, separate from its inertia box (native0x39286C50).
/// The native entity also has render/non-brush bounds not exposed by our model loader;
/// use the authored DEFAULT physics proxies transformed into the prefab frame. This
/// documented proxy adaptation avoids inventing dimensions when assets are available.
pub fn model_collision_bounds(
    files: &Files,
    models: &ModelCache,
    reference: &str,
) -> Option<BoxPrim> {
    let mut xml = HashMap::new();
    let shape = objects::resolve_shape(
        files,
        models,
        &mut xml,
        &objects::parse_model_ref(reference),
    )?;
    let mut min = PVec3::new(f32::INFINITY, f32::INFINITY, f32::INFINITY);
    let mut max = PVec3::new(f32::NEG_INFINITY, f32::NEG_INFINITY, f32::NEG_INFINITY);
    let mut found = false;
    for part in shape.parts {
        let Some(geometry) = part.model.slot(phys_slot::DEFAULT) else {
            continue;
        };
        let bounds = geometry.geom.bbox();
        if !part.scale.is_finite() || part.scale <= 0.0 {
            return None;
        }
        for corner in 0..8 {
            let local = PVec3::new(
                if corner & 1 == 0 {
                    -bounds.size.x
                } else {
                    bounds.size.x
                },
                if corner & 2 == 0 {
                    -bounds.size.y
                } else {
                    bounds.size.y
                },
                if corner & 4 == 0 {
                    -bounds.size.z
                } else {
                    bounds.size.z
                },
            );
            let point = part.pos
                + part
                    .q
                    .rotate((bounds.center + bounds.basis.transpose().mul_vec(local)) * part.scale);
            if !point.x.is_finite() || !point.y.is_finite() || !point.z.is_finite() {
                return None;
            }
            min = PVec3::new(min.x.min(point.x), min.y.min(point.y), min.z.min(point.z));
            max = PVec3::new(max.x.max(point.x), max.y.max(point.y), max.z.max(point.z));
            found = true;
        }
    }
    let half = (max - min) * 0.5;
    (found && half.x > 0.0 && half.y > 0.0 && half.z > 0.0)
        .then_some(BoxPrim::aligned((min + max) * 0.5, half))
}

/// The parts of `InitPhysicalEntity` for a ship model at `scale`.
pub fn ship_parts(m: &ShipModel, scale: f32) -> Vec<ShipPart> {
    ship_parts_with_bounds(m, scale, None)
}

pub fn ship_parts_with_bounds(m: &ShipModel, scale: f32, bounds: Option<BoxPrim>) -> Vec<ShipPart> {
    let s = scale;
    let half = PVec3::new(
        m.mass_box_size_x * 0.5 * s,
        m.mass_box_size_y * 0.5 * s,
        m.mass_box_size_z.max(0.01) * 0.5 * s,
    );
    let c = PVec3::new(
        m.mass_center_x * s,
        m.mass_center_y * s,
        m.mass_center_z * s,
    );
    let hull_half = bounds.map_or(half, |b| b.size * s);
    let hull_center = bounds.map_or(c, |b| b.center * s);
    let mut parts = Vec::new();
    // buoy tube (desc +200 length, +204 z offset, +208 radius; 0 → defaults from the bounds)
    let tube_len = if m.tube_length == 0.0 {
        hull_half.y
    } else {
        m.tube_length * s
    };
    let tube_r = if m.tube_radius == 0.0 {
        hull_half.x
    } else {
        m.tube_radius * s
    };
    let tube_z = if m.tube_offset_z == 0.0 {
        hull_half.x
    } else {
        m.tube_offset_z * s
    };
    parts.push(ShipPart {
        prim: Primitive::Cylinder(CylinderPrim {
            center: PVec3::new(0.0, 0.0, tube_z),
            axis: PVec3::new(0.0, 1.0, 0.0),
            r: tube_r,
            hh: tube_len,
        }),
        flags: 0x1_0000,
        collider: 0,
        mass: 0.0,
    });
    // keel (desc +100 height, +104 length, +108 z offset)
    if m.keel_length > 0.0 && m.keel_height > 0.0 {
        parts.push(ShipPart {
            prim: boxp(
                PVec3::new(0.0, 0.0, (m.keel_offset_z - m.keel_height) * s),
                PVec3::new(0.1 * s, m.keel_length * 0.5 * s, m.keel_height * 0.5 * s),
            ),
            flags: 0x1_0000,
            collider: 0,
            mass: 0.0,
        });
    }
    // Native mass box: inertia only, never a solid collider.
    parts.push(ShipPart {
        prim: boxp(c, half),
        flags: 0,
        collider: 0,
        mass: m.mass,
    });
    parts.push(ShipPart {
        prim: boxp(hull_center, hull_half),
        flags: 0x80,
        collider: 0x80,
        mass: 1.0,
    });
    // the bottom box (g_unit_collide_bottom_box_size_rate 0.9, max gap 3.0, height rate 0.2,
    // min height gap 0.4)
    let bx = (hull_half.x - 3.0).max(hull_half.x * 0.9);
    let by = (hull_half.y - 3.0).max(hull_half.y * 0.9);
    let bz = (hull_half.z * 0.2).max(0.4 * 0.5);
    parts.push(ShipPart {
        prim: boxp(
            PVec3::new(
                hull_center.x,
                hull_center.y,
                hull_center.z - (bz + hull_half.z),
            ),
            PVec3::new(bx, by, bz),
        ),
        flags: 0x100,
        collider: 0x100,
        mass: 0.01,
    });
    parts
}

/// Creates the hull entity at the ship's current state.
pub fn create_entity(st: &mut WorldState, ship: &PhysShip) -> Option<EntIdx> {
    let pp = ParamsPos {
        pos: to_phys(ship.body.position),
        q: quat_to_phys(ship.body.orientation),
        ..ParamsPos::default()
    };
    let e = st
        .world
        .create_physical_entity(2, 0.0, Some(&pp), 0, 0, -1)?;
    for (index, p) in ship_parts_with_bounds(&ship.model, ship.spec.scale, ship.collision_bounds)
        .into_iter()
        .enumerate()
    {
        if index == 0 && !ship.buoys.is_empty() {
            continue; // Authored buoys replace the fallback tube, never add both volumes.
        }
        let Some(g) = st.world.geometry(&p.prim) else {
            continue;
        };
        let gp = GeomParams {
            mass: p.mass,
            density: 0.0,
            flags: p.flags,
            flags_collider: p.collider,
            ..GeomParams::default()
        };
        add_geometry(&mut st.world, e, &g, &gp, -1);
    }
    for part in &ship.buoys {
        if let Some(geom) = part.model.slot(phys_slot::DEFAULT) {
            let gp = GeomParams {
                pos: part.pos * ship.spec.scale,
                q: part.q,
                scale: part.scale * ship.spec.scale,
                mass: 0.0,
                density: 0.0,
                flags: 0x1_0000,
                flags_collider: 0,
                ..GeomParams::default()
            };
            add_geometry(&mut st.world, e, geom, &gp, -1);
        }
    }
    // kWaterDensity comes from the model (rowboats use 0.7); 0 = unset.
    let kd = if ship.model.water_density > 0.0 {
        ship.model.water_density
    } else {
        1.0
    };
    let kr = if ship.model.water_resistance > 0.0 {
        ship.model.water_resistance
    } else {
        1.0
    };
    let b = ParamsBuoyancy {
        k_water_density: kd,
        water_damping: ship.model.water_damping,
        k_water_resistance: kr,
        ..ParamsBuoyancy::default()
    };
    aaemu_physics::entities::rigid::buoyancy::set_params_buoyancy(&mut st.world, e, &b);
    let sim = ParamsSimulation {
        max_time_step: 0.02,
        min_energy: 0.16,
        damping: 0.1,
        // +60 = 2 (v297[15]): logged collision slots per step
        max_logged_collisions: 2,
        ..ParamsSimulation::default()
    };
    set_params_simulation(&mut st.world, e, &sim);
    set_params_flags(
        &mut st.world,
        e,
        &ParamsFlags {
            flags_and: !0x200,
            ..ParamsFlags::default()
        },
    );
    set_params_flags(
        &mut st.world,
        e,
        &ParamsFlags {
            flags_or: PEF_LOG_COLLISIONS,
            ..ParamsFlags::default()
        },
    );
    st.owners.insert(
        e,
        Owner::Object {
            kind: ObjKind::Ship,
            obj_id: ship.spec.obj_id,
        },
    );
    Some(e)
}

/// The engine state written back after a step.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct PulledState {
    pub pos: PVec3,
    pub q: PQuat,
}

/// The CryPhysics side of the ships of one instance.
#[derive(Debug, Default)]
pub struct CryShips {
    pub ents: HashMap<u32, EntIdx>,
    /// Last state read back (to detect positions changed by the ship code).
    pulled: HashMap<u32, PulledState>,
    /// Collision events of the hulls (filled by the event client).
    pub events: Arc<Mutex<Vec<Event>>>,
    client_installed: bool,
    pub collisions: UnitCollisions,
    /// Collision events received (diagnostics).
    pub events_seen: u64,
}

impl CryShips {
    fn install_client(&mut self, st: &mut WorldState) {
        if self.client_installed {
            return;
        }
        let buf = self.events.clone();
        st.world.add_event_client(
            1,
            true,
            0.0,
            Box::new(move |ev| {
                if let Ok(mut b) = buf.lock() {
                    if b.len() < 4096 {
                        b.push(ev.clone());
                    }
                }
                1
            }),
        );
        self.client_installed = true;
    }

    /// Before the step: entities for new ships, removal of gone ones, the ship code's
    /// velocities (and positions it changed) pushed into the engine.
    pub fn push(&mut self, st: &mut WorldState, ships: &[PhysShip]) {
        self.install_client(st);
        let alive: Vec<u32> = ships.iter().map(|s| s.spec.obj_id).collect();
        let gone: Vec<u32> = self
            .ents
            .keys()
            .copied()
            .filter(|id| !alive.contains(id))
            .collect();
        for id in gone {
            if let Some(e) = self.ents.remove(&id) {
                st.world.destroy_physical_entity(e, 0);
                st.owners.remove(&e);
                st.pending_deletes += 1;
            }
            self.pulled.remove(&id);
        }
        for s in ships {
            let id = s.spec.obj_id;
            let e = match self.ents.get(&id) {
                Some(e) => *e,
                None => {
                    let Some(e) = create_entity(st, s) else {
                        continue;
                    };
                    self.ents.insert(id, e);
                    e
                }
            };
            let pos = to_phys(s.body.position);
            let q = quat_to_phys(s.body.orientation);
            let moved = self.pulled.get(&id).is_none_or(|p| {
                (p.pos - pos).len2() > 1e-8
                    || (p.q.x - q.x).abs()
                        + (p.q.y - q.y).abs()
                        + (p.q.z - q.z).abs()
                        + (p.q.w - q.w).abs()
                        > 1e-6
            });
            if moved && self.pulled.contains_key(&id) {
                let pp = ParamsPos {
                    pos,
                    q,
                    ..ParamsPos::default()
                };
                aaemu_physics::entities::base::params::set_params_pos(&mut st.world, e, &pp, true);
            }
            let a = ActionSetVelocity {
                v: to_phys(s.body.velocity),
                w: to_phys(s.body.angular_velocity),
                ..ActionSetVelocity::default()
            };
            set_velocity(&mut st.world, e, &a);
        }
    }

    /// After the step: positions, orientations and velocities back into the ship bodies.
    pub fn pull(&mut self, st: &WorldState, ships: &mut [PhysShip]) {
        for s in ships.iter_mut() {
            let Some(e) = self.ents.get(&s.spec.obj_id).copied() else {
                continue;
            };
            let b = st.world.body_state(e);
            s.body.position = from_phys(b.pos);
            s.body.orientation = quat_from_phys(b.q);
            if b.awake {
                s.body.velocity = from_phys(b.v);
                s.body.angular_velocity = from_phys(b.w);
            } else {
                s.body.velocity = Vec3::new(0.0, 0.0, 0.0);
                s.body.angular_velocity = Vec3::new(0.0, 0.0, 0.0);
            }
            self.pulled
                .insert(s.spec.obj_id, PulledState { pos: b.pos, q: b.q });
        }
    }

    /// The collision events since the last call, folded into the 500 ms pair maxima; returns
    /// the hull damage of the pairs flushed now.
    pub fn collision_damage(
        &mut self,
        st: &WorldState,
        ships: &[PhysShip],
        now_ms: i64,
    ) -> Vec<HullDamage> {
        let events: Vec<Event> =
            std::mem::take(&mut *self.events.lock().unwrap_or_else(|e| e.into_inner()));
        self.events_seen += events.len() as u64;
        let unit_of = |e: EntIdx| match st.owner(e) {
            Some(Owner::Object {
                kind: ObjKind::Ship,
                obj_id,
            }) => Some(obj_id),
            _ => None,
        };
        for ev in events {
            if let Event::Collision { ent, pt, n, v, .. } = ev {
                let a = unit_of(ent[0]);
                let b = if ent[1].is_none() {
                    None
                } else {
                    unit_of(ent[1])
                };
                if a.is_none() && b.is_none() {
                    continue;
                }
                // v = −dot(v0 − v1, n)
                let dv = v[0] - v[1];
                let speed = -dv.dot(n);
                self.collisions.record(a, b, speed, pt, now_ms);
            }
        }
        self.collisions.flush(ships, now_ms)
    }
}

/// One aggregated pair.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct PairImpact {
    pub impact: f32,
    pub pos: PVec3,
}

/// The ZWUnitCollision manager: max closing speed per pair, flushed every 500 ms.
#[derive(Debug, Default)]
pub struct UnitCollisions {
    /// `(unit a, unit b)` with 0 = terrain / static.
    pairs: HashMap<(u32, u32), PairImpact>,
    last_flush_ms: i64,
}

impl UnitCollisions {
    /// One contact (`sub_390AF3C0`): dropped at or below the minimum speed.
    pub fn record(&mut self, a: Option<u32>, b: Option<u32>, speed: f32, pos: PVec3, _now_ms: i64) {
        if speed <= pair::HULL_DAMAGE_LOW_SPEED_THRESHOLD_MPS {
            return;
        }
        let (x, y) = (a.unwrap_or(0), b.unwrap_or(0));
        let key = (x.min(y), x.max(y));
        let e = self
            .pairs
            .entry(key)
            .or_insert(PairImpact { impact: 0.0, pos });
        if speed > e.impact {
            e.impact = speed;
            e.pos = pos;
        }
    }

    /// The flush (`sub_390AF2C0`, every 500 ms): `impact = min(impact, max)` per pair, then the
    /// world's damage.
    pub fn flush(&mut self, ships: &[PhysShip], now_ms: i64) -> Vec<HullDamage> {
        if now_ms - self.last_flush_ms < COLLIDE_REPORT_INTERVAL_MS {
            return Vec::new();
        }
        self.last_flush_ms = now_ms;
        let mut out = Vec::new();
        let find = |id: u32| ships.iter().find(|s| s.spec.obj_id == id);
        for ((a, b), p) in self.pairs.drain() {
            let impact = p.impact.min(pair::HULL_DAMAGE_SPEED_INTERP_MAX_MPS);
            let scaled = speed_scaled_hull_damage_percent(impact);
            match (find(a), find(b)) {
                (Some(sa), Some(sb)) => {
                    let (ax, az) = sa.mass_box_center_xz();
                    let (bx, bz) = sb.mass_box_center_xz();
                    let nose_a = super::contacts::nose_cone(bx - ax, bz - az, sa.bow());
                    let nose_b = super::contacts::nose_cone(ax - bx, az - bz, sb.bow());
                    out.push(HullDamage {
                        target: a,
                        attacker: b,
                        percent: if nose_a {
                            pair::HULL_DAMAGE_SPEED_SCALED_MIN_PERCENT
                        } else {
                            scaled
                        },
                    });
                    out.push(HullDamage {
                        target: b,
                        attacker: a,
                        percent: if nose_b {
                            pair::HULL_DAMAGE_SPEED_SCALED_MIN_PERCENT
                        } else {
                            scaled
                        },
                    });
                }
                (Some(s), None) | (None, Some(s)) => out.push(HullDamage {
                    target: s.spec.obj_id,
                    attacker: s.spec.obj_id,
                    percent: scaled,
                }),
                (None, None) => {}
            }
        }
        out
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::systems::ai::collision::world::PhysWorld;
    use crate::systems::ai::physics::ship::tests::{model, spec};
    use crate::systems::ai::physics::ship::{PhysShip, ShipGameView};

    fn assert_same_primitive(a: Primitive, b: Primitive) {
        match (a, b) {
            (Primitive::Box(a), Primitive::Box(b)) => assert_eq!(a, b),
            (Primitive::Cylinder(a), Primitive::Cylinder(b)) => assert_eq!(a, b),
            _ => panic!("different primitive kinds: {a:?}, {b:?}"),
        }
    }

    #[test]
    fn authored_hull_bounds_do_not_change_the_inertia_box_or_explicit_buoy_parameters() {
        let model = model();
        let bounds = BoxPrim::aligned(PVec3::new(2.0, -1.0, 3.0), PVec3::new(4.0, 8.0, 2.0));
        for scale in [0.5, 1.0, 2.0] {
            let fallback = ship_parts(&model, scale);
            let authored = ship_parts_with_bounds(&model, scale, Some(bounds));
            let mass =
                |parts: &[ShipPart]| parts.iter().find(|p| p.mass == model.mass).unwrap().clone();
            assert_same_primitive(mass(&fallback).prim, mass(&authored).prim);
            assert_eq!((mass(&authored).flags, mass(&authored).collider), (0, 0));
            let hull = authored.iter().find(|p| p.collider == 0x80).unwrap();
            assert_same_primitive(
                hull.prim,
                Primitive::Box(BoxPrim::aligned(bounds.center * scale, bounds.size * scale)),
            );
            let bottom = authored.iter().find(|p| p.collider == 0x100).unwrap();
            let Primitive::Box(bottom) = bottom.prim else {
                panic!("bottom box")
            };
            assert!(
                (bottom.center.z + bottom.size.z - (bounds.center.z - bounds.size.z) * scale).abs()
                    < 1e-5
            );
        }
        let mut explicit = model;
        explicit.tube_length = 7.0;
        explicit.tube_radius = 1.25;
        explicit.tube_offset_z = 0.3;
        assert_same_primitive(
            ship_parts(&explicit, 2.0)[0].prim,
            ship_parts_with_bounds(&explicit, 2.0, Some(bounds))[0].prim,
        );
        let fallback = ship_parts(&model, 1.0);
        let explicit_fallback = ship_parts_with_bounds(&model, 1.0, None);
        assert_eq!(fallback.len(), explicit_fallback.len());
        for (a, b) in fallback.iter().zip(explicit_fallback) {
            assert_same_primitive(a.prim, b.prim);
            assert_eq!((a.flags, a.collider, a.mass), (b.flags, b.collider, b.mass));
        }
    }

    #[test]
    fn prefab_hull_bounds_apply_rotation_scale_translation_and_cache_models() {
        use aaemu_physics::geometry::shapes::stream::{save_phys_geometry, PhysGeomHeader};
        use aaemu_physics::geometry::shapes::BoxGeom;
        let geometry = BoxGeom::new(&BoxPrim::aligned(PVec3::ZERO, PVec3::new(1.0, 2.0, 0.5)));
        let mut blob = Vec::new();
        save_phys_geometry(
            &PhysGeomHeader {
                inertia: PVec3::new(1.0, 1.0, 1.0),
                q: PQuat::IDENTITY,
                origin: PVec3::ZERO,
                volume: 8.0,
                unk52: 1,
                surface_idx: 0,
            },
            &geometry,
            &mut blob,
        );
        let mut map = HashMap::new();
        map.insert(
            "game/objects/test/hull.cgf".to_string(),
            crate::systems::ai::collision::cgf::tests::synthetic_cgf(&blob),
        );
        map.insert("game/prefabs/test.xml".to_string(), br#"<PrefabsLibrary><Prefab Name="boats.normal"><Objects>
          <Object Type="Brush" Name="hullA" Prefab="objects/test/hull.cgf" Pos="10,-3,2" Rotate="0.70710678,0,0,0.70710678" Scale="2,2,2"/>
          <Object Type="Brush" Name="hullB" Prefab="objects/test/hull.cgf" Pos="-3,6,1"/>
        </Objects></Prefab></PrefabsLibrary>"#.to_vec());
        let map = Arc::new(map);
        let files = Files::Map(map.clone());
        let cache = ModelCache::new(map);
        let bounds =
            model_collision_bounds(&files, &cache, "prefab://prefabs/test.xml/boats.normal")
                .unwrap();
        assert!((bounds.center.x - 5.0).abs() < 1e-5);
        assert!((bounds.center.y - 1.5).abs() < 1e-5);
        assert!((bounds.center.z - 1.75).abs() < 1e-5);
        assert!((bounds.size.x - 9.0).abs() < 1e-5);
        assert!((bounds.size.y - 6.5).abs() < 1e-5);
        assert!((bounds.size.z - 1.25).abs() < 1e-5);
        let loaded = cache
            .stats
            .loaded
            .load(std::sync::atomic::Ordering::Relaxed);
        assert_eq!(
            model_collision_bounds(&files, &cache, "prefab://prefabs/test.xml/boats.normal"),
            Some(bounds)
        );
        assert_eq!(
            cache
                .stats
                .loaded
                .load(std::sync::atomic::Ordering::Relaxed),
            loaded
        );
        assert!(
            model_collision_bounds(&files, &cache, "prefab://prefabs/test.xml/boats.missing")
                .is_none()
        );
    }

    #[test]
    fn authored_buoys_replace_the_tube_and_preserve_their_local_placements() {
        use crate::systems::ai::collision::models::{ModelPart, ModelPhys};

        let pw = PhysWorld::new(0, 0, "t", 1, 1, 100.0, None);
        let mut st = pw.lock();
        let geom = st
            .world
            .geometry(&boxp(PVec3::ZERO, PVec3::new(1.0, 1.0, 0.125)))
            .unwrap();
        let model_phys = Arc::new(ModelPhys {
            path: "game/objects/test_buoy.cgf".into(),
            parts: vec![ModelPart {
                slot: phys_slot::DEFAULT,
                geom: geom.clone(),
            }],
            blob_bytes: 0,
        });
        let mut ship = PhysShip::new(
            spec(1, 300.0, 300.0, 100.0, 0.0),
            model(),
            ShipGameView::default(),
        );
        ship.spec.scale = 2.0;
        let fallback = create_entity(&mut st, &ship).unwrap();
        assert_eq!(
            st.world.ents[fallback.0 as usize]
                .as_ref()
                .unwrap()
                .parts
                .iter()
                .filter(|p| p.flags & 0x1_0000 != 0)
                .count(),
            1
        );
        let q = PQuat::new(0.043619387, 0.0, 0.0, 0.99904823);
        ship.buoys = [-3.0, 2.7]
            .into_iter()
            .map(|y| ShapePart {
                model: model_phys.clone(),
                pos: PVec3::new(0.0, y, 0.15),
                q,
                scale: 3.0,
            })
            .collect();
        let authored = create_entity(&mut st, &ship).unwrap();
        let floating: Vec<_> = st.world.ents[authored.0 as usize]
            .as_ref()
            .unwrap()
            .parts
            .iter()
            .filter(|p| p.flags & 0x1_0000 != 0)
            .collect();
        assert_eq!(floating.len(), 2, "no extra fallback volume");
        for (part, y) in floating.iter().zip([-6.0, 5.4]) {
            assert_eq!(part.pos, PVec3::new(0.0, y, 0.3));
            assert_eq!(part.q, q);
            assert_eq!(part.scale, 6.0);
            assert_eq!((part.flags_collider, part.mass), (0, 0.0));
            assert!(Arc::ptr_eq(&part.pgeom, &geom));
        }
    }

    #[test]
    fn a_falling_hull_logs_collision_events() {
        let pw = PhysWorld::new(
            0,
            0,
            "t",
            1,
            1,
            0.0,
            Some(Arc::new(
                crate::systems::ai::collision::terrain::tests::Plane {
                    a: 50.0,
                    b: 0.0,
                    c: 0.0,
                    n: 512,
                },
            )),
        );
        let mut ships = vec![PhysShip::new(
            spec(1, 300.0, 300.0, 60.0, 0.0),
            model(),
            ShipGameView::default(),
        )];
        let mut cry = CryShips::default();
        let mut seen = 0;
        for _ in 0..100 {
            let mut st = pw.lock();
            cry.push(&mut st, &ships);
            st.world.step(0.04);
            cry.pull(&st, &mut ships);
            seen += cry.events.lock().unwrap().len();
            cry.events.lock().unwrap().clear();
        }
        assert!(seen > 0);
    }
}
