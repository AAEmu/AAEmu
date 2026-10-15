//! `Core/Managers/TeamManager` + `Models/Game/Team` (parties and raids): invitations, team
//! creation, leave / kick / dismiss (`AskRiskyTeam`), owner / role / marker / looting rule /
//! ping changes, raid conversion, member moves, offline / reconnect handling and the 500 ms
//! remote-member sync with its delta cache and the zombie-team disband.
//!
//! Locking: the manager's state mutex is taken first; while holding it the code locks at most
//! one player's data at a time (snapshots) and sends packets (non-blocking). Nothing calls into
//! the manager while holding a player's data lock.

use std::collections::{BTreeMap, HashMap};
use std::sync::{Arc, Mutex, MutexGuard};

use crate::packets::world::SCUnitsRemovedPacket;
use crate::server::GameServer;
use crate::systems::social::faction::{self, relation_state};
use crate::systems::social::friend;
use crate::systems::social::packets::team::*;
use crate::systems::social::util::{self, error, send_error};
use crate::world::{PlayerRef, WorldObject};

/// `RiskyAction`.
pub mod risky_action {
    pub const LEAVE: u8 = 0;
    pub const KICK: u8 = 1;
    pub const DISMISS: u8 = 2;
}

/// `MemberRole` (0..=3 are defined).
pub mod member_role {
    pub const UNDECIDED: u8 = 0;
    pub const TANK: u8 = 1;
    pub const HEALER: u8 = 2;
    pub const ATTACKER: u8 = 3;
}

/// `LootingRuleMethod`.
pub mod looting_method {
    pub const FREE_FOR_ALL: u8 = 0;
    pub const ROTATE_WINNER: u8 = 1;
    pub const LOOT_MASTER: u8 = 2;
    pub const PUBLIC: u8 = 0xFF;
}

/// `OverHeadMark.ResetAll`.
pub const OVER_HEAD_MARK_RESET_ALL: i32 = -2;

/// `TeamSyncIntervalMs`.
pub const TEAM_SYNC_INTERVAL_MS: u64 = 500;
/// `ForcedResyncIntervalMs`.
const FORCED_RESYNC_INTERVAL_MS: i64 = 5000;
/// `OfflineTeamDisbandGrace` (5 minutes).
pub const OFFLINE_TEAM_DISBAND_GRACE_MS: i64 = 5 * 60 * 1000;
/// Invitation timeout (`activeInvitation.Time.AddSeconds(60)`).
const INVITATION_TIMEOUT_MS: i64 = 60_000;

/// `LootingRule`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct LootingRule {
    pub loot_method: u8,
    pub minimum_grade: u8,
    pub loot_master: u32,
    pub roll_for_bind_on_pickup: bool,
}

impl Default for LootingRule {
    fn default() -> Self {
        Self {
            loot_method: looting_method::ROTATE_WINNER,
            minimum_grade: 2, // Grand+
            loot_master: 0,
            roll_for_bind_on_pickup: true,
        }
    }
}

impl LootingRule {
    /// `LootingRule.Write`.
    pub fn write(&self, s: &mut aaemu_commons::PacketStream) {
        s.write_u8(self.loot_method)
            .write_u8(self.minimum_grade)
            .write_u32(self.loot_master)
            .write_bool(self.roll_for_bind_on_pickup);
    }

    /// `LootingRule.Read`.
    pub fn read(
        s: &mut aaemu_commons::PacketStream,
    ) -> std::result::Result<Self, aaemu_commons::MarshalError> {
        Ok(Self {
            loot_method: s.read_u8()?,
            minimum_grade: s.read_u8()?,
            loot_master: s.read_u32()?,
            roll_for_bind_on_pickup: s.read_bool()?,
        })
    }
}

/// `TeamMember`.
#[derive(Debug, Clone)]
pub struct TeamMember {
    /// `Character` (the object of the last session; replaced on reconnect by `ChangeStatus`).
    pub character: PlayerRef,
    pub role: u8,
    pub has_gone_round_robin: bool,
}

/// `Team`.
#[derive(Debug, Clone)]
pub struct Team {
    pub id: u32,
    pub owner_id: u32,
    pub is_party: bool,
    /// `Members` (50 slots).
    pub members: Vec<Option<TeamMember>>,
    pub looting_rule: LootingRule,
    /// `MarksList` (type, obj / character id).
    pub marks: [(u8, u32); 12],
    /// `PingPosition` (x, y, z).
    pub ping_position: (f32, f32, f32),
    /// `AllOfflineSince` (unix ms; `None` = `DateTime.MinValue`).
    pub all_offline_since_ms: Option<i64>,
}

impl Team {
    pub fn new(id: u32, owner_id: u32, is_party: bool) -> Self {
        Self {
            id,
            owner_id,
            is_party,
            members: vec![None; 50],
            looting_rule: LootingRule::default(),
            marks: [(0, 0); 12],
            ping_position: (0.0, 0.0, 0.0),
            all_offline_since_ms: None,
        }
    }

    /// `ResetMarks`.
    pub fn reset_marks(&mut self) {
        self.marks = [(0, 0); 12];
    }

    /// `IsMarked(id)`.
    pub fn is_marked(&self, id: u32) -> bool {
        self.marks.iter().any(|&(_, obj)| obj == id)
    }

    /// `MembersCount`.
    pub fn members_count(&self) -> usize {
        self.members.iter().flatten().count()
    }

    /// `MembersOnlineCount`.
    pub fn members_online_count(&self) -> usize {
        self.members
            .iter()
            .flatten()
            .filter(|m| m.character.data().is_online)
            .count()
    }

    /// `IsMember(id)`.
    pub fn is_member(&self, id: u32) -> bool {
        self.members
            .iter()
            .flatten()
            .any(|m| m.character.character_id == id)
    }

    /// `GetNewOwner` (first online member that is not the owner; 0 = none).
    pub fn new_owner(&self) -> u32 {
        self.members
            .iter()
            .flatten()
            .find(|m| m.character.character_id != self.owner_id && m.character.data().is_online)
            .map_or(0, |m| m.character.character_id)
    }

    /// `ChangeRole(id, role)`.
    pub fn change_role(&mut self, id: u32, role: u8) -> bool {
        for m in self.members.iter_mut().flatten() {
            if m.character.character_id != id {
                continue;
            }
            if m.role == role {
                return false;
            }
            m.role = role;
            return true;
        }
        false
    }

