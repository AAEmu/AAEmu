//! What the WebApi mail / auction controllers need from the economy subsystem
//! (`MailManager`, `BaseMail.Send`, `AuctionManager`, item containers). The economy owner
//! installs an implementation in `server.systems.services.webapi_economy`; until then the
//! defaults behave as "nothing found / nothing sent" and log once per call.

use chrono::NaiveDateTime;

use super::json::{DateKind, JValue};
use crate::models::item::Item;
use crate::server::GameServer;

/// The `BaseMail` the `MailController.Send` builds for one recipient.
#[derive(Debug, Clone, PartialEq)]
pub struct WebApiMail {
    /// `MailType` (numeric, as sent in the request).
    pub mail_type: u8,
    pub title: String,
    pub receiver_name: String,
    /// `Header.SenderId` (always 0).
    pub sender_id: u32,
    pub sender_name: String,
    pub receiver_id: u32,
    /// `Header.Extra` (always 0).
    pub extra: i64,
    /// `Body.Text`.
    pub text: String,
    /// `Body.SendDate` / `RecvDate` (`DateTime.UtcNow`).
    pub send_date: NaiveDateTime,
    pub recv_date: NaiveDateTime,
    pub copper_coins: i32,
    pub billing_amount: i32,
    /// `Body.Attachments` (owner = receiver, `SlotType.Mail`).
    pub attachments: Vec<Item>,
}

/// The `BaseMail` members `ListMailResponseItem` / the delete check read.
#[derive(Debug, Clone, PartialEq)]
pub struct WebApiMailInfo {
    pub id: i64,
    pub sender_id: u32,
    pub sender_name: String,
    pub receiver_id: u32,
    pub receiver_name: String,
    pub is_delivered: bool,
    pub title: String,
    /// `MailType`.
    pub mail_type: u8,
    pub send_date: (NaiveDateTime, DateKind),
    pub receive_date: (NaiveDateTime, DateKind),
    pub open_date: (NaiveDateTime, DateKind),
    pub attachment_count: i32,
    pub copper_coins: i64,
}

/// An `AuctionLot` for `/api/auction/list` and `/search`: the Newtonsoft form of the whole lot
/// plus the members the search filters on.
#[derive(Debug, Clone, PartialEq)]
pub struct AuctionLotView {
    /// `JsonConvert.SerializeObject(lot)`.
    pub json: JValue,
    /// `Item.TemplateId`.
    pub template_id: u32,
    pub client_name: String,
    /// `Item.Count`.
    pub stack_size: u32,
    pub direct_money: i32,
    pub bid_money: i32,
    /// `BidderName` (`None` = null: `Equals` throws in C#).
    pub bidder_name: Option<String>,
}

/// `AuctionManager.CreateAuctionLot(...)` arguments.
#[derive(Debug, Clone, PartialEq)]
pub struct NewAuctionLot {
    pub client_id: u32,
    pub client_name: String,
    pub item: Item,
    pub start_price: i32,
    pub buyout_price: i32,
    /// `AuctionDuration` (`(AuctionDuration)int`: truncated to the byte).
    pub duration: u8,
    pub min_stack: i32,
    pub max_stack: i32,
}

fn not_ported(what: &str) {
    tracing::warn!("WebApi: {what} is not available (economy subsystem not installed)");
}

/// The economy behaviour the controllers call.
pub trait WebApiEconomyHooks: Send + Sync {
    /// `BaseMail.Send()` (`true` = sent).
    fn send_mail(&self, _server: &GameServer, _mail: WebApiMail) -> bool {
        not_ported("BaseMail.Send");
        false
    }

    /// `MailManager.GetCurrentMailList(characterId)`.
    fn current_mail_list(&self, _server: &GameServer, _character_id: u32) -> Vec<WebApiMailInfo> {
        not_ported("MailManager.GetCurrentMailList");
        Vec::new()
    }

    /// `MailManager.GetMailById(id)`.
    fn mail_by_id(&self, _server: &GameServer, _mail_id: i64) -> Option<WebApiMailInfo> {
        not_ported("MailManager.GetMailById");
        None
    }

    /// `MailManager.DeleteMail(mail, trashItems)`.
    fn delete_mail(&self, _server: &GameServer, _mail_id: i64, _trash_items: bool) -> bool {
        not_ported("MailManager.DeleteMail");
        false
    }

    /// `AuctionManager.AuctionLots.Values` (an `Err` is the exception the C# catches).
    fn auction_lots(&self, _server: &GameServer) -> anyhow::Result<Vec<AuctionLotView>> {
        Err(anyhow::anyhow!("AuctionManager is not available"))
    }

    /// `CreateAuctionLot` + `AddAuctionLot`: the new lot's JSON.
    fn create_auction_lot(
        &self,
        _server: &GameServer,
        _lot: NewAuctionLot,
    ) -> anyhow::Result<JValue> {
        Err(anyhow::anyhow!("AuctionManager is not available"))
    }

    /// `GetItemContainerForCharacter(clientId, SlotType.Auction)` +
    /// `AcquireDefaultItemEx(Invalid, templateId, quantity, grade, out newItems, ...)`:
    /// `None` when the container refused, else the new items.
    fn acquire_auction_items(
        &self,
        _server: &GameServer,
        _character_id: u32,
        _template_id: u32,
        _quantity: i32,
        _grade: u8,
    ) -> anyhow::Result<Option<Vec<Item>>> {
        Err(anyhow::anyhow!("item containers are not available"))
    }
}

/// The default (nothing installed).
#[derive(Debug, Default)]
pub struct NotPorted;

impl WebApiEconomyHooks for NotPorted {}
