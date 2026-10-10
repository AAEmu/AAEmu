//! Port of `Models/Game/Mails/*` (`BaseMail`, `MailHeader`, `MailBody`, `CountUnreadMail`
//! helpers, the enums) and `Core/Managers/MailManager` (mail store, `Send`, delivery,
//! `PayChargeMoney`, house tax mails, quest reward mails, MySQL load / save).
//!
//! Mails are kept in a [`DotNetDict`] (the C# `Dictionary<long, BaseMail>` iteration order,
//! including the slot reuse after removals). Attachments are item ids: the item objects stay in
//! whatever container holds them (normally the sender's / receiver's `SlotType.Mail`
//! container, or a loose item in the `ItemManager`), exactly like the C# `Body.Attachments`
//! references.
//!
//! Lock order: economy operation lock ([`ItemTxn`]) -> mail store -> character data / item
//! containers (leaf). Every mutating mail operation runs inside an [`ItemTxn`]; packets are
//! queued in `txn.env.fx` so they stay ordered with the item task packets.

use std::collections::HashMap;
use std::sync::{Arc, Mutex, MutexGuard};

use aaemu_commons::{Marshal, PacketStream};
use chrono::Utc;
use sqlx::Row;

use crate::data::Feature;
use crate::db::{dt, from_unix, int, unix};
use crate::models::char_data::CountUnreadMail;
use crate::models::container::{slot_type, ContainerRef};
use crate::models::item::{Item, UnixTime};
use crate::models::item_task::{task_type, ItemFxList};
use crate::server::GameServer;
use crate::systems::HookSlot;
use crate::world::PlayerRef;

use super::error_message;
use super::inventory::{self, Inv, ItemTxn};
use super::mail_packets::{
    SCChargeMoneyPaidPacket, SCGotMailPacket, SCMailDeletedPacket, SCMailReturnedPacket,
};

/// `MailType`.
pub mod mail_type {
    pub const INVALID_MAIL_TYPE: u8 = 0;
    pub const NORMAL: u8 = 1;
    pub const EXPRESS: u8 = 2;
    pub const REGISTERED: u8 = 3;
    pub const SPAM: u8 = 4;
    pub const ADMIN: u8 = 5;
    pub const BILLING: u8 = 6;
    pub const BALANCE_RECEIPT: u8 = 7;
    pub const TAXRATE_CHANGED: u8 = 8;
    pub const CHARGED: u8 = 9;
    pub const PROMOTION: u8 = 10;
    pub const MIA_SEND: u8 = 11;
    pub const MIA_RECV: u8 = 12;
    pub const AUC_OFF_CANCEL: u8 = 13;
    pub const AUC_OFF_SUCCESS: u8 = 14;
    pub const AUC_OFF_FAIL: u8 = 15;
    pub const AUC_BID_WIN: u8 = 16;
    pub const AUC_BID_FAIL: u8 = 17;
    pub const SYS_EXPRESS: u8 = 18;
    pub const SYS_SELL_BACKPACK: u8 = 19;
    pub const DEMOLISH_WITH_PENALTY: u8 = 20;
    pub const HOUSING_SALE: u8 = 21;
    pub const USER_BILL: u8 = 22;
    pub const NATION_TAX_RATE: u8 = 23;
    pub const NATION_TAX_RECEIPT: u8 = 24;
    pub const NATION_REQUEST_FRIEND: u8 = 25;
    pub const KICKED_FROM_BEGINNER_EXPED: u8 = 26;
    pub const MOBILIZATION_GIVE_ITEM: u8 = 27;
    pub const HERO_CANDIDATE_ALARM: u8 = 28;
    pub const HERO_ELECTION_ITEM: u8 = 29;
    pub const HOUSING_REBUILD: u8 = 30;
    pub const DEMOLISH: u8 = 31;
    pub const SIEGE_WIN_GIVE_ITEM: u8 = 32;
    pub const SIEGE_LOSE_GIVE_ITEM: u8 = 33;
    pub const TAX_IN_KIND_RECEIPT: u8 = 34;
    pub const NATION_RELATION_FRIEND: u8 = 35;
    pub const NATION_RELATION_FRIEND_SUCCESS: u8 = 36;
    pub const NATION_RELATION_FRIEND_REJECT: u8 = 37;
    pub const NATION_RELATION_FRIEND_REJECT_ITEM: u8 = 38;
    pub const NATION_RELATION_HOSTILE: u8 = 39;
    pub const NATION_RELATION_HOSTILE_SUCCESS: u8 = 40;
    pub const NATION_RELATION_HOSTILE_REJECT: u8 = 41;
    pub const SIEGE_AUCTION_START: u8 = 42;
    pub const EXPEDITION_IMMIGRATION_REJECT: u8 = 43;
    pub const RESIDENT_BALANCE: u8 = 44;
}

/// `MailStatus`.
pub mod mail_status {
    pub const UNREAD: u8 = 0;
    pub const READ: u8 = 1;
    pub const UNPAID: u8 = 2;
}

/// `MailResult`.
pub mod mail_result {
    pub const SUCCESS: u8 = 0;
    pub const INSUFFICIENT_COINS: u8 = 1;
    pub const CAN_NOT_BE_MAILED: u8 = 2;
    pub const INVALID_SLOT: u8 = 3;
    pub const CAN_NOT_FIND_MAIL: u8 = 4;
    pub const NO_ATTACHED_MONEY: u8 = 5;
    pub const NO_ATTACHED_ITEMS: u8 = 6;
    pub const INSUFFICIENT_COINS_2: u8 = 7;
    pub const RETURNS_NOT_ALLOWED: u8 = 8;
    pub const SUBJECT_LENGTH_LIMITED: u8 = 9;
    pub const TEXT_LENGTH_LIMITED: u8 = 10;
    pub const MAIL_NOT_INITIALIZED: u8 = 11;
    pub const INVALID_LETTER_FORMAT: u8 = 12;
    pub const INCORRECT_ITEM_INFORMATION: u8 = 13;
    pub const UNABLE_TO_ACCESS_SELECTED_MAIL: u8 = 14;
    pub const UNABLE_TO_FIND_RECIPIENT: u8 = 15;
    pub const NO_MAILBOX_NEARBY: u8 = 16;
    pub const INSUFFICIENT_COINS_3: u8 = 17;
    pub const FEATURE_UNDER_MAINTENANCE: u8 = 18;
    pub const BOUND_ITEM: u8 = 19;
    pub const NOT_AVAILABLE_IN_FREE_TRIAL: u8 = 20;
    pub const NOT_AVAILABLE_FOR_FREE_ACCOUNTS: u8 = 21;
    pub const LEVEL_TOO_LOW: u8 = 22;
    pub const YOU_MUST_PAY_FIRST: u8 = 23;
    pub const CHARACTERS_TRANSFERRING_SERVERS: u8 = 24;
    pub const SECONDARY_PASSWORD_REQUIRED: u8 = 25;
    pub const ITEM_LOCKED: u8 = 26;
    pub const CANNOT_PERFORM_SENSITIVE_ACTIONS: u8 = 27;
    pub const SENDING_MAILS_TOO_FREQUENT: u8 = 28;
    pub const SENT_SAME_MAIL_TOO_MANY_TIMES: u8 = 29;
    pub const MAIL_ERROR_OCCURRED: u8 = 30;
}

