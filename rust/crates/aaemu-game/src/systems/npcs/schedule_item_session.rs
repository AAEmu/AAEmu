//! The world side of the schedule items (the event timer bar): the entry / leave sessions, the
//! periodic accrual tick, the `SCScheduleItemUpdatePacket` (0x1eb) sends and the take path that
//! answers with `SCScheduleItemSentPacket` (0x151).
//!
//! The state transitions are [`super::schedule_item_runtime`] and the map / save handshake is
//! [`super::schedule_item_manager`]; this file only connects them to the connected characters.
//!
//! # What is sent, and when
//!
//! * World entry ([`on_enter`], from `CSNotifyInGame`): the character joins its account's session
//!   set and receives the full row list ([`AccountScheduleProgress::build_update_rows`]).
//! * Every [`TICK_SECONDS`] ([`tick`]): the account clock is credited to every available row, and
//!   a character whose list differs from the one it last received gets the new list. That one
//!   comparison covers progress, a window opening or closing (a row appears or disappears), a
//!   daily reset and a receipt made through another character of the same account.
//! * A take ([`take`]): the answer `SCScheduleItemSentPacket` goes to the taker, then the new list
//!   to every online character of the account. A refused take only re-sends the list, so the
//!   client's view is corrected.
//!
//! # Locking
//!
//! Every mutation of the progress map runs under the economy `ops_lock` (an [`ItemTxn`] for the
//! take, the bare gate for the tick), like every mutation that must land on one side of the
//! financial snapshot boundary; the map mutex is only ever taken inside it, never the other way
//! round (`capture_financial_snapshot` uses the same order). No character lock is held across a
//! container, mail or map call, and every packet is sent after the gate is released.
//!
//! # Exactly-once
//!
//! A take reserves with [`AccountScheduleProgress::plan_claim`], hands the reward over, and only
//! then records the receipt with [`AccountScheduleProgress::commit_claim`] - all under one gate,
//! so a second take of the same window finds `gave` already advanced and `cumulated = 0` and is
//! refused. A failed hand-over writes no receipt and the term stays ready.
//!
//! # Eligibility
//!
//! `paid_patron` is the connection's payment state ([`accounts::connection_payment`], premium for
//! every account today); the premium grade and PC-bang membership have no 1.2 source, so rows
//! that need a specific grade or a PC-bang are never listed.

use std::sync::Arc;
use std::time::Duration;

use chrono::Utc;

use crate::models::container::{slot_type, ContainerRef};
use crate::models::item_task::task_type;
use crate::packets::g2c_misc::{
    SCScheduleItemSentPacket, SCScheduleItemUpdatePacket, ScheduleItem,
};
use crate::server::GameServer;
use crate::systems::economy::inventory::{Inv, ItemTxn};
use crate::systems::economy::mail::{self, mail_status, mail_type, BaseMail};
use crate::systems::services::accounts;
use crate::world::PlayerRef;

use super::schedule_item_runtime::{AccountScheduleProgress, ClaimCommit, ClaimMode, ClaimPlan};
use super::schedule_items::ScheduleEligibility;

/// Seconds between two accrual ticks. One minute keeps the bar current without a packet per
/// second; the client advances the visible timer between lists on its own.
pub const TICK_SECONDS: u64 = 60;

/// The overflow letter's sender (plain 1.2 text, see `achievement_rewards::OVERFLOW_MAIL_SENDER`
/// for why it is not a locale lookup).
pub const OVERFLOW_MAIL_SENDER: &str = "Event Rewards";

/// The overflow letter's title.
pub const OVERFLOW_MAIL_TITLE: &str = "Event Reward";

/// The overflow letter's body.
pub fn overflow_mail_body(schedule_id: i32) -> String {
    format!("Reward for event {schedule_id}.")
}

