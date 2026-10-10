//! Port of `Core/Managers/NameManager` and `StringExtensions.NormalizeName`.

use std::collections::HashMap;
use std::sync::RwLock;

use regex::Regex;
use sqlx::mysql::MySqlPool;
use sqlx::Row;

/// `CharacterCreateError`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum CharacterCreateError {
    Ok = 0,
    /// Also generates "Name is pending deletion".
    Failed = 3,
    NameAlreadyExists = 4,
    InvalidCharacters = 5,
}

/// `NameManager.DefaultCharacterNameRegexPattern`.
pub const DEFAULT_CHARACTER_NAME_REGEX: &str = "^[a-zA-Z0-9а-яА-Я]{1,18}$";

/// `string.NormalizeName()`: trim, first char upper case, the rest lower case. A blank input is
/// returned unchanged. (C# uses `char.ToUpper` / current-culture `ToLower` per UTF-16 unit; Rust
/// case mapping can expand a few special characters, which the name regex rejects anyway.)
pub fn normalize_name(input: &str) -> String {
    let trimmed = input.trim();
    let mut chars = trimmed.chars();
    match chars.next() {
        None => input.to_string(),
        Some(first) => {
            let mut out: String = first.to_uppercase().collect();
            out.extend(chars.flat_map(char::to_lowercase));
            out
        }
    }
}

#[derive(Debug, Default)]
struct Names {
    ids: HashMap<u32, String>,
    names: HashMap<String, u32>,
    accounts: HashMap<u32, u32>,
}

/// `NameManager`.
#[derive(Debug)]
pub struct NameManager {
    regex: Regex,
    inner: RwLock<Names>,
}

impl Default for NameManager {
    fn default() -> Self {
        Self::new(None)
    }
}

impl NameManager {
    /// `CharacterNameRegex` from the configuration (`None` = the default pattern).
    pub fn new(custom_regex: Option<&str>) -> Self {
        let regex = custom_regex
            .and_then(|r| match Regex::new(r) {
                Ok(r) => Some(r),
                Err(e) => {
                    tracing::error!("Invalid CharacterNameRegex {r}: {e}");
                    None
                }
            })
            .unwrap_or_else(|| Regex::new(DEFAULT_CHARACTER_NAME_REGEX).expect("valid regex"));
        Self {
            regex,
            inner: RwLock::new(Names::default()),
        }
    }

    /// `Load()`: `SELECT id, name, account_id, deleted FROM characters`. Deleted names keep their
    /// id/account registration but not the name -> id mapping.
    pub async fn load(&self, pool: &MySqlPool) -> crate::Result<()> {
        let rows = sqlx::query(
            "SELECT CAST(id AS UNSIGNED) AS id, name, CAST(account_id AS UNSIGNED) AS account_id, CAST(deleted AS SIGNED) AS deleted FROM characters",
        )
        .fetch_all(pool)
        .await?;
        let mut n = self.inner.write().expect("names poisoned");
        for r in rows {
            let id = r.try_get::<u64, _>("id")? as u32;
            let name: String = r.try_get("name")?;
            let account = r.try_get::<u64, _>("account_id")? as u32;
            let deleted: i64 = r.try_get("deleted")?;
            let normalized = normalize_name(&name.to_lowercase());
            // C# Dictionary.Add throws on duplicates here (fatal at startup).
            if n.ids.insert(id, normalized.clone()).is_some() {
                return Err(crate::GameError::Protocol(format!(
                    "NameManager: duplicate character id {id}"
                )));
            }
            if deleted == 0 && n.names.insert(normalized.clone(), id).is_some() {
                return Err(crate::GameError::Protocol(format!(
                    "NameManager: duplicate character name {normalized}"
                )));
            }
            n.accounts.insert(id, account);
        }
        tracing::info!("Loaded {} character names", n.ids.len());
        Ok(())
    }

    pub fn get_character_name(&self, id: u32) -> Option<String> {
        self.inner
            .read()
            .expect("names poisoned")
            .ids
            .get(&id)
            .cloned()
    }

    pub fn get_character_id(&self, normalized_name: &str) -> u32 {
        self.inner
            .read()
            .expect("names poisoned")
            .names
            .get(normalized_name)
            .copied()
            .unwrap_or(0)
    }

