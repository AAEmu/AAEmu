//! Gimmicks (physics props: elevators, rafts, projectiles):
//!
//! | module | C# |
//! |---|---|
//! | here | `GameData/GimmickGameData` + `Models/Game/Gimmicks/GimmickTemplate` |
//! | [`spawner`] | `Models/Game/Gimmicks/GimmickSpawner` (JSON fields, `gimmick_spawns*.json` parsing) |
//! | [`manager`] | `Core/Managers/GimmickManager` (one per world instance), `Gimmick.GimmickTick`, `GimmickSpawner.Spawn/Despawn` |
//! | [`movement`] | `GimmickMovementHandler`, `GimmickMovementElevator`, `GimmickMovementFloatToSurface`, `Gimmick.MoveAlongZAxis` |
//! | [`packets`] | `SCGimmickMovementPacket` (the visibility packets are in `packets::world_objects`) |
//!
//! The SpawnManager loads `gimmick_spawns*.json` with [`parse_gimmick_spawners`] and spawns each
//! entry with [`spawn_from_spawner`].

pub mod manager;
pub mod movement;
pub mod packets;
pub mod spawner;

use std::collections::HashMap;
use std::sync::Arc;

use crate::data::sqlite::for_each_row;
use crate::net::registry::PacketRegistry;
use crate::server::GameServer;
use crate::systems::DataLoadCtx;

pub use manager::{
    despawn, spawn_from_effect, spawn_from_spawner, ActiveGimmick, GimmickManager,
    SpawnGimmickParams,
};
pub use spawner::{parse_gimmick_spawners, GimmickSpawner};

/// `GimmickTemplate` (`gimmicks` row).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct GimmickTemplate {
    pub id: u32,
    pub air_resistance: f32,
    pub collision_min_speed: f32,
    pub collision_skill_id: u32,
    pub collision_unit_only: bool,
    pub damping: f32,
    pub density: f32,
    pub disappear_by_collision: bool,
    pub fade_in_duration: u32,
    pub fade_out_duration: u32,
    pub free_fall_damping: f32,
    pub graspable: bool,
    pub gravity: f32,
    pub life_time: u32,
    pub mass: f32,
    pub model_path: String,
    pub name: String,
    pub no_ground_collider: bool,
    pub pushable_by_player: bool,
    pub skill_delay: u32,
    pub skill_id: u32,
    pub spawn_delay: u32,
    pub water_damping: f32,
    pub water_density: f32,
    pub water_resistance: f32,
}

/// `GimmickGameData` (`server.game_data.systems.npcs.gimmick`).
#[derive(Debug, Default)]
pub struct GimmickData {
    pub templates: HashMap<u32, GimmickTemplate>,
}

impl GimmickData {
    /// `GimmickGameData.Load`. Booleans use `GetBoolean` (Microsoft.Data.Sqlite `GetInt64 != 0`):
    /// the `'t'`/`'f'` text values of compact.sqlite3 all read as false, as in C#.
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        let mut d = Self::default();
        let mut duplicate = None;
        for_each_row(ctx.conn, "SELECT * FROM gimmicks", [], |r| {
            let t = GimmickTemplate {
                id: r.u32("id")?,
                air_resistance: r.f32("air_resistance")?,
                collision_min_speed: r.f32("collision_min_speed")?,
                collision_skill_id: r.u32_or("collision_skill_id", 0)?,
                collision_unit_only: r.bool("collision_unit_only")?,
                damping: r.f32("damping")?,
                density: r.f32("density")?,
                disappear_by_collision: r.bool("disappear_by_collision")?,
                fade_in_duration: r.u32("fade_in_duration")?,
                fade_out_duration: r.u32("fade_out_duration")?,
                free_fall_damping: r.f32("free_fall_damping")?,
                graspable: r.bool("graspable")?,
                gravity: r.f32("gravity")?,
                life_time: r.u32("life_time")?,
                mass: r.f32("mass")?,
                model_path: r.string("model_path")?,
                name: r.string("name")?,
                no_ground_collider: r.bool("no_ground_collider")?,
                pushable_by_player: r.bool("pushable_by_player")?,
                skill_delay: r.u32("skill_delay")?,
                skill_id: r.u32_or("skill_id", 0)?,
                spawn_delay: r.u32("spawn_delay")?,
                water_damping: r.f32("water_damping")?,
                water_density: r.f32("water_density")?,
                water_resistance: r.f32("water_resistance")?,
            };
            // `_templates.Add` throws on a duplicate id.
            match d.templates.entry(t.id) {
                std::collections::hash_map::Entry::Occupied(_) => {
                    duplicate.get_or_insert(t.id);
                }
                std::collections::hash_map::Entry::Vacant(v) => {
                    v.insert(t);
                }
            }
            Ok(())
        })?;
        if let Some(id) = duplicate {
            return Err(crate::data::GameDataError::File(format!(
                "gimmicks: duplicate template id {id}"
            )));
        }
        tracing::info!("Loaded {} gimmick templates", d.templates.len());
        Ok(d)
    }

    /// `GimmickGameData.Exist`.
    pub fn exist(&self, template_id: u32) -> bool {
        self.templates.contains_key(&template_id)
    }

    /// `GimmickGameData.GetGimmickTemplate`.
    pub fn get_gimmick_template(&self, id: u32) -> Option<&GimmickTemplate> {
        self.templates.get(&id)
    }
}

