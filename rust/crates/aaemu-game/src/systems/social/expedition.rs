//! `Core/Managers/ExpeditionManager` + `Models/Game/Expeditions` (guilds): loading guilds /
//! members / role policies, `GetExpedition`, `SendExpeditions`, `SendExpeditionInfo`, member
//! status refresh on login / logout / zone change, and guild management (create, invite /
//! reply, leave, kick, member roles, owner change, role policies, disband) with the
//! `Expedition.Save` persistence. (Moved here from `managers::expedition`, which re-exports
//! this module.)

use std::sync::RwLock;

use aaemu_commons::{Marshal, PacketStream};
use sqlx::mysql::MySqlPool;
use sqlx::Row;

use crate::data::world_data::SystemFaction;
use crate::db::{dt, from_unix, int, unix};
use crate::error::Result;
use crate::models::character::convert_long;
use crate::models::item::UnixTime;
use crate::packets::world::{
    SCExpeditionMemberListPacket, SCExpeditionMemberStatusChangedPacket,
    SCExpeditionRolePolicyListPacket, SCFactionListPacket,
};
use crate::server::GameServer;
use crate::systems::social::packets::expedition::*;
use crate::systems::social::util::{self, error, send_error};
use crate::world::{broadcast, PlayerRef};

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

impl ExpeditionRolePolicy {
    /// `ExpeditionRolePolicy.Read`.
    pub fn read(s: &mut PacketStream) -> std::result::Result<Self, aaemu_commons::MarshalError> {
        Ok(Self {
            expedition_id: s.read_u32()?,
            role: s.read_u8()?,
            name: s.read_string()?,
            dominion_declare: s.read_bool()?,
            invite: s.read_bool()?,
            expel: s.read_bool()?,
            promote: s.read_bool()?,
            dismiss: s.read_bool()?,
            chat: s.read_bool()?,
            manager_chat: s.read_bool()?,
            siege_master: s.read_bool()?,
            join_siege: s.read_bool()?,
        })
    }
}

/// `Expedition` (a `SystemFaction` with members and policies).
#[derive(Debug, Clone, PartialEq)]
pub struct Expedition {
    pub faction: SystemFaction,
    pub members: Vec<ExpeditionMember>,
    pub policies: Vec<ExpeditionRolePolicy>,
    /// `_removedMembers` (character ids, flushed by `Save`).
    pub removed_members: Vec<u32>,
    /// `isDisbanded`.
    pub is_disbanded: bool,
}

impl Expedition {
    /// `GetPolicyByRole(role)`.
    pub fn policy_by_role(&self, role: u8) -> Option<&ExpeditionRolePolicy> {
        self.policies.iter().find(|p| p.role == role)
    }

    /// `GetMember(characterId)`.
    pub fn member(&self, character_id: u32) -> Option<&ExpeditionMember> {
        self.members.iter().find(|m| m.character_id == character_id)
    }

    fn member_mut(&mut self, character_id: u32) -> Option<&mut ExpeditionMember> {
        self.members
            .iter_mut()
            .find(|m| m.character_id == character_id)
    }

    /// The member list of `Members.Remove(member)` + `_removedMembers.Add(member.CharacterId)`
    /// (the guild chat part of `RemoveMember` is done by the caller).
    fn remove_member(&mut self, character_id: u32) {
        if let Some(i) = self
            .members
            .iter()
            .position(|m| m.character_id == character_id)
        {
            self.members.remove(i);
        }
        self.removed_members.push(character_id);
    }

    pub fn id(&self) -> u32 {
        self.faction.id
    }

    pub fn member_ids(&self) -> Vec<u32> {
        self.members.iter().map(|m| m.character_id).collect()
    }
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

/// `ExpeditionManager`.
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
                removed_members: Vec::new(),
                is_disbanded: false,
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

    /// Adds an expedition (`_expeditions.Add`).
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

    /// `GetExpedition(id)` (a copy).
    pub fn get(&self, id: u32) -> Option<Expedition> {
        self.expeditions
            .read()
            .expect("expeditions poisoned")
            .iter()
            .find(|e| e.faction.id == id)
            .cloned()
    }

