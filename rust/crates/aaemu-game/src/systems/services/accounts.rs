//! `Core/Managers/AccountManager` remainder: `Add` (login time + timed rewards), the
//! dead-connection sweep (`Initialize` -> `RemoveDeadConnections` every 30 s) and the account
//! value updates (`AddCredits`, `RemoveCredits`, `AddLoyalty`, `UpdateLabor`,
//! `UpdateTickTimes`, `UpdateDivineClock`, `GetDivineClock`) under the per-account lock.

use std::time::Duration;

use chrono::{NaiveDateTime, Utc};

use crate::db::{self, AccountDetails, AccountPointColumn};
use crate::net::connection::{AccountPayment, GameConnection};
use crate::server::GameServer;

/// `RemoveDeadConnections` interval and ping timeout.
pub const DEAD_CONNECTION_TIMEOUT: Duration = Duration::from_secs(30);

/// `AccountManager.Add(connection)`: registers the account, updates `last_login`, grants the
/// daily login reward on a new (UTC) day and the offline labor.
pub async fn add(server: &GameServer, conn: &GameConnection) {
    let account_id = conn.account_id();
    if !server.accounts.try_add(account_id, conn.id) {
        return;
    }
    let now = Utc::now().naive_utc();
    let last_login = update_login_time(server, account_id, now).await;
    let details = get_account_details(server, account_id).await;
    let today = now.date().and_hms_opt(0, 0, 0).expect("midnight");
    if last_login.is_none_or(|l| l < today) {
        // Logged in for a new day
        super::timed_rewards::do_daily_account_login(server, account_id).await;
    }
    super::timed_rewards::add_offline_labor(
        server,
        account_id,
        &conn.payment,
        last_login,
        details.labor,
    )
    .await;
}

/// `AccountManager.Initialize`: `RemoveDeadConnections` on the tick manager every 30 s.
pub fn initialize(server: &std::sync::Arc<GameServer>) {
    let weak = std::sync::Arc::downgrade(server);
    server.systems.services.tick.on_tick.subscribe(
        move |_| {
            if let Some(server) = weak.upgrade() {
                remove_dead_connections(&server, Utc::now().naive_utc());
            }
        },
        DEAD_CONNECTION_TIMEOUT,
        false,
    );
}

/// `RemoveDeadConnections`: shuts down every registered account connection whose `LastPing`
/// is older than 30 s. A connection that never sent a `PingPacket` has `LastPing =
/// DateTime.MinValue` in C# and is shut down too. Returns the closed connection ids.
pub fn remove_dead_connections(server: &GameServer, now: NaiveDateTime) -> Vec<u32> {
    let timeout = chrono::Duration::from_std(DEAD_CONNECTION_TIMEOUT).expect("30 s");
    let mut closed = Vec::new();
    for (_, conn_id) in server.accounts.entries() {
        let Some(handle) = server.connections.get(conn_id) else {
            continue;
        };
        let alive = handle.last_ping().is_some_and(|p| p + timeout >= now);
        if alive {
            continue;
        }
        if let Some(p) = server.world.character_by_connection(conn_id) {
            tracing::trace!(
                "Disconnecting {} due to no network activity",
                p.data().unit.name
            );
        }
        handle.close();
        closed.push(conn_id);
    }
    closed
}

/// `GetAccountDetails(accountId)` under the account lock.
pub async fn get_account_details(server: &GameServer, account_id: u32) -> AccountDetails {
    let lock = server.accounts.lock_of(account_id);
    let _g = lock.lock().await;
    db::get_account_details(&server.db, &server.config, account_id).await
}

/// `UpdateLoginTime(accountId, newTime)` under the account lock: the previous `last_login`.
pub async fn update_login_time(
    server: &GameServer,
    account_id: u32,
    new_time: NaiveDateTime,
) -> Option<NaiveDateTime> {
    let lock = server.accounts.lock_of(account_id);
    let _g = lock.lock().await;
    db::update_login_time(&server.db, &server.config, account_id, new_time).await
}

