//! Port of `Core/Managers/World/StreamManager` (+ the stream-server state that hangs off it).
//!
//! * Token map: `AddToken` (from `EnterWorldManager.Login`), `RemoveToken` (game disconnect) and
//!   `Login` (stream `CTJoinPacket`).
//! * `RequestCell` / `ContinueCell` (doodad streaming) live in [`crate::stream::cells`], because
//!   they need the per-connection request table.
//! * The stream connection table, the [`UccManager`] and the [`StreamWorld`] hook are owned here
//!   so `GameServer` (which only constructs `StreamManager::default()`) carries all stream state.

use std::sync::{Arc, RwLock};

use dashmap::DashMap;

use crate::stream::connection::StreamConnectionTable;
use crate::stream::ucc::UccManager;
use crate::stream::world::{NullStreamWorld, StreamWorld};

/// Result of [`StreamManager::login`] (`TCJoinResponsePacket` value).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum JoinResult {
    /// Response `0`; `game_connection_id` is the token (the game connection's id).
    Accepted { game_connection_id: u32 },
    /// Response `1`.
    Rejected,
}

impl JoinResult {
    pub fn response(self) -> u8 {
        match self {
            JoinResult::Accepted { .. } => 0,
            JoinResult::Rejected => 1,
        }
    }
}

pub struct StreamManager {
    /// game connection id (the "token" / cookie) -> account id.
    tokens: DashMap<u32, u32>,
    /// `StreamConnectionTable`.
    pub connections: StreamConnectionTable,
    /// `UccManager`.
    pub ucc: UccManager,
    /// World / character / item lookups the stream packets need (see [`StreamWorld`]).
    world: RwLock<Arc<dyn StreamWorld>>,
}

impl Default for StreamManager {
    fn default() -> Self {
        Self {
            tokens: DashMap::new(),
            connections: StreamConnectionTable::default(),
            ucc: UccManager::default(),
            world: RwLock::new(Arc::new(NullStreamWorld)),
        }
    }
}

impl std::fmt::Debug for StreamManager {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("StreamManager")
            .field("tokens", &self.tokens.len())
            .field("connections", &self.connections.len())
            .finish_non_exhaustive()
    }
}

impl StreamManager {
    /// `AddToken(accountId, connectionId)`. C# uses `Dictionary.Add`, which throws when the same
    /// connection enters twice; `false` is returned in that case (the entry is left unchanged) so
    /// the caller can fail the packet like the C# exception does.
    pub fn add_token(&self, account_id: u32, connection_id: u32) -> bool {
        match self.tokens.entry(connection_id) {
            dashmap::mapref::entry::Entry::Occupied(_) => false,
            dashmap::mapref::entry::Entry::Vacant(v) => {
                v.insert(account_id);
                true
            }
        }
    }

    pub fn remove_token(&self, connection_id: u32) {
        self.tokens.remove(&connection_id);
    }

    pub fn account_for(&self, token: u32) -> Option<u32> {
        self.tokens.get(&token).map(|v| *v)
    }

    /// `StreamManager.Login(connection, accountId, token)` minus the packet send.
    ///
    /// * known token, matching account -> accepted (the token is kept, so it can be reused);
    /// * known token, other account -> the token is **removed** (C#: `_accounts.Remove(token)`),
    ///   rejected;
    /// * unknown token -> rejected.
    pub fn login(&self, account_id: u32, token: u32) -> JoinResult {
        match self.account_for(token) {
            Some(expected) if expected == account_id => JoinResult::Accepted {
                game_connection_id: token,
            },
            Some(_) => {
                self.tokens.remove(&token);
                JoinResult::Rejected
            }
            None => JoinResult::Rejected,
        }
    }

    /// Current world hook.
    pub fn world(&self) -> Arc<dyn StreamWorld> {
        self.world.read().expect("stream world lock").clone()
    }

    /// Replaces the world hook (to be called once the world / character / item managers exist).
    pub fn set_world(&self, world: Arc<dyn StreamWorld>) {
        *self.world.write().expect("stream world lock") = world;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn token_flow() {
        let m = StreamManager::default();
        assert!(m.add_token(1000, 7));
        assert!(!m.add_token(1001, 7));
        assert_eq!(m.account_for(7), Some(1000));
        assert_eq!(
            m.login(1000, 7),
            JoinResult::Accepted {
                game_connection_id: 7
            }
        );
        // The token survives a successful join.
        assert_eq!(m.login(1000, 7).response(), 0);
        assert_eq!(m.login(1000, 8), JoinResult::Rejected);
        // Wrong account: rejected and the token is dropped.
        assert_eq!(m.login(999, 7), JoinResult::Rejected);
        assert_eq!(m.account_for(7), None);
        assert_eq!(m.login(1000, 7), JoinResult::Rejected);
        m.add_token(5, 9);
        m.remove_token(9);
        assert_eq!(m.login(5, 9).response(), 1);
    }
}
