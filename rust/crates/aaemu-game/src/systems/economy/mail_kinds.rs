//! Port of the `BaseMail` descendants: `MailPlayerToPlayer`, `MailForAuction`,
//! `MailForSpeciality`, `MailForTax`, `CommercialMail`, and `MailManager.CreateQuestRewardMails`.
//!
//! Each type wraps the [`BaseMail`] it builds (`.mail`) plus the private C# fields; `send`
//! is `BaseMail.Send()` (see [`mail::send`]). Every item operation needs the caller's
//! [`ItemTxn`].

use crate::models::container::slot_type;
use crate::models::item::{Item, UnixTime};
use crate::models::item_task::{task_type, ItemTask};
use crate::server::GameServer;
use crate::world::PlayerRef;

use super::inventory::{Inv, ItemTxn};
use super::item_packets::SCItemTaskSuccessPacket;
use super::mail::{
    self, mail_status, mail_type, BaseMail, COST_EXPRESS, COST_EXPRESS_ATTACHMENT,
    COST_FREE_ATTACHMENT_COUNT, COST_NORMAL, COST_NORMAL_ATTACHMENT, ITEM_COINS,
};

/// `LocalizationManager.Get("items", "name", templateId, fallback)`.
pub fn item_name(server: &GameServer, template_id: u32, fallback: &str) -> String {
    server
        .game_data
        .systems
        .economy
        .mail
        .item_name(template_id, fallback)
}

/// `ItemManager.Create(templateId, count, grade, true)` (a loose item with a new id).
fn create_loose_item(txn: &ItemTxn<'_>, template_id: u32, count: i32, grade: u8) -> Option<Item> {
    txn.env.create(template_id, count, grade)
}

// ---------------------------------------------------------------------------------------------
// MailPlayerToPlayer
// ---------------------------------------------------------------------------------------------

/// `MailPlayerToPlayer`.
#[derive(Debug, Clone)]
pub struct MailPlayerToPlayer {
    pub mail: BaseMail,
    sender: PlayerRef,
}

impl MailPlayerToPlayer {
    /// `new MailPlayerToPlayer(sender, receiverPlayerName)`.
    pub fn new(server: &GameServer, sender: &PlayerRef, receiver_player_name: &str) -> Self {
        let mut mail = BaseMail::new();
        {
            let c = sender.data();
            mail.header.sender_id = c.id;
            mail.header.sender_name = c.unit.name.clone();
        }
        mail.header.receiver_id = server.names.get_character_id(receiver_player_name);
        mail.receiver_name = receiver_player_name.to_string();
        Self {
            mail,
            sender: sender.clone(),
        }
    }

    /// `GetMailFee()`.
    pub fn get_mail_fee(&self) -> i32 {
        let mut mail_fee = 0;
        let mut attachment_cost = 0;
        let mut attachment_count_for_fee = self.mail.body.attachments.len() as i32;
        if self.mail.body.copper_coins > 0 {
            attachment_count_for_fee += 1;
        }
        if self.mail.mail_type == mail_type::NORMAL {
            mail_fee += COST_NORMAL;
            attachment_cost = COST_NORMAL_ATTACHMENT;
        } else if self.mail.mail_type == mail_type::EXPRESS {
            mail_fee += COST_EXPRESS;
            attachment_cost = COST_EXPRESS_ATTACHMENT;
        }
        // If Invalid mail type, assume zero cost
        // Add cost based on attachments past the first one
        if attachment_count_for_fee > COST_FREE_ATTACHMENT_COUNT {
            mail_fee += (attachment_count_for_fee - COST_FREE_ATTACHMENT_COUNT) * attachment_cost;
        }
        mail_fee
    }

    /// `PrepareAttachmentItems(itemSlotsForMail)`: the sender's bag items to attach (nothing is
    /// moved yet).
    pub fn prepare_attachment_items(&mut self, item_slots: &[(u8, u8)]) -> bool {
        self.mail.body.attachments.clear();
        let inv = Inv::of(&self.sender);
        for (st, slot) in item_slots {
            if *st != 0 {
                match inv.item_at(*st, *slot) {
                    Some(item) if item.slot_type == slot_type::INVENTORY => {
                        self.mail.body.attachments.push(item.id);
                    }
                    // Attachment Items do not match player inventory, abort
                    _ => return false,
                }
            }
        }
        self.mail.header.attachments = self.mail.get_total_attachment_count();
        true
    }

