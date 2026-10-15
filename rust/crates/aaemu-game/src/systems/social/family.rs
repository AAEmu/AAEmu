//! `Core/Managers/FamilyManager` + `Models/Game/Family` (`Family`, `FamilyMember`): loading,
//! invitations, creation, joining, leaving, kicking, titles, steward changes, login / logout
//! status and `Family.Save`.

use std::collections::{BTreeMap, HashMap};
use std::sync::{Mutex, MutexGuard};

use aaemu_commons::PacketStream;
use sqlx::mysql::MySqlPool;
use sqlx::Row;

use crate::db::int;
use crate::error::Result;
use crate::net::packet::ServerPacket;
use crate::server::GameServer;
use crate::systems::social::packets::family::*;
use crate::world::PlayerRef;

/// `FamilyMember`.
#[derive(Debug, Clone)]
pub struct FamilyMember {
    /// `Character` (set while the member is logged in; `Online => Character != null`).
    pub character: Option<PlayerRef>,
    pub id: u32,
    pub name: String,
    /// 1 = steward (owner).
    pub role: u8,
    pub title: String,
}

impl FamilyMember {
    pub fn online(&self) -> bool {
        self.character.is_some()
    }

    /// `FamilyMember.Write`.
    pub fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.id)
            .write_str(&self.name)
            .write_u8(self.role)
            .write_bool(self.online())
            .write_str(&self.title);
    }
}

/// `Family`.
#[derive(Debug, Clone, Default)]
pub struct Family {
    pub id: u32,
    pub members: Vec<FamilyMember>,
    /// `_removedMembers` (flushed by `Save`).
    pub removed_members: Vec<u32>,
}

/// What `Family.Write` writes (a snapshot).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct FamilyInfo {
    pub id: u32,
    /// (id, name, role, online, title)
    pub members: Vec<(u32, String, u8, bool, String)>,
}

impl FamilyInfo {
    /// `Family.Write`.
    pub fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.id).write_i32(self.members.len() as i32);
        for (id, name, role, online, title) in &self.members {
            s.write_u32(*id)
                .write_str(name)
                .write_u8(*role)
                .write_bool(*online)
                .write_str(title);
        }
    }
}

impl Family {
    pub fn info(&self) -> FamilyInfo {
        FamilyInfo {
            id: self.id,
            members: self
                .members
                .iter()
                .map(|m| (m.id, m.name.clone(), m.role, m.online(), m.title.clone()))
                .collect(),
        }
    }

    /// `GetMember(character)`.
    pub fn member(&self, id: u32) -> Option<&FamilyMember> {
        self.members.iter().find(|m| m.id == id)
    }

    fn member_mut(&mut self, id: u32) -> Option<&mut FamilyMember> {
        self.members.iter_mut().find(|m| m.id == id)
    }

    /// `RemoveMember(member)`.
    fn remove_member(&mut self, id: u32) {
        if let Some(i) = self.members.iter().position(|m| m.id == id) {
            self.members.remove(i);
        }
        self.removed_members.push(id);
    }

    /// `SendPacket(packet, exclude)`: every logged-in member except `exclude`.
    pub fn send_packet(&self, packet: &dyn ServerPacket, exclude: u32) {
        let mut out = crate::net::connection::Fanout::new(packet);
        for m in &self.members {
            if m.id != exclude {
                if let Some(c) = &m.character {
                    out.send(&c.conn);
                }
            }
        }
    }
}

#[derive(Debug, Default)]
struct State {
    /// `_families`.
    families: BTreeMap<u32, Family>,
    /// `_familyMembers` (character id -> family id).
    family_members: HashMap<u32, u32>,
}

/// `FamilyManager`.
#[derive(Debug, Default)]
pub struct FamilyManager {
    state: Mutex<State>,
}