    /// `AddMember(unit)`: (slot index, party index) of the new member.
    pub fn add_member(&mut self, p: &PlayerRef) -> Option<(usize, usize)> {
        let i = self.members.iter().position(Option::is_none)?;
        self.members[i] = Some(TeamMember {
            character: p.clone(),
            role: member_role::UNDECIDED,
            has_gone_round_robin: false,
        });
        Some((i, Self::party_of(i)))
    }

    /// `RemoveMember(id)`.
    pub fn remove_member(&mut self, id: u32) -> bool {
        match self.index_of(id) {
            Some(i) => {
                self.members[i] = None;
                true
            }
            None => false,
        }
    }

    /// `MoveMember(id, id2, from, to)` (out of range indices fail like the C# `catch`).
    pub fn move_member(&mut self, from: u8, to: u8) -> bool {
        let (from, to) = (usize::from(from), usize::from(to));
        if from >= self.members.len() || to >= self.members.len() {
            return false;
        }
        self.members.swap(from, to);
        true
    }

    /// `ChangeStatus(unit)`: points the member at the character's current object.
    pub fn change_status(&mut self, p: &PlayerRef) -> Option<usize> {
        let i = self.index_of(p.character_id)?;
        if let Some(m) = self.members[i].as_mut() {
            m.character = p.clone();
        }
        Some(i)
    }

    /// `GetIndex(id)`.
    pub fn index_of(&self, id: u32) -> Option<usize> {
        self.members
            .iter()
            .position(|m| m.as_ref().is_some_and(|m| m.character.character_id == id))
    }

    /// `Team.GetParty(index)`.
    pub fn party_of(index: usize) -> usize {
        if index < 5 {
            0
        } else {
            index / 5
        }
    }

    /// `GetPartyCounts`.
    pub fn party_counts(&self) -> [u8; 10] {
        let mut res = [0u8; 10];
        for (i, m) in self.members.iter().enumerate() {
            if m.is_some() {
                res[Self::party_of(i)] += 1;
            }
        }
        res
    }

    /// The online member characters (slot order).
    pub fn online_members(&self) -> Vec<PlayerRef> {
        self.members
            .iter()
            .flatten()
            .filter(|m| m.character.data().is_online)
            .map(|m| m.character.clone())
            .collect()
    }

    /// `BroadcastPacket(packet, id)`: every online member except `exclude`.
    pub fn broadcast(&self, packet: &dyn crate::net::packet::ServerPacket, exclude: u32) {
        let mut out = crate::net::connection::Fanout::new(packet);
        for m in self.members.iter().flatten() {
            if m.character.character_id == exclude || !m.character.data().is_online {
                continue;
            }
            out.send(&m.character.conn);
        }
    }

    /// The party number of a character (`GetPartyChat`: slot / 5, 0 when not a member).
    pub fn party_number_of(&self, character_id: u32) -> u32 {
        self.index_of(character_id).map_or(0, |i| (i / 5) as u32)
    }
}

/// `InvitationTemplate`.
#[derive(Debug, Clone)]
struct Invitation {
    team_id: u32,
    owner: PlayerRef,
    target: PlayerRef,
    time_ms: i64,
    is_party: bool,
}

/// `MemberSyncState`.
#[derive(Debug, Clone, Copy, PartialEq)]
struct MemberSyncState {
    hp: i32,
    max_hp: i32,
    mp: i32,
    max_mp: i32,
    x: f32,
    y: f32,
    z: f32,
    obj_id: u32,
    last_sync_ms: i64,
}

impl MemberSyncState {
    /// `PositionEpsilon`.
    const POSITION_EPSILON: f32 = 0.01;

    /// `HasSameSnapshotAs`.
    fn same_snapshot(&self, o: &Self) -> bool {
        self.hp == o.hp
            && self.max_hp == o.max_hp
            && self.mp == o.mp
            && self.max_mp == o.max_mp
            && (self.x - o.x).abs() < Self::POSITION_EPSILON
            && (self.y - o.y).abs() < Self::POSITION_EPSILON
            && (self.z - o.z).abs() < Self::POSITION_EPSILON
            && self.obj_id == o.obj_id
    }
}

#[derive(Debug, Default)]
struct State {
    /// `_activeTeams` (by id; the C# `ConcurrentDictionary` order is unspecified).
    teams: BTreeMap<u32, Team>,
    /// `_activeInvitations` (target id -> invitation).
    invitations: HashMap<u32, Invitation>,
    /// `_lastSyncState`.
    last_sync: HashMap<(u32, u32), MemberSyncState>,
}

/// A member snapshot for `TeamMember.Write` / `WritePerson`.
pub fn member_info(server: &GameServer, m: &TeamMember) -> TeamMemberInfo {
    let pos = util::world_position(server, &m.character);
    let c = m.character.data();
    TeamMemberInfo {
        id: c.id,
        name: c.unit.name.clone(),
        race: c.race,
        gender: c.gender,
        level: c.unit.level,
        role: m.role,
        obj_id: m.character.obj_id,
        is_online: c.is_online,
        zone_id: c.unit.transform.zone_id,
        instance_id: c.unit.transform.instance_id,
        hp: c.unit.hp,
        max_hp: c.unit.max_hp,
        mp: c.unit.mp,
        max_mp: c.unit.max_mp,
        position: pos,
        abilities: [c.ability1, c.ability2, c.ability3],
    }
}

/// `Team.Write` data.
pub fn team_info(team: &Team) -> TeamInfo {
    TeamInfo {
        id: team.id,
        owner_id: team.owner_id,
        is_party: team.is_party,
        party_counts: team.party_counts(),
        slots: team
            .members
            .iter()
            .map(|m| match m {
                Some(m) => (m.character.character_id, m.character.data().is_online),
                None => (0, false),
            })
            .collect(),
        marks: team.marks,
        looting_rule: team.looting_rule,
    }
}

/// `new SCJoinedTeamPacket(team)`.
fn joined_team_packet(server: &GameServer, team: &Team) -> SCJoinedTeamPacket {
    SCJoinedTeamPacket {
        team: team_info(team),
        members: team
            .members
            .iter()
            .flatten()
            .map(|m| member_info(server, m))
            .collect(),
    }
}

