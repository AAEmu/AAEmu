//! Phase-3 subsystem: items / craft / loot / mail / auction / trade / cash shop.
//!
//! C# scope: item actions / tasks (`Models/Game/Items/Actions`, `SCItemTaskSuccessPacket`),
//! moving / consuming items between containers (`Inventory`, `ItemContainer`), money / points /
//! labor changes, `CraftManager`, loot (`LootingContainer`, `Models/Game/Items/Loots`),
//! `MailManager`, `AuctionManager`, `TradeManager`, `CashShopManager`, stores and their packets.
//!
//! Layering:
//! * `models::item_task` — `ItemTask` / `ItemTaskType` / the recorded side effects (`ItemFx`);
//! * `models::container` — the container operations (`AddOrMoveExistingItem`, `RemoveItem`,
//!   `ConsumeItem`, `AcquireDefaultItem`, ...), pure, under the container locks;
//! * [`inventory`] — `Inventory` / `Character` operations of a player ([`inventory::ItemTxn`]
//!   serialises item operations and flushes the effects through [`fx::flush`]);
//! * [`hooks`] — what the economy needs from other subsystems (quests, gear bonuses, NPC shop
//!   flags, slaves / mates); installed into `server.systems.economy.hooks` by their owners.

pub mod cash_shop;
#[cfg(test)]
mod cash_shop_tests;
pub mod data;
pub mod error_message;
pub mod fx;
pub mod hooks;
pub mod inventory;
pub mod item_handlers;
pub mod item_packets;
#[cfg(test)]
pub(crate) mod tests;
pub mod trade;
#[cfg(test)]
mod trade_tests;

use std::collections::HashMap;
use std::sync::{Arc, Mutex, MutexGuard};

use crate::net::registry::PacketRegistry;
use crate::server::GameServer;
use crate::systems::SaveStage;

pub use data::Data;

/// Managers of this subsystem (`server.systems.economy`).
#[derive(Debug, Default)]
pub struct System {
    /// Hooks other subsystems install (see [`hooks`]).
    pub hooks: hooks::EconomyHooks,
    /// Serialises item operations (see [`inventory::ItemTxn`]).
    ops: Mutex<()>,
    /// `Inventory.PreviousBackPackItemId` by character id (glider re-equip after a trade pack).
    previous_backpack: Mutex<HashMap<u32, u64>>,
    /// `TradeManager`.
    pub trade: trade::TradeManager,
    /// `CashShopManager`.
    pub cash_shop: cash_shop::CashShopManager,
}

impl System {
    /// The item-operation lock (outermost lock of every economy operation; never taken while a
    /// character or container lock is held).
    pub fn ops_lock(&self) -> MutexGuard<'_, ()> {
        self.ops.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// `Inventory.PreviousBackPackItemId = id`.
    pub fn set_previous_backpack(&self, character_id: u32, item_id: u64) {
        self.previous_backpack
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .insert(character_id, item_id);
    }

    /// `Inventory.PreviousBackPackItemId`.
    pub fn previous_backpack(&self, character_id: u32) -> u64 {
        self.previous_backpack
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .get(&character_id)
            .copied()
            .unwrap_or(0)
    }

    /// Startup after the phase-1/2 managers: DB loads, timers, hook installation.
    pub async fn start(server: &Arc<GameServer>) -> anyhow::Result<()> {
        cash_shop::load(server).await?;
        Ok(())
    }

    /// This subsystem's part of `SaveManager.DoSave` (inside its transaction). Returns the
    /// number of rows written.
    pub async fn save(
        &self,
        _server: &GameServer,
        _tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
        _stage: SaveStage,
    ) -> crate::Result<usize> {
        Ok(0)
    }
}

/// Registers this subsystem's C->S packets.
pub fn register(reg: &mut PacketRegistry) {
    item_handlers::register(reg);
    trade::register(reg);
    cash_shop::register(reg);
}
