//! The inventory operations the character managers of this subsystem need from the economy
//! subsystem (`Inventory.GetItemsCount` / `CheckItems`, `ItemContainer.ConsumeItem` with its
//! item task packets, the hair swap of the beauty salon). Install with
//! [`CHARACTER_ITEM_OPS`]; without it these operations report "no items".

use crate::server::GameServer;
use crate::systems::HookSlot;
use crate::world::PlayerRef;

/// `ItemTaskType` values used here.
pub mod item_task {
    pub const EXPAND_BAG: u8 = 75;
    pub const EXPAND_BANK: u8 = 76;
    pub const SAVE_MUSIC_NOTES: u8 = 93;
    pub const EXPAND_EXPERT: u8 = 102;
    pub const EDIT_COSMETIC: u8 = 109;
}

/// Inventory operations on a player (economy subsystem).
pub trait CharacterItemOps: Send + Sync {
    /// `Inventory.GetItemsCount(SlotType.Inventory, templateId)`.
    fn items_count(&self, server: &GameServer, player: &PlayerRef, template_id: u32) -> i32;
    /// `Inventory.CheckItems(SlotType.Inventory, templateId, count)`.
    fn check_items(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        template_id: u32,
        count: i32,
    ) -> bool {
        self.items_count(server, player, template_id) >= count
    }
    /// `Bag.ConsumeItem(taskType, templateId, count, null)`: the consumed count.
    fn consume_item(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        task_type: u8,
        template_id: u32,
        count: i32,
    ) -> i32;
    /// The beauty salon hair swap: removes the old hair item and acquires `hair_model` into
    /// the hair slot; `false` = the C# error paths.
    fn replace_hair(&self, server: &GameServer, player: &PlayerRef, hair_model: u32) -> bool;
}

/// The installed [`CharacterItemOps`].
pub static CHARACTER_ITEM_OPS: std::sync::LazyLock<HookSlot<dyn CharacterItemOps>> =
    std::sync::LazyLock::new(HookSlot::default);
