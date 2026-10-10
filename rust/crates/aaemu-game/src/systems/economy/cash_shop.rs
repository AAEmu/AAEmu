//! Port of `Core/Managers/CashShopManager.cs`, `Models/Game/CashShop/*` (`IcsItem`, `IcsSku`,
//! `IcsMenu`, `PremiumDetail`, `AuditIcsSale`), `Models/Tasks/CashShop/CashShopBuyTask.cs`, the
//! account credit / loyalty members of `AccountManager` the shop uses, and the packets
//! `CSICSMenuListPacket`, `CSICSGoodsListPacket`, `CSICSBuyGoodPacket`,
//! `CSICSMoneyRequestPacket`, `CSPremiumServiceBuyPacket`, `CSPremiumServiceListPacket`,
//! `CSPremiumServiceMsgPacket`, `SCICSMenuListPacket`, `SCICSGoodListPacket`,
//! `SCICSGoodDetailPacket`, `SCICSCheckTimePacket`, `SCICSBuyResultPacket`,
//! `SCICSSyncGoodPacket`, `SCICSCashPointPacket`, `SCICSExchangeRatioPacket`,
//! `SCPremiumServiceListPacket`, `SCPremiumPointChangedPacket`, `SCBmPointPacket`,
//! `SCAccountWarnedPacket` (`SCUpdatePremiumPointPacket` lives in `packets::g2c`).
//!
//! The shop tables come from MySQL (`ics_skus`, `ics_shop_items`, `ics_menu`), loaded in
//! `System::start`. Account values and the sales audit go through [`CashShopDb`] (MySQL by
//! default, the C# SQL; tests install an in-memory store). Bought goods are delivered by
//! `CommercialMail`, which belongs to the mail port: [`CashShopMailHooks`].

use std::collections::HashMap;
use std::sync::{Arc, Mutex, MutexGuard};
use std::time::Duration;

use aaemu_commons::{Marshal, PacketStream};
use chrono::{NaiveDateTime, Utc};
use futures::future::BoxFuture;
use sqlx::mysql::MySqlPool;
use sqlx::Row;

use crate::data::sqlite::for_each_row;
use crate::data::GameDataError;
use crate::db::{self, AccountDetails};
use crate::error::Result;
use crate::models::container::slot_type;
use crate::models::item::Item;
use crate::models::item_task::task_type;
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::{ClientPacket, PacketRegistry};
use crate::opcodes::{cs, sc};
use crate::packets::packet_def;
use crate::server::GameServer;
use crate::systems::{DataLoadCtx, HookSlot};
use crate::world::PlayerRef;

use super::error_message;
use super::inventory::{self, ItemTxn};
use super::item_handlers::{active, sync_client_packet};

// ---------------------------------------------------------------------------------------------
// Static values
// ---------------------------------------------------------------------------------------------

/// `CashShopCurrencyType`.
pub mod currency {
    pub const CREDITS: u8 = 0;
    pub const AA_POINTS: u8 = 1;
    pub const LOYALTY: u8 = 2;
    pub const COINS: u8 = 3;
    pub const MAX: u8 = 4;
}

/// `CashShopLimitType`.
pub mod limit_type {
    pub const NONE: u8 = 0;
    pub const ACCOUNT: u8 = 1;
    pub const CHARACTER: u8 = 2;
}

/// `CashShopRestrictSaleType`.
pub mod restrict_sale_type {
    pub const NONE: u8 = 0;
    pub const LEVEL: u8 = 1;
    pub const QUEST: u8 = 2;
}

/// `CashShopCmdUiType`.
pub mod cmd_ui_type {
    pub const ALLOW_ALL: u8 = 0;
    pub const NO_CART_ALLOWED: u8 = 1;
    pub const NO_GIFT_ALLOWED: u8 = 2;
    pub const ONLY_BUY_ALLOWED: u8 = 3;
}

// ---------------------------------------------------------------------------------------------
// Models
// ---------------------------------------------------------------------------------------------

/// `IcsSku` (table `ics_skus`). Dates: `None` = `DateTime.MinValue`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct IcsSku {
    pub sku: u32,
    pub shop_id: u32,
    pub position: i32,
    pub item_id: u32,
    pub item_count: u32,
    pub select_type: u8,
    pub is_default: bool,
    pub event_type: u8,
    pub event_end_date: Option<NaiveDateTime>,
    pub currency: u8,
    pub price: u32,
    pub discount_price: u32,
    pub bonus_item_id: u32,
    pub bonus_item_count: u32,
}

impl IcsSku {
    /// `DiscountPrice > 0 ? DiscountPrice : Price`.
    pub fn effective_price(&self) -> u32 {
        if self.discount_price > 0 {
            self.discount_price
        } else {
            self.price
        }
    }
}

/// `IcsItem` (table `ics_shop_items`).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct IcsItem {
    pub shop_id: u32,
    pub display_item_id: u32,
    pub name: String,
    pub limited_type: u8,
    pub limited_stock_max: u16,
    pub level_min: u8,
    pub level_max: u8,
    pub buy_restrict_type: u8,
    pub buy_restrict_id: u32,
    pub is_sale: bool,
    pub is_hidden: bool,
    pub sale_start: Option<NaiveDateTime>,
    pub sale_end: Option<NaiveDateTime>,
    pub remaining: i32,
    pub shop_buttons: u8,
    /// `Skus` (insertion order).
    pub skus: Vec<IcsSku>,
}

impl IcsItem {
    /// `FirstSku`.
    pub fn first_sku(&self) -> Option<&IcsSku> {
        self.skus.first()
    }
}

/// `IcsMenu` (table `ics_menu`); `ShopItem` is referenced by its shop id.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct IcsMenu {
    pub id: i64,
    pub main_tab: u8,
    pub sub_tab: u8,
    pub tab_pos: i32,
    pub shop_id: u32,
}

/// `AuditIcsSale` (table `audit_ics_sales`).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct AuditIcsSale {
    pub id: i64,
    pub buyer_account: u32,
    pub buyer_char: u32,
    pub target_account: u32,
    pub target_char: u32,
    pub sale_date: Option<NaiveDateTime>,
    pub shop_item_id: u32,
    pub sku: u32,
    pub sale_cost: i32,
    pub sale_currency: u8,
    pub description: String,
}

/// `PremiumDetail`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct PremiumDetail {
    pub c_id: i32,
    pub c_name: String,
    pub p_id: u16,
    pub is_sell: u8,
    pub is_hidden: u8,
    pub p_time: i32,
    pub p_type: u8,
    pub price: i32,
    pub id: u32,
    pub b_count: i32,
}

impl Marshal for PremiumDetail {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.c_id)
            .write_str(&self.c_name)
            .write_u16(self.p_id)
            .write_u8(self.is_sell)
            .write_u8(self.is_hidden)
            .write_i32(self.p_time)
            .write_u8(self.p_type)
            .write_i32(self.price)
            .write_u32(self.id)
            .write_i32(self.b_count);
    }
}

// ---------------------------------------------------------------------------------------------
// Game data: item names (`LocalizationManager.Get("items", "name", id)`)
// ---------------------------------------------------------------------------------------------

/// The localized texts the cash shop reads (`server.game_data.systems.economy.cash_shop`).
#[derive(Debug, Clone, Default)]
pub struct CashShopData {
    /// `localized_texts` rows `items.name` in `DefaultLanguage`.
    pub item_names: HashMap<u32, String>,
}

