//! Character persistence: `GameConnection.LoadAccount`, `Character.Load(connection, id, account)`,
//! `Character.Load()` (the select-time sub loads), `Character.Save` /
//! `SaveDirectlyToDatabase`, the deletion queries of `CharacterManager`, and the friend /
//! blocked info lookups.
//!
//! Integer columns are cast to SIGNED / UNSIGNED because sqlx checks MySQL integer widths
//! strictly, while the C# reader converts.

use chrono::{NaiveDateTime, Utc};
use sqlx::mysql::{MySqlPool, MySqlRow};
use sqlx::{MySql, Row, Transaction};

use aaemu_commons::PacketStream;

use crate::db::{dt, from_unix, get_account_details, int, is_set, unix};
use crate::error::{GameError, Result};
use crate::models::buff::{Buff, SkillCaster};
use crate::models::char_data::*;
use crate::models::character::{Character, Inventory, Transform, DEFAULT_INSTANCE_ID};
use crate::models::unit_model::UnitCustomModelParams;
use crate::server::GameServer;

/// `CrimeManager.PirateCrimePointThreshold`.
pub const PIRATE_CRIME_POINT_THRESHOLD: i32 = 3000;
/// `CrimeManager.WantedCrimePointThreshold`.
pub const WANTED_CRIME_POINT_THRESHOLD: i16 = 50;
/// `BuffConstants.Wanted`.
pub const BUFF_WANTED: u32 = 3710;
/// `BuffConstants.Contemptuous`.
pub const BUFF_CONTEMPTUOUS: u32 = 4832;
/// `FactionsEnum.Pirate`.
pub const FACTION_PIRATE: u32 = 161;

const CHARACTER_COLUMNS: &str = "CAST(id AS SIGNED) AS id, CAST(account_id AS SIGNED) AS account_id, name, \
     CAST(access_level AS SIGNED) AS access_level, CAST(race AS SIGNED) AS race, \
     CAST(gender AS SIGNED) AS gender, unit_model_params, CAST(level AS SIGNED) AS level, \
     CAST(experience AS SIGNED) AS experience, CAST(recoverable_exp AS SIGNED) AS recoverable_exp, CAST(hp AS SIGNED) AS hp, \
     CAST(mp AS SIGNED) AS mp, CAST(consumed_lp AS SIGNED) AS consumed_lp, CAST(ability1 AS SIGNED) AS ability1, \
     CAST(ability2 AS SIGNED) AS ability2, CAST(ability3 AS SIGNED) AS ability3, CAST(world_id AS SIGNED) AS world_id, \
     CAST(zone_id AS SIGNED) AS zone_id, x, y, z, yaw, pitch, roll, CAST(faction_id AS SIGNED) AS faction_id, \
     faction_name, CAST(expedition_id AS SIGNED) AS expedition_id, CAST(family AS SIGNED) AS family, \
     CAST(dead_count AS SIGNED) AS dead_count, dead_time, CAST(rez_wait_duration AS SIGNED) AS rez_wait_duration, \
     rez_time, CAST(rez_penalty_duration AS SIGNED) AS rez_penalty_duration, leave_time, \
     CAST(money AS SIGNED) AS money, CAST(money2 AS SIGNED) AS money2, CAST(honor_point AS SIGNED) AS honor_point, \
     CAST(vocation_point AS SIGNED) AS vocation_point, \
     CAST(crime_point AS SIGNED) AS crime_point, CAST(crime_record AS SIGNED) AS crime_record, \
     CAST(jury_point AS SIGNED) AS jury_point, CAST(hostile_faction_kills AS SIGNED) AS hostile_faction_kills, \
     CAST(pvp_honor AS SIGNED) AS pvp_honor, CAST(died_in_pvp AS SIGNED) AS died_in_pvp, \
     CAST(died_in_pvp_war_zone AS SIGNED) AS died_in_pvp_war_zone, transfer_request_time, \
     delete_request_time, delete_time, CAST(auto_use_aapoint AS SIGNED) AS auto_use_aapoint, \
     CAST(prev_point AS SIGNED) AS prev_point, CAST(point AS SIGNED) AS point, CAST(gift AS SIGNED) AS gift, \
     CAST(num_inv_slot AS SIGNED) AS num_inv_slot, CAST(num_bank_slot AS SIGNED) AS num_bank_slot, \
     CAST(expanded_expert AS SIGNED) AS expanded_expert, created_at, updated_at, \
     CAST(return_district AS SIGNED) AS return_district, CAST(online_time AS SIGNED) AS online_time, \
     CAST(arrest_count AS SIGNED) AS arrest_count, CAST(accept_guilty_count AS SIGNED) AS accept_guilty_count, \
     CAST(accept_trial_count AS SIGNED) AS accept_trial_count, CAST(not_guilty_count AS SIGNED) AS not_guilty_count, \
     CAST(guilty_count AS SIGNED) AS guilty_count, CAST(evidence_reported_count AS SIGNED) AS evidence_reported_count, \
     CAST(bot_reported_count AS SIGNED) AS bot_reported_count, CAST(reported_as_bot_count AS SIGNED) AS reported_as_bot_count, \
     CAST(offline_guilty_time AS SIGNED) AS offline_guilty_time, CAST(offline_guilty_region AS SIGNED) AS offline_guilty_region, \
     slots";

fn opt_int(r: &MySqlRow, col: &str) -> Result<i64> {
    Ok(r.try_get::<Option<i64>, _>(col)?.unwrap_or(0))
}

impl Character {
    /// `CheckWantedThreshold` (the buff / faction part; `SetFaction` side effects on houses,
    /// guild and party are not ported — see PORTING.md).
    pub fn check_wanted_threshold(&mut self, server: &GameServer, now_ms: i64) {
        let add = |c: &mut Character, id: u32| {
            if c.buffs.check_buff(id) {
                return;
            }
            match server.game_data.skills.buff(id) {
                Some(t) => {
                    let caster = SkillCaster {
                        kind: 0,
                        obj_id: c.obj_id,
                    };
                    let b = Buff::new(t.clone(), caster, Some((c.id, c.level)), now_ms);
                    c.buffs.add_buff(b, 0, 0, now_ms);
                }
                None => tracing::error!("CheckWantedThreshold: no buff template {id}"),
            }
        };
        if self.infamy_point >= PIRATE_CRIME_POINT_THRESHOLD {
            add(self, BUFF_WANTED);
            add(self, BUFF_CONTEMPTUOUS);
            if self.faction_id != FACTION_PIRATE {
                tracing::warn!(
                    "Character {} ({}) has {} infamy: SetFaction(Pirate)",
                    self.id,
                    self.name,
                    self.infamy_point
                );
                self.faction_id = FACTION_PIRATE;
            }
        } else {
            if self.crime_point >= WANTED_CRIME_POINT_THRESHOLD {
                add(self, BUFF_WANTED);
            } else {
                self.buffs.remove_buff(BUFF_WANTED);
            }
            self.buffs.remove_buff(BUFF_CONTEMPTUOUS);
        }
    }
}

