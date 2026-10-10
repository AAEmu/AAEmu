//! The data half of `Core/Managers/ExpeditionManager` + `Models/Game/Expeditions`: loading
//! guilds / members / role policies, `GetExpedition`, `SendExpeditions`, `SendExpeditionInfo`
//! and the member status refresh on login / logout / zone change. Guild management (create,
//! invite, kick, ...) is a later phase.

use std::sync::RwLock;

use aaemu_commons::{Marshal, PacketStream};
use sqlx::mysql::MySqlPool;
use sqlx::Row;

use crate::data::world_data::SystemFaction;
use crate::db::{dt, int, unix};
use crate::error::Result;
use crate::models::character::convert_long;
use crate::models::item::UnixTime;

/// `ExpeditionMember`.
#[derive(Debug, Clone, PartialEq)]
pub struct ExpeditionMember {
    pub expedition_id: u32,
    pub character_id: u32,
    pub in_party: bool,
    pub is_online: bool,
    pub last_world_leave_time: UnixTime,
    pub name: String,
    pub level: u8,
    pub zone_id: u32,
    pub id3: u32,
    pub abilities: [u8; 3],
    pub role: u8,
    pub position: (f32, f32, f32),
    pub memo: String,
    pub transfer_requested_time: UnixTime,
}

impl Marshal for ExpeditionMember {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.expedition_id)
            .write_u32(self.character_id)
            .write_bool(self.in_party)
            .write_bool(self.is_online)
            .write_i64(self.last_world_leave_time)
            .write_str(&self.name)
            .write_u8(self.level)
            .write_u32(self.zone_id)
            .write_u32(self.id3);
        for a in self.abilities {
            s.write_u8(a);
        }
        s.write_u8(self.role)
            .write_i64(convert_long(self.position.0))
            .write_i64(convert_long(self.position.1))
            .write_f32(self.position.2)
            .write_str(&self.memo)
            .write_i64(self.transfer_requested_time);
    }
}

/// `ExpeditionRolePolicy`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ExpeditionRolePolicy {
    pub expedition_id: u32,
    pub role: u8,
    pub name: String,
    pub dominion_declare: bool,
    pub invite: bool,
    pub expel: bool,
    pub promote: bool,
    pub dismiss: bool,
    pub chat: bool,
    pub manager_chat: bool,
    pub siege_master: bool,
    pub join_siege: bool,
}

impl Marshal for ExpeditionRolePolicy {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.expedition_id)
            .write_u8(self.role)
            .write_str(&self.name)
            .write_bool(self.dominion_declare)
            .write_bool(self.invite)
            .write_bool(self.expel)
            .write_bool(self.promote)
            .write_bool(self.dismiss)
            .write_bool(self.chat)
            .write_bool(self.manager_chat)
            .write_bool(self.siege_master)
            .write_bool(self.join_siege);
    }
}

/// `Expedition` (a `SystemFaction` with members and policies).
#[derive(Debug, Clone, PartialEq)]
pub struct Expedition {
    pub faction: SystemFaction,
    pub members: Vec<ExpeditionMember>,
    pub policies: Vec<ExpeditionRolePolicy>,
}

/// Values a member refresh copies from the character (`ExpeditionMember.Refresh`).
#[derive(Debug, Clone)]
pub struct MemberRefresh {
    pub character_id: u32,
    pub name: String,
    pub level: u8,
    pub zone_id: u32,
    pub abilities: [u8; 3],
    pub position: (f32, f32, f32),
}

/// `ExpeditionManager` (data).
#[derive(Debug, Default)]
pub struct ExpeditionManager {
    /// `_expeditions.Values` in insertion order.
    expeditions: RwLock<Vec<Expedition>>,
}

