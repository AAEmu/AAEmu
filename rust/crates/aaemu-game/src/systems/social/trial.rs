//! `Core/Managers/TrialManager` + `Models/Game/Crime/{TrialData,TrialCourtRoom,TrialJuryBox,
//! TrialStep,TrialSentenceResult,CourtRoomRegion}` + `Models/Tasks/Crime/TrialUpdateTask` and
//! `ChatManager.GetTrialChat(character)`.
//!
//! Locking: one mutex holds the court rooms, the trials and the jury queues. While holding it the
//! code locks at most one player's / doodad's data at a time (copy, release, act), sends packets
//! (non-blocking), joins / leaves chat channels, broadcasts to the players around an object and
//! reads the crime manager (order: trials -> crimes -> object data). Nothing calls into this
//! manager while holding a player's data lock. Calls that may re-enter other subsystems (buff
//! event dispatch, `PostUpdateCurrentHp`, `ForceDismount`, `RemoveBuffs` by tag, `SetFaction`)
//! are collected as [`Effect`]s and run after the mutex is released. The `check_buff_tag` /
//! `judge_obj_id` hooks are called while the mutex is held (they must not call back into the
//! trial manager).
//!
//! Time: every function that reads `DateTime.UtcNow` takes `now_ms` (unix ms); the 5 s
//! `TrialUpdateTask` calls [`TrialManager::update_tick`] with the real time.

use std::collections::BTreeMap;
use std::sync::{Arc, Mutex, MutexGuard};

use crate::data::skills::BuffKind;
use crate::data::world_data::factions;
use crate::managers::chat::ChannelRef;
use crate::net::packet::ServerPacket;
use crate::packets::world::{SCChatMessagePacket, SCUnitPointsPacket};
use crate::server::GameServer;
use crate::systems::skills::packets_special::SCLoadInstancePacket;
use crate::systems::social::chat::{chat_type, court_room_region};
use crate::systems::social::config::SpawnPosition;
use crate::systems::social::crime::{
    self, add_own_buff, buff, crime_state, fmt_vec3, set_crime_point, set_infamy_point,
    set_jury_point, sus_log, world_pos_rot, CrimeEvent,
};
use crate::systems::social::packets::trial::*;
use crate::systems::social::util::{self, error, send_error};
use crate::world::{broadcast, DoodadRef, PlayerRef, WorldObject};

/// `TrialManager.TrialInitSeconds`.
pub const TRIAL_INIT_SECONDS: i64 = 10;
/// `TrialManager.AwaitingJurySummonsMinutes`.
pub const AWAITING_JURY_SUMMONS_MINUTES: i64 = 2;
const CONFIRM_CRIMINAL_RECORD_MINUTES: i64 = 5;
const CLOSING_STATEMENT_MINUTES: i64 = 1;
const JURY_VERDICT_MINUTES: i64 = 1;
const END_TRIAL_SECONDS: i64 = 30;
/// `TrialUpdateTask` interval.
pub const TRIAL_TICK_INTERVAL_MS: u64 = 5000;

/// `DateTime.MaxValue` as a step end time.
const TIME_MAX: i64 = i64::MAX;

const MINUTE_MS: i64 = 60_000;
const SECOND_MS: i64 = 1000;
// Durations on the 1.2 wire and on buffs are signed milliseconds.
const MAX_JAIL_MINUTES: i32 = i32::MAX / 60_000;

/// `TrialStep`.
pub mod trial_step {
    /// 0 - Actual trial has not started yet, waiting in court jail.
    pub const DEFENDANT_AWAITING_TRIAL: u8 = 0;
    /// 1 - Initialize the trial, send crime records and stuff to defendant.
    pub const VERIFY_CRIMINAL_RECORD: u8 = 1;
    /// 2 - Send out jury invites and wait for them.
    pub const AWAITING_JURY_SUMMONS: u8 = 2;
    /// 3 - Shows crime records to participants.
    pub const CONFIRM_CRIMINAL_RECORD: u8 = 3;
    /// 4 - Defendant closing statement (asks to skip).
    pub const CLOSING_STATEMENT: u8 = 4;
    /// 5 - Jury decides verdict (shows selection box to jury).
    pub const JURY_VERDICT: u8 = 5;
    /// 6 - Trial canceled => default sentence.
    pub const TRIAL_CANCELLED: u8 = 6;
    /// 7 - Defendant plead guilty.
    pub const PLEAD_GUILTY: u8 = 7;
    /// 8 - Trial has ended.
    pub const END_TRIAL: u8 = 8;
    /// Internal cleanup.
    pub const CLEANUP: u8 = 9;
    /// No longer active.
    pub const INVALID: u8 = 10;
}

/// `TrialSentenceResult`.
pub mod sentence_result {
    pub const UNDEFINED: u8 = 0;
    pub const NOT_GUILTY: u8 = 1;
    pub const GUILTY1: u8 = 2;
    pub const GUILTY2: u8 = 3;
    pub const GUILTY3: u8 = 4;
    pub const GUILTY4: u8 = 5;
    pub const GUILTY5: u8 = 6;
}

use trial_step as step;

/// `TrialCourtRoom`.
#[derive(Debug, Clone)]
pub struct CourtRoom {
    pub id: u32,
    pub name: String,
    /// [`court_room_region`].
    pub region: u8,
    /// `FactionsEnum`.
    pub faction: u32,
    /// `JudgeSpawner` (an opaque spawner id registered by the npcs subsystem; the judge's obj id
    /// is resolved through `SocialHooks::judge_obj_id`).
    pub judge_spawner: Option<u32>,
    pub jury_seats: BTreeMap<i32, DoodadRef>,
    pub audience_seats: Vec<DoodadRef>,
    pub audience_members: Vec<PlayerRef>,
    pub defendant: SpawnPosition,
    /// `Jail` (the configuration's `HoldingCell`).
    pub jail: SpawnPosition,
    /// `TrialChatChannel` (`None` = the C# null channel of an invalid region).
    pub chat: Option<ChannelRef>,
    /// `CurrentTrial` (trial id).
    pub current_trial: Option<u32>,
}

impl CourtRoom {
    /// `ClearRoom()`.
    pub fn clear_room(&mut self) {
        self.current_trial = None;
        self.jury_seats.clear();
    }

    fn join_chat(&self, p: &PlayerRef) {
        match &self.chat {
            Some(c) => {
                c.join(p);
            }
            None => tracing::error!("Court room {} has no trial chat channel", self.id),
        }
    }

    fn leave_chat(&self, p: &PlayerRef) {
        match &self.chat {
            Some(c) => {
                c.leave(p);
            }
            None => tracing::error!("Court room {} has no trial chat channel", self.id),
        }
    }
}

/// `TrialJuryBox`.
#[derive(Debug, Clone)]
pub struct JuryBox {
    pub seat: DoodadRef,
    pub seat_id: i32,
    pub jury_member: Option<PlayerRef>,
    pub confirm_testimony: bool,
    pub selected_sentence: i32,
}

/// `TrialData`.
#[derive(Debug, Clone)]
pub struct TrialData {
    pub id: u32,
    /// [`court_room_region`] of the court handling this case.
    pub court_region: u8,
    /// `CourtRoom` (court room id).
    pub court_room: Option<u32>,
    /// [`trial_step`].
    pub step: u8,
    pub defendant: PlayerRef,
    pub defendant_id: u32,
    pub defendant_name: String,
    /// `CurrentStepEndTime` (unix ms; `i64::MAX` = `DateTime.MaxValue`).
    pub current_step_end_ms: i64,
    pub jury: BTreeMap<i32, JuryBox>,
    /// Characters actually invited to this case.
    pub invited_jury: Vec<u32>,
    /// Minutes.
    pub jail_time: i32,
    pub evidence: Vec<CrimeEvent>,
}

impl TrialData {
    /// `RemainingCurrentStepTime` (`(uint)TotalMilliseconds`, saturating like .NET 10).
    pub fn remaining_ms(&self, now_ms: i64) -> u32 {
        self.current_step_end_ms
            .saturating_sub(now_ms)
            .clamp(0, i64::from(u32::MAX)) as u32
    }

    /// `SendPackets(packet)`: the defendant and every jury member.
    pub fn send_packets(&self, packet: &dyn ServerPacket) {
        self.defendant.send_dyn(packet);
        for j in self.jury.values() {
            if let Some(m) = &j.jury_member {
                m.send_dyn(packet);
            }
        }
    }

    /// `GetActiveJuryCount()`.
    pub fn active_jury_count(&self) -> i32 {
        if self.court_room.is_none() {
            return 0;
        }
        self.jury
            .values()
            .filter(|j| j.jury_member.as_ref().is_some_and(|m| m.data().is_online))
            .count() as i32
    }

    /// `AllJuryConfirmedCriminalRecords()`.
    pub fn all_jury_confirmed_criminal_records(&self) -> bool {
        self.jury.values().all(|j| {
            !j.jury_member.as_ref().is_some_and(|m| m.data().is_online) || j.confirm_testimony
        })
    }

    /// `AllJurySelectedSentence()`.
    pub fn all_jury_selected_sentence(&self) -> bool {
        self.jury.values().all(|j| {
            !j.jury_member.as_ref().is_some_and(|m| m.data().is_online) || j.selected_sentence >= 0
        })
    }

    fn state_packet(&self, cur_jury: i32, now_ms: i64) -> SCChangeTrialStatePacket {
        SCChangeTrialStatePacket {
            trial_id: self.id,
            state: self.step,
            cur_jury,
            remain_time: self.remaining_ms(now_ms),
        }
    }

    fn send_state(&self, now_ms: i64) {
        self.send_packets(&self.state_packet(self.active_jury_count(), now_ms));
    }
}

