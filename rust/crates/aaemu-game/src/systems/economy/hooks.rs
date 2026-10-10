//! Behaviour the economy needs from other subsystems (and the events it raises for them). Each
//! trait is installed by its owning subsystem in its `System::start`:
//!
//! ```ignore
//! server.systems.economy.hooks.quests.install(Arc::new(MyQuestHooks));
//! ```
//!
//! Every method defaults to the "not ported yet" behaviour (no-op / permissive / `None`), so the
//! economy works stand-alone. All hooks are called without any item container or character
//! lock held.

use crate::models::item::Item;
use crate::server::GameServer;
use crate::systems::HookSlot;
use crate::world::{NpcRef, PlayerRef, WorldObject};

/// Quest events (`systems::quests`): `Inventory.OnAcquiredItem` / `OnConsumedItem` /
/// `OnItemManuallyDestroyed`, `Character.ItemUse` (`Events.OnItemUse`).
pub trait QuestItemHooks: Send + Sync {
    /// `QuestManager.DoItemsAcquiredEvents(owner, templateId, count)`.
    fn on_items_acquired(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _template_id: u32,
        _count: i32,
    ) {
    }

    /// `QuestManager.DoItemsConsumedEvents(owner, templateId, count)`.
    fn on_items_consumed(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _template_id: u32,
        _count: i32,
    ) {
    }

    /// `Inventory.OnItemManuallyDestroyed` -> `Owner.Quests.OnQuestItemManuallyDestroyed(item)`
    /// for items whose template has a `LootQuestId` the player has.
    fn on_quest_item_manually_destroyed(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _item: &Item,
        _loot_quest_id: u32,
    ) {
    }

    /// `Character.ItemUse(item)` -> `Events.OnItemUse(templateId)`.
    fn on_item_use(&self, _server: &GameServer, _player: &PlayerRef, _template_id: u32) {}
}

/// Gear / stat behaviour (`systems::skills`).
pub trait GearHooks: Send + Sync {
    /// `Unit.UpdateGearBonuses(equipped, unequipped)` (item buffs, set bonuses, stat caches).
    fn update_gear_bonuses(
        &self,
        _server: &GameServer,
        _unit: &WorldObject,
        _equipped: Option<&Item>,
        _unequipped: Option<&Item>,
    ) {
    }
}

/// Character progression (whoever ports `Character.AddExp`, probably `systems::quests`).
pub trait ProgressionHooks: Send + Sync {
    /// `Character.AddExp(exp, shouldAddAbilityExp)`.
    fn add_exp(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _exp: i32,
        _should_add_ability_exp: bool,
    ) {
    }
}

/// The NPC template facts the shops / repair / auction / mail checks read
/// (`npc.Template.Merchant`, `MerchantPackId`, `Blacksmith`, `Auctioneer`, ...).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct NpcShopInfo {
    pub merchant: bool,
    pub merchant_pack_id: u32,
    pub blacksmith: bool,
    pub auctioneer: bool,
    pub banker: bool,
    pub specialty: bool,
    pub stabler: bool,
    pub priest: bool,
}

/// NPC data (`systems::npcs`: `NpcTemplate`, `NpcManager.GetGoods`).
pub trait NpcEconomyHooks: Send + Sync {
    /// The shop flags of `npc`'s template (`None` = unknown template).
    fn shop_info(&self, _server: &GameServer, _npc: &NpcRef) -> Option<NpcShopInfo> {
        None
    }

    /// `NpcManager.GetGoods(packId)?.SellsItem(itemId)`.
    fn merchant_sells(&self, _server: &GameServer, _pack_id: u32, _item_id: u32) -> bool {
        false
    }
}

/// Slave / mate items (`SlaveManager` / `MateManager`, owned by whoever ports them).
pub trait SummonItemHooks: Send + Sync {
    /// `SummonSlave.CanDestroy()` (refuses while the slave of the item is summoned; sends
    /// `SlaveSpawnItemLocked`). `true` = may be destroyed.
    fn can_destroy_summon_slave(&self, _server: &GameServer, _item: &Item) -> bool {
        true
    }

    /// `SummonSlave.OnManuallyDestroyingItem` (`SlaveManager.OnDeleteSlaveItem`).
    fn on_slave_item_destroyed(&self, _server: &GameServer, _item: &Item) {}

    /// `MateEquipmentContainer.OnEnter/LeaveContainer` (`SCMateEquipmentChangedPacket` +
    /// `SCUnitEquipmentsChanged` of the mate): `entered` = `Some(slot)` on enter.
    fn on_mate_equipment_changed(
        &self,
        _server: &GameServer,
        _owner: &PlayerRef,
        _mate_id: u32,
        _item: &Item,
        _slot: u8,
        _entered: bool,
    ) {
    }
}

/// The economy's hook slots (`server.systems.economy.hooks`).
#[derive(Debug, Default)]
pub struct EconomyHooks {
    pub quests: HookSlot<dyn QuestItemHooks>,
    pub gear: HookSlot<dyn GearHooks>,
    pub progression: HookSlot<dyn ProgressionHooks>,
    pub npcs: HookSlot<dyn NpcEconomyHooks>,
    pub summons: HookSlot<dyn SummonItemHooks>,
}
