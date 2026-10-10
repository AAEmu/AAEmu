//! `Utils/DB/HoldablesSchemaCheck`: makes sure `holdables` in `compact.sqlite3` has the six
//! auto-attack animation columns (adds missing ones as `INT DEFAULT 0`). Best effort: every
//! failure is logged and startup continues.

use std::collections::HashSet;
use std::path::Path;

use rusqlite::{Connection, OpenFlags};

/// `RequiredColumns`.
pub const REQUIRED_COLUMNS: [&str; 6] = [
    "anim_r1_id",
    "anim_l1_id",
    "anim_r2_id",
    "anim_l2_id",
    "anim_r3_id",
    "anim_l3_id",
];

/// `EnsureColumns(directory = "Data", sqlite = "compact.sqlite3")` relative to the app path.
/// Returns the columns that were added.
pub fn ensure_columns(app_path: &Path) -> Vec<String> {
    let db_path = app_path.join("Data").join("compact.sqlite3");
    if !db_path.exists() {
        tracing::warn!(
            "HoldablesSchemaCheck: {} not found — skipping.",
            db_path.display()
        );
        return Vec::new();
    }
    match ensure_columns_at(&db_path) {
        Ok(added) => added,
        Err(e) => {
            tracing::error!(
                "HoldablesSchemaCheck failed — server will continue without migration: {e}"
            );
            Vec::new()
        }
    }
}

/// [`ensure_columns`] for an explicit database file.
pub fn ensure_columns_at(db_path: &Path) -> rusqlite::Result<Vec<String>> {
    // Phase 1: read-only scan.
    let existing: HashSet<String> = {
        let ro = Connection::open_with_flags(db_path, OpenFlags::SQLITE_OPEN_READ_ONLY)?;
        let mut stmt = ro.prepare("PRAGMA table_info(holdables);")?;
        let names = stmt.query_map([], |r| r.get::<_, String>(1))?;
        names
            .filter_map(Result::ok)
            .map(|n| n.to_lowercase())
            .collect()
    };
    let missing: Vec<&str> = REQUIRED_COLUMNS
        .iter()
        .copied()
        .filter(|c| !existing.contains(*c))
        .collect();
    if missing.is_empty() {
        tracing::info!("HoldablesSchemaCheck: all 6 anim columns present, no migration needed.");
        return Ok(Vec::new());
    }
    tracing::warn!(
        "HoldablesSchemaCheck: {} missing column(s) — adding: {}",
        missing.len(),
        missing.join(", ")
    );
    // Phase 2: read-write ALTER TABLE.
    let rw = Connection::open_with_flags(db_path, OpenFlags::SQLITE_OPEN_READ_WRITE)?;
    let mut added = Vec::new();
    for col in missing {
        match rw.execute(
            &format!("ALTER TABLE holdables ADD COLUMN {col} INT DEFAULT 0;"),
            [],
        ) {
            Ok(_) => {
                tracing::info!("  + added column {col}");
                added.push(col.to_string());
            }
            Err(e) => tracing::warn!("  ! failed to add {col}: {e}"),
        }
    }
    tracing::info!(
        "HoldablesSchemaCheck: migration complete. Anim IDs default to 0 — auto-attack will fall back to fist animations until real values are set."
    );
    Ok(added)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn adds_missing_columns_once() {
        let dir = std::env::temp_dir().join(format!("aaemu-holdables-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let db = dir.join("compact.sqlite3");
        let _ = std::fs::remove_file(&db);
        {
            let c = Connection::open(&db).unwrap();
            c.execute("CREATE TABLE holdables (id INT, anim_r1_id INT)", [])
                .unwrap();
        }
        let added = ensure_columns_at(&db).unwrap();
        assert_eq!(added.len(), 5);
        assert!(ensure_columns_at(&db).unwrap().is_empty());
        std::fs::remove_dir_all(&dir).ok();
    }

    #[test]
    fn real_compact_db_has_the_columns() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let ro = Connection::open_with_flags(&path, OpenFlags::SQLITE_OPEN_READ_ONLY).unwrap();
        let mut stmt = ro.prepare("PRAGMA table_info(holdables);").unwrap();
        let names: HashSet<String> = stmt
            .query_map([], |r| r.get::<_, String>(1))
            .unwrap()
            .filter_map(Result::ok)
            .collect();
        for c in REQUIRED_COLUMNS {
            assert!(names.contains(c), "{c}");
        }
    }
}
