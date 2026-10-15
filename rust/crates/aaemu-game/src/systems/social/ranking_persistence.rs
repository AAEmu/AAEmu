//! Ranking persistence: the `character_rank_scores` and `rank_period_payouts` tables behind
//! [`super::ranking_manager::RankingManager`].
//!
//! There is no C# behind this (the 1.2 C# never ported the ranking boards); the layout follows
//! the other whole-table loads (`account_schedule_items`): everything is read once at startup,
//! and a save upserts rows inside the `SaveManager` transaction, so a statement error
//! propagates and the caller rolls the transaction back.
//!
//! * `character_rank_scores`: one row per `(rank_id, period_start, character_id)`; the figures are
//!   signed `INT`, every timestamp is UTC Unix seconds. See `SQL/updates/2026-10-15_aaemu_game_
//!   ranking.sql`.
//! * `rank_period_payouts`: the `(rank_id, period_start)` periods whose rewards were already
//!   mailed, so a restart never pays a closed period twice.
//!
//! A save writes every row it is handed (idempotent upserts). The set is small (a few boards,
//! one row per participant and period) and an upsert of unchanged values is a no-op in MySQL, so
//! no dirty tracking is needed and a rolled-back save loses nothing.

use std::collections::BTreeMap;

use sqlx::mysql::MySqlPool;
use sqlx::{MySql, Transaction};

use super::ranking_runtime::{RankScore, RankingState};
use crate::db::int;
use crate::error::Result;

/// Everything the tables hold, grouped for the manager.
#[derive(Debug, Default)]
pub struct LoadedRanking {
    /// `character_id -> that character's scores`.
    pub states: BTreeMap<u32, RankingState>,
    /// `(rank_id, period_start, paid_at)` of every period already paid.
    pub paid: Vec<(u32, i64, i64)>,
}

/// Reads both tables (`ORDER BY` their key, so the result never depends on row order). A
/// `SELECT` error propagates: the caller must fail the startup instead of serving empty boards.
pub async fn load(pool: &MySqlPool) -> Result<LoadedRanking> {
    let rows = sqlx::query(
        "SELECT CAST(`rank_id` AS SIGNED) AS rank_id, `period_start`, \
         CAST(`character_id` AS SIGNED) AS character_id, `period_end`, \
         CAST(`account_id` AS SIGNED) AS account_id, `account`, \
         CAST(`world_id` AS SIGNED) AS world_id, CAST(`v1` AS SIGNED) AS v1, \
         CAST(`v2` AS SIGNED) AS v2, `updated` \
         FROM `character_rank_scores` ORDER BY `rank_id`, `period_start`, `character_id`",
    )
    .fetch_all(pool)
    .await?;
    let mut loaded = LoadedRanking::default();
    for row in &rows {
        let character_id = int(row, "character_id")? as u32;
        loaded
            .states
            .entry(character_id)
            .or_default()
            .restore(RankScore {
                rank_id: int(row, "rank_id")? as u32,
                period_start: int(row, "period_start")?,
                period_end: int(row, "period_end")?,
                account_id: int(row, "account_id")? as u32,
                account: sqlx::Row::try_get::<String, _>(row, "account")?,
                character_id,
                world_id: int(row, "world_id")? as u8,
                v1: int(row, "v1")? as i32,
                v2: int(row, "v2")? as i32,
                updated: int(row, "updated")?,
            });
    }
    let payouts = sqlx::query(
        "SELECT CAST(`rank_id` AS SIGNED) AS rank_id, `period_start` \
         FROM `rank_period_payouts` ORDER BY `rank_id`, `period_start`",
    )
    .fetch_all(pool)
    .await?;
    for row in &payouts {
        loaded.paid.push((
            int(row, "rank_id")? as u32,
            int(row, "period_start")?,
            int(row, "paid_at")?,
        ));
    }
    tracing::info!(
        "Loaded ranking scores: {} characters, {} paid periods",
        loaded.states.len(),
        loaded.paid.len()
    );
    Ok(loaded)
}