/// Startup: `GimmickManager.Initialize` (the 50 ms gimmick tick).
pub async fn start(server: &Arc<GameServer>) -> anyhow::Result<()> {
    manager::start_tick(server);
    Ok(())
}

/// C->S packets (none in 1.2).
pub fn register(_reg: &mut PacketRegistry) {}

#[cfg(test)]
mod tests {
    use rusqlite::Connection;

    use super::*;
    use crate::data::client_files::ClientFileManager;

    #[test]
    fn loads_templates_like_csharp() {
        let conn = Connection::open_in_memory().unwrap();
        conn.execute_batch(
            "CREATE TABLE gimmicks (id INT, name TEXT, model_path TEXT, mass REAL, density REAL,
               pushable_by_player NUM, water_damping REAL, water_density REAL, water_resistance REAL,
               skill_id INT, skill_delay INT, life_time INT, collision_min_speed REAL,
               collision_skill_id INT, disappear_by_collision NUM, collision_unit_only NUM,
               spawn_delay INT, fade_in_duration INT, fade_out_duration INT, damping REAL,
               free_fall_damping REAL, graspable NUM, gravity REAL, air_resistance REAL,
               no_ground_collider NUM);
             INSERT INTO gimmicks VALUES (37, 'chest', 'm.cgf', 1.5, 2, 't', 0, 0, 0, NULL, 500,
               60000, 0, 7, 'f', 't', 0, 1, 2, 0, 0, 1, -9.8, 0, 'f');",
        )
        .unwrap();
        let files = ClientFileManager::new();
        let ctx = DataLoadCtx {
            conn: &conn,
            app_path: std::path::Path::new("."),
            files: &files,
            language: "en_us",
        };
        let d = GimmickData::load(&ctx).unwrap();
        let t = d.get_gimmick_template(37).unwrap();
        assert!(d.exist(37) && !d.exist(1));
        assert_eq!(t.skill_id, 0); // NULL -> default
        assert_eq!(t.collision_skill_id, 7);
        assert!(!t.pushable_by_player && !t.collision_unit_only); // 't' -> GetInt64 = 0
        assert!(t.graspable); // integer 1
        assert_eq!((t.life_time, t.skill_delay, t.mass), (60000, 500, 1.5));
        conn.execute_batch("INSERT INTO gimmicks SELECT * FROM gimmicks;")
            .unwrap();
        assert!(GimmickData::load(&ctx).is_err());
    }
}

#[cfg(test)]
mod real_data_tests {
    //! `AAEMU_COMPACT_DB=... cargo test -p aaemu-game real_gimmick -- --ignored --nocapture`.
    use rusqlite::{Connection, OpenFlags};

    use super::*;
    use crate::data::client_files::ClientFileManager;
    use crate::systems::npcs::json;

    #[test]
    #[ignore = "needs AAEMU_COMPACT_DB"]
    fn real_gimmick_tables_and_spawns() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let conn = Connection::open_with_flags(&path, OpenFlags::SQLITE_OPEN_READ_ONLY).unwrap();
        let files = ClientFileManager::new();
        let ctx = DataLoadCtx {
            conn: &conn,
            app_path: std::path::Path::new("."),
            files: &files,
            language: "en_us",
        };
        let d = GimmickData::load(&ctx).unwrap();
        let rows: i64 = conn
            .query_row("SELECT COUNT(*) FROM gimmicks", [], |r| r.get(0))
            .unwrap();
        assert_eq!(d.templates.len() as i64, rows);
        assert!(d.exist(manager::FLOAT_TO_SURFACE_TEMPLATE_ID));
        let app = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("app");
        let text = std::fs::read_to_string(app.join("Data/Worlds/main_world/gimmick_spawns.json"))
            .unwrap();
        let spawners = parse_gimmick_spawners(&json::parse(&text).unwrap()).unwrap();
        let elevators = spawners
            .iter()
            .filter(|s| s.unit_id == 0 && s.entity_guid > 0)
            .count();
        let unknown = spawners
            .iter()
            .filter(|s| s.unit_id != 0 && !d.exist(s.unit_id))
            .count();
        eprintln!(
            "gimmicks {} spawners {} (elevators {elevators}, unknown templates {unknown})",
            d.templates.len(),
            spawners.len()
        );
        assert!(!spawners.is_empty());
        assert!(spawners.iter().all(|s| s.position.is_some()));
    }
}
