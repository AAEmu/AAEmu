//! Achievement reward delivery: hands over what a completed achievement pays and writes the
//! `character_achievements.reward_delivered` receipt, so a completion is paid exactly once.
//!
//! C# scope (later history, `7851f67cc`): the reward half of `Core/Managers/AchievementManager`
//! (`PayReward`, `TryPayItem`, `TryMailItem`, `CompletionRecords` aside) and
//! `Models/Game/Achievement/AchievementRewardRules`. The 1.2 `achievements` row carries only
//! `item_id` — no item count and no appellation — so one default-grade item is the whole reward.
//!
//! What the pass does, per completed achievement that still owes a reward:
//!
//! * `item_id == 0` (the payer rows and the achievements whose reward is only a receipt): write
//!   the receipt, send nothing. The completion is settled and will not be looked at again.
//! * `item_id > 0`: one item of the real template, into the bag when the whole stack fits,
//!   otherwise one `SYS_EXPRESS` system letter with the item staged in the mail escrow — the
//!   client's `SCAchievementItemSentPacket.byMail` distinguishes the two. The bag is written
//!   through the bag container itself, so a backpack reward is never routed through the
//!   equipment container's auto-equip path.
//! * Anything else — an unknown/switched-off row, an item id the loaded templates do not have, a
//!   container that cannot take the item, a letter that cannot be enqueued — writes nothing and
//!   is left pending, so the next pass retries it. A staged item whose letter fails to enqueue is
//!   removed again (`MailDeliveryRules.TryDiscardStagedAttachments`), so a retry cannot leak or
//!   duplicate a reward.
//!
//! # Locking and the receipt
//!
//! * Each achievement is delivered inside one [`ItemTxn`], which takes the economy `ops_lock`:
//!   the receipt, the granted item and the overflow mail then belong to the same snapshot
//!   boundary `SaveManager::capture_financial_snapshot` clones characters under.
//! * Inside the gate the pending row is re-checked, the item is handed over, and only then is
//!   `mark_reward_delivered` called — still before the gate is released. A failed hand-over never
//!   writes the receipt, which is what keeps the reward retryable.
//! * The character lock is taken only for the re-check and the receipt, never across a container,
//!   mail or world call.
//! * `SCAchievementItemSentPacket` is sent after every character / container lock and the
//!   operation gate are released, one per real item reward, in ascending achievement id.
//! * **Callers must not hold the character lock, an `ItemTxn`, the economy `ops_lock` or a
//!   container lock when they call [`deliver_pending`]** (`ops_lock` is not re-entrant).
//!
//! The receipt is one-way: there is no API here that re-opens a paid completion, and
//! [`AchievementState::restore_progress`](super::achievement_runtime::AchievementState::restore_progress)
//! keeps the receipt of a row restored again, so a replay or a relog cannot pay twice.

use crate::models::container::{slot_type, ContainerRef};
use crate::models::item_task::task_type;
use crate::server::GameServer;
use crate::systems::economy::inventory::{Inv, ItemTxn};
use crate::systems::economy::mail::{self, mail_status, mail_type, BaseMail};
use crate::world::PlayerRef;

use super::packets::misc::SCAchievementItemSentPacket;

/// The overflow letter's sender. A plain literal 1.2 string — deliberately no leading dot. The
/// leading-dot form is a locale-table lookup, which only renders where the 1.2 client actually
/// ships the table (`mail.rs` uses `.houseTax`); the real 1.2 client has no
/// `locale.mail.achievementReward` entry, so that sender shows as NIL in the mailbox. A literal
/// name is always displayable.
pub const OVERFLOW_MAIL_SENDER: &str = "Achievement Rewards";

/// The overflow letter's title. Plain 1.2 text, not the 10.x `title(id)` locale call: that call is
/// resolved by the 10.x client's `mail.achievementNew` table, which 1.2 does not have.
pub const OVERFLOW_MAIL_TITLE: &str = "Achievement Reward";

/// The overflow letter's body (plain 1.2 text, see [`OVERFLOW_MAIL_TITLE`]).
pub fn overflow_mail_body(achievement_id: u32) -> String {
    format!("Reward for achievement {achievement_id}.")
}

