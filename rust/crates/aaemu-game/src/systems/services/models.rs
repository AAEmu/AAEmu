//! `Core/Managers/ModelManager` (complete) and `Models/Game/Models/*`: actor / ship / vehicle
//! models, the `models` type index and the game stances of actor models. The npcs and skills
//! subsystems load the parts they read before this port existed (`MovementId`, radii); this is
//! the full table set for the remaining users (ships, vehicles, AI movement speeds).

use std::collections::{BTreeMap, HashMap};

use rusqlite::Connection;

use crate::data::sqlite::for_each_row;

/// `GameStanceType` (packet values; the DB is +1).
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct GameStanceType(pub u8);

impl GameStanceType {
    pub const COMBAT: Self = Self(0);
    pub const RELAXED: Self = Self(1);
    pub const SWIM: Self = Self(2);
    pub const CO_SWIM: Self = Self(3);
    pub const COMBAT2: Self = Self(4);
    pub const STEALTH: Self = Self(5);
    pub const CROUCH: Self = Self(6);
    pub const PRONE: Self = Self(7);
    pub const FLY: Self = Self(8);
}

/// `GameStance`.
#[derive(Debug, Clone, PartialEq)]
pub struct GameStance {
    pub id: u32,
    pub stance_id: GameStanceType,
    pub actor_model_id: u32,
    pub name: String,
    pub ai_move_speed_run: f32,
    pub ai_move_speed_slow: f32,
    pub ai_move_speed_sprint: f32,
    pub ai_move_speed_walk: f32,
    pub height_collider: f32,
    pub height_pivot: f32,
    pub normal_speed: f32,
    pub max_speed: f32,
    pub size: [f32; 3],
    pub model_offset: [f32; 3],
    pub view_offset: [f32; 3],
    pub use_capsule: bool,
}

/// `ActorModel`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct ActorModel {
    pub id: u32,
    pub radius: f32,
    pub height: f32,
    /// 0 = normal, 1 = mounts, 2 = bird / fish (ignores gravity), 3 = underground.
    pub movement_id: i32,
    pub stances: BTreeMap<GameStanceType, GameStance>,
}

/// `ShipModelV1`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct ShipModel {
    pub id: u32,
    pub velocity: f32,
    pub mass: f32,
    pub mass_center: [f32; 3],
    pub mass_box_size: [f32; 3],
    pub water_density: f32,
    pub water_resistance: f32,
    pub steer_vel: f32,
    pub accel: f32,
    pub reverse_accel: f32,
    pub reverse_velocity: f32,
    pub turn_accel: f32,
    pub tube_length: f32,
    pub tube_radius: f32,
    pub tube_offset_z: f32,
    pub keel_length: f32,
    pub keel_height: f32,
    pub keel_offset_z: f32,
}

/// `VehicleModel`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct VehicleModel {
    pub id: u32,
    pub lin_inertia: f32,
    pub lin_deaccel_inertia: f32,
    pub rot_inertia: f32,
    pub rot_deaccel_inertia: f32,
    pub velocity: f32,
    pub ang_vel: f32,
    pub can_fly: f32,
    pub wheeled_vehicle_mass: f32,
    pub wheeled_vehicle_power: f32,
    pub wheeled_vehicle_brake_torque: f32,
    pub wheeled_vehicle_max_gear: u32,
    pub wheeled_vehicle_gear_speed_ratio_reverse: f32,
    pub wheeled_vehicle_gear_speed_ratio: [f32; 3],
    pub wheeled_vehicle_susp_stroke: f32,
    pub wheeled_vehicle_drive: f32,
    pub wheeled_vehicle_front_optimal_sa: f32,
    pub wheeled_vehicle_rear_optimal_sa: f32,
}

/// `ModelType` (`models`: id -> (sub type, sub id)).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ModelType {
    pub id: u32,
    pub sub_id: u32,
    pub sub_type: String,
}

/// `ModelManager`.
#[derive(Debug, Clone, Default)]
pub struct ModelManager {
    actor_models: HashMap<u32, ActorModel>,
    ship_models: HashMap<u32, ShipModel>,
    vehicle_models: HashMap<u32, VehicleModel>,
    model_types: HashMap<u32, ModelType>,
    game_stances: HashMap<u32, GameStance>,
}