impl CashShopData {
    pub fn load(ctx: &DataLoadCtx<'_>) -> std::result::Result<Self, GameDataError> {
        let mut d = Self::default();
        let sql = format!(
            "SELECT idx, \"{}\" AS txt FROM localized_texts \
             WHERE tbl_name = 'items' AND tbl_column_name = 'name'",
            ctx.language.replace('"', "")
        );
        for_each_row(ctx.conn, &sql, [], |r| {
            let text = r.string_or("txt", "")?;
            d.item_names.insert(r.i64("idx")? as u32, text);
            Ok(())
        })?;
        Ok(d)
    }

    /// `LocalizationManager.Get("items", "name", id)` (empty texts and misses give "").
    pub fn item_name(&self, id: u32) -> String {
        self.item_names.get(&id).cloned().unwrap_or_default()
    }
}

// ---------------------------------------------------------------------------------------------
// Hooks / stores
// ---------------------------------------------------------------------------------------------

/// `new CommercialMail(receiverId, receiverName, senderName, items, isGift, isRefund,
/// purchasedItemTitle)` + `FinalizeMail()` + `Send()`.
#[derive(Debug, Clone, PartialEq)]
pub struct CommercialMailRequest {
    pub receiver_id: u32,
    pub receiver_name: String,
    pub sender_name: String,
    /// Freshly created items (`ItemManager.Create`, not in any container).
    pub items: Vec<Item>,
    pub is_gift: bool,
    pub is_refund: bool,
    pub purchased_item_title: String,
}

/// Cash shop deliveries (implemented by the mail port: `CommercialMail`).
pub trait CashShopMailHooks: Send + Sync {
    /// Builds, finalizes (moves the items to the receiver's mail attachments when online) and
    /// sends the mail; returns `mail.Send()`.
    fn send_commercial_mail(&self, _server: &GameServer, _mail: CommercialMailRequest) -> bool {
        false
    }
}

/// Quest facts the shop restrictions read (implemented by `systems::quests`).
pub trait CashShopQuestHooks: Send + Sync {
    /// `character.Quests.HasQuestCompleted(questId)`.
    fn has_quest_completed(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _quest_id: u32,
    ) -> bool {
        true
    }
}

/// Account values and the sales audit (`AccountManager` credits / loyalty,
/// `CashShopManager.GetSalesForShopItem` / `LogSale` / `UpdateRemainingShopItemStock`).
pub trait CashShopDb: Send + Sync {
    /// `AccountManager.GetAccountDetails(accountId)`.
    fn account_details<'a>(
        &'a self,
        server: &'a GameServer,
        account_id: u32,
    ) -> BoxFuture<'a, AccountDetails>;
    /// `AccountManager.AddCredits(accountId, amount)`.
    fn add_credits<'a>(
        &'a self,
        server: &'a GameServer,
        account_id: u32,
        amount: i32,
    ) -> BoxFuture<'a, bool>;
    /// `AccountManager.AddLoyalty(accountId, amount)`.
    fn add_loyalty<'a>(
        &'a self,
        server: &'a GameServer,
        account_id: u32,
        amount: i32,
    ) -> BoxFuture<'a, bool>;
    /// `CashShopManager.GetSalesForShopItem(accountId, characterId, shopItemId)`.
    fn sales_for_shop_item<'a>(
        &'a self,
        server: &'a GameServer,
        account_id: u32,
        character_id: u32,
        shop_item_id: u32,
    ) -> BoxFuture<'a, Vec<AuditIcsSale>>;
    /// `CashShopManager.LogSale(...)`.
    fn log_sale<'a>(&'a self, server: &'a GameServer, sale: AuditIcsSale) -> BoxFuture<'a, bool>;
    /// `CashShopManager.UpdateRemainingShopItemStock(shopItemId, newRemaining)`.
    fn update_remaining_stock<'a>(
        &'a self,
        server: &'a GameServer,
        shop_item_id: u32,
        remaining: i32,
    ) -> BoxFuture<'a, bool>;
}

/// The C# MySQL implementation of [`CashShopDb`].
#[derive(Debug, Default)]
pub struct MySqlCashShopDb;

impl CashShopDb for MySqlCashShopDb {
    fn account_details<'a>(
        &'a self,
        server: &'a GameServer,
        account_id: u32,
    ) -> BoxFuture<'a, AccountDetails> {
        Box::pin(db::get_account_details(
            &server.db,
            &server.config,
            account_id,
        ))
    }

    fn add_credits<'a>(
        &'a self,
        server: &'a GameServer,
        account_id: u32,
        amount: i32,
    ) -> BoxFuture<'a, bool> {
        Box::pin(add_credits_sql(&server.db, account_id, amount))
    }

    fn add_loyalty<'a>(
        &'a self,
        server: &'a GameServer,
        account_id: u32,
        amount: i32,
    ) -> BoxFuture<'a, bool> {
        Box::pin(add_loyalty_sql(&server.db, account_id, amount))
    }

    fn sales_for_shop_item<'a>(
        &'a self,
        server: &'a GameServer,
        account_id: u32,
        character_id: u32,
        shop_item_id: u32,
    ) -> BoxFuture<'a, Vec<AuditIcsSale>> {
        Box::pin(async move {
            match sales_for_shop_item_sql(&server.db, account_id, character_id, shop_item_id).await
            {
                Ok(v) => v,
                Err(e) => {
                    // The C# lets the exception escape (the buy task dies).
                    tracing::error!("{e}");
                    Vec::new()
                }
            }
        })
    }

    fn log_sale<'a>(&'a self, server: &'a GameServer, sale: AuditIcsSale) -> BoxFuture<'a, bool> {
        Box::pin(log_sale_sql(&server.db, sale))
    }

    fn update_remaining_stock<'a>(
        &'a self,
        server: &'a GameServer,
        shop_item_id: u32,
        remaining: i32,
    ) -> BoxFuture<'a, bool> {
        Box::pin(update_remaining_stock_sql(
            &server.db,
            shop_item_id,
            remaining,
        ))
    }
}

/// `AccountManager.AddCredits` (`RemoveCredits` = negative amount).
pub async fn add_credits_sql(pool: &MySqlPool, account_id: u32, amount: i32) -> bool {
    match sqlx::query(
        "INSERT INTO accounts (account_id, credits) VALUES(?, ?) ON DUPLICATE KEY UPDATE credits = credits + ?",
    )
    .bind(account_id)
    .bind(amount)
    .bind(amount)
    .execute(pool)
    .await
    {
        Ok(r) => r.rows_affected() > 0,
        Err(e) => {
            tracing::error!("{e}");
            false
        }
    }
}

/// `AccountManager.AddLoyalty`.
pub async fn add_loyalty_sql(pool: &MySqlPool, account_id: u32, amount: i32) -> bool {
    match sqlx::query(
        "INSERT INTO accounts (account_id, loyalty) VALUES(?, ?) ON DUPLICATE KEY UPDATE loyalty = loyalty + ?",
    )
    .bind(account_id)
    .bind(amount)
    .bind(amount)
    .execute(pool)
    .await
    {
        Ok(r) => r.rows_affected() > 0,
        Err(e) => {
            tracing::error!("{e}");
            false
        }
    }
}

const SALES_COLUMNS: &str = "CAST(id AS SIGNED) AS id, CAST(buyer_account AS SIGNED) AS buyer_account, \
     CAST(buyer_char AS SIGNED) AS buyer_char, CAST(target_account AS SIGNED) AS target_account, \
     CAST(target_char AS SIGNED) AS target_char, sale_date, CAST(shop_item_id AS SIGNED) AS shop_item_id, \
     CAST(sku AS SIGNED) AS sku, CAST(sale_cost AS SIGNED) AS sale_cost, \
     CAST(sale_currency AS SIGNED) AS sale_currency, description";

