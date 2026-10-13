//! Character achievement persistence: the `character_records` / `character_achievements` tables
//! behind [`super::achievement_runtime::AchievementState`].
//!
//! C# scope (later history, `7851f67cc`): `CharacterRecords.Load` / `Save` and
//! `CharacterAchievements.Load` / `Save`. The two tables are read when the character is selected
//! ([`load_character_achievements`], one call in `db_character::load_character_details`) and
//! rewritten inside the character's save transaction ([`save_character_achievements`], one call in
//! `db_character::save_character`).
//!
//! State lives on `Character` and `Character` is cloned into the save snapshot, so a completion and
//! the record values behind it are committed in the same transaction as the rest of the character,
//! and a save failure keeps the whole transaction open for the caller to roll back: no statement
//! here catches its error.
//!
//! Column units: `character_records.value` is a **signed** BIGINT (the content stores `-1` as its
//! "no target" sentinel and the runtime's store keeps the highest value ever reported);
//! `character_achievements.completed_at` is Unix **seconds** in UTC, `NULL` while the achievement
//! is still in progress — the unit `db::unix` / `db::from_unix` use for the other character
//! timestamps. A non-null `completed_at` outside the timestamp range fails the load with a typed
//! error instead of being read as "in progress", which would make a corrupted completion look
//! unearned and could pay its reward again. `character_achievements.reward_delivered` is the
//! reward receipt and is saved and restored with the row, so a completion that was paid is not
//! paid again after a relog. Both tables are loaded `ORDER BY` their key, so a restored state
//! never depends on the table's row order, and both are written from the runtime's snapshots
//! (`record_snapshot`, `progress_snapshot`), which are in ascending key order.

use chrono::{DateTime, Utc};
use sqlx::mysql::MySqlPool;
use sqlx::{MySql, Row, Transaction};

use super::achievement_runtime::AchievementState;
use crate::db::int;
use crate::error::{GameError, Result};
use crate::models::character::Character;

/// The `completed_at` column (Unix seconds, UTC) as the runtime's instant. `NULL` is "still in
/// progress"; a non-null value outside `DateTime`'s range is a typed error, not `None` — reading
/// it as unearned would let a corrupted completion pay its reward a second time.
fn completed_at_from_unix(
    owner: u32,
    achievement_id: u32,
    seconds: Option<i64>,
) -> Result<Option<DateTime<Utc>>> {
    let Some(seconds) = seconds else {
        return Ok(None);
    };
    if let Some(completed_at) = DateTime::from_timestamp(seconds, 0) {
        return Ok(Some(completed_at));
    }

    Err(GameError::Protocol(format!(
        "character_achievements: owner {owner} achievement {achievement_id} has completed_at \
         {seconds}, which is outside the range of a timestamp"
    )))
}

/// The runtime's instant as the `completed_at` column.
fn completed_at_to_unix(completed_at: Option<DateTime<Utc>>) -> Option<i64> {
    completed_at.map(|completed_at| completed_at.timestamp())
}

/// `CharacterRecords.Load` + `CharacterAchievements.Load`: reads a character's achievement state.
pub async fn load_achievement_state(
    pool: &MySqlPool,
    character_id: u32,
) -> Result<AchievementState> {
    let mut state = AchievementState::default();

    let rows = sqlx::query(
        "SELECT CAST(`record_id` AS SIGNED) AS record_id, `value` FROM `character_records` \
         WHERE `owner` = ? ORDER BY `record_id`",
    )
    .bind(character_id)
    .fetch_all(pool)
    .await?;
    for row in &rows {
        state.restore_record(int(row, "record_id")? as u32, int(row, "value")?);
    }

    let rows = sqlx::query(
        "SELECT CAST(`achievement_id` AS SIGNED) AS achievement_id, CAST(`amount` AS SIGNED) AS amount, \
         `completed_at`, CAST(`reward_delivered` AS SIGNED) AS reward_delivered \
         FROM `character_achievements` WHERE `owner` = ? ORDER BY `achievement_id`",
    )
    .bind(character_id)
    .fetch_all(pool)
    .await?;
    for row in &rows {
        let achievement_id = int(row, "achievement_id")? as u32;
        state.restore_progress_with_receipt(
            achievement_id,
            int(row, "amount")?,
            completed_at_from_unix(
                character_id,
                achievement_id,
                row.try_get::<Option<i64>, _>("completed_at")?,
            )?,
            int(row, "reward_delivered")? != 0,
        );
    }

    Ok(state)
}

