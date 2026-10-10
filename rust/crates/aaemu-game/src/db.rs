//! MySQL access for the lobby flow (runtime `sqlx::query`, no compile-time macros).
//!
//! Queries mirror the C# ones: `AccountManager.GetAccountDetailsInternal` / `UpdateLoginTime`,
//! `GameConnection.LoadAccount`, `Character.Load`, `ItemManager.LoadUserItems` (restricted to one
//! character's equipment), `ExpeditionManager.GetExpedition` and `CharacterManager.LoadCharacters`.
//!
//! Integer columns are selected through `CAST(... AS SIGNED)` because sqlx type-checks MySQL
//! integer widths strictly, while the C# reader converts (`GetInt16` on a MEDIUMINT etc.).

use chrono::{NaiveDateTime, Utc};
use sqlx::mysql::{MySqlPool, MySqlRow};
use sqlx::Row;

use crate::config::AppConfig;
use crate::data::GameData;
use crate::error::{GameError, Result};
use crate::models::character::{Character, EQUIPMENT_CONTAINER_SIZE};
use crate::models::item::{Item, ItemRow, UnixTime};
use crate::models::unit_model::UnitCustomModelParams;
use aaemu_commons::PacketStream;

/// `SlotType.Equipment`.
const SLOT_TYPE_EQUIPMENT: i64 = 1;

/// `DateTime.MinValue` (0001-01-01 00:00:00), the DB default for unset DATETIME columns.
fn min_value() -> NaiveDateTime {
    chrono::NaiveDate::from_ymd_opt(1, 1, 1)
        .expect("valid date")
        .and_hms_opt(0, 0, 0)
        .expect("valid time")
}

/// `t > DateTime.MinValue`.
pub fn is_set(t: Option<NaiveDateTime>) -> bool {
    t.is_some_and(|t| t > min_value())
}

/// `Helpers.UnixTime(DateTime)`: `<= MinValue` or before the epoch -> 0.
pub fn unix(dt: Option<NaiveDateTime>) -> UnixTime {
    dt.map_or(0, |d| d.and_utc().timestamp().max(0))
}

/// Reads a DATETIME column; NULL / zero dates (which sqlx cannot decode) become `None`
/// (`DateTime.MinValue` in C#).
fn dt(row: &MySqlRow, col: &str) -> Option<NaiveDateTime> {
    row.try_get::<Option<NaiveDateTime>, _>(col).ok().flatten()
}

fn int(row: &MySqlRow, col: &str) -> Result<i64> {
    Ok(row.try_get::<i64, _>(col)?)
}

/// `AccountDetails`.
#[derive(Debug, Clone, Default)]
pub struct AccountDetails {
    pub account_id: i32,
    pub access_level: i32,
    pub labor: i16,
    pub credits: i32,
    pub loyalty: i32,
    pub last_updated: Option<NaiveDateTime>,
    pub last_login: Option<NaiveDateTime>,
}

/// `AccountManager.GetAccountDetailsInternal`, including the create-on-first-use insert.
/// Errors are logged and yield default details, as in C#.
pub async fn get_account_details(
    pool: &MySqlPool,
    config: &AppConfig,
    account_id: u32,
) -> AccountDetails {
    match try_get_account_details(pool, config, account_id).await {
        Ok(d) => d,
        Err(e) => {
            tracing::error!("{e}");
            AccountDetails::default()
        }
    }
}

