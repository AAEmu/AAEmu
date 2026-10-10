//! The `LocalizationManager` lookups the mail code needs (`items.name` for auction / trade
//! pack mails, `quest_contexts.name` for quest reward mails), loaded from `localized_texts` in
//! `DefaultLanguage` like `LocalizationManager.Load` (first row of a key wins, `TryAdd`).

use std::collections::HashMap;

use crate::data::sqlite::for_each_row;
use crate::data::GameDataError;
use crate::systems::DataLoadCtx;

/// Mail game data (`server.game_data.systems.economy.mail`).
#[derive(Debug, Default)]
pub struct MailData {
    /// `items` / `name` translations by template id.
    pub item_names: HashMap<u32, String>,
    /// `quest_contexts` / `name` translations by quest template id.
    pub quest_names: HashMap<u32, String>,
}

impl MailData {
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, GameDataError> {
        let mut d = Self::default();
        // The column name comes from the configuration; quote it as an identifier.
        let sql = format!(
            "SELECT tbl_name, idx, \"{}\" AS txt FROM localized_texts \
             WHERE tbl_column_name = 'name' AND tbl_name IN ('items', 'quest_contexts')",
            ctx.language.replace('"', "")
        );
        for_each_row(ctx.conn, &sql, [], |r| {
            let tbl = r.string("tbl_name")?;
            let idx = r.i64("idx")? as u32;
            let text = r.string_or("txt", "")?;
            let map = if tbl == "items" {
                &mut d.item_names
            } else {
                &mut d.quest_names
            };
            map.entry(idx).or_insert(text);
            Ok(())
        })?;
        Ok(d)
    }

    /// `LocalizationManager.Get("items", "name", templateId, fallback)` (empty -> fallback).
    pub fn item_name(&self, template_id: u32, fallback: &str) -> String {
        get(&self.item_names, template_id, fallback)
    }

    /// `LocalizationManager.Get("quest_contexts", "name", questId, fallback)`.
    pub fn quest_name(&self, quest_id: u32, fallback: &str) -> String {
        get(&self.quest_names, quest_id, fallback)
    }
}

fn get(map: &HashMap<u32, String>, idx: u32, fallback: &str) -> String {
    match map.get(&idx) {
        Some(t) if !t.is_empty() => t.clone(),
        _ => fallback.to_string(),
    }
}