    /// Runs `f` on the expedition `id` under the manager's write lock.
    pub fn with_mut<R>(&self, id: u32, f: impl FnOnce(&mut Expedition) -> R) -> Option<R> {
        let mut list = self.expeditions.write().expect("expeditions poisoned");
        list.iter_mut().find(|e| e.faction.id == id).map(f)
    }

    /// Every expedition (a copy, insertion order).
    pub fn all(&self) -> Vec<Expedition> {
        self.expeditions
            .read()
            .expect("expeditions poisoned")
            .clone()
    }

    /// The factions `SendExpeditions` writes, in chunks of 20.
    pub fn faction_chunks(&self) -> Vec<Vec<SystemFaction>> {
        let list = self.expeditions.read().expect("expeditions poisoned");
        list.chunks(20)
            .map(|c| c.iter().map(|e| e.faction.clone()).collect())
            .collect()
    }

    /// `GetExpeditionOfCharacter(characterId)` (0 = none).
    pub fn expedition_of_character(&self, character_id: u32) -> u32 {
        self.expeditions
            .read()
            .expect("expeditions poisoned")
            .iter()
            .find(|e| e.members.iter().any(|m| m.character_id == character_id))
            .map_or(0, |e| e.faction.id)
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
        let m = e.member_mut(r.character_id)?;
        m.is_online = true;
        m.position = r.position;
        m.zone_id = r.zone_id;
        m.abilities = r.abilities;
        m.level = r.level;
        m.name = r.name.clone();
        m.last_world_leave_time = now;
        let m = m.clone();
        Some((m, e.member_ids()))
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
        let m = e.member_mut(character_id)?;
        m.is_online = false;
        m.last_world_leave_time = now;
        let m = m.clone();
        Some((m, e.member_ids()))
    }
}

// ---------------------------------------------------------------------------------------------
// Guild management (ExpeditionManager / Expedition behaviour)
// ---------------------------------------------------------------------------------------------

/// `Expedition.SendPacket`: every member that is in the world.
pub fn send_to_members(
    server: &GameServer,
    members: &[u32],
    packet: &dyn crate::net::packet::ServerPacket,
) {
    let mut out = crate::net::connection::Fanout::new(packet);
    for id in members {
        if let Some(p) = server.world.character_by_id(*id) {
            out.send(&p.conn);
        }
    }
}

/// `GameObject.BroadcastPacket(packet, true)` for a player.
fn broadcast_self(
    server: &GameServer,
    p: &PlayerRef,
    packet: &dyn crate::net::packet::ServerPacket,
) {
    let inst = p.data().unit.transform.instance_id;
    match server.world.world(inst) {
        Some(w) => broadcast(&w, p, packet, true),
        None => p.send_dyn(packet),
    }
}

/// `ChatManager.GetGuildChat(expedition)`.
pub fn guild_chat(server: &GameServer, id: u32, name: &str) -> crate::managers::chat::ChannelRef {
    server.chat.guild_chat(id, name, &server.ids.chat)
}

/// `ExpeditionManager.GetDefaultPolicies(expeditionId)`.
pub fn default_policies(server: &GameServer, expedition_id: u32) -> Vec<ExpeditionRolePolicy> {
    server
        .config
        .expedition
        .role_policies
        .iter()
        .map(|p| p.to_policy(expedition_id))
        .collect()
}

/// `ExpeditionManager.GetMemberFromCharacter(expedition, character, owner)`.
pub fn member_from_character(
    server: &GameServer,
    expedition_id: u32,
    p: &PlayerRef,
    owner: bool,
) -> ExpeditionMember {
    let position = util::world_position(server, p);
    let c = p.data();
    ExpeditionMember {
        is_online: true,
        name: c.unit.name.clone(),
        level: c.unit.level,
        role: if owner { 255 } else { 0 },
        memo: String::new(),
        position,
        zone_id: c.unit.transform.zone_id,
        abilities: [c.ability1, c.ability2, c.ability3],
        expedition_id,
        character_id: c.id,
        last_world_leave_time: util::now_secs(),
        in_party: false,
        id3: 0,
        transfer_requested_time: 0,
    }
}