/// `Character.Load(connection, characterId, accountId)`. Returns the character and its raw
/// `delete_time`.
pub async fn load_character(
    server: &GameServer,
    character_id: u32,
    account_id: u32,
) -> Result<Option<(Character, Option<NaiveDateTime>)>> {
    let pool = &server.db;
    let account = get_account_details(pool, &server.config, account_id).await;
    let row = sqlx::query(&format!(
        "SELECT {CHARACTER_COLUMNS} FROM characters WHERE `id` = ? AND `account_id` = ? and `deleted`=0"
    ))
    .bind(character_id)
    .bind(account_id)
    .fetch_optional(pool)
    .await?;
    let Some(row) = row else { return Ok(None) };

    let blob: Vec<u8> = row.try_get("unit_model_params")?;
    let model_params: UnitCustomModelParams = PacketStream::from_bytes(blob).read()?;
    let id = int(&row, "id")? as u32;
    let mut c = Character::new(id, account_id, model_params);
    c.name = row.try_get("name")?;
    c.access_level = int(&row, "access_level")? as i32;
    c.race = int(&row, "race")? as u8;
    c.gender = int(&row, "gender")? as u8;
    c.level = int(&row, "level")? as u8;
    c.experience = int(&row, "experience")? as i32;
    c.recoverable_exp = int(&row, "recoverable_exp")? as i32;
    c.hp = int(&row, "hp")? as i32;
    c.mp = int(&row, "mp")? as i32;
    c.saved_hp = c.hp;
    c.saved_mp = c.mp;
    c.labor_power = account.labor;
    c.labor_power_modified = unix(account.last_updated);
    c.consumed_labor_power = int(&row, "consumed_lp")? as i32;
    c.ability1 = int(&row, "ability1")? as u8;
    c.ability2 = int(&row, "ability2")? as u8;
    c.ability3 = int(&row, "ability3")? as u8;
    c.server_id = int(&row, "world_id")? as u32;
    // new Transform(character, null, zone_id, DefaultInstanceId, x, y, z, yaw, pitch, roll):
    // the constructor's (roll, pitch, yaw) parameters receive the yaw / pitch / roll columns.
    c.transform = Transform {
        instance_id: DEFAULT_INSTANCE_ID,
        world_id: server.game_data.worlds.default_world_template_id(),
        zone_id: int(&row, "zone_id")? as u32,
        x: row.try_get("x")?,
        y: row.try_get("y")?,
        z: row.try_get("z")?,
        rot_x: row.try_get("yaw")?,
        rot_y: row.try_get("pitch")?,
        rot_z: row.try_get("roll")?,
    };
    // Later phase: FactionManager.GetFaction returns null for unknown ids (Write would throw).
    c.faction_id = int(&row, "faction_id")? as u32;
    c.faction_name = row.try_get("faction_name")?;
    let expedition_id = int(&row, "expedition_id")? as u32;
    c.expedition_id = if server.expeditions.exists(expedition_id) {
        expedition_id
    } else {
        0
    };
    c.family = int(&row, "family")? as u32;
    c.dead_count = int(&row, "dead_count")? as i16;
    c.dead_time = unix(dt(&row, "dead_time"));
    c.rez_wait_duration = int(&row, "rez_wait_duration")? as i32;
    c.rez_time = unix(dt(&row, "rez_time"));
    c.rez_penalty_duration = int(&row, "rez_penalty_duration")? as i32;
    c.leave_time = unix(dt(&row, "leave_time"));
    c.money = int(&row, "money")?;
    c.money2 = int(&row, "money2")?;
    c.honor_point = int(&row, "honor_point")? as i32;
    c.vocation_point = int(&row, "vocation_point")? as i32;
    let now_ms = Utc::now().timestamp_millis();
    // CrimePoint / InfamyPoint setters: clamp to >= 0 and CheckWantedThreshold (only on change).
    let crime = (int(&row, "crime_point")? as i16).max(0);
    if crime != 0 {
        c.crime_point = crime;
        c.check_wanted_threshold(server, now_ms);
    }
    let infamy = (int(&row, "crime_record")? as i32).max(0);
    if infamy != 0 {
        c.infamy_point = infamy;
        c.check_wanted_threshold(server, now_ms);
    }
    c.jury_point = int(&row, "jury_point")? as i32;
    c.hostile_faction_kills = int(&row, "hostile_faction_kills")? as u32;
    c.honor_gained_in_combat = int(&row, "pvp_honor")? as u32;
    c.died_in_pvp = int(&row, "died_in_pvp")? != 0;
    c.died_in_pvp_war_zone = int(&row, "died_in_pvp_war_zone")? != 0;
    c.transfer_request_time = unix(dt(&row, "transfer_request_time"));
    let delete_time = dt(&row, "delete_time");
    c.delete_request_time = unix(dt(&row, "delete_request_time"));
    c.delete_time = unix(delete_time);
    c.auto_use_aa_point = int(&row, "auto_use_aapoint")? != 0;
    c.prev_point = int(&row, "prev_point")? as i32;
    c.point = int(&row, "point")? as i32;
    c.gift = int(&row, "gift")? as i32;
    c.num_inventory_slots = int(&row, "num_inv_slot")? as u8;
    c.num_bank_slots = int(&row, "num_bank_slot")? as i16;
    c.expanded_expert = int(&row, "expanded_expert")? as u8;
    c.created = unix(dt(&row, "created_at"));
    c.updated = unix(dt(&row, "updated_at"));
    c.return_district_id = int(&row, "return_district")? as u32;
    c.online_time = int(&row, "online_time")? as u32;
    c.arrest_count = opt_int(&row, "arrest_count")? as i32;
    c.accept_guilty_count = opt_int(&row, "accept_guilty_count")? as i32;
    c.accept_trial_count = opt_int(&row, "accept_trial_count")? as i32;
    c.not_guilty_count = opt_int(&row, "not_guilty_count")? as i32;
    c.guilty_count = opt_int(&row, "guilty_count")? as i32;
    c.evidence_reported_count = opt_int(&row, "evidence_reported_count")? as i32;
    c.bot_reported_count = opt_int(&row, "bot_reported_count")? as i32;
    c.reported_as_bot_count = opt_int(&row, "reported_as_bot_count")? as i32;
    c.offline_guilty_time = opt_int(&row, "offline_guilty_time")? as i32;
    c.offline_guilty_region = opt_int(&row, "offline_guilty_region")? as i32;

    c.inventory = Inventory::new(
        id,
        c.num_inventory_slots,
        c.num_bank_slots,
        &server.items,
        &server.ids.container,
    );
    let slots: Vec<u8> = row.try_get("slots")?;
    read_action_slots(&mut c.slots, &slots);

    c.bm_point = i64::from(
        get_account_details(pool, &server.config, account_id)
            .await
            .loyalty,
    );

    c.clamp_hp_mp(&server.game_data.formulas);
    // ValidateAndFixExpAndLevel.
    let (level, experience) = server
        .game_data
        .experience
        .validate_and_fix(c.level, c.experience)
        .map_err(|e| GameError::Protocol(format!("Character.Load({id}): {e}")))?;
    c.level = level;
    c.experience = experience;
    // PostUpdateCurrentHp(character, 0, Hp, Unknown): no HP triggers on characters.

    let rows = sqlx::query("SELECT `key`, `value` FROM `options` WHERE `owner` = ?")
        .bind(character_id)
        .fetch_all(pool)
        .await?;
    for r in rows {
        let key: String = r.try_get("key")?;
        let value: String = r.try_get("value")?;
        let key = key
            .trim()
            .parse::<u16>()
            .map_err(|e| GameError::Protocol(format!("options key {key:?}: {e}")))?;
        c.options.insert(key, value);
    }
    Ok(Some((c, delete_time)))
}