/// `CommercialMailTypes` (values of [`mail_type`]).
pub mod commercial_mail_types {
    use super::mail_type;
    pub const CHARGED: u8 = mail_type::CHARGED;
    pub const MIA_RECV: u8 = mail_type::MIA_RECV;
    pub const MIA_SEND: u8 = mail_type::MIA_SEND;
    pub const PROMOTION: u8 = mail_type::PROMOTION;
}

/// `MiaMailTypes` (values of [`mail_type`]).
pub mod mia_mail_types {
    use super::mail_type;
    pub const MIA_RECV: u8 = mail_type::MIA_RECV;
    pub const MIA_SEND: u8 = mail_type::MIA_SEND;
}

/// `MailBody.MaxMailAttachments`.
pub const MAX_MAIL_ATTACHMENTS: usize = 10;

/// `MailManager.CostNormal` and friends (static, settable in C#; constants here).
pub const COST_NORMAL: i32 = 50;
pub const COST_NORMAL_ATTACHMENT: i32 = 30;
pub const COST_EXPRESS: i32 = 100;
pub const COST_EXPRESS_ATTACHMENT: i32 = 80;
pub const COST_FREE_ATTACHMENT_COUNT: i32 = 1;
/// `MailManager.NormalMailDelay` (30 minutes), in seconds.
pub const NORMAL_MAIL_DELAY_SECS: i64 = 30 * 60;
/// `MailManager.MailExpireDelay` (14 days), in seconds (unused in C#).
pub const MAIL_EXPIRE_DELAY_SECS: i64 = 14 * 24 * 3600;
/// `AppConfiguration.Specialty.TradePackMailDelayInMinutes` default.
pub const DEFAULT_TRADE_PACK_MAIL_DELAY_MINUTES: f64 = 480.0;

/// `Item.Coins`, `Item.TaxCertificate`, `Item.BoundTaxCertificate`.
pub const ITEM_COINS: u32 = 500;
pub const ITEM_TAX_CERTIFICATE: u32 = 31891;
pub const ITEM_BOUND_TAX_CERTIFICATE: u32 = 31892;
/// `ActabilityType.Commerce`.
pub const ACTABILITY_COMMERCE: i32 = 31;

/// `DateTime.UtcNow` (unix seconds).
pub fn now() -> UnixTime {
    Utc::now().timestamp()
}

// ---------------------------------------------------------------------------------------------
// CountUnreadMail
// ---------------------------------------------------------------------------------------------

impl CountUnreadMail {
    /// `ResetReceived()`.
    pub fn reset_received(&mut self) {
        self.received = 0;
        self.mia_received = 0;
        self.commercial_received = 0;
    }

    /// `UpdateReceived(mailType, amount)`.
    pub fn update_received(&mut self, mail_type: u8, amount: i32) {
        if mail_type == mail_type::CHARGED || mail_type == mail_type::PROMOTION {
            self.commercial_received += amount;
        } else if mail_type == mail_type::MIA_RECV {
            self.mia_received += amount;
        } else {
            self.received += amount;
        }
    }
}

// ---------------------------------------------------------------------------------------------
// BaseMail / MailHeader / MailBody
// ---------------------------------------------------------------------------------------------

/// The stored members of `MailHeader` (the others are read from the parent [`BaseMail`]).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct MailHeader {
    pub status: u8,
    pub sender_id: u32,
    pub sender_name: String,
    pub attachments: u8,
    pub receiver_id: u32,
    pub returned: bool,
    pub extra: i64,
}

/// The stored members of `MailBody`. `attachments` are item ids (`List<Item>`).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct MailBody {
    pub text: String,
    pub copper_coins: i32,
    pub billing_amount: i32,
    pub money_amount2: i32,
    pub send_date: UnixTime,
    pub recv_date: UnixTime,
    pub attachments: Vec<u64>,
}

/// `BaseMail`. The C# setters of `Id`, `MailType`, `Title`, `ReceiverName`, `OpenDate` and of
/// the body's `Text`, money and dates set `IsDirty`; the callers here set [`BaseMail::is_dirty`]
/// where the C# goes through one of those setters (header fields do not).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BaseMail {
    pub id: i64,
    pub mail_type: u8,
    pub title: String,
    pub receiver_name: String,
    pub open_date: UnixTime,
    pub header: MailHeader,
    pub body: MailBody,
    /// Local helper: the receiver was notified.
    pub is_delivered: bool,
    pub is_dirty: bool,
}

impl Default for BaseMail {
    /// `new BaseMail()` (the constructor's `Header = ...` / `Body = ...` set `IsDirty`).
    fn default() -> Self {
        Self {
            id: 0,
            mail_type: mail_type::INVALID_MAIL_TYPE,
            title: String::new(),
            receiver_name: String::new(),
            open_date: 0,
            header: MailHeader::default(),
            body: MailBody::default(),
            is_delivered: false,
            is_dirty: true,
        }
    }
}

impl BaseMail {
    pub fn new() -> Self {
        Self::default()
    }

    /// `GetTotalAttachmentCount()`.
    pub fn get_total_attachment_count(&self) -> u8 {
        let mut res = self.body.attachments.len() as u8;
        if self.body.copper_coins != 0 {
            res = res.wrapping_add(1);
        }
        if self.body.billing_amount != 0 {
            res = res.wrapping_add(1);
        }
        if self.body.money_amount2 != 0 {
            res = res.wrapping_add(1);
        }
        res
    }

    /// `AttachMoney(copperCoinsAmount, money1Amount, money2Amount)`: does not take the money from
    /// anyone.
    pub fn attach_money(&mut self, copper: i32, money1: i32, money2: i32) {
        self.body.copper_coins = copper;
        self.body.billing_amount = money1;
        self.body.money_amount2 = money2;
        self.is_dirty = true;
        self.header.attachments = self.get_total_attachment_count();
    }

    /// `CanReturnMail()`.
    pub fn can_return_mail(&self) -> bool {
        !self.is_delivered
            && self.header.sender_id != self.header.receiver_id
            && self.header.sender_id > 0
            && (self.mail_type == mail_type::NORMAL || self.mail_type == mail_type::EXPRESS)
    }