/// Work deferred until the trial mutex is released (see the module docs).
#[derive(Debug)]
pub enum Effect {
    /// `systems::dispatch_buff_events` after buffs changed.
    DispatchBuffs(PlayerRef),
    /// `Character.PostUpdateCurrentHp(self, oldHp, newHp, KillReason.PvpEnemy)`.
    PostUpdateHp(PlayerRef, i32, i32),
    /// `Character.ForceDismount()`.
    ForceDismount(PlayerRef),
    /// `Buffs.RemoveBuffs(kind, count, tag)`.
    RemoveBuffsByTag(PlayerRef, BuffKind, i32, u32),
    /// `Character.SetFaction(factionId)`.
    SetFaction(PlayerRef, u32),
    /// Update world membership and visibility after a justice teleport, outside the trial lock.
    Teleport(PlayerRef, crate::models::transform::Transform),
}

type Effects = Vec<Effect>;

/// Runs deferred effects; `SetFaction` (async) is spawned on the runtime.
fn run_effects(server: &GameServer, fx: Effects) {
    let mut factions = Vec::new();
    for e in fx {
        match e {
            Effect::SetFaction(p, f) => factions.push((p, f)),
            other => run_effect(server, other),
        }
    }
    if factions.is_empty() {
        return;
    }
    match server.self_arc() {
        Some(s) => {
            tokio::spawn(async move {
                for (p, f) in factions {
                    crate::systems::social::faction::set_faction(&s, &p, f).await;
                }
            });
        }
        None => tracing::error!("SetFaction after a trial: server handle not available"),
    }
}

async fn run_effects_async(server: &GameServer, fx: Effects) {
    for e in fx {
        match e {
            Effect::SetFaction(p, f) => {
                crate::systems::social::faction::set_faction(server, &p, f).await;
            }
            other => run_effect(server, other),
        }
    }
}

fn run_effect(server: &GameServer, e: Effect) {
    let hooks = server.systems.social.hooks.get();
    match e {
        Effect::DispatchBuffs(p) => {
            crate::systems::dispatch_buff_events(server, &WorldObject::from(&p));
        }
        Effect::PostUpdateHp(p, old, new) => hooks.post_update_current_hp(server, &p, old, new),
        Effect::ForceDismount(p) => hooks.force_dismount(server, &p),
        Effect::RemoveBuffsByTag(p, kind, count, tag) => {
            hooks.remove_buffs_by_tag(server, &p, kind, count, tag);
            crate::systems::dispatch_buff_events(server, &WorldObject::from(&p));
        }
        Effect::SetFaction(..) => unreachable!("handled by the callers"),
        Effect::Teleport(p, destination) => {
            let old_instance = p.data().unit.transform.instance_id;
            let Some(world) = server.world.world(destination.instance_id) else {
                tracing::error!(
                    "Justice teleport: missing instance {}",
                    destination.instance_id
                );
                return;
            };
            if old_instance != destination.instance_id {
                if let Some(old) = server.world.world(old_instance) {
                    old.delete(&p);
                }
            }
            p.data().unit.transform = destination;
            let changes = if old_instance == destination.instance_id {
                world.add_visible_object(&p, &server.view_ctx())
            } else {
                world.spawn(&p, &server.view_ctx())
            };
            crate::world::session::handle_zone_changes(server, changes);
            crate::systems::social::session::on_moved(server, &p);
        }
    }
}

#[derive(Debug, Default)]
struct State {
    loading: bool,
    loaded: bool,
    /// `CourtRooms` (configuration order).
    rooms: Vec<CourtRoom>,
    /// `Trials` (a `ConcurrentDictionary`: iterated here in id order).
    trials: BTreeMap<u32, TrialData>,
    /// `JuryQueues` (mother faction -> player ids).
    queues: Vec<(u32, Vec<u32>)>,
}

/// `TrialManager`.
#[derive(Debug, Default)]
pub struct TrialManager {
    state: Mutex<State>,
}

/// `GetCourtRoomRegionByFaction(faction)`.
pub fn court_room_region_by_faction(faction: u32) -> u8 {
    match faction {
        factions::NUIA_ALLIANCE => court_room_region::NUIAN,
        factions::HARANYA_ALLIANCE => court_room_region::HARANYAN,
        _ => court_room_region::INVALID,
    }
}

/// `float.DegToRad()`.
fn deg_to_rad(v: f32) -> f32 {
    std::f32::consts::PI / 180.0 * v
}

fn room_by_id(rooms: &[CourtRoom], id: Option<u32>) -> Option<&CourtRoom> {
    let id = id?;
    rooms.iter().find(|r| r.id == id)
}

fn room_by_id_mut(rooms: &mut [CourtRoom], id: Option<u32>) -> Option<&mut CourtRoom> {
    let id = id?;
    rooms.iter_mut().find(|r| r.id == id)
}

fn mother_of(server: &GameServer, p: &PlayerRef) -> u32 {
    let f = p.data().unit.faction_id;
    util::mother_id(server, f)
}

/// `RemovePlayerFromQueue(player)`.
fn remove_from_queues(queues: &mut [(u32, Vec<u32>)], id: u32) {
    for (_, q) in queues.iter_mut() {
        if let Some(i) = q.iter().position(|x| *x == id) {
            q.remove(i);
        }
    }
}

/// `GetParticipatingTrial(player)`.
fn participating_trial(
    rooms: &[CourtRoom],
    trials: &BTreeMap<u32, TrialData>,
    p: &PlayerRef,
) -> Option<u32> {
    for room in rooms {
        let Some(tid) = room.current_trial else {
            continue;
        };
        let Some(t) = trials.get(&tid) else {
            continue;
        };
        if Arc::ptr_eq(&t.defendant, p) {
            return Some(tid);
        }
        if t.jury
            .values()
            .any(|j| j.jury_member.as_ref().is_some_and(|m| Arc::ptr_eq(m, p)))
        {
            return Some(tid);
        }
        if room.audience_members.iter().any(|a| Arc::ptr_eq(a, p)) {
            return Some(tid);
        }
    }
    None
}

/// `CanAcceptTrialInvites(character)`.
fn can_accept_trial_invites(
    server: &GameServer,
    rooms: &[CourtRoom],
    trials: &BTreeMap<u32, TrialData>,
    p: Option<&PlayerRef>,
) -> bool {
    let Some(p) = p else {
        return false;
    };
    let (online, jury_point, crime_point, detained) = {
        let c = p.data();
        (
            c.is_online,
            c.jury_point,
            c.crime_point,
            c.unit.buffs.check_buffs(&[
                buff::FORCIBLY_AWAITING_TRIAL,
                buff::TRIAL_DEFENDANT,
                buff::PRISONER_NUIAN,
                buff::PRISONER_HARANYAN,
            ]),
        )
    };
    if !online || detained || jury_point <= 0 || crime_point >= crime::WANTED_CRIME_POINT_THRESHOLD
    {
        return false;
    }
    let hooks = server.systems.social.hooks.get();
    if hooks.check_buff_tag(server, p, buff::TAG_OFFENDER)
        || hooks.check_buff_tag(server, p, buff::TAG_PRISONER)
    {
        return false;
    }
    participating_trial(rooms, trials, p).is_none()
}

/// `CalculateJailTime()`.
fn calculate_jail_time(server: &GameServer, t: &mut TrialData) {
    let mut default_minutes = 0i32;
    for e in &t.evidence {
        let mut score = match e.crime_kind {
            crime::crime_kind::MURDER => 20,
            crime::crime_kind::THEFT => 8,
            _ => 0,
        };
        if let Some(v) = server.world.character_by_id(e.victim) {
            if v.data().unit.level < 30 {
                score *= 10;
            }
        }
        default_minutes = default_minutes.saturating_add(score);
    }
    t.jail_time = default_minutes;
    let (infamy, faction) = {
        let c = t.defendant.data();
        (c.infamy_point, c.unit.faction_id)
    };
    t.jail_time = t
        .jail_time
        .saturating_mul(1 + infamy / 1000)
        .clamp(0, MAX_JAIL_MINUTES);
    if faction == factions::PIRATE {
        t.jail_time = 40;
    }
    let mut c = t.defendant.data();
    // Pending sentences are stored in whole minutes. Even a zero-minute case must not
    // disappear by disconnecting before the minimum prison buff is applied.
    c.offline_guilty_time = t.jail_time.max(1);
    c.offline_guilty_region = i32::from(t.court_region);
}

/// `GenerateCrimeRecordsPacketList(trialId, unk1, events)`: one packet per 5 events.
pub fn generate_crime_records_packet_list(
    server: &GameServer,
    trial_id: u32,
    unk1: u32,
    events: &[CrimeEvent],
) -> Vec<SCCrimeRecordsPacket> {
    events
        .chunks(5)
        .map(|block| SCCrimeRecordsPacket {
            trial_id,
            unknown1: unk1,
            total_count: events.len() as i32,
            this_count: block.len() as i32,
            entries: block
                .iter()
                .map(|e| server.systems.social.crimes.record_entry(server, e))
                .collect(),
        })
        .collect()
}

/// `TrialData.SendAllTrialRecords(player)`.
fn send_all_trial_records(server: &GameServer, rooms: &[CourtRoom], t: &TrialData, p: &PlayerRef) {
    let info = {
        let c = t.defendant.data();
        SCTrialInfoPacket {
            player_id: t.defendant_id,
            crime_point: i32::from(c.crime_point),
            arrest: c.arrest_count,
            accept_guilty: c.accept_guilty_count,
            accept_trial: c.accept_trial_count,
            not_guilty: c.not_guilty_count,
            guilty: c.guilty_count,
            total: 0,
            cur: 0,
            crime_record_count: c.evidence_reported_count,
            bot_report: c.bot_reported_count,
        }
    };
    let (race, faction) = {
        let c = t.defendant.data();
        (c.race, c.unit.faction_id)
    };
    p.send(info);
    let evidence = server
        .systems
        .social
        .crimes
        .crimes_of_player(t.defendant_id, false);
    for pkt in generate_crime_records_packet_list(server, t.id, 0, &evidence) {
        p.send(pkt);
    }
    // `CourtRoom` is never null here (the C# would throw inside its try/catch).
    let Some(room) = room_by_id(rooms, t.court_room) else {
        return;
    };
    let judge_id = if room.judge_spawner.is_some() {
        server
            .systems
            .social
            .hooks
            .get()
            .judge_obj_id(server, room.id)
    } else {
        0
    };
    p.send(SCCrimeDataPacket {
        defendant_id: t.defendant_id,
        defendant_name: t.defendant_name.clone(),
        defendant_race: race,
        defendant_faction: faction,
        trial_id: t.id,
        sentence_time_ms: t.jail_time.wrapping_mul(60_000),
        judge_id,
    });
}

