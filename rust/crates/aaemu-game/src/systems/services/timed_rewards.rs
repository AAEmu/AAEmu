//! `Core/Managers/TimedRewardsManager` + `Models/Tasks/TimedRewards/TimedRewardsTask`: labor,
//! credits and loyalty ticks for online connections (every minute), the daily login reward and
//! the offline labor granted on login.

use std::sync::{Arc, Weak};
use std::time::Duration;

use chrono::{NaiveDateTime, Utc};
use futures::future::BoxFuture;

use super::accounts::{self, TickKind};
use super::tasks::{GameTask, TaskState};
use crate::net::connection::AccountPayment;
use crate::server::GameServer;
use crate::systems::skills::packets_special::{SCBmPointPacket, SCICSCashPointPacket};
use crate::systems::skills::progress::SCCharacterLaborPowerChangedPacket;
use crate::world::PlayerRef;

/// `MaxLabor`.
pub const MAX_LABOR: i16 = 2000;
/// `MaxLaborPremium`.
pub const MAX_LABOR_PREMIUM: i16 = 5000;

/// `GetMaxLabor(isPremium)`.
pub fn get_max_labor(is_premium: bool) -> i16 {
    if is_premium {
        MAX_LABOR_PREMIUM
    } else {
        MAX_LABOR
    }
}

/// `TimedRewardsTask`.
pub struct TimedRewardsTask {
    state: TaskState,
    server: Weak<GameServer>,
}

impl TimedRewardsTask {
    pub fn new(server: &Arc<GameServer>) -> Arc<Self> {
        Arc::new(Self {
            state: TaskState::new("TimedRewardsTask"),
            server: Arc::downgrade(server),
        })
    }
}

impl GameTask for TimedRewardsTask {
    fn state(&self) -> &TaskState {
        &self.state
    }

    fn execute(&self) {
        if let (Some(server), Ok(h)) =
            (self.server.upgrade(), tokio::runtime::Handle::try_current())
        {
            h.spawn(async move { do_tick(&server).await });
        }
    }

    fn execute_async(self: Arc<Self>) -> BoxFuture<'static, ()> {
        Box::pin(async move {
            if let Some(server) = self.server.upgrade() {
                do_tick(&server).await;
            }
        })
    }
}

/// `Initialize()`: `TimedRewardsTask` every minute (first run after one minute).
pub fn initialize(server: &Arc<GameServer>) {
    let minute = Duration::from_secs(60);
    server.systems.services.tasks.schedule(
        TimedRewardsTask::new(server),
        Some(minute),
        Some(minute),
        -1,
    );
}

fn now() -> NaiveDateTime {
    Utc::now().naive_utc()
}

/// `DoAddLabor(connection, currentLabor, addLabor)`.
async fn do_add_labor(
    server: &GameServer,
    account_id: u32,
    premium: bool,
    active_char: Option<&PlayerRef>,
    current_labor: i16,
    add_labor: i32,
) {
    let max_labor_to_add = (i32::from(get_max_labor(premium)) - i32::from(current_labor)).max(0);
    let add_labor = add_labor.min(max_labor_to_add);
    accounts::update_tick_times(server, account_id, now(), TickKind::Labor).await;
    if add_labor > 0 {
        let new_labor = (i32::from(current_labor) + add_labor) as i16;
        accounts::update_labor(server, account_id, new_labor).await;
        if let Some(p) = active_char {
            p.send(SCCharacterLaborPowerChangedPacket {
                amount: add_labor,
                action: 0,
                point: 0,
                step: 0,
            });
            // Update cache if character was logged in (`InitializeLaborCache`).
            let mut c = p.data();
            c.labor_power = new_labor;
            c.labor_power_modified = Utc::now().timestamp();
        }
    }
}