/// The `family_members` rows of one family (`Family.Load`).
async fn load_members(pool: &MySqlPool, family_id: u32) -> Result<Vec<FamilyMember>> {
    let mut out = Vec::new();
    for r in sqlx::query(
        "SELECT CAST(character_id AS SIGNED) AS character_id, name, CAST(role AS SIGNED) AS role, title FROM family_members WHERE family_id = ?",
    )
    .bind(family_id)
    .fetch_all(pool)
    .await?
    {
        out.push(FamilyMember {
            character: None,
            id: int(&r, "character_id")? as u32,
            name: r.try_get("name")?,
            role: int(&r, "role")? as u8,
            title: r.try_get("title")?,
        });
    }
    Ok(out)
}

impl FamilyManager {
    fn lock(&self) -> MutexGuard<'_, State> {
        self.state.lock().expect("families poisoned")
    }

    /// `Load()`: one family per distinct non-zero `characters.family`. Note (C#): a character
    /// listed in two families aborts the load (`_familyMembers.Add`).
    pub async fn load(&self, pool: &MySqlPool) -> Result<()> {
        tracing::info!("Loading families...");
        let ids: Vec<u32> =
            sqlx::query("SELECT DISTINCT CAST(family AS SIGNED) AS family FROM characters")
                .fetch_all(pool)
                .await?
                .iter()
                .map(|r| int(r, "family").map(|v| v as u32))
                .collect::<Result<_>>()?;
        let mut st = State::default();
        for id in ids {
            if id == 0 {
                continue;
            }
            let members = load_members(pool, id).await?;
            for m in &members {
                if st.family_members.insert(m.id, id).is_some() {
                    return Err(crate::error::GameError::Protocol(format!(
                        "family member {} is in two families",
                        m.id
                    )));
                }
            }
            st.families.insert(
                id,
                Family {
                    id,
                    members,
                    removed_members: Vec::new(),
                },
            );
        }
        tracing::info!("Loaded {} families", st.families.len());
        *self.lock() = st;
        Ok(())
    }

    /// Adds a loaded family (tests).
    pub fn insert(&self, family: Family) {
        let mut st = self.lock();
        for m in &family.members {
            st.family_members.insert(m.id, family.id);
        }
        st.families.insert(family.id, family);
    }

    /// `GetFamily(id)` (a copy).
    pub fn family(&self, id: u32) -> Option<Family> {
        self.lock().families.get(&id).cloned()
    }

    /// `GetFamilyOfCharacter(characterId)` (0 = none).
    pub fn family_of_character(&self, character_id: u32) -> u32 {
        self.lock()
            .families
            .values()
            .find(|f| f.members.iter().any(|m| m.id == character_id))
            .map_or(0, |f| f.id)
    }

    pub fn count(&self) -> usize {
        self.lock().families.len()
    }

    /// A copy of every family (`SaveAllFamilies`).
    fn take_snapshot(&self, id: u32) -> Option<Family> {
        let mut st = self.lock();
        let f = st.families.get_mut(&id)?;
        let snap = f.clone();
        f.removed_members.clear();
        Some(snap)
    }
}

fn family_chat(server: &GameServer, id: u32) -> crate::managers::chat::ChannelRef {
    server.chat.family_chat(id, &server.ids.chat)
}

/// `AddFamilyMember(family, character, title)`: the first member is the steward (role 1, empty
/// title). Returns `false` when the character is already registered in a family (C#:
/// `_familyMembers.Add` throws after the member was appended to the family list).
fn add_family_member(
    server: &GameServer,
    st: &mut State,
    family_id: u32,
    p: &PlayerRef,
    title: Option<&str>,
) -> bool {
    let name = p.data().unit.name.clone();
    let family = st.families.get_mut(&family_id).expect("family");
    let is_owner = family.members.is_empty();
    let title = if is_owner { "" } else { title.unwrap_or("") };
    family.members.push(FamilyMember {
        character: Some(p.clone()),
        id: p.character_id,
        name,
        role: u8::from(is_owner),
        title: title.to_string(),
    });
    if st.family_members.contains_key(&p.character_id) {
        tracing::error!(
            "AddFamilyMember: {} is already a member of family {}",
            p.character_id,
            st.family_members[&p.character_id]
        );
        return false;
    }
    st.family_members.insert(p.character_id, family_id);
    p.data().family = family_id;
    family_chat(server, family_id).join(p);
    // ENROLL_FAMILY: queued, the family manager's lock is held here.
    crate::systems::refresh_achievements_later(server, p);
    true
}