    /// `FinalizeAttachments()`: moves every attachment into the sender's mail container.
    pub fn finalize_attachments(&mut self, txn: &mut ItemTxn<'_>) -> bool {
        let Some(mail_container) = mail::mail_container(&self.sender) else {
            tracing::error!("FinalizeAttachments: sender has no mail container");
            return false;
        };
        let owner = self.sender.character_id;
        for (i, id) in self.mail.body.attachments.clone().into_iter().enumerate() {
            // Move Item to sender's Mail ItemContainer, technically speaking this can never fail
            if mail::add_or_move_existing_item(txn, &mail_container, task_type::INVALID, id, -1) {
                let moved = mail_container
                    .lock()
                    .expect("container poisoned")
                    .get_item_by_id(id)
                    .cloned();
                if let Some(moved) = &moved {
                    txn.env.fx.send(
                        owner,
                        SCItemTaskSuccessPacket {
                            action: task_type::MAIL,
                            tasks: vec![ItemTask::remove(moved)],
                            force_remove: Vec::new(),
                        },
                    );
                }
                // Technically not needed, I just want to sync it up
                mail::mutate_item(txn.server, id, |it| {
                    it.slot_type = slot_type::MAIL;
                    it.slot = i as i32;
                    it.dirty = true;
                });
            } else {
                // Should never be able to fail, abort
                return false;
            }
        }
        true
    }

    /// `Send()`.
    pub fn send(self, txn: &mut ItemTxn<'_>) -> bool {
        mail::send(txn, self.mail)
    }
}

// ---------------------------------------------------------------------------------------------
// MailForAuction
// ---------------------------------------------------------------------------------------------

const AUCTION_NAME: &str = "Auctioneer";
const TITLE_SOLD: &str = "Successful Auction Notice";
const TITLE_NOT_SOLD: &str = "Failed Auction Notice";
const TITLE_BID_WIN: &str = "Succesfull Purchase";
const TITLE_BID_LOST: &str = "Failed Bid Notice";
const TITLE_CANCEL: &str = "Cancelled Auction Notice";

/// `MailForAuction`. The item is referenced by id (it stays wherever it is until taken).
#[derive(Debug, Clone)]
pub struct MailForAuction {
    pub mail: BaseMail,
    buyer_id: u32,
    seller_id: u32,
    item_id: Option<u64>,
    item_name: String,
    item_buyout_price: i32,
    seller_share: i32,
    listing_fee: i32,
    trade_tax_fee: i32,
}

impl MailForAuction {
    fn base(
        item_name: String,
        seller_id: u32,
        item_id: Option<u64>,
        buyout: i32,
        fee: i32,
    ) -> Self {
        let mut mail = BaseMail::new();
        // Correct types and name will be set in finalize functions
        mail.mail_type = mail_type::INVALID_MAIL_TYPE;
        mail.header.sender_id = 0;
        mail.header.sender_name = AUCTION_NAME.to_string(); // Name changes depending on type of mail
        mail.body.recv_date = mail::now(); // These mails should always be instant
        Self {
            mail,
            buyer_id: 0,
            seller_id,
            item_id,
            item_name,
            item_buyout_price: buyout,
            seller_share: 0,
            listing_fee: fee,
            trade_tax_fee: 0,
        }
    }

    /// `new MailForAuction(Item itemToSell, sellerId, buyoutPrice, listingFee)`.
    pub fn new(
        server: &GameServer,
        item_to_sell: &Item,
        seller_id: u32,
        buyout_price: i32,
        listing_fee: i32,
    ) -> Self {
        let name = item_name(
            server,
            item_to_sell.template_id,
            &format!("Item:{}", item_to_sell.template_id),
        );
        Self::base(
            name,
            seller_id,
            Some(item_to_sell.id),
            buyout_price,
            listing_fee,
        )
    }

