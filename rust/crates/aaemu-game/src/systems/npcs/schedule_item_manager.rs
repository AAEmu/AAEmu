//! The account-wide schedule-item progress store: one [`AccountScheduleProgress`] per login
//! account (`ScheduleItemManager._progress`), the one-time startup load and the prepared-save
//! handshake the financial snapshot uses.
//!
//! C# scope (`AAEmu.Game/Core/Managers/ScheduleItemManager.cs` at `7851f67cc`): `_progress`, the
//! singleton the world paths reach it through, and the save half (`Load` / `PrepareSave` /
//! `ConfirmSaved`). The state transitions are the runtime ([`super::schedule_item_runtime`]);
//! this file owns only the map, its load and the persistence handshake. The world/session wiring
//! (entry, leave, ticks and claims) is a later task.
//!
//! # Locking
//!
//! The map is one `Mutex`, and every state operation is synchronous, so the guard is taken and
//! released inside one call and must never live across an `await`:
//!
//! * [`ScheduleItemManager::lock`] is the short accessor a later manager tick / claim operation
//!   uses. That operation has to run under the economy `ops_lock`, like every other mutation
//!   that must land on one side of the financial snapshot boundary.
//! * [`ScheduleItemManager::prepare_save`] clones the dirty rows and their mark stamps
//!   synchronously (the financial snapshot calls it under that same economy `ops_lock`), so the
//!   counters it captures are the ones of the granted items / mails boundary.
//! * [`ScheduleItemManager::write_prepared`] writes only the owned snapshot — the live map is
//!   never read while the SQL is in flight — and [`ScheduleItemManager::commit_save`] clears only
//!   the stamps that did not change since it, after the outer transaction committed. A failed or
//!   rolled-back write drops the receipt (callers must not acknowledge it) and leaves every row
//!   dirty for the next save; a row changed after the snapshot keeps its newer stamp.

use std::collections::BTreeMap;
use std::sync::{Mutex, MutexGuard};

use sqlx::mysql::MySqlPool;
use sqlx::{MySql, Transaction};

use crate::error::Result;

use super::schedule_item_persistence::{self, PreparedAccountSave, PreparedScheduleSave};
use super::schedule_item_runtime::AccountScheduleProgress;
use crate::packets::g2c_misc::ScheduleItem;

/// One account's schedule-item progress per login account (`ScheduleItemManager._progress`).
#[derive(Debug, Default)]
pub struct ScheduleItemManager {
    progress: Mutex<BTreeMap<u32, AccountScheduleProgress>>,
    /// character id -> the rows of the last `SCScheduleItemUpdatePacket` sent to it; transient
    /// (never saved). Lets the periodic tick re-send only when something the client shows changed.
    sent: Mutex<BTreeMap<u32, Vec<ScheduleItem>>>,
}

/// The receipt of [`ScheduleItemManager::write_prepared`]: the marks of the snapshot that was
/// written, to acknowledge once the outer transaction committed.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct ScheduleSaveToken {
    /// `(account_id, marks)` of the written snapshot, in account id order.
    accounts: Vec<(u32, Vec<(i32, u64)>)>,
    written: usize,
}

impl ScheduleSaveToken {
    /// The stamps of a snapshot, as [`ScheduleItemManager::write_prepared`] returns them.
    fn of_snapshot(batch: &PreparedScheduleSave) -> Self {
        Self {
            accounts: batch
                .accounts
                .iter()
                .map(|account| (account.account_id, account.marks.clone()))
                .collect(),
            written: 0,
        }
    }

    /// The rows the snapshot wrote.
    pub fn written(&self) -> usize {
        self.written
    }
}

impl ScheduleItemManager {
    /// `ScheduleItemManager.Load`: restores every account row once at startup
    /// ([`schedule_item_persistence::load_accounts`]). Loading the whole table up front means a
    /// login never awaits the database for its progress and no account can race a lazy load; a
    /// load error propagates, so startup fails instead of serving empty progress.
    pub async fn load(pool: &MySqlPool) -> Result<Self> {
        Ok(Self::from_accounts(
            schedule_item_persistence::load_accounts(pool).await?,
        ))
    }

