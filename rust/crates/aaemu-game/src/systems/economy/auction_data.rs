//! The localization data the auction house needs (`LocalizationManager`'s item-name parts):
//! `ItemNameCache` (every locale's lower-cased `items.name`, for `MatchItemName`) and the
//! default-language `items.name` texts (`LocalizationManager.Get("items", "name", id, fallback)`,
//! used by `MailForAuction` for the item name).
//!
//! NOTE (dedup): the mail / item-search helpers may load the same `localized_texts` rows; the
//! coordinator can merge these into one localization table later.

use std::collections::HashMap;

use crate::data::sqlite::for_each_row;
use crate::data::GameDataError;
use crate::systems::DataLoadCtx;

/// `LocalizationManager` item-name data.
#[derive(Debug, Clone, Default)]
pub struct AuctionData {
    /// `ItemNameCache`: (locale column, item template id -> lower-cased name), in column order.
    pub item_name_cache: Vec<(String, HashMap<i64, String>)>,
    /// `_translations` restricted to `items:name:*` in `DefaultLanguage`.
    pub item_names: HashMap<i64, String>,
}

impl AuctionData {
    /// The item-name part of `LocalizationManager.Load`.
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, GameDataError> {
        let mut data = Self::default();
        let locales: Vec<String> = {
            let stmt = ctx.conn.prepare("SELECT * FROM localized_texts LIMIT 0")?;
            stmt.column_names()
                .iter()
                .filter(|c| {
                    !matches!(**c, "id" | "tbl_name" | "tbl_column_name" | "idx")
                        && !c.ends_with("_ver")
                })
                .map(|c| c.to_string())
                .collect()
        };
        data.item_name_cache = locales
            .iter()
            .map(|l| (l.clone(), HashMap::new()))
            .collect();
        for_each_row(
            ctx.conn,
            "SELECT * FROM localized_texts WHERE tbl_name = 'items' AND tbl_column_name = 'name'",
            [],
            |r| {
                let idx = r.i64("idx")?;
                if r.has(ctx.language) {
                    let default_text = r.string_or(ctx.language, "")?;
                    data.item_names.entry(idx).or_insert(default_text);
                }
                for (locale, group) in data.item_name_cache.iter_mut() {
                    let translated = r.string_or(locale, "")?.to_lowercase();
                    if translated.trim().is_empty() {
                        continue;
                    }
                    group.insert(idx, translated);
                }
                Ok(())
            },
        )?;
        Ok(data)
    }

    /// `LocalizationManager.Get("items", "name", templateId, fallback)`.
    pub fn item_name(&self, template_id: u32, fallback: &str) -> String {
        match self.item_names.get(&i64::from(template_id)) {
            Some(t) if !t.is_empty() => t.clone(),
            _ => fallback.to_string(),
        }
    }

    /// `LocalizationManager.MatchItemName(itemTemplateId, keywords, exactMatch)`: checks the
    /// keywords against the item's name in any locale.
    pub fn match_item_name(&self, template_id: i64, keywords: &str, exact_match: bool) -> bool {
        // Empty Check
        if keywords.trim().is_empty() {
            return false;
        }
        // Lowercase
        let k = keywords.to_lowercase();
        // Split words for partial checks
        let search_words: Vec<&str> = k.split(' ').filter(|w| !w.trim().is_empty()).collect();

        for (_, item_list) in &self.item_name_cache {
            let Some(localized_name) = item_list.get(&template_id) else {
                continue;
            };
            // Skip empty translations
            if localized_name.trim().is_empty() {
                continue;
            }
            // Exact match check
            if exact_match {
                if *localized_name == k {
                    return true;
                }
                continue;
            }
            // Partial match check (with individual words)
            if search_words.iter().all(|w| localized_name.contains(w)) {
                return true;
            }
        }
        false
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn data() -> AuctionData {
        let mut en = HashMap::new();
        en.insert(10, "iron sword".to_string());
        en.insert(11, "   ".to_string());
        let mut de = HashMap::new();
        de.insert(10, "eisenschwert".to_string());
        AuctionData {
            item_name_cache: vec![("en_us".into(), en), ("de".into(), de)],
            item_names: HashMap::new(),
        }
    }

    #[test]
    fn match_item_name_mirrors_csharp() {
        let d = data();
        assert!(d.match_item_name(10, "Iron", false));
        assert!(d.match_item_name(10, "sword  iron", false));
        assert!(!d.match_item_name(10, "iron axe", false));
        assert!(d.match_item_name(10, "EISEN", false));
        assert!(d.match_item_name(10, "Iron Sword", true));
        assert!(!d.match_item_name(10, "iron", true));
        assert!(!d.match_item_name(10, "  ", false));
        assert!(!d.match_item_name(11, "a", false));
        assert!(!d.match_item_name(12, "iron", false));
        assert_eq!(d.item_name(5, "Item:5"), "Item:5");
    }

    #[test]
    fn real_compact_db_item_names() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let conn =
            rusqlite::Connection::open_with_flags(path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
                .unwrap();
        let files = crate::data::client_files::ClientFileManager::default();
        let ctx = DataLoadCtx {
            conn: &conn,
            app_path: std::path::Path::new("."),
            files: &files,
            language: "en_us",
        };
        let d = AuctionData::load(&ctx).unwrap();
        let locales: Vec<&str> = d.item_name_cache.iter().map(|(l, _)| l.as_str()).collect();
        eprintln!(
            "locales {locales:?}, en names {}, cache sizes {:?}",
            d.item_names.len(),
            d.item_name_cache
                .iter()
                .map(|(_, m)| m.len())
                .collect::<Vec<_>>()
        );
        assert!(locales.contains(&"en_us"));
        assert!(d.item_names.len() > 10_000);
    }
}
