//! Game-data tables of the economy (`server.game_data.systems.economy`).

use std::collections::BTreeMap;

use crate::data::sqlite::for_each_row;
use crate::data::GameDataError;
use crate::systems::DataLoadCtx;

/// `Expand` (`bag_expands`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct Expand {
    pub is_bank: bool,
    pub step: i32,
    pub price: i32,
    pub item_id: u32,
    pub item_count: i32,
    pub currency_id: i32,
}

/// Game-data tables of this subsystem.
#[derive(Debug, Default)]
pub struct Data {
    /// `CharacterManager._expands` (by step, rows in table order).
    pub expands: BTreeMap<i32, Vec<Expand>>,
    /// `LocalizationManager` item names (auction search / auction mails).
    pub auction: super::auction_data::AuctionData,
    /// Cash shop texts (`LocalizationManager` item names).
    pub cash_shop: super::cash_shop::CashShopData,
    /// Crafting tables.
    pub craft: super::craft_data::CraftData,
    /// Store / specialty / taxation / expert / item-look tables.
    pub store: super::store_data::StoreData,
    /// Mail localization lookups.
    pub mail: super::mail_data::MailData,
    /// `ItemGameData`, `ItemConversionGameData`, `FishDetailsGameData`, localized item names.
    pub items: super::items_data::ItemsData,
    /// `LootGameData`.
    pub loot: super::loot_data::LootData,
}

impl Data {
    /// Loads this subsystem's tables from `ctx`.
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, GameDataError> {
        let mut data = Self {
            auction: super::auction_data::AuctionData::load(ctx)?,
            cash_shop: super::cash_shop::CashShopData::load(ctx)?,
            ..Self::default()
        };
        for_each_row(ctx.conn, "SELECT * FROM bag_expands", [], |r| {
            let e = Expand {
                is_bank: r.bool_str("is_bank")?,
                step: r.i32("step")?,
                price: r.i32("price")?,
                item_id: r.u32_or("item_id", 0)?,
                item_count: r.i32("item_count")?,
                currency_id: r.i32("currency_id")?,
            };
            data.expands.entry(e.step).or_default().push(e);
            Ok(())
        })?;
        data.craft = super::craft_data::CraftData::load(ctx)?;
        data.store = super::store_data::StoreData::load(ctx)?;
        data.mail = super::mail_data::MailData::load(ctx)?;
        data.items = super::items_data::ItemsData::load(ctx.conn, ctx.language)?;
        data.loot = super::loot_data::LootData::load(ctx.conn)?;
        Ok(data)
    }

    /// `CharacterManager.GetExpands(step)` (`None` where the C# throws `KeyNotFoundException`).
    pub fn expands(&self, step: i32) -> Option<&[Expand]> {
        self.expands.get(&step).map(Vec::as_slice)
    }
}