    /// `MailHeader` as written on the wire.
    pub fn header_wire(&self) -> MailHeaderWire {
        MailHeaderWire {
            mail_id: self.id,
            mail_type: self.mail_type,
            status: self.header.status,
            title: self.title.clone(),
            sender_name: self.header.sender_name.clone(),
            attachments: self.header.attachments,
            receiver_name: self.receiver_name.clone(),
            open_date: self.open_date,
            returned: self.header.returned,
            extra: self.header.extra,
        }
    }

    /// `MailBody` as written on the wire (attachment items resolved through the item store).
    pub fn body_wire(&self, server: &GameServer) -> MailBodyWire {
        let items = (0..MAX_MAIL_ATTACHMENTS)
            .map(|i| {
                self.body
                    .attachments
                    .get(i)
                    .and_then(|id| server.items.item_by_id(*id))
            })
            .collect();
        MailBodyWire {
            mail_id: self.id,
            mail_type: self.mail_type,
            receiver_name: self.receiver_name.clone(),
            title: self.title.clone(),
            text: self.body.text.clone(),
            copper_coins: self.body.copper_coins,
            billing_amount: self.body.billing_amount,
            money_amount2: self.body.money_amount2,
            send_date: self.body.send_date,
            recv_date: self.body.recv_date,
            open_date: self.open_date,
            items,
        }
    }
}

/// A `MailHeader` snapshot (`MailHeader.Write`).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct MailHeaderWire {
    pub mail_id: i64,
    pub mail_type: u8,
    pub status: u8,
    pub title: String,
    pub sender_name: String,
    pub attachments: u8,
    pub receiver_name: String,
    pub open_date: UnixTime,
    pub returned: bool,
    pub extra: i64,
}

impl Marshal for MailHeaderWire {
    fn write(&self, s: &mut PacketStream) {
        s.write_i64(self.mail_id)
            .write_u8(self.mail_type)
            .write_u8(self.status)
            .write_str(&self.title)
            .write_str(&self.sender_name)
            .write_u8(self.attachments)
            .write_str(&self.receiver_name)
            .write_i64(self.open_date)
            .write_bool(self.returned)
            .write_i64(self.extra);
    }
}

/// A `MailBody` snapshot (`MailBody.Write`); `items` has [`MAX_MAIL_ATTACHMENTS`] entries.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct MailBodyWire {
    pub mail_id: i64,
    pub mail_type: u8,
    pub receiver_name: String,
    pub title: String,
    pub text: String,
    pub copper_coins: i32,
    pub billing_amount: i32,
    pub money_amount2: i32,
    pub send_date: UnixTime,
    pub recv_date: UnixTime,
    pub open_date: UnixTime,
    pub items: Vec<Option<Item>>,
}

impl Marshal for MailBodyWire {
    fn write(&self, s: &mut PacketStream) {
        s.write_i64(self.mail_id)
            .write_u8(self.mail_type)
            .write_str(&self.receiver_name)
            .write_str(&self.title)
            .write_str(&self.text)
            .write_i32(self.copper_coins)
            .write_i32(self.billing_amount)
            .write_i32(self.money_amount2)
            .write_i64(self.send_date)
            .write_i64(self.recv_date)
            .write_i64(self.open_date);
        for i in 0..MAX_MAIL_ATTACHMENTS {
            match self.items.get(i).and_then(Option::as_ref) {
                Some(item) => item.write(s),
                None => {
                    s.write_i32(0);
                }
            }
        }
    }
}

// ---------------------------------------------------------------------------------------------
// DotNetDict
// ---------------------------------------------------------------------------------------------

/// A `Dictionary<long, V>` with the .NET enumeration order: entries live in an array, removed
/// entries go on a LIFO free list that the next `Add` reuses.
#[derive(Debug, Clone)]
pub struct DotNetDict<V> {
    entries: Vec<Option<(i64, V)>>,
    free: Vec<usize>,
    index: HashMap<i64, usize>,
}

impl<V> Default for DotNetDict<V> {
    fn default() -> Self {
        Self {
            entries: Vec::new(),
            free: Vec::new(),
            index: HashMap::new(),
        }
    }
}

impl<V> DotNetDict<V> {
    /// `Add` (`false` where the C# throws for a duplicate key).
    pub fn add(&mut self, key: i64, value: V) -> bool {
        if self.index.contains_key(&key) {
            return false;
        }
        let idx = match self.free.pop() {
            Some(i) => {
                self.entries[i] = Some((key, value));
                i
            }
            None => {
                self.entries.push(Some((key, value)));
                self.entries.len() - 1
            }
        };
        self.index.insert(key, idx);
        true
    }

    pub fn remove(&mut self, key: i64) -> Option<V> {
        let idx = self.index.remove(&key)?;
        let (_, v) = self.entries[idx].take()?;
        self.free.push(idx);
        Some(v)
    }

    pub fn get(&self, key: i64) -> Option<&V> {
        let idx = *self.index.get(&key)?;
        self.entries[idx].as_ref().map(|(_, v)| v)
    }

    pub fn get_mut(&mut self, key: i64) -> Option<&mut V> {
        let idx = *self.index.get(&key)?;
        self.entries[idx].as_mut().map(|(_, v)| v)
    }

    pub fn contains_key(&self, key: i64) -> bool {
        self.index.contains_key(&key)
    }

    pub fn len(&self) -> usize {
        self.index.len()
    }

    pub fn is_empty(&self) -> bool {
        self.index.is_empty()
    }

    /// Enumeration in .NET order.
    pub fn iter(&self) -> impl Iterator<Item = (i64, &V)> {
        self.entries
            .iter()
            .filter_map(|e| e.as_ref().map(|(k, v)| (*k, v)))
    }

    pub fn iter_mut(&mut self) -> impl Iterator<Item = (i64, &mut V)> {
        self.entries
            .iter_mut()
            .filter_map(|e| e.as_mut().map(|(k, v)| (*k, v)))
    }

    /// The keys in .NET order.
    pub fn keys(&self) -> Vec<i64> {
        self.iter().map(|(k, _)| k).collect()
    }
}

// ---------------------------------------------------------------------------------------------
// Hooks
// ---------------------------------------------------------------------------------------------

/// What `MailManager.PayChargeMoney` needs from the housing subsystem (`HousingManager`).
/// Installed by the housing owner in its `System::start`; the defaults behave as if no house
/// exists (there is no housing without that subsystem).
pub trait MailHousingHooks: Send + Sync {
    /// `HousingManager.GetHouseById(houseId) != null`.
    fn house_exists(&self, _server: &GameServer, _house_id: u32) -> bool {
        false
    }

    /// `HousingManager.PayWeeklyTax(house)` (adds `DaysForTaxPayment` to the protection end).
    fn pay_weekly_tax(&self, _server: &GameServer, _house_id: u32) -> bool {
        false
    }
}

