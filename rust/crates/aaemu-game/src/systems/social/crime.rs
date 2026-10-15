//! `Core/Managers/CrimeManager` + `Models/Game/Crime/{CrimeEvent,CrimeKind,BountyDescription}`
//! and the crime members of `Character` (`AddCrime`, `GetCrimeState`, the `CrimePoint` /
//! `InfamyPoint` / `JuryPoint` setters) and `CharacterManager.AddOfflineCrimePoints`.
//!
//! Locking: the manager's state mutex is a leaf for everything but player data: it is never held
//! while a player's data is locked, and the code releases it before calling into the trial
//! manager (`TrialManager.AddNewEvidence`) or the hooks that create / change doodads. The trial
//! manager may take this mutex while holding its own (order: trials -> crimes -> player data).

use std::collections::{BTreeMap, HashMap};
use std::sync::{Arc, Mutex, Weak};

use aaemu_commons::PacketStream;
use sqlx::Row;

use crate::data::skills::BuffKind;
use crate::db::{dt, from_unix, unix};
use crate::models::buff::{Buff, SkillCaster};
use crate::models::character::Character;
use crate::models::doodad::doodad_owner_type;
use crate::models::transform::convert_long;
use crate::server::GameServer;
use crate::systems::social::achievement::char_record_kind;
use crate::systems::social::achievement_hooks::{AchievementInput, RecordMatch};
use crate::systems::social::packets::trial::{
    CrimeRecordEntry, SCCrimeChangedPacket, SCJuryPointChangedPacket,
};
use crate::systems::social::util;
use crate::world::{DoodadRef, PlayerRef, WorldObject};

/// `CrimeManager.WantedCrimePointThreshold`.
pub const WANTED_CRIME_POINT_THRESHOLD: i16 = 50;
/// `CrimeManager.PirateCrimePointThreshold`.
pub const PIRATE_CRIME_POINT_THRESHOLD: i32 = 3000;

/// `CrimeKind`.
pub mod crime_kind {
    pub const INVALID: u8 = 0;
    pub const NONE: u8 = 1;
    pub const ASSAULT: u8 = 2;
    pub const MURDER: u8 = 3;
    pub const THEFT: u8 = 4;
    pub const BOT_REPORT: u8 = 5;
    pub const FALSE_BOT_REPORT: u8 = 6;
}

/// The `BuffConstants` used by the crime / trial system.
pub mod buff {
    pub const PRISONER_NUIAN: u32 = 631;
    pub const PRISONER_HARANYAN: u32 = 2028;
    pub const FORCIBLY_AWAITING_TRIAL: u32 = 3619;
    pub const JURY: u32 = 3621;
    pub const TRIAL_DEFENDANT: u32 = 3623;
    pub const WANTED: u32 = 3710;
    pub const SUSPECTED_USER: u32 = 4862;
    pub const TRANSFORMING_INTO_PRIME_SUSPECT: u32 = 4947;
    pub const COURT_HOUSE: u32 = 4970;
    pub const CANNOT_ESCAPE: u32 = 6729;
    /// Buff tags (`BuffConstants.Tag*`).
    pub const TAG_PRISONER: u32 = 344;
    pub const TAG_WANTED: u32 = 894;
    pub const TAG_OFFENDER: u32 = 1008;
    pub const TAG_SUSPECTS: u32 = 1035;
}

/// `DoodadConstants` (crime evidence).
pub mod evidence_doodad {
    pub const SMALL_BLOODSTAIN: u32 = 877;
    pub const LARGE_BLOODSTAIN: u32 = 878;
    pub const FOOTPRINT_MALE: u32 = 3313;
    pub const FOOTPRINT_FEMALE: u32 = 3314;
}

/// `Gender.Female`.
const GENDER_FEMALE: u8 = 2;

/// `CrimeEvent`. Times are unix seconds (`0` = `DateTime.MinValue`).
#[derive(Debug, Clone, PartialEq)]
pub struct CrimeEvent {
    pub id: u32,
    pub criminal: u32,
    pub victim: u32,
    pub reporter: u32,
    /// Source template of the stolen doodad.
    pub doodad_template: u32,
    /// [`crime_kind`].
    pub crime_kind: u8,
    pub zone_key: u32,
    pub position: (f32, f32, f32),
    pub crime_time: i64,
    pub report_time: i64,
    pub used_skill_id: u32,
    pub doodad_next_func_group: u32,
    pub doodad_func_id: u32,
    pub msg: String,
    /// `JudgementTime` (`0` = `DateTime.MinValue`: not judged yet).
    pub judgement_time: i64,
}

impl Default for CrimeEvent {
    fn default() -> Self {
        let now = util::now_secs();
        Self {
            id: 0,
            criminal: 0,
            victim: 0,
            reporter: 0,
            doodad_template: 0,
            crime_kind: crime_kind::INVALID,
            zone_key: 0,
            position: (0.0, 0.0, 0.0),
            crime_time: now,
            report_time: now,
            used_skill_id: 0,
            doodad_next_func_group: 0,
            doodad_func_id: 0,
            msg: String::new(),
            judgement_time: 0,
        }
    }
}