/// `ExpeditionManager.SendExpeditionInfo(character)`.
pub fn send_expedition_info(p: &PlayerRef, e: &Expedition) {
    p.send(SCExpeditionRolePolicyListPacket {
        policies: e.policies.clone(),
    });
    let total = e.members.len() as u32;
    for chunk in e.members.chunks(20) {
        p.send(SCExpeditionMemberListPacket {
            total,
            id: e.faction.id,
            members: chunk.to_vec(),
        });
    }
}

/// `ExpeditionManager.SendExpeditions(character)` (lists every guild, then an empty policy
/// list).
pub fn send_expeditions(server: &GameServer, p: &PlayerRef) {
    for chunk in server.expeditions.faction_chunks() {
        p.send(SCFactionListPacket { factions: chunk });
    }
    p.send(SCExpeditionRolePolicyListPacket {
        policies: Vec::new(),
    });
}

fn member_refresh(server: &GameServer, p: &PlayerRef) -> MemberRefresh {
    let position = util::world_position(server, p);
    let c = p.data();
    MemberRefresh {
        character_id: c.id,
        name: c.unit.name.clone(),
        level: c.unit.level,
        zone_id: c.unit.transform.zone_id,
        abilities: [c.ability1, c.ability2, c.ability3],
        position,
    }
}

/// `Expedition.OnCharacterRefresh(character)` (refresh + status packet to the guild).
pub fn on_character_refresh(server: &GameServer, p: &PlayerRef, expedition_id: u32) {
    let r = member_refresh(server, p);
    if let Some((member, members)) =
        server
            .expeditions
            .refresh_member(expedition_id, &r, util::now_secs())
    {
        send_to_members(
            server,
            &members,
            &SCExpeditionMemberStatusChangedPacket { member, flag: 0 },
        );
    }
}

/// `Expedition.OnCharacterLogin(character)` (refresh + status packet + guild chat join).
pub fn on_character_login(server: &GameServer, p: &PlayerRef, expedition_id: u32) {
    let r = member_refresh(server, p);
    let Some((member, members)) =
        server
            .expeditions
            .refresh_member(expedition_id, &r, util::now_secs())
    else {
        return;
    };
    send_to_members(
        server,
        &members,
        &SCExpeditionMemberStatusChangedPacket { member, flag: 0 },
    );
    if let Some(e) = server.expeditions.get(expedition_id) {
        guild_chat(server, e.id(), &e.faction.name).join(p);
    }
}

/// `Expedition.OnCharacterLogout(character)`.
pub fn on_character_logout(server: &GameServer, p: &PlayerRef, expedition_id: u32) {
    if let Some((member, members)) =
        server
            .expeditions
            .logout_member(expedition_id, p.character_id, util::now_secs())
    {
        send_to_members(
            server,
            &members,
            &SCExpeditionMemberStatusChangedPacket { member, flag: 0 },
        );
    }
    if let Some(e) = server.expeditions.get(expedition_id) {
        guild_chat(server, e.id(), &e.faction.name).leave(p);
    }
}

/// `Character.Expedition` (the id when the guild exists, as `GetExpedition` would return null).
fn expedition_of(server: &GameServer, p: &PlayerRef) -> Option<Expedition> {
    let id = p.data().unit.expedition_id;
    if id == 0 {
        return None;
    }
    server.expeditions.get(id)
}

/// `Expedition.RemoveMember(member)`: leaves the guild chat (when online) and records the id.
fn remove_member(server: &GameServer, expedition_id: u32, character_id: u32) {
    let name = server
        .expeditions
        .with_mut(expedition_id, |e| {
            e.remove_member(character_id);
            e.faction.name.clone()
        })
        .unwrap_or_default();
    if let Some(c) = server.world.character_by_id(character_id) {
        guild_chat(server, expedition_id, &name).leave(&c);
    }
}