/// `GameConnection.LoadAccount` (characters; houses are a later phase). Characters whose
/// deletion time has passed are finalised (`CheckForDeletedCharactersDeletion`) and left out;
/// the packets it sends to the account's connection are returned.
pub async fn load_account_characters(
    server: &GameServer,
    account_id: u32,
) -> Result<(
    Vec<Character>,
    Vec<Box<dyn crate::net::packet::ServerPacket>>,
)> {
    let ids: Vec<i64> = sqlx::query_scalar(
        "SELECT CAST(id AS SIGNED) FROM characters WHERE `account_id` = ? and `deleted`=0",
    )
    .bind(account_id)
    .fetch_all(&server.db)
    .await?;

    let mut out = Vec::with_capacity(ids.len());
    let mut packets = Vec::new();
    for id in ids {
        let Some((character, _)) = load_character(server, id as u32, account_id).await? else {
            continue; // TODO (C#) ...
        };
        if !check_for_deleted_characters_deletion(server, &character, Some(&mut packets)).await? {
            out.push(character);
        }
    }
    Ok((out, packets))
}

/// `CharacterManager.CheckForDeletedCharactersDeletion`. `packets` receives what C# sends to the
/// game connection (when there is one).
pub async fn check_for_deleted_characters_deletion(
    server: &GameServer,
    character: &Character,
    packets: Option<&mut Vec<Box<dyn crate::net::packet::ServerPacket>>>,
) -> Result<bool> {
    let now = Utc::now().timestamp();
    if character.delete_time > 0 && character.delete_time <= now {
        tracing::info!(
            "CheckForDeletedCharactersDeletion - Deleting Account:{} Id:{} Name:{}",
            character.account_id,
            character.id,
            character.name
        );
        let mut deleted_name = character.name.clone();
        if server.config.account.delete_release_name {
            deleted_name = format!("!{}", character.name);
            server.names.remove_character_id(character.id);
            server
                .names
                .add_character(character.id, &deleted_name, character.account_id);
        }
        let res = sqlx::query(
            "UPDATE `characters` SET `deleted`='1', `delete_time`=?, `name`=? WHERE `id`=? and `account_id`=?",
        )
        .bind(crate::db::min_value())
        .bind(&deleted_name)
        .bind(character.id)
        .bind(character.account_id)
        .execute(&server.db)
        .await?
        .rows_affected();
        if res > 0 {
            // DeleteCharacterAssets(character, false): houses, guild, family and mail returns are
            // later phases.
            if let Some(p) = packets {
                p.push(Box::new(crate::packets::world::SCCharacterDeletedPacket {
                    character_id: character.id,
                    name: character.name.clone(),
                }));
                p.push(Box::new(
                    crate::packets::world::SCDeleteCharacterResponsePacket {
                        character_id: character.id,
                        status: 1,
                        delete_request_time: character.delete_request_time,
                        delete_time: character.delete_time,
                    },
                ));
            }
        }
        return Ok(res > 0);
    } else if character.delete_request_time > 0 {
        tracing::warn!(
            "CheckForDeletedCharactersDeletion - Delete request for Account:{} Id:{} Name:{}, but character is no longer marked for deletion (possibly cancelled delete)",
            character.account_id,
            character.id,
            character.name
        );
    }
    Ok(false)
}

/// `CharacterManager.CheckForDeletedCharacters`: finalises due deletions and returns the next
/// pending deletion time (unix seconds) to schedule `CharacterDeleteTask` for.
pub async fn check_for_deleted_characters(server: &GameServer) -> Result<Option<i64>> {
    let rows = sqlx::query(
        "SELECT CAST(`id` AS SIGNED) AS id, `name`, CAST(`account_id` AS SIGNED) AS account_id, `delete_time` FROM characters WHERE `deleted`=0",
    )
    .fetch_all(&server.db)
    .await?;
    let now = Utc::now().naive_utc();
    let mut next: Option<NaiveDateTime> = None;
    let mut due = Vec::new();
    for r in rows {
        let t = dt(&r, "delete_time");
        if is_set(t) {
            let t = t.expect("set");
            if t <= now {
                due.push((int(&r, "id")? as u32, int(&r, "account_id")? as u32));
            } else if next.is_none_or(|n| t < n) {
                next = Some(t);
            }
        }
    }
    for (char_id, account_id) in due {
        match load_character(server, char_id, account_id).await? {
            Some((c, _)) => {
                let conn = server.connections.get_by_account(account_id);
                let mut packets = Vec::new();
                if check_for_deleted_characters_deletion(server, &c, Some(&mut packets)).await? {
                    if let Some(conn) = conn {
                        for p in &packets {
                            conn.send_dyn(p.as_ref());
                        }
                    }
                    tracing::info!("CheckForDeletedCharacters - Delete charId:{char_id}");
                } else {
                    tracing::error!(
                        "CheckForDeletedCharacters - Failed to delete character for deletion charId:{char_id}"
                    );
                }
            }
            None => tracing::error!(
                "CheckForDeletedCharacters - Failed to load character for deletion charId:{char_id}"
            ),
        }
    }
    Ok(next.map(|n| n.and_utc().timestamp()))
}