/// The mail hook slots (`server.systems.economy.mail.hooks`).
#[derive(Debug, Default)]
pub struct MailHooks {
    pub housing: HookSlot<dyn MailHousingHooks>,
}

// ---------------------------------------------------------------------------------------------
// MailManager
// ---------------------------------------------------------------------------------------------

#[derive(Debug, Default)]
pub struct MailState {
    /// `_allPlayerMails`.
    pub all: DotNetDict<BaseMail>,
    /// `_deletedMailIds`.
    pub deleted: Vec<i64>,
}

/// `MailManager` (`server.systems.economy.mail`).
#[derive(Debug)]
pub struct MailManager {
    state: Mutex<MailState>,
    pub hooks: MailHooks,
    /// `AppConfiguration.Specialty.TradePackMailDelayInMinutes` (`/settradepackmaildelay`).
    trade_pack_mail_delay_minutes: Mutex<f64>,
}

impl Default for MailManager {
    fn default() -> Self {
        Self {
            state: Mutex::new(MailState::default()),
            hooks: MailHooks::default(),
            trade_pack_mail_delay_minutes: Mutex::new(DEFAULT_TRADE_PACK_MAIL_DELAY_MINUTES),
        }
    }
}

impl MailManager {
    /// The mail store (see the module docs for the lock order).
    pub fn state(&self) -> MutexGuard<'_, MailState> {
        self.state.lock().unwrap_or_else(|e| e.into_inner())
    }

    pub fn trade_pack_mail_delay_minutes(&self) -> f64 {
        *self
            .trade_pack_mail_delay_minutes
            .lock()
            .unwrap_or_else(|e| e.into_inner())
    }

    pub fn set_trade_pack_mail_delay_minutes(&self, minutes: f64) {
        *self
            .trade_pack_mail_delay_minutes
            .lock()
            .unwrap_or_else(|e| e.into_inner()) = minutes;
    }

    /// `GetMailById(id)` (a copy).
    pub fn get_mail_by_id(&self, id: i64) -> Option<BaseMail> {
        self.state().all.get(id).cloned()
    }

    /// Runs `f` on the stored mail `id`.
    pub fn with_mail_mut<R>(&self, id: i64, f: impl FnOnce(&mut BaseMail) -> R) -> Option<R> {
        self.state().all.get_mut(id).map(f)
    }

    /// `AllPlayerMails.Count`.
    pub fn count(&self) -> usize {
        self.state().all.len()
    }

    /// `GetNewMailId()`.
    pub fn get_new_mail_id(&self, server: &GameServer) -> u32 {
        let mut st = self.state();
        new_mail_id(server, &mut st)
    }

    /// `DeleteMail(long id)`: queues the row deletion, releases the id and forgets the mail.
    pub fn delete_mail(&self, server: &GameServer, id: i64) -> bool {
        let mut st = self.state();
        delete_mail_in(server, &mut st, id)
    }

    /// `GetMyHouseMails(houseId)`: the ids of the tax mails of that house.
    pub fn get_my_house_mails(&self, house_id: u32) -> Vec<i64> {
        let st = self.state();
        st.all
            .iter()
            .filter(|(_, m)| {
                m.mail_type == mail_type::BILLING
                    && extract_extra_for_house(m.header.extra).1 == house_id
            })
            .map(|(_, m)| m.id)
            .collect()
    }

    /// `Load()` (the MySQL part): every row of `mails`. The delivery task is scheduled by
    /// [`start_delivery_task`].
    pub async fn load(&self, server: &GameServer) -> crate::Result<()> {
        tracing::info!("Loading player mails ...");
        let rows = sqlx::query(MAIL_SELECT).fetch_all(&server.db).await?;
        let mut loaded = Vec::with_capacity(rows.len());
        for r in &rows {
            let mut attachment_ids = [0u64; MAX_MAIL_ATTACHMENTS];
            for (i, a) in attachment_ids.iter_mut().enumerate() {
                *a = r.try_get::<u64, _>(format!("attachment{i}").as_str())?;
            }
            loaded.push(MailRow {
                id: int(r, "id")?,
                title: r.try_get("title")?,
                mail_type: int(r, "type")?,
                receiver_name: r.try_get("receiver_name")?,
                open_date: unix(dt(r, "open_date")),
                status: int(r, "status")?,
                sender_id: int(r, "sender_id")?,
                sender_name: r.try_get("sender_name")?,
                attachment_count: int(r, "attachment_count")?,
                receiver_id: int(r, "receiver_id")?,
                returned: int(r, "returned")?,
                extra: int(r, "extra")?,
                text: r.try_get("text")?,
                money1: int(r, "money_amount_1")?,
                money2: int(r, "money_amount_2")?,
                money3: int(r, "money_amount_3")?,
                send_date: unix(dt(r, "send_date")),
                received_date: unix(dt(r, "received_date")),
                attachment_ids,
            });
        }
        self.load_rows(server, loaded, now());
        tracing::info!("Loaded {} player mails", self.count());
        Ok(())
    }

    /// The row handling of `Load()` (DB-free).
    pub fn load_rows(&self, server: &GameServer, rows: Vec<MailRow>, now: UnixTime) {
        let mut st = self.state();
        st.all = DotNetDict::default();
        st.deleted.clear();
        for r in rows {
            let mut m = BaseMail {
                id: i64::from(r.id as i32),
                title: r.title,
                mail_type: r.mail_type as i32 as u8,
                receiver_name: r.receiver_name,
                open_date: r.open_date,
                ..BaseMail::default()
            };
            m.header = MailHeader {
                status: r.status as i32 as u8,
                sender_id: r.sender_id as u32,
                sender_name: r.sender_name,
                attachments: r.attachment_count as i32 as u8,
                receiver_id: r.receiver_id as u32,
                returned: r.returned as i32 != 0,
                extra: r.extra,
            };
            m.body = MailBody {
                text: r.text,
                copper_coins: r.money1 as i32,
                billing_amount: r.money2 as i32,
                money_amount2: r.money3 as i32,
                send_date: r.send_date,
                recv_date: r.received_date,
                attachments: Vec::new(),
            };
            // Read/Load Items
            for item_id in r.attachment_ids {
                if item_id == 0 {
                    continue;
                }
                let receiver = u64::from(m.header.receiver_id);
                if mutate_item(server, item_id, |i| {
                    i.owner_id = receiver;
                    i.dirty = true;
                }) {
                    m.body.attachments.push(item_id);
                } else {
                    tracing::warn!(
                        "Found orphaned itemId {item_id} in mailId {}, not loaded!",
                        m.id
                    );
                }
            }
            let mut attachment_count = m.body.attachments.len() as i32;
            if m.body.copper_coins > 0 {
                attachment_count += 1;
            }
            if m.body.billing_amount > 0 {
                attachment_count += 1;
            }
            if m.body.money_amount2 > 0 {
                attachment_count += 1;
            }
            if attachment_count != i32::from(m.header.attachments) {
                tracing::warn!(
                    "Attachment count listed in mailId {} did not match the number of attachments, possible mail or item corruption !",
                    m.id
                );
            }
            // Reset the attachment counter
            m.header.attachments = attachment_count as u8;
            // Set internal delivered flag
            m.is_delivered = m.body.recv_date <= now;
            m.is_dirty = false;
            // Remove from delete list if it's a recycled Id
            let id = m.id;
            st.deleted.retain(|&d| d != id);
            if !st.all.add(id, m) {
                // C#: Dictionary.Add throws (the load aborts).
                tracing::error!("Duplicate mail id {id} in mails table");
            }
        }
    }

    /// `Save(connection, transaction)`: `(updated, deleted)`.
    pub async fn save(
        &self,
        tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
    ) -> crate::Result<(usize, usize)> {
        let (deleted, dirty) = {
            let mut st = self.state();
            let deleted = std::mem::take(&mut st.deleted);
            let mut dirty = Vec::new();
            for (_, m) in st.all.iter_mut() {
                if m.is_dirty {
                    dirty.push(m.clone());
                    m.is_dirty = false;
                }
            }
            (deleted, dirty)
        };
        let deleted_count = deleted.len();
        if !deleted.is_empty() {
            let list = deleted
                .iter()
                .map(i64::to_string)
                .collect::<Vec<_>>()
                .join(",");
            sqlx::query(&format!("DELETE FROM mails WHERE `id` IN({list})"))
                .execute(&mut **tx)
                .await?;
        }
        let mut updated = 0;
        for m in &dirty {
            let mut q = sqlx::query(MAIL_REPLACE)
                .bind(m.id)
                .bind(i32::from(m.mail_type))
                .bind(i32::from(m.header.status))
                .bind(&m.title)
                .bind(&m.body.text)
                .bind(m.header.sender_id)
                .bind(&m.header.sender_name)
                .bind(i32::from(m.header.attachments))
                .bind(m.header.receiver_id)
                .bind(&m.receiver_name)
                .bind(from_unix(m.open_date))
                .bind(from_unix(m.body.send_date))
                .bind(from_unix(m.body.recv_date))
                .bind(i32::from(m.header.returned))
                .bind(m.header.extra)
                .bind(m.body.copper_coins)
                .bind(m.body.billing_amount)
                .bind(m.body.money_amount2);
            for i in 0..MAX_MAIL_ATTACHMENTS {
                q = q.bind(m.body.attachments.get(i).copied().unwrap_or(0));
            }
            match q.execute(&mut **tx).await {
                Ok(_) => updated += 1,
                Err(e) => {
                    // Keep it for the next save (the C# exception aborts the transaction).
                    self.with_mail_mut(m.id, |x| x.is_dirty = true);
                    return Err(e.into());
                }
            }
        }
        Ok((updated, deleted_count))
    }
}