    pub fn get_character_account(&self, id: u32) -> u32 {
        self.inner
            .read()
            .expect("names poisoned")
            .accounts
            .get(&id)
            .copied()
            .unwrap_or(0)
    }

    pub fn name_exists(&self, name: &str) -> bool {
        self.inner
            .read()
            .expect("names poisoned")
            .names
            .contains_key(name)
    }

    /// `ValidateCharacterName`. `pending_deletion` is `CharacterManager.IsCharacterPendingDeletion`
    /// for an existing name (only evaluated in that case).
    pub fn validate_character_name(
        &self,
        name: &str,
        pending_deletion: impl FnOnce() -> bool,
    ) -> CharacterCreateError {
        if self.name_exists(name) {
            if pending_deletion() {
                return CharacterCreateError::Failed;
            }
            return CharacterCreateError::NameAlreadyExists;
        }
        if name.trim().is_empty() || !self.regex.is_match(name) {
            return CharacterCreateError::InvalidCharacters;
        }
        CharacterCreateError::Ok
    }

    /// `AddCharacter`.
    pub fn add_character(&self, id: u32, name: &str, account_id: u32) {
        let normalized = normalize_name(name);
        let mut n = self.inner.write().expect("names poisoned");
        if let Some(old) = n.ids.get(&id) {
            if !old.eq_ignore_ascii_case(name) {
                tracing::error!("AddCharacterName, failed to register name for {name} ({id}), Account {account_id}, OldName {old}");
            }
        } else {
            n.ids.insert(id, normalized.clone());
            tracing::info!("AddCharacterName, Registered character name {name} ({id})");
        }
        if let Some(&old) = n.names.get(&normalized) {
            if old != id {
                tracing::error!("AddCharacterName, failed to register id for {name} ({id}), Account {account_id}, OldId {old}");
            }
        } else {
            n.names.insert(normalized, id);
            tracing::info!("AddCharacterName, Registered character id {name} ({id})");
        }
        if let Some(&old) = n.accounts.get(&id) {
            if old != account_id {
                tracing::error!("AddCharacterName, failed to register account for {name} ({id}), Account {account_id}, OldAccount {old}");
            }
        } else {
            n.accounts.insert(id, account_id);
            tracing::info!("AddCharacterName, Registered account {account_id} for {name} ({id})");
        }
    }

    /// `RemoveCharacterId`.
    pub fn remove_character_id(&self, id: u32) {
        let mut n = self.inner.write().expect("names poisoned");
        if let Some(name) = n.ids.remove(&id) {
            n.names.remove(&name);
            tracing::info!(
                "AddCharacterName, Remove name and id registrations for character Id {id}"
            );
        } else {
            tracing::error!("AddCharacterName, No name was registered for character Id {id}");
        }
        if n.accounts.remove(&id).is_some() {
            tracing::info!("AddCharacterName, Removed account registration for character Id {id}");
        } else {
            tracing::error!("AddCharacterName, No account was registered for character Id {id}");
        }
    }

    /// `NoNamesRegistered`.
    pub fn no_names_registered(&self) -> bool {
        self.inner.read().expect("names poisoned").ids.is_empty()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn normalize() {
        assert_eq!(normalize_name("  hELLo "), "Hello");
        assert_eq!(normalize_name("   "), "   ");
        assert_eq!(normalize_name("иван"), "Иван");
    }

    #[test]
    fn validation_follows_csharp_order() {
        let m = NameManager::default();
        m.add_character(1, "Taken", 10);
        assert_eq!(
            m.validate_character_name("Taken", || false),
            CharacterCreateError::NameAlreadyExists
        );
        assert_eq!(
            m.validate_character_name("Taken", || true),
            CharacterCreateError::Failed
        );
        assert_eq!(
            m.validate_character_name("Bad name", || false),
            CharacterCreateError::InvalidCharacters
        );
        assert_eq!(
            m.validate_character_name("Abcdefghijklmnopqrs", || false),
            CharacterCreateError::InvalidCharacters
        );
        assert_eq!(
            m.validate_character_name("Good", || false),
            CharacterCreateError::Ok
        );
        assert!(!m.no_names_registered());
        m.remove_character_id(1);
        assert!(m.no_names_registered());
        assert_eq!(m.get_character_id("Taken"), 0);
    }
}
