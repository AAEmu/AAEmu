//! MySQL access for the lobby flow (runtime `sqlx::query`, no compile-time macros).
//!
//! Queries mirror the C# ones: `AccountManager.GetAccountDetailsInternal` / `UpdateLoginTime` and
//! `CharacterManager.LoadCharacters`; character persistence lives in [`crate::db_character`].
//!
//! Integer columns are selected through `CAST(... AS SIGNED)` because sqlx type-checks MySQL
//! integer widths strictly, while the C# reader converts (`GetInt16` on a MEDIUMINT etc.).

use chrono::{NaiveDateTime, Utc};
use sqlx::mysql::{MySqlPool, MySqlRow};
use sqlx::Row;

use crate::config::AppConfig;
use crate::error::Result;
use crate::models::item::UnixTime;

/// `DateTime.MinValue` (0001-01-01 00:00:00), the DB default for unset DATETIME columns.
pub fn min_value() -> NaiveDateTime {
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

/// Unix seconds back to a DATETIME for writes (`0` = `DateTime.MinValue`).
pub fn from_unix(t: UnixTime) -> NaiveDateTime {
    if t <= 0 {
        return min_value();
    }
    chrono::DateTime::from_timestamp(t, 0).map_or_else(min_value, |d| d.naive_utc())
}

/// Reads a DATETIME column; NULL / zero dates (which sqlx cannot decode) become `None`
/// (`DateTime.MinValue` in C#).
pub fn dt(row: &MySqlRow, col: &str) -> Option<NaiveDateTime> {
    row.try_get::<Option<NaiveDateTime>, _>(col).ok().flatten()
}

pub fn int(row: &MySqlRow, col: &str) -> Result<i64> {
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
    pub last_labor_tick: Option<NaiveDateTime>,
    pub last_credits_tick: Option<NaiveDateTime>,
    pub last_loyalty_tick: Option<NaiveDateTime>,
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
            last_labor_tick: dt(&row, "last_labor_tick"),
            last_credits_tick: dt(&row, "last_credits_tick"),
            last_loyalty_tick: dt(&row, "last_loyalty_tick"),
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
        last_labor_tick: Some(now),
        last_credits_tick: Some(now),
        last_loyalty_tick: Some(now),
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

/// `AccountManager.AddCredits` / `AddLoyalty`: `INSERT ... ON DUPLICATE KEY UPDATE col = col +
/// amount`; `true` when a row was affected (errors are logged, as in C#).
pub async fn add_account_points(
    pool: &MySqlPool,
    column: AccountPointColumn,
    account_id: u32,
    amount: i32,
) -> bool {
    let col = match column {
        AccountPointColumn::Credits => "credits",
        AccountPointColumn::Loyalty => "loyalty",
    };
    let sql = format!(
        "INSERT INTO accounts (account_id, {col}) VALUES(?, ?) ON DUPLICATE KEY UPDATE {col} = {col} + ?"
    );
    match sqlx::query(&sql)
        .bind(account_id)
        .bind(amount)
        .bind(amount)
        .execute(pool)
        .await
    {
        Ok(r) => r.rows_affected() > 0,
        Err(e) => {
            tracing::error!("{e}");
            false
        }
    }
}

/// The account point columns of [`add_account_points`].
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AccountPointColumn {
    Credits,
    Loyalty,
}

/// `AccountManager.UpdateLabor`.
pub async fn update_labor(pool: &MySqlPool, account_id: u32, labor: i16) {
    if let Err(e) = sqlx::query("UPDATE accounts SET labor = ? WHERE account_id = ?")
        .bind(labor)
        .bind(account_id)
        .execute(pool)
        .await
    {
        tracing::error!("{e}");
    }
}

/// `AccountManager.UpdateTickTimes`: the column of the last set flag (labor, credits, loyalty
/// in that order of precedence); no flag writes the column `error` (fails and logs, as C#).
pub async fn update_tick_times(
    pool: &MySqlPool,
    account_id: u32,
    new_time: NaiveDateTime,
    update_labor: bool,
    update_credits: bool,
    update_loyalty: bool,
) {
    let mut field = "error";
    if update_labor {
        field = "last_labor_tick";
    }
    if update_credits {
        field = "last_credits_tick";
    }
    if update_loyalty {
        field = "last_loyalty_tick";
    }
    let sql = format!("UPDATE accounts SET {field} = ? WHERE account_id = ?");
    if let Err(e) = sqlx::query(&sql)
        .bind(new_time)
        .bind(account_id)
        .execute(pool)
        .await
    {
        tracing::error!("{e}");
    }
}

/// `AccountManager.UpdateDivineClock`.
pub async fn update_divine_clock(
    pool: &MySqlPool,
    account_id: u32,
    time_elapsed: u32,
    times_taken: u32,
) {
    if let Err(e) = sqlx::query(
        "UPDATE accounts SET divine_clock_time = ? , divine_clock_taken = ? WHERE account_id = ?",
    )
    .bind(time_elapsed)
    .bind(times_taken)
    .bind(account_id)
    .execute(pool)
    .await
    {
        tracing::error!("{e}");
    }
}

/// `AccountManager.GetDivineClock`: `(divine_clock_time, divine_clock_taken)`, `(0, 0)` when the
/// account does not exist or the query fails.
pub async fn get_divine_clock(pool: &MySqlPool, account_id: u32) -> (u32, u32) {
    let row = sqlx::query(
        "SELECT account_id, CAST(divine_clock_time AS SIGNED) AS t, CAST(divine_clock_taken AS SIGNED) AS n          FROM accounts WHERE account_id = ?",
    )
    .bind(account_id)
    .fetch_optional(pool)
    .await;
    match row {
        Ok(Some(r)) => (
            int(&r, "t").unwrap_or(0) as u32,
            int(&r, "n").unwrap_or(0) as u32,
        ),
        Ok(None) => (0, 0),
        Err(e) => {
            tracing::error!("{e}");
            (0, 0)
        }
    }
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
