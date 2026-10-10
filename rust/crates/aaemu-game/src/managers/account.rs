//! Connection bookkeeping half of `AccountManager` (`Add`, `Remove`, `Contains`, `Count`).
//! The account-details queries live in [`crate::db`]. Timed rewards (daily login, offline labor)
//! and the dead-connection sweeper are later phases.

use dashmap::DashMap;

#[derive(Debug, Default)]
pub struct AccountManager {
    /// account id -> game connection id (`_accounts`).
    accounts: DashMap<u32, u32>,
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
}