/// `CashShopManager.GetSalesForShopItem`.
pub async fn sales_for_shop_item_sql(
    pool: &MySqlPool,
    account_id: u32,
    character_id: u32,
    shop_item_id: u32,
) -> Result<Vec<AuditIcsSale>> {
    let mut res = Vec::new();
    if (account_id == 0 && character_id == 0) || shop_item_id == 0 {
        return Ok(res);
    }
    let rows = if character_id > 0 {
        sqlx::query(&format!(
            "SELECT {SALES_COLUMNS} FROM audit_ics_sales WHERE (buyer_char = ?) AND (shop_item_id = ?)"
        ))
        .bind(character_id)
        .bind(shop_item_id)
        .fetch_all(pool)
        .await?
    } else {
        sqlx::query(&format!(
            "SELECT {SALES_COLUMNS} FROM audit_ics_sales WHERE (buyer_account = ?) AND (shop_item_id = ?)"
        ))
        .bind(account_id)
        .bind(shop_item_id)
        .fetch_all(pool)
        .await?
    };
    for row in rows {
        res.push(AuditIcsSale {
            id: db::int(&row, "id")?,
            buyer_account: db::int(&row, "buyer_account")? as u32,
            buyer_char: db::int(&row, "buyer_char")? as u32,
            target_account: db::int(&row, "target_account")? as u32,
            target_char: db::int(&row, "target_char")? as u32,
            sale_date: db::dt(&row, "sale_date"),
            shop_item_id: db::int(&row, "shop_item_id")? as u32,
            sku: db::int(&row, "sku")? as u32,
            sale_cost: db::int(&row, "sale_cost")? as i32,
            sale_currency: db::int(&row, "sale_currency")? as u8,
            description: row.try_get::<String, _>("description")?,
        });
    }
    Ok(res)
}

/// `CashShopManager.LogSale`.
pub async fn log_sale_sql(pool: &MySqlPool, sale: AuditIcsSale) -> bool {
    match sqlx::query(
        "INSERT INTO audit_ics_sales (buyer_account, buyer_char, target_account, target_char, sale_date, shop_item_id, sku, sale_cost, sale_currency, description) \
         VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
    )
    .bind(sale.buyer_account)
    .bind(sale.buyer_char)
    .bind(sale.target_account)
    .bind(sale.target_char)
    .bind(sale.sale_date.unwrap_or_else(db::min_value))
    .bind(sale.shop_item_id)
    .bind(sale.sku)
    .bind(sale.sale_cost as u32) // the C# passes the uint saleCost
    .bind(sale.sale_currency)
    .bind(sale.description)
    .execute(pool)
    .await
    {
        Ok(r) if r.rows_affected() > 0 => true,
        Ok(_) => {
            tracing::error!("Saving sale failed");
            false
        }
        Err(e) => {
            tracing::error!("Saving sale failed Exception: {e}");
            false
        }
    }
}

