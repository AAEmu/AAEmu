//! Port of `Core/Managers/AuctionManager.cs` and `Models/Game/Auction/**` (`AuctionLot`,
//! `AuctionBid`, `AuctionDisplay`, `AuctionSearch`, `AuctionDuration`, `AuctionSearchSortKind`,
//! `AuctionSearchSortOrder`, `AuctionHouseTask`) plus `Models/Game/Items/AuctionSettings.cs`
//! (the template's `auction_category_a/b/c`).
//!
//! * Lots live in [`AuctionManager`] (`server.systems.economy.auction`), keyed by lot id.
//! * Listed items are moved into the seller's `SlotType.Auction` container
//!   (`Inventory.AuctionAttachments`); the lot keeps a snapshot of the item and packets use the
//!   live item (`ItemManager.GetItemByItemId`) when it still exists.
//! * Results are delivered as `MailForAuction` mails through [`AuctionMailHooks`] (installed by
//!   the mail port), at the same point as the C# `Send()` calls.
//! * `AuctionHouseTask` (every 5 s) is [`AuctionManager::start_timer`], started from
//!   `System::start` after [`AuctionManager::load`].

use std::collections::BTreeMap;
use std::sync::{Arc, Mutex, MutexGuard};

use aaemu_commons::{Marshal, PacketStream};
use chrono::{DateTime, Duration, NaiveDateTime, Utc};
use sqlx::{MySql, Row, Transaction};

use crate::db::{dt, int, min_value};
use crate::error::Result;
use crate::models::container::slot_type;
use crate::models::item::Item;
use crate::models::item_task::task_type;
use crate::server::GameServer;
use crate::systems::social::achievement::char_record_kind;
use crate::systems::social::achievement_hooks::{AchievementInput, RecordMatch};
use crate::systems::HookSlot;
use crate::world::PlayerRef;

use super::auction_packets::{
    SCAuctionBidPacket, SCAuctionCanceledPacket, SCAuctionLowestPricePacket, SCAuctionPostedPacket,
    SCAuctionSearchedPacket,
};
use super::error_message;
use super::inventory::{self, Inv, ItemTxn};

/// `AuctionManager.MaxListingFee` (100g; 100 copper = 1 silver, 100 silver = 1 gold).
pub const MAX_LISTING_FEE: i32 = 1_000_000;

/// Lots per result page (`Helpers.SplitArray(articles, 9)`).
pub const PAGE_SIZE: usize = 9;

/// `AuctionDuration` (byte; any value can arrive from the client).
pub mod auction_duration {
    pub const HOURS_6: u8 = 0;
    pub const HOURS_12: u8 = 1;
    pub const HOURS_24: u8 = 2;
    pub const HOURS_48: u8 = 3;
}

/// `AuctionSearchSortKind`.
pub mod sort_kind {
    pub const DEFAULT: u8 = 0;
    pub const EVALUATED: u8 = 1;
    pub const ITEM_NAME: u8 = 2;
    pub const ITEM_LEVEL: u8 = 3;
    pub const EXPIRE_DATE: u8 = 4;
    pub const BID_PRICE: u8 = 5;
    pub const DIRECT_PRICE: u8 = 6;
}

/// `AuctionSearchSortOrder`.
pub mod sort_order {
    pub const ASC: u8 = 0;
    pub const DESC: u8 = 1;
}

/// `AuctionLot`.
#[derive(Debug, Clone, PartialEq)]
pub struct AuctionLot {
    pub id: u64,
    /// `AuctionDuration` (0 = 6 h, 1 = 12 h, 2 = 24 h, 3 = 48 h).
    pub duration: u8,
    /// `Item` (`null` when the item id of a DB row no longer exists).
    pub item: Option<Item>,
    pub end_time: DateTime<Utc>,
    pub world_id: u8,
    pub client_id: u32,
    pub client_name: String,
    pub start_money: i32,
    pub direct_money: i32,
    pub post_date: DateTime<Utc>,
    pub bid_world_id: u8,
    pub bidder_id: u32,
    pub bidder_name: String,
    pub bid_money: i32,
    pub extra: i32,
    /// `IsDirty`.
    pub dirty: bool,
}

impl AuctionLot {
    /// `TimeLeft` (seconds; the C# casts a negative double to ulong).
    pub fn time_left(&self, now: DateTime<Utc>) -> u64 {
        (self.end_time - now).num_seconds().max(0) as u64
    }

    /// `Item.Id` (0 without item).
    pub fn item_id(&self) -> u64 {
        self.item.as_ref().map_or(0, |i| i.id)
    }

    /// `Item.TemplateId` (0 without item).
    pub fn item_template_id(&self) -> u32 {
        self.item.as_ref().map_or(0, |i| i.template_id)
    }

    /// The lot with `Item` replaced by the live item object (`ItemManager.GetItemByItemId`),
    /// as the C# lot references the item object itself.
    pub fn with_live_item(&self, server: &GameServer) -> AuctionLot {
        let mut lot = self.clone();
        if let Some(live) = self
            .item
            .as_ref()
            .and_then(|i| server.items.item_by_id(i.id))
        {
            lot.item = Some(live);
        }
        lot
    }
}