async fn try_get_account_details(
    pool: &MySqlPool,
    config: &AppConfig,
    account_id: u32,
) -> Result<AccountDetails> {
    let row = sqlx::query(
        "SELECT CAST(account_id AS SIGNED) AS account_id, CAST(access_level AS SIGNED) AS access_level, \
         CAST(labor AS SIGNED) AS labor, CAST(credits AS SIGNED) AS credits, CAST(loyalty AS SIGNED) AS loyalty, \
         last_updated, last_login, last_labor_tick, last_credits_tick, last_loyalty_tick \
         FROM accounts WHERE account_id = ?",
    )
    .bind(account_id)
    .fetch_optional(pool)
    .await?;
    if let Some(row) = row {
        return Ok(AccountDetails {
            account_id: int(&row, "account_id")? as i32,
            access_level: int(&row, "access_level")? as i32,
            labor: int(&row, "labor")? as i16,
            credits: int(&row, "credits")? as i32,
            loyalty: int(&row, "loyalty")? as i32,
            last_updated: dt(&row, "last_updated"),
            last_login: dt(&row, "last_login"),
        });
    }

    // Account didn't exist, check if it's our first
    let count: i64 = sqlx::query_scalar("SELECT COUNT(*) FROM accounts")
        .fetch_one(pool)
        .await?;
    let access_level = if count <= 0 {
        config.account.access_level_first_account
    } else {
        0
    };
    let now = Utc::now().naive_utc();
    sqlx::query(
        "INSERT INTO accounts (account_id, access_level, labor, credits, loyalty, last_login, last_labor_tick, last_credits_tick, last_loyalty_tick) \
         VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
    )
    .bind(account_id)
    .bind(access_level)
    .bind(config.labor.default)
    .bind(config.credits.default)
    .bind(config.loyalty.default)
    .bind(now)
    .bind(now)
    .bind(now)
    .bind(now)
    .execute(pool)
    .await?;
    // C# returns `res` with only the timestamps set (AccountId = LastInsertedId, which is 0 for a
    // non auto-increment key); Labor/Loyalty stay 0 for this first call.
    Ok(AccountDetails {
        last_login: Some(now),
        last_updated: Some(now),
        ..AccountDetails::default()
    })
}

/// `AccountManager.UpdateLoginTime`: returns the previous `last_login`.
pub async fn update_login_time(
    pool: &MySqlPool,
    config: &AppConfig,
    account_id: u32,
    new_time: NaiveDateTime,
) -> Option<NaiveDateTime> {
    let res = get_account_details(pool, config, account_id).await;
    if let Err(e) = sqlx::query("UPDATE accounts SET last_login = ? WHERE account_id = ?")
        .bind(new_time)
        .bind(account_id)
        .execute(pool)
        .await
    {
        tracing::error!("{e}");
        return Some(Utc::now().naive_utc());
    }
    res.last_login
}

/// `GameConnection.LoadAccount` (characters only; houses are a later phase).
pub async fn load_account_characters(
    pool: &MySqlPool,
    config: &AppConfig,
    game_data: &GameData,
    account_id: u32,
) -> Result<Vec<Character>> {
    let ids: Vec<i64> = sqlx::query_scalar(
        "SELECT CAST(id AS SIGNED) FROM characters WHERE `account_id` = ? and `deleted`=0",
    )
    .bind(account_id)
    .fetch_all(pool)
    .await?;

    let mut out = Vec::with_capacity(ids.len());
    for id in ids {
        let Some((character, delete_time)) =
            load_character(pool, config, game_data, id as u32, account_id).await?
        else {
            continue; // TODO (C#) ...
        };
        // CharacterManager.CheckForDeletedCharactersDeletion: characters whose delete time has passed
        // are finalised there and left out of the list. Finalisation (rename, asset deletion,
        // SCCharacterDeleted) is a later phase; for now they are only left out.
        if let Some(t) = delete_time.filter(|_| is_set(delete_time)) {
            if t <= Utc::now().naive_utc() {
                tracing::warn!(
                    "CheckForDeletedCharactersDeletion - Account:{} Id:{} Name:{} is due for deletion (finalisation not ported yet); hiding it",
                    character.account_id,
                    character.id,
                    character.name
                );
                continue;
            }
        }
        out.push(character);
    }
    Ok(out)
}

/// `FactionsEnum.Pirate`.
pub const FACTION_PIRATE: u32 = 161;
/// `CrimeManager.PirateCrimePointThreshold`.
pub const PIRATE_CRIME_POINT_THRESHOLD: i32 = 3000;