impl ExpeditionManager {
    /// `Load()`.
    pub async fn load(&self, pool: &MySqlPool) -> Result<()> {
        let mut list = Vec::new();
        for r in sqlx::query(
            "SELECT CAST(id AS SIGNED) AS id, CAST(mother AS SIGNED) AS mother, name, CAST(owner AS SIGNED) AS owner, owner_name, created_at FROM expeditions",
        )
        .fetch_all(pool)
        .await?
        {
            list.push(Expedition {
                faction: SystemFaction {
                    id: int(&r, "id")? as u32,
                    mother_id: int(&r, "mother")? as u32,
                    name: r.try_get("name")?,
                    owner_id: int(&r, "owner")? as u32,
                    owner_name: r.try_get("owner_name")?,
                    unit_owner_type: 0,
                    political_system: 1,
                    created: unix(dt(&r, "created_at")),
                    aggro_link: false,
                    diplomacy_target: false,
                    guard_help: false,
                    allow_change_name: 0,
                },
                members: Vec::new(),
                policies: Vec::new(),
            });
        }
        for e in &mut list {
            for r in sqlx::query(
                "SELECT CAST(character_id AS SIGNED) AS character_id, CAST(expedition_id AS SIGNED) AS expedition_id, \
                 CAST(role AS SIGNED) AS role, memo, last_leave_time, name, CAST(level AS SIGNED) AS level, \
                 CAST(ability1 AS SIGNED) AS ability1, CAST(ability2 AS SIGNED) AS ability2, CAST(ability3 AS SIGNED) AS ability3 \
                 FROM expedition_members WHERE expedition_id = ?",
            )
            .bind(e.faction.id)
            .fetch_all(pool)
            .await?
            {
                e.members.push(ExpeditionMember {
                    character_id: int(&r, "character_id")? as u32,
                    expedition_id: int(&r, "expedition_id")? as u32,
                    role: int(&r, "role")? as u8,
                    memo: r.try_get("memo")?,
                    last_world_leave_time: unix(dt(&r, "last_leave_time")),
                    name: r.try_get("name")?,
                    level: int(&r, "level")? as u8,
                    abilities: [
                        int(&r, "ability1")? as u8,
                        int(&r, "ability2")? as u8,
                        int(&r, "ability3")? as u8,
                    ],
                    is_online: false,
                    in_party: false,
                    zone_id: 0,
                    id3: 0,
                    position: (0.0, 0.0, 0.0),
                    transfer_requested_time: 0,
                });
            }
            for r in sqlx::query(
                "SELECT CAST(expedition_id AS SIGNED) AS expedition_id, CAST(role AS SIGNED) AS role, name, \
                 CAST(dominion_declare AS SIGNED) AS dominion_declare, CAST(invite AS SIGNED) AS invite, CAST(expel AS SIGNED) AS expel, \
                 CAST(promote AS SIGNED) AS promote, CAST(dismiss AS SIGNED) AS dismiss, CAST(chat AS SIGNED) AS chat, \
                 CAST(manager_chat AS SIGNED) AS manager_chat, CAST(siege_master AS SIGNED) AS siege_master, CAST(join_siege AS SIGNED) AS join_siege \
                 FROM expedition_role_policies WHERE expedition_id = ?",
            )
            .bind(e.faction.id)
            .fetch_all(pool)
            .await?
            {
                let b = |c: &str| -> Result<bool> { Ok(int(&r, c)? != 0) };
                e.policies.push(ExpeditionRolePolicy {
                    expedition_id: int(&r, "expedition_id")? as u32,
                    role: int(&r, "role")? as u8,
                    name: r.try_get("name")?,
                    dominion_declare: b("dominion_declare")?,
                    invite: b("invite")?,
                    expel: b("expel")?,
                    promote: b("promote")?,
                    dismiss: b("dismiss")?,
                    chat: b("chat")?,
                    manager_chat: b("manager_chat")?,
                    siege_master: b("siege_master")?,
                    join_siege: b("join_siege")?,
                });
            }
        }
        *self.expeditions.write().expect("expeditions poisoned") = list;
        Ok(())
    }

    /// Adds an expedition (tests).
    pub fn add(&self, e: Expedition) {
        self.expeditions
            .write()
            .expect("expeditions poisoned")
            .push(e);
    }

    /// `GetExpedition(id) != null`.
    pub fn exists(&self, id: u32) -> bool {
        self.expeditions
            .read()
            .expect("expeditions poisoned")
            .iter()
            .any(|e| e.faction.id == id)
    }

    pub fn get(&self, id: u32) -> Option<Expedition> {
        self.expeditions
            .read()
            .expect("expeditions poisoned")
            .iter()
            .find(|e| e.faction.id == id)
            .cloned()
    }

    /// The factions `SendExpeditions` writes, in chunks of 20.
    pub fn faction_chunks(&self) -> Vec<Vec<SystemFaction>> {
        let list = self.expeditions.read().expect("expeditions poisoned");
        list.chunks(20)
            .map(|c| c.iter().map(|e| e.faction.clone()).collect())
            .collect()
    }

    /// `ExpeditionMember.Refresh` + status packet payload for `OnCharacterLogin` /
    /// `OnCharacterRefresh`. Returns the refreshed member and the member ids to notify.
    pub fn refresh_member(
        &self,
        expedition_id: u32,
        r: &MemberRefresh,
        now: UnixTime,
    ) -> Option<(ExpeditionMember, Vec<u32>)> {
        let mut list = self.expeditions.write().expect("expeditions poisoned");
        let e = list.iter_mut().find(|e| e.faction.id == expedition_id)?;
        let m = e
            .members
            .iter_mut()
            .find(|m| m.character_id == r.character_id)?;
        m.is_online = true;
        m.position = r.position;
        m.zone_id = r.zone_id;
        m.abilities = r.abilities;
        m.level = r.level;
        m.name = r.name.clone();
        m.last_world_leave_time = now;
        let m = m.clone();
        Some((m, e.members.iter().map(|m| m.character_id).collect()))
    }

    /// `OnCharacterLogout` member update.
    pub fn logout_member(
        &self,
        expedition_id: u32,
        character_id: u32,
        now: UnixTime,
    ) -> Option<(ExpeditionMember, Vec<u32>)> {
        let mut list = self.expeditions.write().expect("expeditions poisoned");
        let e = list.iter_mut().find(|e| e.faction.id == expedition_id)?;
        let m = e
            .members
            .iter_mut()
            .find(|m| m.character_id == character_id)?;
        m.is_online = false;
        m.last_world_leave_time = now;
        let m = m.clone();
        Some((m, e.members.iter().map(|m| m.character_id).collect()))
    }
}