/// `BountyDescription`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct BountyDescription {
    pub player_id: u32,
    pub player_name: String,
    pub player_level: u8,
    pub zone_id: u32,
    pub pos: (f32, f32, f32),
    pub bounty_amount: u64,
    pub crime_points: i16,
    pub is_live: bool,
    pub player_race: u8,
    pub type184: i32,
    pub arrest_count: i32,
    pub accept_guilty_count: i32,
    pub accept_trial_count: i32,
    pub guilty_count: i32,
    pub not_guilty_count: i32,
    pub seconds: i32,
}

impl BountyDescription {
    /// One entry of `SCBountyListPacket.Write` (`ConvertLongX` for both x and y; guilty is
    /// written before notGuilty although the client reads notGuilty first — as in C#).
    pub fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.player_id)
            .write_str(&self.player_name)
            .write_u8(self.player_level)
            .write_u32(self.zone_id)
            .write_i64(convert_long(self.pos.0))
            .write_i64(convert_long(self.pos.1))
            .write_f32(self.pos.2)
            .write_u64(self.bounty_amount)
            .write_i16(self.crime_points)
            .write_bool(self.is_live)
            .write_u8(self.player_race)
            .write_i32(self.type184)
            .write_i32(self.arrest_count)
            .write_i32(self.accept_guilty_count)
            .write_i32(self.accept_trial_count)
            .write_i32(self.guilty_count)
            .write_i32(self.not_guilty_count)
            .write_i32(self.seconds);
    }
}

// ---------------------------------------------------------------------------------------------
// Character members
// ---------------------------------------------------------------------------------------------

/// `Buffs.AddBuff(buffId, caster, forcedDuration)` with an explicit caster (`caster_obj_id`,
/// `caster` = (`Caster.Id`, `Caster.Level`)). Logs and skips unknown templates.
pub fn add_buff_from(
    server: &GameServer,
    c: &mut Character,
    buff_id: u32,
    caster_obj_id: u32,
    caster: Option<(u32, u8)>,
    forced_duration: i32,
    now_ms: i64,
) {
    let Some(t) = server.game_data.skills.buff(buff_id) else {
        tracing::error!("AddBuff: no buff template {buff_id}");
        return;
    };
    let sc = SkillCaster {
        kind: 0,
        obj_id: caster_obj_id,
    };
    let b = Buff::new(t.clone(), sc, caster, now_ms);
    c.unit.buffs.add_buff(b, 0, forced_duration, now_ms);
}

/// `Buffs.AddBuff(buffId, self, forcedDuration)`.
pub fn add_own_buff(
    server: &GameServer,
    c: &mut Character,
    buff_id: u32,
    forced_duration: i32,
    now_ms: i64,
) {
    let (obj, id, level) = (c.obj_id, c.id, c.unit.level);
    add_buff_from(
        server,
        c,
        buff_id,
        obj,
        Some((id, level)),
        forced_duration,
        now_ms,
    );
}

/// `Character.CrimePoint` setter (`value != field`: clamp to >= 0, `CheckWantedThreshold`).
pub fn set_crime_point(server: &GameServer, c: &mut Character, value: i16, now_ms: i64) {
    if value != c.crime_point {
        c.crime_point = value.max(0);
        c.check_wanted_threshold(server, now_ms);
    }
}

/// `Character.InfamyPoint` setter (`value != field`: clamp to >= 0, `CheckWantedThreshold`).
pub fn set_infamy_point(server: &GameServer, c: &mut Character, value: i32, now_ms: i64) {
    if value != c.infamy_point {
        c.infamy_point = value.max(0);
        c.check_wanted_threshold(server, now_ms);
    }
}

/// `Character.JuryPoint` setter: returns the `SCJuryPointChangedPacket` to send when the value
/// changed.
pub fn set_jury_point(c: &mut Character, value: i32) -> Option<SCJuryPointChangedPacket> {
    if c.jury_point == value {
        return None;
    }
    c.jury_point = value;
    Some(SCJuryPointChangedPacket { jury_point: value })
}

/// `Character.GetCrimeState()`.
pub fn crime_state(c: &Character) -> i16 {
    if c.unit.buffs.check_buff(buff::FORCIBLY_AWAITING_TRIAL) {
        return 2;
    }
    if c.unit.buffs.check_buff(buff::WANTED) {
        return 1;
    }
    0
}

/// Add crime and infamy points without wrapping away the wanted status.
pub fn add_crime(
    server: &GameServer,
    c: &mut Character,
    amount: i16,
    now_ms: i64,
) -> SCCrimeChangedPacket {
    let new_amount = i32::from(c.crime_point) + i32::from(amount);
    set_crime_point(
        server,
        c,
        new_amount.clamp(0, i32::from(i16::MAX)) as i16,
        now_ms,
    );
    let infamy = c.infamy_point.saturating_add(i32::from(amount));
    set_infamy_point(server, c, infamy, now_ms);
    if c.infamy_point < 0 {
        set_infamy_point(server, c, 0, now_ms);
    }
    SCCrimeChangedPacket {
        points: i32::from(amount),
        crime_points: c.crime_point,
        infamy_points: c.infamy_point,
        crime_state: crime_state(c),
    }
}

