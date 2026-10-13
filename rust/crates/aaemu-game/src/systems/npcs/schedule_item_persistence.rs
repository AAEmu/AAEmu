//! `account_schedule_items`: the persistent half of the account-wide schedule-item progress and
//! the prepared-save protocol the financial snapshot uses.
//!
//! C# scope (`AAEmu.Game/Core/Managers/ScheduleItemManager.cs` at `7851f67cc`): `Load` and
//! `Save` around `_progress`. The state transitions live in [`super::schedule_item_runtime`];
//! this module owns the SQL and the snapshot types the save handshake moves around.
//!
//! # Identity
//!
//! A row is one login account and one `schedule_items.id`, so every character of an account reads
//! and advances the same counters. `account_id` is a login account (shared with `aaemu_login`),
//! never a character: the table deliberately has no foreign key, neither to `characters` (the row
//! is not owned by a character) nor to `accounts` (the game database does not own the login
//! accounts).
//!
//! # Columns
//!
//! * `gave`: the `u8` receipt counter (`ScheduleItemManager.Progress.Gave`).
//! * `cumulated`: accrued online seconds toward the current term; signed.
//! * `updated`: UTC Unix seconds of the last accrual, receipt or reset; signed, and also the
//!   row's UTC day key (an unusable `<= 0` value means "unknown day" to the runtime).
//!
//! Session clocks and per-character baselines are transient and are never stored.
//!
//! # The prepared save
//!
//! `ScheduleItemManager::prepare_save` snapshots each dirty account's rows together with the mark
//! stamps they were taken at; [`write_rows`] writes exactly that snapshot, one
//! `INSERT ... ON DUPLICATE KEY UPDATE` per row, so another character of the same account saving
//! concurrently only touches the rows of its own snapshot (a full-account rewrite could drop
//! them). Every statement error propagates: the caller rolls the outer transaction back, keeps
//! the snapshot unacknowledged and the rows stay dirty for the next save. No manager state is
//! read here, so no live read happens while the SQL is in flight.

use std::collections::BTreeMap;

use sqlx::mysql::MySqlPool;
use sqlx::{MySql, Transaction};

use crate::db::int;
use crate::error::Result;

use super::schedule_item_runtime::{AccountScheduleProgress, AccountScheduleState};

/// One account's share of a prepared save: the rows that were dirty when the snapshot was taken,
/// together with the state and mark stamp each was taken at.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct PreparedAccountSave {
    /// The login account all rows belong to.
    pub account_id: u32,
    /// `(schedule_id, state)` of every dirty row, in schedule id order.
    pub rows: Vec<(i32, AccountScheduleState)>,
    /// `(schedule_id, stamp)` of the same rows (the runtime's dirty marks,
    /// [`AccountScheduleProgress::dirty_rows`]).
    pub marks: Vec<(i32, u64)>,
}

/// An owned snapshot of every dirty account row: the only thing a save writes.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct PreparedScheduleSave {
    /// The accounts with at least one dirty row, in account id order.
    pub accounts: Vec<PreparedAccountSave>,
}

impl PreparedScheduleSave {
    /// Whether the snapshot has nothing to write.
    pub fn is_empty(&self) -> bool {
        self.accounts.is_empty()
    }

    /// How many rows the snapshot holds.
    pub fn row_count(&self) -> usize {
        self.accounts.iter().map(|account| account.rows.len()).sum()
    }
}

/// Reads every saved row (`ScheduleItemManager.Load`), grouped per account.
///
/// The whole table is loaded once at startup; there is no per-login query, so a character's first
/// tick can never race a load and no account is silently reset. A `SELECT` error propagates: the
/// caller must fail the startup instead of serving empty progress.
pub async fn load_accounts(pool: &MySqlPool) -> Result<BTreeMap<u32, AccountScheduleProgress>> {
    let rows = sqlx::query(
        "SELECT CAST(`account_id` AS SIGNED) AS account_id, CAST(`schedule_id` AS SIGNED) AS schedule_id, \
         CAST(`gave` AS SIGNED) AS gave, `cumulated`, `updated` \
         FROM `account_schedule_items` ORDER BY `account_id`, `schedule_id`",
    )
    .fetch_all(pool)
    .await?;

    let mut by_account: BTreeMap<u32, Vec<(i32, AccountScheduleState)>> = BTreeMap::new();
    for row in &rows {
        by_account
            .entry(int(row, "account_id")? as u32)
            .or_default()
            .push((
                int(row, "schedule_id")? as i32,
                AccountScheduleState {
                    gave: int(row, "gave")? as u8,
                    cumulated: int(row, "cumulated")?,
                    updated: int(row, "updated")?,
                },
            ));
    }

    let accounts: BTreeMap<u32, AccountScheduleProgress> = by_account
        .into_iter()
        .map(|(account_id, rows)| {
            (
                account_id,
                AccountScheduleProgress::from_saved(account_id, rows),
            )
        })
        .collect();
    tracing::info!(
        "Loaded schedule item progress: {} accounts, {} rows",
        accounts.len(),
        rows.len()
    );
    Ok(accounts)
}