/// `FamilyManager.SaveFamily(family)` (`Family.Save` in its own transaction).
pub async fn save_family(server: &GameServer, id: u32) {
    let Some(f) = server.systems.social.families.take_snapshot(id) else {
        return;
    };
    if let Err(e) = save_family_rows(&server.db, &f).await {
        tracing::error!("Family {id} save failed: {e}");
    }
}

/// `Family.Save(connection, transaction)`.
pub async fn save_family_rows(pool: &MySqlPool, f: &Family) -> Result<()> {
    let mut tx = pool.begin().await?;
    if !f.removed_members.is_empty() {
        let list = f
            .removed_members
            .iter()
            .map(u32::to_string)
            .collect::<Vec<_>>()
            .join(",");
        sqlx::query(&format!(
            "DELETE FROM family_members WHERE character_id IN ({list})"
        ))
        .execute(&mut *tx)
        .await?;
        sqlx::query(&format!(
            "UPDATE characters SET family = 0 WHERE `characters`.`id` IN ({list})"
        ))
        .execute(&mut *tx)
        .await?;
    }
    for m in &f.members {
        sqlx::query("REPLACE INTO family_members(`character_id`,`family_id`,`name`,`role`,`title`) VALUES (?,?,?,?,?)")
            .bind(m.id)
            .bind(f.id)
            .bind(&m.name)
            .bind(m.role)
            .bind(&m.title)
            .execute(&mut *tx)
            .await?;
    }
    tx.commit().await?;
    Ok(())
}

/// `InviteToFamily(inviter, invitedCharacterName, title)`. Note (C#): the family id sent is
/// always 1.
pub fn invite_to_family(server: &GameServer, inviter: &PlayerRef, invited_name: &str, title: &str) {
    let Some(invited) = server.world.character_by_name(invited_name) else {
        return;
    };
    if invited.data().family != 0 {
        return;
    }
    let inviter_name = inviter.data().unit.name.clone();
    invited.send(SCFamilyInvitationPacket {
        invitor_id: inviter.character_id,
        invitor_name: inviter_name,
        family_id: 1,
        role: title.to_string(),
    });
}

/// `ReplyToInvite(invitorId, invitedChar, join, title)`.
pub async fn reply_to_invite(
    server: &GameServer,
    invitor_id: u32,
    invited: &PlayerRef,
    join: bool,
    title: &str,
) {
    if !join {
        return;
    }
    let Some(invitor) = server.world.character_by_id(invitor_id) else {
        return;
    };
    let invitor_family = invitor.data().family;
    let fm = &server.systems.social.families;
    let saved = if invitor_family == 0 {
        // CreateFamily(invitor, invitedChar, title)
        let id = match server.ids.family.get_next_id() {
            Ok(id) => id,
            Err(e) => {
                tracing::error!("{e}");
                return;
            }
        };
        let mut st = fm.lock();
        st.families.insert(
            id,
            Family {
                id,
                members: Vec::new(),
                removed_members: Vec::new(),
            },
        );
        if !add_family_member(server, &mut st, id, &invitor, None)
            || !add_family_member(server, &mut st, id, invited, Some(title))
        {
            return;
        }
        let f = &st.families[&id];
        f.send_packet(&SCFamilyCreatedPacket { family: f.info() }, 0);
        id
    } else {
        let mut st = fm.lock();
        if !st.families.contains_key(&invitor_family) {
            // C#: `_families[invitor.Family]` throws.
            tracing::error!("ReplyToInvite: no family {invitor_family}");
            return;
        }
        if !add_family_member(server, &mut st, invitor_family, invited, Some(title)) {
            return;
        }
        let f = &st.families[&invitor_family];
        f.send_packet(
            &SCFamilyMemberAddedPacket {
                family: f.info(),
                added_index: f.members.len() as i32 - 1,
            },
            0,
        );
        invitor_family
    };
    save_family(server, saved).await;
}