/// `Character.Load()`: the per-character tables read when the character is selected.
pub async fn load_character_details(server: &GameServer, c: &mut Character) -> Result<()> {
    let pool = &server.db;
    let data = &server.game_data;
    let id = c.id;
    let now_ms = Utc::now().timestamp_millis();
    if let Some(t) = data.characters.template(c.race, c.gender) {
        c.model_id = t.model_id;
    } else {
        return Err(GameError::Protocol(format!(
            "no character template for race {} gender {}",
            c.race, c.gender
        )));
    }
    c.slots = vec![ActionSlot::default(); MAX_ACTION_SLOTS];

    // Abilities
    c.abilities = CharacterAbilities::default();
    let actives = [c.ability1, c.ability2, c.ability3];
    for r in sqlx::query("SELECT CAST(id AS SIGNED) AS id, CAST(exp AS SIGNED) AS exp FROM abilities WHERE `owner` = ?")
        .bind(id)
        .fetch_all(pool)
        .await?
    {
        c.abilities
            .load_row(int(&r, "id")? as u8, int(&r, "exp")? as i32, actives);
    }

    // Actability
    c.actability = CharacterActability::default();
    for r in sqlx::query("SELECT CAST(id AS SIGNED) AS id, CAST(point AS SIGNED) AS point, CAST(step AS SIGNED) AS step FROM actabilities WHERE `owner` = ?")
        .bind(id)
        .fetch_all(pool)
        .await?
    {
        let aid = int(&r, "id")? as u32;
        if data.characters.actability(aid).is_none() {
            // `_actabilities[id]` throws KeyNotFoundException.
            return Err(GameError::Protocol(format!("unknown actability {aid}")));
        }
        c.actability.actabilities.push(Actability {
            id: aid,
            point: int(&r, "point")? as i32,
            step: int(&r, "step")? as u8,
        });
    }

    // Skills (each stored skill is re-learned through AddSkill(skillId), which re-checks the
    // ability tree and the skill points and recomputes the level).
    c.skills = CharacterSkills::default();
    for r in sqlx::query("SELECT CAST(id AS SIGNED) AS id, CAST(level AS SIGNED) AS level, CAST(`type` AS CHAR) AS type FROM skills WHERE `owner` = ?")
        .bind(id)
        .fetch_all(pool)
        .await?
    {
        let ty: String = r.try_get("type")?;
        let sid = int(&r, "id")? as u32;
        if ty.eq_ignore_ascii_case("skill") || ty == "1" {
            learn_skill(server, c, sid);
        } else if ty.eq_ignore_ascii_case("buff") || ty == "2" {
            c.skills.passive_buffs.push(sid);
            apply_passive_buff(server, c, sid, now_ms);
        } else {
            return Err(GameError::Protocol(format!("unknown skill type {ty:?}")));
        }
    }

    // Appellations
    c.appellations = CharacterAppellations::default();
    for r in sqlx::query("SELECT CAST(id AS SIGNED) AS id, CAST(active AS SIGNED) AS active FROM appellations WHERE `owner` = ?")
        .bind(id)
        .fetch_all(pool)
        .await?
    {
        let aid = int(&r, "id")? as u32;
        c.appellations.appellations.push(aid);
        if int(&r, "active")? != 0 {
            c.appellations.active = aid;
            // addBuff(titleId)
            let buff_id = data.characters.appellations.get(&aid).copied().unwrap_or(0);
            if buff_id != 0 {
                add_buff_by_id(server, c, buff_id, now_ms);
            }
            tracing::info!("title: {aid} giving buff {buff_id}");
        }
    }

    // Portals
    c.portals = CharacterPortals::default();
    for r in sqlx::query("SELECT CAST(id AS SIGNED) AS id, name, CAST(x AS DOUBLE) AS x, CAST(y AS DOUBLE) AS y, CAST(z AS DOUBLE) AS z, \
         CAST(zone_id AS SIGNED) AS zone_id, CAST(z_rot AS DOUBLE) AS z_rot, CAST(sub_zone_id AS SIGNED) AS sub_zone_id, CAST(owner AS SIGNED) AS owner \
         FROM portal_book_coords WHERE `owner` = ?")
        .bind(id)
        .fetch_all(pool)
        .await?
    {
        let f = |c: &str| -> Result<f32> { Ok(r.try_get::<Option<f64>, _>(c)?.unwrap_or(0.0) as f32) };
        c.portals.private_portals.push(crate::data::world_data::Portal {
            id: int(&r, "id")? as u32,
            name: r.try_get("name")?,
            x: f("x")?,
            y: f("y")?,
            z: f("z")?,
            zone_id: opt_int(&r, "zone_id")? as u32,
            z_rot: f("z_rot")?,
            sub_zone_id: opt_int(&r, "sub_zone_id")? as u32,
            owner: int(&r, "owner")? as u32,
            ..Default::default()
        });
    }
    for r in sqlx::query("SELECT CAST(id AS SIGNED) AS id, CAST(subzone AS SIGNED) AS subzone, CAST(owner AS SIGNED) AS owner FROM portal_visited_district WHERE `owner` = ?")
        .bind(id)
        .fetch_all(pool)
        .await?
    {
        let v = VisitedDistrict {
            id: int(&r, "id")? as u32,
            sub_zone: int(&r, "subzone")? as u32,
            owner: int(&r, "owner")? as u32,
        };
        if c.portals.visited_districts.iter().any(|x| x.sub_zone == v.sub_zone) {
            return Err(GameError::Protocol(format!("duplicate visited district {}", v.sub_zone)));
        }
        c.portals.visited_districts.push(v);
    }
    c.portals.populate_district_portals(&data.portals);

    // Friends / blocked
    c.friends.clear();
    for r in sqlx::query("SELECT CAST(id AS SIGNED) AS id, CAST(friend_id AS SIGNED) AS friend_id, CAST(owner AS SIGNED) AS owner FROM friends WHERE `owner` = ?")
        .bind(id)
        .fetch_all(pool)
        .await?
    {
        let f = FriendEntry {
            id: int(&r, "id")? as u32,
            friend_id: int(&r, "friend_id")? as u32,
            owner: int(&r, "owner")? as u32,
        };
        if c.friends.iter().any(|x| x.friend_id == f.friend_id) {
            return Err(GameError::Protocol(format!("duplicate friend {}", f.friend_id)));
        }
        c.friends.push(f);
    }
    c.blocked.clear();
    for r in sqlx::query("SELECT CAST(owner AS SIGNED) AS owner, CAST(blocked_id AS SIGNED) AS blocked_id FROM blocked WHERE `owner` = ?")
        .bind(id)
        .fetch_all(pool)
        .await?
    {
        c.blocked
            .push((int(&r, "owner")? as u32, int(&r, "blocked_id")? as u32));
    }

    // Quests (rows only: templates / evaluation / daily resets are a later phase)
    c.quests = CharacterQuests::default();
    for r in
        sqlx::query("SELECT CAST(id AS SIGNED) AS id, data FROM completed_quests WHERE `owner` = ?")
            .bind(id)
            .fetch_all(pool)
            .await?
    {
        c.quests.completed.push(CompletedQuest {
            id: int(&r, "id")? as u16,
            body: r.try_get("data")?,
        });
    }
    for r in sqlx::query("SELECT CAST(id AS SIGNED) AS id, CAST(template_id AS SIGNED) AS template_id, data, CAST(status AS SIGNED) AS status FROM quests WHERE `owner` = ?")
        .bind(id)
        .fetch_all(pool)
        .await?
    {
        let mut q = ActiveQuest {
            id: int(&r, "id")? as u32,
            template_id: int(&r, "template_id")? as u32,
            status: int(&r, "status")? as u8,
            objectives: [0; 5],
            step: 0,
            acceptor_type: 0,
            component_id: 0,
            acceptor_id: 0,
            time: 0,
        };
        let data: Vec<u8> = r.try_get("data")?;
        if let Err(e) = q.read_data(&data) {
            tracing::error!("Quest {} data of {}: {e}", q.template_id, c.name);
        }
        c.quests.active.push(q);
    }

    // LoadActionSlots(connection)
    if let Ok(Some(r)) =
        sqlx::query("SELECT slots FROM `characters` WHERE `id` = ? AND `account_id` = ?")
            .bind(id)
            .bind(c.account_id)
            .fetch_optional(pool)
            .await
    {
        let blob: Vec<u8> = r.try_get("slots")?;
        read_action_slots(&mut c.slots, &blob);
    }

    // Mails: MailManager.GetCurrentMailList(Id) runs while the character is not registered in
    // the WorldManager yet, so the unread counters stay 0 (as in C#).
    c.unread_mail = CountUnreadMail::default();
    Ok(())
}