/// `CashShopManager.UpdateRemainingShopItemStock`.
pub async fn update_remaining_stock_sql(
    pool: &MySqlPool,
    shop_item_id: u32,
    remaining: i32,
) -> bool {
    match sqlx::query("UPDATE ics_shop_items SET `remaining` = ? WHERE `shop_id` = ?")
        .bind(remaining)
        .bind(shop_item_id)
        .execute(pool)
        .await
    {
        Ok(r) if r.rows_affected() > 0 => true,
        Ok(_) => {
            tracing::error!("Updating stock failed! ShopItem: {shop_item_id} -> {remaining}");
            false
        }
        Err(e) => {
            tracing::error!("Stock updating failed Exception: {e}");
            false
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Manager
// ---------------------------------------------------------------------------------------------

/// The loaded shop (`SKUs`, `ShopItems`, `MenuItems`, `Enabled`).
#[derive(Debug, Clone, Default)]
pub struct CashShopState {
    pub enabled: bool,
    /// `SKUs` (load order: `shop_id, position`).
    pub skus: Vec<IcsSku>,
    /// `ShopItems` by shop id.
    pub shop_items: HashMap<u32, IcsItem>,
    /// `MenuItems` (ordered by main tab, sub tab, position).
    pub menu_items: Vec<IcsMenu>,
}

impl CashShopState {
    /// `SKUs.TryGetValue(sku)`.
    pub fn sku(&self, sku: u32) -> Option<&IcsSku> {
        self.skus.iter().find(|s| s.sku == sku)
    }

    /// The attach / name pass of `Load` after `SKUs` and `ShopItems` were read.
    pub fn attach_skus(&mut self, names: &CashShopData) {
        for sku in &self.skus {
            if let Some(shop_item) = self.shop_items.get_mut(&sku.shop_id) {
                if shop_item.skus.is_empty() && shop_item.name.trim().is_empty() {
                    // First Item, grab it's name when needed (`Get` never returns null: the
                    // "???" fallback of the C# is unreachable)
                    shop_item.name = names.item_name(sku.item_id);
                }
                shop_item.skus.push(sku.clone());
            } else {
                tracing::warn!(
                    "Found SKU without a valid Shop Item SKU: {}, ShopItem: {}",
                    sku.sku,
                    sku.shop_id
                );
            }
        }
        for (key, shop_item) in &self.shop_items {
            if shop_item.skus.is_empty() {
                tracing::error!("Shop Item found without any SKUs attached {key}");
            }
        }
    }
}

/// `CashShopManager` (`server.systems.economy.cash_shop`).
#[derive(Debug, Default)]
pub struct CashShopManager {
    state: Mutex<CashShopState>,
    /// Account / audit store; MySQL ([`MySqlCashShopDb`]) when nothing is installed.
    pub db: HookSlot<dyn CashShopDb>,
    /// `CommercialMail` (mail port).
    pub mail: HookSlot<dyn CashShopMailHooks>,
    /// `Quests.HasQuestCompleted` (quests port).
    pub quests: HookSlot<dyn CashShopQuestHooks>,
}

impl CashShopManager {
    pub fn state(&self) -> MutexGuard<'_, CashShopState> {
        self.state.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// Replaces the shop tables (loaders and tests).
    pub fn set_state(&self, state: CashShopState) {
        *self.state() = state;
    }

    pub fn enabled(&self) -> bool {
        self.state().enabled
    }

    fn store(&self) -> Arc<dyn CashShopDb> {
        self.db.get().unwrap_or_else(|| Arc::new(MySqlCashShopDb))
    }
}

fn manager(server: &GameServer) -> &CashShopManager {
    &server.systems.economy.cash_shop
}

/// `CashShopManager.Load()` (MySQL) + `GameService`'s `EnabledShop()` right after.
pub async fn load(server: &GameServer) -> Result<()> {
    let mut st = CashShopState::default();
    let pool = &server.db;

    // Load SKUs
    let rows = sqlx::query(
        "SELECT CAST(sku AS SIGNED) AS sku, CAST(shop_id AS SIGNED) AS shop_id, CAST(position AS SIGNED) AS position, \
         CAST(item_id AS SIGNED) AS item_id, CAST(item_count AS SIGNED) AS item_count, \
         CAST(select_type AS SIGNED) AS select_type, CAST(is_default AS SIGNED) AS is_default, \
         CAST(event_type AS SIGNED) AS event_type, event_end_date, CAST(currency AS SIGNED) AS currency, \
         CAST(price AS SIGNED) AS price, CAST(discount_price AS SIGNED) AS discount_price, \
         CAST(bonus_item_id AS SIGNED) AS bonus_item_id, CAST(bonus_item_count AS SIGNED) AS bonus_item_count \
         FROM ics_skus ORDER BY shop_id, position",
    )
    .fetch_all(pool)
    .await?;
    for row in rows {
        let entry = IcsSku {
            sku: db::int(&row, "sku")? as u32,
            shop_id: db::int(&row, "shop_id")? as u32,
            position: db::int(&row, "position")? as i32,
            item_id: db::int(&row, "item_id")? as u32,
            item_count: db::int(&row, "item_count")? as u32,
            select_type: db::int(&row, "select_type")? as u8,
            is_default: db::int(&row, "is_default")? != 0,
            event_type: db::int(&row, "event_type")? as u8,
            event_end_date: db::dt(&row, "event_end_date"),
            currency: db::int(&row, "currency")? as u8,
            price: db::int(&row, "price")? as u32,
            discount_price: db::int(&row, "discount_price")? as u32,
            bonus_item_id: db::int(&row, "bonus_item_id")? as u32,
            bonus_item_count: db::int(&row, "bonus_item_count")? as u32,
        };
        if st.sku(entry.sku).is_some() {
            tracing::error!("Duplicate SKU {}", entry.sku);
        } else {
            st.skus.push(entry);
        }
    }

    // Load Shop Items
    let rows = sqlx::query(
        "SELECT CAST(shop_id AS SIGNED) AS shop_id, CAST(display_item_id AS SIGNED) AS display_item_id, name, \
         CAST(limited_type AS SIGNED) AS limited_type, CAST(limited_stock_max AS SIGNED) AS limited_stock_max, \
         CAST(level_min AS SIGNED) AS level_min, CAST(level_max AS SIGNED) AS level_max, \
         CAST(buy_restrict_type AS SIGNED) AS buy_restrict_type, CAST(buy_restrict_id AS SIGNED) AS buy_restrict_id, \
         CAST(is_sale AS SIGNED) AS is_sale, CAST(is_hidden AS SIGNED) AS is_hidden, sale_start, sale_end, \
         CAST(remaining AS SIGNED) AS remaining, CAST(shop_buttons AS SIGNED) AS shop_buttons \
         FROM ics_shop_items",
    )
    .fetch_all(pool)
    .await?;
    for row in rows {
        let entry = IcsItem {
            shop_id: db::int(&row, "shop_id")? as u32,
            display_item_id: db::int(&row, "display_item_id")? as u32,
            name: row
                .try_get::<Option<String>, _>("name")
                .ok()
                .flatten()
                .unwrap_or_default(),
            limited_type: db::int(&row, "limited_type")? as u8,
            limited_stock_max: db::int(&row, "limited_stock_max")? as u16,
            level_min: db::int(&row, "level_min")? as u8,
            level_max: db::int(&row, "level_max")? as u8,
            buy_restrict_type: db::int(&row, "buy_restrict_type")? as u8,
            buy_restrict_id: db::int(&row, "buy_restrict_id")? as u32,
            is_sale: db::int(&row, "is_sale")? != 0,
            is_hidden: db::int(&row, "is_hidden")? != 0,
            sale_start: db::dt(&row, "sale_start"),
            sale_end: db::dt(&row, "sale_end"),
            remaining: db::int(&row, "remaining")? as i32,
            shop_buttons: db::int(&row, "shop_buttons")? as u8,
            skus: Vec::new(),
        };
        match st.shop_items.entry(entry.shop_id) {
            std::collections::hash_map::Entry::Occupied(_) => {
                tracing::error!("Duplicate ShopItem {}", entry.shop_id);
            }
            std::collections::hash_map::Entry::Vacant(v) => {
                v.insert(entry);
            }
        }
    }

    // Attach SKUs to Shop Items
    st.attach_skus(&server.game_data.systems.economy.cash_shop);

    let rows = sqlx::query(
        "SELECT CAST(id AS SIGNED) AS id, CAST(main_tab AS SIGNED) AS main_tab, CAST(sub_tab AS SIGNED) AS sub_tab, \
         CAST(tab_pos AS SIGNED) AS tab_pos, CAST(shop_id AS SIGNED) AS shop_id \
         FROM ics_menu ORDER BY main_tab, sub_tab, tab_pos",
    )
    .fetch_all(pool)
    .await?;
    for row in rows {
        let shop_id = db::int(&row, "shop_id")? as u32;
        if !st.shop_items.contains_key(&shop_id) {
            tracing::warn!("Menu Entry without a valid ShopId: {shop_id}");
            continue;
        }
        // Note that this List should technically always be in order by main, sub and position
        st.menu_items.push(IcsMenu {
            id: db::int(&row, "id")?,
            main_tab: db::int(&row, "main_tab")? as u8,
            sub_tab: db::int(&row, "sub_tab")? as u8,
            tab_pos: db::int(&row, "tab_pos")? as i32,
            shop_id,
        });
    }
    let empty = st.menu_items.is_empty() || st.shop_items.is_empty() || st.skus.is_empty();
    manager(server).set_state(st);
    // If something didn't load, force close the shop
    if empty {
        disable_shop(server);
    }
    tracing::info!(
        "Loaded {} cash shop items in {} menu entries",
        manager(server).state().shop_items.len(),
        manager(server).state().menu_items.len()
    );
    // GameService: CashShopManager.Instance.EnabledShop() after the load stage.
    enable_shop(server);
    Ok(())
}

/// `EnabledShop()`.
pub fn enable_shop(server: &GameServer) {
    manager(server).state().enabled = true;
}

/// `DisableShop()`: every character re-requests the shop list.
pub fn disable_shop(server: &GameServer) {
    manager(server).state().enabled = false;
    for p in server.world.all_characters() {
        p.send(SCICSCheckTimePacket);
    }
}

/// `CreditDisperseTick` (its timer is commented out in the C# `Initialize`).
pub async fn credit_disperse_tick(server: &GameServer) {
    let store = manager(server).store();
    for p in server.world.all_characters() {
        let account_id = p.data().account_id;
        store.add_credits(server, account_id, 100).await;
        crate::world::session::send_message(
            &p,
            crate::managers::chat::chat_type::SYSTEM,
            "You have received 100 credits.",
        );
    }
}

/// `SendICSPage(connection, mainTabId, subTabId, page)`.
pub fn send_ics_page(
    server: &GameServer,
    conn: &mut GameConnection,
    main_tab: u8,
    sub_tab: u8,
    page: u16,
) {
    for p in ics_page(server, main_tab, sub_tab, page) {
        conn.send(p.as_ref());
    }
}

/// The packets of `SendICSPage`.
pub fn ics_page(
    server: &GameServer,
    main_tab: u8,
    sub_tab: u8,
    page: u16,
) -> Vec<Box<dyn crate::net::packet::ServerPacket>> {
    let st = manager(server).state();
    let this_tab: Vec<&IcsMenu> = st
        .menu_items
        .iter()
        .filter(|t| t.main_tab == main_tab && t.sub_tab == sub_tab)
        .collect();
    let is_limited_tab = main_tab == 1 && sub_tab == 1;
    let items_per_page: usize = if is_limited_tab { 4 } else { 8 };
    let number_of_pages = (this_tab.len() as f32 / items_per_page as f32).ceil() as u16;
    // `Skip(itemsPerPage * (page - 1))`: page 0 skips a negative count (= nothing).
    let skip = (items_per_page as i64 * (i64::from(page) - 1)).max(0) as usize;
    let this_page: Vec<&IcsItem> = this_tab
        .iter()
        .skip(skip)
        .take(items_per_page)
        .filter_map(|m| st.shop_items.get(&m.shop_id))
        .collect();
    let mut out: Vec<Box<dyn crate::net::packet::ServerPacket>> = Vec::new();
    for (i, shop_item) in this_page.iter().enumerate() {
        let is_last = i == this_page.len() - 1;
        out.push(Box::new(SCICSGoodListPacket {
            page_end: is_last,
            total_page: number_of_pages,
            main_tab,
            sub_tab,
            item: (*shop_item).clone(),
        }));
    }
    for (i, shop_item) in this_page.iter().enumerate() {
        let is_last_item = i + 1 >= this_page.len();
        for (n, sku) in shop_item.skus.iter().enumerate() {
            let is_last_sku = n + 1 >= shop_item.skus.len();
            out.push(Box::new(SCICSGoodDetailPacket {
                page_end: is_last_sku && is_last_item,
                sku: sku.clone(),
            }));
        }
    }
    out
}

// ---------------------------------------------------------------------------------------------
// CashShopBuyTask
// ---------------------------------------------------------------------------------------------

fn send_error(p: &PlayerRef, error: i16) {
    inventory::send_error_message(p, error, 0, true);
}

fn fail(buyer: &PlayerRef, error: i16, buy_mode: u8, target_name: &str) {
    send_error(buyer, error);
    buyer.send(SCICSBuyResultPacket {
        success: false,
        buy_mode,
        receiver_name: target_name.to_string(),
        charge_aa_point: 0,
    });
}

/// `DateTime.UtcNow`.
fn utc_now() -> NaiveDateTime {
    Utc::now().naive_utc()
}

/// `CashShopBuyTask.Execute()`.
pub async fn execute_buy(
    server: &GameServer,
    buy_mode: u8,
    buyer: &PlayerRef,
    target: &PlayerRef,
    cart: &[IcsSku],
) {
    let mgr = manager(server);
    let store = mgr.store();
    let (buyer_name, buyer_id, buyer_account, buyer_level, buyer_money) = {
        let c = buyer.data();
        (
            c.unit.name.clone(),
            c.id,
            c.account_id,
            c.unit.level,
            c.money,
        )
    };
    let (target_name, target_id, target_account) = {
        let c = target.data();
        (c.unit.name.clone(), c.id, c.account_id)
    };

    // ---- check_costs
    // Calculate costs (of all different types in the cart)
    let mut costs = [0u32; currency::MAX as usize];
    for sku in cart {
        let Some(c) = costs.get_mut(usize::from(sku.currency)) else {
            // C#: IndexOutOfRangeException ends the task.
            tracing::error!("Invalid Currency {}", sku.currency);
            return;
        };
        *c = c.wrapping_add(sku.effective_price());
    }
    let before = store.account_details(server, buyer_account).await;
    // Check Credits
    if i64::from(costs[currency::CREDITS as usize]) > i64::from(before.credits) {
        fail(
            buyer,
            error_message::INGAME_SHOP_NOT_ENOUGH_AA_CASH,
            buy_mode,
            &target_name,
        );
        return;
    }
    // TODO (C#): Check AA Points
    // Check Loyalty
    if i64::from(costs[currency::LOYALTY as usize]) > i64::from(before.loyalty) {
        fail(
            buyer,
            error_message::INGAME_SHOP_NOT_ENOUGH_BM_MILEAGE,
            buy_mode,
            &target_name,
        );
        return;
    }
    // Check Copper Coins
    if i64::from(costs[currency::COINS as usize]) > buyer_money {
        fail(
            buyer,
            error_message::NOT_ENOUGH_COIN,
            buy_mode,
            &target_name,
        );
        return;
    }

    // ---- validate_cart
    let now = utc_now();
    for sku in cart {
        let shop_item = mgr.state().shop_items.get(&sku.shop_id).cloned();
        let Some(shop_item) = shop_item else {
            fail(
                buyer,
                error_message::INGAME_SHOP_BUY_FAIL,
                buy_mode,
                &target_name,
            ); // generic error
            return;
        };
        // Check Event Date
        if db::is_set(sku.event_end_date) && sku.event_end_date.is_some_and(|d| now >= d) {
            fail(
                buyer,
                error_message::INGAME_SHOP_EXPIRED_SELL_BY_DATE,
                buy_mode,
                &target_name,
            );
            return;
        }
        // Check Sale Start Date
        if db::is_set(shop_item.sale_start) && shop_item.sale_start.is_some_and(|d| now <= d) {
            fail(
                buyer,
                error_message::INGAME_SHOP_EXPIRED_SELL_BY_DATE,
                buy_mode,
                &target_name,
            );
            return;
        }
        // Check Sale End Date
        if db::is_set(shop_item.sale_end) && shop_item.sale_end.is_some_and(|d| now >= d) {
            fail(
                buyer,
                error_message::INGAME_SHOP_EXPIRED_SELL_BY_DATE,
                buy_mode,
                &target_name,
            );
            return;
        }
        // Check Minimum Level
        if shop_item.level_min > 0 && buyer_level < shop_item.level_min {
            fail(
                buyer,
                error_message::INGAME_SHOP_BUY_LOW_LEVEL,
                buy_mode,
                &target_name,
            );
            return;
        }
        // Check Maximum Level (likely not the correct error, as in C#)
        if shop_item.level_max > 0 && buyer_level > shop_item.level_max {
            fail(
                buyer,
                error_message::INGAME_SHOP_BUY_LOW_LEVEL,
                buy_mode,
                &target_name,
            );
            return;
        }
        // Check Minimum Level by Restriction Type
        if shop_item.buy_restrict_type == restrict_sale_type::LEVEL
            && u32::from(buyer_level) < shop_item.buy_restrict_id
        {
            fail(
                buyer,
                error_message::INGAME_SHOP_BUY_LOW_LEVEL,
                buy_mode,
                &target_name,
            );
            return;
        }
        // Check Quest by Restriction Type
        if shop_item.buy_restrict_type == restrict_sale_type::QUEST {
            let done = mgr
                .quests
                .get()
                .is_none_or(|q| q.has_quest_completed(server, buyer, shop_item.buy_restrict_id));
            if !done {
                fail(
                    buyer,
                    error_message::INGAME_SHOP_BUY_QUEST_INCOMPLETE,
                    buy_mode,
                    &target_name,
                );
                return;
            }
        }
        // Check Remaining Stock (limited stock items)
        if shop_item.remaining >= 0 {
            let bought = cart.iter().filter(|b| b.shop_id == sku.shop_id).count() as i64;
            if i64::from(shop_item.remaining) < bought {
                fail(
                    buyer,
                    error_message::INGAME_SHOP_SOLD_OUT,
                    buy_mode,
                    &target_name,
                );
                return;
            }
        }
        // TODO (C#): Check Limited Sales remaining (character or account)
    }

    // ---- transactions
    let mut entries_sold = 0;
    for sku in cart {
        let shop_item = mgr.state().shop_items.get(&sku.shop_id).cloned();
        let Some(shop_item) = shop_item else {
            tracing::error!("Something went wrong in region transactions detecting shopItem");
            continue;
        };

        // Validate Limited Sales
        if shop_item.limited_type != limit_type::NONE {
            let char_filter = if shop_item.limited_type == limit_type::CHARACTER {
                buyer_id
            } else {
                0
            };
            let old_sales = store
                .sales_for_shop_item(server, buyer_account, char_filter, shop_item.shop_id)
                .await;
            let mut old_count = 0u32;
            {
                let st = mgr.state();
                for old in &old_sales {
                    // Ignore if SKU no longer exists
                    let Some(old_sku) = st.sku(old.sku) else {
                        continue;
                    };
                    if shop_item.limited_type == limit_type::CHARACTER {
                        if old.buyer_char == buyer_id {
                            old_count = old_count.wrapping_add(old_sku.item_count);
                        }
                    } else if shop_item.limited_type == limit_type::ACCOUNT
                        && old.buyer_account == buyer_account
                    {
                        old_count = old_count.wrapping_add(old_sku.item_count);
                    }
                }
            }
            // Check if with the new amount we still stay under the limit
            if old_count.wrapping_add(sku.item_count) > u32::from(shop_item.limited_stock_max) {
                tracing::error!("Tried to buy more items than allowed by the limit");
                send_error(buyer, error_message::INGAME_SHOP_SOLD_OUT);
                continue;
            }
        }

        // Reduce remaining stock if needed
        if shop_item.remaining >= 0 {
            let new_remaining = {
                let mut st = mgr.state();
                match st.shop_items.get_mut(&shop_item.shop_id) {
                    Some(si) if i64::from(si.remaining) >= i64::from(sku.item_count) => {
                        si.remaining -= sku.item_count as i32;
                        Some(si.remaining)
                    }
                    Some(si) => {
                        tracing::error!(
                            "Sale validation failed for {buyer_name}, ShopItem: {}, Sku: {}, not enough stock remaining {}",
                            si.shop_id,
                            sku.sku,
                            si.remaining
                        );
                        None
                    }
                    None => None,
                }
            };
            match new_remaining {
                Some(r) => {
                    store
                        .update_remaining_stock(server, shop_item.shop_id, r)
                        .await;
                }
                None => {
                    // Out of Stock!!!
                    send_error(buyer, error_message::INGAME_SHOP_SOLD_OUT);
                    continue;
                }
            }
        }

        // Reduce currency
        match sku.currency {
            currency::CREDITS => {
                if !store
                    .add_credits(server, buyer_account, -(sku.effective_price() as i32))
                    .await
                {
                    tracing::error!(
                        "Sale validation failed for {buyer_name}, {} x {}",
                        sku.currency,
                        sku.price
                    );
                }
            }
            currency::AA_POINTS => {
                tracing::warn!(
                    "Sale currency not implemented {} for {buyer_name}",
                    sku.currency
                );
            }
            currency::LOYALTY => {
                if i64::from(before.loyalty) < i64::from(sku.price) {
                    tracing::error!(
                        "Sale validation failed for {buyer_name}, {} x {}",
                        sku.currency,
                        sku.price
                    );
                }
                store
                    .add_loyalty(server, buyer_account, (sku.price as i32).wrapping_neg())
                    .await;
            }
            currency::COINS => {
                let ok = {
                    let mut txn = ItemTxn::begin(server);
                    inventory::subtract_money(
                        &mut txn.out(),
                        buyer,
                        slot_type::INVENTORY,
                        sku.price as i32,
                        task_type::STORE_BUY,
                    )
                };
                if !ok {
                    tracing::error!(
                        "Sale validation failed for {buyer_name}, {} x {}",
                        sku.currency,
                        sku.price
                    );
                }
            }
            _ => tracing::error!("Invalid Currency {}", sku.currency),
        }

        let names = &server.game_data.systems.economy.cash_shop;
        let use_name = if !shop_item.name.trim().is_empty() {
            shop_item.name.clone()
        } else {
            names.item_name(sku.item_id)
        };
        // `ItemManager.Create(id, count, FixedGrade >= 0 ? FixedGrade : 0)`; a missing template
        // makes the C# throw here (the task ends; earlier entries stay sold).
        let Some(main) = create_item(server, sku.item_id, sku.item_count) else {
            tracing::error!("Unknown item template {} in SKU {}", sku.item_id, sku.sku);
            return;
        };
        let mut items = vec![main];
        if sku.bonus_item_id > 0 && sku.bonus_item_count > 0 {
            let Some(bonus) = create_item(server, sku.bonus_item_id, sku.bonus_item_count) else {
                tracing::error!(
                    "Unknown item template {} in SKU {}",
                    sku.bonus_item_id,
                    sku.sku
                );
                return;
            };
            items.push(bonus);
        }

        let mail = CommercialMailRequest {
            receiver_id: target_id,
            receiver_name: target_name.clone(),
            sender_name: buyer_name.clone(),
            items,
            is_gift: target_id != buyer_id,
            is_refund: false,
            purchased_item_title: use_name.clone(),
        };
        let sent = match mgr.mail.get() {
            Some(m) => m.send_commercial_mail(server, mail),
            None => {
                tracing::warn!("No CommercialMail handler installed, {use_name} is not delivered");
                false
            }
        };
        if !sent {
            // Sending this mail should actually never be able to fail.
            send_error(target, error_message::INGAME_SHOP_BUY_FAIL);
        }

        entries_sold += 1;
        tracing::info!(
            "ICSBuyGood {buyer_name} -> {target_name} - {use_name} x {}, SKU:{}",
            sku.item_count,
            sku.sku
        );
        let logged = store
            .log_sale(
                server,
                AuditIcsSale {
                    id: 0,
                    buyer_account,
                    buyer_char: buyer_id,
                    target_account,
                    target_char: target_id,
                    sale_date: Some(utc_now()),
                    shop_item_id: shop_item.shop_id,
                    sku: sku.sku,
                    sale_cost: sku.effective_price() as i32,
                    sale_currency: sku.currency,
                    description: String::new(),
                },
            )
            .await;
        if !logged {
            tracing::error!(
                "ICSBuyGood {buyer_name} -> {target_name} - {use_name} x {}, SKU:{}, save failed!",
                sku.item_count,
                sku.sku
            );
        }
    }

    let aa_points = costs[currency::AA_POINTS as usize] as i32;
    if entries_sold > 0 {
        let after = store.account_details(server, buyer_account).await;
        buyer.data().bm_point = i64::from(after.loyalty);
        buyer.send(SCICSCashPointPacket {
            point: after.credits,
        });
        buyer.send(SCBmPointPacket {
            bm_point: i64::from(after.loyalty),
        });
        buyer.send(SCICSBuyResultPacket {
            success: true,
            buy_mode,
            receiver_name: target_name,
            charge_aa_point: aa_points,
        });
    } else {
        buyer.send(SCICSBuyResultPacket {
            success: false,
            buy_mode,
            receiver_name: target_name,
            charge_aa_point: aa_points,
        });
    }
}

/// `ItemManager.Create(templateId, count, FixedGrade >= 0 ? FixedGrade : 0, true)`.
fn create_item(server: &GameServer, template_id: u32, count: u32) -> Option<Item> {
    let data = &server.game_data.items;
    let t = data.template(template_id)?;
    let grade = if t.fixed_grade >= 0 {
        t.fixed_grade as u8
    } else {
        0
    };
    server.items.create(
        template_id,
        count as i32,
        grade,
        server.config.id,
        data,
        &server.ids.item,
        Utc::now().timestamp(),
    )
}

// ---------------------------------------------------------------------------------------------
// S->C packets
// ---------------------------------------------------------------------------------------------

/// `SCICSMenuListPacket(enabled)` (tab flags computed from `MenuItems` at construction).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCICSMenuListPacket {
    pub enabled: bool,
    /// `[mainTab - 1][subTab - 1]` used flags.
    pub tabs: [[bool; 7]; 6],
}
packet_def!(
    SCICSMenuListPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCICSMenuListPacket
);

impl SCICSMenuListPacket {
    pub const MAIN_TAB_COUNT: u8 = 6;
    pub const SUB_TAB_COUNT: u8 = 7;

    /// `new SCICSMenuListPacket(enabled)`; `None` where the C# constructor throws
    /// (a menu entry outside the 6x7 tab grid: KeyNotFoundException).
    pub fn new(enabled: bool, menu: &[IcsMenu]) -> Option<Self> {
        let mut tabs = [[false; 7]; 6];
        for m in menu {
            if !(1..=Self::MAIN_TAB_COUNT).contains(&m.main_tab)
                || !(1..=Self::SUB_TAB_COUNT).contains(&m.sub_tab)
            {
                return None;
            }
            tabs[usize::from(m.main_tab - 1)][usize::from(m.sub_tab - 1)] = true;
        }
        Some(Self { enabled, tabs })
    }
}

impl Marshal for SCICSMenuListPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.enabled);
        for main_tab in 1..=Self::MAIN_TAB_COUNT {
            s.write_u8(main_tab);
            for sub_tab in 1..=Self::SUB_TAB_COUNT {
                let used = self.tabs[usize::from(main_tab - 1)][usize::from(sub_tab - 1)];
                s.write_u8(if used { sub_tab } else { 0 });
            }
        }
    }
}