impl ModelManager {
    /// `Load()` (`TryAdd`: the first row of an id wins).
    pub fn load(conn: &Connection) -> rusqlite::Result<Self> {
        let mut m = Self::default();
        for_each_row(conn, "SELECT * FROM actor_models", [], |r| {
            let model = ActorModel {
                id: r.u32("id")?,
                radius: r.f32("radius")?,
                height: r.f32("height")?,
                movement_id: r.i32("movement_id")?,
                stances: BTreeMap::new(),
            };
            m.actor_models.entry(model.id).or_insert(model);
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM ship_models", [], |r| {
            let model = ShipModel {
                id: r.u32("id")?,
                velocity: r.f32("velocity")?,
                mass: r.f32("mass")?,
                mass_center: [
                    r.f32("mass_center_x")?,
                    r.f32("mass_center_y")?,
                    r.f32("mass_center_z")?,
                ],
                mass_box_size: [
                    r.f32("mass_box_size_x")?,
                    r.f32("mass_box_size_y")?,
                    r.f32("mass_box_size_z")?,
                ],
                water_density: r.f32_or("water_density", 1.0)?,
                water_resistance: r.f32_or("water_resistance", 1.0)?,
                steer_vel: r.f32("steer_vel")?,
                accel: r.f32("accel")?,
                reverse_accel: r.f32("reverse_accel")?,
                reverse_velocity: r.f32("reverse_velocity")?,
                turn_accel: r.f32("turn_accel")?,
                tube_length: r.f32("tube_length")?,
                tube_radius: r.f32("tube_radius")?,
                tube_offset_z: r.f32("tube_offset_z")?,
                keel_length: r.f32("keel_length")?,
                keel_height: r.f32("keel_height")?,
                keel_offset_z: r.f32("keel_offset_z")?,
            };
            m.ship_models.entry(model.id).or_insert(model);
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM vehicle_models", [], |r| {
            let model = VehicleModel {
                id: r.u32("id")?,
                lin_inertia: r.f32("lin_inertia")?,
                lin_deaccel_inertia: r.f32("lin_deaccel_inertia")?,
                rot_inertia: r.f32("rot_inertia")?,
                rot_deaccel_inertia: r.f32("rot_deaccel_inertia")?,
                velocity: r.f32("velocity")?,
                ang_vel: r.f32("angVel")?,
                can_fly: r.f32("can_fly")?,
                wheeled_vehicle_mass: r.f32("wheeled_vehicle_mass")?,
                wheeled_vehicle_power: r.f32("wheeled_vehicle_power")?,
                wheeled_vehicle_brake_torque: r.f32("wheeled_vehicle_brake_torque")?,
                wheeled_vehicle_max_gear: r.u32("wheeled_vehicle_max_gear")?,
                wheeled_vehicle_gear_speed_ratio_reverse: r
                    .f32("wheeled_vehicle_gear_speed_ratio_reverse")?,
                wheeled_vehicle_gear_speed_ratio: [
                    r.f32("wheeled_vehicle_gear_speed_ratio_1")?,
                    r.f32("wheeled_vehicle_gear_speed_ratio_2")?,
                    r.f32("wheeled_vehicle_gear_speed_ratio_3")?,
                ],
                wheeled_vehicle_susp_stroke: r.f32("wheeled_vehicle_susp_stroke")?,
                wheeled_vehicle_drive: r.f32("wheeled_vehicle_drive")?,
                wheeled_vehicle_front_optimal_sa: r.f32("wheeled_vehicle_front_optimal_sa")?,
                wheeled_vehicle_rear_optimal_sa: r.f32("wheeled_vehicle_rear_optimal_sa")?,
            };
            m.vehicle_models.entry(model.id).or_insert(model);
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM models", [], |r| {
            let t = ModelType {
                id: r.u32("id")?,
                sub_id: r.u32("sub_id")?,
                sub_type: r.string("sub_type")?,
            };
            m.model_types.entry(t.id).or_insert(t);
            Ok(())
        })?;
        let mut duplicate_stance = None;
        for_each_row(conn, "SELECT * FROM game_stances", [], |r| {
            let v3 = |a: &str, b: &str, c: &str| -> rusqlite::Result<[f32; 3]> {
                Ok([r.f32(a)?, r.f32(b)?, r.f32(c)?])
            };
            let stance = GameStance {
                id: r.u32("id")?,
                actor_model_id: r.u32("actor_model_id")?,
                // This seems to be +1 in the DB compared to the packets.
                stance_id: GameStanceType(r.u8("stance_id")?.wrapping_sub(1)),
                name: r.string("name")?,
                ai_move_speed_run: r.f32("ai_move_speed_run")?,
                ai_move_speed_slow: r.f32("ai_move_speed_slow")?,
                ai_move_speed_sprint: r.f32("ai_move_speed_sprint")?,
                ai_move_speed_walk: r.f32("ai_move_speed_walk")?,
                height_collider: r.f32("height_collider")?,
                height_pivot: r.f32("height_pivot")?,
                max_speed: r.f32("max_speed")?,
                model_offset: v3("model_offset_x", "model_offset_y", "model_offset_z")?,
                normal_speed: r.f32("normal_speed")?,
                size: v3("size_x", "size_y", "size_z")?,
                // `GetBoolean("use_capsule", fromString: true)`.
                use_capsule: r.bool_str("use_capsule")?,
                view_offset: v3("view_offset_x", "view_offset_y", "view_offset_z")?,
            };
            m.game_stances
                .entry(stance.id)
                .or_insert_with(|| stance.clone());
            if let Some(actor) = m.actor_models.get_mut(&stance.actor_model_id) {
                // `Stances.Add`: a duplicate stance throws in C# (aborts the load).
                match actor.stances.entry(stance.stance_id) {
                    std::collections::btree_map::Entry::Occupied(_) => {
                        duplicate_stance = Some((stance.actor_model_id, stance.stance_id.0));
                    }
                    std::collections::btree_map::Entry::Vacant(v) => {
                        v.insert(stance);
                    }
                }
            }
            Ok(())
        })?;
        if let Some((model, stance)) = duplicate_stance {
            return Err(rusqlite::Error::InvalidParameterName(format!(
                "An item with the same key has already been added. Key: {stance} (actor model {model})"
            )));
        }
        Ok(m)
    }