/// `CharacterSkills.AddSkill(uint skillId)` without the packet (no connection at load time).
pub fn learn_skill(server: &GameServer, c: &mut Character, skill_id: u32) {
    let data = &server.game_data.skills;
    let Some(t) = data.skill(skill_id) else {
        tracing::error!("AddSkill: unknown skill {skill_id}");
        return;
    };
    if t.ability_id > 0
        && t.ability_id != c.ability1
        && t.ability_id != c.ability2
        && t.ability_id != c.ability3
    {
        return;
    }
    let mut points = server.game_data.experience.skill_points_for_level(c.level);
    points -= c.skills.used_skill_points(data, 0);
    if t.skill_points > points {
        return;
    }
    if c.skills.skills.iter().any(|s| s.id == skill_id) {
        return; // C# re-sends SCSkillLearnedPacket (no connection here)
    }
    let ab_level = ab_level(server, c, t.ability_id);
    c.skills.add_skill_template(data, skill_id, ab_level);
}

/// `GetAbLevel(type)`: `General` = `Level`, otherwise the level for the ability's exp.
pub fn ab_level(server: &GameServer, c: &Character, ability_id: u8) -> i32 {
    if ability_id == 0 {
        return i32::from(c.level);
    }
    let exp = c
        .abilities
        .abilities
        .iter()
        .find(|a| a.id == ability_id)
        .map_or(0, |a| a.exp);
    i32::from(
        server
            .game_data
            .experience
            .level_from_exp_any(exp, false)
            .map_or(0, |(l, _)| l),
    )
}

/// `PassiveBuff.Apply(owner)`.
pub fn apply_passive_buff(server: &GameServer, c: &mut Character, passive_id: u32, now_ms: i64) {
    let Some(pt) = server.game_data.skills.passive_buff(passive_id) else {
        tracing::error!("PassiveBuff {passive_id}: no template");
        return;
    };
    let Some(t) = server.game_data.skills.buff(pt.buff_id) else {
        tracing::error!("PassiveBuff {passive_id}: no buff template {}", pt.buff_id);
        return;
    };
    let mut b = Buff::new(
        t.clone(),
        SkillCaster::default(),
        Some((c.id, c.level)),
        now_ms,
    );
    b.passive = true;
    c.buffs.add_buff(b, 0, 0, now_ms);
}

/// `Buffs.AddBuff(uint buffId, BaseUnit caster)` with the character as caster.
pub fn add_buff_by_id(server: &GameServer, c: &mut Character, buff_id: u32, now_ms: i64) {
    let Some(t) = server.game_data.skills.buff(buff_id) else {
        tracing::error!("AddBuff: no buff template {buff_id}");
        return;
    };
    let caster = SkillCaster {
        kind: 0,
        obj_id: c.obj_id,
    };
    let b = Buff::new(t.clone(), caster, Some((c.id, c.level)), now_ms);
    c.buffs.add_buff(b, 0, 0, now_ms);
}

/// A `character_active_buffs` row.
#[derive(Debug, Clone, PartialEq)]
pub struct SavedBuffRow {
    pub buff_id: u32,
    pub ab_level: u32,
    pub time_left: i32,
    pub charge: i32,
    pub real_time: bool,
    /// `saved_at` (unix ms).
    pub saved_at_ms: i64,
}

/// The query half of `Buffs.LoadActiveBuffs` (errors are logged, as in C#).
pub async fn fetch_active_buffs(server: &GameServer, character_id: u32) -> Vec<SavedBuffRow> {
    let rows = match sqlx::query(
        "SELECT CAST(buff_id AS SIGNED) AS buff_id, CAST(ab_level AS SIGNED) AS ab_level, \
         CAST(time_left AS SIGNED) AS time_left, CAST(charge AS SIGNED) AS charge, \
         CAST(real_time AS SIGNED) AS real_time, saved_at FROM `character_active_buffs` WHERE `character_id` = ?",
    )
    .bind(character_id)
    .fetch_all(&server.db)
    .await
    {
        Ok(r) => r,
        Err(e) => {
            tracing::error!("Failed to load active buffs for character {character_id}: {e}");
            return Vec::new();
        }
    };
    rows.iter()
        .filter_map(|r| {
            Some(SavedBuffRow {
                buff_id: int(r, "buff_id").ok()? as u32,
                ab_level: int(r, "ab_level").ok()? as u32,
                time_left: int(r, "time_left").ok()? as i32,
                charge: int(r, "charge").ok()? as i32,
                real_time: int(r, "real_time").ok()? != 0,
                saved_at_ms: dt(r, "saved_at").map_or(0, |d| d.and_utc().timestamp_millis()),
            })
        })
        .collect()
}