    /// `new MailForAuction(uint itemTemplateIdToSell, sellerId, buyoutPrice, listingFee)` (no
    /// item: only `FinalizeForBidFail` works, the others hit a null item in C#).
    pub fn new_from_template(
        server: &GameServer,
        item_template_id_to_sell: u32,
        seller_id: u32,
        buyout_price: i32,
        listing_fee: i32,
    ) -> Self {
        let name = item_name(
            server,
            item_template_id_to_sell,
            &format!("Item:{item_template_id_to_sell}"),
        );
        Self::base(name, seller_id, None, buyout_price, listing_fee)
    }

    /// `_item.Count` (the live item).
    fn item_count(&self, server: &GameServer) -> Option<i32> {
        let id = self.item_id?;
        server.items.item_by_id(id).map(|i| i.count)
    }

    /// `_item.OwnerId = owner; _item.SlotType = SlotType.Mail; Body.Attachments.Add(_item)`.
    fn attach_item(&mut self, server: &GameServer, owner: u32) {
        let Some(id) = self.item_id else { return };
        mail::mutate_item(server, id, |i| {
            i.owner_id = u64::from(owner);
            i.slot_type = slot_type::MAIL;
            i.dirty = true;
        });
        self.mail.body.attachments.push(id);
    }

    /// `FinalizeForSaleBuyer(buyerId)`.
    pub fn finalize_for_sale_buyer(&mut self, server: &GameServer, buyer_id: u32) -> bool {
        self.buyer_id = buyer_id;
        let Some(name_buyer) = server.names.get_character_name(self.buyer_id) else {
            return false;
        };
        self.mail.header.sender_name = ".auctionBidWin".to_string();
        self.mail.receiver_name = name_buyer;
        self.mail.mail_type = mail_type::AUC_BID_WIN;
        self.mail.title = TITLE_BID_WIN.to_string();
        self.mail.header.receiver_id = self.buyer_id;
        let Some(count) = self.item_count(server) else {
            tracing::error!("MailForAuction.FinalizeForSaleBuyer: NullReferenceException (_item)");
            return false;
        };
        self.mail.body.text = format!(
            "body('{}', {}, {})",
            self.item_name, count, self.item_buyout_price
        );
        self.attach_item(server, self.buyer_id);
        self.mail.is_dirty = true;
        true
    }

    /// `FinalizeForSaleSeller(sellerShare, tradeTaxFee)`.
    pub fn finalize_for_sale_seller(
        &mut self,
        server: &GameServer,
        seller_share: i32,
        trade_tax_fee: i32,
    ) -> bool {
        self.seller_share = seller_share;
        self.trade_tax_fee = trade_tax_fee;
        let Some(name_seller) = server.names.get_character_name(self.seller_id) else {
            return false;
        };
        self.mail.header.sender_name = ".auctionOffSuccess".to_string();
        self.mail.header.receiver_id = self.seller_id;
        self.mail.receiver_name = name_seller;
        self.mail.mail_type = mail_type::AUC_OFF_SUCCESS;
        self.mail.title = TITLE_SOLD.to_string();
        let Some(count) = self.item_count(server) else {
            tracing::error!("MailForAuction.FinalizeForSaleSeller: NullReferenceException (_item)");
            return false;
        };
        self.mail.body.text = format!(
            "body('{}', {}, {}, {}, {}, {})",
            self.item_name,
            count,
            self.seller_share,
            self.item_buyout_price,
            self.trade_tax_fee,
            self.listing_fee
        );
        self.mail.attach_money(seller_share, 0, 0);
        true
    }

    /// `FinalizeForCancel()`.
    pub fn finalize_for_cancel(&mut self, server: &GameServer) -> bool {
        self.finalize_return(
            server,
            ".auctionOffCancel",
            mail_type::AUC_OFF_CANCEL,
            TITLE_CANCEL,
        )
    }

    /// `FinalizeForFail()`.
    pub fn finalize_for_fail(&mut self, server: &GameServer) -> bool {
        self.finalize_return(
            server,
            ".auctionOffFail",
            mail_type::AUC_OFF_FAIL,
            TITLE_NOT_SOLD,
        )
    }

