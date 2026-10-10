//! `Core/Managers/FriendMananger` + `Models/Game/Char/{CharacterFriends,CharacterBlocked}`: the
//! global friend list (`_allFriends`), `SendStatusChange`, `GetFriendInfo`, `FormatFriend`,
//! adding / removing friends and blocked users. (Moved here from `managers::friend`, which
//! re-exports this module.)

use std::collections::HashMap;
use std::sync::RwLock;

use sqlx::mysql::MySqlPool;

use crate::db::int;
use crate::db_character::offline_friend_infos;
use crate::error::{GameError, Result};
use crate::models::char_data::{FriendEntry, FriendInfo};
use crate::models::character::Character;
use crate::packets::world::SCFriendStatusChangedPacket;
use crate::server::GameServer;
use crate::systems::social::packets::friend::*;
use crate::world::PlayerRef;

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

    /// `AddToAllFriends(template)` (`TryAdd`).
    pub fn add_to_all(&self, f: FriendEntry) {
        self.all
            .write()
            .expect("friends poisoned")
            .entry(f.id)
            .or_insert(f);
    }

    /// `RemoveFromAllFriends(id)`.
    pub fn remove_from_all(&self, id: u32) {
        self.all.write().expect("friends poisoned").remove(&id);
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

    pub fn count(&self) -> usize {
        self.all.read().expect("friends poisoned").len()
    }
}

/// `FormatFriend(character)` (`in_party` is the character's `InParty`).
pub fn format_friend(c: &Character, in_party: bool) -> FriendInfo {
    FriendInfo {
        character_id: c.id,
        name: c.unit.name.clone(),
        race: c.race,
        level: c.unit.level,
        health: c.unit.hp,
        abilities: [c.ability1, c.ability2, c.ability3],
        x: c.unit.transform.x,
        y: c.unit.transform.y,
        z: c.unit.transform.z,
        zone_id: c.unit.transform.zone_id,
        in_party,
        is_online: true,
        last_world_leave_time: c.leave_time,
    }
}

/// `FriendMananger.SendStatusChange(unit, forOnline, value)`.
pub fn send_status_change(server: &GameServer, p: &PlayerRef, for_online: bool, value: bool) {
    let owners = server.friends.owners_of(p.character_id);
    if owners.is_empty() {
        return;
    }
    let mut info = {
        let c = p.data();
        format_friend(&c, c.in_party)
    };
    if for_online {
        info.is_online = value;
    } else {
        info.in_party = value;
    }
    for owner in owners {
        if let Some(o) = server.world.character_by_id(owner) {
            o.send(SCFriendStatusChangedPacket {
                friend: info.clone(),
            });
        }
    }
}

/// `FriendMananger.GetFriendInfo(name)`: the online character, else the `characters` row of
/// that name (`None` when there is none).
pub async fn friend_info_by_name(server: &GameServer, name: &str) -> Option<FriendInfo> {
    if let Some(p) = server.world.character_by_name(name) {
        let c = p.data();
        return Some(format_friend(&c, c.in_party));
    }
    let id = match sqlx::query("SELECT CAST(id AS SIGNED) AS id FROM characters WHERE `name` = ?")
        .bind(name)
        .fetch_all(&server.db)
        .await
    {
        // `while (reader.Read()) friendId = ...` keeps the last row.
        Ok(rows) => rows
            .last()
            .and_then(|r| int(r, "id").ok())
            .map_or(0, |v| v as u32),
        Err(e) => {
            tracing::error!("GetFriendInfo({name}): {e}");
            return None;
        }
    };
    friend_infos(server, &[id]).await.into_iter().next()
}

/// `FriendMananger.GetFriendInfo(ids)`: online characters first, then the offline rows.
pub async fn friend_infos(server: &GameServer, ids: &[u32]) -> Vec<FriendInfo> {
    let mut out = Vec::new();
    let mut offline = Vec::new();
    for &id in ids {
        match server.world.character_by_id(id) {
            Some(p) => {
                let c = p.data();
                out.push(format_friend(&c, c.in_party));
            }
            None => offline.push(id),
        }
    }
    if offline.is_empty() {
        return out;
    }
    match offline_friend_infos(&server.db, &offline).await {
        Ok(v) => out.extend(v),
        Err(e) => tracing::error!("GetFriendInfo: {e}"),
    }
    out
}

/// `CharacterFriends.AddFriend(name)`. Note (C#): adding yourself is not rejected.
pub async fn add_friend(server: &GameServer, owner: &PlayerRef, name: &str) {
    let Some(friend) = friend_info_by_name(server, name).await else {
        // TODO (C#): ERROR MESSAGE ALREADY ADDED
        return;
    };
    if owner
        .data()
        .friends
        .iter()
        .any(|f| f.friend_id == friend.character_id)
    {
        return;
    }
    let id = match server.ids.friend.get_next_id() {
        Ok(id) => id,
        Err(e) => {
            tracing::error!("{e}");
            return;
        }
    };
    let entry = FriendEntry {
        id,
        friend_id: friend.character_id,
        owner: owner.character_id,
    };
    owner.data().friends.push(entry);
    server.friends.add_to_all(entry);
    owner.send(SCAddFriendPacket {
        friend,
        success: true,
        error_message: 0,
    });
}

/// `CharacterFriends.RemoveFriend(name)`.
pub async fn remove_friend(server: &GameServer, owner: &PlayerRef, name: &str) {
    let Some(friend) = friend_info_by_name(server, name).await else {
        return;
    };
    let entry = {
        let mut c = owner.data();
        match c
            .friends
            .iter()
            .position(|f| f.friend_id == friend.character_id)
        {
            Some(i) => {
                let e = c.friends.remove(i);
                c.removed_friends.push(friend.character_id);
                e
            }
            None => return,
        }
    };
    server.friends.remove_from_all(entry.id);
    owner.send(SCDeleteFriendPacket {
        character_id: friend.character_id,
        success: true,
        friend_name: name.to_string(),
        error_message: 0,
    });
}

/// `CharacterBlocked.AddBlockedUser(name)` (online characters only).
pub fn add_blocked_user(server: &GameServer, owner: &PlayerRef, name: &str) {
    let Some(blocked) = server.world.character_by_name(name) else {
        return;
    };
    let blocked_name = blocked.data().unit.name.clone();
    {
        let mut c = owner.data();
        if c.blocked.iter().any(|(_, b)| *b == blocked.character_id) {
            return; // already blocked
        }
        c.blocked.push((owner.character_id, blocked.character_id));
    }
    owner.send(SCAddBlockedUserPacket {
        character_id: blocked.character_id,
        character_name: blocked_name,
        success: true,
        error_message: 0,
    });
}

/// `CharacterBlocked.RemoveBlockedUser(name)` (online characters only).
pub fn remove_blocked_user(server: &GameServer, owner: &PlayerRef, name: &str) {
    let Some(blocked) = server.world.character_by_name(name) else {
        return;
    };
    {
        let mut c = owner.data();
        match c
            .blocked
            .iter()
            .position(|(_, b)| *b == blocked.character_id)
        {
            Some(i) => {
                c.blocked.remove(i);
                c.removed_blocked.push(blocked.character_id);
            }
            None => return, // not blocked
        }
    }
    owner.send(SCDeleteBlockedUserPacket {
        character_id: blocked.character_id,
        success: true,
        blocked_name: name.to_string(),
        error_message: 0,
    });
}