/// The apply half of `Buffs.LoadActiveBuffs`.
pub fn apply_active_buffs(
    server: &GameServer,
    c: &mut Character,
    rows: &[SavedBuffRow],
    now_ms: i64,
) {
    let mut restored = 0;
    for row in rows {
        let Some(t) = server.game_data.skills.buff(row.buff_id) else {
            tracing::warn!(
                "LoadActiveBuffs: BuffTemplate {} not found, skipping",
                row.buff_id
            );
            continue;
        };
        let remaining = if row.real_time {
            let r = row.time_left - (now_ms - row.saved_at_ms) as i32;
            if r <= 0 {
                tracing::debug!(
                    "LoadActiveBuffs: RealTime buff {} expired offline",
                    row.buff_id
                );
                continue;
            }
            r
        } else {
            row.time_left
        };
        if remaining <= 0 {
            continue;
        }
        if c.buffs.check_buff(row.buff_id) {
            tracing::debug!(
                "LoadActiveBuffs: Buff {} already active, skipping",
                row.buff_id
            );
            continue;
        }
        let caster = SkillCaster {
            kind: 0,
            obj_id: c.obj_id,
        };
        let mut b = Buff::new(t.clone(), caster, Some((c.id, c.level)), now_ms);
        b.ab_level = row.ab_level;
        b.duration = remaining;
        b.charge = if row.charge > 0 {
            row.charge
        } else {
            t.init_min_charge
        };
        c.buffs.add_buff(b, 0, remaining, now_ms);
        restored += 1;
    }
    if restored > 0 {
        tracing::info!("Restored {restored} active buff(s) for character {}", c.id);
    }
}