/// `SCICSGoodListPacket(pageEnd, totalPage, mainTab, subTab, item)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCICSGoodListPacket {
    pub page_end: bool,
    pub total_page: u16,
    pub main_tab: u8,
    pub sub_tab: u8,
    pub item: IcsItem,
}
packet_def!(
    SCICSGoodListPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCICSGoodListPacket
);
impl Marshal for SCICSGoodListPacket {
    fn write(&self, s: &mut PacketStream) {
        let item = &self.item;
        s.write_bool(self.page_end)
            .write_u16(self.total_page)
            .write_u32(item.shop_id)
            .write_str(&item.name)
            .write_u8(self.main_tab)
            .write_u8(self.sub_tab)
            .write_u8(item.level_min)
            .write_u8(item.level_max)
            .write_u32(item.display_item_id)
            .write_bool(item.is_sale)
            .write_bool(item.is_hidden)
            .write_u8(item.limited_type)
            .write_u16(item.limited_stock_max)
            .write_u8(item.buy_restrict_type)
            .write_u32(item.buy_restrict_id)
            .write_i64(db::unix(item.sale_start))
            .write_i64(db::unix(item.sale_end));
        match item.first_sku() {
            Some(first) => {
                s.write_u8(first.currency)
                    .write_u32(first.price)
                    .write_i32(item.remaining)
                    .write_u32(first.bonus_item_id)
                    .write_u32(first.bonus_item_count)
                    .write_u8(item.shop_buttons);
            }
            None => {
                // No item for some reason?
                s.write_u8(currency::CREDITS)
                    .write_i32(0)
                    .write_i32(0)
                    .write_i32(0)
                    .write_i32(0)
                    .write_u8(cmd_ui_type::ALLOW_ALL);
            }
        }
    }
}