/// `Character.InParty` setter (`FriendMananger.SendStatusChange(this, false, value)`).
pub fn set_in_party(server: &GameServer, p: &PlayerRef, value: bool) {
    if p.data().in_party == value {
        return;
    }
    friend::send_status_change(server, p, false, value);
    p.data().in_party = value;
    if value {
        // ENROLL_PARTY / ENROLL_RAID_GROUP: queued, the team manager's lock is held here.
        crate::systems::refresh_achievements_later(server, p);
    }
}

/// `AreInRenderDistance(a, b)`: the same or a neighbouring region (2-region neighbourhood).
pub fn are_in_render_distance(a: &PlayerRef, b: &PlayerRef) -> bool {
    let (Some(ra), Some(rb)) = (a.region(), b.region()) else {
        return false;
    };
    ra.0 == rb.0 && (ra.1 - rb.1).abs() <= 2 && (ra.2 - rb.2).abs() <= 2
}

fn owner_name(server: &GameServer, owner_id: u32) -> Option<String> {
    server
        .world
        .character_by_id(owner_id)
        .map(|p| p.data().unit.name.clone())
}

/// `ChatManager.GetPartyChat(team, character)`.
pub fn party_chat(
    server: &GameServer,
    team: &Team,
    character_id: u32,
) -> crate::managers::chat::ChannelRef {
    let name = owner_name(server, team.owner_id);
    server.chat.party_chat(
        team.id,
        team.party_number_of(character_id),
        name.as_deref(),
        &server.ids.chat,
    )
}

/// `ChatManager.GetRaidChat(team)`.
pub fn raid_chat(server: &GameServer, team: &Team) -> crate::managers::chat::ChannelRef {
    let name = owner_name(server, team.owner_id);
    server
        .chat
        .raid_chat(team.id, name.as_deref(), &server.ids.chat)
}

/// `TeamManager`.
#[derive(Debug, Default)]
pub struct TeamManager {
    state: Mutex<State>,
}