/// `CharacterManager.AddOfflineCrimePoints(playerId, crimePointsToAdd)`.
pub async fn add_offline_crime_points(server: &GameServer, player_id: u32, points: i16) {
    let res = sqlx::query(
        "UPDATE characters SET `crime_point` = LEAST(32767, GREATEST(0, CAST(`crime_point` AS SIGNED) + ?)), \
         `crime_record` = LEAST(2147483647, GREATEST(0, CAST(`crime_record` AS SIGNED) + ?)) WHERE `id` = ?",
    )
    .bind(points)
    .bind(points)
    .bind(player_id)
    .execute(&server.db)
    .await;
    match res {
        Ok(r) if r.rows_affected() == 1 => {}
        Ok(_) => tracing::warn!(
            "Failed to update offline crime points for player {player_id}! (Add {points})"
        ),
        Err(e) => tracing::error!(
            "Failed to update offline crime points for player {player_id}! (Add {points}): {e}"
        ),
    }
}

/// `SusManager.LogActivity(category, character, description)` (`systems::services::sus`; the
/// `audit_char_sus` insert runs as a task).
pub(crate) fn sus_log(server: &GameServer, category: &str, character_id: u32, msg: &str) {
    tracing::warn!("[Sus:{category}] ({character_id}) {msg}");
    let (Some(srv), Ok(rt)) = (server.self_arc(), tokio::runtime::Handle::try_current()) else {
        return;
    };
    let player = server.world.character_by_id(character_id);
    let (category, msg) = (category.to_string(), msg.to_string());
    rt.spawn(async move {
        srv.systems
            .services
            .sus
            .log_activity_player(&srv, &category, player.as_ref(), &msg)
            .await;
    });
}

// ---------------------------------------------------------------------------------------------
// CrimeManager
// ---------------------------------------------------------------------------------------------

/// The victim values `SCCrimeRecordsPacket` reads from an offline victim (`Character.Load`),
/// prefetched from MySQL.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct VictimInfo {
    pub name: String,
    pub faction_id: u32,
    pub expedition_id: u32,
}

#[derive(Debug, Default)]
struct State {
    /// `CrimeEvents` (a `ConcurrentDictionary`: iterated here in id order).
    events: BTreeMap<u32, CrimeEvent>,
    deleted: Vec<u32>,
    /// `UpdatedEventIds` (never cleared, as in C#).
    updated: Vec<u32>,
    /// `ReportedSuspects` (bot id -> reporter ids).
    reported_suspects: HashMap<u32, Vec<u32>>,
    /// Offline victims (see [`VictimInfo`]).
    victims: HashMap<u32, VictimInfo>,
    /// Claims are tied to the live evidence handle, so reused object ids remain reportable.
    reported_evidence: Vec<Weak<crate::world::Entity<crate::models::doodad::Doodad>>>,
}

/// `CrimeManager`.
#[derive(Debug, Default)]
pub struct CrimeManager {
    state: Mutex<State>,
}