/// A `mails` row.
#[derive(Debug, Clone, Default)]
pub struct MailRow {
    pub id: i64,
    pub mail_type: i64,
    pub status: i64,
    pub title: String,
    pub text: String,
    pub sender_id: i64,
    pub sender_name: String,
    pub attachment_count: i64,
    pub receiver_id: i64,
    pub receiver_name: String,
    pub open_date: UnixTime,
    pub send_date: UnixTime,
    pub received_date: UnixTime,
    pub returned: i64,
    pub extra: i64,
    pub money1: i64,
    pub money2: i64,
    pub money3: i64,
    pub attachment_ids: [u64; MAX_MAIL_ATTACHMENTS],
}

const MAIL_SELECT: &str = "SELECT CAST(id AS SIGNED) AS id, CAST(`type` AS SIGNED) AS `type`, \
     CAST(status AS SIGNED) AS status, title, `text`, CAST(sender_id AS SIGNED) AS sender_id, sender_name, \
     CAST(attachment_count AS SIGNED) AS attachment_count, CAST(receiver_id AS SIGNED) AS receiver_id, \
     receiver_name, open_date, send_date, received_date, CAST(returned AS SIGNED) AS returned, \
     CAST(extra AS SIGNED) AS extra, CAST(money_amount_1 AS SIGNED) AS money_amount_1, \
     CAST(money_amount_2 AS SIGNED) AS money_amount_2, CAST(money_amount_3 AS SIGNED) AS money_amount_3, \
     CAST(attachment0 AS UNSIGNED) AS attachment0, CAST(attachment1 AS UNSIGNED) AS attachment1, \
     CAST(attachment2 AS UNSIGNED) AS attachment2, CAST(attachment3 AS UNSIGNED) AS attachment3, \
     CAST(attachment4 AS UNSIGNED) AS attachment4, CAST(attachment5 AS UNSIGNED) AS attachment5, \
     CAST(attachment6 AS UNSIGNED) AS attachment6, CAST(attachment7 AS UNSIGNED) AS attachment7, \
     CAST(attachment8 AS UNSIGNED) AS attachment8, CAST(attachment9 AS UNSIGNED) AS attachment9 \
     FROM mails";

const MAIL_REPLACE: &str = "REPLACE INTO mails(\
     `id`,`type`,`status`,`title`,`text`,`sender_id`,`sender_name`,\
     `attachment_count`,`receiver_id`,`receiver_name`,`open_date`,`send_date`,`received_date`,\
     `returned`,`extra`,`money_amount_1`,`money_amount_2`,`money_amount_3`,\
     `attachment0`,`attachment1`,`attachment2`,`attachment3`,`attachment4`,`attachment5`,\
     `attachment6`,`attachment7`,`attachment8`,`attachment9`\
     ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";

/// `GetNewMailId()` on a locked store.
fn new_mail_id(server: &GameServer, st: &mut MailState) -> u32 {
    let id = match server.ids.mail.get_next_id() {
        Ok(id) => id,
        Err(e) => {
            tracing::error!("{e}");
            0
        }
    };
    st.deleted.retain(|&d| d != i64::from(id));
    id
}

/// `DeleteMail(long id)` on a locked store.
pub fn delete_mail_in(server: &GameServer, st: &mut MailState, id: i64) -> bool {
    if !st.deleted.contains(&id) {
        st.deleted.push(id);
    }
    server.ids.mail.release_id(id as u32);
    st.all.remove(id).is_some()
}

/// `ExtractExtraForHouse(extra, out zoneGroupId, out houseId)` -> `(zoneGroupId, houseId)`.
pub fn extract_extra_for_house(extra: i64) -> (u16, u32) {
    let house_id = (extra & 0xFFFF_FFFF) as u32;
    let zone_group_id = ((extra >> 48) & 0xFFFF) as u16;
    (zone_group_id, house_id)
}

// ---------------------------------------------------------------------------------------------
// Item helpers (attachments are item ids)
// ---------------------------------------------------------------------------------------------

