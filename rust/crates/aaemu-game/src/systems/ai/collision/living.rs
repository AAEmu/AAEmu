//! Units as CryPhysics living entities (type 4, `CLivingEntity`, P5 of `aaemu-physics`).
//!
//! The zone keeps a living entity for every character, NPC and mount (`WZUnitState` creates /
//! updates the copy, `ZONE-PHYSICS-USAGE.md` §1.2) built from `actor_models.physics_living_*`
//! (mass, gravity, air control / resistance, slide / climb / fall angles, max ground speed,
//! impulse recovery) with the model's radius / height as the collider. Here the client stays
//! authoritative for positions (the 1.2 protocol has no zone), so the entities are
//! **inactive proxies** (`pe_player_dynamics.bActive = 0`): they are placed at the unit
//! positions (every client move for players and mounts, every residency tick for NPCs) and
//! are hit by queries that include living entities (`WZRayCasting` 0x31F, `entTypes` 15
//! sweeps), and their dimensions are the blink collider of `GetBlinkPosition`
//! (`sub_3925E670` uses the living entity's `pe_player_dimensions`).

use aaemu_physics::entities::living;
use aaemu_physics::math::Vec3;
use aaemu_physics::params::{ParamsPlayerDimensions, ParamsPlayerDynamics, ParamsPos, PeStruct};
use aaemu_physics::world::EntIdx;

use super::data::LivingParams;
use super::world::{ObjKind, Owner, WorldState};

/// `pe_player_dimensions` from the model: capsule of radius `radius`, total height `height`
/// (at least 2·radius), centred at half the height above the feet.
pub fn dimensions(p: &LivingParams) -> ParamsPlayerDimensions {
    let r = p.radius.max(0.1);
    let h = p.height.max(2.0 * r);
    ParamsPlayerDimensions {
        height_pivot: 0.0,
        height_eye: h * 0.9,
        size_collider: Vec3::new(r, r, (h * 0.5 - r).max(0.001)),
        height_collider: h * 0.5,
        head_radius: 0.0,
        height_head: h * 0.9,
        unproj_dir: Vec3::new(0.0, 0.0, 1.0),
        max_unproj: 0.0,
        use_capsule: 1,
        // placed on the ground: the size change must not be refused by the unprojection test
        apply_on_fail: 1,
        ..ParamsPlayerDimensions::default()
    }
}

/// `pe_player_dynamics` from `actor_models.physics_living_*` (inactive proxy).
pub fn dynamics(p: &LivingParams) -> ParamsPlayerDynamics {
    ParamsPlayerDynamics {
        k_air_control: p.k_air_control,
        k_air_resistance: p.air_resistance,
        gravity: Vec3::new(0.0, 0.0, -p.gravity),
        mass: p.mass,
        min_slide_angle: p.min_slide_angle,
        max_climb_angle: p.max_climb_angle,
        min_fall_angle: p.min_fall_angle,
        max_vel_ground: p.max_vel_ground,
        time_impulse_recover: p.time_impulse_recover,
        active: 0,
        ..ParamsPlayerDynamics::default()
    }
}

fn set<T: PeStruct>(st: &mut WorldState, e: EntIdx, p: &T) -> i32 {
    let mut buf = vec![0u8; T::SIZE];
    p.write(&mut buf);
    living::set_params(&mut st.world, e, &mut buf, 1)
}

impl WorldState {
    /// The living proxy of a unit (created on first use), placed at `pos` (feet).
    pub fn sync_living(&mut self, obj_id: u32, params: &LivingParams, pos: Vec3) -> Option<EntIdx> {
        let key = (ObjKind::Unit, obj_id);
        if let Some(c) = self.living.get(&obj_id).copied() {
            let cur = self.world.ent(c).pos;
            if (cur - pos).len2() > 1e-6 {
                let pp = ParamsPos {
                    pos,
                    ..ParamsPos::default()
                };
                aaemu_physics::entities::base::params::set_params_pos(
                    &mut self.world,
                    c,
                    &pp,
                    true,
                );
            }
            return Some(c);
        }
        let pp = ParamsPos {
            pos,
            ..ParamsPos::default()
        };
        let e = self
            .world
            .create_physical_entity(4, 0.0, Some(&pp), 0, 0, -1)?;
        // dynamics first: an inactive entity skips the size-change unprojection test
        let r2 = set(self, e, &dynamics(params));
        let r1 = set(self, e, &dimensions(params));
        tracing::trace!("living proxy {obj_id}: dims {r1} dyn {r2}");

        self.owners.insert(
            e,
            Owner::Object {
                kind: key.0,
                obj_id,
            },
        );
        self.living.insert(obj_id, e);
        Some(e)
    }