/// `OnCharacterLogin(character)`: family description to the character, online status to the
/// family (the character included).
pub fn on_character_login(server: &GameServer, p: &PlayerRef) {
    let family_id = p.data().family;
    let mut st = server.systems.social.families.lock();
    let member_family = st.family_members.get(&p.character_id).copied();
    let valid = st.families.contains_key(&family_id) && member_family.is_some();
    if !valid {
        // Family no longer valid
        drop(st);
        p.data().family = 0;
        return;
    }
    // `_familyMembers[character.Id]` is the member object of its own family.
    let mf = member_family.expect("checked");
    if let Some(m) = st
        .families
        .get_mut(&mf)
        .and_then(|f| f.member_mut(p.character_id))
    {
        m.character = Some(p.clone());
    }
    let f = &st.families[&family_id];
    family_chat(server, f.id).join(p);
    p.send(SCFamilyDescPacket { family: f.info() });
    f.send_packet(
        &SCFamilyMemberOnlinePacket {
            family_id: f.id,
            member_id: p.character_id,
            online: true,
        },
        0,
    );
}

/// `OnCharacterLogout(character)`.
pub fn on_character_logout(server: &GameServer, p: &PlayerRef) {
    let family_id = p.data().family;
    let mut st = server.systems.social.families.lock();
    let Some(f) = st.families.get_mut(&family_id) else {
        // C#: `_families[character.Family]` throws.
        tracing::error!("OnCharacterLogout: no family {family_id}");
        return;
    };
    match f.member_mut(p.character_id) {
        Some(m) => m.character = None,
        None => {
            tracing::error!(
                "OnCharacterLogout: {} is not in family {family_id}",
                p.character_id
            );
            return;
        }
    }
    family_chat(server, f.id).leave(p);
    f.send_packet(
        &SCFamilyMemberOnlinePacket {
            family_id: f.id,
            member_id: p.character_id,
            online: false,
        },
        p.character_id,
    );
}

/// `DisbandFamily(family)` (with the state lock held); returns the family to save.
fn disband_family(server: &GameServer, st: &mut State, family_id: u32) {
    let f = st.families.get_mut(&family_id).expect("family");
    let removed = SCFamilyRemovedPacket { family_id };
    let members: Vec<FamilyMember> = f.members.iter().rev().cloned().collect();
    for m in &members {
        if let Some(c) = &m.character {
            family_chat(server, family_id).leave(c);
            c.send(removed.clone());
            c.data().family = 0;
        }
        f.remove_member(m.id);
    }
    for m in &members {
        st.family_members.remove(&m.id);
    }
}

/// Saves (or, after a disband, saves and removes) a family.
async fn save_or_finish_disband(server: &GameServer, family_id: u32, disbanded: bool) {
    save_family(server, family_id).await;
    if disbanded {
        server
            .systems
            .social
            .families
            .lock()
            .families
            .remove(&family_id);
    }
}