/// One achievement whose item reward was handed over by a pass.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct GrantedReward {
    pub achievement_id: u32,
    /// The item did not fit the bag and was sent as one `SYS_EXPRESS` letter.
    pub by_mail: bool,
}

/// What one [`deliver_pending`] pass did.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct RewardDelivery {
    /// Item rewards handed over, ascending achievement id (one `SCAchievementItemSentPacket`
    /// each).
    pub granted: Vec<GrantedReward>,
    /// Completions settled by the receipt alone (`item_id == 0`), ascending.
    pub settled: Vec<u32>,
    /// Completions still owed a reward when the pass ended, ascending. A pending reward is
    /// retried by the next pass; it is never marked delivered.
    pub pending: Vec<u32>,
}

impl RewardDelivery {
    /// Whether the pass granted or settled anything.
    pub fn delivered(&self) -> bool {
        !self.granted.is_empty() || !self.settled.is_empty()
    }
}

/// One completed achievement that still owes a reward.
struct PendingReward {
    achievement_id: u32,
    item_id: u32,
}

/// What delivering one achievement did.
enum Delivery {
    /// The receipt was written and, for an item reward, the item handed over. The unreachable
    /// refused-receipt anomaly (see [`deliver_one`]) is also a grant: the item is handed over, so
    /// the reward must never be reported as still owed.
    Granted { by_mail: bool },
    /// Completed with nothing to hand over (`item_id == 0`); the receipt was written.
    Settled,
    /// Nothing was written; the reward is still owed.
    Pending,
}

/// Delivers every reward the character's completed achievements still owe.
///
/// The candidates are the completed, active, known rows of
/// [`AchievementState::pending_reward_ids`](super::achievement_runtime::AchievementState::pending_reward_ids).
/// Each is delivered under its own [`ItemTxn`] gate, so one failing reward does not block the
/// others; the failures stay pending and the returned [`RewardDelivery::pending`] is the truth
/// after the pass.
///
/// Ready for the entry path and for a period task; no gameplay hook calls it yet.
pub fn deliver_pending(server: &GameServer, player: &PlayerRef) -> RewardDelivery {
    let mut delivery = RewardDelivery::default();

    for reward in pending_rewards(server, player) {
        match deliver_one(server, player, &reward) {
            Delivery::Granted { by_mail } => delivery.granted.push(GrantedReward {
                achievement_id: reward.achievement_id,
                by_mail,
            }),
            Delivery::Settled => delivery.settled.push(reward.achievement_id),
            Delivery::Pending => {}
        }
    }

    // The reward's own notification goes out after every lock (character, container, gate) is
    // released: one packet per real item reward, none for a receipt-only settlement.
    for granted in &delivery.granted {
        player.send(SCAchievementItemSentPacket {
            id: granted.achievement_id,
            by_mail: granted.by_mail,
        });
    }

    delivery.pending = player.data().achievement_state.pending_reward_ids();
    delivery
}

/// The completed rows that owe a reward *and* can be paid: the content has the achievement and it
/// is switched on. An unknown or inactive row is not a delivery candidate (it stays pending).
fn pending_rewards(server: &GameServer, player: &PlayerRef) -> Vec<PendingReward> {
    let content = &server.game_data.systems.social.achievements;
    let character = player.data();
    character
        .achievement_state
        .pending_reward_ids()
        .into_iter()
        .filter_map(|achievement_id| {
            let achievement = content.achievements.get(&achievement_id)?;
            achievement.is_active.then_some(PendingReward {
                achievement_id,
                item_id: achievement.item_id,
            })
        })
        .collect()
}