/// `GenerateJuryList(trial, maxCount)`.
fn generate_jury_list(
    server: &GameServer,
    rooms: &[CourtRoom],
    queues: &[(u32, Vec<u32>)],
    t: &TrialData,
    max_count: usize,
) -> Vec<PlayerRef> {
    let mut res = Vec::new();
    if max_count == 0 {
        return res;
    }
    let Some(faction) = room_by_id(rooms, t.court_room).map(|r| r.faction) else {
        return res;
    };
    let Some((_, queue)) = queues.iter().find(|q| q.0 == faction) else {
        return res;
    };
    for &pid in queue {
        let Some(p) = server.world.character_by_id(pid) else {
            continue;
        };
        if !p.data().is_online {
            continue;
        }
        if pid == t.defendant_id {
            continue;
        }
        res.push(p);
        if res.len() >= max_count {
            break;
        }
    }
    res
}

fn teleport(server: &GameServer, p: &PlayerRef, pos: SpawnPosition, reason: u8, fx: &mut Effects) {
    let zone_id = server.world.world(pos.world_id).map_or(pos.zone_id, |w| {
        server
            .game_data
            .worlds
            .get_zone_id(Some(&w.template), pos.x, pos.y)
    });
    let destination = crate::models::transform::Transform {
        instance_id: pos.world_id,
        world_id: pos.world_id,
        zone_id,
        x: pos.x,
        y: pos.y,
        z: pos.z,
        rot_z: deg_to_rad(pos.yaw),
        ..Default::default()
    };
    if p.data().unit.transform.instance_id != destination.instance_id {
        p.send(SCLoadInstancePacket {
            instance_id: destination.world_id,
            zone_id: destination.zone_id,
            x: destination.x,
            y: destination.y,
            z: destination.z,
            ang_x: destination.rot_x,
            ang_y: destination.rot_y,
            ang_z: destination.rot_z,
        });
    }
    p.data().disabled_set_position = true;
    p.send(SCTeleportUnitPacket {
        reason,
        error_message: 0,
        x: pos.x,
        y: pos.y,
        z: pos.z,
        z2: destination.rot_z,
    });
    fx.push(Effect::Teleport(p.clone(), destination));
}

/// `TrialData.EnterCourtJail(temporaryCourtRoom, defendant)`.
fn enter_court_jail(server: &GameServer, t: &mut TrialData, room: &CourtRoom, fx: &mut Effects) {
    t.court_region = room.region;
    t.court_room = Some(room.id);
    let d = t.defendant.clone();
    let (hp, mp, inst) = {
        let mut c = d.data();
        t.defendant_id = c.id;
        t.defendant_name = c.unit.name.clone();
        // Make sure the criminal is alive.
        c.unit.hp = c.unit.hp.max(1);
        c.offline_guilty_region = i32::from(room.region);
        add_own_buff(
            server,
            &mut c,
            buff::FORCIBLY_AWAITING_TRIAL,
            0,
            util::now_ms(),
        );
        (c.unit.hp, c.unit.mp, c.unit.transform.instance_id)
    };
    let pr = world_pos_rot(server, &WorldObject::from(&d));
    if let Some(w) = server.world.world(inst) {
        broadcast(
            &w,
            &d,
            &SCCharacterResurrectedPacket {
                unit_id: d.obj_id,
                x: pr.x,
                y: pr.y,
                z: pr.z,
                z_rot: pr.rot_z,
            },
            true,
        );
        broadcast(
            &w,
            &d,
            &SCUnitPointsPacket {
                obj_id: d.obj_id,
                hp,
                mp,
            },
            true,
        );
    }
    fx.push(Effect::PostUpdateHp(d.clone(), 0, hp));
    // Teleport the criminal to the local jail.
    fx.push(Effect::ForceDismount(d.clone()));
    teleport(server, &d, room.jail, teleport_reason::LOCKUP, fx);
    let crime_point = d.data().crime_point;
    d.send(SCAskImprisonOrTrialPacket {
        crime_value: crime_point as u32,
        jail_minutes: t.jail_time,
    });
    d.send(SCTrialWaitStatusPacket {
        order: 0,
        sentence_time_ms: t.jail_time.wrapping_mul(60_000),
    });
    fx.push(Effect::DispatchBuffs(d));
}

/// `TrialData.EnterCourtRoom(courtRoom, defendant)` (+ `SendJuryInvites`).
fn enter_court_room(
    server: &GameServer,
    rooms: &mut [CourtRoom],
    queues: &[(u32, Vec<u32>)],
    t: &mut TrialData,
    room_index: usize,
    now_ms: i64,
    fx: &mut Effects,
) {
    let d = t.defendant.clone();
    {
        let room = &mut rooms[room_index];
        room.current_trial = Some(t.id);
        t.court_room = Some(room.id);
        // Make jury seats.
        t.jury.clear();
        for (&i, seat) in &room.jury_seats {
            t.jury.insert(
                i,
                JuryBox {
                    seat: seat.clone(),
                    seat_id: i,
                    jury_member: None,
                    confirm_testimony: false,
                    selected_sentence: -1,
                },
            );
        }
    }
    let room = rooms[room_index].clone();
    {
        let mut c = d.data();
        t.defendant_id = c.id;
        t.defendant_name = c.unit.name.clone();
        // On Trial buff.
        c.unit.buffs.remove_buff(buff::FORCIBLY_AWAITING_TRIAL);
        add_own_buff(server, &mut c, buff::TRIAL_DEFENDANT, 0, now_ms);
    }
    teleport(server, &d, room.defendant, teleport_reason::DEFENDANT, fx);
    d.send(SCSummonDefendantPacket { trial: t.id });
    room.join_chat(&d);
    d.send(SCJuryWaitStatusPacket {
        count: 0,
        total: t.jury.len() as i32,
        sentence_time_ms: t.jail_time.wrapping_mul(60_000),
    });
    t.step = step::VERIFY_CRIMINAL_RECORD;
    t.current_step_end_ms = now_ms + TRIAL_INIT_SECONDS * SECOND_MS;
    d.send(t.state_packet(0, now_ms));
    send_all_trial_records(server, rooms, t, &d);
    t.step = step::AWAITING_JURY_SUMMONS;
    t.current_step_end_ms = now_ms + AWAITING_JURY_SUMMONS_MINUTES * MINUTE_MS;
    d.send(t.state_packet(0, now_ms));
    {
        let mut c = d.data();
        add_own_buff(server, &mut c, buff::CANNOT_ESCAPE, 0, now_ms);
    }
    d.send(SCForceAttackSetPacket {
        obj_id: d.obj_id,
        on: false,
    });
    fx.push(Effect::DispatchBuffs(d));
    // SendJuryInvites
    for p in generate_jury_list(server, rooms, queues, t, 10) {
        t.invited_jury.push(p.character_id);
        p.send(SCInviteJuryPacket {
            defendant_name: t.defendant_name.clone(),
            trial: t.id,
        });
    }
}

/// `TrialData.SummonJuryMember(player)`.
fn summon_jury_member(
    server: &GameServer,
    rooms: &[CourtRoom],
    queues: &mut [(u32, Vec<u32>)],
    t: &mut TrialData,
    player: &PlayerRef,
    now_ms: i64,
    fx: &mut Effects,
) -> bool {
    let seat_ids: Vec<i32> = t.jury.keys().copied().collect();
    for seat_id in seat_ids {
        let seat = {
            let Some(jb) = t.jury.get_mut(&seat_id) else {
                continue;
            };
            if jb.jury_member.is_some() {
                continue;
            }
            jb.jury_member = Some(player.clone());
            jb.seat.clone()
        };
        remove_from_queues(queues, player.character_id);
        let Some(room) = room_by_id(rooms, t.court_room) else {
            tracing::error!("SummonJuryMember: trial {} has no court room", t.id);
            return false;
        };
        // Save the current position to return to after the trial (or on disconnect).
        let pr = world_pos_rot(server, &WorldObject::from(player));
        {
            let mut c = player.data();
            let t = c.unit.transform;
            c.main_world_position = Some(crate::models::transform::Transform {
                instance_id: t.instance_id,
                world_id: t.world_id,
                zone_id: t.zone_id,
                x: pr.x,
                y: pr.y,
                z: pr.z,
                rot_x: pr.rot_x,
                rot_y: pr.rot_y,
                rot_z: pr.rot_z,
                parent_obj_id: 0,
            });
        }
        player.send(SCSummonJuryPacket {
            trial: t.id,
            court: room.id,
            jury_number: seat_id,
        });
        // Teleport the jury.
        let seat_pr = world_pos_rot(server, &WorldObject::from(&seat));
        let seat_world_id = seat.data().transform.world_id;
        fx.push(Effect::ForceDismount(player.clone()));
        teleport(
            server,
            player,
            SpawnPosition {
                world_id: seat_world_id,
                x: seat_pr.x,
                y: seat_pr.y,
                z: seat_pr.z,
                yaw: seat_pr.rot_z.to_degrees(),
                ..Default::default()
            },
            teleport_reason::JURY,
            fx,
        );
        // Seat them down.
        player.send(SCJuryBeSeatedPacket {
            is_west: t.court_region == court_room_region::HARANYAN,
            trial: t.id,
            court: room.id as i32,
            jury_number: seat_id,
        });
        room.join_chat(player);
        {
            let mut c = player.data();
            add_own_buff(server, &mut c, buff::COURT_HOUSE, 0, now_ms);
            add_own_buff(server, &mut c, buff::JURY, 0, now_ms);
            add_own_buff(server, &mut c, buff::CANNOT_ESCAPE, 0, now_ms);
        }
        fx.push(Effect::DispatchBuffs(player.clone()));
        // Send them the trial info.
        send_all_trial_records(server, rooms, t, player);
        let count = t.active_jury_count();
        let total = t.jury.len() as i32;
        t.send_packets(&SCJuryWaitStatusPacket {
            count,
            total,
            sentence_time_ms: t.jail_time.wrapping_mul(60_000),
        });
        // End this step early if all seats are taken.
        if count >= total && t.step == step::AWAITING_JURY_SUMMONS && t.current_step_end_ms > now_ms
        {
            t.current_step_end_ms = now_ms;
        }
        return true;
    }
    false
}

