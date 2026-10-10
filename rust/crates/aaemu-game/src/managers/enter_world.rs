//! Lobby half of `EnterWorldManager` (`AddAccount`, the token check of `Login`,
//! `Leave(ServerSelect)`). The in-world leave path (`LeaveWorldTask`, logout timers) is a later
//! phase.

use std::collections::HashMap;
use std::sync::Mutex;

/// Outcome of [`EnterWorldManager::login`].
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum LoginDecision {
    /// The token was registered for this account; the entry has been consumed.
    Accepted,
    /// The token was registered for another account ("phishing attempt?"); the entry is kept.
    AccountMismatch { expected: u32 },
    /// Unknown token ("hacking attempt?").
    InvalidToken,
}

/// Outcome of [`EnterWorldManager::add_account`].
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AddAccountResult {
    /// Send `GLPlayerEnterPacket` with this `result` byte.
    Reply(u8),
    /// The connection id was already registered for `stale_account` (a client that got its
    /// world cookie but never connected to the game server). The login server reuses a
    /// connection id only after that connection closed, so the old entry is replaced; reply 0.
    /// (C# `Dictionary.Add` throws there, no reply is sent and the login link breaks: every
    /// later login on the reused id is denied.)
    ReplacedStale { stale_account: u32 },
}

#[derive(Debug, Default)]
pub struct EnterWorldManager {
    /// `_accounts`: login-server connection id (the client's cookie / token) -> account id.
    accounts: Mutex<HashMap<u32, u32>>,
}

impl EnterWorldManager {
    /// `AddAccount` bookkeeping. `already_connected` is `accountManager.Contains(accountId)`:
    /// such accounts are refused with result 1 and not registered; otherwise the cookie is
    /// registered and result 0 is returned.
    pub fn add_account(
        &self,
        account_id: u32,
        connection_id: u32,
        already_connected: bool,
    ) -> AddAccountResult {
        if already_connected {
            return AddAccountResult::Reply(1);
        }
        let mut accounts = self.accounts.lock().expect("enter world state poisoned");
        match accounts.insert(connection_id, account_id) {
            Some(stale_account) => AddAccountResult::ReplacedStale { stale_account },
            None => AddAccountResult::Reply(0),
        }
    }

    /// The token check of `Login(connection, accountId, token)`.
    pub fn login(&self, account_id: u32, token: u32) -> LoginDecision {
        let mut accounts = self.accounts.lock().expect("enter world state poisoned");
        match accounts.get(&token).copied() {
            Some(a) if a == account_id => {
                accounts.remove(&token);
                LoginDecision::Accepted
            }
            Some(expected) => LoginDecision::AccountMismatch { expected },
            None => LoginDecision::InvalidToken,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn add_and_login_follow_csharp() {
        let m = EnterWorldManager::default();
        assert_eq!(m.add_account(10, 111, false), AddAccountResult::Reply(0));
        // Contains(accountId): refused, nothing registered.
        assert_eq!(m.add_account(11, 112, true), AddAccountResult::Reply(1));
        // A reused connection id replaces the stale entry (C#: Dictionary.Add throws).
        assert_eq!(
            m.add_account(12, 111, false),
            AddAccountResult::ReplacedStale { stale_account: 10 }
        );
        assert_eq!(
            m.login(10, 111),
            LoginDecision::AccountMismatch { expected: 12 }
        );
        assert_eq!(
            m.add_account(10, 111, false),
            AddAccountResult::ReplacedStale { stale_account: 12 }
        );
        // Wrong account for a known token: refused, entry kept.
        assert_eq!(
            m.login(99, 111),
            LoginDecision::AccountMismatch { expected: 10 }
        );
        assert_eq!(m.login(11, 112), LoginDecision::InvalidToken);
        assert_eq!(m.login(10, 111), LoginDecision::Accepted);
        // Consumed: a second connection with the same token is refused.
        assert_eq!(m.login(10, 111), LoginDecision::InvalidToken);
        assert_eq!(m.login(10, 0), LoginDecision::InvalidToken);
    }
}