    /// The already-loaded map (startup and tests; an empty manager is the `Default`).
    pub(crate) fn from_accounts(accounts: BTreeMap<u32, AccountScheduleProgress>) -> Self {
        Self {
            progress: Mutex::new(accounts),
            sent: Mutex::new(BTreeMap::new()),
        }
    }

    /// Startup: replaces the (empty) map with the saved rows. The manager lives in the
    /// `Default`-built subsystem state, so the load fills it in place
    /// ([`schedule_item_persistence::load_accounts`]); an error propagates and fails startup.
    pub async fn restore(&self, pool: &MySqlPool) -> Result<usize> {
        let accounts = schedule_item_persistence::load_accounts(pool).await?;
        let rows = accounts.len();
        *self.lock() = accounts;
        Ok(rows)
    }

    /// The rows last sent to `character_id` (`None` before the first send).
    pub fn last_sent(&self, character_id: u32) -> Option<Vec<ScheduleItem>> {
        self.sent
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .get(&character_id)
            .cloned()
    }

    /// Records the rows just sent to `character_id`.
    pub fn record_sent(&self, character_id: u32, rows: Vec<ScheduleItem>) {
        self.sent
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .insert(character_id, rows);
    }

    /// Forgets the last-sent rows of a character that left the world.
    pub fn forget_sent(&self, character_id: u32) {
        self.sent
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .remove(&character_id);
    }

    /// The short lock accessor for one synchronous state operation: lock, read or mutate, drop.
    ///
    /// The guard is a `std::sync::MutexGuard` and must not live across an `await`. The later
    /// manager tick / claim operation takes it under the economy `ops_lock`, like every mutation
    /// that must land on the same side of the financial snapshot boundary as its granted items.
    pub fn lock(&self) -> MutexGuard<'_, BTreeMap<u32, AccountScheduleProgress>> {
        self.progress.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// The owned snapshot of every account's dirty rows (`ScheduleItemManager.PrepareSave`): only
    /// accounts with dirty rows, each with the row states and mark stamps captured under the same
    /// lock. The financial snapshot calls this under the economy `ops_lock`, so the counters
    /// written later are exactly the ones of the granted items / mails boundary.
    pub fn prepare_save(&self) -> PreparedScheduleSave {
        let accounts = self.lock();
        let mut prepared = Vec::new();
        for (account_id, account) in accounts.iter() {
            let mut rows = Vec::new();
            let mut marks = Vec::new();
            for (schedule_id, stamp) in account.dirty_rows() {
                if let Some(state) = account.state(schedule_id) {
                    rows.push((schedule_id, *state));
                    marks.push((schedule_id, stamp));
                }
            }
            if rows.is_empty() {
                continue;
            }
            prepared.push(PreparedAccountSave {
                account_id: *account_id,
                rows,
                marks,
            });
        }
        PreparedScheduleSave { accounts: prepared }
    }

    /// Writes a prepared snapshot inside the caller's transaction and returns the receipt for
    /// [`Self::commit_save`]. Only the supplied snapshot is written — the live map is never read
    /// — and every statement error propagates, so the caller can roll the transaction back and the
    /// rows stay dirty for the next save.
    pub async fn write_prepared(
        tx: &mut Transaction<'_, MySql>,
        batch: PreparedScheduleSave,
    ) -> Result<ScheduleSaveToken> {
        let mut token = ScheduleSaveToken::of_snapshot(&batch);
        token.written = schedule_item_persistence::write_rows(tx, &batch).await?;
        Ok(token)
    }