    /// `GetModelType(modelId)`.
    pub fn get_model_type(&self, model_id: u32) -> Option<&ModelType> {
        self.model_types.get(&model_id)
    }

    fn sub(&self, model_id: u32, sub_type: &str) -> Option<u32> {
        self.model_types
            .get(&model_id)
            .filter(|t| t.sub_type == sub_type)
            .map(|t| t.sub_id)
    }

    /// `GetActorModel(modelId)`.
    pub fn get_actor_model(&self, model_id: u32) -> Option<&ActorModel> {
        self.sub(model_id, "ActorModel")
            .and_then(|id| self.actor_models.get(&id))
    }

    /// `GetShipModel(modelId)`.
    pub fn get_ship_model(&self, model_id: u32) -> Option<&ShipModel> {
        self.sub(model_id, "ShipModel")
            .and_then(|id| self.ship_models.get(&id))
    }

    /// `GetVehicleModels(modelId)`.
    pub fn get_vehicle_models(&self, model_id: u32) -> Option<&VehicleModel> {
        self.sub(model_id, "VehicleModel")
            .and_then(|id| self.vehicle_models.get(&id))
    }

    /// `IsFlyOrSwim(modelId)`: an actor model with `MovementId == 2`.
    pub fn is_fly_or_swim(&self, model_id: u32) -> bool {
        self.get_actor_model(model_id)
            .is_some_and(|m| m.movement_id == 2)
    }

    /// The game stances by id (`_gameStances`).
    pub fn game_stance(&self, id: u32) -> Option<&GameStance> {
        self.game_stances.get(&id)
    }

