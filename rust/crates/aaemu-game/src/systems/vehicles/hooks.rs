//! Behaviour the vehicles subsystem needs from other owners (installed by them through
//! `server.systems.vehicles.hooks.*.install(..)`). The mate / slave parts of the skills
//! `NpcSkillHooks` and the social `ForceDismount` are called directly by the npcs / social
//! implementations of those hooks (`systems::npcs::bridges`, `systems::social::bridges`).
//!
//! * [`VehiclePhysicsHooks`] (owner: the AI / physics agent, `Physics/` + `WaterBodies`): ship
//!   rigid bodies (`Physics.AddShip` / `RemoveShip`), water surface queries. The defaults are
//!   the C# behaviour of a world without water bodies (`OceanLevel` everywhere) and no ship
//!   simulation (ships only move by `ShipRequestMoveType` inputs the physics would consume).
//! * [`VehicleItemHooks`] (owner: economy): `Craft.EndCraft`. Mate equipment moves
//!   (`CSChangeMateEquipmentPacket`) are the economy subsystem's (`economy::mate_equipment`).
//!
//! The other item / money operations go through the existing `skills::ext::ItemSkillHooks`
//! (`item_count`, `consume_bag_item`, `acquire_default_item`, `item_use_skill`) or the shared
//! `ItemManager` (summon item details).

use std::sync::Arc;

use crate::server::GameServer;
use crate::systems::HookSlot;
use crate::world::{PlayerRef, SlaveRef, WorldInstance};

/// Ship physics and water (`WorldInstance.Physics`, `WorldInstance.Water`).
pub trait VehiclePhysicsHooks: Send + Sync {
    /// `world.Physics.AddShip(slave)`.
    fn add_ship(&self, _server: &GameServer, _slave: &SlaveRef) {}

    /// `world.Physics.RemoveShip(slave)`.
    fn remove_ship(&self, _server: &GameServer, _slave: &SlaveRef) {}

    /// `world.Water.GetWaterSurface(point, out _)`. Default: `OceanLevel` (no water bodies).
    fn water_surface(&self, _server: &GameServer, world: &WorldInstance, _pos: [f32; 3]) -> f32 {
        world.template.ocean_level
    }

    /// `world.IsWater(point)`. Default: below the ocean level.
    fn is_water(&self, _server: &GameServer, world: &WorldInstance, pos: [f32; 3]) -> bool {
        pos[2] <= world.template.ocean_level
    }
}

/// Item operations owned by the economy subsystem.
pub trait VehicleItemHooks: Send + Sync {
    /// `character.Craft.EndCraft()`.
    fn end_craft(&self, _server: &GameServer, _player: &PlayerRef) {}
}

struct NoPhysics;
impl VehiclePhysicsHooks for NoPhysics {}
struct NoItems;
impl VehicleItemHooks for NoItems {}

/// `server.systems.vehicles.hooks`.
#[derive(Debug, Default)]
pub struct VehicleHooks {
    pub physics: HookSlot<dyn VehiclePhysicsHooks>,
    pub items: HookSlot<dyn VehicleItemHooks>,
}

impl VehicleHooks {
    pub fn physics(&self) -> Arc<dyn VehiclePhysicsHooks> {
        self.physics.get().unwrap_or_else(|| Arc::new(NoPhysics))
    }

    pub fn items(&self) -> Arc<dyn VehicleItemHooks> {
        self.items.get().unwrap_or_else(|| Arc::new(NoItems))
    }
}