/// `AddCredits(accountId, amount)`.
pub async fn add_credits(server: &GameServer, account_id: u32, amount: i32) -> bool {
    let lock = server.accounts.lock_of(account_id);
    let _g = lock.lock().await;
    db::add_account_points(&server.db, AccountPointColumn::Credits, account_id, amount).await
}

/// `RemoveCredits(accountId, credits)`.
pub async fn remove_credits(server: &GameServer, account_id: u32, credits: i32) -> bool {
    add_credits(server, account_id, credits.wrapping_neg()).await
}

/// `AddLoyalty(accountId, amount)`.
pub async fn add_loyalty(server: &GameServer, account_id: u32, amount: i32) -> bool {
    let lock = server.accounts.lock_of(account_id);
    let _g = lock.lock().await;
    db::add_account_points(&server.db, AccountPointColumn::Loyalty, account_id, amount).await
}

/// `UpdateLabor(accountId, laborPower)`.
pub async fn update_labor(server: &GameServer, account_id: u32, labor: i16) {
    let lock = server.accounts.lock_of(account_id);
    let _g = lock.lock().await;
    db::update_labor(&server.db, account_id, labor).await;
}

/// Which tick time [`update_tick_times`] writes (C# passes three flags; the last set wins).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TickKind {
    Labor,
    Credits,
    Loyalty,
}

/// `UpdateTickTimes(accountId, newTime, updateLabor, updateCredits, updateLoyalty)`.
pub async fn update_tick_times(
    server: &GameServer,
    account_id: u32,
    new_time: NaiveDateTime,
    kind: TickKind,
) {
    let lock = server.accounts.lock_of(account_id);
    let _g = lock.lock().await;
    db::update_tick_times(
        &server.db,
        account_id,
        new_time,
        kind == TickKind::Labor,
        kind == TickKind::Credits,
        kind == TickKind::Loyalty,
    )
    .await;
}

/// `UpdateDivineClock(accountId, timeElapsed, timesTaken)`.
pub async fn update_divine_clock(server: &GameServer, account_id: u32, time: u32, taken: u32) {
    let lock = server.accounts.lock_of(account_id);
    let _g = lock.lock().await;
    db::update_divine_clock(&server.db, account_id, time, taken).await;
}

/// `GetDivineClock(accountId)`.
pub async fn get_divine_clock(server: &GameServer, account_id: u32) -> (u32, u32) {
    let lock = server.accounts.lock_of(account_id);
    let _g = lock.lock().await;
    db::get_divine_clock(&server.db, account_id).await
}

/// The payment of a connection reached through its handle: `AccountPayment` is never changed
/// after the connection is created (C# TODO "Load payment and account tier information").
pub fn connection_payment() -> AccountPayment {
    AccountPayment::default()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::test_support::{test_server, TestClient};

    #[tokio::test]
    async fn dead_connection_sweep_closes_silent_connections() {
        let server = test_server();
        let quiet = TestClient::new(&server, 1, 10);
        let mut pinging = TestClient::new(&server, 2, 11);
        assert!(server.accounts.try_add(10, quiet.conn.id));
        assert!(server.accounts.try_add(11, pinging.conn.id));
        pinging.conn.touch_ping();
        let now = Utc::now().naive_utc();
        // Never pinged (`DateTime.MinValue`) -> closed; pinged just now -> kept.
        assert_eq!(remove_dead_connections(&server, now), vec![quiet.conn.id]);
        // 31 s later the pinging one is dead too.
        let later = now + chrono::Duration::seconds(31);
        let mut closed = remove_dead_connections(&server, later);
        closed.sort_unstable();
        let mut expected = vec![quiet.conn.id, pinging.conn.id];
        expected.sort_unstable();
        assert_eq!(closed, expected);
    }
}
