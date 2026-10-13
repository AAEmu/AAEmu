//! What the housing subsystem needs from the **economy** subsystem (items, money, mail). The
//! economy owner installs an implementation in its `System::start`:
//!
//! ```text
//! server.systems.housing.hooks.install(Arc::new(MyHousingEconomy));
//! ```
//!
//! Every method defaults to the "not ported yet" behaviour documented on it, so the housing
//! flows run on their own: no items are known (building a house fails with
//! `BagInvalidItem` as for an invalid design item), no money moves, no mail is sent.

use std::sync::Arc;

use crate::server::GameServer;
use crate::systems::HookSlot;
use crate::world::PlayerRef;

/// `ItemTaskType` values housing uses.
pub mod item_task {
    pub const INVALID: u8 = 0;
    pub const DOODAD_CREATE: u8 = 14;
    pub const HOUSE_CREATION: u8 = 31;
    pub const HOUSE_BUILDING: u8 = 33;
    pub const BUY_HOUSE: u8 = 92;
}

pub use crate::models::container::slot_type;

/// `MailType` values housing sends.
pub mod mail_type {
    pub const BILLING: u8 = 6;
    pub const HOUSING_SALE: u8 = 21;
    pub const DEMOLISH: u8 = 31;
}

/// `MailStatus.Unpaid`.
pub const MAIL_STATUS_UNPAID: u8 = 2;

/// A construction payment must either consume the selected design and the complete tax, or
/// leave both unchanged.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum HouseConstructionError {
    InvalidDesign,
    InsufficientPayment,
}

pub fn construction_certificates(total: i32) -> i32 {
    ((i64::from(total.max(0)) + 9_999) / 10_000) as i32
}

/// The item fields housing reads (`ItemManager.GetItemByItemId` / `Inventory.GetItemById`).
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct HouseItem {
    pub item_id: u64,
    pub template_id: u32,
    /// `Item.OwnerId` (character id).
    pub owner_id: u64,
    pub count: i32,
    /// `Template.MaxCount`.
    pub max_count: i32,
    /// `ItemFlags.HasFlag(ItemFlag.SoulBound)`.
    pub soul_bound: bool,
    /// `SlotType` ([`slot_type`]).
    pub slot_type: u8,
    /// `UccId` (`UccManager.GetUccFromItem` resolves it).
    pub ucc_id: u64,
    /// `BigFish` (`Weight`, `Length`) when the item is a trophy fish.
    pub big_fish: Option<(f32, f32)>,
}

/// One attachment of a housing mail (`Body.Attachments`).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum MailAttachment {
    /// `ItemManager.Create(templateId, count, 0)` with `Grade = grade`, `OwnerId = owner_id`,
    /// `SlotType = Mail` (the design item, tax certificates, restored furniture).
    NewItem {
        template_id: u32,
        count: i32,
        grade: u8,
        owner_id: u32,
    },
    /// An existing item (furniture kept in the owner's system container, coffer contents):
    /// moved to `owner_id`'s `MailAttachments` when online, else `SlotType = Mail`. `add_count`
    /// is what the C# stacking loop added to its `Count` (`oldItem.Count++`).
    Existing {
        item_id: u64,
        owner_id: u32,
        add_count: i32,
    },
}

/// A `BaseMail` built by housing (`MailForTax`, demolition / sale / cancel mails).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct HouseMail {
    /// `MailType` ([`mail_type`]).
    pub mail_type: u8,
    /// `ReceiverName` (`None` = null).
    pub receiver_name: Option<String>,
    /// `Header.ReceiverId`.
    pub receiver_id: u32,
    pub sender_id: u32,
    pub sender_name: String,
    /// `Header.Extra`.
    pub extra: i64,
    /// `Header.Status` (`MailStatus`).
    pub status: u8,
    pub title: String,
    /// `Body.Text`.
    pub text: String,
    /// `Body.SendDate` / `RecvDate` (unix ms).
    pub send_date_ms: i64,
    pub recv_date_ms: i64,
    /// `Body.CopperCoins` (`AttachMoney` / direct).
    pub copper_coins: i32,
    /// `Body.BillingAmount`.
    pub billing_amount: i32,
    pub attachments: Vec<MailAttachment>,
}

/// What `MailForTax.UpdateTaxInfo(mail, house)` writes into an existing mail before it returns
/// (it returns early, keeping the earlier assignments, when the owner name, the zone or the tax
/// calculation is missing).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct TaxMailUpdate {
    /// `Header.ReceiverId` / `ReceiverName`.
    pub receiver: Option<(u32, String)>,
    pub title: Option<String>,
    /// `Body.Text`, `Body.BillingAmount`, `Header.Extra` (+ `Header.Status = Unpaid`).
    pub body: Option<(String, i32, i64)>,
}

impl TaxMailUpdate {
    /// `UpdateTaxInfo`'s return value.
    pub fn complete(&self) -> bool {
        self.body.is_some()
    }
}