/// `LeaveFamily(character)`.
pub async fn leave_family(server: &GameServer, p: &PlayerRef) {
    let family_id = p.data().family;
    let disband = {
        let mut st = server.systems.social.families.lock();
        let Some(f) = st.families.get_mut(&family_id) else {
            tracing::error!("LeaveFamily: no family {family_id}");
            return;
        };
        if f.member(p.character_id).is_none() {
            // C#: RemoveMember(null) throws.
            tracing::error!(
                "LeaveFamily: {} is not in family {family_id}",
                p.character_id
            );
            return;
        }
        p.data().family = 0;
        f.remove_member(p.character_id);
        st.family_members.remove(&p.character_id);
        let f = &st.families[&family_id];
        p.send(SCFamilyRemovedPacket { family_id });
        f.send_packet(
            &SCFamilyMemberRemovedPacket {
                family_id,
                kicked: false,
                member_id: p.character_id,
            },
            0,
        );
        family_chat(server, family_id).leave(p);
        let disband = f.members.len() < 2;
        if disband {
            disband_family(server, &mut st, family_id);
        }
        disband
    };
    save_or_finish_disband(server, family_id, disband).await;
}

/// `Character.Load(id) != null` for an offline character (`KickMember`).
async fn character_exists(server: &GameServer, id: u32) -> bool {
    match sqlx::query("SELECT CAST(id AS SIGNED) AS id FROM characters WHERE id = ?")
        .bind(id)
        .fetch_optional(&server.db)
        .await
    {
        Ok(r) => r.is_some(),
        Err(e) => {
            tracing::error!("Character.Load({id}): {e}");
            false
        }
    }
}

/// `KickMember(kicker, kickedId)`: steward only; disbands the family when fewer than two
/// members remain.
pub async fn kick_member(server: &GameServer, kicker: &PlayerRef, kicked_id: u32) {
    let family_id = kicker.data().family;
    if family_id == 0 {
        return;
    }
    {
        let st = server.systems.social.families.lock();
        let Some(f) = st.families.get(&family_id) else {
            tracing::error!("KickMember: no family {family_id}");
            return;
        };
        match f.member(kicker.character_id) {
            Some(m) if m.role == 1 => {}
            Some(_) => return, // Only the steward can kick
            None => {
                tracing::error!("KickMember: kicker {} not in family", kicker.character_id);
                return;
            }
        }
    }
    let online = server.world.character_by_id(kicked_id);
    if online.is_none() && !character_exists(server, kicked_id).await {
        return;
    }
    let disband = {
        let mut st = server.systems.social.families.lock();
        let Some(f) = st.families.get_mut(&family_id) else {
            return;
        };
        if f.member(kicked_id).is_none() {
            // C#: RemoveMember(null) throws.
            tracing::error!("KickMember: {kicked_id} is not in family {family_id}");
            return;
        }
        if let Some(k) = &online {
            k.data().family = 0;
        }
        f.remove_member(kicked_id);
        st.family_members.remove(&kicked_id);
        if let Some(k) = &online {
            family_chat(server, family_id).leave(k);
            k.send(SCFamilyRemovedPacket { family_id });
        }
        let f = &st.families[&family_id];
        f.send_packet(
            &SCFamilyMemberRemovedPacket {
                family_id,
                kicked: true,
                member_id: kicked_id,
            },
            0,
        );
        let disband = f.members.len() < 2;
        if disband {
            disband_family(server, &mut st, family_id);
        }
        disband
    };
    save_or_finish_disband(server, family_id, disband).await;
}

/// `ChangeTitle(owner, memberId, newTitle)` (steward only).
pub fn change_title(server: &GameServer, owner: &PlayerRef, member_id: u32, new_title: &str) {
    let _ = update_title(server, owner, member_id, new_title);
}

/// Applies an authorized title change and persists the resulting family snapshot.
pub async fn change_title_and_save(
    server: &GameServer,
    owner: &PlayerRef,
    member_id: u32,
    new_title: &str,
) {
    if let Some(id) = update_title(server, owner, member_id, new_title) {
        save_family(server, id).await;
    }
}