/// Pays one achievement: re-check under the gate, hand the item over, write the receipt.
fn deliver_one(server: &GameServer, player: &PlayerRef, reward: &PendingReward) -> Delivery {
    // The operation gate. It is released when `txn` drops, after the receipt is written.
    let mut txn = ItemTxn::begin(server);

    // Re-check inside the gate: the row may have been settled between the snapshot and here, and
    // a settled reward must never be paid twice.
    if !player
        .data()
        .achievement_state
        .has_pending_reward(reward.achievement_id)
    {
        return Delivery::Pending;
    }

    // A completion with no item is settled by the receipt alone; there is nothing to hand over.
    if reward.item_id == 0 {
        return if mark_delivered(player, reward.achievement_id) {
            Delivery::Settled
        } else {
            Delivery::Pending
        };
    }

    if server.game_data.items.template(reward.item_id).is_none() {
        tracing::warn!(
            achievement_id = reward.achievement_id,
            item_id = reward.item_id,
            "achievement reward item has no template; left pending"
        );
        return Delivery::Pending;
    }

    // The bag first, like the C# (`SpaceLeftForItem(reward.ItemId) >= count`, count is 1 in 1.2).
    // The container method writes the stack itself, so the equipment container's auto-equip path
    // never sees the reward.
    let inventory = Inv::of(player);
    let bag = inventory.bag();
    let bag_has_room = {
        let money = player.data().money;
        bag.lock()
            .expect("container poisoned")
            .space_left_for_template(reward.item_id, &server.game_data.items, money)
            >= 1
    };

    let by_mail = if bag_has_room && acquire_in_bag(&mut txn, &bag, reward.item_id) {
        false
    } else if acquire_in_mail(&mut txn, player, &inventory, reward) {
        true
    } else {
        tracing::warn!(
            achievement_id = reward.achievement_id,
            item_id = reward.item_id,
            "achievement reward could not be handed over; left pending"
        );
        return Delivery::Pending;
    };

    // The receipt is written only after the item exists, and before the gate is released.
    if !mark_delivered(player, reward.achievement_id) {
        // Unreachable under supported callers: every mutator of the reward state owns this gate,
        // and a refused receipt already means the row is gone, incomplete or receipted — never a
        // pending completion. The item is handed over for good: never roll it back, and never
        // report the reward as retryable.
        tracing::error!(
            achievement_id = reward.achievement_id,
            by_mail,
            "achievement reward handed over but the receipt was refused"
        );
        // Fail closed: a still-payable row would let the next pass hand the item over a second
        // time, so take the receipt again. A payable row satisfies the receipt's precondition,
        // so this succeeds unless a writer that does not own the gate interferes.
        if player
            .data()
            .achievement_state
            .has_pending_reward(reward.achievement_id)
        {
            let receipted = mark_delivered(player, reward.achievement_id);
            tracing::error!(
                achievement_id = reward.achievement_id,
                by_mail,
                receipted,
                "achievement reward was still payable after a refused receipt; receipt retaken"
            );
        }
        return Delivery::Granted { by_mail };
    }

    Delivery::Granted { by_mail }
}

/// `bag.AcquireDefaultItem(ItemTaskType.SkillEffectGainItem, itemId, 1)`.
///
/// The task type is the one the C# reward path uses so the container publishes the stack (the
/// neutral type writes nothing); the achievement's own `SCAchievementItemSentPacket` is the
/// context. A `false` return means nothing was added (invalid template / no room).
fn acquire_in_bag(txn: &mut ItemTxn<'_>, bag: &ContainerRef, item_id: u32) -> bool {
    bag.lock()
        .expect("container poisoned")
        .acquire_default_item(
            task_type::SKILL_EFFECT_GAIN_ITEM,
            item_id,
            1,
            -1,
            0,
            &mut txn.env,
        )
}