/// Runs `f` on the item object `id` wherever it is (a persistent container or the loose items).
pub fn mutate_item(server: &GameServer, id: u64, f: impl FnOnce(&mut Item)) -> bool {
    if let Some(c) = server.items.holding_container(id) {
        let mut g = c.lock().expect("container poisoned");
        if let Some(i) = g.get_item_by_id_mut(id) {
            f(i);
            return true;
        }
    }
    if let Some(mut i) = server.items.take_loose(id) {
        f(&mut i);
        server.items.add_loose(i);
        return true;
    }
    false
}

/// `BaseMail.RenumberSlots()`: every attachment gets `SlotType.Mail` and its index.
pub fn renumber_slots(server: &GameServer, mail: &BaseMail) {
    for (i, id) in mail.body.attachments.iter().enumerate() {
        mutate_item(server, *id, |it| {
            it.slot_type = slot_type::MAIL;
            it.slot = i as i32;
            it.dirty = true;
        });
    }
}

/// `character.Inventory.MailAttachments`.
pub fn mail_container(player: &PlayerRef) -> Option<ContainerRef> {
    Inv::of(player).get(slot_type::MAIL)
}

/// `container.AddOrMoveExistingItem(task, item, preferredSlot)` for an item that may be in any
/// container (its `_holdingContainer`) or loose. Returns the C# result.
pub fn add_or_move_existing_item(
    txn: &mut ItemTxn<'_>,
    target: &ContainerRef,
    task: u8,
    item_id: u64,
    preferred_slot: i32,
) -> bool {
    let server = txn.server;
    let data = &server.game_data.items;
    match server.items.holding_container(item_id) {
        Some(src) if Arc::ptr_eq(&src, target) => target
            .lock()
            .expect("container poisoned")
            .relocate_item(task, item_id, preferred_slot, data, &mut txn.env.fx),
        Some(src) => {
            let (mut t, s) = inventory::lock_pair(target, &src);
            let Some(mut s) = s else { return false };
            t.move_item_from(&mut s, task, item_id, preferred_slot, data, &mut txn.env.fx)
        }
        None => {
            let Some(item) = server.items.take_loose(item_id) else {
                // C#: `item` is null -> NullReferenceException.
                tracing::error!("AddOrMoveExistingItem: item {item_id} no longer exists");
                return false;
            };
            let res = target.lock().expect("container poisoned").add_item(
                task,
                item,
                preferred_slot,
                data,
                &mut txn.env.fx,
            );
            match res {
                Ok(r) => r,
                Err(item) => {
                    server.items.add_loose(item);
                    false
                }
            }
        }
    }
}

// ---------------------------------------------------------------------------------------------
// MailManager operations
// ---------------------------------------------------------------------------------------------

/// `BaseMail.Send()`: recount the attachments, renumber their slots, `MailManager.Send`.
pub fn send(txn: &mut ItemTxn<'_>, mail: BaseMail) -> bool {
    send_mail(txn, mail).is_some()
}

/// [`send`] returning the id of the stored mail.
pub fn send_mail(txn: &mut ItemTxn<'_>, mut mail: BaseMail) -> Option<i64> {
    mail.header.attachments = mail.get_total_attachment_count();
    renumber_slots(txn.server, &mail);
    manager_send(txn, mail)
}

/// `MailManager.Send(mail)`: verifies the receiver, assigns an id, stores the mail and notifies
/// the receiver when online. Returns the mail id (`None` = `false`).
pub fn manager_send(txn: &mut ItemTxn<'_>, mut mail: BaseMail) -> Option<i64> {
    let server = txn.server;
    // Verify Receiver
    let target_name = server.names.get_character_name(mail.header.receiver_id);
    let target_id = server.names.get_character_id(&mail.receiver_name);
    let names_match = match &target_name {
        Some(n) => n.to_lowercase() == mail.receiver_name.to_lowercase(),
        None => false,
    };
    if !names_match {
        tracing::debug!(
            "Send() - Failed to verify receiver name {:?} != {}",
            target_name,
            mail.receiver_name
        );
        return None; // Name mismatch
    }
    if target_id != mail.header.receiver_id {
        tracing::debug!(
            "Send() - Failed to verify receiver id {target_id} != {}",
            mail.header.receiver_id
        );
        return None; // Id mismatch
    }
    let target_name = target_name.unwrap_or_default();
    let mail_manager = &server.systems.economy.mail;
    let mut st = mail_manager.state();
    // Assign a Id if we didn't have one yet
    if mail.id <= 0 {
        tracing::trace!("Send() - Assign new mail Id");
        mail.id = i64::from(new_mail_id(server, &mut st));
        mail.is_dirty = true;
    }
    let id = mail.id;
    if !st.all.add(id, mail) {
        // C#: Dictionary.Add throws ArgumentException.
        tracing::error!("Send() - mail {id} is already stored");
        return None;
    }
    let m = st.all.get_mut(id).expect("just added");
    notify_new_mail_by_name_if_online(server, &mut txn.env.fx, m, &target_name);
    Some(id)
}

/// `NotifyNewMailByNameIfOnline(m, receiverName)`.
pub fn notify_new_mail_by_name_if_online(
    server: &GameServer,
    fx: &mut ItemFxList,
    m: &mut BaseMail,
    receiver_name: &str,
) -> bool {
    tracing::trace!("NotifyNewMailByNameIfOnline() - {receiver_name}");
    // If unread and ready to deliver
    if m.header.status != mail_status::READ && m.body.recv_date <= now() && !m.is_delivered {
        if let Some(player) = server.world.character_by_name(receiver_name) {
            // TODO: Mia mail stuff
            let add_body = m.mail_type == mail_type::CHARGED;
            let count = {
                let mut c = player.data();
                c.unread_mail.update_received(m.mail_type, 1);
                c.unread_mail
            };
            fx.send(
                player.character_id,
                SCGotMailPacket {
                    mail: m.header_wire(),
                    count,
                    is_cancel: false,
                    body: add_body.then(|| m.body_wire(server)),
                },
            );
            m.is_delivered = true;
            return true;
        }
    }
    false
}

/// `NotifyDeleteMailByNameIfOnline(m, receiverName)`.
pub fn notify_delete_mail_by_name_if_online(
    server: &GameServer,
    fx: &mut ItemFxList,
    m: &BaseMail,
    receiver_name: &str,
) -> bool {
    tracing::trace!("NotifyDeleteMailByNameIfOnline() - {receiver_name}");
    let Some(player) = server.world.character_by_name(receiver_name) else {
        return false;
    };
    let count = {
        let mut c = player.data();
        if m.header.status != mail_status::READ {
            c.unread_mail.update_received(m.mail_type, -1);
        }
        c.unread_mail
    };
    fx.send(
        player.character_id,
        SCMailDeletedPacket {
            is_sent: false,
            mail_id: m.id,
            is_unread_mail_count_modified: true,
            count,
        },
    );
    true
}