/// `CharacterRecords.Save` + `CharacterAchievements.Save`: rewrites a character's rows inside the
/// caller's transaction. The rows are deleted first because the snapshot is the whole truth — a
/// reset drops progress (and its reward receipt) and a cleared record has to disappear, not be
/// zeroed. Reward receipts are written with the progress rows they belong to.
pub async fn save_achievement_state(
    tx: &mut Transaction<'_, MySql>,
    character_id: u32,
    state: &AchievementState,
) -> Result<()> {
    sqlx::query("DELETE FROM `character_records` WHERE `owner` = ?")
        .bind(character_id)
        .execute(&mut **tx)
        .await?;
    for (record_id, value) in state.record_snapshot() {
        sqlx::query(
            "INSERT INTO `character_records` (`owner`, `record_id`, `value`) VALUES (?, ?, ?)",
        )
        .bind(character_id)
        .bind(record_id)
        .bind(value)
        .execute(&mut **tx)
        .await?;
    }

    sqlx::query("DELETE FROM `character_achievements` WHERE `owner` = ?")
        .bind(character_id)
        .execute(&mut **tx)
        .await?;
    for progress in state.progress_snapshot() {
        sqlx::query(
            "INSERT INTO `character_achievements` (`owner`, `achievement_id`, `amount`, `completed_at`, \
             `reward_delivered`) VALUES (?, ?, ?, ?, ?)",
        )
        .bind(character_id)
        .bind(progress.achievement_id())
        .bind(progress.amount())
        .bind(completed_at_to_unix(progress.completed_at()))
        .bind(progress.reward_delivered())
        .execute(&mut **tx)
        .await?;
    }

    Ok(())
}

/// `Character.Load()`: reads the selected character's achievement state (`Character.Load` runs it
/// with the other per-character tables). Errors are returned so the load fails instead of running
/// the character with silently empty progress.
pub async fn load_character_achievements(pool: &MySqlPool, c: &mut Character) -> Result<()> {
    c.achievement_state = load_achievement_state(pool, c.id).await?;
    Ok(())
}

/// `Character.Save(connection, transaction)`: writes the snapshot the character was cloned with,
/// inside the caller's transaction.
pub async fn save_character_achievements(
    tx: &mut Transaction<'_, MySql>,
    c: &Character,
) -> Result<()> {
    save_achievement_state(tx, c.id, &c.achievement_state).await
}

/// `SHOW CREATE TABLE` output as a `CREATE TEMPORARY TABLE` statement for the tests: MySQL
/// supports no foreign keys on temporary tables, so the `FOREIGN KEY` constraint — and the comma
/// that separated it from the previous line — is dropped while the real columns, keys, defaults
/// and comments are kept.
#[cfg(test)]
pub(crate) fn temporary_shadow_definition(definition: &str) -> String {
    let mut lines: Vec<&str> = definition.lines().collect();
    if let Some(index) = lines.iter().position(|line| line.contains("FOREIGN KEY")) {
        lines.remove(index);
        if index > 0 {
            let previous = lines[index - 1];
            if let Some(stripped) = previous.strip_suffix(',') {
                lines[index - 1] = stripped;
            }
        }
    }

    lines
        .join("\n")
        .replacen("CREATE TABLE ", "CREATE TEMPORARY TABLE ", 1)
}

#[cfg(test)]
mod tests {
    use super::*;
    use sqlx::mysql::{MySqlConnectOptions, MySqlPoolOptions};
    use std::str::FromStr;

    /// A fixed instant, so the timestamp assertions read are deterministic.
    fn at(seconds: i64) -> DateTime<Utc> {
        DateTime::from_timestamp(seconds, 0).expect("valid timestamp")
    }