/// Stages one item in the character's mail escrow and publishes it as a single `SYS_EXPRESS`
/// letter (`MailAttachments.AcquireDefaultItemEx` + `BaseMail.Send`).
///
/// The letter is only kept when [`mail::send`] succeeds. A failed send removes the staged item
/// again ([`roll_back_staged`]), so the retry cannot leak or duplicate the reward.
fn acquire_in_mail(
    txn: &mut ItemTxn<'_>,
    player: &PlayerRef,
    inventory: &Inv,
    reward: &PendingReward,
) -> bool {
    let Some(escrow) = inventory.get(slot_type::MAIL) else {
        tracing::warn!(
            achievement_id = reward.achievement_id,
            "achievement reward has no mail escrow container"
        );
        return false;
    };

    // The C# stages through `MailAttachments.AcquireDefaultItemEx(ItemTaskType.Invalid, ...)`
    // with the character id as the crafter argument. The mail container never merges stacks, so a
    // successful call always reports the item as new; anything else added nothing.
    let staged = {
        let mut escrow = escrow.lock().expect("container poisoned");
        match escrow.acquire_default_item_ex(
            task_type::INVALID,
            reward.item_id,
            1,
            -1,
            player.character_id,
            -1,
            &mut txn.env,
        ) {
            Some(result) if !result.new.is_empty() => result.new,
            _ => return false,
        }
    };

    // `MailManager.Send` verifies the receiver against the name manager; read the real name for
    // the letter (nothing is sent to an unregistered / offline character).
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
    letter.body.text = overflow_mail_body(reward.achievement_id);
    letter.body.send_date = now;
    letter.body.recv_date = now;
    letter.body.attachments = staged.clone();

    if mail::send(txn, letter) {
        return true;
    }

    roll_back_staged(txn, &escrow, &staged);
    false
}

/// `MailDeliveryRules.TryDiscardStagedAttachments`: drop the staged item rows again so a failed
/// send can retry without leaking items (`RemoveItem(Invalid, item, releaseIdAsWell: true)`).
fn roll_back_staged(txn: &mut ItemTxn<'_>, escrow: &ContainerRef, staged: &[u64]) {
    let mut escrow = escrow.lock().expect("container poisoned");
    for item_id in staged {
        if escrow
            .remove_item_task(task_type::INVALID, *item_id, true, &mut txn.env)
            .is_none()
        {
            tracing::error!(
                item_id,
                "achievement reward: staged item could not be rolled back"
            );
        }
    }
}

/// The `character_achievements.reward_delivered` receipt, written inside the operation gate.
fn mark_delivered(player: &PlayerRef, achievement_id: u32) -> bool {
    player
        .data()
        .achievement_state
        .mark_reward_delivered(achievement_id)
}

#[cfg(test)]
mod tests {
    use std::sync::Arc;

    use chrono::{DateTime, Utc};