fn update_title(
    server: &GameServer,
    owner: &PlayerRef,
    member_id: u32,
    new_title: &str,
) -> Option<u32> {
    let family_id = owner.data().family;
    if family_id == 0 {
        return None;
    }
    let mut st = server.systems.social.families.lock();
    let Some(f) = st.families.get(&family_id) else {
        tracing::error!("ChangeTitle: no family {family_id}");
        return None;
    };
    match f.member(owner.character_id) {
        Some(m) if m.role == 1 => {}
        Some(_) => return None,
        None => {
            tracing::error!("ChangeTitle: {} not in family", owner.character_id);
            return None;
        }
    }
    // The target must belong to the steward's own family.
    let Some(&mf) = st.family_members.get(&member_id) else {
        tracing::error!("ChangeTitle: unknown member {member_id}");
        return None;
    };
    if mf != family_id {
        return None;
    }
    if let Some(m) = st
        .families
        .get_mut(&mf)
        .and_then(|f| f.member_mut(member_id))
    {
        m.title = new_title.to_string();
    }
    st.families[&family_id].send_packet(
        &SCFamilyTitleChangedPacket {
            family_id,
            member_id,
            title: new_title.to_string(),
        },
        0,
    );
    Some(family_id)
}

/// `ChangeOwner(previousOwner, memberId)` (steward only).
pub fn change_owner(server: &GameServer, previous: &PlayerRef, member_id: u32) {
    let _ = update_owner(server, previous, member_id);
}

/// Applies an authorized steward change and persists the resulting family snapshot.
pub async fn change_owner_and_save(server: &GameServer, previous: &PlayerRef, member_id: u32) {
    if let Some(id) = update_owner(server, previous, member_id) {
        save_family(server, id).await;
    }
}

fn update_owner(server: &GameServer, previous: &PlayerRef, member_id: u32) -> Option<u32> {
    if member_id == previous.character_id {
        return None;
    }
    let family_id = previous.data().family;
    if family_id == 0 {
        return None;
    }
    let mut st = server.systems.social.families.lock();
    let Some(f) = st.families.get(&family_id) else {
        tracing::error!("ChangeOwner: no family {family_id}");
        return None;
    };
    match f.member(previous.character_id) {
        Some(m) if m.role == 1 => {}
        Some(_) => return None,
        None => {
            tracing::error!("ChangeOwner: {} not in family", previous.character_id);
            return None;
        }
    }
    let Some(&mf) = st.family_members.get(&member_id) else {
        tracing::error!("ChangeOwner: unknown member {member_id}");
        return None;
    };
    if mf != family_id {
        return None;
    }
    if let Some(m) = st
        .families
        .get_mut(&mf)
        .and_then(|f| f.member_mut(member_id))
    {
        m.role = 1;
    }
    if let Some(m) = st
        .families
        .get_mut(&family_id)
        .and_then(|f| f.member_mut(previous.character_id))
    {
        m.role = 0;
    }
    let f = &st.families[&family_id];
    f.send_packet(
        &SCFamilyOwnerChangedPacket {
            family_id,
            member_id,
        },
        0,
    );
    f.send_packet(&SCFamilyDescPacket { family: f.info() }, 0);
    Some(family_id)
}

/// `FamilyManager.LeaveFamily(character)` for a character that is not in the world (character
/// deletion). The C# also sends `SCFamilyRemovedPacket` to the (lobby) character.
pub async fn leave_family_offline(server: &GameServer, character_id: u32, family_id: u32) {
    let disband = {
        let mut st = server.systems.social.families.lock();
        let Some(f) = st.families.get_mut(&family_id) else {
            tracing::error!("LeaveFamily: no family {family_id}");
            return;
        };
        if f.member(character_id).is_none() {
            tracing::error!("LeaveFamily: {character_id} is not in family {family_id}");
            return;
        }
        f.remove_member(character_id);
        st.family_members.remove(&character_id);
        let f = &st.families[&family_id];
        f.send_packet(
            &SCFamilyMemberRemovedPacket {
                family_id,
                kicked: false,
                member_id: character_id,
            },
            0,
        );
        let disband = f.members.len() < 2;
        if disband {
            disband_family(server, &mut st, family_id);
        }
        disband
    };
    save_or_finish_disband(server, family_id, disband).await;
}