/// `TrialData.ArchiveEvidence()`.
fn archive_trial_evidence(server: &GameServer, t: &TrialData, now_ms: i64) {
    let judge_time = now_ms.div_euclid(1000);
    let crimes = &server.systems.social.crimes;
    for e in &t.evidence {
        if server.config.justice.keep_history {
            crimes.archive_evidence(e.id, judge_time);
        } else {
            crimes.remove_evidence(e.id);
        }
    }
}

/// `ResultIsGuilty(defendant, trial, pleadGuilty)`.
fn result_is_guilty(
    server: &GameServer,
    rooms: &[CourtRoom],
    defendant: &PlayerRef,
    t: &mut TrialData,
    plead_guilty: bool,
    now_ms: i64,
    fx: &mut Effects,
) {
    if t.step >= step::PLEAD_GUILTY {
        return;
    }
    tracing::debug!("TrialStep.EndTrial - {}", t.id);
    let Some(room) = room_by_id(rooms, t.court_room) else {
        tracing::error!("ResultIsGuilty: trial {} has no court room", t.id);
        return;
    };
    // Find the jail to go to.
    let Some(jail) = server
        .config
        .justice
        .jails
        .iter()
        .find(|j| j.faction == room.faction)
    else {
        let name = defendant.data().unit.name.clone();
        tracing::error!(
            "Could not find a jail location for Court: {}, Defendant: {name}",
            room.name
        );
        return;
    };
    t.step = if plead_guilty {
        step::PLEAD_GUILTY
    } else {
        step::END_TRIAL
    };
    t.current_step_end_ms = now_ms + END_TRIAL_SECONDS * SECOND_MS;
    t.send_state(now_ms);
    room.leave_chat(defendant);
    archive_trial_evidence(server, t, now_ms);
    let pkt = {
        let mut c = defendant.data();
        let crime_count = c.crime_point;
        c.unit.buffs.remove_buff(buff::FORCIBLY_AWAITING_TRIAL);
        c.unit.buffs.remove_buff(buff::CANNOT_ESCAPE);
        c.offline_guilty_time = 0;
        c.offline_guilty_region = i32::from(court_room_region::INVALID);
        let v = c.crime_point.wrapping_sub(crime_count);
        set_crime_point(server, &mut c, v, now_ms);
        if plead_guilty {
            c.accept_guilty_count += 1;
        } else {
            c.guilty_count += 1;
        }
        SCCrimeChangedPacket {
            points: i32::from(crime_count),
            crime_points: c.crime_point,
            infamy_points: c.infamy_point,
            crime_state: crime_state(&c),
        }
    };
    defendant.send(pkt);
    // Prisoner buff (minimum 10 s: 0 would use the default 30 minutes).
    let jail_time_ms = t.jail_time.saturating_mul(60_000).max(10_000);
    {
        let mut c = defendant.data();
        match t.court_region {
            court_room_region::NUIAN => {
                add_own_buff(server, &mut c, buff::PRISONER_NUIAN, jail_time_ms, now_ms)
            }
            court_room_region::HARANYAN => add_own_buff(
                server,
                &mut c,
                buff::PRISONER_HARANYAN,
                jail_time_ms,
                now_ms,
            ),
            r => tracing::error!(
                "ResultIsGuilty - Invalid court region? {r}, defendant: {}",
                t.defendant_name
            ),
        }
        c.disabled_set_position = true;
    }
    fx.push(Effect::ForceDismount(defendant.clone()));
    teleport(server, defendant, jail.pos, teleport_reason::JAIL, fx);
    defendant
        .data()
        .unit
        .buffs
        .remove_buff(buff::TRIAL_DEFENDANT);
    fx.push(Effect::DispatchBuffs(defendant.clone()));
}

/// `ResultIsNotGuilty(defendant, trial)`.
fn result_is_not_guilty(
    server: &GameServer,
    defendant: &PlayerRef,
    t: &mut TrialData,
    now_ms: i64,
    fx: &mut Effects,
) {
    tracing::debug!("TrialStep.EndTrial - {}", t.id);
    t.step = step::END_TRIAL;
    t.current_step_end_ms = now_ms + END_TRIAL_SECONDS * SECOND_MS;
    t.send_state(now_ms);
    archive_trial_evidence(server, t, now_ms);
    let (pkt, infamy, faction, race, gender) = {
        let mut c = defendant.data();
        let crime_count = c.crime_point;
        c.unit.buffs.remove_buff(buff::FORCIBLY_AWAITING_TRIAL);
        c.unit.buffs.remove_buff(buff::CANNOT_ESCAPE);
        c.offline_guilty_time = 0;
        c.offline_guilty_region = i32::from(court_room_region::INVALID);
        c.not_guilty_count += 1;
        let v = c.crime_point.wrapping_sub(crime_count);
        set_crime_point(server, &mut c, v, now_ms);
        let v = c.infamy_point.wrapping_sub(i32::from(crime_count));
        set_infamy_point(server, &mut c, v, now_ms);
        (
            SCCrimeChangedPacket {
                points: i32::from(crime_count),
                crime_points: c.crime_point,
                infamy_points: c.infamy_point,
                crime_state: crime_state(&c),
            },
            c.infamy_point,
            c.unit.faction_id,
            c.race,
            c.gender,
        )
    };
    defendant.send(pkt);
    fx.push(Effect::RemoveBuffsByTag(
        defendant.clone(),
        BuffKind::Good,
        1,
        buff::TAG_PRISONER,
    ));
    defendant
        .data()
        .unit
        .buffs
        .remove_buff(buff::TRIAL_DEFENDANT);
    fx.push(Effect::DispatchBuffs(defendant.clone()));
    if infamy <= 0 && faction == factions::PIRATE {
        // Return to the original faction.
        match server.game_data.characters.template(race, gender) {
            Some(tpl) => fx.push(Effect::SetFaction(defendant.clone(), tpl.faction_id)),
            None => tracing::error!("ResultIsNotGuilty: no character template {race}/{gender}"),
        }
    }
}

/// `TrialData.FinalizeVerdict()`.
fn finalize_verdict(
    server: &GameServer,
    rooms: &[CourtRoom],
    t: &mut TrialData,
    now_ms: i64,
    fx: &mut Effects,
) {
    let mut total = 0;
    let mut selected_count = 0;
    let mut votes: Vec<(i32, i32)> = Vec::new();
    for j in t.jury.values() {
        if let Some(m) = &j.jury_member {
            total += 1;
            if j.selected_sentence >= 0 {
                match votes.iter_mut().find(|v| v.0 == j.selected_sentence) {
                    Some(v) => v.1 += 1,
                    None => votes.push((j.selected_sentence, 1)),
                }
                selected_count += 1;
            }
            m.data().unit.buffs.remove_buff(buff::JURY);
            fx.push(Effect::DispatchBuffs(m.clone()));
        }
    }
    let mut most_voted = 3; // default guilty
    let mut most_voted_count = 0;
    for &(k, c) in &votes {
        if c > most_voted_count {
            most_voted = k;
            most_voted_count = c;
        }
    }
    let is_guilty = most_voted > 1;
    let round = |f: f32| f64::from(t.jail_time as f32 * f).round_ties_even() as i32;
    let (sentence_type, sentence_time) = match most_voted {
        0 | 1 => (sentence_result::NOT_GUILTY, 0),
        2 => (sentence_result::GUILTY1, round(0.2)),
        3 => (sentence_result::GUILTY2, round(0.5)),
        4 => (sentence_result::GUILTY3, round(0.8)),
        5 => (sentence_result::GUILTY4, round(1.0)),
        6 => (sentence_result::GUILTY5, round(1.2)),
        _ => (sentence_result::NOT_GUILTY, 0),
    };
    let sentence_time = sentence_time.clamp(0, MAX_JAIL_MINUTES);
    t.send_packets(&SCRulingStatusPacket {
        count: selected_count,
        total,
        sentence_type,
        sentence_time: sentence_time.wrapping_mul(60_000),
    });
    t.jail_time = sentence_time.clamp(0, MAX_JAIL_MINUTES);
    let d = t.defendant.clone();
    if is_guilty {
        result_is_guilty(server, rooms, &d, t, false, now_ms, fx);
    } else {
        result_is_not_guilty(server, &d, t, now_ms, fx);
    }
}

/// `CreateTrialCase(defendant)`.
fn create_trial_case(
    server: &GameServer,
    trials: &mut BTreeMap<u32, TrialData>,
    defendant: &PlayerRef,
    court_region: u8,
) -> Option<u32> {
    let id = match server.ids.trial.get_next_id() {
        Ok(id) => id,
        Err(e) => {
            tracing::error!("CreateTrialCase: {e:?}");
            return None;
        }
    };
    let (did, name) = {
        let c = defendant.data();
        (c.id, c.unit.name.clone())
    };
    let mut t = TrialData {
        id,
        court_region,
        court_room: None,
        step: step::DEFENDANT_AWAITING_TRIAL,
        defendant: defendant.clone(),
        defendant_id: did,
        defendant_name: name,
        current_step_end_ms: TIME_MAX,
        jury: BTreeMap::new(),
        invited_jury: Vec::new(),
        jail_time: 15,
        evidence: Vec::new(),
    };
    tracing::debug!("TrialStep.DefendantAwaitingTrial - {id}");
    t.evidence = server.systems.social.crimes.crimes_of_player(did, false);
    calculate_jail_time(server, &mut t);
    if trials.contains_key(&id) {
        return None;
    }
    trials.insert(id, t);
    Some(id)
}