/// `ExpeditionManager.CreateExpedition(name, connection)`.
pub async fn create_expedition(server: &GameServer, owner: &PlayerRef, name: &str) {
    let cfg = &server.config.expedition;
    if expedition_of(server, owner).is_some() {
        send_error(owner, error::EXPEDITION_ALREADY_MEMBER);
        return;
    }
    if name.encode_utf16().count() > 32 {
        send_error(owner, error::EXPEDITION_NAME_LENGTH);
        return;
    }
    let matches = match regex::Regex::new(&cfg.name_regex) {
        Ok(r) => r.is_match(name),
        Err(e) => {
            tracing::error!("Expedition.NameRegex: {e}");
            false
        }
    };
    if !matches {
        send_error(owner, error::EXPEDITION_NAME_CHARACTER);
        return;
    }
    if server
        .expeditions
        .all()
        .iter()
        .any(|e| e.faction.name == name)
    {
        send_error(owner, error::EXPEDITION_NAME_EXIST);
        return;
    }
    // ----------------- Conditions, can change this...
    let Some(team_members) = server
        .systems
        .social
        .teams
        .team_characters_of(owner.character_id)
    else {
        // We send the same error on number of party members when we don't have a party
        send_error(owner, error::EXPEDITION_CREATE_MEMBER);
        return;
    };
    let owner_mother = util::mother_id(server, owner.data().unit.faction_id);
    let mut valid: Vec<PlayerRef> = Vec::new();
    for m in team_members {
        let (level, exp, faction) = {
            let c = m.data();
            (c.unit.level, c.unit.expedition_id, c.unit.faction_id)
        };
        if level < cfg.create.level {
            send_error(owner, error::EXPEDITION_CREATE_LEVEL);
            return;
        }
        if exp != 0 && server.expeditions.exists(exp) {
            send_error(owner, error::EXPEDITION_CREATE_MEMBER_EXPEDITION);
            return;
        }
        if util::mother_id(server, faction) != owner_mother {
            send_error(owner, error::EXPEDITION_CREATE_FACTION);
            return;
        }
        valid.push(m);
    }
    if valid.len() < usize::from(cfg.create.party_member_count) {
        send_error(owner, error::EXPEDITION_CREATE_MEMBER);
        return;
    }
    let cost = i64::from(cfg.create.cost);
    {
        let mut c = owner.data();
        if c.money < cost {
            drop(c);
            send_error(owner, error::EXPEDITION_CREATE_MONEY);
            return;
        }
        c.money -= cost;
    }
    owner.send(item_task_money_packet(
        ITEM_TASK_EXPEDITION_CREATION,
        -cfg.create.cost,
    ));
    // -----------------
    let id = match server.ids.expedition.get_next_id() {
        Ok(id) => id,
        Err(e) => {
            tracing::error!("{e}");
            return;
        }
    };
    let (owner_faction, owner_name, owner_obj) = {
        let c = owner.data();
        (c.unit.faction_id, c.unit.name.clone(), owner.obj_id)
    };
    let expedition = Expedition {
        faction: SystemFaction {
            id,
            mother_id: owner_faction,
            name: name.to_string(),
            owner_id: owner.character_id,
            owner_name: owner_name.clone(),
            unit_owner_type: 0,
            political_system: 1,
            created: util::now_secs(),
            aggro_link: false,
            diplomacy_target: false,
            guard_help: false,
            allow_change_name: 0,
        },
        members: vec![member_from_character(server, id, owner, true)],
        policies: default_policies(server, id),
        removed_members: Vec::new(),
        is_disbanded: false,
    };
    server.expeditions.add(expedition.clone());
    owner.data().unit.expedition_id = id;
    // ENROLL_GUILD (queued: runs once the packet is handled).
    crate::systems::refresh_achievements_later(server, owner);

    owner.send(SCFactionCreatedPacket {
        faction: expedition.faction.clone(),
        owner_obj_id: owner_obj,
        members: vec![(owner_obj, owner.character_id, owner_name.clone())],
    });
    server.world.broadcast_to_server(&SCFactionListPacket {
        factions: vec![expedition.faction.clone()],
    });
    broadcast_self(
        server,
        owner,
        &SCUnitExpeditionChangedPacket {
            obj_id: owner_obj,
            character_id: owner.character_id,
            kicker: String::new(),
            unit_name: owner_name,
            id: 0,
            expedition_id: id,
            expel: false,
        },
    );
    guild_chat(server, id, name).join(owner);
    send_expedition_info(owner, &expedition);

    for m in valid {
        if m.character_id == owner.character_id {
            continue;
        }
        let member = member_from_character(server, id, &m, false);
        m.data().unit.expedition_id = id;
        crate::systems::refresh_achievements_later(server, &m);
        let snapshot = server
            .expeditions
            .with_mut(id, |e| {
                e.members.push(member);
                e.clone()
            })
            .expect("just added");
        let unit_name = m.data().unit.name.clone();
        broadcast_self(
            server,
            &m,
            &SCUnitExpeditionChangedPacket {
                obj_id: m.obj_id,
                character_id: m.character_id,
                kicker: String::new(),
                unit_name,
                id: 0,
                expedition_id: id,
                expel: false,
            },
        );
        send_expedition_info(&m, &snapshot);
        on_character_login(server, &m, id);
    }
    save(server, id).await;
}