    #[test]
    fn shadow_definition_drops_the_foreign_key_and_its_comma() {
        let definition = "CREATE TABLE `t` (\n  `owner` int unsigned NOT NULL,\n  \
                          PRIMARY KEY (`owner`),\n  CONSTRAINT `fk` FOREIGN KEY (`owner`) \
                          REFERENCES `characters` (`id`) ON DELETE CASCADE\n) ENGINE=InnoDB";
        let shadow = temporary_shadow_definition(definition);
        assert!(shadow.starts_with("CREATE TEMPORARY TABLE `t` ("));
        assert!(!shadow.contains("FOREIGN KEY"));
        // The comma that separated the constraint from the previous line must go with it.
        assert!(!shadow.contains(",\n)"));
        assert!(shadow.contains("PRIMARY KEY (`owner`)\n)"));
    }

    #[test]
    fn out_of_range_completed_at_is_a_typed_error_but_null_is_not() {
        assert_eq!(completed_at_from_unix(1, 2, None).unwrap(), None);
        assert_eq!(
            completed_at_from_unix(1, 2, Some(1_700_000_000)).unwrap(),
            Some(at(1_700_000_000))
        );

        // A corrupted completion must not read as "still in progress": that would make its reward
        // payable again.
        let error = completed_at_from_unix(1, 2, Some(i64::MAX)).expect_err("out of range");
        assert!(matches!(error, GameError::Protocol(_)), "got {error:?}");
    }