impl TrialManager {
    fn st(&self) -> MutexGuard<'_, State> {
        self.state.lock().expect("trial manager poisoned")
    }

    /// `Load()`: the jury queues and the configured court rooms (seats and judges are added
    /// by [`TrialManager::initialize_doodads`] / [`TrialManager::initialize_npc`]). The 5 s tick
    /// is started by [`TrialManager::start_tick`].
    pub fn load(&self, server: &GameServer) {
        let mut st = self.st();
        if st.loading || st.loaded {
            return;
        }
        st.loading = true;
        st.queues.push((factions::NUIA_ALLIANCE, Vec::new()));
        st.queues.push((factions::HARANYA_ALLIANCE, Vec::new()));
        for cfg in &server.config.justice.court_rooms {
            if st.rooms.iter().any(|r| r.id == cfg.id) {
                // `Dictionary.Add` throws in C#.
                tracing::error!("Duplicate court room id {} in the configuration", cfg.id);
                continue;
            }
            let region = court_room_region_by_faction(cfg.faction);
            st.rooms.push(CourtRoom {
                id: cfg.id,
                name: cfg.name.clone(),
                region,
                faction: cfg.faction,
                judge_spawner: None,
                jury_seats: BTreeMap::new(),
                audience_seats: Vec::new(),
                audience_members: Vec::new(),
                defendant: cfg.defendant,
                jail: cfg.holding_cell,
                chat: server.chat.court_chat(region),
                current_trial: None,
            });
        }
        tracing::info!("Loaded trials data");
        st.loaded = true;
        st.loading = false;
    }

    /// Schedules the `TrialUpdateTask` (`Schedule(task, null, 5 s)`: first run immediately, then
    /// every 5 s).
    pub fn start_tick(server: &Arc<GameServer>) {
        let weak = Arc::downgrade(server);
        tokio::spawn(async move {
            let mut iv =
                tokio::time::interval(std::time::Duration::from_millis(TRIAL_TICK_INTERVAL_MS));
            loop {
                iv.tick().await;
                let Some(server) = weak.upgrade() else {
                    break;
                };
                server
                    .systems
                    .social
                    .trials
                    .update_tick(&server, util::now_ms())
                    .await;
            }
        });
    }

    /// The configured court rooms (snapshot).
    pub fn court_rooms(&self) -> Vec<CourtRoom> {
        self.st().rooms.clone()
    }

    /// `GetTrial(trialId)` (snapshot).
    pub fn get_trial(&self, trial_id: u32) -> Option<TrialData> {
        self.st().trials.get(&trial_id).cloned()
    }

    /// The ids of the running trials.
    pub fn trial_ids(&self) -> Vec<u32> {
        self.st().trials.keys().copied().collect()
    }

    /// The jury queue of a mother faction (snapshot).
    pub fn jury_queue(&self, faction: u32) -> Vec<u32> {
        self.st()
            .queues
            .iter()
            .find(|q| q.0 == faction)
            .map(|q| q.1.clone())
            .unwrap_or_default()
    }

    /// `InitializeDoodads(world)`: `links` are the (`SpecialLink`, last spawned doodad) pairs of
    /// the world's `justice.` doodad spawners (`SpawnManager.GetSpecialDoodadSpawners`).
    pub fn initialize_doodads(&self, links: &[(String, DoodadRef)]) {
        let mut st = self.st();
        for room in st.rooms.iter_mut() {
            for (link, doodad) in links {
                for seat_id in 1..=5 {
                    if *link == format!("justice.{}.seat.{seat_id}", room.id) {
                        match room.jury_seats.entry(seat_id) {
                            // `Dictionary.Add` throws in C#.
                            std::collections::btree_map::Entry::Occupied(_) => {
                                tracing::error!("Duplicate jury seat {link}")
                            }
                            std::collections::btree_map::Entry::Vacant(v) => {
                                v.insert(doodad.clone());
                            }
                        }
                    }
                }
                if *link == format!("justice.{}.spectator", room.id) {
                    room.audience_seats.push(doodad.clone());
                }
            }
        }
    }

    /// `InitializeNpc(world)`: `links` are the (`SpecialLink`, spawner id) pairs of the world's
    /// `justice.` npc spawners; the first `justice.<court>.judge` becomes the court's judge.
    pub fn initialize_npc(&self, links: &[(String, u32)]) {
        let mut st = self.st();
        for room in st.rooms.iter_mut() {
            for (link, spawner) in links {
                if *link == format!("justice.{}.judge", room.id) && room.judge_spawner.is_none() {
                    room.judge_spawner = Some(*spawner);
                    break;
                }
            }
        }
    }

    /// `UpdateJuryQueue()`.
    fn update_jury_queue(&self, server: &GameServer, st: &mut State) {
        let State {
            rooms,
            trials,
            queues,
            ..
        } = st;
        let mut to_remove = Vec::new();
        if let Some(w) = server.world.world(0) {
            for ch in w.all_characters() {
                if !can_accept_trial_invites(server, rooms, trials, Some(&ch)) {
                    continue;
                }
                let mother = mother_of(server, &ch);
                let Some((_, q)) = queues.iter_mut().find(|q| q.0 == mother) else {
                    continue;
                };
                if !q.contains(&ch.character_id) {
                    q.push(ch.character_id);
                }
            }
        }
        for (faction, q) in queues.iter() {
            for &pid in q {
                let player = server.world.character_by_id(pid);
                if !can_accept_trial_invites(server, rooms, trials, player.as_ref())
                    || player.is_some_and(|p| *faction != mother_of(server, &p))
                {
                    to_remove.push(pid);
                }
            }
        }
        for pid in to_remove {
            for (_, q) in queues.iter_mut() {
                if let Some(i) = q.iter().position(|x| *x == pid) {
                    q.remove(i);
                }
            }
        }
    }

    /// `UpdateTrialStates()`.
    fn update_trial_states(&self, server: &GameServer, st: &mut State, now: i64, fx: &mut Effects) {
        let State {
            rooms,
            trials,
            queues,
            ..
        } = st;
        let ids: Vec<u32> = trials.keys().copied().collect();
        for id in ids {
            let mut remove = false;
            {
                let Some(t) = trials.get_mut(&id) else {
                    continue;
                };
                // Waiting for an empty court room when the defendant decided to go on trial.
                if t.step == step::DEFENDANT_AWAITING_TRIAL && t.current_step_end_ms <= now {
                    let Some(ri) = rooms
                        .iter()
                        .position(|r| r.region == t.court_region && r.current_trial.is_none())
                    else {
                        continue;
                    };
                    enter_court_room(server, rooms, queues, t, ri, now, fx);
                }
                if t.step > step::AWAITING_JURY_SUMMONS
                    && t.step < step::TRIAL_CANCELLED
                    && t.active_jury_count() <= 0
                {
                    // No jury left during a trial: auto-cancel it.
                    t.step = step::TRIAL_CANCELLED;
                }
                // Nobody left in the queue: skip waiting for the others.
                if t.step == step::AWAITING_JURY_SUMMONS {
                    let faction = room_by_id(rooms, t.court_room).map(|r| r.faction);
                    let available = queues
                        .iter()
                        .find(|q| Some(q.0) == faction)
                        .is_some_and(|q| !q.1.is_empty());
                    if !available {
                        tracing::debug!(
                            "TrialStep.AwaitingJurySummons skipping jury wait, nobody in queue - {}",
                            t.id
                        );
                        t.current_step_end_ms = now;
                    }
                }
                // Wait for the jury.
                if t.step == step::AWAITING_JURY_SUMMONS && t.current_step_end_ms <= now {
                    tracing::debug!("TrialStep.ConfirmCriminalRecord - {}", t.id);
                    t.step = step::CONFIRM_CRIMINAL_RECORD;
                    t.current_step_end_ms = now + CONFIRM_CRIMINAL_RECORD_MINUTES * MINUTE_MS;
                    t.send_packets(&SCChatMessagePacket {
                        chat_type: chat_type::JUDGE,
                        sender: None,
                        message: format!("Begin the trial of {}", t.defendant_name),
                        ability: 0,
                        language_type: 0,
                    });
                    t.send_state(now);
                }
                // Wait for "give verdict" by the jury.
                if t.step == step::CONFIRM_CRIMINAL_RECORD && t.current_step_end_ms <= now {
                    tracing::debug!("TrialStep.ClosingStatement - {}", t.id);
                    t.step = step::CLOSING_STATEMENT;
                    t.current_step_end_ms = now + CLOSING_STATEMENT_MINUTES * MINUTE_MS;
                    t.send_state(now);
                }
                // Wait for the closing statement of the defendant.
                if t.step == step::CLOSING_STATEMENT && t.current_step_end_ms <= now {
                    tracing::debug!("TrialStep.JuryVerdict - {}", t.id);
                    t.step = step::JURY_VERDICT;
                    t.current_step_end_ms = now + JURY_VERDICT_MINUTES * MINUTE_MS;
                    t.send_state(now);
                    // Update the jury verdict panel to default.
                    t.defendant.send(SCRulingStatusPacket {
                        count: 0,
                        total: t.active_jury_count(),
                        sentence_type: 0,
                        sentence_time: 0,
                    });
                }
                if t.step == step::JURY_VERDICT && t.current_step_end_ms <= now {
                    tracing::debug!("TrialStep.CalculateSentence - {}", t.id);
                    finalize_verdict(server, rooms, t, now, fx);
                }
                if t.step == step::END_TRIAL && t.current_step_end_ms <= now {
                    t.send_packets(&SCRulingClosedPacket {});
                    tracing::debug!("TrialStep.Cleanup - {}", t.id);
                    t.step = step::CLEANUP;
                    t.current_step_end_ms = TIME_MAX;
                }
                if t.step == step::PLEAD_GUILTY {
                    t.send_packets(&SCRulingClosedPacket {});
                    tracing::debug!("TrialStep.Cleanup from PleadGuilty - {}", t.id);
                    t.step = step::CLEANUP;
                    t.current_step_end_ms = TIME_MAX;
                }
                if t.step == step::TRIAL_CANCELLED {
                    let d = t.defendant.clone();
                    result_is_guilty(server, rooms, &d, t, false, now, fx);
                    t.send_packets(&SCRulingClosedPacket {});
                    tracing::debug!("TrialStep.TrialCancelled - {}", t.id);
                    t.step = step::CLEANUP;
                    t.current_step_end_ms = TIME_MAX;
                }
                // Delete the trial when done.
                if t.step == step::CLEANUP {
                    t.step = step::INVALID;
                    cleanup_trial(server, rooms, t, true, fx);
                    tracing::debug!("TrialStep.Cleanup - {}", t.id);
                    remove = true;
                }
            }
            if remove && trials.remove(&id).is_none() {
                tracing::warn!("Failed to remove Trial {id}");
            }
        }
    }

    /// `UpdateTick()` (the `TrialUpdateTask`).
    pub async fn update_tick(&self, server: &GameServer, now_ms: i64) {
        let mut fx = Vec::new();
        {
            let mut st = self.st();
            self.update_jury_queue(server, &mut st);
            self.update_trial_states(server, &mut st, now_ms, &mut fx);
        }
        run_effects_async(server, fx).await;
    }

    /// `UpdateJuryQueue()` on its own.
    pub fn update_jury_queues(&self, server: &GameServer) {
        let mut st = self.st();
        self.update_jury_queue(server, &mut st);
    }

    /// `GetJuryQueueForPlayer(player)`: sends `SCJuryWaitingNumberPacket` when queued.
    pub fn get_jury_queue_for_player(&self, server: &GameServer, player: &PlayerRef) {
        let mother = mother_of(server, player);
        let st = self.st();
        let Some((_, q)) = st.queues.iter().find(|q| q.0 == mother) else {
            return;
        };
        if let Some(pos) = q.iter().position(|x| *x == player.character_id) {
            player.send(SCJuryWaitingNumberPacket {
                waiting_number: pos as i32 + 1,
            });
        }
    }

    /// `RemovePlayerFromQueue(player)`.
    pub fn remove_player_from_queue(&self, player_id: u32) {
        remove_from_queues(&mut self.st().queues, player_id);
    }

    /// `GenerateJuryList(trial, maxCount)`.
    pub fn generate_jury_list(
        &self,
        server: &GameServer,
        trial_id: u32,
        max_count: usize,
    ) -> Vec<PlayerRef> {
        let st = self.st();
        match st.trials.get(&trial_id) {
            Some(t) => generate_jury_list(server, &st.rooms, &st.queues, t, max_count),
            None => Vec::new(),
        }
    }

    /// `ProcessTrialInviteReply(player, accept, trialId)`.
    pub fn process_trial_invite_reply(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        accept: bool,
        trial_id: u32,
        now_ms: i64,
    ) -> bool {
        let mother = mother_of(server, player);
        let mut fx = Vec::new();
        let res = {
            let mut st = self.st();
            let State {
                rooms,
                trials,
                queues,
                ..
            } = &mut *st;
            self.invite_reply_inner(
                server, rooms, trials, queues, player, mother, accept, trial_id, now_ms, &mut fx,
            )
        };
        run_effects(server, fx);
        res
    }

    #[allow(clippy::too_many_arguments)]
    fn invite_reply_inner(
        &self,
        server: &GameServer,
        rooms: &mut [CourtRoom],
        trials: &mut BTreeMap<u32, TrialData>,
        queues: &mut [(u32, Vec<u32>)],
        player: &PlayerRef,
        mother: u32,
        accept: bool,
        trial_id: u32,
        now_ms: i64,
        fx: &mut Effects,
    ) -> bool {
        let name = player.data().unit.name.clone();
        if !trials.contains_key(&trial_id) {
            // Replied too late, don't remove from the queue.
            tracing::debug!(
                "{name} sent a jury invite reply for a non-existing trial ({trial_id}). Possibly already concluded."
            );
            send_error(player, error::TRIALS_ALREADY_CLOSED);
            return false;
        }
        let Some(qi) = queues.iter().position(|q| q.0 == mother) else {
            send_error(player, error::TRIALS_JURY_FULL);
            sus_log(
                server,
                crate::systems::services::sus::CATEGORY_CHEATING,
                player.character_id,
                "Player is trying to join as jury while not in an eligible faction",
            );
            return false;
        };
        if !queues[qi].1.contains(&player.character_id) {
            send_error(player, error::TRIALS_JURY_FULL);
            sus_log(
                server,
                crate::systems::services::sus::CATEGORY_CHEATING,
                player.character_id,
                "Player is trying to join as jury while not in an the jury queue",
            );
            return false;
        }
        if !accept {
            // Remove the current queue position if manually declined.
            let q = &mut queues[qi].1;
            if let Some(i) = q.iter().position(|x| *x == player.character_id) {
                q.remove(i);
            }
            return false;
        }
        let Some(t) = trials.get(&trial_id) else {
            return false;
        };
        if t.step != step::AWAITING_JURY_SUMMONS {
            send_error(player, error::TRIALS_CANNOT_JOIN_AFTER_START);
            return false;
        }
        if !t.invited_jury.contains(&player.character_id)
            || room_by_id(rooms, t.court_room).is_none_or(|r| r.faction != mother)
            || !can_accept_trial_invites(server, rooms, trials, Some(player))
        {
            send_error(player, error::TRIALS_JURY_FULL);
            return false;
        }
        // Verify we are not already part of another trial.
        if trials.values().any(|t| {
            t.jury.values().any(|j| {
                j.jury_member
                    .as_ref()
                    .is_some_and(|m| Arc::ptr_eq(m, player))
            })
        }) {
            send_error(player, error::TRIALS_JURY_FULL);
            return false;
        }
        if player.data().unit.transform.instance_id != 0 {
            // Only allow joining from within the main world.
            send_error(player, error::CANNOT_JOIN_TRIAL_FROM_INSTANT_ZONE);
            return false;
        }
        let Some(t) = trials.get_mut(&trial_id) else {
            return false;
        };
        if t.step > step::AWAITING_JURY_SUMMONS {
            send_error(player, error::TRIALS_CANNOT_JOIN_AFTER_START);
            return false;
        }
        let res = summon_jury_member(server, rooms, queues, t, player, now_ms, fx);
        if !res {
            send_error(player, error::TRIALS_JURY_FULL);
        }
        res
    }

    /// Restore justice state before considering jury eligibility.
    pub fn handle_player_login(&self, server: &GameServer, player: &PlayerRef) {
        let mother = mother_of(server, player);
        let now = util::now_ms();
        let mut fx = Vec::new();
        {
            let mut st = self.st();
            let State {
                rooms,
                trials,
                queues,
                ..
            } = &mut *st;
            if trials
                .values()
                .any(|t| t.defendant_id == player.character_id)
            {
                return;
            }
            let (offline_guilty_time, region, prisoner_region) = {
                let c = player.data();
                let prisoner = if c.unit.buffs.check_buff(buff::PRISONER_NUIAN) {
                    Some(court_room_region::NUIAN)
                } else if c.unit.buffs.check_buff(buff::PRISONER_HARANYAN) {
                    Some(court_room_region::HARANYAN)
                } else {
                    None
                };
                (
                    c.offline_guilty_time,
                    c.offline_guilty_region as u8,
                    prisoner,
                )
            };
            if let Some(prisoner_region) = prisoner_region {
                if let Some(room) = rooms.iter().find(|r| r.region == prisoner_region) {
                    if let Some(jail) = server
                        .config
                        .justice
                        .jails
                        .iter()
                        .find(|j| j.faction == room.faction)
                    {
                        fx.push(Effect::ForceDismount(player.clone()));
                        teleport(server, player, jail.pos, teleport_reason::JAIL, &mut fx);
                    }
                }
            } else if offline_guilty_time > 0 {
                if let Some(room) = rooms.iter().find(|r| {
                    r.region == region
                        && server
                            .config
                            .justice
                            .jails
                            .iter()
                            .any(|j| j.faction == r.faction)
                }) {
                    if let Some(tid) = create_trial_case(server, trials, player, region) {
                        if let Some(t) = trials.get_mut(&tid) {
                            t.court_room = Some(room.id);
                            t.jail_time = offline_guilty_time.clamp(0, MAX_JAIL_MINUTES);
                            result_is_guilty(server, rooms, player, t, false, now, &mut fx);
                        }
                    }
                } else {
                    tracing::error!(
                        "No court/jail for pending sentence of {} in region {region}",
                        player.character_id
                    );
                }
            } else if can_accept_trial_invites(server, rooms, trials, Some(player)) {
                if let Some((_, q)) = queues.iter_mut().find(|q| q.0 == mother) {
                    if !q.contains(&player.character_id) {
                        q.push(player.character_id);
                    }
                }
            }
        }
        run_effects(server, fx);
    }

    /// Close an abandoned defendant's case, or vacate a disconnected juror's seat before save.
    pub fn handle_player_logout(&self, server: &GameServer, player: &PlayerRef) {
        let mut fx = Vec::new();
        {
            let mut st = self.st();
            let State {
                rooms,
                trials,
                queues,
                ..
            } = &mut *st;
            remove_from_queues(queues, player.character_id);
            let defendant_trial = trials
                .values()
                .find(|t| Arc::ptr_eq(&t.defendant, player))
                .map(|t| t.id);
            if let Some(id) = defendant_trial {
                if let Some(t) = trials.remove(&id) {
                    t.send_packets(&SCRulingClosedPacket {});
                    cleanup_trial(server, rooms, &t, false, &mut fx);
                }
            }
            for t in trials.values_mut() {
                let seat_id = t
                    .jury
                    .iter()
                    .find(|(_, j)| {
                        j.jury_member
                            .as_ref()
                            .is_some_and(|m| Arc::ptr_eq(m, player))
                    })
                    .map(|(&id, _)| id);
                if let Some(seat_id) = seat_id {
                    cleanup_jury_member(server, rooms, t, player, false, &mut fx);
                    if let Some(j) = t.jury.get_mut(&seat_id) {
                        j.jury_member = None;
                        j.confirm_testimony = false;
                        j.selected_sentence = -1;
                    }
                    t.send_state(util::now_ms());
                }
            }
            for room in rooms {
                room.audience_members.retain(|m| !Arc::ptr_eq(m, player));
                room.leave_chat(player);
            }
        }
        run_effects(server, fx);
    }

    /// `ArrestCriminal(criminal, arrestor)`: returns the new trial id.
    pub fn arrest_criminal(
        &self,
        server: &GameServer,
        criminal: &PlayerRef,
        arrestor: Option<&PlayerRef>,
    ) -> Option<u32> {
        let mut fx = Vec::new();
        let res = {
            let mut st = self.st();
            let State { rooms, trials, .. } = &mut *st;
            self.arrest_inner(server, rooms, trials, criminal, arrestor, &mut fx)
        };
        run_effects(server, fx);
        res
    }

    fn arrest_inner(
        &self,
        server: &GameServer,
        rooms: &[CourtRoom],
        trials: &mut BTreeMap<u32, TrialData>,
        criminal: &PlayerRef,
        arrestor: Option<&PlayerRef>,
        fx: &mut Effects,
    ) -> Option<u32> {
        if let Some(t) = trials
            .values()
            .find(|t| t.defendant_id == criminal.character_id)
        {
            return Some(t.id);
        }
        let mut criminal_region = court_room_region_by_faction(mother_of(server, criminal));
        let arrestor_region = match arrestor {
            Some(a) => court_room_region_by_faction(mother_of(server, a)),
            None => criminal.data().offline_guilty_region as u8,
        };
        let criminal_name = criminal.data().unit.name.clone();
        if let Some(a) = arrestor {
            if criminal_region == court_room_region::INVALID
                && arrestor_region == court_room_region::INVALID
            {
                let an = a.data().unit.name.clone();
                tracing::debug!(
                    "ArrestCriminal: {an} cannot arrest {criminal_name}, both fall outside of justice system"
                );
                return None;
            }
        }
        if criminal_region == court_room_region::INVALID {
            criminal_region = arrestor_region;
        }
        let Some(room) = rooms.iter().find(|r| r.region == criminal_region) else {
            tracing::warn!("Failed to find a court room for {criminal_name}");
            return None;
        };
        if !server
            .config
            .justice
            .jails
            .iter()
            .any(|j| j.faction == room.faction)
        {
            tracing::warn!("No jail configured for court {}", room.id);
            return None;
        }
        if arrestor.is_some() {
            criminal.data().arrest_count += 1;
        }
        let Some(tid) = create_trial_case(server, trials, criminal, room.region) else {
            tracing::warn!("Failed to create a court case for {criminal_name}");
            return None;
        };
        if let Some(a) = arrestor {
            let an = a.data().unit.name.clone();
            let inst = criminal.data().unit.transform.instance_id;
            if let Some(w) = server.world.world(inst) {
                broadcast(
                    &w,
                    criminal,
                    &SCCriminalArrestedPacket {
                        criminal_obj_id: criminal.obj_id,
                        criminal: criminal_name,
                        arrestor: an,
                    },
                    true,
                );
            }
        }
        let t = trials.get_mut(&tid)?;
        enter_court_jail(server, t, room, fx);
        Some(tid)
    }

    /// `AddNewEvidence(newEvent)`.
    pub fn add_new_evidence(&self, server: &GameServer, e: &CrimeEvent) {
        let mut st = self.st();
        let Some(t) = st
            .trials
            .values_mut()
            .find(|t| t.defendant_id == e.criminal && t.step < step::TRIAL_CANCELLED)
        else {
            return;
        };
        t.evidence.push(e.clone());
        calculate_jail_time(server, t);
        let entry = server.systems.social.crimes.record_entry(server, e);
        t.send_packets(&SCCrimeRecordsPacket {
            trial_id: t.id,
            unknown1: 0,
            total_count: t.evidence.len() as i32,
            this_count: 1,
            entries: vec![entry],
        });
    }

    /// `ReplyImprisonOrTrial(defendant, requestTrial)`.
    pub fn reply_imprison_or_trial(
        &self,
        server: &GameServer,
        defendant: &PlayerRef,
        request_trial: bool,
        now_ms: i64,
    ) {
        let mut fx = Vec::new();
        {
            let mut st = self.st();
            let State { rooms, trials, .. } = &mut *st;
            let Some(t) = trials
                .values_mut()
                .find(|t| t.defendant_id == defendant.character_id)
            else {
                let name = defendant.data().unit.name.clone();
                tracing::warn!(
                    "{name} ({}) sent a CSReplyImprisonOrTrialPacket without being on trial. requestTrial={request_trial}",
                    defendant.character_id
                );
                return;
            };
            if !Arc::ptr_eq(&t.defendant, defendant) {
                return;
            }
            if t.step != step::DEFENDANT_AWAITING_TRIAL {
                return;
            }
            if !request_trial {
                // Directly to jail with trial.JailTime.
                result_is_guilty(server, rooms, defendant, t, true, now_ms, &mut fx);
            } else if t.current_step_end_ms == TIME_MAX {
                // End the waiting time now.
                defendant.data().accept_trial_count += 1;
                t.current_step_end_ms = now_ms;
            }
        }
        run_effects(server, fx);
    }

    /// `CSCancelTrialPacket`: `ResultIsGuilty(activeChar, trial, true)` for the defendant (a
    /// missing trial is the C# `NullReferenceException`: logged).
    pub fn cancel_trial(&self, server: &GameServer, p: &PlayerRef, trial_id: u32, now_ms: i64) {
        let mut fx = Vec::new();
        {
            let mut st = self.st();
            let State { rooms, trials, .. } = &mut *st;
            let Some(t) = trials.get_mut(&trial_id) else {
                tracing::error!(
                    "CSCancelTrialPacket: NullReferenceException (trial {trial_id} not found)"
                );
                return;
            };
            if Arc::ptr_eq(&t.defendant, p) {
                if t.step < step::TRIAL_CANCELLED {
                    result_is_guilty(server, rooms, p, t, true, now_ms, &mut fx);
                }
            } else {
                let name = p.data().unit.name.clone();
                sus_log(
                    server,
                    crate::systems::services::sus::CATEGORY_CHEATING,
                    p.character_id,
                    &format!("Player {name} tried to cancel a trial they do not belong to"),
                );
            }
        }
        run_effects(server, fx);
    }

    /// `ResultIsGuilty(defendant, trial, pleadGuilty)` for a trial id.
    pub fn result_is_guilty(
        &self,
        server: &GameServer,
        defendant: &PlayerRef,
        trial_id: u32,
        plead_guilty: bool,
        now_ms: i64,
    ) {
        let mut fx = Vec::new();
        {
            let mut st = self.st();
            let State { rooms, trials, .. } = &mut *st;
            match trials.get_mut(&trial_id) {
                Some(t) if Arc::ptr_eq(&t.defendant, defendant) => {
                    result_is_guilty(server, rooms, defendant, t, plead_guilty, now_ms, &mut fx)
                }
                _ => tracing::warn!("No matching trial for {}", defendant.character_id),
            }
        }
        run_effects(server, fx);
    }

    /// Confirm records only for the caller's occupied jury seat.
    pub fn jury_end_testimony(
        &self,
        jury_member: &PlayerRef,
        trial_id: u32,
        jury_id: i32,
        now_ms: i64,
    ) {
        let mut st = self.st();
        let Some(t) = st.trials.get_mut(&trial_id) else {
            return;
        };
        let name = jury_member.data().unit.name.clone();
        if t.step != step::CONFIRM_CRIMINAL_RECORD {
            tracing::warn!(
                "JuryEndTestimony by {name} for trial {} with seatId {jury_id} was confirmed outside of the correct trial step",
                t.id
            );
            return;
        }
        match t.jury.get_mut(&jury_id) {
            Some(j)
                if j.seat_id == jury_id
                    && j.jury_member
                        .as_ref()
                        .is_some_and(|m| Arc::ptr_eq(m, jury_member))
                    && !j.confirm_testimony =>
            {
                j.confirm_testimony = true
            }
            _ => {
                tracing::warn!(
                    "JuryEndTestimony by {name} seems to be invalid for trial {} with seatId {jury_id}",
                    t.id
                );
                return;
            }
        }
        let total = t.jury.values().filter(|j| j.jury_member.is_some()).count() as i32;
        let count = t
            .jury
            .values()
            .filter(|j| j.jury_member.is_some() && j.confirm_testimony)
            .count() as i32;
        t.send_packets(&SCChangeJuryOkCountPacket { count, total });
        if t.all_jury_confirmed_criminal_records() {
            t.current_step_end_ms = now_ms;
        }
    }

    /// `JuryVerdict(juryMember, trial, juryId, sentence)`.
    pub fn jury_verdict(
        &self,
        jury_member: &PlayerRef,
        trial_id: u32,
        jury_id: i32,
        sentence: u8,
        now_ms: i64,
    ) {
        let mut st = self.st();
        let Some(t) = st.trials.get_mut(&trial_id) else {
            return;
        };
        let name = jury_member.data().unit.name.clone();
        if !(sentence_result::NOT_GUILTY..=sentence_result::GUILTY5).contains(&sentence) {
            tracing::warn!("JuryVerdict by {name}: invalid sentence {sentence}");
            return;
        }
        if t.step != step::JURY_VERDICT {
            tracing::warn!(
                "JuryVerdict by {name} for trial {} with seatId {jury_id} was confirmed outside of the correct trial step",
                t.id
            );
            return;
        }
        let Some(j) = t.jury.get_mut(&jury_id).filter(|j| j.seat_id == jury_id) else {
            tracing::warn!(
                "JuryVerdict by {name} seems to be invalid for trial {} with seatId {jury_id}",
                t.id
            );
            return;
        };
        match &j.jury_member {
            None => {
                tracing::error!("JuryVerdict: NullReferenceException (seat {jury_id} is empty)");
                return;
            }
            Some(m) if !Arc::ptr_eq(m, jury_member) => {
                tracing::warn!("Jury Verdict Seat does not match the expected player");
                return;
            }
            Some(_) => {}
        }
        if j.selected_sentence >= 0 {
            return;
        }
        j.selected_sentence = i32::from(sentence);
        let total = t.jury.values().filter(|j| j.jury_member.is_some()).count() as i32;
        let count = t
            .jury
            .values()
            .filter(|j| j.jury_member.is_some() && j.selected_sentence >= 0)
            .count() as i32;
        let pkt = SCRulingStatusPacket {
            count,
            total,
            sentence_type: 0,
            sentence_time: 0,
        };
        t.defendant.send(pkt.clone());
        for j in t.jury.values() {
            if j.selected_sentence >= 0 {
                if let Some(m) = &j.jury_member {
                    m.send(pkt.clone());
                }
            }
        }
        if t.all_jury_selected_sentence() {
            t.current_step_end_ms = now_ms;
        }
    }

    /// `SkipFinalStatementReply(defendant, trial)`.
    pub fn skip_final_statement_reply(&self, defendant: &PlayerRef, trial_id: u32, now_ms: i64) {
        let mut st = self.st();
        let Some(t) = st.trials.get_mut(&trial_id) else {
            return;
        };
        if !Arc::ptr_eq(&t.defendant, defendant) {
            return;
        }
        if t.step != step::CLOSING_STATEMENT {
            return;
        }
        t.current_step_end_ms = now_ms;
    }

    /// `IsPlayerInCourt(playerId)`.
    pub fn is_player_in_court(&self, player_id: u32) -> bool {
        self.st().trials.values().any(|t| {
            t.defendant_id == player_id
                || t.jury.values().any(|j| {
                    j.jury_member
                        .as_ref()
                        .is_some_and(|m| m.character_id == player_id)
                })
        })
    }

    /// `GetParticipatingTrial(player)` (trial id).
    pub fn get_participating_trial(&self, player: &PlayerRef) -> Option<u32> {
        let st = self.st();
        participating_trial(&st.rooms, &st.trials, player)
    }

    /// `JoinTrialAudience(player, doodadTemplateId)`.
    pub fn join_trial_audience(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        doodad_template_id: u32,
    ) {
        let mut st = self.st();
        for room in st.rooms.iter_mut() {
            if room.audience_members.iter().any(|a| Arc::ptr_eq(a, player)) {
                return; // Already in this audience
            }
            let mut found = false;
            for seat in &room.audience_seats {
                if seat.data().template_id != doodad_template_id {
                    continue;
                }
                // player.GetDistanceTo(seat) (2D, minus the player's model size)
                use crate::systems::skills::geometry::{distance_to, Target};
                let dist = distance_to(
                    server,
                    &Target::Obj(WorldObject::from(player)),
                    &Target::Obj(WorldObject::from(seat)),
                    false,
                );
                // `!(dist <= 5f)` (NaN never matches).
                if dist.is_nan() || dist > 5.0 {
                    continue;
                }
                found = true;
                break;
            }
            if found {
                room.audience_members.push(player.clone());
                let (name, inst) = {
                    let c = player.data();
                    (c.unit.name.clone(), c.unit.transform.instance_id)
                };
                if let Some(w) = server.world.world(inst) {
                    broadcast(
                        &w,
                        player,
                        &SCTrialAudienceJoinedPacket {
                            trial_id: room.current_trial.unwrap_or(0),
                            bc: player.obj_id,
                            audience_name: name,
                        },
                        true,
                    );
                }
                room.join_chat(player);
                return;
            }
        }
        let name = player.data().unit.name.clone();
        let msg = format!("{name} tried to join courtroom audience while not being near a seat");
        tracing::warn!("{msg}");
        sus_log(
            server,
            crate::systems::services::sus::CATEGORY_CHEATING,
            player.character_id,
            &msg,
        );
    }

    /// `LeaveTrialAudience(player)`.
    pub fn leave_trial_audience(&self, server: &GameServer, player: &PlayerRef) {
        let mut st = self.st();
        for room in st.rooms.iter_mut() {
            if let Some(i) = room
                .audience_members
                .iter()
                .position(|a| Arc::ptr_eq(a, player))
            {
                room.audience_members.remove(i);
                let (name, inst) = {
                    let c = player.data();
                    (c.unit.name.clone(), c.unit.transform.instance_id)
                };
                if let Some(w) = server.world.world(inst) {
                    broadcast(
                        &w,
                        player,
                        &SCTrialAudienceLeftPacket {
                            bc: player.obj_id,
                            audience_name: name,
                        },
                        true,
                    );
                }
                room.leave_chat(player);
                return;
            }
        }
        let name = player.data().unit.name.clone();
        tracing::debug!("{name} tried to leave the courtroom audience but was not part of it");
    }
}