/// `ExpeditionManager.Invite(connection, invitedName)`.
pub fn invite(server: &GameServer, inviter: &PlayerRef, invited_name: &str) {
    let Some(e) = expedition_of(server, inviter) else {
        return;
    };
    let Some(m) = e.member(inviter.character_id) else {
        return;
    };
    match e.policy_by_role(m.role) {
        Some(p) if p.invite => {}
        Some(_) => return,
        None => {
            // C#: GetPolicyByRole(..).Invite on a null policy throws.
            tracing::error!("Expedition {} has no policy for role {}", e.id(), m.role);
            return;
        }
    }
    let Some(invited) = server.world.character_by_name(invited_name) else {
        return;
    };
    if expedition_of(server, &invited).is_some() {
        return;
    }
    let inviter_name = inviter.data().unit.name.clone();
    invited.send(SCExpeditionInvitationPacket {
        invitor_id: inviter.character_id,
        invitor_name: inviter_name,
        faction_id: e.id(),
        faction_name: e.faction.name.clone(),
    });
}

/// `ExpeditionManager.ReplyInvite(connection, id1, id2, reply)`.
pub async fn reply_invite(
    server: &GameServer,
    invited: &PlayerRef,
    id1: u32,
    _id2: u32,
    reply: bool,
) {
    if !reply {
        return;
    }
    if !server.expeditions.exists(id1) {
        // C#: `_expeditions[id1]` throws KeyNotFoundException.
        tracing::error!("ReplyExpeditionInvitation: no expedition {id1}");
        return;
    }
    let member = member_from_character(server, id1, invited, false);
    invited.data().unit.expedition_id = id1;
    crate::systems::refresh_achievements_later(server, invited);
    let Some(snapshot) = server.expeditions.with_mut(id1, |e| {
        e.members.push(member);
        e.clone()
    }) else {
        return;
    };
    let unit_name = invited.data().unit.name.clone();
    broadcast_self(
        server,
        invited,
        &SCUnitExpeditionChangedPacket {
            obj_id: invited.obj_id,
            character_id: invited.character_id,
            kicker: String::new(),
            unit_name,
            id: 0,
            expedition_id: id1,
            expel: false,
        },
    );
    send_expedition_info(invited, &snapshot);
    on_character_login(server, invited, id1);
    save(server, id1).await;
}

/// `ExpeditionManager.ChangeExpeditionRolePolicy(connection, policy)`.
pub async fn change_role_policy(server: &GameServer, p: &PlayerRef, policy: ExpeditionRolePolicy) {
    let id = policy.expedition_id;
    let Some(e) = server.expeditions.get(id) else {
        tracing::error!("ChangeExpeditionRolePolicy: no expedition {id}");
        return;
    };
    let Some(member) = e.member(p.character_id) else {
        return;
    };
    match e.policy_by_role(member.role) {
        Some(x) if x.expel => {}
        Some(_) => return,
        None => {
            tracing::error!("Expedition {id} has no policy for role {}", member.role);
            return;
        }
    }
    let updated = server.expeditions.with_mut(id, |e| {
        match e.policies.iter_mut().find(|x| x.role == policy.role) {
            Some(cur) => {
                cur.name = policy.name.clone();
                cur.invite = policy.invite;
                cur.join_siege = policy.join_siege;
                cur.promote = policy.promote;
                cur.expel = policy.expel;
                Some(e.member_ids())
            }
            None => None,
        }
    });
    let Some(Some(members)) = updated else {
        tracing::error!("Expedition {id} has no policy for role {}", policy.role);
        return;
    };
    send_to_members(
        server,
        &members,
        &SCExpeditionRolePolicyChangedPacket {
            policy,
            success: true,
        },
    );
    save(server, id).await;
}