impl Marshal for AuctionLot {
    /// `AuctionLot.Write`.
    fn write(&self, s: &mut PacketStream) {
        s.write_u64(self.id).write_u8(self.duration);
        match &self.item {
            Some(item) => {
                item.write(s);
            }
            // C#: NullReferenceException. Written as an empty item (template id 0).
            None => {
                s.write_u32(0);
            }
        }
        s.write_u8(self.world_id)
            .write_u32(self.client_id)
            .write_str(&self.client_name)
            .write_i32(self.start_money)
            .write_i32(self.direct_money)
            .write_date_time(self.post_date)
            .write_u8(self.bid_world_id)
            .write_u32(self.bidder_id)
            .write_str(&self.bidder_name)
            .write_i32(self.bid_money)
            .write_i32(self.extra);
    }
}

/// `AuctionBid`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct AuctionBid {
    pub lot_id: u64,
    pub world_id: u8,
    pub bidder_id: u32,
    pub bidder_name: String,
    pub money: i32,
}

impl AuctionBid {
    /// `AuctionBid.Read`.
    pub fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            lot_id: s.read_u64()?,
            world_id: s.read_u8()?,
            bidder_id: s.read_u32()?,
            bidder_name: s.read_string()?,
            money: s.read_i32()?,
        })
    }
}

impl Marshal for AuctionBid {
    /// `AuctionBid.Write`.
    fn write(&self, s: &mut PacketStream) {
        s.write_u64(self.lot_id)
            .write_u8(self.world_id)
            .write_u32(self.bidder_id)
            .write_str(&self.bidder_name)
            .write_i32(self.money);
    }
}

/// `AuctionSearch` (`Models/Game/Auction/Templates/AuctionSearchTemplate.cs`).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct AuctionSearch {
    pub keyword: String,
    pub exact_match: bool,
    pub grade: u8,
    pub category_a: u8,
    pub category_b: u8,
    pub category_c: u8,
    pub page: i32,
    pub client_id: u32,
    pub filter: i32,
    pub world_id: u8,
    pub min_item_level: u8,
    pub max_item_level: u8,
    pub sort_kind: u8,
    pub sort_order: u8,
}

impl AuctionSearch {
    /// `AuctionSearch.Read`.
    pub fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            keyword: s.read_string()?,
            exact_match: s.read_bool()?,
            grade: s.read_u8()?,
            category_a: s.read_u8()?,
            category_b: s.read_u8()?,
            category_c: s.read_u8()?,
            page: s.read_i32()?,
            client_id: s.read_u32()?,
            filter: s.read_i32()?,
            world_id: s.read_u8()?,
            min_item_level: s.read_u8()?,
            max_item_level: s.read_u8()?,
            sort_kind: s.read_u8()?,
            sort_order: s.read_u8()?,
        })
    }
}

impl Marshal for AuctionSearch {
    /// `AuctionSearch.Write`.
    fn write(&self, s: &mut PacketStream) {
        s.write_str(&self.keyword)
            .write_bool(self.exact_match)
            .write_u8(self.grade)
            .write_u8(self.category_a)
            .write_u8(self.category_b)
            .write_u8(self.category_c)
            .write_i32(self.page)
            .write_u32(self.client_id)
            .write_i32(self.filter)
            .write_u8(self.world_id)
            .write_u8(self.min_item_level)
            .write_u8(self.max_item_level)
            .write_u8(self.sort_kind)
            .write_u8(self.sort_order);
    }
}

/// An item as read by the base `Item.Read` (client copies inside C->S lots; only used to keep
/// the stream position, the server never trusts it).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct WireItem {
    pub template_id: u32,
    pub id: u64,
    pub grade: u8,
    pub flags: u8,
    pub count: i32,
    pub detail_type: u8,
    pub detail: Vec<u8>,
    pub create_time: i64,
    pub lifespan_mins: i32,
    pub made_unit_id: u32,
    pub world_id: u8,
    pub unsecure_time: i64,
    pub unpack_time: i64,
}

impl WireItem {
    /// `Item.Read` + the base `Item.ReadDetails`.
    pub fn read(s: &mut PacketStream) -> Result<Self> {
        let mut i = WireItem {
            template_id: s.read_u32()?,
            ..WireItem::default()
        };
        if i.template_id == 0 {
            return Ok(i);
        }
        i.id = s.read_u64()?;
        i.grade = s.read_u8()?;
        i.flags = s.read_u8()?;
        i.count = s.read_i32()?;
        i.detail_type = s.read_u8()?;
        let len: i32 = match i.detail_type {
            1 => 56,      // Equipment
            2 => 30,      // Slave
            3 => 7,       // Mate
            4 => 10,      // Ucc
            5 | 11 => 25, // Treasure / Location
            6 | 7 => 17,  // BigFish / Decoration
            8 => 9,       // MusicSheet
            9 => 5,       // Glider
            10 => 13,     // SlaveEquipment
            _ => 0,
        } - 1;
        if len > 0 {
            i.detail = s.read_bytes(len as usize)?;
        }
        i.create_time = s.read_i64()?;
        i.lifespan_mins = s.read_i32()?;
        i.made_unit_id = s.read_u32()?;
        i.world_id = s.read_u8()?;
        i.unsecure_time = s.read_i64()?;
        i.unpack_time = s.read_i64()?;
        Ok(i)
    }
}

/// An `AuctionLot` as read by `AuctionLot.Read` (C->S packets; `AuctionDisplay` wraps one).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct ClientAuctionLot {
    pub id: u64,
    pub duration: u8,
    pub item: WireItem,
    pub world_id: u8,
    pub client_id: u32,
    pub client_name: String,
    pub start_money: i32,
    pub direct_money: i32,
    /// `DateTime.FromBinary(ReadInt64())` (raw value).
    pub post_date: i64,
    pub bid_world_id: u8,
    pub bidder_id: u32,
    pub bidder_name: String,
    pub bid_money: i32,
    pub extra: i32,
}