    fn finalize_return(&mut self, server: &GameServer, sender: &str, mt: u8, title: &str) -> bool {
        let Some(name_seller) = server.names.get_character_name(self.seller_id) else {
            return false;
        };
        self.mail.header.sender_name = sender.to_string();
        self.mail.header.receiver_id = self.seller_id;
        self.mail.receiver_name = name_seller;
        self.mail.mail_type = mt;
        self.mail.title = title.to_string();
        let Some(count) = self.item_count(server) else {
            tracing::error!("MailForAuction.{sender}: NullReferenceException (_item)");
            return false;
        };
        self.mail.body.text = format!("body('{}', {})", self.item_name, count);
        self.attach_item(server, self.seller_id);
        self.mail.is_dirty = true;
        true
    }

    /// `FinalizeForBidFail(previousBuyerId, previousBid)` (C# uses `MailType.AucOffFail`).
    pub fn finalize_for_bid_fail(
        &mut self,
        server: &GameServer,
        previous_buyer_id: u32,
        previous_bid: i32,
    ) -> bool {
        self.buyer_id = previous_buyer_id;
        let Some(name_buyer) = server.names.get_character_name(self.buyer_id) else {
            return false;
        };
        self.mail.header.sender_name = ".auctionBidFail".to_string();
        self.mail.header.receiver_id = self.buyer_id;
        self.mail.receiver_name = name_buyer;
        self.mail.mail_type = mail_type::AUC_OFF_FAIL;
        self.mail.title = TITLE_BID_LOST.to_string();
        self.mail.body.text = format!("body('{}')", self.item_name);
        self.mail.attach_money(previous_bid, 0, 0);
        true
    }

    /// `Send()`.
    pub fn send(self, txn: &mut ItemTxn<'_>) -> bool {
        mail::send(txn, self.mail)
    }
}

// ---------------------------------------------------------------------------------------------
// MailForSpeciality
// ---------------------------------------------------------------------------------------------

const TRADE_DELIVERY_NAME: &str = ".sellBackpack";
const TRADE_DELIVERY_TITLE: &str = "Speciality Payment";
const TRADE_DELIVERY_TITLE_SELLER: &str = "Speciality Payment [Delivery]";
const TRADE_DELIVERY_TITLE_CRAFTER: &str = "Speciality Payment [Crafter]";

/// `MailForSpeciality` (trade pack payout).
#[derive(Debug, Clone)]
pub struct MailForSpeciality {
    pub mail: BaseMail,
    sender_id: u32,
    sender_name: String,
    crafter_id: u32,
    traded_rate: i32,
    item_to_send: u32,
    item_count_base: i32,
    item_count_bonus: i32,
    item_count_seller: i32,
    item_count_crafter: i32,
    interest_rate: i32,
    trade_pack_name: String,
    seller_is_crafter: bool,
}

impl MailForSpeciality {
    /// `new MailForSpeciality(seller, crafterId, tradepackTemplate, tradeRate,
    /// itemRewardTemplateId, itemCountBase, itemCountBonus, itemCountForSeller,
    /// itemCountForCrafter, interestRate)`.
    #[allow(clippy::too_many_arguments)]
    pub fn new(
        server: &GameServer,
        seller: &PlayerRef,
        crafter_id: u32,
        tradepack_template: u32,
        trade_rate: i32,
        item_reward_template_id: u32,
        item_count_base: i32,
        item_count_bonus: i32,
        item_count_for_seller: i32,
        item_count_for_crafter: i32,
        interest_rate: i32,
    ) -> Self {
        let (sender_id, sender_name) = {
            let c = seller.data();
            (c.id, c.unit.name.clone())
        };
        let seller_is_crafter = crafter_id == 0 || crafter_id == sender_id;
        let crafter_id = if crafter_id != 0 && crafter_id != sender_id {
            crafter_id
        } else {
            0
        };
        // TODO: make name localized based on activeplayer locale
        let trade_pack_name = item_name(server, tradepack_template, "");
        let mut mail = BaseMail::new();
        mail.mail_type = mail_type::SYS_SELL_BACKPACK;
        let delay_minutes = server.systems.economy.mail.trade_pack_mail_delay_minutes();
        mail.body.recv_date = mail::now() + (delay_minutes * 60.0) as i64;
        Self {
            mail,
            sender_id,
            sender_name,
            crafter_id,
            traded_rate: trade_rate,
            item_to_send: item_reward_template_id,
            item_count_base,
            item_count_bonus,
            item_count_seller: item_count_for_seller,
            item_count_crafter: item_count_for_crafter,
            interest_rate,
            trade_pack_name,
            seller_is_crafter,
        }
    }

