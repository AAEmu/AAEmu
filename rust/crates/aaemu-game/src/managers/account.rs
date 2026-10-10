//! Connection bookkeeping half of `AccountManager` (`Add`, `Remove`, `Contains`, `Count`) and
//! the per-account locks (`_locks`). The account-details queries live in [`crate::db`]; the
//! full `Add` (timed rewards), the credits / loyalty / labor / divine-clock updates and the
//! dead-connection sweeper are in `systems::services::accounts`.

use std::sync::Arc;

use dashmap::DashMap;

#[derive(Debug, Default)]
pub struct AccountManager {
    /// account id -> game connection id (`_accounts`).
    accounts: DashMap<u32, u32>,
    /// `_locks` (account id -> lock serialising that account's DB updates).
    locks: DashMap<u32, Arc<tokio::sync::Mutex<()>>>,
}

impl AccountManager {
    /// Registers the account; returns `false` if it was already present (C# `Add` then returns
    /// early and skips the login-time update).
    pub fn try_add(&self, account_id: u32, connection_id: u32) -> bool {
        match self.accounts.entry(account_id) {
            dashmap::mapref::entry::Entry::Occupied(_) => false,
            dashmap::mapref::entry::Entry::Vacant(v) => {
                v.insert(connection_id);
                true
            }
        }
    }

    /// `Remove(id)`. Note: C# removes the account by id, even if the entry belongs to another
    /// connection of the same account (e.g. a connection whose token check failed).
    pub fn remove(&self, account_id: u32) {
        self.accounts.remove(&account_id);
    }

    pub fn contains(&self, account_id: u32) -> bool {
        self.accounts.contains_key(&account_id)
    }

    pub fn count(&self) -> usize {
        self.accounts.len()
    }

    /// `_accounts` as (account id, connection id) pairs.
    pub fn entries(&self) -> Vec<(u32, u32)> {
        self.accounts
            .iter()
            .map(|e| (*e.key(), *e.value()))
            .collect()
    }

    /// The lock object of `account_id` (`lock (_locks) { ... }` + `lock (accLock)`).
    pub fn lock_of(&self, account_id: u32) -> Arc<tokio::sync::Mutex<()>> {
        self.locks.entry(account_id).or_default().clone()
    }
}