/// `ExpeditionManager.Leave(character)`.
pub async fn leave(server: &GameServer, p: &PlayerRef) {
    let Some(e) = expedition_of(server, p) else {
        return;
    };
    let id = e.id();
    if e.member(p.character_id).is_none() {
        // C#: RemoveMember(null) throws a NullReferenceException.
        tracing::error!(
            "Leave: {} is not a member of expedition {id}",
            p.character_id
        );
        return;
    }
    remove_member(server, id, p.character_id);
    let unit_name = p.data().unit.name.clone();
    let packet = SCUnitExpeditionChangedPacket {
        obj_id: p.obj_id,
        character_id: p.character_id,
        kicker: String::new(),
        unit_name,
        id,
        expedition_id: 0,
        expel: false,
    };
    p.data().unit.expedition_id = 0;
    broadcast_self(server, p, &packet);
    let members = server
        .expeditions
        .get(id)
        .map(|e| e.member_ids())
        .unwrap_or_default();
    send_to_members(server, &members, &packet);
    save(server, id).await;
}

/// `ExpeditionManager.Kick(connection, kickedId)`.
pub async fn kick(server: &GameServer, kicker: &PlayerRef, kicked_id: u32) {
    let Some(e) = expedition_of(server, kicker) else {
        return;
    };
    let id = e.id();
    let Some(me) = e.member(kicker.character_id) else {
        return;
    };
    match e.policy_by_role(me.role) {
        Some(x) if x.expel => {}
        Some(_) => return,
        None => {
            tracing::error!("Expedition {id} has no policy for role {}", me.role);
            return;
        }
    }
    let Some(kicked) = e.member(kicked_id).cloned() else {
        return;
    };
    remove_member(server, id, kicked_id);
    let kicked_char = server.world.character_by_id(kicked_id);
    let packet = SCUnitExpeditionChangedPacket {
        obj_id: kicked_char.as_ref().map_or(0, |c| c.obj_id),
        character_id: kicked.character_id,
        kicker: kicker.data().unit.name.clone(),
        unit_name: kicked.name.clone(),
        id,
        expedition_id: 0,
        expel: true,
    };
    if let Some(k) = &kicked_char {
        k.data().unit.expedition_id = 0;
        broadcast_self(server, k, &packet);
    }
    let members = server
        .expeditions
        .get(id)
        .map(|e| e.member_ids())
        .unwrap_or_default();
    send_to_members(server, &members, &packet);
    save(server, id).await;
}

/// `ExpeditionManager.ChangeMemberRole(connection, newRole, changedId)`.
pub async fn change_member_role(server: &GameServer, p: &PlayerRef, new_role: u8, changed_id: u32) {
    let Some(e) = expedition_of(server, p) else {
        return;
    };
    let id = e.id();
    let Some(changer) = e.member(p.character_id) else {
        return;
    };
    if changer.role <= new_role {
        return;
    }
    match e.policy_by_role(changer.role) {
        Some(x) if x.promote => {}
        Some(_) => return,
        None => {
            tracing::error!("Expedition {id} has no policy for role {}", changer.role);
            return;
        }
    }
    let changed = server.expeditions.with_mut(id, |e| {
        let members = e.member_ids();
        e.member_mut(changed_id).map(|m| {
            m.role = new_role;
            (m.character_id, m.role, m.name.clone(), members)
        })
    });
    let Some(Some((cid, role, name, members))) = changed else {
        return;
    };
    send_to_members(
        server,
        &members,
        &SCExpeditionRoleChangedPacket {
            id: cid,
            role,
            char_name: name,
        },
    );
    save(server, id).await;
}