/// `SCICSGoodDetailPacket(pageEnd, itemDetail)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCICSGoodDetailPacket {
    pub page_end: bool,
    pub sku: IcsSku,
}
packet_def!(
    SCICSGoodDetailPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCICSGoodDetailPacket
);
impl Marshal for SCICSGoodDetailPacket {
    fn write(&self, s: &mut PacketStream) {
        let d = &self.sku;
        s.write_bool(self.page_end)
            .write_u32(d.shop_id)
            .write_u32(d.sku)
            .write_u32(d.item_id)
            .write_u32(d.item_count)
            .write_u8(d.select_type)
            .write_bool(d.is_default)
            .write_u8(d.event_type)
            .write_i64(db::unix(d.event_end_date))
            .write_u8(d.currency)
            .write_u32(d.price)
            .write_u32(d.discount_price)
            .write_u32(d.bonus_item_id)
            .write_u32(d.bonus_item_count);
    }
}

/// `SCICSCheckTimePacket` (empty: the client re-requests the shop list).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCICSCheckTimePacket;
packet_def!(
    SCICSCheckTimePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCICSCheckTimePacket
);
impl Marshal for SCICSCheckTimePacket {
    fn write(&self, _s: &mut PacketStream) {}
}

/// `SCICSBuyResultPacket(success, buyMode, receiverName, chargeAaPoint)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCICSBuyResultPacket {
    pub success: bool,
    pub buy_mode: u8,
    pub receiver_name: String,
    pub charge_aa_point: i32,
}
packet_def!(
    SCICSBuyResultPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCICSBuyResultPacket
);
impl Marshal for SCICSBuyResultPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.success)
            .write_u8(self.buy_mode)
            .write_str(&self.receiver_name)
            .write_i32(self.charge_aa_point);
    }
}