/// Upserts `scores` and the paid-period ledger inside the caller's transaction and returns the
/// number of rows written. Any statement error propagates (the caller rolls back).
pub async fn write(
    tx: &mut Transaction<'_, MySql>,
    scores: &[RankScore],
    paid: &[(u32, i64, i64)],
) -> Result<usize> {
    for score in scores {
        sqlx::query(
            "INSERT INTO `character_rank_scores` (`rank_id`, `period_start`, `character_id`, \
             `period_end`, `account_id`, `account`, `world_id`, `v1`, `v2`, `updated`) \
             VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?) \
             ON DUPLICATE KEY UPDATE `period_end` = ?, `account_id` = ?, `account` = ?, \
             `world_id` = ?, `v1` = ?, `v2` = ?, `updated` = ?",
        )
        .bind(score.rank_id)
        .bind(score.period_start)
        .bind(score.character_id)
        .bind(score.period_end)
        .bind(score.account_id)
        .bind(&score.account)
        .bind(score.world_id)
        .bind(score.v1)
        .bind(score.v2)
        .bind(score.updated)
        .bind(score.period_end)
        .bind(score.account_id)
        .bind(&score.account)
        .bind(score.world_id)
        .bind(score.v1)
        .bind(score.v2)
        .bind(score.updated)
        .execute(&mut **tx)
        .await?;
    }
    for (rank_id, period_start, paid_at) in paid {
        sqlx::query(
            "INSERT INTO `rank_period_payouts` (`rank_id`, `period_start`, `paid_at`) \
             VALUES (?, ?, ?) ON DUPLICATE KEY UPDATE `paid_at` = `paid_at`",
        )
        .bind(rank_id)
        .bind(period_start)
        .bind(paid_at)
        .execute(&mut **tx)
        .await?;
    }
    Ok(scores.len() + paid.len())
}

#[cfg(test)]
mod tests {
    use super::*;
    use sqlx::mysql::{MySqlConnectOptions, MySqlPoolOptions};
    use std::str::FromStr;

    fn score(rank_id: u32, character_id: u32, v1: i32, v2: i32) -> RankScore {
        RankScore {
            rank_id,
            period_start: 1_704_067_200,
            period_end: 1_706_486_400,
            account_id: 1000 + character_id,
            account: format!("Char{character_id}"),
            character_id,
            world_id: 1,
            v1,
            v2,
            updated: 1_704_100_000,
        }
    }

    /// Scores and the payout ledger survive a write + load, a second write upserts, and a signed
    /// figure keeps its sign. Temporary tables only, on the private MySQL 3317.
    #[tokio::test]
    #[ignore = "requires private MySQL3317 via AAEMU_TEST_MYSQL; uses only temporary tables"]
    async fn private_mysql_ranking_scores_round_trip() {
        let Ok(url) = std::env::var("AAEMU_TEST_MYSQL") else {
            return;
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
        let ddl = include_str!("../../../../../../SQL/updates/2026-10-15_aaemu_game_ranking.sql")
            .lines()
            .filter(|line| !line.trim_start().starts_with("--"))
            .collect::<Vec<_>>()
            .join("\n")
            .replace("CREATE TABLE IF NOT EXISTS", "CREATE TEMPORARY TABLE");
        for statement in ddl.split(';').filter(|s| !s.trim().is_empty()) {
            sqlx::query(statement).execute(&pool).await.unwrap();
        }

        let mut scores = vec![score(1, 7, 4, 1), score(1, 8, -3, 0)];
        let mut tx = pool.begin().await.unwrap();
        let written = write(&mut tx, &scores, &[(1, 1_704_067_200, 1_706_500_000)])
            .await
            .unwrap();
        tx.commit().await.unwrap();
        assert_eq!(written, 3);

        scores[0].v1 = 9;
        let mut tx = pool.begin().await.unwrap();
        write(&mut tx, &scores, &[(1, 1_704_067_200, 1_706_500_000)])
            .await
            .unwrap();
        tx.commit().await.unwrap();

        let loaded = load(&pool).await.unwrap();
        assert_eq!(loaded.paid, [(1, 1_704_067_200, 1_706_500_000)]);
        let a = loaded.states[&7].score(1, 1_704_067_200).unwrap();
        assert_eq!((a.v1, a.v2, a.account.as_str()), (9, 1, "Char7"));
        let b = loaded.states[&8].score(1, 1_704_067_200).unwrap();
        assert_eq!((b.v1, b.world_id, b.account_id), (-3, 1, 1008));
        assert_eq!(b, &scores[1]);
    }
}