/// What a [`take`] did.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TakeOutcome {
    /// The reward was handed over (`by_mail`: it did not fit the bag) and the receipt recorded.
    Granted { by_mail: bool },
    /// Nothing was granted: unknown or silent row, closed window, ineligible, term not ready,
    /// allowance used up, or the reward could not be handed over (the term stays ready).
    Refused,
}

/// What the claim path knows about the account (see the module documentation).
pub fn eligibility(now: i64) -> ScheduleEligibility {
    ScheduleEligibility {
        paid_patron: accounts::connection_payment().premium_state(now),
        premium_grade_id: 0,
        pc_bang: false,
    }
}

/// A character entered the world: joins the account session and sends the full list.
pub fn on_enter(server: &GameServer, player: &PlayerRef) {
    let account_id = player.data().account_id;
    let now = Utc::now().timestamp();
    let evidence = eligibility(now);
    let manager = &server.systems.npcs.schedule_items;
    let rows = {
        let mut map = manager.lock();
        let account = map
            .entry(account_id)
            .or_insert_with(|| AccountScheduleProgress::new(account_id));
        account.join(player.character_id, now);
        account.build_update_rows(
            &server.game_data.systems.npcs.schedule_items,
            now,
            &evidence,
        )
    };
    if rows.is_empty() {
        // Nothing to show on entry (no event is on air): the client starts with an empty bar, so
        // no packet is needed; the empty list is still remembered so the tick only sends a change.
        manager.record_sent(player.character_id, rows);
        return;
    }
    send_rows(server, player, rows);
}

/// A character left the world (logout, disconnect): credits the time since the last tick, then
/// ends its session so the offline gap is never credited.
pub fn on_leave(server: &GameServer, player: &PlayerRef) {
    let account_id = player.data().account_id;
    let now = Utc::now().timestamp();
    let evidence = eligibility(now);
    let manager = &server.systems.npcs.schedule_items;
    {
        let _gate = server.systems.economy.ops_lock();
        let mut map = manager.lock();
        if let Some(account) = map.get_mut(&account_id) {
            if account.is_online(player.character_id) {
                account.tick(
                    now,
                    &server.game_data.systems.npcs.schedule_items,
                    &evidence,
                );
                account.leave(player.character_id);
            }
        }
    }
    manager.forget_sent(player.character_id);
}

/// One accrual pass over every account with an online character; re-sends the list to every
/// character whose list changed. Returns how many packets were sent.
pub fn tick(server: &GameServer) -> usize {
    let now = Utc::now().timestamp();
    let evidence = eligibility(now);
    let data = &server.game_data.systems.npcs.schedule_items;
    let manager = &server.systems.npcs.schedule_items;

    let mut due: Vec<(u32, Vec<ScheduleItem>)> = Vec::new();
    {
        let _gate = server.systems.economy.ops_lock();
        let mut map = manager.lock();
        for account in map.values_mut() {
            let online = account.online_character_ids();
            if online.is_empty() {
                continue;
            }
            account.tick(now, data, &evidence);
            let rows = account.build_update_rows(data, now, &evidence);
            due.extend(online.into_iter().map(|id| (id, rows.clone())));
        }
    }

    let mut sent = 0;
    for (character_id, rows) in due {
        if manager.last_sent(character_id).as_ref() == Some(&rows) {
            continue;
        }
        if let Some(player) = server.world.character_by_id(character_id) {
            send_rows(server, &player, rows);
            sent += 1;
        }
    }
    sent
}

/// Starts the periodic accrual task (`TICK_SECONDS`).
pub fn start_tick_task(server: &Arc<GameServer>) {
    let weak = Arc::downgrade(server);
    tokio::spawn(async move {
        let period = Duration::from_secs(TICK_SECONDS);
        let mut interval = tokio::time::interval_at(tokio::time::Instant::now() + period, period);
        loop {
            interval.tick().await;
            let Some(server) = weak.upgrade() else { break };
            tick(&server);
        }
    });
}