impl ClientAuctionLot {
    /// `AuctionLot.Read`.
    pub fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            id: s.read_u64()?,
            duration: s.read_u8()?,
            item: WireItem::read(s)?,
            world_id: s.read_u8()?,
            client_id: s.read_u32()?,
            client_name: s.read_string()?,
            start_money: s.read_i32()?,
            direct_money: s.read_i32()?,
            post_date: s.read_i64()?,
            bid_world_id: s.read_u8()?,
            bidder_id: s.read_u32()?,
            bidder_name: s.read_string()?,
            bid_money: s.read_i32()?,
            extra: s.read_i32()?,
        })
    }
}

/// One `MailForAuction` (constructor arguments + the `FinalizeFor*` call + `Send()`).
///
/// `item` variants carry a snapshot of the item object the C# passes to the constructor. For
/// `SaleSeller` / `SaleBuyer` / `Fail` it is the listed item, which still sits in the seller's
/// `SlotType.Auction` container (C#: `_holdingContainer` stays the auction container; the
/// finalizer only sets `OwnerId` / `SlotType = Mail` on it). For `Cancel` it is a new item
/// (`ItemManager.Create(template, count, grade)`) registered as a loose item in
/// `server.items` (the original item stays in the auction container, C# bug mirrored).
#[derive(Debug, Clone, PartialEq)]
pub enum AuctionMail {
    /// `new MailForAuction(item, sellerId, buyoutPrice, listingFee)`
    /// `.FinalizeForSaleSeller(sellerShare, tradeTaxFee)` (money only, no attachment).
    SaleSeller {
        item: Item,
        seller_id: u32,
        buyout_price: i32,
        listing_fee: i32,
        seller_share: i32,
        trade_tax_fee: i32,
    },
    /// `new MailForAuction(item, sellerId, buyoutPrice, listingFee).FinalizeForSaleBuyer(buyerId)`
    /// (item attached; `buyer_id` is `NameManager.GetCharacterId(buyerName)`, 0 if unknown).
    SaleBuyer {
        item: Item,
        seller_id: u32,
        buyout_price: i32,
        listing_fee: i32,
        buyer_id: u32,
    },
    /// `new MailForAuction(item, sellerId, buyoutPrice, listingFee).FinalizeForCancel()`.
    Cancel {
        item: Item,
        seller_id: u32,
        buyout_price: i32,
        listing_fee: i32,
    },
    /// `new MailForAuction(item, sellerId, buyoutPrice, listingFee).FinalizeForFail()`.
    Fail {
        item: Item,
        seller_id: u32,
        buyout_price: i32,
        listing_fee: i32,
    },
    /// `new MailForAuction(itemTemplateId, sellerId, buyoutPrice, listingFee)`
    /// `.FinalizeForBidFail(previousBuyerId, previousBid)` (refund of the outbid money).
    BidFail {
        item_template_id: u32,
        seller_id: u32,
        buyout_price: i32,
        listing_fee: i32,
        previous_buyer_id: u32,
        previous_bid: i32,
    },
}

/// Credits `kind_id` (+1) to the online character `character_id`, if any.
fn report_auction(server: &GameServer, character_id: u32, kind_id: u32) {
    if let Some(player) = server.world.character_by_id(character_id) {
        crate::systems::report_achievement(
            server,
            &player,
            AchievementInput::Increment {
                kind_id,
                amount: 1,
                matcher: RecordMatch::Any,
            },
        );
    }
}

/// `MailForAuction` creation + `Send()`, implemented by the mail port and installed into
/// `server.systems.economy.auction.mail`. Called at the C# `Send()` point, with no container /
/// character lock held and outside any `ItemTxn` (the implementation may start its own), but
/// while the auction operation lock is held: it must not call back into [`AuctionManager`].
/// The return value is `Send()`'s (ignored by the auction code, as in C#). Without a hook the
/// mail is dropped with a warning.
pub trait AuctionMailHooks: Send + Sync {
    fn send_auction_mail(&self, server: &GameServer, mail: AuctionMail) -> bool;
}

/// `AuctionManager`.
#[derive(Debug, Default)]
pub struct AuctionManager {
    /// `AuctionLots` (by id; the C# `ConcurrentDictionary` enumeration order is replaced by id
    /// order).
    lots: Mutex<BTreeMap<u64, AuctionLot>>,
    /// `DeletedAuctionItemIds`.
    deleted: Mutex<Vec<u64>>,
    deletion_versions: Mutex<BTreeMap<u64, Arc<()>>>,
    pending_save: Mutex<Option<AuctionSaveBatch>>,
    /// `MailForAuction` delivery (mail port).
    pub mail: HookSlot<dyn AuctionMailHooks>,
    /// Auction operation lock (taken before any `ItemTxn`).
    op: Mutex<()>,
}

#[derive(Debug)]
pub(crate) struct AuctionSaveBatch {
    deleted: Vec<(u64, Arc<()>)>,
    dirty: Vec<AuctionLot>,
}

/// `lot.DirectMoney * .01 * ((int)duration + 1)`, capped at `MaxListingFee`.
pub fn listing_fee(direct_money: i32, duration: u8) -> f64 {
    let fee = f64::from(direct_money) * 0.01 * f64::from(i32::from(duration) + 1);
    if fee > f64::from(MAX_LISTING_FEE) {
        f64::from(MAX_LISTING_FEE)
    } else {
        fee
    }
}