/// `GetCurrentMailList(characterId)`: the ids of the delivered mails the character sent or
/// received. When the character is online its unread counters are recomputed and an
/// `SCGotMailPacket` is sent for every unread mail; every unread mail gets `IsDelivered`.
pub fn get_current_mail_list(txn: &mut ItemTxn<'_>, character_id: u32) -> Vec<i64> {
    let server = txn.server;
    // Try to grab the actual online Character object to send live updates
    let character = server.world.character_by_id(character_id);
    let now = now();
    let mut st = server.systems.economy.mail.state();
    let ids: Vec<i64> = st
        .all
        .iter()
        .filter(|(_, m)| {
            m.body.recv_date <= now
                && (m.header.receiver_id == character_id || m.header.sender_id == character_id)
        })
        .map(|(k, _)| k)
        .collect();
    if let Some(p) = &character {
        p.data().unread_mail.reset_received();
    }
    for id in &ids {
        let m = st.all.get_mut(*id).expect("listed");
        if m.header.status != mail_status::READ {
            if let Some(p) = &character {
                let count = {
                    let mut c = p.data();
                    c.unread_mail.update_received(m.mail_type, 1);
                    c.unread_mail
                };
                let add_body = m.mail_type == mail_type::CHARGED;
                txn.env.fx.send(
                    p.character_id,
                    SCGotMailPacket {
                        mail: m.header_wire(),
                        count,
                        is_cancel: false,
                        body: add_body.then(|| m.body_wire(server)),
                    },
                );
            }
            m.is_delivered = true;
        }
    }
    ids
}

/// [`get_current_mail_list`] in its own operation (character select: `Character.Load` calls
/// it before the character is added to the world, so only the `IsDelivered` flags change).
pub fn get_current_mail_list_for(server: &GameServer, character_id: u32) -> Vec<i64> {
    let mut txn = ItemTxn::begin(server);
    get_current_mail_list(&mut txn, character_id)
}

/// `CheckAllMailTimings()` (the `MailDeliveryTask`).
pub fn check_all_mail_timings(txn: &mut ItemTxn<'_>) {
    tracing::trace!("CheckAllMailTimings");
    let server = txn.server;
    let now = now();
    let mut st = server.systems.economy.mail.state();
    let undelivered: Vec<i64> = st
        .all
        .iter()
        .filter(|(_, m)| m.body.recv_date <= now && !m.is_delivered)
        .map(|(k, _)| k)
        .collect();
    let mut delivered = 0;
    for id in &undelivered {
        let m = st.all.get_mut(*id).expect("listed");
        let name = m.receiver_name.clone();
        if notify_new_mail_by_name_if_online(server, &mut txn.env.fx, m, &name) {
            delivered += 1;
        }
    }
    if delivered > 0 {
        tracing::debug!("{delivered}/{} mail(s) delivered", undelivered.len());
    }
    // TODO: Return expired mails back to owner if undelivered/unread
}

/// Schedules the `MailDeliveryTask` (30 s, then every 5 s).
pub fn start_delivery_task(server: &Arc<GameServer>) {
    let weak = Arc::downgrade(server);
    tokio::spawn(async move {
        tokio::time::sleep(std::time::Duration::from_secs(30)).await;
        let mut tick = tokio::time::interval(std::time::Duration::from_secs(5));
        loop {
            tick.tick().await;
            let Some(server) = weak.upgrade() else { break };
            let mut txn = ItemTxn::begin(&server);
            check_all_mail_timings(&mut txn);
        }
    });
}

/// `DeleteMail(BaseMail mail, bool trashItems)`.
pub fn delete_mail_trash(txn: &mut ItemTxn<'_>, mail_id: i64, trash_items: bool) -> bool {
    let server = txn.server;
    if trash_items {
        let attachments = server
            .systems
            .economy
            .mail
            .get_mail_by_id(mail_id)
            .map(|m| m.body.attachments)
            .unwrap_or_default();
        for (i, item_id) in attachments.iter().enumerate().rev() {
            match server.items.holding_container(*item_id) {
                Some(c) => {
                    c.lock().expect("container poisoned").remove_item_task(
                        task_type::INVALID,
                        *item_id,
                        true,
                        &mut txn.env,
                    );
                }
                None => tracing::error!(
                    "Failed to remove mail attachment [{i}] from {mail_id}: item has no container"
                ),
            }
        }
    }
    server.systems.economy.mail.delete_mail(server, mail_id)
}