    /// `(int)(_itemCountBase * _tradedRate / 100f) + _itemCountBonus` and the interest version.
    fn payouts(&self) -> (i32, i32) {
        let payout = (self.item_count_base.wrapping_mul(self.traded_rate) as f32 / 100f32) as i32
            + self.item_count_bonus;
        let payout_with_interest =
            (payout.wrapping_mul(100 + self.interest_rate) as f32 / 100f32) as i32;
        (payout, payout_with_interest)
    }

    fn attach_new_item(&mut self, txn: &mut ItemTxn<'_>, count: i32) -> bool {
        let Some(template) = txn.server.game_data.items.template(self.item_to_send) else {
            return false;
        };
        let mut grade = template.fixed_grade;
        if grade <= 0 {
            grade = 0;
        }
        let Some(mut new_item) = create_loose_item(txn, self.item_to_send, count, grade as u8)
        else {
            // C#: NullReferenceException on newItem
            tracing::error!(
                "MailForSpeciality: failed to create item {}",
                self.item_to_send
            );
            return false;
        };
        new_item.owner_id = u64::from(self.sender_id);
        new_item.slot_type = slot_type::MAIL;
        new_item.dirty = true;
        self.mail.body.attachments.push(new_item.id);
        txn.server.items.add_loose(new_item);
        true
    }

    /// `FinalizeForSeller()`.
    pub fn finalize_for_seller(&mut self, txn: &mut ItemTxn<'_>) -> bool {
        if txn
            .server
            .game_data
            .items
            .template(self.item_to_send)
            .is_none()
        {
            return false;
        }
        self.mail.header.sender_id = 0;
        self.mail.header.sender_name = TRADE_DELIVERY_NAME.to_string();
        self.mail.header.receiver_id = self.sender_id;
        self.mail.receiver_name = self.sender_name.clone();
        self.mail.title = if self.crafter_id == 0 {
            TRADE_DELIVERY_TITLE
        } else {
            TRADE_DELIVERY_TITLE_SELLER
        }
        .to_string();
        let (payout, payout_with_interest) = self.payouts();
        if self.item_to_send == ITEM_COINS {
            self.mail.attach_money(self.item_count_seller, 0, 0);
            self.mail.body.text = format!(
                "body('{}', {}, {}, {}, {}, {}, {}, {}, {}, {})",
                self.trade_pack_name,
                self.traded_rate,
                payout,
                if self.seller_is_crafter {
                    payout_with_interest
                } else {
                    self.item_count_base
                },
                self.item_count_bonus,
                self.item_count_seller,
                if self.seller_is_crafter { 2 } else { 1 },
                1,
                0,
                0
            );
        } else {
            // Send items
            if !self.attach_new_item(txn, self.item_count_seller) {
                return false;
            }
            // For item delivery, the client will calculate the total for you depending on receiverCase
            self.mail.body.text = format!(
                "body('{}', {}, {}, {}, {}, {}, {}, {}, {}, {})",
                self.trade_pack_name,
                self.traded_rate,
                0,
                0,
                0,
                0,
                if self.seller_is_crafter { 2 } else { 1 },
                0,
                self.item_count_crafter,
                self.item_count_seller
            );
        }
        self.mail.is_dirty = true;
        true
    }

    /// `FinalizeForCrafter()`. The crafter's item gets the *seller* as owner (C# bug, mirrored).
    pub fn finalize_for_crafter(&mut self, txn: &mut ItemTxn<'_>) -> bool {
        // TODO: test this part of the code (currently no crafter id support on items)
        if self.crafter_id == 0 {
            return false;
        }
        let crafter_name = txn.server.names.get_character_name(self.crafter_id);
        if txn
            .server
            .game_data
            .items
            .template(self.item_to_send)
            .is_none()
        {
            return false;
        }
        self.mail.header.sender_id = 0;
        self.mail.header.sender_name = TRADE_DELIVERY_NAME.to_string();
        self.mail.header.receiver_id = self.crafter_id;
        self.mail.receiver_name = crafter_name.unwrap_or_default();
        let (payout, _) = self.payouts();
        self.mail.title = TRADE_DELIVERY_TITLE_CRAFTER.to_string();
        if self.item_to_send == ITEM_COINS {
            self.mail.attach_money(self.item_count_crafter, 0, 0);
            self.mail.body.text = format!(
                "body('{}', {}, {}, {}, {}, {}, {}, {}, {}, {})",
                self.trade_pack_name,
                self.traded_rate,
                payout,
                self.item_count_base,
                self.item_count_bonus,
                self.item_count_crafter,
                0,
                1,
                0,
                0
            );
        } else {
            if !self.attach_new_item(txn, self.item_count_crafter) {
                return false;
            }
            self.mail.body.text = format!(
                "body('{}', {}, {}, {}, {}, {}, {}, {}, {}, {})",
                self.trade_pack_name,
                self.traded_rate,
                0,
                0,
                0,
                0,
                0,
                0,
                self.item_count_crafter,
                self.item_count_seller
            );
        }
        self.mail.is_dirty = true;
        true
    }