/// `ExpeditionManager.ChangeOwner(connection, newOwnerId)`: keep the persistent owner id,
/// name and roles consistent; transferring to oneself is a no-op.
pub async fn change_owner(server: &GameServer, owner: &PlayerRef, new_owner_id: u32) {
    if new_owner_id == owner.character_id {
        return;
    }
    let Some(e) = expedition_of(server, owner) else {
        return;
    };
    let id = e.id();
    match e.member(owner.character_id) {
        Some(m) if m.role == 255 => {}
        _ => return,
    }
    if e.member(new_owner_id).is_none() {
        return;
    }
    let res = server.expeditions.with_mut(id, |e| {
        let members = e.member_ids();
        let new = e.member_mut(new_owner_id).expect("checked");
        new.role = 255;
        let new = (new.character_id, new.role, new.name.clone());
        let old = e.member_mut(owner.character_id).expect("checked");
        old.role = 0;
        let old = (old.character_id, old.role, old.name.clone());
        e.faction.owner_id = new_owner_id;
        e.faction.owner_name = new.2.clone();
        (old, new, members)
    });
    let Some((old, new, members)) = res else {
        return;
    };
    send_to_members(
        server,
        &members,
        &SCExpeditionOwnerChangedPacket {
            id: old.0,
            id2: new.0,
            char_name: new.2.clone(),
        },
    );
    send_to_members(
        server,
        &members,
        &SCExpeditionRoleChangedPacket {
            id: old.0,
            role: old.1,
            char_name: old.2,
        },
    );
    send_to_members(
        server,
        &members,
        &SCExpeditionRoleChangedPacket {
            id: new.0,
            role: new.1,
            char_name: new.2,
        },
    );
    save(server, id).await;
}

/// `ExpeditionManager.Disband(owner)`. The guild stays in the list (renamed
/// `$deleted-guild-<id>`, owner 0) as in C#.
pub async fn disband(server: &GameServer, owner: &PlayerRef) -> bool {
    let id = {
        let _housing_operation = server
            .systems
            .housing
            .operation_lock
            .lock()
            .expect("housing poisoned");
        let Some(e) = expedition_of(server, owner) else {
            send_error(owner, error::ONLY_EXPEDITION_MEMBER);
            return false;
        };
        if e.faction.owner_id != owner.character_id {
            send_error(owner, error::ONLY_EXPEDITION_OWNER);
            return false;
        }
        let id = e.id();
        if server.systems.housing.dominions.all().iter().any(|d| {
            d.owner_expedition_id == id
                || d.siege.as_ref().is_some_and(|siege| {
                    siege.status != crate::systems::housing::dominion::SiegeStatus::Finished
                        && (siege.attacker_expedition_id == id
                            || siege.defender_expedition_id == id)
                })
        }) {
            crate::world::session::send_message(
                owner,
                crate::systems::social::chat::chat_type::SYSTEM,
                "An expedition that owns territory or is registered for a siege cannot disband.",
            );
            return false;
        }
        for m in e.members.iter().rev() {
            if let Some(c) = server.world.character_by_id(m.character_id) {
                let online = c.data().is_online;
                if online {
                    c.send(SCExpeditionDismissedPacket { id, success: true });
                }
                c.data().unit.expedition_id = 0;
            }
            remove_member(server, id, m.character_id);
        }
        server.expeditions.with_mut(id, |e| {
            e.faction.name = format!("$deleted-guild-{id}");
            e.faction.owner_id = 0;
            e.is_disbanded = true;
        });
        id
    };
    save(server, id).await;
    true
}

/// `ExpeditionManager.Save(expedition)` (`Expedition.Save` in its own transaction).
pub async fn save(server: &GameServer, id: u32) {
    let Some(e) = server.expeditions.with_mut(id, |e| {
        let snapshot = e.clone();
        e.removed_members.clear();
        snapshot
    }) else {
        return;
    };
    if let Err(err) = save_expedition(&server.db, &e).await {
        tracing::error!("Expedition {id} save failed: {err}");
        // Keep the removed member ids for the next save attempt.
        server.expeditions.with_mut(id, |x| {
            for r in e.removed_members.iter().rev() {
                x.removed_members.insert(0, *r);
            }
        });
    }
}