/// Every `characters` column the C# `Character.Load` reads (`SELECT *` + `reader.GetX(column)`),
/// so a schema missing one of them fails the same way (the packet throws and the connection is
/// shut down). Integer columns that feed the character list are cast to SIGNED; the rest are
/// only checked for existence.
const CHARACTER_COLUMNS: &str = "CAST(id AS SIGNED) AS id, name, access_level, CAST(race AS SIGNED) AS race, \
     CAST(gender AS SIGNED) AS gender, unit_model_params, CAST(level AS SIGNED) AS level, \
     CAST(experience AS SIGNED) AS experience, recoverable_exp, CAST(hp AS SIGNED) AS hp, \
     CAST(mp AS SIGNED) AS mp, CAST(consumed_lp AS SIGNED) AS consumed_lp, CAST(ability1 AS SIGNED) AS ability1, \
     CAST(ability2 AS SIGNED) AS ability2, CAST(ability3 AS SIGNED) AS ability3, world_id, \
     CAST(zone_id AS SIGNED) AS zone_id, x, y, z, yaw, pitch, roll, CAST(faction_id AS SIGNED) AS faction_id, \
     faction_name, CAST(expedition_id AS SIGNED) AS expedition_id, CAST(family AS SIGNED) AS family, \
     CAST(dead_count AS SIGNED) AS dead_count, dead_time, CAST(rez_wait_duration AS SIGNED) AS rez_wait_duration, \
     rez_time, CAST(rez_penalty_duration AS SIGNED) AS rez_penalty_duration, leave_time, \
     CAST(money AS SIGNED) AS money, CAST(money2 AS SIGNED) AS money2, honor_point, vocation_point, \
     CAST(crime_point AS SIGNED) AS crime_point, CAST(crime_record AS SIGNED) AS crime_record, jury_point, \
     hostile_faction_kills, pvp_honor, died_in_pvp, died_in_pvp_war_zone, transfer_request_time, \
     delete_request_time, delete_time, CAST(auto_use_aapoint AS SIGNED) AS auto_use_aapoint, \
     CAST(prev_point AS SIGNED) AS prev_point, CAST(point AS SIGNED) AS point, CAST(gift AS SIGNED) AS gift, \
     num_inv_slot, num_bank_slot, expanded_expert, created_at, updated_at, return_district, online_time, \
     arrest_count, accept_guilty_count, accept_trial_count, not_guilty_count, guilty_count, \
     evidence_reported_count, bot_reported_count, reported_as_bot_count, offline_guilty_time, \
     offline_guilty_region, slots";