/// `SortArticles(articles, kind, order)` (LINQ `OrderBy` is stable; unknown kinds keep the
/// order).
pub fn sort_articles(
    server: &GameServer,
    mut articles: Vec<AuctionLot>,
    kind: u8,
    order: u8,
) -> Vec<AuctionLot> {
    fn by<K: Ord>(v: &mut [AuctionLot], order: u8, key: impl Fn(&AuctionLot) -> K) {
        if order == sort_order::ASC {
            v.sort_by_key(|l| key(l));
        } else {
            v.sort_by_key(|l| std::cmp::Reverse(key(l)));
        }
    }
    let level = |l: &AuctionLot| {
        server
            .game_data
            .items
            .template(l.item_template_id())
            .map_or(0, |t| t.level)
    };
    match kind {
        sort_kind::BID_PRICE => by(&mut articles, order, |l| l.bid_money),
        sort_kind::DIRECT_PRICE => by(&mut articles, order, |l| l.direct_money),
        // C# sorts "ExpireDate" by PostDate.
        sort_kind::EXPIRE_DATE => by(&mut articles, order, |l| l.post_date),
        sort_kind::ITEM_LEVEL => by(&mut articles, order, level),
        _ => {}
    }
    articles
}

impl AuctionManager {
    fn lots(&self) -> MutexGuard<'_, BTreeMap<u64, AuctionLot>> {
        self.lots.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// Snapshot of `AuctionLots.Values`.
    pub fn all_lots(&self) -> Vec<AuctionLot> {
        self.lots().values().cloned().collect()
    }

    /// `GetAuctionLotFromId`.
    pub fn lot(&self, id: u64) -> Option<AuctionLot> {
        self.lots().get(&id).cloned()
    }

    /// Snapshot of `DeletedAuctionItemIds`.
    pub fn deleted_ids(&self) -> Vec<u64> {
        self.deleted
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .clone()
    }

    /// Replaces a lot (tests / `UpdateAuctionLotInList`).
    pub fn set_lot(&self, lot: AuctionLot) {
        self.lots().insert(lot.id, lot);
    }

    /// `AddAuctionLot`.
    pub fn add_auction_lot(&self, lot: AuctionLot) {
        let mut lots = self.lots();
        if lots.contains_key(&lot.id) {
            tracing::warn!(
                "Unable to add Auction Lot with Id {}, possible duplicate Id",
                lot.id
            );
            return;
        }
        lots.insert(lot.id, lot);
    }

    /// `RemoveAuctionLot`.
    fn remove_auction_lot(&self, server: &GameServer, id: u64) {
        let mut lots = self.lots();
        if !lots.contains_key(&id) {
            return;
        }
        server.ids.auction.release_id(id as u32);
        self.deleted
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .push(id);
        self.deletion_versions
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .insert(id, Arc::new(()));
        lots.remove(&id);
    }

    /// `mail.Send()` (never inside an `ItemTxn`).
    fn send_mail(&self, server: &GameServer, mail: AuctionMail) {
        match self.mail.get() {
            Some(h) => {
                h.send_auction_mail(server, mail);
            }
            None => tracing::warn!("Auction mail not delivered (no mail hook): {mail:?}"),
        }
    }

