//! Client game data (`Data/compact.sqlite3`) — only the tables the lobby flow needs:
//!
//! * `levels` -> `ExperienceManager` (level caps for the fsets, `ValidateAndFixExpAndLevel` on
//!   character load)
//! * `items.bind_id / fixed_grade / gradable` -> item grade + `EquipItem.WriteDetails` time field
//!
//! As in the 1.2 C# (no tolerant loaders), a failing table aborts startup. Every other
//! `GameData` loader / manager `Load()` is a later phase (see PORTING.md).

pub mod experience;
pub mod features;

use std::collections::HashMap;
use std::path::Path;

use rusqlite::{Connection, OpenFlags};

pub use experience::{ExperienceError, ExperienceTable, LevelTemplate};
pub use features::{Feature, FeatureSet};

/// The parts of `ItemTemplate` that affect the character-list wire format.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ItemTemplateInfo {
    /// `bind_id` (`ItemBindType`).
    pub bind_type: u32,
    pub fixed_grade: i32,
    pub gradable: bool,
}

/// Failure while loading `compact.sqlite3`.
#[derive(Debug, thiserror::Error)]
pub enum GameDataError {
    #[error(transparent)]
    Sqlite(#[from] rusqlite::Error),
    #[error(transparent)]
    Experience(#[from] ExperienceError),
}

#[derive(Debug, Default)]
pub struct GameData {
    /// `ExperienceManager`.
    pub experience: ExperienceTable,
    pub item_templates: HashMap<u32, ItemTemplateInfo>,
    /// `FeaturesManager.Fsets`.
    pub features: FeatureSet,
}

impl GameData {
    /// Loads from `<app_path>/Data/compact.sqlite3` (read-only), like `SQLite.CreateConnection()`.
    pub fn load(app_path: &Path) -> Result<Self, GameDataError> {
        let path = app_path.join("Data").join("compact.sqlite3");
        let conn = Connection::open_with_flags(&path, OpenFlags::SQLITE_OPEN_READ_ONLY)?;

        let experience = ExperienceTable::from_templates(load_levels(&conn)?)?;
        let item_templates = load_item_templates(&conn)?;
        let data = GameData {
            features: FeatureSet::initialized(
                experience.max_player_level(),
                experience.max_mate_level(),
            ),
            experience,
            item_templates,
        };
        tracing::info!(
            "Game data loaded: max level {}/{}, {} item templates, fsets {}",
            data.experience.max_player_level(),
            data.experience.max_mate_level(),
            data.item_templates.len(),
            data.features
        );
        Ok(data)
    }

    /// Game data with no tables loaded (unit tests): level caps 0, no item templates.
    pub fn empty() -> Self {
        GameData {
            features: FeatureSet::initialized(0, 0),
            ..GameData::default()
        }
    }

    pub fn item_template(&self, id: u32) -> Option<&ItemTemplateInfo> {
        self.item_templates.get(&id)
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

fn load_item_templates(
    conn: &Connection,
) -> Result<HashMap<u32, ItemTemplateInfo>, rusqlite::Error> {
    let mut stmt = conn.prepare("SELECT id, bind_id, fixed_grade, gradable FROM items")?;
    let rows = stmt.query_map([], |r| {
        let id: i64 = r.get(0)?;
        let bind: i64 = r.get(1)?;
        let fixed: i64 = r.get(2)?;
        let gradable = sqlite_bool(r.get_ref(3)?);
        Ok((
            id as u32,
            ItemTemplateInfo {
                bind_type: bind as u32,
                fixed_grade: fixed as i32,
                gradable,
            },
        ))
    })?;
    rows.collect()
}

/// `SQLiteWrapperReader.GetBoolean(column, fromString: true)`: NULL -> false, else `"t"` or `"1"`.
fn sqlite_bool(v: rusqlite::types::ValueRef<'_>) -> bool {
    use rusqlite::types::ValueRef;
    match v {
        ValueRef::Null => false,
        ValueRef::Integer(i) => i == 1,
        ValueRef::Real(_) => false, // GetString yields "1.0"-style text, never "t"/"1"
        ValueRef::Text(t) | ValueRef::Blob(t) => t == b"t" || t == b"1",
    }
}