impl CrimeManager {
    fn st(&self) -> std::sync::MutexGuard<'_, State> {
        self.state.lock().expect("crime manager poisoned")
    }

    /// `GetCrimesOfPlayer(playerId, includeOld)`.
    pub fn crimes_of_player(&self, player_id: u32, include_old: bool) -> Vec<CrimeEvent> {
        self.st()
            .events
            .values()
            .filter(|e| e.criminal == player_id && (include_old || e.judgement_time <= 0))
            .cloned()
            .collect()
    }

    /// A crime event by id.
    pub fn get(&self, id: u32) -> Option<CrimeEvent> {
        self.st().events.get(&id).cloned()
    }

    /// Adds a loaded / created event (tests and tools; `CrimeEvents.TryAdd`).
    pub fn insert(&self, e: CrimeEvent) -> bool {
        let mut st = self.st();
        if st.events.contains_key(&e.id) {
            return false;
        }
        st.events.insert(e.id, e);
        true
    }

    /// `UpdatedEventIds` / `DeletedEventIds` (tests).
    pub fn pending_ids(&self) -> (Vec<u32>, Vec<u32>) {
        let st = self.st();
        (st.updated.clone(), st.deleted.clone())
    }

    /// `Load()`: `SELECT * FROM crime` (+ prefetches the offline victims of the records).
    pub async fn load(&self, server: &GameServer) -> crate::Result<()> {
        tracing::info!("Loading crime events...");
        {
            let mut st = self.st();
            st.events.clear();
            st.deleted.clear();
            st.updated.clear();
        }
        let rows = sqlx::query("SELECT * FROM crime")
            .fetch_all(&server.db)
            .await?;
        let mut victims = Vec::new();
        for r in &rows {
            let u = |c: &str| -> u32 {
                r.try_get::<Option<u32>, _>(c)
                    .ok()
                    .flatten()
                    .unwrap_or_default()
            };
            let f = |c: &str| -> f32 {
                r.try_get::<Option<f32>, _>(c)
                    .ok()
                    .flatten()
                    .unwrap_or_default()
            };
            let e = CrimeEvent {
                id: u("id"),
                criminal: u("criminal"),
                victim: u("victim"),
                reporter: u("reporter"),
                doodad_template: u("doodad_template"),
                crime_kind: u("crime_type") as u8,
                zone_key: u("zone_key"),
                position: (f("x"), f("y"), f("z")),
                crime_time: unix(dt(r, "crime_time")),
                report_time: unix(dt(r, "report_time")),
                used_skill_id: u("skill_id"),
                doodad_next_func_group: u("next_func_group"),
                doodad_func_id: u("func_id"),
                msg: r
                    .try_get::<Option<String>, _>("msg")
                    .ok()
                    .flatten()
                    .unwrap_or_default(),
                judgement_time: unix(dt(r, "judgement_time")),
            };
            victims.push(e.victim);
            let id = e.id;
            if !self.insert(e) {
                tracing::warn!("Was not able to load Crime Event {id}");
            }
        }
        self.prefetch_victims(server, &victims).await;
        Ok(())
    }

    /// `Save(connection, transaction)`: returns (updated rows, deleted rows).
    pub async fn save(
        &self,
        tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
    ) -> crate::Result<(usize, usize)> {
        let mut delete_count = 0usize;
        let mut update_count = 0usize;
        let deleted = self.st().deleted.clone();
        if !deleted.is_empty() {
            let list = deleted
                .iter()
                .map(u32::to_string)
                .collect::<Vec<_>>()
                .join(",");
            let r = sqlx::query(&format!("DELETE FROM crime WHERE `id` IN({list})"))
                .execute(&mut **tx)
                .await?;
            delete_count += r.rows_affected() as usize;
            if delete_count != deleted.len() {
                tracing::error!(
                    "Some crime events could not be deleted, only {delete_count}/{} events removed !",
                    deleted.len()
                );
            }
            self.st().deleted.retain(|id| !deleted.contains(id));
        }
        let updated: Vec<CrimeEvent> = {
            let st = self.st();
            st.updated
                .iter()
                .filter_map(|id| st.events.get(id).cloned())
                .collect()
        };
        for e in updated {
            let res = sqlx::query(
                "REPLACE INTO crime (`id`,`criminal`,`victim`,`reporter`,`doodad_template`,`crime_type`,`zone_key`,`x`,`y`,`z`,\
                 `crime_time`,`report_time`,`skill_id`,`next_func_group`,`func_id`,`msg`,`judgement_time`\
                 ) VALUES ( ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            )
            .bind(e.id)
            .bind(e.criminal)
            .bind(e.victim)
            .bind(e.reporter)
            .bind(e.doodad_template)
            .bind(e.crime_kind)
            .bind(e.zone_key)
            .bind(e.position.0)
            .bind(e.position.1)
            .bind(e.position.2)
            .bind(from_unix(e.crime_time))
            .bind(from_unix(e.report_time))
            .bind(e.used_skill_id)
            .bind(e.doodad_next_func_group)
            .bind(e.doodad_func_id)
            .bind(&e.msg)
            .bind(from_unix(e.judgement_time))
            .execute(&mut **tx)
            .await;
            match res {
                Ok(r) => update_count += r.rows_affected() as usize,
                Err(err) => tracing::error!("{err}"),
            }
        }
        Ok((update_count, delete_count))
    }

    /// Loads the `SCCrimeRecordsPacket` values of offline victims that are not cached yet
    /// (`Character.Load(victim)` in the C# `Write`; done ahead because packets are built
    /// synchronously). Errors are logged.
    pub async fn prefetch_victims(&self, server: &GameServer, ids: &[u32]) {
        let mut missing: Vec<u32> = Vec::new();
        {
            let st = self.st();
            for &id in ids {
                if id != 0
                    && !st.victims.contains_key(&id)
                    && !missing.contains(&id)
                    && server.world.character_by_id(id).is_none()
                {
                    missing.push(id);
                }
            }
        }
        if missing.is_empty() {
            return;
        }
        let marks = vec!["?"; missing.len()].join(",");
        let sql = format!(
            "SELECT CAST(id AS SIGNED) AS id, name, CAST(faction_id AS SIGNED) AS faction_id, \
             CAST(expedition_id AS SIGNED) AS expedition_id FROM characters WHERE `id` IN ({marks}) and `deleted`=0"
        );
        let mut q = sqlx::query(&sql);
        for id in &missing {
            q = q.bind(*id);
        }
        match q.fetch_all(&server.db).await {
            Ok(rows) => {
                let mut st = self.st();
                for r in rows {
                    let id = r.try_get::<i64, _>("id").unwrap_or_default() as u32;
                    st.victims.insert(
                        id,
                        VictimInfo {
                            name: r.try_get::<String, _>("name").unwrap_or_default(),
                            faction_id: r.try_get::<i64, _>("faction_id").unwrap_or_default()
                                as u32,
                            expedition_id: r.try_get::<i64, _>("expedition_id").unwrap_or_default()
                                as u32,
                        },
                    );
                }
            }
            Err(e) => tracing::error!("Failed to load crime victims {missing:?}: {e}"),
        }
    }

    /// Caches an offline victim's values (tests / tools).
    pub fn set_victim_info(&self, id: u32, info: VictimInfo) {
        self.st().victims.insert(id, info);
    }

    /// The values one `SCCrimeRecordsPacket` entry reads: the victim from the world
    /// (`GetCharacterById`) or the prefetched offline values, the reporter's name from the
    /// `NameManager`. Must be called without any player data lock held.
    pub fn record_entry(&self, server: &GameServer, e: &CrimeEvent) -> CrimeRecordEntry {
        let (victim_name, faction, expedition) = match server.world.character_by_id(e.victim) {
            Some(v) => {
                let c = v.data();
                (c.unit.name.clone(), c.unit.faction_id, c.unit.expedition_id)
            }
            None => match self.st().victims.get(&e.victim) {
                Some(i) => (i.name.clone(), i.faction_id, i.expedition_id),
                None => (String::new(), 0, 0),
            },
        };
        // `victim?.Faction?.Id`, `victim?.Expedition?.Id`: unknown objects are null.
        let victim_faction = if server.game_data.factions.get(faction).is_some() {
            faction
        } else {
            0
        };
        let victim_expedition = if expedition != 0 && server.expeditions.get(expedition).is_some() {
            expedition
        } else {
            0
        };
        CrimeRecordEntry {
            id: e.id,
            victim: e.victim,
            victim_name,
            reporter: e.reporter,
            reporter_name: server.names.get_character_name(e.reporter),
            victim_faction,
            victim_expedition,
            crime_kind: e.crime_kind,
            doodad_template: e.doodad_template,
            used_skill_id: e.used_skill_id,
            position: e.position,
            msg: e.msg.clone(),
            report_time: e.report_time,
        }
    }

    /// `ReportCrime(reporter, evidence, usedSkillId, doodadNextFuncGroup, doodadFuncId,
    /// message)`. Returns the new event (`None` when the report failed).
    #[allow(clippy::too_many_arguments)]
    pub async fn report_crime(
        &self,
        server: &GameServer,
        reporter: &PlayerRef,
        evidence: &DoodadRef,
        used_skill_id: u32,
        doodad_next_func_group: i32,
        doodad_func_id: u32,
        message: &str,
    ) -> Option<CrimeEvent> {
        let (owner_type, owner_id, data, item_template_id, plant_time, zone_id, template_id, inst) = {
            let d = evidence.data();
            (
                d.owner_type,
                d.owner_id,
                d.data,
                d.item_template_id,
                d.plant_time,
                d.transform.zone_id,
                d.template_id,
                d.transform.instance_id,
            )
        };
        let pos = match server.world.world(inst) {
            Some(w) => {
                let pr = w.world_pos_rot(&WorldObject::from(evidence));
                (pr.x, pr.y, pr.z)
            }
            None => {
                let t = evidence.data().transform;
                (t.x, t.y, t.z)
            }
        };
        if owner_type == doodad_owner_type::CHARACTER && owner_id == reporter.character_id {
            let name = reporter.data().unit.name.clone();
            tracing::warn!(
                "{name} ({}) tried to somehow report their own crimes at {}",
                reporter.character_id,
                fmt_vec3(pos)
            );
            sus_log(
                server,
                crate::systems::services::sus::CATEGORY_CHEATING,
                reporter.character_id,
                &format!(
                    "Reported own crime at {} (type={template_id}",
                    fmt_vec3(pos)
                ),
            );
            return None;
        }
        let hooks = server.systems.social.hooks.get();
        let (crime_type, crime_value, next_phase) = match hooks.evidence_func(server, evidence) {
            Some(f) => (f.crime_kind, f.crime_value, f.next_phase),
            None => return None,
        };
        if owner_type != doodad_owner_type::CHARACTER
            || owner_id == 0
            || !matches!(
                crime_type,
                crime_kind::ASSAULT | crime_kind::MURDER | crime_kind::THEFT
            )
            || crime_value <= 0
        {
            return None;
        }
        // `ZoneManager.GetZoneByKey(evidence.Transform.ZoneId)?.Id`.
        let zone_key = server
            .game_data
            .zones
            .zone_by_key(zone_id)
            .map_or(0, |z| z.id);
        let new_id = match server.ids.crime.get_next_id() {
            Ok(id) => id,
            Err(e) => {
                tracing::error!("ReportCrime: {e:?}");
                return None;
            }
        };
        let new_event = CrimeEvent {
            id: new_id,
            criminal: owner_id,
            victim: data as u32,
            reporter: reporter.character_id,
            doodad_template: item_template_id,
            crime_kind: crime_type,
            crime_time: plant_time,
            report_time: util::now_secs(),
            position: pos,
            zone_key,
            used_skill_id,
            doodad_next_func_group: doodad_next_func_group as u32,
            doodad_func_id,
            msg: message.to_string(),
            judgement_time: 0,
        };
        {
            let mut st = self.st();
            st.reported_evidence.retain(|d| d.strong_count() > 0);
            if st
                .reported_evidence
                .iter()
                .filter_map(Weak::upgrade)
                .any(|d| Arc::ptr_eq(&d, evidence))
            {
                server.ids.crime.release_id(new_id);
                return None;
            }
            if st.events.contains_key(&new_id) {
                drop(st);
                server.ids.crime.release_id(new_id);
                tracing::warn!(
                    "Unable to report crime at {} with message {}",
                    fmt_vec3(new_event.position),
                    new_event.msg
                );
                return None;
            }
            st.events.insert(new_id, new_event.clone());
            st.updated.push(new_id);
            st.reported_evidence.push(Arc::downgrade(evidence));
        }
        // Add crime points to the criminal.
        self.add_crime_points(server, new_event.criminal, crime_type, crime_value)
            .await;
        // Try to add this to ongoing trials.
        self.prefetch_victims(server, &[new_event.victim]).await;
        server
            .systems
            .social
            .trials
            .add_new_evidence(server, &new_event);
        // Progress the doodad to its next phase to finish the report.
        if next_phase > 0 {
            hooks.doodad_change_phase(server, evidence, reporter, next_phase);
        }
        // REPORT_CRIME: one accepted report, credited to the reporter (value1 = the crime kind).
        crate::systems::report_achievement(
            server,
            reporter,
            AchievementInput::Increment {
                kind_id: char_record_kind::REPORT_CRIME,
                amount: 1,
                matcher: RecordMatch::Value1(u32::from(crime_type)),
            },
        );
        Some(new_event)
    }

    /// `AddCrimePoints(criminalId, crimeType, crimePoints)`.
    async fn add_crime_points(&self, server: &GameServer, criminal_id: u32, kind: u8, points: i16) {
        tracing::debug!("Adding {points} crime points to player {criminal_id} for {kind} ({kind})");
        let criminal = server.world.character_by_id(criminal_id);
        let online = criminal.as_ref().is_some_and(|p| p.data().is_online);
        match criminal {
            Some(p) if online => {
                let pkt = {
                    let mut c = p.data();
                    let pkt = add_crime(server, &mut c, points, util::now_ms());
                    c.evidence_reported_count += 1;
                    pkt
                };
                p.send(pkt);
                crate::systems::dispatch_buff_events(server, &WorldObject::from(&p));
                // GET_FACTION: crossing the infamy threshold made the criminal a pirate.
                let faction_id = p.data().unit.faction_id;
                if faction_id == crate::data::world_data::factions::PIRATE {
                    crate::systems::report_achievement(
                        server,
                        &p,
                        AchievementInput::Absolute {
                            kind_id: char_record_kind::GET_FACTION,
                            value: 1,
                            matcher: RecordMatch::Value1(faction_id),
                        },
                    );
                }
            }
            _ => add_offline_crime_points(server, criminal_id, points).await,
        }
    }

    /// `GenerateEvidence(criminal, evidenceDoodadTemplateId, targetLocation, targetRotation,
    /// victimPlayerId, sourceDoodadTemplateId)` through `SocialHooks::create_player_doodad`. A
    /// missing owner character (the C# passes `null` on) creates nothing.
    #[allow(clippy::too_many_arguments)]
    fn generate_evidence(
        &self,
        server: &GameServer,
        criminal: Option<&PlayerRef>,
        template_id: u32,
        pos: (f32, f32, f32),
        rot_z: f32,
        victim_player_id: u32,
        source_template_id: u32,
    ) -> Option<DoodadRef> {
        let Some(criminal) = criminal else {
            tracing::warn!("GenerateEvidence: no owner character for evidence {template_id}");
            return None;
        };
        server.systems.social.hooks.get().create_player_doodad(
            server,
            criminal,
            template_id,
            pos,
            rot_z,
            source_template_id,
            victim_player_id as i32,
        )
    }

    /// `GenerateEvidenceFromDamage(criminal, victim)` (small bloodstain). `criminal_owner` /
    /// `victim_owner` are the units' `GetOwnerCharacter()`.
    pub fn generate_evidence_from_damage(
        &self,
        server: &GameServer,
        criminal: &WorldObject,
        criminal_owner: Option<&PlayerRef>,
        victim_owner: Option<&PlayerRef>,
    ) -> Option<DoodadRef> {
        self.generate_bloodstain(
            server,
            evidence_doodad::SMALL_BLOODSTAIN,
            criminal,
            criminal_owner,
            victim_owner,
        )
    }

    /// `GenerateEvidenceFromKill(criminal, victim)` (large bloodstain).
    pub fn generate_evidence_from_kill(
        &self,
        server: &GameServer,
        criminal: &WorldObject,
        criminal_owner: Option<&PlayerRef>,
        victim_owner: Option<&PlayerRef>,
    ) -> Option<DoodadRef> {
        self.generate_bloodstain(
            server,
            evidence_doodad::LARGE_BLOODSTAIN,
            criminal,
            criminal_owner,
            victim_owner,
        )
    }

    fn generate_bloodstain(
        &self,
        server: &GameServer,
        template_id: u32,
        criminal: &WorldObject,
        criminal_owner: Option<&PlayerRef>,
        victim_owner: Option<&PlayerRef>,
    ) -> Option<DoodadRef> {
        let (pos, rot_z, victim_id) = match victim_owner {
            Some(v) => {
                let pr = world_pos_rot(server, &WorldObject::from(v));
                ((pr.x, pr.y, pr.z), pr.rot_z, v.character_id)
            }
            None => {
                let pr = world_pos_rot(server, criminal);
                ((pr.x, pr.y, pr.z), 0.0, 0)
            }
        };
        self.generate_evidence(
            server,
            criminal_owner,
            template_id,
            pos,
            rot_z,
            victim_id,
            0,
        )
    }

    /// `GenerateEvidenceFromTheft(criminal, stolenDoodad)` (footprints).
    pub fn generate_evidence_from_theft(
        &self,
        server: &GameServer,
        criminal: Option<&PlayerRef>,
        stolen: &DoodadRef,
    ) -> Option<DoodadRef> {
        let (owner_type, owner_id, template_id) = {
            let d = stolen.data();
            (d.owner_type, d.owner_id, d.template_id)
        };
        if owner_type != doodad_owner_type::CHARACTER {
            return None;
        }
        let criminal = criminal?;
        let gender = criminal.data().gender;
        let pr = world_pos_rot(server, &WorldObject::from(stolen));
        self.generate_evidence(
            server,
            Some(criminal),
            if gender == GENDER_FEMALE {
                evidence_doodad::FOOTPRINT_FEMALE
            } else {
                evidence_doodad::FOOTPRINT_MALE
            },
            (pr.x, pr.y, pr.z),
            pr.rot_z,
            owner_id,
            template_id,
        )
    }

    /// `ArchiveEvidence(crimeEvent, time)` (`time` in unix seconds).
    pub fn archive_evidence(&self, id: u32, time: i64) {
        let mut st = self.st();
        if let Some(e) = st.events.get_mut(&id) {
            e.judgement_time = time;
        }
        st.updated.push(id);
    }

    /// `RemoveEvidence(crimeEvent)`.
    pub fn remove_evidence(&self, id: u32) {
        let mut st = self.st();
        if st.events.remove(&id).is_some() {
            st.deleted.push(id);
        }
    }

    /// `ReportBot(bot, reporter, message)`: `true` if the report was accepted.
    pub fn report_bot(
        &self,
        server: &GameServer,
        bot: &PlayerRef,
        reporter: &PlayerRef,
        message: &str,
        now_ms: i64,
    ) -> bool {
        let count = {
            let mut st = self.st();
            let list = st.reported_suspects.entry(bot.character_id).or_default();
            if list.contains(&reporter.character_id) {
                return false;
            }
            list.push(reporter.character_id);
            list.len() as i32
        };
        let bot_name = bot.data().unit.name.clone();
        let (reporter_name, reporter_obj, reporter_level) = {
            let c = reporter.data();
            (c.unit.name.clone(), c.obj_id, c.unit.level)
        };
        let caster = Some((reporter.character_id, reporter_level));
        sus_log(
            server,
            crate::systems::services::sus::CATEGORY_BOT_REPORT,
            bot.character_id,
            &format!(
                "Possible bot {bot_name} ({}) got reported by {reporter_name} ({}), Msg: {message}",
                bot.character_id, reporter.character_id
            ),
        );
        {
            let mut c = bot.data();
            if !c.unit.buffs.check_buff(buff::SUSPECTED_USER) {
                add_buff_from(
                    server,
                    &mut c,
                    buff::SUSPECTED_USER,
                    reporter_obj,
                    caster,
                    0,
                    now_ms,
                );
            }
        }
        let prime = server.config.justice.bot_report_prime_suspect_count;
        if count >= prime {
            let hooks = server.systems.social.hooks.get();
            if !hooks.check_buff_tag(server, bot, buff::TAG_SUSPECTS) {
                let mut c = bot.data();
                add_buff_from(
                    server,
                    &mut c,
                    buff::TRANSFORMING_INTO_PRIME_SUSPECT,
                    reporter_obj,
                    caster,
                    0,
                    now_ms,
                );
            }
            if count == prime {
                sus_log(
                    server,
                    crate::systems::services::sus::CATEGORY_BOT,
                    bot.character_id,
                    &format!(
                        "Possible bot {bot_name} ({}) has reached the report threshold of {prime}",
                        bot.character_id
                    ),
                );
            }
        }
        reporter.data().bot_reported_count += 1;
        bot.data().reported_as_bot_count += 1;
        crate::systems::dispatch_buff_events(server, &WorldObject::from(bot));
        true
    }

    /// `ReportBotExpired(bot)`.
    pub fn report_bot_expired(&self, server: &GameServer, bot: &PlayerRef) -> bool {
        if !self.st().reported_suspects.contains_key(&bot.character_id) {
            return true; // Does not have a report list
        }
        let hooks = server.systems.social.hooks.get();
        if hooks.check_buff_tag(server, bot, buff::TAG_SUSPECTS) {
            hooks.remove_buffs_by_tag(server, bot, BuffKind::Bad, 10, buff::TAG_SUSPECTS);
            if let Some(l) = self.st().reported_suspects.get_mut(&bot.character_id) {
                l.clear();
            }
            // The C# interpolates the Character object itself (its `ToString()`).
            sus_log(

                server,
                crate::systems::services::sus::CATEGORY_BOT,
                bot.character_id,
                &format!(
                    "Possible bot {} ({}) has cleared their Prime Suspect status by talking to a judge.",
                    "AAEmu.Game.Models.Game.Char.Character", bot.character_id
                ),
            );
            return true;
        }
        false
    }
}