/// `Character.Save(connection, transaction)`. `now` becomes `Updated`. Errors are returned so
/// `SaveDirectlyToDatabase` can roll back.
pub async fn save_character(
    tx: &mut Transaction<'_, MySql>,
    c: &mut Character,
    now: i64,
) -> Result<()> {
    let mut model = PacketStream::new();
    aaemu_commons::Marshal::write(&c.model_params, &mut model);
    c.updated = now;
    // Column order roll,pitch,yaw receives @yaw,@pitch,@roll: `roll` stores Rotation.Z and
    // `yaw` stores Rotation.X; Load reads them back crosswise, so the round trip is consistent.
    // Note (C#): @reported_as_bot_count is bound but not in the column list, so the column is
    // reset to its default on every save.
    sqlx::query(
        "REPLACE INTO `characters` \
        (`id`,`account_id`,`name`,`access_level`,`race`,`gender`,`unit_model_params`,`level`,`experience`,`recoverable_exp`,\
        `hp`,`mp`,`consumed_lp`,`ability1`,`ability2`,`ability3`,\
        `world_id`,`zone_id`,`x`,`y`,`z`,`roll`,`pitch`,`yaw`,\
        `faction_id`,`faction_name`,`expedition_id`,`family`,`dead_count`,`dead_time`,`rez_wait_duration`,`rez_time`,`rez_penalty_duration`,`leave_time`,\
        `money`,`money2`,`honor_point`,`vocation_point`,`crime_point`,`crime_record`,`jury_point`,\
        `hostile_faction_kills`,`pvp_honor`,`died_in_pvp`,`died_in_pvp_war_zone`,\
        `delete_request_time`,`transfer_request_time`,`delete_time`,`auto_use_aapoint`,`prev_point`,`point`,`gift`,\
        `num_inv_slot`,`num_bank_slot`,`expanded_expert`,`slots`,`created_at`,`updated_at`,`return_district`,`online_time`,\
        `arrest_count`, `accept_guilty_count`, `accept_trial_count`, `not_guilty_count`, `guilty_count`, `evidence_reported_count`, `bot_reported_count`,\
        `offline_guilty_time`,`offline_guilty_region`) VALUES \
        (?,?,?,?,?,?,?,?,?,?, ?,?,?,?,?,?, ?,?,?,?,?,?,?,?, ?,?,?,?,?,?,?,?,?,?, ?,?,?,?,?,?,?, ?,?,?,?, ?,?,?,?,?,?,?, ?,?,?,?,?,?,?,?, ?,?,?,?,?,?,?, ?,?)",
    )
    .bind(c.id)
    .bind(c.account_id)
    .bind(&c.name)
    .bind(c.access_level)
    .bind(c.race)
    .bind(c.gender)
    .bind(model.into_bytes())
    .bind(c.level)
    .bind(c.experience)
    .bind(c.recoverable_exp)
    .bind(c.hp)
    .bind(c.mp)
    .bind(c.consumed_labor_power)
    .bind(c.ability1)
    .bind(c.ability2)
    .bind(c.ability3)
    .bind(c.server_id)
    .bind(c.transform.zone_id)
    .bind(c.transform.x)
    .bind(c.transform.y)
    .bind(c.transform.z)
    .bind(c.transform.rot_z) // `roll` <- @yaw
    .bind(c.transform.rot_y)
    .bind(c.transform.rot_x) // `yaw` <- @roll
    .bind(c.faction_id)
    .bind(&c.faction_name)
    .bind(c.expedition_id)
    .bind(c.family)
    .bind(c.dead_count)
    .bind(from_unix(c.dead_time))
    .bind(c.rez_wait_duration)
    .bind(from_unix(c.rez_time))
    .bind(c.rez_penalty_duration)
    .bind(from_unix(c.leave_time))
    .bind(c.money)
    .bind(c.money2)
    .bind(c.honor_point)
    .bind(c.vocation_point)
    .bind(c.crime_point)
    .bind(c.infamy_point)
    .bind(c.jury_point)
    .bind(c.hostile_faction_kills)
    .bind(c.honor_gained_in_combat)
    .bind(c.died_in_pvp)
    .bind(c.died_in_pvp_war_zone)
    .bind(from_unix(c.delete_request_time))
    .bind(from_unix(c.transfer_request_time))
    .bind(from_unix(c.delete_time))
    .bind(c.auto_use_aa_point)
    .bind(c.prev_point)
    .bind(c.point)
    .bind(c.gift)
    .bind(c.num_inventory_slots)
    .bind(c.num_bank_slots)
    .bind(c.expanded_expert)
    .bind(c.action_slots_blob())
    .bind(from_unix(c.created))
    .bind(from_unix(c.updated))
    .bind(c.return_district_id)
    .bind(f64::from(c.online_time))
    .bind(c.arrest_count)
    .bind(c.accept_guilty_count)
    .bind(c.accept_trial_count)
    .bind(c.not_guilty_count)
    .bind(c.guilty_count)
    .bind(c.evidence_reported_count)
    .bind(c.bot_reported_count)
    .bind(c.offline_guilty_time)
    .bind(c.offline_guilty_region)
    .execute(&mut **tx)
    .await?;

    for (k, v) in &c.options {
        sqlx::query("REPLACE INTO `options` (`key`,`value`,`owner`) VALUES (?,?,?)")
            .bind(k)
            .bind(v)
            .bind(c.id)
            .execute(&mut **tx)
            .await?;
    }
    for a in &c.abilities.abilities {
        sqlx::query("REPLACE INTO abilities(`id`,`exp`,`owner`) VALUES (?, ?, ?)")
            .bind(a.id)
            .bind(a.exp)
            .bind(c.id)
            .execute(&mut **tx)
            .await?;
    }
    for a in &c.actability.actabilities {
        sqlx::query("REPLACE INTO actabilities(`id`,`point`,`step`,`owner`) VALUES (?, ?, ?, ?)")
            .bind(a.id as u8) // C# binds (byte)actability.Id
            .bind(a.point)
            .bind(a.step)
            .bind(c.id)
            .execute(&mut **tx)
            .await?;
    }
    for &a in &c.appellations.appellations {
        sqlx::query("REPLACE INTO appellations(`id`,`active`,`owner`) VALUES (?, ?, ?)")
            .bind(a)
            .bind(c.appellations.active == a)
            .bind(c.id)
            .execute(&mut **tx)
            .await?;
    }
    save_active_buffs(tx, c).await;
    save_portals(tx, c).await?;
    // Friends
    if !c.removed_friends.is_empty() {
        let list = join(&c.removed_friends);
        sqlx::query(&format!(
            "DELETE FROM friends WHERE owner = ? AND friend_id IN({list})"
        ))
        .bind(c.id)
        .execute(&mut **tx)
        .await?;
        c.removed_friends.clear();
    }
    for f in &c.friends {
        sqlx::query("REPLACE INTO friends(`id`,`friend_id`,`owner`) VALUES (?, ?, ?)")
            .bind(f.id)
            .bind(f.friend_id)
            .bind(f.owner)
            .execute(&mut **tx)
            .await?;
    }
    if !c.removed_blocked.is_empty() {
        let list = join(&c.removed_blocked);
        sqlx::query(&format!(
            "DELETE FROM blocked WHERE owner = ? AND blocked_id IN({list})"
        ))
        .bind(c.id)
        .execute(&mut **tx)
        .await?;
        c.removed_blocked.clear();
    }
    for (owner, blocked) in &c.blocked {
        sqlx::query("REPLACE INTO blocked(`owner`,`blocked_id`) VALUES (?, ?)")
            .bind(owner)
            .bind(blocked)
            .execute(&mut **tx)
            .await?;
    }
    // Skills
    if !c.skills.removed.is_empty() {
        let list = join(&c.skills.removed);
        sqlx::query(&format!(
            "DELETE FROM skills WHERE owner = ? AND id IN({list})"
        ))
        .bind(c.id)
        .execute(&mut **tx)
        .await?;
        c.skills.removed.clear();
    }
    for s in &c.skills.skills {
        sqlx::query("REPLACE INTO skills(`id`,`level`,`type`,`owner`) VALUES (?, ?, ?, ?)")
            .bind(s.id)
            .bind(s.level)
            .bind(1u8) // SkillType.Skill (enum index)
            .bind(c.id)
            .execute(&mut **tx)
            .await?;
    }
    for b in &c.skills.passive_buffs {
        sqlx::query("REPLACE INTO skills(`id`,`level`,`type`,`owner`) VALUES (?, ?, ?, ?)")
            .bind(b)
            .bind(1)
            .bind(2u8) // SkillType.Buff
            .bind(c.id)
            .execute(&mut **tx)
            .await?;
    }
    // Quests
    if !c.quests.removed.is_empty() {
        let list = join(&c.quests.removed);
        sqlx::query(&format!(
            "DELETE FROM quests WHERE owner = ? AND template_id IN({list})"
        ))
        .bind(c.id)
        .execute(&mut **tx)
        .await?;
        c.quests.removed.clear();
    }
    for q in &c.quests.completed {
        sqlx::query("REPLACE INTO completed_quests(`id`,`data`,`owner`) VALUES(?,?,?)")
            .bind(q.id)
            .bind(q.body8().to_vec())
            .bind(c.id)
            .execute(&mut **tx)
            .await?;
    }
    for q in &c.quests.active {
        sqlx::query(
            "REPLACE INTO quests(`id`,`template_id`,`data`,`status`,`owner`) VALUES(?,?,?,?,?)",
        )
        .bind(q.id)
        .bind(q.template_id)
        .bind(q.write_data())
        .bind(q.status)
        .bind(c.id)
        .execute(&mut **tx)
        .await?;
    }
    // Later phase: Mates.Save.
    Ok(())
}

fn join(ids: &[u32]) -> String {
    ids.iter().map(u32::to_string).collect::<Vec<_>>().join(",")
}

async fn save_portals(tx: &mut Transaction<'_, MySql>, c: &Character) -> Result<()> {
    for p in &c.portals.private_portals {
        sqlx::query("REPLACE INTO portal_book_coords(`id`,`name`,`x`,`y`,`z`,`zone_id`,`z_rot`,`sub_zone_id`,`owner`) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)")
            .bind(p.id)
            .bind(&p.name)
            .bind(p.x)
            .bind(p.y)
            .bind(p.z)
            .bind(p.zone_id)
            .bind(p.z_rot)
            .bind(p.sub_zone_id)
            .bind(p.owner)
            .execute(&mut **tx)
            .await?;
    }
    for v in &c.portals.visited_districts {
        sqlx::query(
            "REPLACE INTO portal_visited_district(`id`,`subzone`,`owner`) VALUES (?, ?, ?)",
        )
        .bind(v.id)
        .bind(v.sub_zone)
        .bind(v.owner)
        .execute(&mut **tx)
        .await?;
    }
    Ok(())
}