    /// Connects to the private MySQL3317 schema and shadows the two live tables with
    /// connection-local temporary copies of their actual schemas, so the statements run against
    /// the migrated shape without touching any live row. `None` when `AAEMU_TEST_MYSQL` is not
    /// set.
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
            .connect_with(options)
            .await
            .expect("private MySQL");
        for table in ["character_records", "character_achievements"] {
            let (_, definition): (String, String) =
                sqlx::query_as(&format!("SHOW CREATE TABLE `{table}`"))
                    .fetch_one(&pool)
                    .await
                    .unwrap();
            assert!(definition.starts_with("CREATE TABLE "));
            sqlx::query(&temporary_shadow_definition(&definition))
                .execute(&pool)
                .await
                .unwrap();
        }
        Some(pool)
    }

    async fn save(pool: &MySqlPool, character_id: u32, state: &AchievementState) {
        let mut tx = pool.begin().await.unwrap();
        save_achievement_state(&mut tx, character_id, state)
            .await
            .unwrap();
        tx.commit().await.unwrap();
    }

    #[tokio::test]
    #[ignore = "requires private MySQL3317 via AAEMU_TEST_MYSQL; shadows both achievement tables on one connection"]
    async fn private_mysql_achievements_round_trip_records_and_progress() {
        let Some(pool) = private_pool().await else {
            return;
        };

        // A completion timestamp is Unix seconds, a negative record value stays signed, an
        // achievement still in progress keeps a NULL `completed_at`, and the reward receipt is part
        // of the row: achievement 1 was paid, achievement 2 is still owed, 3 never completed.
        let earned = at(1_700_000_000);
        let mut state = AchievementState::default();
        state.restore_record(10, 50);
        state.restore_record(11, -1);
        state.restore_progress(1, 50, Some(earned));
        assert!(state.mark_reward_delivered(1));
        state.restore_progress(2, 7, Some(at(1_700_000_001)));
        state.restore_progress(3, 7, None);
        save(&pool, 1, &state).await;

        let loaded = load_achievement_state(&pool, 1).await.unwrap();
        assert_eq!(loaded.record_snapshot(), state.record_snapshot());
        assert_eq!(loaded.progress_snapshot(), state.progress_snapshot());
        assert_eq!(loaded.record_value(11), -1);
        assert_eq!(loaded.completed_at(1), Some(earned));
        assert!(loaded.is_complete(1));
        assert!(loaded.progress(1).expect("row").reward_delivered());
        assert!(!loaded.has_pending_reward(1));
        assert_eq!(loaded.completed_at(2), Some(at(1_700_000_001)));
        assert!(!loaded.progress(2).expect("row").reward_delivered());
        assert_eq!(loaded.amount(3), 7);
        assert_eq!(loaded.completed_at(3), None);
        // Only the unpaid completion is pending; the paid one and the unfinished one are not.
        assert_eq!(loaded.pending_reward_ids(), [2]);

        // Another character's rows are neither read nor overwritten by the first one's save.
        let mut other = AchievementState::default();
        other.restore_record(10, 1);
        other.restore_progress(1, 1, None);
        save(&pool, 2, &other).await;
        assert_eq!(
            load_achievement_state(&pool, 2)
                .await
                .unwrap()
                .record_snapshot(),
            other.record_snapshot()
        );
        assert_eq!(
            load_achievement_state(&pool, 1)
                .await
                .unwrap()
                .record_snapshot(),
            state.record_snapshot()
        );

        pool.close().await;
    }

    #[tokio::test]
    #[ignore = "requires private MySQL3317 via AAEMU_TEST_MYSQL; shadows both achievement tables on one connection"]
    async fn private_mysql_achievements_reset_deletes_rows_and_rewrites_timestamps() {
        let Some(pool) = private_pool().await else {
            return;
        };

        let mut state = AchievementState::default();
        state.restore_record(10, 50);
        state.restore_record(11, 5);
        state.restore_progress(1, 50, Some(at(1_700_000_000)));
        assert!(state.mark_reward_delivered(1));
        state.restore_progress(2, 7, None);
        save(&pool, 1, &state).await;

        // The reset path: forgetting an achievement and clearing its record delete the rows (a
        // reset row must not come back at 0, nor its reward receipt), a lowered record is written
        // as it is (Set, not Report), and the next completion rewrites the timestamp.
        let later = at(1_700_000_123);
        let mut reset = load_achievement_state(&pool, 1).await.unwrap();
        assert!(reset.progress(1).expect("row").reward_delivered());
        reset.forget_achievement(1);
        reset.clear_record(11);
        reset.restore_record(10, 3);
        reset.restore_progress(2, 7, Some(later));
        save(&pool, 1, &reset).await;

        let mut reloaded = load_achievement_state(&pool, 1).await.unwrap();
        assert!(!reloaded.is_complete(1));
        assert_eq!(reloaded.progress(1), None);
        assert_eq!(reloaded.amount(1), 0);
        assert!(!reloaded.mark_reward_delivered(1));
        assert_eq!(reloaded.record_value(11), 0);
        assert_eq!(reloaded.record_value(10), 3);
        assert_eq!(reloaded.amount(2), 7);
        assert_eq!(reloaded.completed_at(2), Some(later));
        // The reset dropped the paid receipt; the completion that survived is still owed.
        assert_eq!(reloaded.pending_reward_ids(), [2]);

        // The two rows that were dropped are gone from the tables, not zeroed in place.
        let records: i64 = sqlx::query_scalar("SELECT COUNT(*) FROM `character_records`")
            .fetch_one(&pool)
            .await
            .unwrap();
        let progress: i64 = sqlx::query_scalar("SELECT COUNT(*) FROM `character_achievements`")
            .fetch_one(&pool)
            .await
            .unwrap();
        assert_eq!((records, progress), (1, 1));

        pool.close().await;
    }

    #[tokio::test]
    #[ignore = "requires private MySQL3317 via AAEMU_TEST_MYSQL; shadows both achievement tables on one connection"]
    async fn private_mysql_achievements_propagate_sql_errors() {
        let Some(pool) = private_pool().await else {
            return;
        };

        // A statement that fails has to come back as an error, not as an empty state: the caller's
        // transaction has to roll back instead of committing a character whose progress was
        // dropped. The shadow is replaced with a drifted one (no `value` / `completed_at`
        // columns), which is what a table the migration did not bring up to date looks like.
        sqlx::query("DROP TEMPORARY TABLE `character_records`")
            .execute(&pool)
            .await
            .unwrap();
        sqlx::query(
            "CREATE TEMPORARY TABLE `character_records` (`owner` int unsigned NOT NULL, \
             `record_id` int unsigned NOT NULL, PRIMARY KEY (`owner`, `record_id`)) \
             ENGINE=InnoDB",
        )
        .execute(&pool)
        .await
        .unwrap();
        assert!(load_achievement_state(&pool, 1).await.is_err());

        let mut state = AchievementState::default();
        state.restore_record(10, 50);
        let mut tx = pool.begin().await.unwrap();
        assert!(save_achievement_state(&mut tx, 1, &state).await.is_err());
        tx.rollback().await.unwrap();

        pool.close().await;
    }

    #[tokio::test]
    #[ignore = "requires private MySQL3317 via AAEMU_TEST_MYSQL; shadows both achievement tables on one connection"]
    async fn private_mysql_achievement_receipt_survives_replay_and_reset() {
        let Some(pool) = private_pool().await else {
            return;
        };

        // A completed, paid achievement is written with its receipt.
        let earned = at(1_700_000_000);
        let mut state = AchievementState::default();
        state.restore_progress(1, 50, Some(earned));
        assert!(state.mark_reward_delivered(1));
        save(&pool, 1, &state).await;

        // Loading it back and restoring the same row again (what a relog does) keeps the receipt:
        // a completion that was paid is not payable a second time.
        let mut loaded = load_achievement_state(&pool, 1).await.unwrap();
        assert!(loaded.progress(1).expect("row").reward_delivered());
        loaded.restore_progress(1, 50, Some(earned));
        assert!(loaded.progress(1).expect("row").reward_delivered());
        assert!(!loaded.mark_reward_delivered(1));
        save(&pool, 1, &loaded).await;
        let again = load_achievement_state(&pool, 1).await.unwrap();
        assert!(again.progress(1).expect("row").reward_delivered());
        assert!(again.pending_reward_ids().is_empty());

        // The reset path drops the row and its receipt; the re-earned completion is a fresh one,
        // so its reward is owed again and can be settled again.
        let mut reset = again;
        reset.forget_achievement(1);
        save(&pool, 1, &reset).await;
        let mut reset = load_achievement_state(&pool, 1).await.unwrap();
        assert_eq!(reset.progress(1), None);
        assert!(!reset.mark_reward_delivered(1));

        reset.restore_progress(1, 50, Some(at(1_700_000_002)));
        assert_eq!(reset.pending_reward_ids(), [1]);
        assert!(reset.mark_reward_delivered(1));
        save(&pool, 1, &reset).await;
        let paid = load_achievement_state(&pool, 1).await.unwrap();
        assert!(paid.progress(1).expect("row").reward_delivered());
        assert!(paid.pending_reward_ids().is_empty());

        pool.close().await;
    }

    #[tokio::test]
    #[ignore = "requires private MySQL3317 via AAEMU_TEST_MYSQL; shadows both achievement tables on one connection"]
    async fn private_mysql_out_of_range_completed_at_fails_the_load() {
        let Some(pool) = private_pool().await else {
            return;
        };

        // A corrupted completion must fail the load instead of reading as "still in progress",
        // which would make its reward payable again.
        sqlx::query(
            "INSERT INTO `character_achievements` (`owner`, `achievement_id`, `amount`, `completed_at`, \
             `reward_delivered`) VALUES (1, 7, 1, ?, 1)",
        )
        .bind(i64::MAX)
        .execute(&pool)
        .await
        .unwrap();

        let error = load_achievement_state(&pool, 1)
            .await
            .expect_err("out-of-range completed_at");
        assert!(matches!(error, GameError::Protocol(_)), "got {error:?}");

        // NULL is not corrupted, and a receipt on an unfinished row is restored as it is (it is not
        // a payable completion).
        sqlx::query(
            "UPDATE `character_achievements` SET `completed_at` = NULL \
             WHERE `owner` = 1 AND `achievement_id` = 7",
        )
        .execute(&pool)
        .await
        .unwrap();
        let state = load_achievement_state(&pool, 1).await.unwrap();
        assert_eq!(state.amount(7), 1);
        assert!(!state.is_complete(7));
        assert!(state.progress(7).expect("row").reward_delivered());
        assert!(!state.has_pending_reward(7));
        assert!(state.pending_reward_ids().is_empty());

        pool.close().await;
    }
}