/// `Character.Load(connection, characterId, accountId)` restricted to what `Character.Write`
/// serialises. Returns the character and its raw `delete_time`.
pub async fn load_character(
    pool: &MySqlPool,
    config: &AppConfig,
    game_data: &GameData,
    character_id: u32,
    account_id: u32,
) -> Result<Option<(Character, Option<NaiveDateTime>)>> {
    let account = get_account_details(pool, config, account_id).await;
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
    let name: String = row.try_get("name")?;
    let expedition_id = int(&row, "expedition_id")?;
    let delete_time = dt(&row, "delete_time");
    let expedition_id = if expedition_exists(pool, expedition_id).await? {
        expedition_id as u32
    } else {
        0
    };

    // The `CrimePoint` / `InfamyPoint` setters clamp negatives to 0 and run
    // CheckWantedThreshold, which turns a character with InfamyPoint >= 3000 into a pirate
    // (`SetFaction(Pirate)`) while it is being loaded.
    let crime_point = (int(&row, "crime_point")? as i16).max(0);
    let infamy_point = (int(&row, "crime_record")? as i32).max(0);
    let mut faction_id = int(&row, "faction_id")? as u32;
    if infamy_point >= PIRATE_CRIME_POINT_THRESHOLD && faction_id != FACTION_PIRATE {
        // Later phase: SetFaction's side effects (Wanted/Contemptuous buffs,
        // HousingManager.UpdateOwnedHousingFaction). C# also calls
        // ExpeditionManager.Kick(this.Connection, Id) when the character is in a guild, which
        // throws on the null lobby connection (see PORTING.md); that is not reproduced.
        tracing::warn!(
            "Character {id} ({name}) has {infamy_point} infamy: listed with the pirate faction"
        );
        faction_id = FACTION_PIRATE;
    }

    // Character.ValidateAndFixExpAndLevel (can raise the level that is written).
    let stored_level = int(&row, "level")? as u8;
    let experience = int(&row, "experience")? as i32;
    let (level, _experience) = game_data
        .experience
        .validate_and_fix(stored_level, experience)
        .map_err(|e| GameError::Protocol(format!("Character.Load({id}): {e}")))?;

    let character = Character {
        id,
        account_id,
        name,
        race: int(&row, "race")? as u8,
        gender: int(&row, "gender")? as u8,
        level,
        // Later phase: C# clamps Hp/Mp to MaxHp/MaxMp (stat formulas, equipment, buffs) before
        // writing; stored values are sent as-is here.
        hp: int(&row, "hp")? as i32,
        mp: int(&row, "mp")? as i32,
        zone_id: int(&row, "zone_id")? as u32,
        faction_id,
        faction_name: row.try_get("faction_name")?,
        expedition_id,
        family: int(&row, "family")? as u32,
        equipment: load_equipment(pool, game_data, config.id, id).await?,
        ability1: int(&row, "ability1")? as u8,
        ability2: int(&row, "ability2")? as u8,
        ability3: int(&row, "ability3")? as u8,
        x: row.try_get("x")?,
        y: row.try_get("y")?,
        z: row.try_get("z")?,
        model_params,
        labor_power: account.labor,
        labor_power_modified: unix(account.last_updated),
        dead_count: int(&row, "dead_count")? as i16,
        dead_time: unix(dt(&row, "dead_time")),
        rez_wait_duration: int(&row, "rez_wait_duration")? as i32,
        rez_time: unix(dt(&row, "rez_time")),
        rez_penalty_duration: int(&row, "rez_penalty_duration")? as i32,
        leave_time: unix(dt(&row, "leave_time")),
        money: int(&row, "money")?,
        crime_point,
        infamy_point,
        delete_request_time: unix(dt(&row, "delete_request_time")),
        transfer_request_time: unix(dt(&row, "transfer_request_time")),
        delete_time: unix(delete_time),
        consumed_labor_power: int(&row, "consumed_lp")? as i32,
        bm_point: i64::from(get_account_details(pool, config, account_id).await.loyalty),
        money2: int(&row, "money2")?,
        auto_use_aa_point: int(&row, "auto_use_aapoint")? != 0,
        prev_point: int(&row, "prev_point")? as i32,
        point: int(&row, "point")? as i32,
        gift: int(&row, "gift")? as i32,
        updated: unix(dt(&row, "updated_at")),
    };

    // `SELECT * FROM options WHERE owner = @owner` (character UI options; not part of the list).
    sqlx::query("SELECT `key`, `value` FROM `options` WHERE `owner` = ?")
        .bind(character_id)
        .fetch_all(pool)
        .await?;

    Ok(Some((character, delete_time)))
}

/// `ExpeditionManager.GetExpedition(id) != null`.
async fn expedition_exists(pool: &MySqlPool, id: i64) -> Result<bool> {
    if id <= 0 {
        return Ok(false);
    }
    let n: i64 = sqlx::query_scalar("SELECT COUNT(*) FROM expeditions WHERE id = ?")
        .bind(id)
        .fetch_one(pool)
        .await?;
    Ok(n > 0)
}