/// `Transform.World` of any object.
pub(crate) fn world_pos_rot(
    server: &GameServer,
    obj: &WorldObject,
) -> crate::models::transform::PositionAndRotation {
    match server.world.world_of(obj) {
        Some(w) => w.world_pos_rot(obj),
        None => obj.transform().local(),
    }
}

/// `Vector3.ToString()` (`<x, y, z>`).
pub(crate) fn fmt_vec3(v: (f32, f32, f32)) -> String {
    format!("<{}, {}, {}>", v.0, v.1, v.2)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::data::skills::BuffTemplate;
    use crate::data::GameData;

    fn server() -> GameServer {
        let mut data = GameData::empty();
        for id in [buff::WANTED, 4832] {
            data.skills
                .buffs
                .insert(id, BuffTemplate::simple(id, BuffKind::Bad));
        }
        crate::test_support::test_server_with(data)
    }

    #[tokio::test]
    async fn add_crime_saturates_and_keeps_wanted_threshold() {
        let s = server();
        let mut c = crate::test_support::test_character(1, "Crook", 0.0, 0.0);
        let p = add_crime(&s, &mut c, 30, 0);
        assert_eq!((c.crime_point, c.infamy_point), (30, 30));
        assert_eq!(p.crime_state, 0);
        let p = add_crime(&s, &mut c, 30, 0);
        assert_eq!((c.crime_point, c.infamy_point), (60, 60));
        // Wanted buff from CheckWantedThreshold -> crime state 1.
        assert_eq!(p.crime_state, 1);
        assert_eq!(
            p,
            SCCrimeChangedPacket {
                points: 30,
                crime_points: 60,
                infamy_points: 60,
                crime_state: 1
            }
        );
        // Overflow keeps the criminal wanted.
        c.crime_point = i16::MAX - 1;
        let p = add_crime(&s, &mut c, 10, 0);
        assert_eq!(c.crime_point, i16::MAX);
        assert_eq!(p.crime_points, i16::MAX);
        assert_eq!(p.crime_state, 1);
        // Negative amounts clamp at 0.
        c.crime_point = 5;
        c.infamy_point = 5;
        add_crime(&s, &mut c, -10, 0);
        assert_eq!((c.crime_point, c.infamy_point), (0, 0));
    }

    #[tokio::test]
    #[ignore = "requires private MySQL3317 via AAEMU_TEST_MYSQL; uses only a temporary table"]
    async fn private_mysql_offline_crime_points_saturate() {
        use sqlx::mysql::{MySqlConnectOptions, MySqlPoolOptions};
        use std::str::FromStr;
        let Ok(url) = std::env::var("AAEMU_TEST_MYSQL") else {
            return;
        };
        let options = MySqlConnectOptions::from_str(&url).expect("private MySQL URL");
        assert_eq!(
            options.get_port(),
            3317,
            "only private MySQL3317 is allowed"
        );
        let pool = MySqlPoolOptions::new()
            .max_connections(1)
            .connect_with(options)
            .await
            .unwrap();
        sqlx::query("CREATE TEMPORARY TABLE characters (id INT PRIMARY KEY, crime_point SMALLINT UNSIGNED, crime_record INT UNSIGNED)")
            .execute(&pool).await.unwrap();
        sqlx::query("INSERT INTO characters VALUES (1, 32766, 2147483646), (2, 5, 5)")
            .execute(&pool)
            .await
            .unwrap();
        let mut server = server();
        server.db = pool.clone();
        for (id, points, expected) in [(1, 10, (32767i64, 2147483647i64)), (2, -10, (0, 0))] {
            add_offline_crime_points(&server, id, points).await;
            let saved: (i64, i64) = sqlx::query_as("SELECT CAST(crime_point AS SIGNED), CAST(crime_record AS SIGNED) FROM characters WHERE id = ?")
                .bind(id).fetch_one(&pool).await.unwrap();
            assert_eq!(saved, expected);
        }
        pool.close().await;
    }

    #[test]
    fn jury_point_setter_sends_on_change_only() {
        let mut c = crate::test_support::test_character(1, "J", 0.0, 0.0);
        assert!(set_jury_point(&mut c, 0).is_none());
        assert_eq!(
            set_jury_point(&mut c, 2),
            Some(SCJuryPointChangedPacket { jury_point: 2 })
        );
    }

    #[test]
    fn crimes_of_player_filters_judged() {
        let m = CrimeManager::default();
        for (id, criminal, judged) in [(1, 7, 0), (2, 7, 100), (3, 8, 0)] {
            assert!(m.insert(CrimeEvent {
                id,
                criminal,
                judgement_time: judged,
                ..CrimeEvent::default()
            }));
        }
        let ids = |v: Vec<CrimeEvent>| v.iter().map(|e| e.id).collect::<Vec<_>>();
        assert_eq!(ids(m.crimes_of_player(7, false)), vec![1]);
        assert_eq!(ids(m.crimes_of_player(7, true)), vec![1, 2]);
        m.archive_evidence(1, 50);
        assert_eq!(m.get(1).unwrap().judgement_time, 50);
        m.remove_evidence(3);
        m.remove_evidence(3);
        assert_eq!(m.pending_ids(), (vec![1], vec![3]));
    }
}