/// Writes a prepared snapshot inside the caller's transaction and returns the number of rows
/// written. Each row is an `INSERT ... ON DUPLICATE KEY UPDATE`, so a concurrent save of another
/// character of the same account can only touch the rows in this snapshot. Any statement error
/// propagates: the caller rolls the outer transaction back and keeps the snapshot unacknowledged,
/// so every row stays dirty for a retry.
pub async fn write_rows(
    tx: &mut Transaction<'_, MySql>,
    batch: &PreparedScheduleSave,
) -> Result<usize> {
    let mut written = 0;
    for account in &batch.accounts {
        for (schedule_id, state) in &account.rows {
            sqlx::query(
                "INSERT INTO `account_schedule_items` (`account_id`, `schedule_id`, `gave`, `cumulated`, `updated`) \
                 VALUES (?, ?, ?, ?, ?) \
                 ON DUPLICATE KEY UPDATE `gave` = ?, `cumulated` = ?, `updated` = ?",
            )
            .bind(account.account_id)
            .bind(*schedule_id)
            .bind(state.gave)
            .bind(state.cumulated)
            .bind(state.updated)
            .bind(state.gave)
            .bind(state.cumulated)
            .bind(state.updated)
            .execute(&mut **tx)
            .await?;
            written += 1;
        }
    }
    Ok(written)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::systems::npcs::schedule_item_manager::ScheduleItemManager;
    use crate::systems::npcs::schedule_items::{
        ScheduleEligibility, ScheduleItemData, ScheduleItemDef, ScheduleItemKind,
    };
    use sqlx::mysql::{MySqlConnectOptions, MySqlPoolOptions};
    use std::str::FromStr;

    fn ts(s: &str) -> i64 {
        chrono::NaiveDateTime::parse_from_str(s, "%Y-%m-%d %H:%M:%S")
            .unwrap()
            .and_utc()
            .timestamp()
    }

    /// A live kind-1 row with a real grant and no authored bounds (always on air).
    fn live(id: i32) -> ScheduleItemDef {
        ScheduleItemDef {
            id,
            kind: ScheduleItemKind::Every,
            give_term: 30,
            give_max: 3,
            item_id: 1000 + u32::try_from(id).unwrap(),
            item_count: 2,
            active_take: true,
            on_air: true,
            ..ScheduleItemDef::default()
        }
    }

    /// The two saved rows of account 7 with `cumulated` seconds each.
    fn saved_rows(cumulated: i64, updated: i64) -> Vec<(i32, AccountScheduleState)> {
        vec![
            (
                1,
                AccountScheduleState {
                    gave: 0,
                    cumulated,
                    updated,
                },
            ),
            (
                4,
                AccountScheduleState {
                    gave: 0,
                    cumulated,
                    updated,
                },
            ),
        ]
    }

    /// A manager whose account 7 accrued `seconds` on row 1 (dirty) since `start`.
    fn dirty_manager(start: i64, seconds: i64) -> ScheduleItemManager {
        let data = ScheduleItemData::from_rows(vec![live(1)]);
        let none = ScheduleEligibility::default();
        let mut account = AccountScheduleProgress::new(7);
        account.join(1, start);
        account.tick(start + seconds, &data, &none);
        ScheduleItemManager::from_accounts(BTreeMap::from([(7, account)]))
    }

    /// Connects to the private MySQL3317 schema and shadows `account_schedule_items` with a
    /// connection-local temporary copy of the shipped schema, so the statements run against the
    /// migrated shape without touching any live row. `None` when `AAEMU_TEST_MYSQL` is not set.
    async fn private_pool() -> Option<MySqlPool> {
        let Ok(url) = std::env::var("AAEMU_TEST_MYSQL") else {
            return None;
        };
        let options = MySqlConnectOptions::from_str(&url).expect("valid private MySQL URL");
        assert_eq!(
            options.get_port(),
            3317,
            "only private MySQL3317 is allowed"
        );
        let pool = MySqlPoolOptions::new()
            .max_connections(1)
            .idle_timeout(None)
            .max_lifetime(None)
            .connect_with(options)
            .await
            .expect("private MySQL");
        sqlx::query(
            "CREATE TEMPORARY TABLE `account_schedule_items` (\
             `account_id` int unsigned NOT NULL, \
             `schedule_id` int NOT NULL, \
             `gave` tinyint unsigned NOT NULL DEFAULT 0, \
             `cumulated` bigint NOT NULL DEFAULT 0, \
             `updated` bigint NOT NULL DEFAULT 0, \
             PRIMARY KEY (`account_id`, `schedule_id`)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
        )
        .execute(&pool)
        .await
        .unwrap();
        Some(pool)
    }

    async fn row_count(pool: &MySqlPool) -> i64 {
        sqlx::query_scalar("SELECT COUNT(*) FROM `account_schedule_items`")
            .fetch_one(pool)
            .await
            .unwrap()
    }

    #[test]
    fn an_empty_snapshot_has_nothing_to_write() {
        assert!(PreparedScheduleSave::default().is_empty());
        assert_eq!(PreparedScheduleSave::default().row_count(), 0);
    }

    #[tokio::test]
    #[ignore = "requires private MySQL3317 via AAEMU_TEST_MYSQL; uses a connection-local temporary table"]
    async fn private_mysql_round_trips_an_account_shared_by_two_characters() {
        let Some(pool) = private_pool().await else {
            return;
        };
        let start = ts("2026-09-06 01:00:00");
        let data = ScheduleItemData::from_rows(vec![live(1), live(4)]);
        let none = ScheduleEligibility::default();
        let mut account = AccountScheduleProgress::new(7);
        // Two characters of the same login account share the one clock and the one row set.
        account.join(11, start);
        account.join(12, start + 10);
        account.tick(start + 300, &data, &none);
        account.leave(11);
        account.tick(start + 600, &data, &none);
        let manager = ScheduleItemManager::from_accounts(BTreeMap::from([(7, account)]));

        let batch = manager.prepare_save();
        assert_eq!(batch.row_count(), 2);
        let mut tx = pool.begin().await.unwrap();
        let token = ScheduleItemManager::write_prepared(&mut tx, batch)
            .await
            .unwrap();
        assert_eq!(token.written(), 2);
        tx.commit().await.unwrap();
        manager.commit_save(token);
        assert!(manager.prepare_save().is_empty());

        // One row per (account, schedule id), never one row set per character.
        assert_eq!(row_count(&pool).await, 2);

        // The reload restores the counters and their seconds, with an idle clock: sessions and
        // accrual baselines are transient and never come back from the table.
        let restored = ScheduleItemManager::load(&pool).await.unwrap();
        {
            let accounts = restored.lock();
            let account = accounts.get(&7).expect("account 7");
            assert_eq!(account.account_id(), 7);
            for schedule_id in [1, 4] {
                let state = account.state(schedule_id).expect("row");
                assert_eq!(state.gave, 0);
                assert_eq!(state.cumulated, 600);
                assert_eq!(state.updated, start + 600);
            }
            assert_eq!(account.account_clock(), 0);
            assert_eq!(account.online_characters(), 0);
            assert!(account.dirty_rows().is_empty());
        }
        assert!(restored.prepare_save().is_empty());

        pool.close().await;
    }

    #[tokio::test]
    #[ignore = "requires private MySQL3317 via AAEMU_TEST_MYSQL; uses a connection-local temporary table"]
    async fn private_mysql_rollback_keeps_the_rows_dirty_for_a_retry() {
        let Some(pool) = private_pool().await else {
            return;
        };
        let start = ts("2026-09-06 01:00:00");
        let manager = dirty_manager(start, 600);

        let batch = manager.prepare_save();
        assert_eq!(batch.row_count(), 1);
        let mut tx = pool.begin().await.unwrap();
        let token = ScheduleItemManager::write_prepared(&mut tx, batch)
            .await
            .unwrap();
        tx.rollback().await.unwrap();
        // A rolled-back write must not be acknowledged: the same dirty snapshot is retried and
        // nothing reached the table.
        drop(token);
        assert_eq!(manager.prepare_save().row_count(), 1);
        assert_eq!(row_count(&pool).await, 0);

        // The retry writes and acknowledges the same dirty row.
        let batch = manager.prepare_save();
        let mut tx = pool.begin().await.unwrap();
        let token = ScheduleItemManager::write_prepared(&mut tx, batch)
            .await
            .unwrap();
        tx.commit().await.unwrap();
        manager.commit_save(token);
        assert!(manager.prepare_save().is_empty());
        assert_eq!(row_count(&pool).await, 1);
        let restored = ScheduleItemManager::load(&pool).await.unwrap();
        assert_eq!(
            restored
                .lock()
                .get(&7)
                .expect("account 7")
                .state(1)
                .expect("row 1")
                .cumulated,
            600
        );

        pool.close().await;
    }

    #[tokio::test]
    #[ignore = "requires private MySQL3317 via AAEMU_TEST_MYSQL; uses a connection-local temporary table"]
    async fn private_mysql_per_row_upsert_keeps_another_characters_rows() {
        let Some(pool) = private_pool().await else {
            return;
        };
        let start = ts("2026-09-06 01:00:00");
        let none = ScheduleEligibility::default();
        // Both characters of account 7 have rows 1 and 4 saved; each carries data for only one of
        // them, so each save snapshot holds exactly one dirty row. A full-account rewrite would
        // drop the other character's row here.
        let mut first = AccountScheduleProgress::from_saved(7, saved_rows(0, start));
        first.join(11, start);
        first.tick(
            start + 600,
            &ScheduleItemData::from_rows(vec![live(1)]),
            &none,
        );
        let mut second = AccountScheduleProgress::from_saved(7, saved_rows(0, start));
        second.join(12, start);
        second.tick(
            start + 300,
            &ScheduleItemData::from_rows(vec![live(4)]),
            &none,
        );
        let first_manager = ScheduleItemManager::from_accounts(BTreeMap::from([(7, first)]));
        let second_manager = ScheduleItemManager::from_accounts(BTreeMap::from([(7, second)]));

        for manager in [&first_manager, &second_manager] {
            let batch = manager.prepare_save();
            assert_eq!(batch.row_count(), 1);
            let mut tx = pool.begin().await.unwrap();
            let token = ScheduleItemManager::write_prepared(&mut tx, batch)
                .await
                .unwrap();
            tx.commit().await.unwrap();
            manager.commit_save(token);
        }

        assert_eq!(row_count(&pool).await, 2);
        let restored = ScheduleItemManager::load(&pool).await.unwrap();
        {
            let accounts = restored.lock();
            let account = accounts.get(&7).expect("account 7");
            assert_eq!(account.state(1).expect("row 1").cumulated, 600);
            assert_eq!(account.state(4).expect("row 4").cumulated, 300);
        }

        pool.close().await;
    }

    #[tokio::test]
    #[ignore = "requires private MySQL3317 via AAEMU_TEST_MYSQL; uses a connection-local temporary table"]
    async fn private_mysql_statement_errors_propagate() {
        let Some(pool) = private_pool().await else {
            return;
        };
        // A drifted shadow (the migration never ran) must fail the load instead of reading as an
        // empty store, which would reset every account's progress on the next save.
        sqlx::query("DROP TEMPORARY TABLE `account_schedule_items`")
            .execute(&pool)
            .await
            .unwrap();
        sqlx::query(
            "CREATE TEMPORARY TABLE `account_schedule_items` (\
             `account_id` int unsigned NOT NULL, \
             `schedule_id` int NOT NULL, \
             `gave` tinyint unsigned NOT NULL DEFAULT 0, \
             PRIMARY KEY (`account_id`, `schedule_id`)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
        )
        .execute(&pool)
        .await
        .unwrap();
        assert!(load_accounts(&pool).await.is_err());

        let batch = PreparedScheduleSave {
            accounts: vec![PreparedAccountSave {
                account_id: 7,
                rows: vec![(
                    1,
                    AccountScheduleState {
                        gave: 1,
                        cumulated: 60,
                        updated: 1_700_000_000,
                    },
                )],
                marks: vec![(1, 1)],
            }],
        };
        let mut tx = pool.begin().await.unwrap();
        assert!(write_rows(&mut tx, &batch).await.is_err());
        tx.rollback().await.unwrap();

        pool.close().await;
    }
}