/// The jury part of the `EndTrial` cleanup for one member.
fn cleanup_trial(
    server: &GameServer,
    rooms: &mut [CourtRoom],
    t: &TrialData,
    reward: bool,
    fx: &mut Effects,
) {
    for j in t.jury.values() {
        if let Some(m) = &j.jury_member {
            cleanup_jury_member(server, rooms, t, m, reward && j.selected_sentence >= 0, fx);
        }
    }
    if let Some(room) = room_by_id_mut(rooms, t.court_room) {
        room.leave_chat(&t.defendant);
        if room.current_trial == Some(t.id) {
            room.current_trial = None;
        }
    }
    {
        let mut c = t.defendant.data();
        c.unit.buffs.remove_buff(buff::FORCIBLY_AWAITING_TRIAL);
        c.unit.buffs.remove_buff(buff::TRIAL_DEFENDANT);
        c.unit.buffs.remove_buff(buff::CANNOT_ESCAPE);
    }
    fx.push(Effect::DispatchBuffs(t.defendant.clone()));
}

fn cleanup_jury_member(
    server: &GameServer,
    rooms: &[CourtRoom],
    t: &TrialData,
    m: &PlayerRef,
    reward: bool,
    fx: &mut Effects,
) {
    let escaped = server.config.justice.allow_jury_escape
        && !m.data().unit.buffs.check_buff(buff::COURT_HOUSE);
    if escaped {
        // The jury is allowed to escape: only cancel the MainWorldPosition.
        m.data().main_world_position = None;
    } else {
        let cur = world_pos_rot(server, &WorldObject::from(m));
        let (name, main) = {
            let c = m.data();
            (c.unit.name.clone(), c.main_world_position)
        };
        let pos = main.map_or((cur.x, cur.y, cur.z), |m| (m.x, m.y, m.z));
        let cur3 = (cur.x, cur.y, cur.z);
        tracing::debug!(
            "Returning {name} to {} <- {}",
            fmt_vec3(pos),
            fmt_vec3(cur3)
        );
        fx.push(Effect::ForceDismount(m.clone()));
        m.data().disabled_set_position = true;
        tracing::debug!(
            "Returning jury member {name} to their old position {}",
            fmt_vec3(pos)
        );
        let world_id = main.map_or_else(|| m.data().unit.transform.world_id, |p| p.world_id);
        teleport(
            server,
            m,
            SpawnPosition {
                world_id,
                x: pos.0,
                y: pos.1,
                z: pos.2,
                yaw: main.map_or(cur.rot_z, |p| p.rot_z).to_degrees(),
                ..Default::default()
            },
            teleport_reason::JURY,
            fx,
        );
        let pkt = {
            let mut c = m.data();
            c.main_world_position = None;
            // Update the trials attended count.
            if reward {
                let v = c.jury_point.saturating_add(1);
                set_jury_point(&mut c, v)
            } else {
                None
            }
        };
        tracing::debug!("{name} back at {}", fmt_vec3(cur3));
        if let Some(pkt) = pkt {
            m.send(pkt);
        }
    }
    {
        let mut c = m.data();
        c.unit.buffs.remove_buff(buff::COURT_HOUSE);
        c.unit.buffs.remove_buff(buff::JURY);
        c.unit.buffs.remove_buff(buff::CANNOT_ESCAPE);
    }
    fx.push(Effect::DispatchBuffs(m.clone()));
    if let Some(room) = room_by_id(rooms, t.court_room) {
        room.leave_chat(m);
    }
}

/// `TrialManager.HandlePlayerLogin(player)`.
pub fn handle_player_login(server: &GameServer, p: &PlayerRef) {
    server.systems.social.trials.handle_player_login(server, p);
}

/// `ChatManager.GetTrialChat(character)`. A player without a home court that takes part in no
/// trial is the C# `NullReferenceException` (logged, `None`).
pub fn trial_chat(server: &GameServer, p: &PlayerRef) -> Option<ChannelRef> {
    let region = court_room_region_by_faction(mother_of(server, p));
    if region == court_room_region::INVALID {
        let trials = &server.systems.social.trials;
        let st = trials.st();
        return match participating_trial(&st.rooms, &st.trials, p) {
            Some(tid) => {
                let r = st.trials.get(&tid).map(|t| t.court_region)?;
                server.chat.court_chat(r)
            }
            None => {
                tracing::error!(
                    "GetTrialChat: NullReferenceException (character {} has no court and no trial)",
                    p.character_id
                );
                None
            }
        };
    }
    server.chat.court_chat(region)
}
