//! `Core/Managers/FriendMananger`: the global friend list (`_allFriends`), `SendStatusChange` and
//! `FormatFriend`. Adding / removing friends is a later phase.

use std::collections::HashMap;
use std::sync::RwLock;

use sqlx::mysql::MySqlPool;

use crate::db::int;
use crate::error::{GameError, Result};
use crate::models::char_data::{FriendEntry, FriendInfo};
use crate::models::character::Character;

#[derive(Debug, Default)]
pub struct FriendManager {
    /// `_allFriends` (id -> template).
    all: RwLock<HashMap<u32, FriendEntry>>,
}

impl FriendManager {
    /// `Load()`. Note (C#): the dictionary is keyed by the `id` column alone while the table's
    /// key is `(id, owner)`, so a repeated id aborts the load (`Dictionary.Add`).
    pub async fn load(&self, pool: &MySqlPool) -> Result<()> {
        let rows = sqlx::query(
            "SELECT CAST(id AS SIGNED) AS id, CAST(friend_id AS SIGNED) AS friend_id, CAST(owner AS SIGNED) AS owner FROM friends",
        )
        .fetch_all(pool)
        .await?;
        let mut all = self.all.write().expect("friends poisoned");
        for r in rows {
            let f = FriendEntry {
                id: int(&r, "id")? as u32,
                friend_id: int(&r, "friend_id")? as u32,
                owner: int(&r, "owner")? as u32,
            };
            if all.insert(f.id, f).is_some() {
                return Err(GameError::Protocol(format!(
                    "duplicate friends id {}",
                    f.id
                )));
            }
        }
        tracing::info!("Loaded {} friends", all.len());
        Ok(())
    }

    /// Owners that have `character_id` as friend (`SendStatusChange` recipients).
    pub fn owners_of(&self, character_id: u32) -> Vec<u32> {
        self.all
            .read()
            .expect("friends poisoned")
            .values()
            .filter(|f| f.friend_id == character_id)
            .map(|f| f.owner)
            .collect()
    }
}

/// `FormatFriend(character)`.
pub fn format_friend(c: &Character, in_party: bool) -> FriendInfo {
    FriendInfo {
        character_id: c.id,
        name: c.name.clone(),
        race: c.race,
        level: c.level,
        health: c.hp,
        abilities: [c.ability1, c.ability2, c.ability3],
        x: c.transform.x,
        y: c.transform.y,
        z: c.transform.z,
        zone_id: c.transform.zone_id,
        in_party,
        is_online: true,
        last_world_leave_time: c.leave_time,
    }
}