impl TeamManager {
    fn lock(&self) -> MutexGuard<'_, State> {
        self.state.lock().expect("teams poisoned")
    }

    fn team_id_by_unit(st: &State, unit_id: u32) -> Option<u32> {
        st.teams
            .values()
            .find(|t| t.is_member(unit_id))
            .map(|t| t.id)
    }

    /// `GetActiveTeamByUnit(unitId)` (a copy).
    pub fn active_team_by_unit(&self, unit_id: u32) -> Option<Team> {
        let st = self.lock();
        let id = Self::team_id_by_unit(&st, unit_id)?;
        st.teams.get(&id).cloned()
    }

    /// `GetTeamByObjId(objId)` (a copy).
    pub fn team_by_obj_id(&self, obj_id: u32) -> Option<Team> {
        self.lock()
            .teams
            .values()
            .find(|t| {
                t.members
                    .iter()
                    .flatten()
                    .any(|m| m.character.obj_id == obj_id)
            })
            .cloned()
    }

    /// `GetActiveTeam(teamId)` (a copy; `None` for 0).
    pub fn active_team(&self, team_id: u32) -> Option<Team> {
        if team_id == 0 {
            return None;
        }
        self.lock().teams.get(&team_id).cloned()
    }

    /// `AreTeamMembers(unit1, unit2)`.
    pub fn are_team_members(&self, unit1: u32, unit2: u32) -> bool {
        let st = self.lock();
        Self::team_id_by_unit(&st, unit1)
            .and_then(|id| st.teams.get(&id))
            .is_some_and(|t| t.is_member(unit2))
    }

    /// The member characters of `unit_id`'s team in slot order (`ExpeditionManager` uses the
    /// whole list, offline members included).
    pub fn team_characters_of(&self, unit_id: u32) -> Option<Vec<PlayerRef>> {
        let st = self.lock();
        let id = Self::team_id_by_unit(&st, unit_id)?;
        Some(
            st.teams[&id]
                .members
                .iter()
                .flatten()
                .map(|m| m.character.clone())
                .collect(),
        )
    }

    /// `Team.IsObjMember(objId)` for the character obj ids of a team (mates: the skills / mates
    /// subsystem checks the owner itself).
    pub fn is_obj_member(&self, team_id: u32, obj_id: u32) -> bool {
        self.lock().teams.get(&team_id).is_some_and(|t| {
            t.members
                .iter()
                .flatten()
                .any(|m| m.character.obj_id == obj_id)
        })
    }

    /// Number of active teams.
    pub fn team_count(&self) -> usize {
        self.lock().teams.len()
    }

    /// `InviteAreaToTeam(owner, teamId, isParty)`: players within 100 m that are not in a
    /// party and not hostile get an invitation.
    pub fn invite_area_to_team(
        &self,
        server: &GameServer,
        owner: &PlayerRef,
        team_id: u32,
        _is_party: bool,
    ) {
        let Some(world) = server.world.world(owner.data().unit.transform.instance_id) else {
            return;
        };
        let me = util::world_position(server, owner);
        let around: Vec<PlayerRef> = world
            .players_around(owner)
            .into_iter()
            .filter(|p| {
                let pos = util::world_position(server, p);
                let (dx, dy) = (pos.0 - me.0, pos.1 - me.1);
                dx * dx + dy * dy <= 100.0 * 100.0
            })
            .collect();
        for c in around {
            let rel = faction::player_relation(server, owner, &c);
            let in_party = c.data().in_party;
            if !in_party && (rel == relation_state::FRIENDLY || rel == relation_state::NEUTRAL) {
                self.ask_to_join(server, owner, "", team_id, false, Some(&c));
            }
        }
    }

    /// `AskToJoin(owner, targetName, teamId, isParty, targetObj)`.
    pub fn ask_to_join(
        &self,
        server: &GameServer,
        owner: &PlayerRef,
        target_name: &str,
        team_id: u32,
        is_party: bool,
        target_obj: Option<&PlayerRef>,
    ) {
        let target = match target_obj {
            Some(t) => t.clone(),
            None => match server.world.character_by_name(target_name) {
                Some(t) => t,
                None => return,
            },
        };
        // Only hostile players cannot be invited.
        if faction::player_relation(server, owner, &target) == relation_state::HOSTILE {
            send_error(owner, error::TEAM_INVITE_REFUSED);
            return;
        }
        let mut st = self.lock();
        let team = (team_id != 0)
            .then(|| st.teams.get(&team_id))
            .flatten()
            .map(|t| {
                (
                    t.id,
                    t.is_party,
                    t.owner_id,
                    t.is_marked(owner.character_id),
                )
            });
        if st.invitations.contains_key(&target.character_id) {
            owner.send(SCRejectedTeamPacket {
                name: target_name.to_string(),
                party: is_party,
            });
            return;
        }
        if let Some((_, team_is_party, team_owner, marked)) = team {
            let allowed = team_owner == owner.character_id || (!team_is_party && marked);
            if !allowed {
                // TODO (C#): ERROR NOT ALLOWED TO INVITE
                return;
            }
        }
        st.invitations.insert(
            target.character_id,
            Invitation {
                owner: owner.clone(),
                target: target.clone(),
                is_party: team.map_or(is_party, |t| t.1),
                time_ms: util::now_ms(),
                team_id: team.map_or(0, |t| t.0),
            },
        );
        drop(st);
        let owner_name = owner.data().unit.name.clone();
        target.send(SCAskToJoinTeamPacket {
            team_id: team.map_or(0, |t| t.0),
            sender_id: owner.character_id,
            sender_name: owner_name,
            is_party,
        });
    }

    /// `ReplyToJoinTeam(target, teamId, isParty, ownerId, isReject, charName, isArea)`.
    #[allow(clippy::too_many_arguments)]
    pub fn reply_to_join_team(
        &self,
        server: &GameServer,
        target: &PlayerRef,
        _team_id: u32,
        _is_party: bool,
        _owner_id: u32,
        is_reject: bool,
        _char_name: &str,
        is_area: bool,
    ) {
        let mut st = self.lock();
        let Some(inv) = st.invitations.get(&target.character_id).cloned() else {
            // TODO (C#): ERROR NO INVITATION
            return;
        };
        if is_reject || inv.time_ms + INVITATION_TIMEOUT_MS < util::now_ms() {
            let name = inv.target.data().unit.name.clone();
            inv.owner.send(SCRejectedTeamPacket {
                name,
                party: inv.is_party,
            });
            st.invitations.remove(&target.character_id);
            return;
        }
        if is_area {
            // TODO (C#)
            st.invitations.remove(&target.character_id);
            return;
        }
        match Self::team_id_by_unit(&st, inv.owner.character_id) {
            None => {
                if inv.team_id == 0 {
                    self.create_new_team(server, &mut st, &inv);
                } else {
                    st.invitations.remove(&target.character_id);
                    // TODO (C#): ERROR TEAM DO NOT EXISTS ANYMORE
                    return;
                }
            }
            Some(team_id) => {
                let team = st.teams.get_mut(&team_id).expect("team");
                if team.members_count() >= if team.is_party { 5 } else { 50 } {
                    send_error(target, error::TEAM_FULL);
                    st.invitations.remove(&inv.target.character_id);
                    return;
                }
                if let Some((slot, party)) = team.add_member(target) {
                    let team = st.teams.get(&team_id).expect("team");
                    target.send(joined_team_packet(server, team));
                    set_in_party(server, target, true);
                    target.send(SCTeamPingPosPacket {
                        has_ping: true,
                        position: team.ping_position,
                        ins_id: 0,
                    });
                    let member = team.members[slot].as_ref().expect("added");
                    team.broadcast(
                        &SCTeamMemberJoinedPacket {
                            team_id: team.id,
                            member: member_info(server, member),
                            party: party as i32,
                        },
                        target.character_id,
                    );
                    // Note (C#): the new member does not join the party / raid chat here.
                    refresh_team_member_obj_ids(team, target, false);
                }
            }
        }
        st.invitations.remove(&inv.target.character_id);
    }

    /// `CreateNewTeam(activeInvitation)`.
    fn create_new_team(&self, server: &GameServer, st: &mut State, inv: &Invitation) {
        if Self::team_id_by_unit(st, inv.owner.character_id).is_some()
            || Self::team_id_by_unit(st, inv.target.character_id).is_some()
        {
            // TODO (C#): ERROR MESSAGE ALREADY HAVE TEAM
            return;
        }
        let id = match server.ids.team.get_next_id() {
            Ok(id) => id,
            Err(e) => {
                tracing::error!("{e}");
                return;
            }
        };
        let mut team = Team::new(id, inv.owner.character_id, inv.is_party);
        if team.add_member(&inv.owner).is_none() || team.add_member(&inv.target).is_none() {
            return;
        }
        st.teams.insert(id, team);
        let team = &st.teams[&id];
        inv.owner.send(joined_team_packet(server, team));
        set_in_party(server, &inv.owner, true);
        inv.target.send(joined_team_packet(server, team));
        set_in_party(server, &inv.target, true);
        let ping = inv.owner.data().local_ping_position;
        team.broadcast(
            &SCTeamPingPosPacket {
                has_ping: true,
                position: ping,
                ins_id: 0,
            },
            0,
        );
        if !team.is_party {
            raid_chat(server, team).join(&inv.owner);
            raid_chat(server, team).join(&inv.target);
        }
        party_chat(server, team, inv.owner.character_id).join(&inv.owner);
        party_chat(server, team, inv.target.character_id).join(&inv.target);
        // Trigger events (systems::instances dungeons).
        crate::systems::instances::events::on_team_join(server, &inv.owner, team);
        crate::systems::instances::events::on_team_join(server, &inv.target, team);
        refresh_team_member_obj_ids(team, &inv.target, false);
    }

    /// `CreateSoloTeam(character, asParty)` (GM command `soloparty`).
    pub fn create_solo_team(&self, server: &GameServer, p: &PlayerRef, as_party: bool) {
        let mut st = self.lock();
        if Self::team_id_by_unit(&st, p.character_id).is_some() {
            // TODO (C#): ERROR MESSAGE ALREADY HAVE TEAM
            return;
        }
        let id = match server.ids.team.get_next_id() {
            Ok(id) => id,
            Err(e) => {
                tracing::error!("{e}");
                return;
            }
        };
        let mut team = Team::new(id, p.character_id, true);
        if team.add_member(p).is_none() {
            return;
        }
        st.teams.insert(id, team);
        let team = &st.teams[&id];
        p.send(joined_team_packet(server, team));
        set_in_party(server, p, as_party);
        let ping = p.data().local_ping_position;
        team.broadcast(
            &SCTeamPingPosPacket {
                has_ping: true,
                position: ping,
                ins_id: 0,
            },
            0,
        );
        if !team.is_party {
            raid_chat(server, team).join(p);
        }
        party_chat(server, team, p.character_id).join(p);
        // Trigger events (systems::instances dungeons).
        crate::systems::instances::events::on_team_join(server, p, team);
    }

    /// `AskRiskyTeam(unit, teamId, targetId, riskyAction)`: leave, kick or dismiss.
    ///
    /// Note (C#): the chat channels are left by `unit` (the kicker, for a kick). Destructive
    /// requests require membership; kicking and dismissing require the current owner.
    pub fn ask_risky_team(
        &self,
        server: &GameServer,
        unit: &PlayerRef,
        team_id: u32,
        target_id: u32,
        risky_action: u8,
    ) {
        let mut st = self.lock();
        self.ask_risky_team_locked(server, &mut st, unit, team_id, target_id, risky_action);
    }

    fn ask_risky_team_locked(
        &self,
        server: &GameServer,
        st: &mut State,
        unit: &PlayerRef,
        team_id: u32,
        target_id: u32,
        risky_action: u8,
    ) {
        let Some(team) = (team_id != 0).then(|| st.teams.get_mut(&team_id)).flatten() else {
            return;
        };
        if !matches!(
            risky_action,
            risky_action::LEAVE | risky_action::KICK | risky_action::DISMISS
        ) || !team.is_member(unit.character_id)
            || (matches!(risky_action, risky_action::KICK | risky_action::DISMISS)
                && team.owner_id != unit.character_id)
            || (risky_action == risky_action::LEAVE && unit.character_id != target_id)
            || (risky_action != risky_action::DISMISS && !team.is_member(target_id))
        {
            return;
        }
        let mut is_auto_disband = false;
        if !team.is_party {
            raid_chat(server, team).leave(unit);
        }
        party_chat(server, team, unit.character_id).leave(unit);

        let mut cleared = Vec::new();
        if (risky_action == risky_action::LEAVE || risky_action == risky_action::KICK)
            && team.remove_member(target_id)
        {
            if target_id == team.owner_id {
                let new_owner = team.new_owner();
                if new_owner != 0 {
                    team.owner_id = new_owner;
                    team.broadcast(
                        &SCTeamOwnerChangedPacket {
                            team_id,
                            id: new_owner,
                        },
                        0,
                    );
                } else if team.is_party {
                    // couldn't find a new leader, only party will auto-disband
                    is_auto_disband = true;
                }
            }
            team.broadcast(
                &SCTeamMemberLeavedPacket {
                    team_id,
                    id: target_id,
                    kicked: risky_action == risky_action::KICK,
                },
                0,
            );
            if let Some(target) = server.world.character_by_id(target_id) {
                set_in_party(server, &target, false);
                target.send(SCLeavedTeamPacket {
                    team_id,
                    kicked: risky_action == risky_action::KICK,
                    dismissed: false,
                });
                // Events.OnTeamLeave (systems::instances dungeons).
                crate::systems::instances::events::on_team_leave(server, &target, team_id);
            }
            cleared.push(target_id);
        }
        if team.is_party && team.members_count() <= 1 {
            is_auto_disband = true;
        }
        if team.members_online_count() == 0 {
            is_auto_disband = true;
        }
        // TODO (C#): Need to find why we need this
        team.broadcast(
            &SCTeamAckRiskyActionPacket {
                team_id,
                id: target_id,
                risky_action,
                w: 0,
                error_message: 0,
            },
            0,
        );
        let mut remove = false;
        if is_auto_disband || risky_action == risky_action::DISMISS {
            team.broadcast(&SCTeamDismissedPacket { team_id }, 0);
            let members: Vec<PlayerRef> = team
                .members
                .iter()
                .flatten()
                .map(|m| m.character.clone())
                .collect();
            for c in members {
                if !team.is_party {
                    raid_chat(server, team).leave(&c);
                }
                party_chat(server, team, c.character_id).leave(&c);
                if c.data().is_online {
                    c.send(SCLeavedTeamPacket {
                        team_id,
                        kicked: false,
                        dismissed: true,
                    });
                    set_in_party(server, &c, false);
                    // trigger event (systems::instances dungeons)
                    crate::systems::instances::events::on_team_leave(server, &c, team_id);
                }
                cleared.push(c.character_id);
            }
            remove = true;
        }
        for id in cleared {
            clear_sync_state(st, id);
        }
        if remove {
            st.teams.remove(&team_id);
        }
        // TODO (C#): Add this to a timer or trigger instead of calling on a party/raid disband.
        server.chat.clean_up_channels(&server.ids.chat);
    }

    /// `MakeTeamOwner(unit, teamId, memberId)`.
    pub fn make_team_owner(&self, unit: &PlayerRef, team_id: u32, member_id: u32) {
        let mut st = self.lock();
        let Some(team) = (team_id != 0).then(|| st.teams.get_mut(&team_id)).flatten() else {
            return;
        };
        if team.owner_id != unit.character_id || team.owner_id == member_id {
            return;
        }
        if team.is_member(member_id) {
            team.owner_id = member_id;
        }
        team.broadcast(
            &SCTeamOwnerChangedPacket {
                team_id: team.id,
                id: team.owner_id,
            },
            0,
        );
    }

    /// `ConvertToRaid(owner, teamId)`.
    pub fn convert_to_raid(&self, server: &GameServer, owner: &PlayerRef, team_id: u32) {
        let mut st = self.lock();
        let Some(team) = (team_id != 0).then(|| st.teams.get_mut(&team_id)).flatten() else {
            return;
        };
        if team.owner_id != owner.character_id {
            return;
        }
        team.is_party = false;
        team.broadcast(&SCTeamBecameRaidTeamPacket { team_id: team.id }, 0);
        for m in team.members.iter().flatten() {
            raid_chat(server, team).join(&m.character);
        }
        refresh_all_team_member_obj_ids(team);
        // TODO (C#): Handle raids in dungeons
    }

    /// `SetTeamMemberRole(unit, teamId, memberId, role)`.
    pub fn set_team_member_role(&self, unit: &PlayerRef, team_id: u32, member_id: u32, role: u8) {
        let role = if role <= member_role::ATTACKER {
            role
        } else {
            member_role::UNDECIDED
        };
        let mut st = self.lock();
        let Some(team) = (team_id != 0).then(|| st.teams.get_mut(&team_id)).flatten() else {
            return;
        };
        if unit.character_id != member_id {
            return;
        }
        if team.change_role(member_id, role) {
            team.broadcast(
                &SCTeamMemberRoleChangedPacket {
                    team_id: team.id,
                    member_id,
                    role,
                },
                0,
            );
        }
    }

    /// `SetOverHeadMarker(unit, teamId, index, type, targetId)`.
    pub fn set_over_head_marker(
        &self,
        unit: &PlayerRef,
        team_id: u32,
        mut index: i32,
        mut kind: u8,
        mut target_id: u32,
    ) {
        let mut st = self.lock();
        let Some(team) = (team_id != 0).then(|| st.teams.get_mut(&team_id)).flatten() else {
            return;
        };
        if team.owner_id != unit.character_id && !team.is_party {
            return;
        }
        // Enum.IsDefined(OverHeadMark): -2 and 0..=11.
        if (0..=11).contains(&index) && kind <= 2 {
            team.marks[index as usize] = (kind, if kind != 0 { target_id } else { 0 });
        } else {
            team.reset_marks();
            index = OVER_HEAD_MARK_RESET_ALL;
            kind = 100;
            target_id = 0;
        }
        team.broadcast(
            &SCOverHeadMarkerSetPacket {
                team_id,
                index,
                is_obj_id: kind == 2,
                id: target_id,
            },
            0,
        );
    }

    /// `ChangeLootingRule(owner, teamId, flags, method, grade, master, rollForBop)`.
    pub fn change_looting_rule(
        &self,
        owner: &PlayerRef,
        team_id: u32,
        flags: u8,
        rule: LootingRule,
    ) {
        let mut st = self.lock();
        let Some(team) = (team_id != 0).then(|| st.teams.get_mut(&team_id)).flatten() else {
            return;
        };
        if team.owner_id != owner.character_id {
            return;
        }
        if flags & 0x08 != 0 {
            team.looting_rule.roll_for_bind_on_pickup = rule.roll_for_bind_on_pickup;
        }
        if flags & 0x04 != 0 {
            team.looting_rule.loot_master = rule.loot_master;
        }
        if flags & 0x02 != 0 {
            team.looting_rule.minimum_grade = rule.minimum_grade;
        }
        if flags & 0x01 != 0 {
            team.looting_rule.loot_method = rule.loot_method;
            if team.looting_rule.loot_method != looting_method::LOOT_MASTER {
                team.looting_rule.loot_master = 0;
            }
        }
        team.broadcast(
            &SCTeamLootingRuleChangedPacket {
                team_id,
                looting_rule: team.looting_rule,
                change_flags: flags,
            },
            0,
        );
    }

    /// `SetPingPos(unit, teamId, hasPing, position, insId)`.
    pub fn set_ping_pos(
        &self,
        unit: &PlayerRef,
        team_id: u32,
        has_ping: bool,
        position: (f32, f32, f32),
        ins_id: u32,
    ) {
        let mut st = self.lock();
        let Some(team) = (team_id != 0).then(|| st.teams.get_mut(&team_id)).flatten() else {
            return;
        };
        if team.owner_id != unit.character_id && !team.is_marked(unit.character_id) {
            return;
        }
        team.ping_position = position;
        team.broadcast(
            &SCTeamPingPosPacket {
                has_ping,
                position,
                ins_id,
            },
            0,
        );
    }

    /// `MoveTeamMember(owner, teamId, targetId, target2Id, fromIndex, toIndex)`.
    #[allow(clippy::too_many_arguments)]
    pub fn move_team_member(
        &self,
        server: &GameServer,
        owner: &PlayerRef,
        team_id: u32,
        target_id: u32,
        target2_id: u32,
        from: u8,
        to: u8,
    ) {
        let mut st = self.lock();
        let Some(team) = (team_id != 0).then(|| st.teams.get_mut(&team_id)).flatten() else {
            return;
        };
        if team.owner_id != owner.character_id {
            return;
        }
        let t1 = server.world.character_by_id(target_id);
        let t2 = server.world.character_by_id(target2_id);
        for t in [&t1, &t2].into_iter().flatten() {
            party_chat(server, team, t.character_id).leave(t);
        }
        if team.move_member(from, to) {
            team.broadcast(
                &SCTeamMemberMovedPacket {
                    team_id,
                    id_from: target_id,
                    id_to: target2_id,
                    from,
                    to,
                },
                0,
            );
            for t in [&t1, &t2].into_iter().flatten() {
                party_chat(server, team, t.character_id).join(t);
            }
        }
    }

    /// `SetOffline(unit)` (from the `Character.IsOnline` setter, after the flag is cleared).
    pub fn set_offline(&self, server: &GameServer, unit: &PlayerRef) {
        let mut st = self.lock();
        let Some(team_id) = Self::team_id_by_unit(&st, unit.character_id) else {
            return;
        };
        let team = st.teams.get_mut(&team_id).expect("team");
        let Some(slot) = team.change_status(unit) else {
            return;
        };
        if team.owner_id == unit.character_id {
            let new_owner = team.new_owner();
            if new_owner != 0 {
                team.owner_id = new_owner;
                team.broadcast(
                    &SCTeamOwnerChangedPacket {
                        team_id,
                        id: new_owner,
                    },
                    unit.character_id,
                );
            }
        }
        let member = team.members[slot].as_ref().expect("member");
        team.broadcast(
            &SCTeamMemberDisconnectedPacket {
                team_id,
                id: unit.character_id,
                member: member_info(server, member),
            },
            0,
        );
        for m in team.members.iter().flatten() {
            let c = &m.character;
            if c.character_id == unit.character_id || !c.data().is_online {
                continue;
            }
            if are_in_render_distance(c, unit) {
                continue;
            }
            c.send(SCUnitsRemovedPacket {
                ids: vec![unit.obj_id],
            });
        }
        let all_offline = team.members_online_count() == 0;
        if all_offline {
            team.all_offline_since_ms = Some(util::now_ms());
        }
        clear_sync_state(&mut st, unit.character_id);
    }

    /// `MemberRemoveFromTeam(unit, source, leaveType)`.
    pub fn member_remove_from_team(
        &self,
        server: &GameServer,
        unit: &PlayerRef,
        source: &PlayerRef,
        leave_type: u8,
    ) {
        let mut st = self.lock();
        let Some(team_id) = Self::team_id_by_unit(&st, unit.character_id) else {
            return;
        };
        let team = st.teams.get_mut(&team_id).expect("team");
        if team.change_status(unit).is_none() {
            return;
        }
        // `activeTeam.ChangeStatus(source)` (source must be in the same team object).
        if team.change_status(source).is_none() {
            return;
        }
        if Self::team_id_by_unit(&st, source.character_id) != Some(team_id) {
            return;
        }
        let team = &st.teams[&team_id];
        if !team.is_party {
            raid_chat(server, team).leave(unit);
        }
        party_chat(server, team, unit.character_id).leave(unit);
        self.ask_risky_team_locked(
            server,
            &mut st,
            source,
            team_id,
            unit.character_id,
            leave_type,
        );
    }

    /// `UpdatePosition(id)` (from `Character.SetPosition` when `InParty`).
    pub fn update_position(&self, server: &GameServer, id: u32) {
        let st = self.lock();
        let Some(team_id) = Self::team_id_by_unit(&st, id) else {
            return;
        };
        let team = &st.teams[&team_id];
        let Some(i) = team.index_of(id) else {
            return;
        };
        let member = team.members[i].as_ref().expect("member");
        team.broadcast(
            &SCTeamRemoteMembersExPacket {
                members: vec![member_info(server, member)],
            },
            id,
        );
    }

    /// `UpdateAtLogin(unit)` (from `CSNotifyInGamePacket`).
    pub fn update_at_login(&self, server: &GameServer, unit: &PlayerRef) {
        let mut st = self.lock();
        let Some(team_id) = Self::team_id_by_unit(&st, unit.character_id) else {
            return;
        };
        let team = st.teams.get_mut(&team_id).expect("team");
        // Cancel the zombie-team disband timer.
        team.all_offline_since_ms = None;
        team.change_status(unit);
        let team = &st.teams[&team_id];
        unit.send(joined_team_packet(server, team));
        set_in_party(server, unit, true);
        let others: Vec<TeamMemberInfo> = team
            .members
            .iter()
            .flatten()
            .filter(|m| m.character.character_id != unit.character_id)
            .map(|m| member_info(server, m))
            .collect();
        if !others.is_empty() {
            unit.send(SCTeamRemoteMembersExPacket { members: others });
        }
        if let Some(idx) = team.index_of(unit.character_id) {
            let member = team.members[idx].as_ref().expect("member");
            let info = member_info(server, member);
            team.broadcast(
                &SCTeamMemberJoinedPacket {
                    team_id: team.id,
                    member: info.clone(),
                    party: Team::party_of(idx) as i32,
                },
                unit.character_id,
            );
            team.broadcast(
                &SCTeamRemoteMembersExPacket {
                    members: vec![info],
                },
                unit.character_id,
            );
        }
        refresh_team_member_obj_ids(team, unit, true);
        if !team.is_party {
            raid_chat(server, team).join(unit);
        }
        party_chat(server, team, unit.character_id).join(unit);
    }

    /// `UpdateAllTeams` (the 500 ms tick): zombie-team disband, then the remote-member sync
    /// for every out-of-render-distance pair of online members (delta-cached, forced every
    /// 5 s), coalesced per recipient.
    pub fn update_all_teams(&self, server: &GameServer, now_ms: i64) {
        let mut st = self.lock();
        if st.teams.is_empty() {
            return;
        }
        let ids: Vec<u32> = st.teams.keys().copied().collect();
        let mut disbanded = false;
        for id in ids {
            let since = st.teams[&id].all_offline_since_ms;
            if since.is_some_and(|s| now_ms - s >= OFFLINE_TEAM_DISBAND_GRACE_MS) {
                disbanded |= Self::disband_offline_team(server, &mut st, id);
                continue;
            }
            let online: Vec<TeamMember> = st.teams[&id]
                .members
                .iter()
                .flatten()
                .filter(|m| m.character.data().is_online)
                .cloned()
                .collect();
            if online.len() <= 1 {
                continue;
            }
            let infos: Vec<TeamMemberInfo> =
                online.iter().map(|m| member_info(server, m)).collect();
            // (recipient index, sources) in first-seen order.
            let mut per_recipient: Vec<(usize, Vec<usize>)> = Vec::new();
            let mut add = |r: usize, s: usize| match per_recipient.iter_mut().find(|(x, _)| *x == r)
            {
                Some((_, v)) => v.push(s),
                None => per_recipient.push((r, vec![s])),
            };
            for i in 0..online.len() {
                for j in i + 1..online.len() {
                    if are_in_render_distance(&online[i].character, &online[j].character) {
                        continue;
                    }
                    if try_claim_snapshot(&mut st, &infos[i], &infos[j], now_ms) {
                        add(i, j);
                    }
                    if try_claim_snapshot(&mut st, &infos[j], &infos[i], now_ms) {
                        add(j, i);
                    }
                }
            }
            for (r, sources) in per_recipient {
                online[r].character.send(SCTeamRemoteMembersExPacket {
                    members: sources.into_iter().map(|s| infos[s].clone()).collect(),
                });
            }
        }
        drop(st);
        if disbanded {
            server.chat.clean_up_channels(&server.ids.chat);
        }
    }

    /// `DisbandOfflineTeam(team)`: returns whether the team was removed.
    fn disband_offline_team(server: &GameServer, st: &mut State, team_id: u32) -> bool {
        let team = st.teams.get_mut(&team_id).expect("team");
        if team.members_online_count() > 0 {
            team.all_offline_since_ms = None;
            return false;
        }
        let members: Vec<PlayerRef> = team
            .members
            .iter()
            .flatten()
            .map(|m| m.character.clone())
            .collect();
        for c in &members {
            set_in_party(server, c, false);
            clear_sync_state(st, c.character_id);
        }
        st.teams.remove(&team_id);
        tracing::info!(
            "Disbanded zombie team {team_id} after {} ms of all-offline",
            OFFLINE_TEAM_DISBAND_GRACE_MS
        );
        true
    }

    /// `Team.GetNextLootWinner(eligiblePlayer, referenceLootOwner, maxRange)`: a random eligible
    /// member that has not won since the last reset (round robin).
    pub fn next_loot_winner(&self, team_id: u32, eligible: &[u32]) -> Option<PlayerRef> {
        let mut st = self.lock();
        let team = st.teams.get_mut(&team_id)?;
        let eligible_slots: Vec<usize> = team
            .members
            .iter()
            .enumerate()
            .filter(|(_, m)| {
                m.as_ref()
                    .is_some_and(|m| eligible.contains(&m.character.character_id))
            })
            .map(|(i, _)| i)
            .collect();
        if eligible_slots.iter().all(|&i| {
            team.members[i]
                .as_ref()
                .is_some_and(|m| m.has_gone_round_robin)
        }) {
            for &i in &eligible_slots {
                if let Some(m) = team.members[i].as_mut() {
                    m.has_gone_round_robin = false;
                }
            }
        }
        let remaining: Vec<usize> = eligible_slots
            .into_iter()
            .filter(|&i| {
                team.members[i]
                    .as_ref()
                    .is_some_and(|m| !m.has_gone_round_robin)
            })
            .collect();
        if remaining.is_empty() {
            tracing::warn!(
                "Was unable to generate a new random looting order list for team {team_id} with leader {}",
                team.owner_id
            );
            return None;
        }
        let pick = remaining[rand::random::<usize>() % remaining.len()];
        let m = team.members[pick].as_mut().expect("member");
        m.has_gone_round_robin = true;
        Some(m.character.clone())
    }

    /// `GetNextEligibleLooter(teamId, owner)`: the first member within 200 m of `owner_pos`
    /// that has not looted in this round, else resets the round to the first member.
    pub fn next_eligible_looter(
        &self,
        server: &GameServer,
        team_id: u32,
        owner_pos: (f32, f32, f32),
    ) -> Option<PlayerRef> {
        let mut st = self.lock();
        let team = st.teams.get_mut(&team_id)?;
        for m in team.members.iter_mut().flatten() {
            if m.has_gone_round_robin {
                continue;
            }
            let pos = util::world_position(server, &m.character);
            if util::distance(pos, owner_pos, true) >= 200.0 {
                continue;
            }
            m.has_gone_round_robin = true;
            return Some(m.character.clone());
        }
        let mut ret: Option<PlayerRef> = None;
        for m in team.members.iter_mut().flatten() {
            m.has_gone_round_robin = ret.is_none();
            if ret.is_none() {
                ret = Some(m.character.clone());
            }
        }
        ret
    }

    /// Starts the `TickManager` subscription (`Load()`): `UpdateAllTeams` every 500 ms.
    pub fn start_tick(server: &Arc<GameServer>) {
        let weak = Arc::downgrade(server);
        tokio::spawn(async move {
            let mut iv =
                tokio::time::interval(std::time::Duration::from_millis(TEAM_SYNC_INTERVAL_MS));
            loop {
                iv.tick().await;
                let Some(server) = weak.upgrade() else {
                    break;
                };
                server
                    .systems
                    .social
                    .teams
                    .update_all_teams(&server, util::now_ms());
            }
        });
        tracing::info!("TeamManager loaded with {TEAM_SYNC_INTERVAL_MS}ms remote-sync interval");
    }
}