/// `Buffs.SaveActiveBuffs` (errors are logged, as in C#).
async fn save_active_buffs(tx: &mut Transaction<'_, MySql>, c: &Character) {
    let res: Result<usize> = async {
        sqlx::query("DELETE FROM `character_active_buffs` WHERE `character_id` = ?")
            .bind(c.id)
            .execute(&mut **tx)
            .await?;
        let now = Utc::now();
        let now_ms = now.timestamp_millis();
        let mut saved = 0;
        for b in &c.buffs.effects {
            if !b.should_persist(now_ms) {
                continue;
            }
            let time_left = b.time_left(now_ms) as i32;
            if time_left <= 0 {
                continue;
            }
            sqlx::query(
                "INSERT INTO `character_active_buffs` (`character_id`, `buff_id`, `caster_id`, `skill_id`, `ab_level`, \
                 `duration`, `time_left`, `charge`, `stack_count`, `real_time`, `saved_at`) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?) \
                 ON DUPLICATE KEY UPDATE `caster_id`=VALUES(`caster_id`), `skill_id`=VALUES(`skill_id`), `ab_level`=VALUES(`ab_level`), \
                 `duration`=VALUES(`duration`), `time_left`=VALUES(`time_left`), `charge`=VALUES(`charge`), \
                 `stack_count`=VALUES(`stack_count`), `real_time`=VALUES(`real_time`), `saved_at`=VALUES(`saved_at`)",
            )
            .bind(c.id)
            .bind(b.template.id)
            .bind(b.caster.map_or(c.id, |x| x.0))
            .bind(0u32)
            .bind(b.ab_level)
            .bind(b.duration)
            .bind(time_left)
            .bind(b.charge)
            .bind(1)
            .bind(u8::from(b.template.real_time))
            .bind(now.naive_utc())
            .execute(&mut **tx)
            .await?;
            saved += 1;
        }
        Ok(saved)
    }
    .await;
    match res {
        Ok(n) if n > 0 => tracing::info!("Saved {n} active buff(s) for character {}", c.id),
        Ok(_) => {}
        Err(e) => tracing::error!("Failed to save active buffs for character {}: {e}", c.id),
    }
}

/// `Character.SaveDirectlyToDatabase`.
pub async fn save_directly_to_database(pool: &MySqlPool, c: &mut Character) -> bool {
    let res: Result<()> = async {
        let mut tx = pool.begin().await?;
        let now = Utc::now().timestamp();
        match save_character(&mut tx, c, now).await {
            Ok(()) => {
                tx.commit().await?;
                Ok(())
            }
            Err(e) => {
                // Character.Save logs and returns false; the transaction is still committed by
                // SaveDirectlyToDatabase (only exceptions roll back).
                tracing::error!("{e}");
                tx.commit().await?;
                Err(e)
            }
        }
    }
    .await;
    match res {
        Ok(()) => true,
        Err(e) => {
            tracing::error!("Character save failed for {} - {}: {e}", c.id, c.name);
            false
        }
    }
}

/// `CharacterManager.IsCharacterPendingDeletion(name)`.
pub async fn is_character_pending_deletion(pool: &MySqlPool, name: &str) -> Result<bool> {
    let row = sqlx::query(
        "SELECT CAST(deleted AS SIGNED) AS deleted, delete_request_time FROM characters WHERE `name` = ?",
    )
    .bind(name)
    .fetch_optional(pool)
    .await?;
    Ok(match row {
        Some(r) => int(&r, "deleted")? != 0 || is_set(dt(&r, "delete_request_time")),
        None => false,
    })
}

/// The `UPDATE characters SET delete_request_time, delete_time` of `SetDeleteCharacter` /
/// `SetRestoreCharacter`. Returns the affected rows.
pub async fn set_delete_times(pool: &MySqlPool, id: u32, request: i64, delete: i64) -> Result<u64> {
    Ok(sqlx::query(
        "UPDATE characters SET `delete_request_time` = ?, `delete_time` = ? WHERE `id` = ?",
    )
    .bind(from_unix(request))
    .bind(from_unix(delete))
    .bind(id)
    .execute(pool)
    .await?
    .rows_affected())
}

/// `FriendMananger.GetFriendInfo(ids)` for the offline part (online friends are formatted from
/// the world by the caller).
pub async fn offline_friend_infos(pool: &MySqlPool, ids: &[u32]) -> Result<Vec<FriendInfo>> {
    if ids.is_empty() {
        return Ok(Vec::new());
    }
    let rows = sqlx::query(&format!(
        "SELECT CAST(id AS SIGNED) AS id, name, CAST(zone_id AS SIGNED) AS zone_id, x, y, z, CAST(race AS SIGNED) AS race, \
         CAST(level AS SIGNED) AS level, leave_time, CAST(hp AS SIGNED) AS hp, CAST(ability1 AS SIGNED) AS ability1, \
         CAST(ability2 AS SIGNED) AS ability2, CAST(ability3 AS SIGNED) AS ability3 FROM characters WHERE id IN({})",
        join(ids)
    ))
    .fetch_all(pool)
    .await?;
    let mut out = Vec::new();
    for r in rows {
        out.push(FriendInfo {
            character_id: int(&r, "id")? as u32,
            name: r.try_get("name")?,
            race: int(&r, "race")? as u8,
            level: int(&r, "level")? as u8,
            health: int(&r, "hp")? as i32,
            abilities: [
                int(&r, "ability1")? as u8,
                int(&r, "ability2")? as u8,
                int(&r, "ability3")? as u8,
            ],
            x: r.try_get("x")?,
            y: r.try_get("y")?,
            z: r.try_get("z")?,
            zone_id: int(&r, "zone_id")? as u32,
            in_party: false,
            is_online: false,
            last_world_leave_time: unix(dt(&r, "leave_time")),
        });
    }
    Ok(out)
}

/// `CharacterBlocked.GetBlockedInfo` (offline part).
pub async fn offline_blocked_infos(pool: &MySqlPool, ids: &[u32]) -> Result<Vec<BlockedInfo>> {
    if ids.is_empty() {
        return Ok(Vec::new());
    }
    let rows = sqlx::query(&format!(
        "SELECT CAST(id AS SIGNED) AS id, name FROM characters WHERE id IN({})",
        join(ids)
    ))
    .fetch_all(pool)
    .await?;
    let mut out = Vec::new();
    for r in rows {
        out.push(BlockedInfo {
            character_id: int(&r, "id")? as u32,
            name: r.try_get("name")?,
        });
    }
    Ok(out)
}
