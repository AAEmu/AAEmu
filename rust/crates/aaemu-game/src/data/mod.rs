//! Game data: client `Data/compact.sqlite3`, server `Data/*.json` files and the client files
//! (`ClientData` directories / `game_pak`). Ported loaders:
//!
//! * `levels` -> `ExperienceManager`
//! * item templates (`ItemManager.Load`, template part) -> [`items`]
//! * skills / default skills / passive buffs / buff templates (`SkillManager.Load`, subset) ->
//!   [`skills`]
//! * character templates, starting kits, appellations, actabilities, expert limits
//!   (`CharacterManager.Load`) -> [`character_templates`]
//! * factions + localization, zones, recall portals -> [`world_data`]
//! * unit / wearable formulas (`FormulaManager`) -> [`formulas`]
//! * world templates from `game/worlds/*/world.xml` (`WorldManager.Load`) -> [`world_templates`]
//!   via [`client_files`] / [`pak`]
//!
//! As in the 1.2 C# (no tolerant loaders), a failing table aborts startup.

pub mod character_templates;
pub mod client_files;
pub mod experience;
pub mod features;
pub mod formulas;
pub mod items;
pub mod pak;
pub mod skills;
pub mod sqlite;
pub mod world_data;
pub mod world_templates;

use std::path::Path;

use rusqlite::{Connection, OpenFlags};

pub use character_templates::CharacterData;
pub use experience::{ExperienceError, ExperienceTable, LevelTemplate};
pub use features::{Feature, FeatureSet};
pub use formulas::FormulaManager;
pub use items::{ItemData, ItemTemplate};
pub use skills::SkillData;
pub use world_data::{FactionData, Localization, PortalData, ZoneData};
pub use world_templates::{WorldTemplate, WorldTemplates};

