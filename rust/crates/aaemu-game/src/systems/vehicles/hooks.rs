//! Behaviour the vehicles subsystem needs from other owners (installed by them through
//! `server.systems.vehicles.hooks.*.install(..)`). The mate / slave parts of the skills
//! `NpcSkillHooks` and the social `ForceDismount` are called directly by the npcs / social
//! implementations of those hooks (`systems::npcs::bridges`, `systems::social::bridges`); the
//! ship physics and water are called directly in `systems::ai::physics`
//! (see [`super::ship_physics`]).
//!
//! * [`VehicleItemHooks`] (owner: economy): `Craft.EndCraft`. Mate equipment moves
//!   (`CSChangeMateEquipmentPacket`) are the economy subsystem's (`economy::mate_equipment`).
//!
//! The other item / money operations go through the existing `skills::ext::ItemSkillHooks`
//! (`item_count`, `consume_bag_item`, `acquire_default_item`, `item_use_skill`) or the shared
//! `ItemManager` (summon item details).

use std::sync::Arc;

use crate::server::GameServer;
use crate::systems::HookSlot;
use crate::world::PlayerRef;

/// Item operations owned by the economy subsystem.
pub trait VehicleItemHooks: Send + Sync {
    /// `character.Craft.EndCraft()`.
    fn end_craft(&self, _server: &GameServer, _player: &PlayerRef) {}
}

struct NoItems;
impl VehicleItemHooks for NoItems {}

/// `server.systems.vehicles.hooks`.
#[derive(Debug, Default)]
pub struct VehicleHooks {
    pub items: HookSlot<dyn VehicleItemHooks>,
}

impl VehicleHooks {
    pub fn items(&self) -> Arc<dyn VehicleItemHooks> {
        self.items.get().unwrap_or_else(|| Arc::new(NoItems))
    }
}