/// `DoTick()`: labor / credits / loyalty for every connection.
///
/// Deviation: connections without an account (not entered yet) are skipped; the C#
/// `GetAccountDetails(0)` would create an `accounts` row with id 0 for them.
pub async fn do_tick(server: &GameServer) {
    let config = &server.config;
    for handle in server.connections.all() {
        let account_id = handle.account_id();
        if account_id == 0 {
            continue;
        }
        let premium = accounts::connection_payment().premium_state(Utc::now().timestamp());
        let active_char = server.world.character_by_connection(handle.id);
        let details = accounts::get_account_details(server, account_id).await;
        let due = |last: Option<NaiveDateTime>, minutes: i32| {
            last.unwrap_or_default_min() + chrono::Duration::minutes(i64::from(minutes)) <= now()
        };

        // Distribute Labor if needed (only for online labor)
        if config.labor.tick_minutes > 0 && due(details.last_labor_tick, config.labor.tick_minutes)
        {
            let add = config.labor.get_tick_amount(premium);
            do_add_labor(
                server,
                account_id,
                premium,
                active_char.as_ref(),
                details.labor,
                add,
            )
            .await;
        }

        // Distribute Credits if needed
        if config.credits.tick_minutes > 0
            && due(details.last_credits_tick, config.credits.tick_minutes)
        {
            accounts::add_credits(server, account_id, config.credits.get_tick_amount(premium))
                .await;
            accounts::update_tick_times(server, account_id, now(), TickKind::Credits).await;
            if let Some(p) = &active_char {
                let credits = accounts::get_account_details(server, account_id)
                    .await
                    .credits;
                p.send(SCICSCashPointPacket { point: credits });
            }
        }

        // Distribute Loyalty if needed
        if config.loyalty.tick_minutes > 0
            && due(details.last_loyalty_tick, config.loyalty.tick_minutes)
        {
            accounts::add_loyalty(server, account_id, config.loyalty.get_tick_amount(premium))
                .await;
            accounts::update_tick_times(server, account_id, now(), TickKind::Loyalty).await;
            if let Some(p) = &active_char {
                let loyalty = accounts::get_account_details(server, account_id)
                    .await
                    .loyalty;
                p.send(SCBmPointPacket {
                    bm_point: i64::from(loyalty),
                });
            }
        }
    }
}

/// `DoDailyAccountLogin(accountId)`.
pub async fn do_daily_account_login(server: &GameServer, account_id: u32) {
    let config = &server.config;
    if config.credits.daily_login > 0 {
        accounts::add_credits(server, account_id, config.credits.daily_login).await;
    }
    if config.loyalty.daily_login > 0 {
        accounts::add_loyalty(server, account_id, config.loyalty.daily_login).await;
    }
    accounts::update_divine_clock(server, account_id, 0, 0).await;
}

/// The number of `LaborOffline` ticks since `last_login` (`(int)Math.Floor(minutes /
/// TickMinutes)`, saturating like the .NET 9 conversion; `None` = `DateTime.MinValue`).
pub fn offline_ticks(
    last_login: Option<NaiveDateTime>,
    now: NaiveDateTime,
    tick_minutes: i32,
) -> i32 {
    let delta = now - last_login.unwrap_or_default_min();
    let minutes = delta.num_milliseconds() as f64 / 60_000.0;
    (minutes / f64::from(tick_minutes)).floor() as i32
}

/// `AddOfflineLabor(connection, lastLoginTime, currentLabor)` (the connection has no active
/// character yet when `AccountManager.Add` runs).
pub async fn add_offline_labor(
    server: &GameServer,
    account_id: u32,
    payment: &AccountPayment,
    last_login: Option<NaiveDateTime>,
    current_labor: i16,
) {
    let config = &server.config.labor_offline;
    let ticks_to_add = offline_ticks(last_login, now(), config.tick_minutes);
    if ticks_to_add <= 0 {
        return;
    }
    let premium = payment.premium_state(Utc::now().timestamp());
    let add_labor = config.get_tick_amount(premium).wrapping_mul(ticks_to_add);
    // `connection.ActiveChar` is always null here (called from `X2EnterWorld`).
    do_add_labor(server, account_id, premium, None, current_labor, add_labor).await;
}

/// `DateTime.MinValue` for unset DATETIME columns.
trait OrMinValue {
    fn unwrap_or_default_min(self) -> NaiveDateTime;
}

impl OrMinValue for Option<NaiveDateTime> {
    fn unwrap_or_default_min(self) -> NaiveDateTime {
        self.unwrap_or_else(crate::db::min_value)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn at(h: u32, m: u32) -> NaiveDateTime {
        chrono::NaiveDate::from_ymd_opt(2026, 1, 2)
            .unwrap()
            .and_hms_opt(h, m, 0)
            .unwrap()
    }

    #[test]
    fn max_labor() {
        assert_eq!(get_max_labor(true), 5000);
        assert_eq!(get_max_labor(false), 2000);
    }

    #[test]
    fn offline_tick_count() {
        assert_eq!(offline_ticks(Some(at(10, 0)), at(10, 4), 5), 0);
        assert_eq!(offline_ticks(Some(at(10, 0)), at(10, 5), 5), 1);
        assert_eq!(offline_ticks(Some(at(10, 0)), at(11, 2), 5), 12);
        // Never logged in: saturates.
        assert!(offline_ticks(None, at(10, 0), 5) > 200_000_000);
        // TickMinutes 0: +Infinity -> int.MaxValue.
        assert_eq!(offline_ticks(Some(at(10, 0)), at(10, 1), 0), i32::MAX);
    }

    #[test]
    fn default_payment_is_premium_until_2030() {
        let p = AccountPayment::default();
        assert!(p.premium_state(1_800_000_000));
        assert!(!p.premium_state(1_900_000_000));
    }
}
