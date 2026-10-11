//! The DB columns the collision world reads (`server.game_data.systems.ai.collision`):
//!
//! * `doodad_func_groups.model` — the zone picks the doodad's model class from it (the C#
//!   discards this column, so the npcs subsystem's groups have `model = None`);
//! * `actor_models.physics_living_*` — the living-entity parameters of characters / NPCs /
//!   mounts (`X2::UnitModel` → `pe_player_dynamics` / `pe_player_dimensions`), plus the model
//!   radius / height used for the collider capsule.

use std::collections::HashMap;

use rusqlite::Connection;

use crate::data::sqlite::for_each_row;

/// `actor_models.physics_living_*` (+ `radius`, `height`).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct LivingParams {
    pub mass: f32,
    pub gravity: f32,
    pub air_resistance: f32,
    pub k_air_control: f32,
    pub max_vel_ground: f32,
    pub min_slide_angle: f32,
    pub max_climb_angle: f32,
    pub min_fall_angle: f32,
    pub time_impulse_recover: f32,
    pub radius: f32,
    pub height: f32,
}

impl Default for LivingParams {
    /// The values of the common humanoid rows (`actor_models` 102).
    fn default() -> Self {
        Self {
            mass: 80.0,
            gravity: 20.0,
            air_resistance: 0.29,
            k_air_control: 0.9,
            max_vel_ground: 30.0,
            min_slide_angle: 45.0,
            max_climb_angle: 50.0,
            min_fall_angle: 50.0,
            time_impulse_recover: 1.0,
            radius: 0.5,
            height: 0.5,
        }
    }
}

/// Tables of the collision world.
#[derive(Debug, Default)]
pub struct CollisionData {
    /// `doodad_func_groups.id → model` (non-empty only).
    pub func_group_models: HashMap<u32, String>,
    /// `actor_models.id → living params`.
    pub living: HashMap<u32, LivingParams>,
}

impl CollisionData {
    pub fn load(conn: &Connection) -> rusqlite::Result<Self> {
        let mut d = Self::default();
        let _ = for_each_row(conn, "SELECT id, model FROM doodad_func_groups", [], |r| {
            let m = r.string_or("model", "")?;
            if !m.trim().is_empty() {
                d.func_group_models.insert(r.u32("id")?, m);
            }
            Ok(())
        });
        let _ = for_each_row(conn, "SELECT * FROM actor_models", [], |r| {
            let def = LivingParams::default();
            d.living.insert(
                r.u32("id")?,
                LivingParams {
                    mass: r.f32_or("physics_living_mass", def.mass)?,
                    gravity: r.f32_or("physics_living_gravity", def.gravity)?,
                    air_resistance: r
                        .f32_or("physics_living_air_resistance", def.air_resistance)?,
                    k_air_control: r.f32_or("physics_living_k_air_control", def.k_air_control)?,
                    max_vel_ground: r
                        .f32_or("physics_living_max_vel_ground", def.max_vel_ground)?,
                    min_slide_angle: r
                        .f32_or("physics_living_min_slide_angle", def.min_slide_angle)?,
                    max_climb_angle: r
                        .f32_or("physics_living_max_climb_angle", def.max_climb_angle)?,
                    min_fall_angle: r
                        .f32_or("physics_living_min_fall_angle", def.min_fall_angle)?,
                    time_impulse_recover: r.f32_or(
                        "physics_living_time_impulse_recover",
                        def.time_impulse_recover,
                    )?,
                    radius: r.f32_or("radius", def.radius)?,
                    height: r.f32_or("height", def.height)?,
                },
            );
            Ok(())
        });
        Ok(d)
    }

    /// The model reference of a doodad func group.
    pub fn func_group_model(&self, func_group_id: u32) -> Option<&str> {
        self.func_group_models
            .get(&func_group_id)
            .map(String::as_str)
    }
}