/// `TryClaimSnapshot(recipient, source, now)`.
fn try_claim_snapshot(
    st: &mut State,
    recipient: &TeamMemberInfo,
    source: &TeamMemberInfo,
    now_ms: i64,
) -> bool {
    let key = (recipient.id, source.id);
    let next = MemberSyncState {
        hp: source.hp,
        max_hp: source.max_hp,
        mp: source.mp,
        max_mp: source.max_mp,
        x: source.position.0,
        y: source.position.1,
        z: source.position.2,
        obj_id: source.obj_id,
        last_sync_ms: now_ms,
    };
    if let Some(prev) = st.last_sync.get(&key) {
        if prev.same_snapshot(&next) && now_ms - prev.last_sync_ms < FORCED_RESYNC_INTERVAL_MS {
            return false;
        }
    }
    st.last_sync.insert(key, next);
    true
}

/// `ClearSyncState(characterId)`.
fn clear_sync_state(st: &mut State, character_id: u32) {
    st.last_sync
        .retain(|&(r, s), _| r != character_id && s != character_id);
}

/// `RefreshTeamMemberObjIds(team, newMember, force)`.
fn refresh_team_member_obj_ids(team: &Team, new_member: &PlayerRef, force: bool) {
    for m in team.members.iter().flatten() {
        let c = &m.character;
        if c.character_id == new_member.character_id || !c.data().is_online {
            continue;
        }
        if !force && are_in_render_distance(c, new_member) {
            continue;
        }
        c.send(SCRefreshTeamMemberPacket {
            team_id: team.id,
            member_id: new_member.character_id,
            obj_id: new_member.obj_id,
        });
        new_member.send(SCRefreshTeamMemberPacket {
            team_id: team.id,
            member_id: c.character_id,
            obj_id: c.obj_id,
        });
    }
}

/// `RefreshAllTeamMemberObjIds(team)` (upper-triangle pairs).
fn refresh_all_team_member_obj_ids(team: &Team) {
    let online = team.online_members();
    for i in 0..online.len() {
        for j in i + 1..online.len() {
            if are_in_render_distance(&online[i], &online[j]) {
                continue;
            }
            online[i].send(SCRefreshTeamMemberPacket {
                team_id: team.id,
                member_id: online[j].character_id,
                obj_id: online[j].obj_id,
            });
            online[j].send(SCRefreshTeamMemberPacket {
                team_id: team.id,
                member_id: online[i].character_id,
                obj_id: online[i].obj_id,
            });
        }
    }
}

/// `WorldObject` of a player (helpers for other subsystems).
pub fn as_object(p: &PlayerRef) -> WorldObject {
    WorldObject::from(p)
}