/// `PayChargeMoney(character, mailId, autoUseAAPoint)`.
pub fn pay_charge_money(
    txn: &mut ItemTxn<'_>,
    character: &PlayerRef,
    mail_id: i64,
    _auto_use_aa_point: bool,
) -> bool {
    let server = txn.server;
    let mails = &server.systems.economy.mail;
    let Some(mail) = mails.get_mail_by_id(mail_id) else {
        inventory::send_error_message_to(&mut txn.out(), character, error_message::MAIL_INVALID);
        return false;
    };
    // Only tax mail supported
    if mail.mail_type != mail_type::BILLING {
        inventory::send_error_message_to(&mut txn.out(), character, error_message::MAIL_INVALID);
        return false;
    }
    let (_zone_group, house_id) = extract_extra_for_house(mail.header.extra);
    let housing = mails.hooks.housing.get();
    let house_exists = housing
        .as_ref()
        .is_some_and(|h| h.house_exists(server, house_id));
    if !house_exists {
        inventory::send_error_message_to(
            &mut txn.out(),
            character,
            error_message::INVALID_HOUSE_INFO,
        );
        return false;
    }

    if server.game_data.features.check(Feature::TaxItem) {
        // use Tax Certificates as payment
        let inv = Inv::of(character);
        let user_tax_count = inv.items_count_in(slot_type::INVENTORY, ITEM_TAX_CERTIFICATE, -1);
        let user_bound_tax_count =
            inv.items_count_in(slot_type::INVENTORY, ITEM_BOUND_TAX_CERTIFICATE, -1);
        let total = user_tax_count + user_bound_tax_count;
        let mut consumed_certs = (mail.body.billing_amount as f32 / 10000f32).ceil() as i32;
        if total < consumed_certs {
            // Not enough certs
            inventory::send_error_message_to(
                &mut txn.out(),
                character,
                error_message::MAIL_NOT_ENOUGH_MONEY_TO_PAY_TAXES,
            );
            return false;
        }
        let bag = inv.bag();
        // Use Bound First
        let mut c = consumed_certs;
        if user_bound_tax_count > 0 && c > 0 {
            if c > user_bound_tax_count {
                c = user_bound_tax_count;
            }
            bag.lock().expect("container poisoned").consume_item(
                task_type::MAIL,
                ITEM_BOUND_TAX_CERTIFICATE,
                c,
                None,
                &mut txn.env,
            );
            consumed_certs -= c;
        }
        c = consumed_certs;
        if user_tax_count > 0 && c > 0 {
            if c > user_tax_count {
                c = user_tax_count;
            }
            bag.lock().expect("container poisoned").consume_item(
                task_type::MAIL,
                ITEM_TAX_CERTIFICATE,
                c,
                None,
                &mut txn.env,
            );
            consumed_certs -= c;
        }
        if consumed_certs != 0 {
            tracing::error!(
                "Something went wrong when paying tax for mailId {}",
                mail.id
            );
        }
        mails.with_mail_mut(mail_id, |m| {
            m.body.billing_amount = consumed_certs;
            m.is_dirty = true;
        });
    } else {
        // use gold as payment
        let money = character.data().money;
        if i64::from(mail.body.billing_amount) > money {
            // Not enough gold
            inventory::send_error_message_to(
                &mut txn.out(),
                character,
                error_message::MAIL_NOT_ENOUGH_MONEY_TO_PAY_TAXES,
            );
            return false;
        }
        inventory::subtract_money(
            &mut txn.out(),
            character,
            slot_type::INVENTORY,
            mail.body.billing_amount,
            task_type::DEPOSIT_MONEY,
        );
    }

    let paid = housing.is_some_and(|h| h.pay_weekly_tax(server, house_id));
    if !paid {
        tracing::error!(
            "Could not update protection time when paying taxes, mailId {}",
            mail.id
        );
    } else {
        let mut st = mails.state();
        let mail_type = mail.mail_type;
        if let Some(m) = st.all.get_mut(mail_id) {
            if m.header.status != mail_status::READ {
                m.header.status = mail_status::READ;
                character.data().unread_mail.update_received(mail_type, -1);
            }
        }
        let count = character.data().unread_mail;
        let cid = character.character_id;
        txn.env.fx.send(cid, SCChargeMoneyPaidPacket { mail_id });
        txn.env.fx.send(
            cid,
            SCMailDeletedPacket {
                is_sent: false,
                mail_id,
                is_unread_mail_count_modified: false,
                count,
            },
        );
        delete_mail_in(server, &mut st, mail_id);
        drop(st);
        super::mail_character::send_unread_mail_count(&mut txn.env.fx, character);
    }
    true
}

/// `DeleteHouseMails(houseId)`.
pub fn delete_house_mails(txn: &mut ItemTxn<'_>, house_id: u32) {
    let server = txn.server;
    let mails = &server.systems.economy.mail;
    // Check which mails to remove
    let delete_list = mails.get_my_house_mails(house_id);
    // Actually remove them by Id
    let mut st = mails.state();
    for d in delete_list {
        let Some(mail) = st.all.get(d).cloned() else {
            continue;
        };
        notify_delete_mail_by_name_if_online(server, &mut txn.env.fx, &mail, &mail.receiver_name);
        delete_mail_in(server, &mut st, d);
    }
}

/// `BaseMail.ReturnToSender()` for the stored mail `mail_id`. The C# re-`Send`s a mail that is
/// already in `_allPlayerMails`, so `Dictionary.Add` throws after the swap: the swap and the
/// `SCMailReturnedPacket` happen, the final notify does not (mirrored, returns `false`).
pub fn return_to_sender(txn: &mut ItemTxn<'_>, mail_id: i64) -> bool {
    let server = txn.server;
    let mails = &server.systems.economy.mail;
    let Some(mail) = mails.get_mail_by_id(mail_id) else {
        return false;
    };
    if !mail.can_return_mail() {
        return false;
    }
    if let Some(r) = server.world.character_by_id(mail.header.receiver_id) {
        txn.env.fx.send(
            r.character_id,
            SCMailReturnedPacket {
                mail_id,
                mail: mail.header_wire(),
            },
        );
    }
    let swapped = mails.with_mail_mut(mail_id, |m| {
        let original_receiver_id = m.header.receiver_id;
        let original_receiver_name = m.receiver_name.clone();
        m.header.receiver_id = m.header.sender_id;
        m.receiver_name = m.header.sender_name.clone();
        m.is_dirty = true;
        m.header.sender_id = original_receiver_id;
        m.header.sender_name = original_receiver_name;
        // Send(): recount + renumber before MailManager.Send
        m.header.attachments = m.get_total_attachment_count();
        m.clone()
    });
    let Some(swapped) = swapped else { return false };
    renumber_slots(server, &swapped);
    // MailManager.Send: the receiver checks pass, then `_allPlayerMails.Add` throws.
    tracing::error!("ReturnToSender() - mail {mail_id} is already stored (C# ArgumentException)");
    false
}

/// The money / item part `send_system_mail` callers commonly need: a ready-to-send system mail
/// (sender id 0) to `receiver_id` with `copper` attached and already-created loose `items`
/// (at most [`MAX_MAIL_ATTACHMENTS`]). Builds the same mail the C# callers build by hand
/// (`new BaseMail { ... }`, `AttachMoney`, `Body.Attachments.Add`) and `Send`s it.
#[allow(clippy::too_many_arguments)]
pub fn send_system_mail(
    txn: &mut ItemTxn<'_>,
    mail_type: u8,
    sender_name: &str,
    receiver_id: u32,
    title: &str,
    text: &str,
    copper: i32,
    items: Vec<Item>,
) -> bool {
    let server = txn.server;
    let Some(receiver_name) = server.names.get_character_name(receiver_id) else {
        for i in items {
            server.items.add_loose(i);
        }
        return false;
    };
    let mut mail = BaseMail {
        mail_type,
        title: title.to_string(),
        receiver_name: receiver_name.clone(),
        ..BaseMail::default()
    };
    mail.header.sender_id = 0;
    mail.header.sender_name = sender_name.to_string();
    mail.header.receiver_id = receiver_id;
    mail.body.text = text.to_string();
    let n = now();
    mail.body.send_date = n;
    mail.body.recv_date = n;
    mail.attach_money(copper, 0, 0);
    // Items go to the receiver's mail container when online, else stay loose.
    let target = server
        .world
        .character_by_name(&receiver_name)
        .and_then(|p| mail_container(&p));
    for mut item in items {
        item.owner_id = u64::from(receiver_id);
        item.slot_type = slot_type::MAIL;
        item.dirty = true;
        let id = item.id;
        match &target {
            Some(c) => {
                let res = c.lock().expect("container poisoned").add_item(
                    task_type::INVALID,
                    item,
                    -1,
                    &server.game_data.items,
                    &mut txn.env.fx,
                );
                if let Err(item) = res {
                    server.items.add_loose(item);
                }
            }
            None => server.items.add_loose(item),
        }
        mail.body.attachments.push(id);
    }
    send(txn, mail)
}