    /// `Send()`.
    pub fn send(self, txn: &mut ItemTxn<'_>) -> bool {
        mail::send(txn, self.mail)
    }
}

// ---------------------------------------------------------------------------------------------
// MailForTax
// ---------------------------------------------------------------------------------------------

const TAX_SENDER_NAME: &str = ".houseTax";

/// The `House` members `MailForTax.UpdateTaxInfo` reads (built by the housing subsystem).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct TaxMailHouse {
    /// `house.Id` (DB id).
    pub id: u32,
    pub owner_id: u32,
    pub account_id: u32,
    pub name: String,
    /// `house.Transform.ZoneId` (zone key).
    pub zone_id: u32,
    pub place_date: UnixTime,
    pub protection_end_date: UnixTime,
    /// `house.Template.Taxation.Tax`.
    pub base_tax: i32,
    /// `house.Template.HeavyTax`.
    pub heavy_tax: bool,
}

impl TaxMailHouse {
    /// `House.TaxDueDate` (`ProtectionEndDate - 7 days`).
    pub fn tax_due_date(&self) -> UnixTime {
        self.protection_end_date - 7 * 24 * 3600
    }
}

/// The out values of `HousingManager.CalculateBuildingTaxInfo(house.AccountId, house.Template,
/// false, ...)` (`None` = it returned `false`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct BuildingTaxInfo {
    pub total_tax_amount_due: i32,
    pub heavy_tax_house_count: i32,
    pub normal_tax_house_count: i32,
    pub hostile_tax_rate: i32,
}

/// `MailForTax`.
#[derive(Debug, Clone)]
pub struct MailForTax {
    pub mail: BaseMail,
    house: TaxMailHouse,
}

impl MailForTax {
    /// `new MailForTax(house)`.
    pub fn new(house: TaxMailHouse) -> Self {
        let mut mail = BaseMail::new();
        mail.mail_type = mail_type::BILLING;
        mail.body.recv_date = mail::now();
        Self { mail, house }
    }

    /// `MailForTax.UpdateTaxInfo(mail, house)`; `tax` is the caller's
    /// `CalculateBuildingTaxInfo` result.
    pub fn update_tax_info(
        server: &GameServer,
        mail: &mut BaseMail,
        house: &TaxMailHouse,
        tax: Option<BuildingTaxInfo>,
    ) -> bool {
        // Check if owner is still valid
        let Some(owner_name) = server.names.get_character_name(house.owner_id) else {
            return false;
        };
        mail.header.receiver_id = house.owner_id;
        mail.receiver_name = owner_name;
        mail.is_dirty = true;
        // Grab the zone the house is in
        let Some(zone) = server.game_data.zones.zone_by_key(house.zone_id) else {
            return false;
        };
        let group_id = zone.group_id;
        // Set mail title
        mail.title = format!("title({group_id})"); // Title calls a function to call zone group name
                                                   // Get Tax info
        let Some(tax) = tax else { return false };
        let now = mail::now();
        let mut late_fees = 0;
        let mut payment_dead_line = house.tax_due_date();
        if house.tax_due_date() <= now {
            late_fees = 1;
            payment_dead_line = house.protection_end_date;
        }
        mail.body.text = format!(
            "body('{}', '{}', '{}', '{}', '{}', '{}', '{}', '{}', '{}', '{}', '{}')",
            house.name,
            house.place_date.max(0),
            house.protection_end_date.max(0),
            payment_dead_line.max(0),
            house.base_tax,
            tax.hostile_tax_rate,
            tax.heavy_tax_house_count,
            late_fees,
            tax.total_tax_amount_due,
            if house.heavy_tax { "true" } else { "false" },
            tax.normal_tax_house_count
        );
        mail.body.billing_amount = tax.total_tax_amount_due;
        // Extra tag
        let extra_unknown: u16 = 0;
        mail.header.extra =
            (i64::from(group_id) << 48) + (i64::from(extra_unknown) << 32) + i64::from(house.id);
        mail.header.status = mail_status::UNPAID;
        true
    }