/// Sends `rows` to `player` and remembers them as its current list.
fn send_rows(server: &GameServer, player: &PlayerRef, rows: Vec<ScheduleItem>) {
    server
        .systems
        .npcs
        .schedule_items
        .record_sent(player.character_id, rows.clone());
    player.send(SCScheduleItemUpdatePacket {
        schedule_items: rows,
    });
}

/// Re-sends the current list to every online character of `account_id`.
fn resend_account(server: &GameServer, account_id: u32) {
    let now = Utc::now().timestamp();
    let evidence = eligibility(now);
    let (online, rows) = {
        let map = server.systems.npcs.schedule_items.lock();
        let Some(account) = map.get(&account_id) else {
            return;
        };
        (
            account.online_character_ids(),
            account.build_update_rows(
                &server.game_data.systems.npcs.schedule_items,
                now,
                &evidence,
            ),
        )
    };
    for character_id in online {
        if let Some(player) = server.world.character_by_id(character_id) {
            send_rows(server, &player, rows.clone());
        }
    }
}

/// `CSTakeScheduleItem`: the player takes the reward of schedule row `schedule_id`.
///
/// Callers must not hold the character lock, an `ItemTxn`, the economy `ops_lock` or a container
/// lock (`ops_lock` is not re-entrant).
pub fn take(server: &GameServer, player: &PlayerRef, schedule_id: i32) -> TakeOutcome {
    let account_id = player.data().account_id;
    let outcome = deliver(server, player, account_id, schedule_id);
    // Every packet goes out after the gate, the map and the containers are released.
    match outcome {
        TakeOutcome::Granted { by_mail } => {
            if let Ok(item_template_id) = u32::try_from(schedule_id) {
                player.send(SCScheduleItemSentPacket {
                    item_template_id,
                    by_mail,
                });
            }
        }
        TakeOutcome::Refused => {}
    }
    // Granted: the new counters; refused: corrects whatever the client believed.
    resend_account(server, account_id);
    outcome
}

/// Reserve, hand over, record - one gate.
fn deliver(
    server: &GameServer,
    player: &PlayerRef,
    account_id: u32,
    schedule_id: i32,
) -> TakeOutcome {
    let now = Utc::now().timestamp();
    let evidence = eligibility(now);
    let data = &server.game_data.systems.npcs.schedule_items;
    let manager = &server.systems.npcs.schedule_items;

    let mut txn = ItemTxn::begin(server);

    // Bring the accrual up to `now` first: a take right after the visible timer filled must not
    // lose to the next periodic tick.
    let plan = {
        let mut map = manager.lock();
        let Some(account) = map.get_mut(&account_id) else {
            return TakeOutcome::Refused;
        };
        account.tick(now, data, &evidence);
        account.plan_claim(
            data,
            schedule_id,
            player.character_id,
            ClaimMode::Manual,
            now,
            &evidence,
        )
    };
    let Some(plan) = plan else {
        return TakeOutcome::Refused;
    };

    if server.game_data.items.template(plan.item_id).is_none() {
        tracing::warn!(
            schedule_id,
            item_id = plan.item_id,
            "schedule item reward has no template; take refused"
        );
        return TakeOutcome::Refused;
    }

    let Some(by_mail) = hand_over(&mut txn, player, &plan) else {
        tracing::warn!(
            schedule_id,
            item_id = plan.item_id,
            "schedule item reward could not be handed over; take refused"
        );
        return TakeOutcome::Refused;
    };

    // The receipt is written only after the reward exists, and before the gate is released.
    let commit = {
        let mut map = manager.lock();
        map.get_mut(&account_id)
            .map(|account| account.commit_claim(data, &plan, now))
    };
    if !matches!(commit, Some(ClaimCommit::Committed { .. })) {
        // Unreachable under the gate (the plan was made inside it). The reward is out for good:
        // never roll it back, and say so.
        tracing::error!(
            schedule_id,
            by_mail,
            "schedule item reward handed over but the receipt was refused"
        );
    }
    TakeOutcome::Granted { by_mail }
}