/// The character's equipment container as `ItemManager.LoadUserItems` would build it:
/// the persistent `EquipmentContainer` row (first match, its `container_size` wins over the
/// default 28) holding items whose `container_id` points at it, plus owned equipment-slot items
/// whose container is missing (C# re-homes those into the same container).
async fn load_equipment(
    pool: &MySqlPool,
    game_data: &GameData,
    world_id: u8,
    character_id: u32,
) -> Result<Vec<Option<Item>>> {
    let container = sqlx::query(
        "SELECT CAST(container_id AS SIGNED) AS container_id, CAST(container_size AS SIGNED) AS container_size \
         FROM item_containers WHERE owner_id = ? AND slot_type = ? AND mate_id = 0 ORDER BY container_id LIMIT 1",
    )
    .bind(character_id)
    .bind(SLOT_TYPE_EQUIPMENT)
    .fetch_optional(pool)
    .await?;
    let (container_id, size) = match container {
        Some(r) => (
            int(&r, "container_id")?,
            int(&r, "container_size")?.max(0) as usize,
        ),
        None => (-1, EQUIPMENT_CONTAINER_SIZE),
    };

    let rows = sqlx::query(
        "SELECT `type`, CAST(id AS UNSIGNED) AS id, CAST(template_id AS SIGNED) AS template_id, \
         CAST(slot AS SIGNED) AS slot, CAST(`count` AS SIGNED) AS item_count, CAST(lifespan_mins AS SIGNED) AS lifespan_mins, \
         CAST(made_unit_id AS SIGNED) AS made_unit_id, unsecure_time, unpack_time, created_at, \
         CAST(flags AS SIGNED) AS flags, CAST(ucc AS SIGNED) AS ucc, details, CAST(grade AS SIGNED) AS grade, charge_time \
         FROM items WHERE container_id = ? \
            OR (`owner` = ? AND slot_type = ? AND container_id NOT IN (SELECT container_id FROM item_containers))",
    )
    .bind(container_id)
    .bind(character_id)
    .bind(SLOT_TYPE_EQUIPMENT)
    .fetch_all(pool)
    .await?;

    let mut slots: Vec<Option<Item>> = vec![None; size];
    for r in rows {
        let template_id = int(&r, "template_id")? as u32;
        let row = ItemRow {
            type_name: r.try_get("type")?,
            id: r.try_get::<u64, _>("id")?,
            template_id,
            slot: int(&r, "slot")? as i32,
            count: int(&r, "item_count")? as i32,
            lifespan_mins: int(&r, "lifespan_mins")? as i32,
            made_unit_id: int(&r, "made_unit_id")? as u32,
            unsecure_time: unix(dt(&r, "unsecure_time")),
            unpack_time: unix(dt(&r, "unpack_time")),
            created_at: unix(dt(&r, "created_at")),
            flags: int(&r, "flags")? as u8,
            ucc: int(&r, "ucc")? as u32,
            details: r
                .try_get::<Option<Vec<u8>>, _>("details")?
                .unwrap_or_default(),
            grade: r.try_get::<Option<i64>, _>("grade")?.unwrap_or(0) as u8,
            charge_time: unix(dt(&r, "charge_time")),
        };
        let item = Item::from_row(row, game_data.item_template(template_id), world_id);
        match usize::try_from(item.slot).ok().filter(|&s| s < size) {
            Some(s) if slots[s].is_none() => slots[s] = Some(item),
            // Later phase: C# AddOrMoveExistingItem may relocate conflicting items.
            _ => tracing::warn!(
                "Equipment item {} of character {} has an invalid/duplicate slot {}",
                item.id,
                character_id,
                item.slot
            ),
        }
    }
    Ok(slots)
}

/// `LoginCharacterInfo` (AAEmu.Commons.Models).
#[derive(Debug, Clone, PartialEq)]
pub struct LoginCharacterInfo {
    pub id: u32,
    pub account_id: u32,
    pub name: String,
    pub race: u8,
    pub gender: u8,
}

/// `CharacterManager.LoadCharacters(accountId)` (for `LGRequestInfoPacket`).
pub async fn load_login_characters(
    pool: &MySqlPool,
    account_id: u32,
) -> Result<Vec<LoginCharacterInfo>> {
    let rows = sqlx::query(
        "SELECT CAST(`id` AS SIGNED) AS id, `name`, CAST(`race` AS SIGNED) AS race, CAST(`gender` AS SIGNED) AS gender, `delete_time` \
         FROM characters WHERE `account_id`=? and `deleted`=0",
    )
    .bind(account_id)
    .fetch_all(pool)
    .await?;
    let now = Utc::now().naive_utc();
    let mut out = Vec::new();
    for r in rows {
        // Skip this char in the list if it's ready to be deleted
        let delete_time = dt(&r, "delete_time");
        if is_set(delete_time) && delete_time.is_some_and(|t| t < now) {
            continue;
        }
        out.push(LoginCharacterInfo {
            id: int(&r, "id")? as u32,
            account_id,
            name: r.try_get("name")?,
            race: int(&r, "race")? as u8,
            gender: int(&r, "gender")? as u8,
        });
    }
    Ok(out)
}
