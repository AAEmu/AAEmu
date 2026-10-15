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

pub mod auction;
pub mod auction_data;
pub mod auction_packets;
#[cfg(test)]
mod auction_tests;
pub mod bridges;
pub mod cash_shop;
#[cfg(test)]
mod cash_shop_tests;
pub mod craft;
pub mod craft_data;
#[cfg(test)]
mod craft_store_tests;
pub mod data;
#[cfg(test)]
mod econ_data_tests;
pub mod error_message;
pub(crate) mod fishing;
pub mod fx;
pub mod hooks;
pub mod inventory;
pub mod item_effect_packets;
pub mod item_effects;
#[cfg(test)]
mod item_effects_tests;
pub mod item_handlers;
pub mod item_packets;
pub mod item_timers;
pub mod items_data;
pub mod loot;
pub mod loot_data;
pub mod loot_packets;
pub mod looting;
pub mod mail;
pub mod mail_character;
pub mod mail_data;
pub mod mail_kinds;
pub mod mail_packets;
#[cfg(test)]
mod mail_tests;
pub mod mate_equipment;
#[cfg(test)]
mod ported_unit_tests;
pub mod repair;
pub mod specialty;
pub mod store;
pub mod store_data;
#[cfg(test)]
pub(crate) mod tests;
pub mod trade;
#[cfg(test)]
mod trade_tests;
pub mod wiring;

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
    /// `AuctionManager`.
    pub auction: auction::AuctionManager,
    /// `TradeManager`.
    pub trade: trade::TradeManager,
    /// `CashShopManager`.
    pub cash_shop: cash_shop::CashShopManager,
    /// `CharacterCraft` of every character.
    pub craft: craft::CraftManager,
    /// Store state (`Character.BuyBackItems`) and its hooks.
    pub store: store::StoreManager,
    /// `SpecialtyManager` (trade pack ratios).
    pub specialty: specialty::SpecialtyManager,
    /// `MailManager`.
    pub mail: mail::MailManager,
    /// Looting containers + loot hooks (`LootingContainer`).
    pub loot: looting::LootManager,
    /// `ItemManager.UpdateItemTimers` state.
    pub item_timers: item_timers::ItemTimers,
    /// Item skill effects (their hooks: mates, housing, backpack doodads).
    pub item_effects: item_effects::ItemEffectsManager,
}

thread_local! {
    /// How many [`OpsGuard`]s the current thread holds (0 or 1: the lock is not re-entrant).
    static OPS_HELD: std::cell::Cell<u32> = const { std::cell::Cell::new(0) };
}

/// The held item-operation lock ([`System::ops_lock`]); tracks the holding thread.
pub struct OpsGuard<'a>(Option<MutexGuard<'a, ()>>);

impl Drop for OpsGuard<'_> {
    fn drop(&mut self) {
        self.0.take();
        OPS_HELD.with(|held| held.set(held.get().saturating_sub(1)));
    }
}

impl System {
    /// The item-operation lock (outermost lock of every economy operation; never taken while a
    /// character or container lock is held).
    pub fn ops_lock(&self) -> OpsGuard<'_> {
        let guard = self.ops.lock().unwrap_or_else(|e| e.into_inner());
        OPS_HELD.with(|held| held.set(held.get() + 1));
        OpsGuard(Some(guard))
    }

    /// Whether the current thread holds the item-operation lock. The lock is not re-entrant, so
    /// a producer that would take it again (the achievement reports) defers its event instead.
    pub fn ops_held_on_this_thread() -> bool {
        OPS_HELD.with(|held| held.get() > 0)
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
        wiring::install(server);
        bridges::install(server);
        mate_equipment::install_default_summon_hooks(server);
        if server.systems.economy.auction.load(server).await {
            auction::AuctionManager::start_timer(server);
        }
        tracing::debug!("economy: loading cash shop");
        cash_shop::load(server).await?;
        tracing::debug!("economy: initializing specialty");
        specialty::initialize(server);
        tracing::debug!("economy: loading mails");
        server.systems.economy.mail.load(server).await?;
        mail::start_delivery_task(server);
        tracing::debug!("economy: starting item timers");
        item_timers::start(server);
        Ok(())
    }

    /// This subsystem's part of `SaveManager.DoSave` (inside its transaction). Returns the
    /// number of rows written.
    pub async fn save(
        &self,
        server: &GameServer,
        tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
        stage: SaveStage,
    ) -> crate::Result<usize> {
        let mut n = 0;
        if stage == SaveStage::BeforeItems {
            let (updated, deleted) = self.mail.save(tx).await?;
            if updated + deleted > 0 {
                tracing::debug!("Updated {updated} and deleted {deleted} mails ...");
            }
            n += updated + deleted;
        }
        if stage == SaveStage::AfterItems {
            let (updated, deleted) = self.auction.save(server, tx).await?;
            if updated + deleted > 0 {
                tracing::debug!("Updated {updated} and deleted {deleted} auction items ...");
            }
            n += updated + deleted;
        }
        Ok(n)
    }
}

/// `LeaveWorldTask`: `BuyBackItems.Wipe()` and the economy's per-character state
/// (`CharacterCraft`, `PreviousBackPackItemId`) plus trade lifecycle cleanup.
pub fn on_leave_world(server: &GameServer, player: &crate::world::PlayerRef) {
    trade::on_player_departed(server, player);
    let e = &server.systems.economy;
    let id = player.character_id;
    e.store.wipe_buyback(server, id);
    e.craft.forget(id);
    e.previous_backpack
        .lock()
        .unwrap_or_else(|e| e.into_inner())
        .remove(&id);
}

/// Hard disconnect: cancel trades while both player handles can still be resolved.
pub fn on_disconnect(server: &GameServer, player: &crate::world::PlayerRef) {
    trade::on_player_departed(server, player);
}

/// Registers this subsystem's C->S packets.
pub fn register(reg: &mut PacketRegistry) {
    item_handlers::register(reg);
    auction_packets::register(reg);
    trade::register(reg);
    cash_shop::register(reg);
    craft::register(reg);
    store::register(reg);
    specialty::register(reg);
    mail_packets::register(reg);
    looting::register(reg);
    repair::register(reg);
    mate_equipment::register(reg);
}