/// `Expedition.Save(connection, transaction)`.
pub async fn save_expedition(pool: &MySqlPool, e: &Expedition) -> Result<()> {
    let mut tx = pool.begin().await?;
    if !e.removed_members.is_empty() {
        let list = e
            .removed_members
            .iter()
            .map(u32::to_string)
            .collect::<Vec<_>>()
            .join(",");
        sqlx::query(&format!(
            "DELETE FROM expedition_members WHERE character_id IN ({list})"
        ))
        .execute(&mut *tx)
        .await?;
        sqlx::query(&format!(
            "UPDATE characters SET expedition_id = 0 WHERE `characters`.`id` IN ({list})"
        ))
        .execute(&mut *tx)
        .await?;
    }
    if e.is_disbanded {
        sqlx::query("DELETE FROM expeditions WHERE `id` = ?")
            .bind(e.faction.id)
            .execute(&mut *tx)
            .await?;
        sqlx::query("DELETE FROM expedition_role_policies WHERE `expedition_id` = ?")
            .bind(e.faction.id)
            .execute(&mut *tx)
            .await?;
        sqlx::query("DELETE FROM expedition_members WHERE `expedition_id` = ?")
            .bind(e.faction.id)
            .execute(&mut *tx)
            .await?;
    } else {
        sqlx::query("REPLACE INTO expeditions(`id`,`owner`,`owner_name`,`name`,`mother`,`created_at`) VALUES (?, ?, ?, ?, ?, ?)")
            .bind(e.faction.id)
            .bind(e.faction.owner_id)
            .bind(&e.faction.owner_name)
            .bind(&e.faction.name)
            .bind(e.faction.mother_id)
            .bind(from_unix(e.faction.created))
            .execute(&mut *tx)
            .await?;
        for m in &e.members {
            sqlx::query("REPLACE INTO expedition_members(`character_id`,`expedition_id`,`name`,`level`,`role`,`last_leave_time`,`ability1`,`ability2`,`ability3`, `memo`) VALUES (?,?,?,?,?,?,?,?,?,?)")
                .bind(m.character_id)
                .bind(m.expedition_id)
                .bind(&m.name)
                .bind(m.level)
                .bind(m.role)
                .bind(from_unix(m.last_world_leave_time))
                .bind(m.abilities[0])
                .bind(m.abilities[1])
                .bind(m.abilities[2])
                .bind(&m.memo)
                .execute(&mut *tx)
                .await?;
        }
        for p in &e.policies {
            sqlx::query("REPLACE INTO expedition_role_policies(`expedition_id`,`role`,`name`,`dominion_declare`,`invite`,`expel`,`promote`,`dismiss`, `chat`, `manager_chat`, `siege_master`, `join_siege`) VALUES (?,?,?,?,?,?,?,?,?,?,?,?)")
                .bind(p.expedition_id)
                .bind(p.role)
                .bind(&p.name)
                .bind(p.dominion_declare)
                .bind(p.invite)
                .bind(p.expel)
                .bind(p.promote)
                .bind(p.dismiss)
                .bind(p.chat)
                .bind(p.manager_chat)
                .bind(p.siege_master)
                .bind(p.join_siege)
                .execute(&mut *tx)
                .await?;
        }
    }
    tx.commit().await?;
    Ok(())
}

/// `ExpeditionManager.Leave(character)` for a character that is not in the world (character
/// deletion, `CharacterManager.DeleteCharacterAssets`): the member is removed, the guild gets
/// `SCUnitExpeditionChangedPacket` and the guild is saved. Note: the C# also sends the packet
/// to the (lobby) character itself through `BroadcastPacket(.., true)`; there is no world
/// object to do that from here.
pub async fn leave_offline(server: &GameServer, character_id: u32, name: &str, expedition_id: u32) {
    let Some(e) = server.expeditions.get(expedition_id) else {
        return;
    };
    if e.member(character_id).is_none() {
        tracing::error!("Leave: {character_id} is not a member of expedition {expedition_id}");
        return;
    }
    remove_member(server, expedition_id, character_id);
    let packet = SCUnitExpeditionChangedPacket {
        obj_id: 0,
        character_id,
        kicker: String::new(),
        unit_name: name.to_string(),
        id: expedition_id,
        expedition_id: 0,
        expel: false,
    };
    let members = server
        .expeditions
        .get(expedition_id)
        .map(|e| e.member_ids())
        .unwrap_or_default();
    send_to_members(server, &members, &packet);
    save(server, expedition_id).await;
}
