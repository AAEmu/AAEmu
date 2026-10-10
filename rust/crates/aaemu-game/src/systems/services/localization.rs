//! `Core/Managers/LocalizationManager` (complete): every `localized_texts` row in the
//! configured `DefaultLanguage`, plus the item-name cache of all languages used by
//! `MatchItemName` (auction / item searches). `data::world_data::Localization` keeps the small
//! phase-2 subset the faction / portal loaders read before the subsystems load.

use std::collections::HashMap;

use rusqlite::Connection;

use crate::data::sqlite::for_each_row;

/// `LocalizationManager`.
#[derive(Debug, Clone, Default)]
pub struct LocalizationManager {
    /// `_translations` (`tbl:column:idx` -> text).
    translations: HashMap<(String, String, i64), String>,
    /// `ItemNameCache` (locale -> item template id -> lower-cased name), locales in column
    /// order.
    item_name_cache: Vec<(String, HashMap<i64, String>)>,
}

impl LocalizationManager {
    /// `Load()`.
    pub fn load(conn: &Connection, default_language: &str) -> rusqlite::Result<Self> {
        tracing::info!("Loading translations ...");
        let mut m = Self::default();
        // The locale columns (everything but the keys and `*_ver`).
        let stmt = conn.prepare("SELECT * FROM localized_texts LIMIT 0")?;
        for c in stmt.column_names() {
            if matches!(c, "id" | "tbl_name" | "tbl_column_name" | "idx") || c.ends_with("_ver") {
                continue;
            }
            m.item_name_cache.push((c.to_string(), HashMap::new()));
        }
        drop(stmt);
        let locales: Vec<String> = m.item_name_cache.iter().map(|(l, _)| l.clone()).collect();
        let mut duplicates = Vec::new();
        for_each_row(conn, "SELECT * FROM localized_texts", [], |r| {
            let tbl = r.string("tbl_name")?;
            let column = r.string("tbl_column_name")?;
            let idx = r.i64("idx")?;
            let text = r.string(default_language)?;
            let is_item_name = tbl == "items" && column == "name";
            if let Some(dup) = m.add_translation(tbl, column, idx, text) {
                duplicates.push(dup);
            }
            if is_item_name {
                for (i, locale) in locales.iter().enumerate() {
                    let name = r.string(locale)?.to_lowercase();
                    if name.trim().is_empty() {
                        continue;
                    }
                    m.item_name_cache[i].1.insert(idx, name);
                }
            }
            Ok(())
        })?;
        for d in duplicates {
            tracing::error!("Failed to add translation: {d}");
        }
        tracing::info!(
            "Loaded {} translations in {default_language} ...",
            m.translations.len()
        );
        Ok(m)
    }

    /// `AddTranslation(tbl, column, idx, value)`: the first value of a key wins; returns the
    /// key of a rejected duplicate.
    pub fn add_translation(
        &mut self,
        tbl: String,
        column: String,
        idx: i64,
        value: String,
    ) -> Option<String> {
        let key = (tbl, column, idx);
        if self.translations.contains_key(&key) {
            return Some(format!("{}:{}:{}", key.0, key.1, key.2));
        }
        self.translations.insert(key, value);
        None
    }

    /// `Get(tblName, tblColumn, index, fallbackValue)`: empty translations fall back too.
    pub fn get(&self, tbl: &str, column: &str, idx: i64, fallback: &str) -> String {
        match self
            .translations
            .get(&(tbl.to_string(), column.to_string(), idx))
        {
            Some(t) if !t.is_empty() => t.clone(),
            _ => fallback.to_string(),
        }
    }

    pub fn len(&self) -> usize {
        self.translations.len()
    }

    pub fn is_empty(&self) -> bool {
        self.translations.is_empty()
    }

    /// `MatchItemName(itemTemplateId, keywords, exactMatch)`: any language's item name equals
    /// the lower-cased keywords (exact) or contains every word (partial).
    pub fn match_item_name(
        &self,
        item_template_id: i64,
        keywords: &str,
        exact_match: bool,
    ) -> bool {
        if keywords.trim().is_empty() {
            return false;
        }
        let k = keywords.to_lowercase();
        let words: Vec<&str> = k.split(' ').filter(|w| !w.trim().is_empty()).collect();
        for (_, names) in &self.item_name_cache {
            let Some(name) = names.get(&item_template_id) else {
                continue;
            };
            if name.trim().is_empty() {
                continue;
            }
            if exact_match {
                if *name == k {
                    return true;
                }
                continue;
            }
            if words.iter().all(|w| name.contains(w)) {
                return true;
            }
        }
        false
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn db() -> Connection {
        let c = Connection::open_in_memory().unwrap();
        c.execute_batch(
            "CREATE TABLE localized_texts (id INT, tbl_name TEXT, tbl_column_name TEXT, idx INT,
               ko TEXT, ko_ver INT, en_us TEXT, en_us_ver INT, de TEXT, de_ver INT);
             INSERT INTO localized_texts VALUES
               (1, 'items', 'name', 10, '검', 1, 'Iron Sword', 1, 'Eisenschwert', 1),
               (2, 'npcs', 'name', 5, '', 1, '', 1, 'x', 1),
               (3, 'npcs', 'name', 5, '', 1, 'dup', 1, 'x', 1),
               (4, 'zones', 'name', 1, 'z', 1, 'Zone', 1, '', 1);",
        )
        .unwrap();
        c
    }

    #[test]
    fn get_and_match() {
        let l = LocalizationManager::load(&db(), "en_us").unwrap();
        assert_eq!(l.len(), 3);
        assert_eq!(l.get("items", "name", 10, "?"), "Iron Sword");
        // Empty translation (first row wins over the duplicate) -> fallback.
        assert_eq!(l.get("npcs", "name", 5, "fb"), "fb");
        assert_eq!(l.get("nope", "name", 5, "fb"), "fb");
        assert!(l.match_item_name(10, "iron sword", true));
        assert!(l.match_item_name(10, "SWORD iron", false));
        assert!(l.match_item_name(10, "eisen", false));
        assert!(!l.match_item_name(10, "sword x", false));
        assert!(!l.match_item_name(10, "  ", false));
        assert!(!l.match_item_name(11, "sword", false));
        let de = LocalizationManager::load(&db(), "de").unwrap();
        assert_eq!(de.get("zones", "name", 1, "fb"), "fb");
    }

    #[test]
    fn real_compact_db_translations() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let c =
            Connection::open_with_flags(&path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY).unwrap();
        let l = LocalizationManager::load(&c, "en_us").unwrap();
        assert!(l.len() > 200_000, "{}", l.len());
        assert_eq!(l.item_name_cache.len(), 8);
    }
}