    /// `FinalizeMail()`.
    pub fn finalize_mail(&mut self, server: &GameServer, tax: Option<BuildingTaxInfo>) -> bool {
        self.mail.header.sender_id = 0;
        self.mail.header.sender_name = TAX_SENDER_NAME.to_string();
        Self::update_tax_info(server, &mut self.mail, &self.house, tax)
    }

    /// `Send()`.
    pub fn send(self, txn: &mut ItemTxn<'_>) -> bool {
        mail::send(txn, self.mail)
    }
}

/// `foreach (var mail in GetMyHouseMails(house.Id)) MailForTax.UpdateTaxInfo(mail, house)` on
/// the stored mails (HousingManager's tax check).
pub fn update_house_tax_mails(
    server: &GameServer,
    house: &TaxMailHouse,
    tax: Option<BuildingTaxInfo>,
) -> usize {
    let mails = &server.systems.economy.mail;
    let ids = mails.get_my_house_mails(house.id);
    for id in &ids {
        mails.with_mail_mut(*id, |m| MailForTax::update_tax_info(server, m, house, tax));
    }
    ids.len()
}

// ---------------------------------------------------------------------------------------------
// CommercialMail
// ---------------------------------------------------------------------------------------------

const IN_GAME_CASH_SHOP_SENDER_NAME: &str = ".inGameShop";

/// `CommercialMail` (cash shop purchases / gifts). Items must be created beforehand (loose).
#[derive(Debug, Clone)]
pub struct CommercialMail {
    pub mail: BaseMail,
    items: Vec<Item>,
    receiver_name: String,
    sender_name: String,
    is_gift: bool,
    is_refund: bool,
    purchased_item_title: String,
}

impl CommercialMail {
    /// `new CommercialMail(receiverId, receiverName, senderName, items, isGift, isRefund,
    /// purchasedItemTitle)`.
    pub fn new(
        receiver_id: u32,
        receiver_name: &str,
        sender_name: &str,
        items: Vec<Item>,
        is_gift: bool,
        is_refund: bool,
        purchased_item_title: &str,
    ) -> Self {
        let mut mail = BaseMail::new();
        mail.mail_type = mail_type::CHARGED;
        mail.header.sender_id = 0;
        mail.header.sender_name = IN_GAME_CASH_SHOP_SENDER_NAME.to_string();
        mail.receiver_name = receiver_name.to_string();
        mail.header.receiver_id = receiver_id;
        mail.body.recv_date = mail::now(); // These mails should always be instant
        Self {
            mail,
            items,
            receiver_name: receiver_name.to_string(),
            sender_name: sender_name.to_string(),
            is_gift,
            is_refund,
            purchased_item_title: purchased_item_title.to_string(),
        }
    }

    /// `FinalizeMail()`: attaches the items (into the receiver's mail container when online,
    /// otherwise they stay loose) and builds title and body.
    pub fn finalize_mail(&mut self, txn: &mut ItemTxn<'_>) {
        let server = txn.server;
        let items = std::mem::take(&mut self.items);
        self.mail.body.attachments = items.iter().map(|i| i.id).collect();
        // If the player is exists, move it to their mail container first
        let target = server
            .world
            .character_by_name(&self.receiver_name)
            .and_then(|p| mail::mail_container(&p));
        for item in items {
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
        }
        let title = self.purchased_item_title.replace('\'', "\\'");
        self.mail.title = format!("title('{title}')");
        self.mail.open_date = 946_684_800; // 2000-01-01, always in the past
        let is_present = if self.is_gift && !self.sender_name.is_empty() {
            "true"
        } else {
            "false"
        };
        let gifter_name = if self.is_gift {
            self.sender_name.as_str()
        } else {
            ""
        };
        let gift_string = if self.is_gift { "1" } else { "0" };
        let refund_string = if self.is_refund { "1" } else { "0" };
        let expire_date_string = "2100,12,31,00,00,00";
        self.mail.body.text = format!(
            "body({is_present},'{gifter_name}','{title}')|gift:{gift_string};|refund:{refund_string};|limit:{expire_date_string};"
        );
        self.mail.is_dirty = true;
    }