/// `SCICSSyncGoodPacket(cashShopId, remainCount)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCICSSyncGoodPacket {
    pub cash_shop_id: i32,
    pub remain_count: i32,
}
packet_def!(
    SCICSSyncGoodPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCICSSyncGoodPacket
);
impl Marshal for SCICSSyncGoodPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.cash_shop_id).write_i32(self.remain_count);
    }
}

/// `SCICSCashPointPacket(point)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCICSCashPointPacket {
    pub point: i32,
}
packet_def!(
    SCICSCashPointPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCICSCashPointPacket
);
impl Marshal for SCICSCashPointPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.point);
    }
}

/// `SCICSExchangeRatioPacket(exchangeRatio)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCICSExchangeRatioPacket {
    pub exchange_ratio: i32,
}
packet_def!(
    SCICSExchangeRatioPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCICSExchangeRatioPacket
);
impl Marshal for SCICSExchangeRatioPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.exchange_ratio);
    }
}

/// `SCPremiumServiceListPacket(isEnd, size, detail, exchangeRatio)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCPremiumServiceListPacket {
    pub is_end: bool,
    pub size: u8,
    pub detail: PremiumDetail,
    pub exchange_ratio: i32,
}
packet_def!(
    SCPremiumServiceListPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCPremiumServiceListPacket
);
impl Marshal for SCPremiumServiceListPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.is_end).write_u8(self.size);
        self.detail.write(s);
        s.write_i32(self.exchange_ratio);
    }
}

/// `SCPremiumPointChangedPacket(objId, point)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCPremiumPointChangedPacket {
    pub obj_id: u32,
    pub point: i32,
}
packet_def!(
    SCPremiumPointChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCPremiumPointChangedPacket
);
impl Marshal for SCPremiumPointChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id).write_i32(self.point);
    }
}

/// `SCBmPointPacket(bmPoint)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCBmPointPacket {
    pub bm_point: i64,
}
packet_def!(
    SCBmPointPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCBmPointPacket
);
impl Marshal for SCBmPointPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i64(self.bm_point);
    }
}

/// `SCAccountWarnedPacket(source, msg)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCAccountWarnedPacket {
    pub source: u8,
    pub msg: String,
}
packet_def!(
    SCAccountWarnedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCAccountWarnedPacket
);
impl Marshal for SCAccountWarnedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.source).write_str(&self.msg);
    }
}