    /// Removes a unit's living proxy.
    pub fn remove_living(&mut self, obj_id: u32) -> bool {
        let Some(e) = self.living.remove(&obj_id) else {
            return false;
        };
        self.world.destroy_physical_entity(e, 0);
        self.owners.remove(&e);
        self.pending_deletes += 1;
        true
    }
}

/// The living parameters of a unit model (`actor_models`; the humanoid defaults otherwise).
pub fn unit_params(server: &crate::server::GameServer, model_id: u32) -> LivingParams {
    server
        .game_data
        .systems
        .ai
        .collision
        .living
        .get(&model_id)
        .copied()
        .unwrap_or_default()
}

/// Places a unit's proxy (client move / NPC move) when its world has a collision world.
pub fn sync_one(
    server: &crate::server::GameServer,
    instance_id: u32,
    unit: &crate::world::WorldObject,
    pos: [f32; 3],
) {
    let Some(pw) = server.systems.ai.collision.existing(instance_id) else {
        return;
    };
    let Some(model_id) = unit.with_unit(|u| u.unit().model_id) else {
        return;
    };
    let p = unit_params(server, model_id);
    pw.lock()
        .sync_living(unit.obj_id(), &p, Vec3::new(pos[0], pos[1], pos[2]));
}

/// The residency pass: proxies for the characters, NPCs and mounts of the loaded cells; the
/// proxies of units that left are removed.
pub fn sync_world(server: &crate::server::GameServer, pw: &super::world::PhysWorld) {
    let Some(world) = server.world.world(pw.instance_id) else {
        return;
    };
    let cells: Vec<(i32, i32)> = pw.lock().cells.keys().copied().collect();
    let mut units = Vec::new();
    for (cx, cy) in cells {
        for o in world.objects_in_cell(cx, cy) {
            if o.as_player().is_none() && o.as_npc().is_none() && o.as_mate().is_none() {
                continue;
            }
            let Some(model_id) = o.with_unit(|u| u.unit().model_id) else {
                continue;
            };
            let p = world.world_pos_rot(&o);
            units.push((
                o.obj_id(),
                unit_params(server, model_id),
                Vec3::new(p.x, p.y, p.z),
            ));
        }
    }
    let mut st = pw.lock();
    let keep: std::collections::HashSet<u32> = units.iter().map(|u| u.0).collect();
    let gone: Vec<u32> = st
        .living
        .keys()
        .copied()
        .filter(|id| !keep.contains(id))
        .collect();
    for id in gone {
        st.remove_living(id);
    }
    for (id, p, pos) in units {
        st.sync_living(id, &p, pos);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::systems::ai::collision::query::{mask, ray_flags};
    use crate::systems::ai::collision::tests::flat_world;

    #[test]
    fn living_proxies_block_rays_with_the_living_mask_only() {
        let pw = flat_world(100.0);
        let mut st = pw.lock();
        let p = LivingParams::default();
        let e = st.sync_living(7, &p, Vec3::new(50.0, 50.0, 100.0)).unwrap();
        // sideways ray at chest height
        // WZRayCasting flags (colltype any): living colliders have no 0x8000 ray class
        let ray = |st: &mut WorldState, objtypes| {
            st.raycast(
                Vec3::new(45.0, 50.0, 100.5),
                Vec3::new(55.0, 50.0, 100.5),
                objtypes,
                ray_flags::WZ_RAYCASTING,
                &[],
            )
        };
        {
            // the model's capsule: r 0.5, centre 0.5 above the feet
            let ent = st.world.ent(e);
            assert!((ent.bbox[1].x - 50.5).abs() < 1e-3, "{:?}", ent.bbox);
        }
        let h = ray(&mut st, mask::ALL_TERRAIN_WATER).expect("unit hit");
        assert_eq!(
            h.owner,
            Some(Owner::Object {
                kind: ObjKind::Unit,
                obj_id: 7
            })
        );
        assert!((h.pos.x - (50.0 - p.radius)).abs() < 0.05, "{h:?}");
        assert!(ray(&mut st, mask::STATIC_TERRAIN).is_none());
        // moving the unit moves the proxy; the same entity is kept
        assert_eq!(st.sync_living(7, &p, Vec3::new(80.0, 50.0, 100.0)), Some(e));
        assert!(ray(&mut st, mask::ALL_TERRAIN_WATER).is_none());
        assert!(st.remove_living(7));
        st.collect_garbage();
        assert!(st.living.is_empty());
    }
}