/// Failure while loading game data.
#[derive(Debug, thiserror::Error)]
pub enum GameDataError {
    #[error(transparent)]
    Sqlite(#[from] rusqlite::Error),
    #[error(transparent)]
    Experience(#[from] ExperienceError),
    #[error(transparent)]
    World(#[from] world_templates::WorldError),
    #[error(transparent)]
    Formula(#[from] formulas::FormulaLoadError),
    #[error("{0}")]
    File(String),
}

#[derive(Debug, Default)]
pub struct GameData {
    /// `ExperienceManager`.
    pub experience: ExperienceTable,
    /// `ItemManager` templates.
    pub items: ItemData,
    /// `FeaturesManager.Fsets`.
    pub features: FeatureSet,
    pub skills: SkillData,
    pub characters: CharacterData,
    pub factions: FactionData,
    pub zones: ZoneData,
    pub portals: PortalData,
    pub formulas: FormulaManager,
    pub worlds: WorldTemplates,
}

impl GameData {
    /// Loads everything from `<app_path>/Data/compact.sqlite3` (read-only), the server data files
    /// and the client data sources (`ClientData.Sources`).
    pub fn load(
        app_path: &Path,
        client_sources: &[String],
        language: &str,
    ) -> Result<Self, GameDataError> {
        let path = app_path.join("Data").join("compact.sqlite3");
        let conn = Connection::open_with_flags(&path, OpenFlags::SQLITE_OPEN_READ_ONLY)?;

        let experience = ExperienceTable::from_templates(load_levels(&conn)?)?;
        let items = ItemData::load(&conn)?;
        let localization = Localization::load(&conn, language)?;
        let factions = FactionData::load(&conn, &localization)?;
        let zones = ZoneData::load(&conn, chrono::Utc::now().timestamp())?;
        let skills = SkillData::load(&conn)?;
        let formulas = FormulaManager::load(&conn)?;

        let files = client_files::ClientFileManager::from_sources(client_sources, app_path);
        let worlds = WorldTemplates::load(&files, app_path)?;
        let main_world = worlds.get_world_template_by_name("main_world");
        let characters = CharacterData::load(
            &conn,
            app_path,
            |x, y| worlds.get_zone_id(main_world, x, y),
            worlds.default_world_template_id(),
        )?;
        let portals = PortalData::load(&conn, app_path, &localization)?;

        let data = GameData {
            features: FeatureSet::initialized(
                experience.max_player_level(),
                experience.max_mate_level(),
            ),
            experience,
            items,
            skills,
            characters,
            factions,
            zones,
            portals,
            formulas,
            worlds,
        };
        tracing::info!(
            "Game data loaded: max level {}/{}, {} item templates, {} worlds, fsets {}",
            data.experience.max_player_level(),
            data.experience.max_mate_level(),
            data.items.templates.len(),
            data.worlds.templates().len(),
            data.features
        );
        Ok(data)
    }

    /// Game data with no tables loaded (unit tests): level caps 0, no templates.
    pub fn empty() -> Self {
        GameData {
            features: FeatureSet::initialized(0, 0),
            ..GameData::default()
        }
    }

    pub fn item_template(&self, id: u32) -> Option<&ItemTemplate> {
        self.items.template(id)
    }
}

/// `SqliteExperienceLevelTemplateLoader.Load` (`SELECT * FROM levels ORDER BY id ASC`).
fn load_levels(conn: &Connection) -> Result<Vec<LevelTemplate>, rusqlite::Error> {
    let mut stmt = conn.prepare(
        "SELECT id, total_exp, total_mate_exp, skill_points FROM levels ORDER BY id ASC",
    )?;
    let rows = stmt.query_map([], |r| {
        Ok(LevelTemplate {
            level: r.get::<_, i64>(0)? as u8,
            total_exp: r.get::<_, i64>(1)? as i32,
            total_mate_exp: r.get::<_, i64>(2)? as i32,
            skill_points: r.get::<_, i64>(3)? as i32,
        })
    })?;
    rows.collect()
}

#[cfg(test)]
mod real_db_tests {
    //! Loads the real 1.2 `compact.sqlite3` when `AAEMU_COMPACT_DB` points at it
    //! (`cargo test -p aaemu-game real_compact_db -- --ignored`).
    use super::*;

    #[test]
    #[ignore = "needs AAEMU_COMPACT_DB"]
    fn real_compact_db() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let conn = Connection::open_with_flags(&path, OpenFlags::SQLITE_OPEN_READ_ONLY).unwrap();
        let exp = ExperienceTable::from_templates(load_levels(&conn).unwrap()).unwrap();
        let items = ItemData::load(&conn).unwrap();
        let loc = Localization::load(&conn, "en_us").unwrap();
        let factions = FactionData::load(&conn, &loc).unwrap();
        let zones = ZoneData::load(&conn, 0).unwrap();
        let skills = SkillData::load(&conn).unwrap();
        let formulas = FormulaManager::load(&conn).unwrap();
        let app = Path::new(&path).parent().unwrap().parent().unwrap();
        let chars = CharacterData::load(&conn, app, |_, _| 0, 0).unwrap();
        let portals = PortalData::load(&conn, app, &loc).unwrap();
        let stats = crate::models::stats::CharacterStats::compute(&formulas, 1);
        let stats50 = crate::models::stats::CharacterStats::compute(&formulas, 50);
        eprintln!(
            "levels {} items {} grades {} factions {} relations {} zones {} groups {} conflicts {} skills default {} passive {} buffs {} templates {} ability kits {} actabilities {} recalls {} stats1 {:?} stats50 {:?}",
            exp.max_player_level(),
            items.templates.len(),
            items.grades.len(),
            factions.factions.len(),
            factions.relations.len(),
            zones.zones.len(),
            zones.groups.len(),
            zones.conflicts.len(),
            skills.default_skills.len(),
            skills.passive_buffs.len(),
            skills.buffs.len(),
            chars.templates.len(),
            chars.ability_items.len(),
            chars.actabilities.len(),
            portals.recalls.len(),
            stats,
            stats50,
        );
        assert!(items.grade(0).is_some());
        assert!(skills.buff(8_000_011).is_some() && skills.buff(8_000_012).is_some());
        assert!(skills.buff(crate::models::buff::BUFF_LOGGED_ON).is_some());
        assert!(stats.max_hp > 0 && stats.max_mp > 0);
    }
}