    /// Acknowledges a committed write (`ScheduleItemManager.ConfirmSaved`): only the accounts and
    /// marks whose stamp still matches the snapshot are cleared; a row changed after the snapshot
    /// stays dirty. Call it **after** the outer transaction committed — a failed or rolled-back
    /// write must drop the receipt instead.
    pub fn commit_save(&self, token: ScheduleSaveToken) {
        let mut accounts = self.lock();
        for (account_id, marks) in &token.accounts {
            if let Some(account) = accounts.get_mut(account_id) {
                account.mark_saved(marks);
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::systems::npcs::schedule_item_runtime::AccountScheduleState;
    use crate::systems::npcs::schedule_items::{
        ScheduleEligibility, ScheduleItemData, ScheduleItemDef, ScheduleItemKind,
    };

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

    #[test]
    fn restored_counters_keep_their_seconds_and_an_idle_clock() {
        let day = ts("2026-09-06 01:00:00");
        let saved = AccountScheduleState {
            gave: 2,
            cumulated: 900,
            updated: day,
        };
        let previous = AccountScheduleState {
            gave: 0,
            cumulated: 0,
            updated: day - 10,
        };
        let manager = ScheduleItemManager::from_accounts(BTreeMap::from([(
            7,
            AccountScheduleProgress::from_saved(7, [(1, saved), (4, previous)]),
        )]));

        {
            let accounts = manager.lock();
            let account = accounts.get(&7).expect("restored account");
            assert_eq!(account.account_id(), 7);
            assert_eq!(account.state(1), Some(&saved));
            assert_eq!(account.state(4), Some(&previous));
            // A load restores the counters and their seconds but no clock and no session: time
            // spent offline is never credited.
            assert_eq!(account.account_clock(), 0);
            assert_eq!(account.online_characters(), 0);
            assert!(account.dirty_rows().is_empty());
        }
        // A load is not a change: nothing is dirty, so a save has nothing to write.
        assert!(manager.prepare_save().is_empty());
    }

    #[test]
    fn two_characters_share_the_accounts_rows_and_one_clock() {
        let data = ScheduleItemData::from_rows(vec![live(1), live(4)]);
        let none = ScheduleEligibility::default();
        let start = ts("2026-09-06 01:00:00");
        let mut account = AccountScheduleProgress::new(7);
        // Two characters of the same login account: the second join leaves the running clock
        // alone, and both leave share the one row set.
        account.join(11, start);
        account.join(12, start + 10);
        account.tick(start + 60, &data, &none);
        account.leave(11);
        account.tick(start + 120, &data, &none);
        let manager = ScheduleItemManager::from_accounts(BTreeMap::from([(7, account)]));

        let snapshot = manager.prepare_save();
        // One account and one row per schedule id — never one row set per character.
        assert_eq!(snapshot.accounts.len(), 1);
        assert_eq!(snapshot.accounts[0].account_id, 7);
        assert_eq!(
            snapshot.accounts[0].rows,
            [
                (
                    1,
                    AccountScheduleState {
                        gave: 0,
                        cumulated: 120,
                        updated: start + 120,
                    },
                ),
                (
                    4,
                    AccountScheduleState {
                        gave: 0,
                        cumulated: 120,
                        updated: start + 120,
                    },
                ),
            ]
        );
        assert_eq!(snapshot.row_count(), 2);
        assert_eq!(snapshot.accounts[0].marks.len(), 2);
    }

    #[test]
    fn acknowledging_a_snapshot_clears_only_unchanged_stamps() {
        let data = ScheduleItemData::from_rows(vec![live(1)]);
        let none = ScheduleEligibility::default();
        let start = ts("2026-09-06 01:00:00");
        let mut account = AccountScheduleProgress::new(7);
        account.join(1, start);
        account.tick(start + 60, &data, &none);
        let manager = ScheduleItemManager::from_accounts(BTreeMap::from([(7, account)]));

        // The snapshot is owned: a later tick changes the live row, not this value.
        let first = manager.prepare_save();
        assert_eq!(first.accounts[0].rows[0].1.cumulated, 60);
        {
            let mut accounts = manager.lock();
            accounts
                .get_mut(&7)
                .expect("account")
                .tick(start + 120, &data, &none);
        }
        assert_eq!(first.accounts[0].rows[0].1.cumulated, 60);

        // A retry before any acknowledgement returns the same, newer dirty snapshot.
        let second = manager.prepare_save();
        assert_eq!(second.accounts[0].rows[0].1.cumulated, 120);

        // Acknowledging the older snapshot (what a rolled-back or failed write leaves behind)
        // keeps the newer accrual dirty: only stamps that still match are cleared.
        manager.commit_save(ScheduleSaveToken::of_snapshot(&first));
        let third = manager.prepare_save();
        assert_eq!(third, second);

        // Acknowledging the latest snapshot clears it; nothing is left to save.
        manager.commit_save(ScheduleSaveToken::of_snapshot(&third));
        assert!(manager.prepare_save().is_empty());
    }
}