/// The bag when the whole reward fits, otherwise one `SYS_EXPRESS` letter. `None`: nothing was
/// handed over.
fn hand_over(txn: &mut ItemTxn<'_>, player: &PlayerRef, plan: &ClaimPlan) -> Option<bool> {
    let server = txn.server;
    let inventory = Inv::of(player);
    let bag = inventory.bag();
    let money = player.data().money;
    let has_room = bag
        .lock()
        .expect("container poisoned")
        .space_left_for_template(plan.item_id, &server.game_data.items, money)
        >= plan.item_count;
    if has_room && acquire_in_bag(txn, &bag, plan) {
        Some(false)
    } else if acquire_in_mail(txn, player, &inventory, plan) {
        Some(true)
    } else {
        None
    }
}

/// `bag.AcquireDefaultItem(SkillEffectGainItem, itemId, count)`; see
/// `achievement_rewards::acquire_in_bag` for the task type.
fn acquire_in_bag(txn: &mut ItemTxn<'_>, bag: &ContainerRef, plan: &ClaimPlan) -> bool {
    bag.lock()
        .expect("container poisoned")
        .acquire_default_item(
            task_type::SKILL_EFFECT_GAIN_ITEM,
            plan.item_id,
            plan.item_count,
            -1,
            0,
            &mut txn.env,
        )
}

/// Stages the reward in the mail escrow and publishes it as one `SYS_EXPRESS` letter. A failed
/// send removes the staged items again, so a retry cannot leak or duplicate the reward.
fn acquire_in_mail(
    txn: &mut ItemTxn<'_>,
    player: &PlayerRef,
    inventory: &Inv,
    plan: &ClaimPlan,
) -> bool {
    let Some(escrow) = inventory.get(slot_type::MAIL) else {
        tracing::warn!(
            schedule_id = plan.schedule_id,
            "schedule item reward has no mail escrow container"
        );
        return false;
    };

    let staged = {
        let mut escrow = escrow.lock().expect("container poisoned");
        match escrow.acquire_default_item_ex(
            task_type::INVALID,
            plan.item_id,
            plan.item_count,
            -1,
            player.character_id,
            -1,
            &mut txn.env,
        ) {
            Some(result) if !result.new.is_empty() => result.new,
            _ => return false,
        }
    };

    let Some(receiver_name) = txn.server.names.get_character_name(player.character_id) else {
        roll_back_staged(txn, &escrow, &staged);
        return false;
    };

    let now = mail::now();
    let mut letter = BaseMail::new();
    letter.mail_type = mail_type::SYS_EXPRESS;
    letter.title = OVERFLOW_MAIL_TITLE.to_string();
    letter.receiver_name = receiver_name;
    letter.header.sender_id = 0;
    letter.header.sender_name = OVERFLOW_MAIL_SENDER.to_string();
    letter.header.receiver_id = player.character_id;
    letter.header.status = mail_status::UNREAD;
    letter.body.text = overflow_mail_body(plan.schedule_id);
    letter.body.send_date = now;
    letter.body.recv_date = now;
    letter.body.attachments = staged.clone();

    if mail::send(txn, letter) {
        return true;
    }
    roll_back_staged(txn, &escrow, &staged);
    false
}

/// `MailDeliveryRules.TryDiscardStagedAttachments`.
fn roll_back_staged(txn: &mut ItemTxn<'_>, escrow: &ContainerRef, staged: &[u64]) {
    let mut escrow = escrow.lock().expect("container poisoned");
    for item_id in staged {
        if escrow
            .remove_item_task(task_type::INVALID, *item_id, true, &mut txn.env)
            .is_none()
        {
            tracing::error!(
                item_id,
                "schedule item reward: staged item could not be rolled back"
            );
        }
    }
}