// ---------------------------------------------------------------------------------------------
// C->S packets
// ---------------------------------------------------------------------------------------------

pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSICSMenuListPacket>(LEVEL_PLAIN, cs::CSICSMenuListPacket);
    reg.register::<CSICSGoodsListPacket>(LEVEL_PLAIN, cs::CSICSGoodsListPacket);
    reg.register::<CSICSBuyGoodPacket>(LEVEL_PLAIN, cs::CSICSBuyGoodPacket);
    reg.register::<CSICSMoneyRequestPacket>(LEVEL_PLAIN, cs::CSICSMoneyRequestPacket);
    reg.register::<CSPremiumServiceBuyPacket>(LEVEL_PLAIN, cs::CSPremiumServiceBuyPacket);
    reg.register::<CSPremiumServiceListPacket>(LEVEL_PLAIN, cs::CSPremiumServiceListPacket);
    reg.register::<CSPremiumServiceMsgPacket>(LEVEL_PLAIN, cs::CSPremiumServiceMsgPacket);
}

/// `CSICSMenuListPacket` (empty body).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSICSMenuListPacket;

impl CSICSMenuListPacket {
    fn read_body(_s: &mut PacketStream) -> Result<Self> {
        Ok(Self)
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        tracing::warn!("ICSMenuList");
        let packet = {
            let st = manager(server).state();
            SCICSMenuListPacket::new(st.enabled, &st.menu_items)
        };
        if let Some(p) = packet {
            conn.send(&p);
        }
    }
}
sync_client_packet!(CSICSMenuListPacket);

/// `CSICSGoodsListPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSICSGoodsListPacket {
    pub main_tab: u8,
    pub sub_tab: u8,
    pub page: u16,
}

impl CSICSGoodsListPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            main_tab: s.read_u8()?,
            sub_tab: s.read_u8()?,
            page: s.read_u16()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        send_ics_page(server, conn, self.main_tab, self.sub_tab, self.page);
    }
}
sync_client_packet!(CSICSGoodsListPacket);

/// One entry of `CSICSBuyGoodPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct IcsBuyEntry {
    pub cash_shop_id: u32,
    pub main_tab: u8,
    pub sub_tab: u8,
    pub detail_index: u8,
}

/// `CSICSBuyGoodPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSICSBuyGoodPacket {
    pub buys: Vec<IcsBuyEntry>,
    pub receiver_name: String,
}

impl CSICSBuyGoodPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        let num = s.read_u8()?;
        let mut buys = Vec::with_capacity(usize::from(num));
        for _ in 0..num {
            buys.push(IcsBuyEntry {
                cash_shop_id: s.read_u32()?,
                main_tab: s.read_u8()?,
                sub_tab: s.read_u8()?,
                detail_index: s.read_u8()?,
            });
        }
        Ok(Self {
            buys,
            receiver_name: s.read_string()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(this_char) = active(conn) else {
            return;
        };
        let buy_mode = 1u8; // No idea what this means
        let name = this_char.data().unit.name.clone();
        let mut buy_list = Vec::new();
        {
            let st = manager(server).state();
            for b in &self.buys {
                let Some(shop_item) = st.shop_items.get(&b.cash_shop_id) else {
                    tracing::warn!(
                        "{name} is trying to shop for invalid ShopItem: {}",
                        b.cash_shop_id
                    );
                    continue;
                };
                let Some(sku) = shop_item.skus.get(usize::from(b.detail_index)) else {
                    tracing::warn!(
                        "{name} is trying to shop from ShopItem: {}, but with invalid index: {}",
                        shop_item.shop_id,
                        b.detail_index
                    );
                    continue;
                };
                buy_list.push(sku.clone());
            }
        }
        let target = if self.receiver_name.is_empty() {
            Some(this_char.clone())
        } else {
            server.world.character_by_name(&self.receiver_name)
        };
        let Some(target) = target else {
            send_error(
                &this_char,
                error_message::INGAME_SHOP_FIND_CHARACTER_NAME_FAIL,
            );
            this_char.send(SCICSBuyResultPacket {
                success: false,
                buy_mode,
                receiver_name: self.receiver_name,
                charge_aa_point: 0,
            });
            return;
        };
        if buy_list.is_empty() {
            send_error(&this_char, error_message::BUY_CART_EMPTY);
            this_char.send(SCICSBuyResultPacket {
                success: false,
                buy_mode,
                receiver_name: self.receiver_name,
                charge_aa_point: 0,
            });
            return;
        }
        // Create task for the transaction (TaskManager.Schedule(..., 1 second))
        let Some(server) = server.self_arc() else {
            tracing::error!("CSICSBuyGoodPacket: server handle not registered");
            return;
        };
        tokio::spawn(async move {
            tokio::time::sleep(Duration::from_secs(1)).await;
            execute_buy(&server, buy_mode, &this_char, &target, &buy_list).await;
        });
    }
}
sync_client_packet!(CSICSBuyGoodPacket);

/// `CSICSMoneyRequestPacket` (empty body).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSICSMoneyRequestPacket;

impl ClientPacket for CSICSMoneyRequestPacket {
    const NAME: &'static str = "CSICSMoneyRequestPacket";

    fn read(_s: &mut PacketStream) -> Result<Self> {
        Ok(Self)
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!("ICSMoneyRequest");
            let points = manager(server)
                .store()
                .account_details(server, conn.account_id())
                .await;
            conn.send(&SCICSCashPointPacket {
                point: points.credits,
            });
            Ok(())
        })
    }
}

/// `CSPremiumServiceBuyPacket` (logged only).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSPremiumServiceBuyPacket {
    pub c_id: i32,
}

impl CSPremiumServiceBuyPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            c_id: s.read_i32()?,
        })
    }

    fn run(self, _server: &GameServer, _conn: &mut GameConnection) {
        tracing::warn!("PremiumServiceBuy, CId: {}", self.c_id);
    }
}
sync_client_packet!(CSPremiumServiceBuyPacket);

/// The fixed entry `CSPremiumServiceListPacket` answers with.
pub fn premium_detail() -> PremiumDetail {
    PremiumDetail {
        c_id: 8000001,
        c_name: "Премиум-подпиcка (30 дней)".into(),
        p_id: 1,
        p_time: 720, // hours
        price: 300,
        ..PremiumDetail::default()
    }
}

/// `CSPremiumServiceListPacket` (empty body).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSPremiumServiceListPacket;

impl CSPremiumServiceListPacket {
    fn read_body(_s: &mut PacketStream) -> Result<Self> {
        Ok(Self)
    }

    fn run(self, _server: &GameServer, conn: &mut GameConnection) {
        tracing::warn!("PremiumServiceList");
        conn.send(&SCPremiumServiceListPacket {
            is_end: true,
            size: 1,
            detail: premium_detail(),
            exchange_ratio: 0,
        });
    }
}
sync_client_packet!(CSPremiumServiceListPacket);

/// `CSPremiumServiceMsgPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSPremiumServiceMsgPacket {
    pub stage: i32,
}

impl CSPremiumServiceMsgPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            stage: s.read_i32()?,
        })
    }

    fn run(self, _server: &GameServer, conn: &mut GameConnection) {
        tracing::info!("PremiumServieceMsg, stage {}", self.stage);
        conn.send(&SCAccountWarnedPacket {
            source: 2,
            msg: "Premium ...".into(),
        });
    }
}
sync_client_packet!(CSPremiumServiceMsgPacket);