    use super::super::achievement::{AchievementGameData, Achievements};
    use super::super::achievement_runtime::AchievementState;
    use super::*;
    use crate::data::items::ItemTemplate;
    use crate::data::GameData;
    use crate::models::character::Inventory;
    use crate::models::item::Item;
    use crate::opcodes::sc;
    use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};

    /// The reward item the tests grant (one unit per stack, so a grant is always its own item).
    const REWARD_ITEM: u32 = 7001;

    fn at(seconds: i64) -> DateTime<Utc> {
        DateTime::from_timestamp(seconds, 0).expect("valid timestamp")
    }

    /// One achievement per `(id, item_id, is_active)`, like `AchievementGameData.Load` builds
    /// them.
    fn content(rewards: &[(u32, u32, bool)]) -> AchievementGameData {
        let mut data = AchievementGameData::default();
        for (id, item_id, is_active) in rewards {
            data.achievements.insert(
                *id,
                Achievements {
                    id: *id,
                    item_id: *item_id,
                    is_active: *is_active,
                    ..Default::default()
                },
            );
        }
        data
    }

    struct Fixture {
        server: Arc<GameServer>,
        client: TestClient,
        player: PlayerRef,
    }

    /// A server with the reward template as its only item, the given reward content, the
    /// character name registered (the mail receiver check reads it), and a spawned character with
    /// `bag_slots` bag slots. `bag_slots == 0` makes every reward overflow to the mail.
    fn fixture(rewards: &[(u32, u32, bool)], bag_slots: u8) -> Fixture {
        let mut data = GameData::empty();
        data.items
            .templates
            .insert(REWARD_ITEM, ItemTemplate::plain(REWARD_ITEM, 1));
        data.systems.social.achievements = content(rewards);
        let server = Arc::new(test_server_with(data));
        server.set_self();
        server.names.add_character(1, "Tester", 1);
        let mut client = TestClient::new(&server, 1, 1);
        let mut character = test_character(1, "Tester", 0.0, 0.0);
        if bag_slots != 50 {
            character.num_inventory_slots = bag_slots;
            character.inventory = Inventory::standalone(1, bag_slots, 50);
        }
        let player = spawn_player(&server, &client, character, 1000);
        // The item manager resolves attachments through the registered containers; the standalone
        // test inventory keeps them to itself, so register the two the pass touches.
        let inventory = Inv::of(&player);
        server.items.add_container(inventory.bag());
        if let Some(escrow) = inventory.get(slot_type::MAIL) {
            server.items.add_container(escrow);
        }
        client.drain();
        Fixture {
            server,
            client,
            player,
        }
    }

    /// A completed row with no receipt: exactly what loading a freshly-completed achievement
    /// restores.
    fn pending_completion(f: &Fixture, achievement_id: u32, completed_at: DateTime<Utc>) {
        f.player
            .data()
            .achievement_state
            .restore_progress_with_receipt(achievement_id, 0, Some(completed_at), false);
    }

    fn bag_items(f: &Fixture) -> Vec<Item> {
        Inv::of(&f.player)
            .bag()
            .lock()
            .expect("container poisoned")
            .items
            .clone()
    }

    fn mail_items(f: &Fixture) -> Vec<Item> {
        Inv::of(&f.player)
            .get(slot_type::MAIL)
            .expect("mail container")
            .lock()
            .expect("container poisoned")
            .items
            .clone()
    }

    /// The `SCAchievementItemSentPacket`s of the drained frames as `(id, by_mail)`.
    fn item_sent(client: &mut TestClient) -> Vec<(u32, bool)> {
        client
            .drain()
            .into_iter()
            .filter(|(opcode, _)| *opcode == sc::SCAchievementItemSentPacket)
            .map(|(_, body)| {
                (
                    u32::from_le_bytes(body[..4].try_into().expect("id")),
                    body[4] != 0,
                )
            })
            .collect()
    }

    #[tokio::test]
    async fn a_completed_achievement_grants_one_item_with_a_receipt_and_one_packet() {
        let mut f = fixture(&[(1, REWARD_ITEM, true)], 50);
        pending_completion(&f, 1, at(1_000));

        let delivery = deliver_pending(&f.server, &f.player);

        assert_eq!(
            delivery.granted,
            [GrantedReward {
                achievement_id: 1,
                by_mail: false
            }]
        );
        assert!(delivery.settled.is_empty());
        assert!(delivery.pending.is_empty());
        assert!(delivery.delivered());

        // One default-grade item in the bag, nothing staged for the mail.
        let bag = bag_items(&f);
        assert_eq!(bag.len(), 1);
        assert_eq!(
            (bag[0].template_id, bag[0].count, bag[0].grade),
            (REWARD_ITEM, 1, 0)
        );
        assert!(mail_items(&f).is_empty());
        assert!(f.server.systems.economy.mail.state().all.is_empty());

        // The receipt: paid, still complete, and the completion time is untouched.
        {
            let c = f.player.data();
            assert!(!c.achievement_state.has_pending_reward(1));
            let row = c.achievement_state.progress(1).expect("progress row");
            assert!(row.reward_delivered());
            assert_eq!(row.completed_at(), Some(at(1_000)));
            assert!(c.achievement_state.pending_reward_ids().is_empty());
        }
        assert_eq!(item_sent(&mut f.client), [(1, false)]);
    }

    #[tokio::test]
    async fn replaying_a_paid_completion_is_silent_and_keeps_the_item_count() {
        let mut f = fixture(&[(1, REWARD_ITEM, true)], 50);
        pending_completion(&f, 1, at(1_000));
        deliver_pending(&f.server, &f.player);
        f.client.drain();

        let again = deliver_pending(&f.server, &f.player);
        assert_eq!(again, RewardDelivery::default());
        assert!(item_sent(&mut f.client).is_empty());
        assert_eq!(bag_items(&f).len(), 1);

        // A relog restores the same row, receipt included (the DB load path), and must not pay.
        let snapshot = f.player.data().achievement_state.progress_snapshot();
        {
            let mut c = f.player.data();
            let mut restored = AchievementState::default();
            for row in &snapshot {
                restored.restore_progress_with_receipt(
                    row.achievement_id(),
                    i64::from(row.amount()),
                    row.completed_at(),
                    row.reward_delivered(),
                );
            }
            c.achievement_state = restored;
        }

        let replayed = deliver_pending(&f.server, &f.player);
        assert_eq!(replayed, RewardDelivery::default());
        assert!(item_sent(&mut f.client).is_empty());
        assert_eq!(bag_items(&f).len(), 1);
    }

    #[tokio::test]
    async fn a_completion_without_an_item_is_settled_by_the_receipt_alone() {
        let mut f = fixture(&[(2, 0, true)], 50);
        pending_completion(&f, 2, at(2_000));

        let delivery = deliver_pending(&f.server, &f.player);

        assert!(delivery.granted.is_empty());
        assert_eq!(delivery.settled, [2]);
        assert!(delivery.pending.is_empty());
        assert!(bag_items(&f).is_empty());
        assert!(mail_items(&f).is_empty());
        assert!(f.server.systems.economy.mail.state().all.is_empty());
        assert!(item_sent(&mut f.client).is_empty());
        {
            let c = f.player.data();
            let row = c.achievement_state.progress(2).expect("progress row");
            assert!(row.reward_delivered());
            assert!(!c.achievement_state.has_pending_reward(2));
        }
    }

    #[tokio::test]
    async fn a_full_bag_sends_the_reward_by_system_express_mail() {
        let mut f = fixture(&[(1, REWARD_ITEM, true)], 0);
        pending_completion(&f, 1, at(1_000));

        let delivery = deliver_pending(&f.server, &f.player);

        assert_eq!(
            delivery.granted,
            [GrantedReward {
                achievement_id: 1,
                by_mail: true
            }]
        );
        assert!(delivery.pending.is_empty());
        assert!(bag_items(&f).is_empty());

        // The item is staged in the mail escrow, not loose.
        let escrow = mail_items(&f);
        assert_eq!(escrow.len(), 1);
        assert_eq!(escrow[0].template_id, REWARD_ITEM);
        assert_eq!(escrow[0].slot_type, slot_type::MAIL);

        // One proper system letter, addressed to the real character, with the item attached.
        {
            let mails = f.server.systems.economy.mail.state();
            assert_eq!(mails.all.len(), 1);
            let (_, letter) = mails.all.iter().next().expect("letter");
            assert_eq!(letter.mail_type, mail_type::SYS_EXPRESS);
            assert_eq!(letter.title, OVERFLOW_MAIL_TITLE);
            assert_eq!(letter.body.text, overflow_mail_body(1));
            assert_eq!(letter.receiver_name, "Tester");
            assert_eq!(letter.header.receiver_id, 1);
            assert_eq!(letter.header.sender_id, 0);
            // A literal 1.2 sender with no leading locale dot: the 1.2 client has no
            // `mail.achievementReward` table, so a dotted sender renders as NIL in the mailbox.
            assert_eq!(letter.header.sender_name, "Achievement Rewards");
            assert!(!letter.header.sender_name.starts_with('.'));
            assert_eq!(letter.header.status, mail_status::UNREAD);
            assert_eq!(letter.body.attachments, [escrow[0].id]);
        }

        assert_eq!(item_sent(&mut f.client), [(1, true)]);
        {
            let c = f.player.data();
            assert!(!c.achievement_state.has_pending_reward(1));
            assert!(c
                .achievement_state
                .progress(1)
                .expect("row")
                .reward_delivered());
            assert_eq!(c.unread_mail.received, 1);
        }
    }

    #[tokio::test]
    async fn a_failed_grant_stays_pending_and_is_retried_when_space_appears() {
        let mut f = fixture(&[(1, REWARD_ITEM, true)], 0);
        // Neither the bag nor the mail escrow can take the item yet.
        {
            let inv = Inv::of(&f.player);
            inv.get(slot_type::MAIL)
                .expect("mail container")
                .lock()
                .expect("container poisoned")
                .set_container_size(0);
        }
        pending_completion(&f, 1, at(1_000));

        let failed = deliver_pending(&f.server, &f.player);

        assert!(failed.granted.is_empty());
        assert!(failed.settled.is_empty());
        assert_eq!(failed.pending, [1]);
        assert!(f.player.data().achievement_state.has_pending_reward(1));
        assert!(!f
            .player
            .data()
            .achievement_state
            .progress(1)
            .expect("row")
            .reward_delivered());
        assert!(bag_items(&f).is_empty());
        assert!(mail_items(&f).is_empty());
        assert!(f.server.systems.economy.mail.state().all.is_empty());
        assert!(item_sent(&mut f.client).is_empty());

        // Space appears: the next pass retries the still-pending reward and pays exactly once.
        Inv::of(&f.player)
            .get(slot_type::MAIL)
            .expect("mail container")
            .lock()
            .expect("container poisoned")
            .set_container_size(-1);
        let retried = deliver_pending(&f.server, &f.player);
        assert_eq!(
            retried.granted,
            [GrantedReward {
                achievement_id: 1,
                by_mail: true
            }]
        );
        assert!(retried.pending.is_empty());
        assert_eq!(mail_items(&f).len(), 1);
        assert_eq!(item_sent(&mut f.client), [(1, true)]);
        assert!(!f.player.data().achievement_state.has_pending_reward(1));
    }

    #[tokio::test]
    async fn unavailable_rewards_stay_pending_without_receipts() {
        let mut f = fixture(
            &[
                (1, REWARD_ITEM, true),
                (2, REWARD_ITEM, false),
                (3, 999_999, true),
            ],
            50,
        );
        pending_completion(&f, 1, at(1_000));
        pending_completion(&f, 2, at(1_000));
        pending_completion(&f, 3, at(1_000));
        // A completed row the content does not have at all.
        pending_completion(&f, 99, at(1_000));

        let delivery = deliver_pending(&f.server, &f.player);

        // Only the known, active, valid-template row is paid.
        assert_eq!(
            delivery.granted,
            [GrantedReward {
                achievement_id: 1,
                by_mail: false
            }]
        );
        assert_eq!(delivery.pending, [2, 3, 99]);
        assert_eq!(bag_items(&f).len(), 1);
        {
            let c = f.player.data();
            for id in [2, 3, 99] {
                let row = c.achievement_state.progress(id).expect("progress row");
                assert!(row.has_pending_reward());
                assert!(!row.reward_delivered());
            }
        }
        assert_eq!(item_sent(&mut f.client), [(1, false)]);

        // A second pass retries the leftovers and changes nothing else.
        let again = deliver_pending(&f.server, &f.player);
        assert!(again.granted.is_empty() && again.settled.is_empty());
        assert_eq!(again.pending, [2, 3, 99]);
        assert_eq!(bag_items(&f).len(), 1);
        assert!(item_sent(&mut f.client).is_empty());
    }

    #[tokio::test]
    async fn the_receipt_is_part_of_the_persisted_progress_snapshot() {
        let f = fixture(&[(1, REWARD_ITEM, true), (2, 0, true)], 50);
        pending_completion(&f, 1, at(1_000));
        pending_completion(&f, 2, at(2_000));
        deliver_pending(&f.server, &f.player);

        let snapshot = f.player.data().achievement_state.progress_snapshot();
        assert_eq!(
            snapshot
                .iter()
                .map(|row| (
                    row.achievement_id(),
                    row.reward_delivered(),
                    row.completed_at()
                ))
                .collect::<Vec<_>>(),
            [(1, true, Some(at(1_000))), (2, true, Some(at(2_000)))]
        );

        // The DB load round trip (receipt included) leaves nothing payable; the same rows without
        // the receipt — a pre-receipt save — are both owed again.
        let mut loaded = AchievementState::default();
        for row in &snapshot {
            loaded.restore_progress_with_receipt(
                row.achievement_id(),
                i64::from(row.amount()),
                row.completed_at(),
                row.reward_delivered(),
            );
        }
        assert!(loaded.pending_reward_ids().is_empty());

        let mut unpaid = AchievementState::default();
        for row in &snapshot {
            unpaid.restore_progress_with_receipt(
                row.achievement_id(),
                i64::from(row.amount()),
                row.completed_at(),
                false,
            );
        }
        assert_eq!(unpaid.pending_reward_ids(), [1, 2]);
    }
}