/// Item / money / mail services housing calls (economy subsystem).
pub trait HousingEconomyHooks: Send + Sync {
    /// Atomically rechecks and consumes the bag design and its placement tax. Implementations
    /// must not partially charge a failed placement; the economy bridge holds its operation
    /// lock across the inventory and money checks and mutations.
    fn pay_house_construction(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _design: HouseItem,
        _total: i32,
        _tax_item: bool,
    ) -> Result<(), HouseConstructionError> {
        Err(HouseConstructionError::InvalidDesign)
    }

    /// `character.Inventory.GetItemById(itemId)` (any of the character's containers).
    fn inventory_item(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _item_id: u64,
    ) -> Option<HouseItem> {
        None
    }

    /// `ItemManager.GetItemByItemId(itemId)` (any loaded item).
    fn item(&self, _server: &GameServer, _item_id: u64) -> Option<HouseItem> {
        None
    }

    /// `Inventory.GetItemsCount(SlotType.Inventory, templateId)`.
    fn inventory_item_count(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _template_id: u32,
    ) -> i32 {
        0
    }

    /// `Inventory.Bag.ConsumeItem(task, templateId, count, preferredItem)`: the amount consumed.
    fn bag_consume_item(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _task: u8,
        _template_id: u32,
        _count: i32,
        _preferred_item_id: Option<u64>,
    ) -> i32 {
        0
    }

    /// Consume the entire appraisal fee atomically; insufficient certificates consume none.
    fn pay_house_sale_certificates(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _count: i32,
    ) -> bool {
        false
    }

    /// `Character.SubtractMoney(SlotType.Inventory, amount, task)` (false = not enough money).
    fn subtract_money(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _amount: i32,
        _task: u8,
    ) -> bool {
        false
    }

    /// `Inventory.SystemContainer.AddOrMoveExistingItem(task, item)` of `player`.
    fn move_item_to_system_container(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _task: u8,
        _item_id: u64,
    ) -> bool {
        false
    }

    /// `item._holdingContainer?.ConsumeItem(Invalid, item.TemplateId, item.Count, item)`:
    /// destroys a furniture item that is not returned.
    fn destroy_item(&self, _server: &GameServer, _item_id: u64) {}

    /// `DoodadCoffer` part of `ReturnHouseItemsToOwner`: every item of coffer container
    /// `container_id` (last slot first) is moved to the owner's `SlotType.System` container
    /// (`GetItemContainerForCharacter(ownerId, System)`) and returned, in that order.
    fn take_coffer_items(
        &self,
        _server: &GameServer,
        _owner_id: u32,
        _container_id: u64,
    ) -> Vec<HouseItem> {
        Vec::new()
    }

    /// Read-only snapshot used to stage a housing return. No coffer item moves until the
    /// entire return mail batch has passed validation.
    fn coffer_items(&self, _server: &GameServer, _container_id: u64) -> Vec<HouseItem> {
        Vec::new()
    }

    /// Publish the complete return batch and optionally pay for a house purchase together.
    /// All recipients and attachment items must be validated and all IDs reserved before any
    /// existing item or money is changed. `false` leaves them unchanged.
    fn send_return_mails(
        &self,
        _server: &GameServer,
        _mails: Vec<HouseMail>,
        _payment: Option<(&PlayerRef, i32)>,
    ) -> bool {
        false
    }

    /// `MailAttachments.AcquireDefaultItemEx(Invalid, templateId, count, -1, out addedItems,
    /// ...)` on an online `owner` + `mail.Body.Attachments.AddRange(addedItems)` + `Send()` of
    /// `mail` (the `CancelForSale` certificate refund). False = the items could not be created.
    fn send_mail_with_new_items(
        &self,
        _server: &GameServer,
        _owner: &PlayerRef,
        _mail: HouseMail,
        _template_id: u32,
        _count: i32,
    ) -> bool {
        false
    }

    /// `BaseMail.Send()` (creates the attachment items described by `mail.attachments`).
    /// Returns `Send()`'s result.
    fn send_mail(&self, _server: &GameServer, _mail: HouseMail) -> bool {
        false
    }

    /// `MailManager.DeleteHouseMails(houseId)`.
    fn delete_house_mails(&self, _server: &GameServer, _house_id: u32) {}

    /// `MailManager.GetMyHouseMails(houseId)`: the mail ids.
    fn house_mail_ids(&self, _server: &GameServer, _house_id: u32) -> Vec<i64> {
        Vec::new()
    }

    /// `MailForTax.UpdateTaxInfo(mail, house)` applied to the existing mail `mail_id`.
    fn update_tax_mail(&self, _server: &GameServer, _mail_id: i64, _update: &TaxMailUpdate) {}
}

/// `server.systems.housing.hooks`.
#[derive(Debug, Default)]
pub struct HousingHookSlot(HookSlot<dyn HousingEconomyHooks>);

/// The defaults of every hook.
struct NoHooks;
impl HousingEconomyHooks for NoHooks {}

impl HousingHookSlot {
    pub fn install(&self, hooks: Arc<dyn HousingEconomyHooks>) {
        self.0.install(hooks);
    }

    /// The installed hooks or the defaults.
    pub fn get(&self) -> Arc<dyn HousingEconomyHooks> {
        self.0.get().unwrap_or_else(|| Arc::new(NoHooks))
    }
}