    /// `Send()`.
    pub fn send(self, txn: &mut ItemTxn<'_>) -> bool {
        mail::send(txn, self.mail)
    }
}

// ---------------------------------------------------------------------------------------------
// Quest reward mails
// ---------------------------------------------------------------------------------------------

/// `ItemCreationDefinition(templateId, count = 1, gradeId = -1)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ItemCreationDefinition {
    pub template_id: u32,
    pub count: i32,
    pub grade_id: i32,
}

impl ItemCreationDefinition {
    pub fn new(template_id: u32, count: i32, grade_id: i32) -> Self {
        Self {
            template_id,
            count,
            grade_id,
        }
    }
}

/// `MailManager.CreateQuestRewardMails(character, quest, itemCreationDefinitions, mailCopper)`:
/// creates the reward items in the character's mail container and returns the (unsent) mails,
/// 10 items each, the coins on the first one. The caller `Send`s them and sends
/// `SCQuestRewardedByMailPacket`.
pub fn create_quest_reward_mails(
    txn: &mut ItemTxn<'_>,
    character: &PlayerRef,
    quest_template_id: u32,
    item_creation_definitions: &[ItemCreationDefinition],
    mut mail_copper: i32,
) -> Vec<MailPlayerToPlayer> {
    let server = txn.server;
    let mut result_list: Vec<MailPlayerToPlayer> = Vec::new();
    let quest_name = server
        .game_data
        .systems
        .economy
        .mail
        .quest_name(quest_template_id, &quest_template_id.to_string());
    let Some(mail_container) = mail::mail_container(character) else {
        tracing::error!("CreateQuestRewardMails: character has no mail container");
        return result_list;
    };
    // Generate a finalized list of all reward items in the mail attachments container
    let mut total_rewards_items_list = Vec::new();
    for item in item_creation_definitions {
        let Some(template) = server.game_data.items.template(item.template_id) else {
            // C#: NullReferenceException on itemTemplate.FixedGrade
            tracing::error!(
                "CreateQuestRewardMails: unknown item template {}",
                item.template_id
            );
            continue;
        };
        let mut item_grade = template.fixed_grade;
        if item_grade <= 0 {
            item_grade = 0;
        }
        if item.grade_id > 0 {
            item_grade = item.grade_id;
        }
        let res = mail_container
            .lock()
            .expect("container poisoned")
            .acquire_default_item_ex(
                task_type::INVALID,
                item.template_id,
                item.count,
                item_grade,
                0,
                -1,
                &mut txn.env,
            );
        if let Some(r) = res {
            total_rewards_items_list.extend(r.new);
        }
    }
    // Distribute the quest rewards
    let char_name = character.data().unit.name.clone();
    for item_id in total_rewards_items_list {
        let need_new = result_list
            .last()
            .is_none_or(|m| m.mail.body.attachments.len() >= 10);
        if need_new {
            let mut m = MailPlayerToPlayer::new(server, character, &char_name);
            m.mail.header.sender_id = 0;
            m.mail.header.sender_name = ".questReward".to_string();
            // NOTE: newer versions use the .title / .body format, which doesn't work on 1.2
            m.mail.mail_type = mail_type::SYS_EXPRESS;
            m.mail.title = quest_name.clone();
            m.mail.body.text = format!("Reward for quest {quest_name}.");
            m.mail.body.copper_coins = mail_copper;
            mail_copper = 0;
            result_list.push(m);
        }
        result_list
            .last_mut()
            .expect("pushed above")
            .mail
            .body
            .attachments
            .push(item_id);
    }
    for m in &mut result_list {
        m.finalize_attachments(txn);
    }
    result_list
}