    /// (actor, ship, vehicle, model types, stances) counts.
    pub fn counts(&self) -> (usize, usize, usize, usize, usize) {
        (
            self.actor_models.len(),
            self.ship_models.len(),
            self.vehicle_models.len(),
            self.model_types.len(),
            self.game_stances.len(),
        )
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn lookups_follow_the_model_type_index() {
        let c = Connection::open_in_memory().unwrap();
        c.execute_batch(
            "CREATE TABLE actor_models (id INT, radius REAL, height REAL, movement_id INT);
             INSERT INTO actor_models VALUES (1, 0.5, 2.0, 2), (2, 1.0, 1.0, 0);
             CREATE TABLE ship_models (id INT, velocity REAL, mass REAL, mass_center_x REAL, mass_center_y REAL,
               mass_center_z REAL, mass_box_size_x REAL, mass_box_size_y REAL, mass_box_size_z REAL,
               water_density REAL, water_resistance REAL, steer_vel REAL, accel REAL, reverse_accel REAL,
               reverse_velocity REAL, turn_accel REAL, tube_length REAL, tube_radius REAL, tube_offset_z REAL,
               keel_length REAL, keel_height REAL, keel_offset_z REAL);
             INSERT INTO ship_models VALUES (7, 1,2,3,4,5,6,7,8, NULL, NULL, 1,1,1,1,1,1,1,1,1,1,1);
             CREATE TABLE vehicle_models (id INT, lin_inertia REAL, lin_deaccel_inertia REAL, rot_inertia REAL,
               rot_deaccel_inertia REAL, velocity REAL, angVel REAL, can_fly REAL, wheeled_vehicle_mass REAL,
               wheeled_vehicle_power REAL, wheeled_vehicle_brake_torque REAL, wheeled_vehicle_max_gear INT,
               wheeled_vehicle_gear_speed_ratio_reverse REAL, wheeled_vehicle_gear_speed_ratio_1 REAL,
               wheeled_vehicle_gear_speed_ratio_2 REAL, wheeled_vehicle_gear_speed_ratio_3 REAL,
               wheeled_vehicle_susp_stroke REAL, wheeled_vehicle_drive REAL, wheeled_vehicle_front_optimal_sa REAL,
               wheeled_vehicle_rear_optimal_sa REAL);
             INSERT INTO vehicle_models VALUES (3, 1,1,1,1,1,1,0,1,1,1,4,1,1,1,1,1,1,1,1);
             CREATE TABLE models (id INT, sub_id INT, sub_type TEXT);
             INSERT INTO models VALUES (100, 1, 'ActorModel'), (101, 7, 'ShipModel'), (102, 3, 'VehicleModel'), (103, 2, 'PrefabModel');
             CREATE TABLE game_stances (id INT, stance_id INT, normal_speed REAL, max_speed REAL, height_collider REAL,
               height_pivot REAL, size_x REAL, size_y REAL, size_z REAL, model_offset_x REAL, model_offset_y REAL,
               model_offset_z REAL, view_offset_x REAL, view_offset_y REAL, view_offset_z REAL, name TEXT,
               use_capsule TEXT, actor_model_id INT, ai_move_speed_slow REAL, ai_move_speed_walk REAL,
               ai_move_speed_run REAL, ai_move_speed_sprint REAL);
             INSERT INTO game_stances VALUES (1, 2, 5, 7, 1,1, 1,1,1, 0,0,0, 0,0,0, 'relaxed', 'f', 1, 1, 2, 3, 4);",
        )
        .unwrap();
        let m = ModelManager::load(&c).unwrap();
        assert!(m.is_fly_or_swim(100));
        assert!(!m.is_fly_or_swim(102));
        assert_eq!(m.get_actor_model(100).unwrap().radius, 0.5);
        assert!(m.get_actor_model(103).is_none());
        let stance = &m.get_actor_model(100).unwrap().stances[&GameStanceType::RELAXED];
        assert_eq!((stance.ai_move_speed_run, stance.use_capsule), (3.0, false));
        assert_eq!(m.get_ship_model(101).unwrap().water_density, 1.0);
        assert_eq!(
            m.get_vehicle_models(102).unwrap().wheeled_vehicle_max_gear,
            4
        );
        assert!(m.get_ship_model(100).is_none());
        assert_eq!(m.counts(), (2, 1, 1, 4, 1));
    }

    #[test]
    fn real_compact_db_models() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let c =
            Connection::open_with_flags(&path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY).unwrap();
        let m = ModelManager::load(&c).unwrap();
        assert_eq!(m.counts(), (753, 26, 63, 1491, 6777));
    }
}
