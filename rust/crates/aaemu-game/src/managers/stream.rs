//! Token half of `StreamManager` (`AddToken` / `RemoveToken`). The stream server itself
//! (`StreamNetwork`, CT/TC packets: doodads, UCC, emblems) is a later phase.

use dashmap::DashMap;

#[derive(Debug, Default)]
pub struct StreamManager {
    /// game connection id -> account id.
    tokens: DashMap<u32, u32>,
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
}