    /// Serialises the auction operations (outermost lock; mails are sent while it is held).
    fn op(&self) -> MutexGuard<'_, ()> {
        self.op.lock().unwrap_or_else(|e| e.into_inner())
    }

    pub(crate) fn operation_lock(&self) -> MutexGuard<'_, ()> {
        self.op()
    }

    pub(crate) fn prepare_save(&self, server: &GameServer) -> AuctionSaveBatch {
        let ids = self.deleted_ids();
        let versions = self
            .deletion_versions
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .clone();
        AuctionSaveBatch {
            deleted: ids
                .into_iter()
                .filter_map(|id| versions.get(&id).cloned().map(|v| (id, v)))
                .collect(),
            dirty: self
                .lots()
                .values()
                .filter(|lot| lot.dirty)
                .map(|lot| lot.with_live_item(server))
                .collect(),
        }
    }

    pub fn commit_save(&self) {
        let Some(batch) = self
            .pending_save
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .take()
        else {
            return;
        };
        let mut versions = self
            .deletion_versions
            .lock()
            .unwrap_or_else(|e| e.into_inner());
        let mut deleted = self.deleted.lock().unwrap_or_else(|e| e.into_inner());
        for (id, version) in batch.deleted {
            if versions.get(&id).is_some_and(|v| Arc::ptr_eq(v, &version)) {
                versions.remove(&id);
                deleted.retain(|pending| *pending != id);
            }
        }
        drop(deleted);
        drop(versions);
        let mut lots = self.lots();
        for snapshot in batch.dirty {
            if let Some(live) = lots.get_mut(&snapshot.id) {
                if *live == snapshot {
                    live.dirty = false;
                }
            }
        }
    }

    /// `RemoveAuctionLotSold(itemToRemove, buyer, soldAmount)`.
    fn remove_auction_lot_sold(
        &self,
        server: &GameServer,
        lot: &AuctionLot,
        buyer: &str,
        sold_amount: i32,
    ) {
        if !self.lots().contains_key(&lot.id) {
            return;
        }
        if let Some(new_item) = lot
            .item
            .as_ref()
            .and_then(|i| server.items.item_by_id(i.id))
        {
            let money_after_fee = f64::from(sold_amount) * 0.9;
            let recalculated_fee = listing_fee(lot.direct_money, lot.duration);
            if !lot.client_name.is_empty() {
                self.send_mail(
                    server,
                    AuctionMail::SaleSeller {
                        item: new_item.clone(),
                        seller_id: lot.client_id,
                        buyout_price: sold_amount,
                        listing_fee: recalculated_fee as i32,
                        seller_share: money_after_fee as i32,
                        trade_tax_fee: (f64::from(sold_amount) - money_after_fee) as i32,
                    },
                );
            }
            let buyer_id = server.names.get_character_id(buyer);
            self.send_mail(
                server,
                AuctionMail::SaleBuyer {
                    item: new_item,
                    seller_id: lot.client_id,
                    buyout_price: sold_amount,
                    listing_fee: recalculated_fee as i32,
                    buyer_id,
                },
            );
            // AUCTION_SOLD / AUCTION_BUY: only a party that is online is credited.
            if !lot.client_name.is_empty() {
                report_auction(server, lot.client_id, char_record_kind::AUCTION_SOLD);
            }
            report_auction(server, buyer_id, char_record_kind::AUCTION_BUY);
        }
        self.remove_auction_lot(server, lot.id);
    }

    /// `RemoveAuctionLotFail(itemToRemove)`.
    fn remove_auction_lot_fail(&self, server: &GameServer, lot: &AuctionLot) {
        if !self.lots().contains_key(&lot.id) {
            return;
        }
        if !lot.bidder_name.is_empty() {
            // Player won the bid
            self.remove_auction_lot_sold(server, lot, &lot.bidder_name, lot.bid_money);
            return;
        }
        // Item did not sell by end of the timer.
        if let Some(new_item) = lot
            .item
            .as_ref()
            .and_then(|i| server.items.item_by_id(i.id))
        {
            let recalculated_fee = listing_fee(lot.direct_money, lot.duration);
            if !lot.client_name.is_empty() {
                self.send_mail(
                    server,
                    AuctionMail::Fail {
                        item: new_item,
                        seller_id: lot.client_id,
                        buyout_price: lot.direct_money,
                        listing_fee: recalculated_fee as i32,
                    },
                );
            }
        }
        self.remove_auction_lot(server, lot.id);
    }

    /// `CancelAuctionLot(player, auctionId)`: only the seller may cancel a lot without bids.
    /// Return the original escrow item, preserving its ID and details.
    pub fn cancel_auction_lot(&self, server: &GameServer, player: &PlayerRef, auction_id: u64) {
        let _op = self.op();
        let Some(lot) = self.lot(auction_id) else {
            tracing::warn!("AuctionLot with ID {auction_id} not found.");
            return;
        };
        if lot.client_id != player.character_id {
            return;
        }
        if !lot.bidder_name.is_empty() {
            // Someone has already bid on the item, and we do not want to remove it
            tracing::warn!("AuctionLot with ID {auction_id} has already been bid on.");
            return;
        }
        let created = lot
            .item
            .as_ref()
            .and_then(|i| server.items.item_by_id(i.id));
        if let Some(new_item) = created {
            let recalculated_fee = listing_fee(lot.direct_money, lot.duration);
            self.send_mail(
                server,
                AuctionMail::Cancel {
                    item: new_item,
                    seller_id: lot.client_id,
                    buyout_price: lot.direct_money,
                    listing_fee: recalculated_fee as i32,
                },
            );
        }
        self.remove_auction_lot(server, lot.id);
        player.send(SCAuctionCanceledPacket {
            lot: lot.with_live_item(server),
        });
    }

    /// `BidOnAuctionLot(player, auctioneerId, auctioneerId2, lot, bid)`.
    pub fn bid_on_auction_lot(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        lot_id: u64,
        mut bid: AuctionBid,
    ) {
        let _op = self.op();
        let Some(mut lot) = self.lot(lot_id) else {
            tracing::warn!("Invalid auctionItem passed to BidOnAuctionLot.");
            tracing::warn!("AuctionLot with ID {lot_id} not found in the list.");
            return;
        };
        let (player_id, player_name, world_id) = {
            let c = player.data();
            (c.id, c.unit.name.clone(), c.unit.transform.world_id as u8)
        };
        let required = if lot.direct_money > 0 && bid.money >= lot.direct_money {
            lot.direct_money
        } else {
            bid.money
        };
        if player_id == lot.client_id
            || bid.money <= 0
            || bid.money < lot.start_money
            || lot.end_time <= Utc::now()
            || bid.money <= lot.bid_money
        {
            return;
        }

        {
            let mut txn = ItemTxn::begin(server);
            let task = if lot.direct_money > 0 && bid.money >= lot.direct_money {
                task_type::DEPOSIT_MONEY
            } else {
                task_type::AUCTION
            };
            if player.data().money < i64::from(required) {
                return;
            }
            if !inventory::subtract_money(
                &mut txn.out(),
                player,
                slot_type::INVENTORY,
                required,
                task,
            ) {
                return;
            }
        }

        if bid.money >= lot.direct_money && lot.direct_money != 0 {
            // Buy now
            if lot.bidder_id != 0 {
                // send mail to person who bid if item was bought at full price.
                self.send_mail(
                    server,
                    AuctionMail::BidFail {
                        item_template_id: lot.item_template_id(),
                        seller_id: lot.client_id,
                        buyout_price: lot.direct_money,
                        listing_fee: 0,
                        previous_buyer_id: lot.bidder_id,
                        previous_bid: lot.bid_money,
                    },
                );
            }
            self.remove_auction_lot_sold(server, &lot, &player_name, lot.direct_money);
        } else if bid.money > lot.bid_money {
            // Bid
            if !lot.bidder_name.is_empty() && lot.bidder_id != 0 {
                // Send mail to old bidder.
                let recalculated_fee = listing_fee(lot.direct_money, lot.duration);
                self.send_mail(
                    server,
                    AuctionMail::BidFail {
                        item_template_id: lot.item_template_id(),
                        seller_id: lot.client_id,
                        buyout_price: lot.direct_money,
                        listing_fee: recalculated_fee as i32,
                        previous_buyer_id: lot.bidder_id,
                        previous_bid: lot.bid_money,
                    },
                );
            }

            // Set info to new bidders info
            lot.bidder_name = player_name.clone();
            lot.bidder_id = player_id;
            lot.bid_world_id = world_id;
            lot.bid_money = bid.money;

            bid.bidder_name = player_name;
            bid.bidder_id = player_id;
            bid.world_id = world_id;

            player.send(SCAuctionBidPacket {
                bid,
                is_buyout: false,
                item_id: lot.item_template_id(),
            });
            lot.dirty = true;

            // Updating Data in the AuctionLots List
            self.update_auction_lot_in_list(&lot);
        }
    }

    /// `UpdateAuctionLotInList(auctionLot)`.
    fn update_auction_lot_in_list(&self, lot: &AuctionLot) {
        let mut lots = self.lots();
        match lots.get_mut(&lot.id) {
            Some(existing) => {
                existing.bidder_name = lot.bidder_name.clone();
                existing.bidder_id = lot.bidder_id;
                existing.bid_world_id = lot.bid_world_id;
                existing.bid_money = lot.bid_money;
                existing.item = lot.item.clone();
                existing.dirty = lot.dirty;
            }
            None => tracing::warn!("AuctionLot with ID {} not found in the list.", lot.id),
        }
    }

    /// Sends one result page (`SplitArray(articles, 9)` + the out-of-bounds guard).
    fn send_page(
        server: &GameServer,
        player: &PlayerRef,
        articles: Vec<AuctionLot>,
        page: i32,
        tag: &str,
    ) {
        let now = Utc::now();
        if articles.is_empty() {
            player.send(SCAuctionSearchedPacket {
                page: 0,
                count: 0,
                lots: Vec::new(),
                error_msg: error_message::NO_ERROR_MESSAGE,
                server_time: now,
            });
            return;
        }
        let pages = articles.len().div_ceil(PAGE_SIZE);
        if page < 0 || page as usize >= pages {
            // Stops client DC when requesting an out-of-bounds page
            let name = player.data().unit.name.clone();
            tracing::warn!(
                "[{tag}] {name} requested an out-of-bounds page: {page}/{}",
                pages as i64 - 1
            );
            player.send(SCAuctionSearchedPacket {
                page,
                count: 0,
                lots: Vec::new(),
                error_msg: error_message::NO_ERROR_MESSAGE,
                server_time: now,
            });
            return;
        }
        let lots: Vec<AuctionLot> = articles
            .into_iter()
            .skip(page as usize * PAGE_SIZE)
            .take(PAGE_SIZE)
            .map(|l| l.with_live_item(server))
            .collect();
        player.send(SCAuctionSearchedPacket {
            page,
            count: lots.len() as i32,
            lots,
            error_msg: error_message::NO_ERROR_MESSAGE,
            server_time: now,
        });
    }

    /// `GetBidAuctionLots(player, page)`.
    pub fn get_bid_auction_lots(&self, server: &GameServer, player: &PlayerRef, page: i32) {
        let player_id = player.data().id;
        let searched: Vec<AuctionLot> = self
            .lots()
            .values()
            .filter(|l| l.bidder_id == player_id)
            .cloned()
            .collect();
        let articles = sort_articles(server, searched, sort_kind::DEFAULT, sort_order::ASC);
        Self::send_page(server, player, articles, page, "AH-BIDS");
    }

    /// `GetCheapestAuctionLot(templateId)` (first lot with the lowest `DirectMoney`).
    pub fn get_cheapest_auction_lot(&self, template_id: u32) -> Option<AuctionLot> {
        let lots = self.lots();
        let mut best: Option<&AuctionLot> = None;
        for l in lots
            .values()
            .filter(|l| l.item_template_id() == template_id && l.item.is_some())
        {
            if best.is_none_or(|b| l.direct_money < b.direct_money) {
                best = Some(l);
            }
        }
        best.cloned()
    }

    /// `CheapestAuctionLot(player, templateId, itemGrade)`.
    pub fn cheapest_auction_lot(&self, player: &PlayerRef, template_id: u32, item_grade: u8) {
        let money_amount = self
            .get_cheapest_auction_lot(template_id)
            .map_or(0, |l| l.direct_money);
        player.send(SCAuctionLowestPricePacket {
            item_template_id: template_id,
            item_grade,
            money_amount,
        });
    }

    /// `UpdateAuctionHouse()` (`AuctionHouseTask`).
    pub fn update_auction_house(&self, server: &GameServer, now: DateTime<Utc>) {
        tracing::trace!("Updating Auction House!");
        let _op = self.op();
        let expired: Vec<AuctionLot> = self
            .lots()
            .values()
            .filter(|l| now > l.end_time)
            .cloned()
            .collect();
        for lot in &expired {
            if lot.bidder_id != 0 {
                self.remove_auction_lot_sold(server, lot, &lot.bidder_name, lot.bid_money);
            } else {
                self.remove_auction_lot_fail(server, lot);
            }
        }
    }

    /// `CreateAuctionLot(playerId, playerName, itemToList, startPrice, buyoutPrice, duration)`.
    #[allow(clippy::too_many_arguments)]
    pub fn create_auction_lot(
        server: &GameServer,
        player_id: u32,
        player_name: &str,
        item_to_list: Item,
        start_price: i32,
        buyout_price: i32,
        duration: u8,
        now: DateTime<Utc>,
    ) -> Option<AuctionLot> {
        let time_left = match duration {
            auction_duration::HOURS_6 => 6,
            auction_duration::HOURS_12 => 12,
            auction_duration::HOURS_24 => 24,
            auction_duration::HOURS_48 => 48,
            _ => 6, // default to 6 hours
        };
        let id = match server.ids.auction.get_next_id() {
            Ok(id) => u64::from(id),
            Err(e) => {
                tracing::error!("{e}");
                return None;
            }
        };
        Some(AuctionLot {
            id,
            duration,
            item: Some(item_to_list),
            end_time: now + Duration::hours(time_left),
            world_id: 1,
            client_id: player_id,
            client_name: player_name.to_string(),
            start_money: start_price,
            direct_money: buyout_price,
            post_date: now,
            bid_world_id: 255,
            bidder_id: 0,
            bidder_name: String::new(),
            bid_money: 0,
            extra: 0,
            dirty: true,
        })
    }

    /// `PostLotOnAuction(player, npcId, npcId2, itemId, startPrice, buyoutPrice, duration)`.
    pub fn post_lot_on_auction(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        item_id: u64,
        start_price: i32,
        buyout_price: i32,
        duration: u8,
    ) {
        let _op = self.op();
        let mut txn = ItemTxn::begin(server);
        // Only the player's own unbound inventory item can enter auction escrow.
        let Some(item) = server.items.item_by_id(item_id) else {
            return;
        };
        let (player_id, player_name) = {
            let c = player.data();
            (c.id, c.unit.name.clone())
        };
        if item.owner_id != u64::from(player_id)
            || item.slot_type != slot_type::INVENTORY
            || item.flags & crate::models::item::ITEM_FLAG_SOUL_BOUND != 0
            || start_price < 0
            || buyout_price < 0
            || (buyout_price > 0 && buyout_price < start_price)
            || duration > auction_duration::HOURS_48
        {
            return;
        }
        let Some(mut lot) = Self::create_auction_lot(
            server,
            player_id,
            &player_name,
            item,
            start_price,
            buyout_price,
            duration,
            Utc::now(),
        ) else {
            return;
        };

        let auction_fee = listing_fee(lot.direct_money, duration);

        let Some(auction) = Inv::of(player).get(slot_type::AUCTION) else {
            server.ids.auction.release_id(lot.id as u32);
            return;
        };
        if player.data().money < auction_fee as i64
            || !super::mail::add_or_move_existing_item(
                &mut txn,
                &auction,
                task_type::AUCTION,
                item_id,
                -1,
            )
        {
            server.ids.auction.release_id(lot.id as u32);
            inventory::send_error_message_to(
                &mut txn.out(),
                player,
                error_message::CAN_NOT_PUTUP_MONEY,
            );
            return;
        }
        inventory::change_money_at(
            &mut txn.out(),
            player,
            slot_type::INVENTORY,
            -(auction_fee as i32),
            task_type::DEPOSIT_MONEY,
        );
        lot.item = auction
            .lock()
            .expect("container poisoned")
            .get_item_by_id(item_id)
            .cloned();

        let packet = SCAuctionPostedPacket { lot: lot.clone() };
        self.add_auction_lot(lot);
        txn.env.fx.send(player.character_id, packet);
    }

    /// `SearchAuctionLots(player, search)`.
    pub fn search_auction_lots(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        search: &AuctionSearch,
    ) {
        let names = &server.game_data.systems.economy.auction;
        let mut searched = Vec::new();
        for lot in self.lots().values() {
            // C#: lot.Item.Template throws for lots without item / template.
            let Some(item) = &lot.item else { continue };
            let Some(template) = server.game_data.items.template(item.template_id) else {
                continue;
            };
            // Check by ClientId
            if search.client_id != 0 && lot.client_id != search.client_id {
                continue;
            }
            // Check keyword
            if !search.keyword.trim().is_empty()
                && !names.match_item_name(
                    i64::from(template.id),
                    &search.keyword,
                    search.exact_match,
                )
            {
                continue;
            }
            // Check by category and other criteria
            if template.auction_category_a != i32::from(search.category_a) && search.category_a != 0
            {
                continue;
            }
            if template.auction_category_b != i32::from(search.category_b) && search.category_b != 0
            {
                continue;
            }
            if template.auction_category_c != i32::from(search.category_c) && search.category_c != 0
            {
                continue;
            }
            if item.grade != search.grade && search.grade != 0 {
                continue;
            }
            if template.level > i32::from(search.max_item_level) && search.max_item_level != 0 {
                continue;
            }
            if template.level < i32::from(search.min_item_level) && search.min_item_level != 0 {
                continue;
            }
            searched.push(lot.clone());
        }
        let articles = sort_articles(server, searched, search.sort_kind, search.sort_order);
        Self::send_page(server, player, articles, search.page, "AH");
    }

    /// `Load()`: the `auction_house` rows (the `AuctionHouseTask` is started separately by
    /// [`AuctionManager::start_timer`]). Errors are logged, as in C#; returns whether the load
    /// succeeded (the C# only schedules the task then).
    pub async fn load(&self, server: &GameServer) -> bool {
        self.lots().clear();
        self.deleted
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .clear();
        self.deletion_versions
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .clear();
        *self.pending_save.lock().unwrap_or_else(|e| e.into_inner()) = None;
        match self.try_load(server).await {
            Ok(()) => true,
            Err(e) => {
                tracing::error!("Failed to load auction data: {e}");
                false
            }
        }
    }

    async fn try_load(&self, server: &GameServer) -> Result<()> {
        let rows = sqlx::query(
            "SELECT CAST(id AS UNSIGNED) AS id, CAST(duration AS SIGNED) AS duration, \
             CAST(item_id AS SIGNED) AS item_id, post_date, end_time, \
             CAST(world_id AS SIGNED) AS world_id, CAST(client_id AS SIGNED) AS client_id, \
             client_name, CAST(start_money AS SIGNED) AS start_money, \
             CAST(direct_money AS SIGNED) AS direct_money, \
             CAST(bid_world_id AS SIGNED) AS bid_world_id, CAST(bidder_id AS SIGNED) AS bidder_id, \
             bidder_name, CAST(bid_money AS SIGNED) AS bid_money, CAST(extra AS SIGNED) AS extra \
             FROM auction_house",
        )
        .fetch_all(&server.db)
        .await?;
        let to_utc = |t: Option<NaiveDateTime>| t.unwrap_or_else(min_value).and_utc();
        for r in rows {
            let item_id = int(&r, "item_id")? as u32;
            let lot = AuctionLot {
                id: r.try_get::<u64, _>("id")?,
                duration: int(&r, "duration")? as u8,
                item: server.items.item_by_id(u64::from(item_id)),
                post_date: to_utc(dt(&r, "post_date")),
                end_time: to_utc(dt(&r, "end_time")),
                world_id: int(&r, "world_id")? as u8,
                client_id: int(&r, "client_id")? as u32,
                client_name: r.try_get("client_name")?,
                start_money: int(&r, "start_money")? as i32,
                direct_money: int(&r, "direct_money")? as i32,
                bid_world_id: int(&r, "bid_world_id")? as u8,
                bidder_id: int(&r, "bidder_id")? as u32,
                bidder_name: r.try_get("bidder_name")?,
                bid_money: int(&r, "bid_money")? as i32,
                extra: int(&r, "extra")? as i32,
                dirty: false,
            };
            self.add_auction_lot(lot);
        }
        tracing::info!("Loaded {} auction lots", self.lots().len());
        Ok(())
    }

    /// Schedules `AuctionHouseTask` (first run after 5 s, then every 5 s).
    pub fn start_timer(server: &Arc<GameServer>) {
        let weak = Arc::downgrade(server);
        tokio::spawn(async move {
            let period = std::time::Duration::from_secs(5);
            let mut iv = tokio::time::interval_at(tokio::time::Instant::now() + period, period);
            loop {
                iv.tick().await;
                let Some(server) = weak.upgrade() else { break };
                let s = server.clone();
                // Item operations block (std mutexes): run them off the async workers.
                let _ = tokio::task::spawn_blocking(move || {
                    s.systems
                        .economy
                        .auction
                        .update_auction_house(&s, Utc::now());
                })
                .await;
            }
        });
    }

    /// `Save(connection, transaction)` -> `(updatedCount, deletedCount)`.
    pub async fn save(
        &self,
        server: &GameServer,
        tx: &mut Transaction<'_, MySql>,
    ) -> Result<(usize, usize)> {
        let batch = {
            let _op = self.op();
            self.prepare_save(server)
        };
        self.write_prepared(tx, batch).await
    }

    pub(crate) async fn write_prepared(
        &self,
        tx: &mut Transaction<'_, MySql>,
        batch: AuctionSaveBatch,
    ) -> Result<(usize, usize)> {
        *self.pending_save.lock().unwrap_or_else(|e| e.into_inner()) = None;
        let mut deleted_count = 0;
        let deleted = &batch.deleted;
        if !deleted.is_empty() {
            let list = deleted
                .iter()
                .map(|(id, _)| id.to_string())
                .collect::<Vec<_>>()
                .join(",");
            deleted_count = sqlx::query(&format!("DELETE FROM auction_house WHERE `id` IN({list})"))
                .execute(&mut **tx)
                .await?
                .rows_affected() as usize;
        }

        let mut updated_count = 0;
        for lot in &batch.dirty {
            let Some(item) = &lot.item else { continue };
            let item_slot_type = item.slot_type;
            if !slot_type::ALL.contains(&item_slot_type) {
                tracing::warn!(
                    "Found SlotType.{item_slot_type} in itemslist, skipping ID:{} - Template:{}",
                    item.id,
                    item.template_id
                );
                continue;
            }

            let r = sqlx::query(
                "REPLACE INTO auction_house(`id`, `duration`, `item_id`, `post_date`, `stack_size`, `end_time`, \
                 `world_id`, `client_id`, `client_name`, `start_money`, `direct_money`, \
                 `bid_world_id`, `bidder_id`, `bidder_name`, `bid_money`, `extra`\
                 ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            )
            .bind(lot.id)
            .bind(lot.duration)
            .bind(item.id)
            .bind(lot.post_date.naive_utc())
            .bind(item.count)
            .bind(lot.end_time.naive_utc())
            .bind(lot.world_id)
            .bind(lot.client_id)
            .bind(&lot.client_name)
            .bind(lot.start_money)
            .bind(lot.direct_money)
            .bind(lot.bid_world_id)
            .bind(lot.bidder_id)
            .bind(&lot.bidder_name)
            .bind(lot.bid_money)
            .bind(lot.extra)
            .execute(&mut **tx)
            .await?;
            updated_count += r.rows_affected() as usize;
        }
        *self.pending_save.lock().unwrap_or_else(|e| e.into_inner()) = Some(batch);
        Ok((updated_count, deleted_count))
    }
}
