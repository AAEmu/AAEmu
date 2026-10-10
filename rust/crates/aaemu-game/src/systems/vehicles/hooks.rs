//! Behaviour the vehicles subsystem needs from other owners (installed by them through
//! `server.systems.vehicles.hooks.*.install(..)`), plus the wrappers this subsystem installs
//! into the skills (`NpcSkillHooks`) and social (`SocialHooks`) slots.
//!
//! * [`VehiclePhysicsHooks`] (owner: the AI / physics agent, `Physics/` + `WaterBodies`): ship
//!   rigid bodies (`Physics.AddShip` / `RemoveShip`), water surface queries. The defaults are
//!   the C# behaviour of a world without water bodies (`OceanLevel` everywhere) and no ship
//!   simulation (ships only move by `ShipRequestMoveType` inputs the physics would consume).
//! * [`VehicleItemHooks`] (owner: economy): item moves between the bag and a mate's equipment
//!   (`Inventory.SplitOrMoveItemEx`), `Craft.EndCraft`. Defaults: "not ported".
//!
//! The other item / money operations go through the existing `skills::ext::ItemSkillHooks`
//! (`item_count`, `consume_bag_item`, `acquire_default_item`, `item_use_skill`) or the shared
//! `ItemManager` (summon item details).

use std::sync::Arc;

use crate::models::transform::PositionAndRotation;
use crate::server::GameServer;
use crate::systems::skills::effects::EffectArgs;
use crate::systems::skills::ext::{MainWorldPosition, NpcSkillHooks};
use crate::systems::skills::types::{SkillCastTarget, SkillCaster};
use crate::systems::social::hooks::{EvidenceFunc, SocialHooks};
use crate::systems::HookSlot;
use crate::world::{DoodadRef, MateRef, NpcRef, PlayerRef, SlaveRef, WorldInstance, WorldObject};

use super::packets::MateEquipmentMove;

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
    /// `Inventory.SplitOrMoveItemEx(Invalid, source, target, itemId, ...)` between the bag
    /// (`SlotType.Inventory`) and the mate's equipment (`EquipmentMate`, container of
    /// `mate_db_id`), including the containers' `SCMateEquipmentChangedPacket`s. `None` = not
    /// ported (treated as a failed move).
    #[allow(clippy::too_many_arguments)]
    fn move_mate_equipment(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _mate_db_id: u32,
        _item_id: u64,
        _mv: &MateEquipmentMove,
        _equip: bool,
    ) -> Option<bool> {
        tracing::debug!("SplitOrMoveItemEx (mate equipment): economy hook not installed");
        None
    }

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

// ---------------------------------------------------------------------------------------------
// NpcSkillHooks wrapper (the mate / slave methods of the skills ext hooks)
// ---------------------------------------------------------------------------------------------

/// The skills `NpcSkillHooks` with the mate / slave methods implemented here; every other
/// method forwards to the hook that was installed before (the npcs subsystem's), or to the
/// trait default when there was none.
pub struct VehicleNpcSkillHooks {
    pub inner: Option<Arc<dyn NpcSkillHooks>>,
}

struct DefaultNpcHooks;
impl NpcSkillHooks for DefaultNpcHooks {}

impl VehicleNpcSkillHooks {
    fn inner(&self) -> Arc<dyn NpcSkillHooks> {
        self.inner
            .clone()
            .unwrap_or_else(|| Arc::new(DefaultNpcHooks))
    }
}

macro_rules! forward {
    ($( fn $name:ident(&self $(, $arg:ident : $t:ty)* ) $(-> $ret:ty)?; )*) => {
        $(
            fn $name(&self $(, $arg: $t)*) $(-> $ret)? {
                self.inner().$name($($arg),*)
            }
        )*
    };
}

impl NpcSkillHooks for VehicleNpcSkillHooks {
    forward! {
        fn npc_attribute(&self, server: &GameServer, npc: &crate::models::npc::Npc, attr: crate::systems::skills::stats::StatKind) -> Option<f64>;
        fn npc_before_death(&self, server: &GameServer, npc: &NpcRef, killer: Option<&WorldObject>);
        fn npc_after_death(&self, server: &GameServer, npc: &NpcRef, killer: Option<&WorldObject>);
        fn npc_respawn_time(&self, server: &GameServer, npc: &NpcRef) -> i32;
        fn npc_damage_received(&self, server: &GameServer, npc: &NpcRef, attacker: &WorldObject, amount: i32);
        fn npc_no_aggro_target(&self, server: &GameServer, npc: &NpcRef);
        fn npc_aggro_removed(&self, server: &GameServer, npc: &NpcRef, abuser: u32);
        fn npc_engage_combat_quest(&self, server: &GameServer, npc: &crate::models::npc::Npc) -> u32;
        fn doodad_skill_hit(&self, server: &GameServer, doodad: &WorldObject, caster: &WorldObject, skill_id: u32);
        fn spawn_channel_doodad(&self, server: &GameServer, caster: &WorldObject, doodad_template_id: u32) -> Option<u32>;
        fn delete_doodad(&self, server: &GameServer, obj_id: u32);
        fn spawn_doodad(&self, server: &GameServer, caster: &WorldObject, template_id: u32, zone_id: u32, pos: PositionAndRotation, delay_ms: i32);
        fn npc_pet_item_id(&self, server: &GameServer, npc: &NpcRef) -> u32;
        fn despawn_npc(&self, server: &GameServer, npc: &NpcRef);
        fn npc_non_pushable_by_actor(&self, server: &GameServer, npc: &NpcRef) -> bool;
        fn npc_move_state(&self, server: &GameServer, npc: &NpcRef) -> (crate::models::movement::GameStanceType, crate::models::movement::MoveTypeAlertness);
        fn world_gate(&self, server: &GameServer, id: u32) -> Option<crate::data::world_data::Portal>;
        fn closest_return_portal(&self, server: &GameServer, player: &PlayerRef) -> Option<crate::data::world_data::Portal>;
        fn main_world_position(&self, server: &GameServer, player: &PlayerRef) -> Option<MainWorldPosition>;
        fn add_private_portal(&self, server: &GameServer, player: &PlayerRef, x: f32, y: f32, z: f32, z_rot: f32, zone_id: u32, name: &str);
        fn rename_private_portal(&self, server: &GameServer, player: &PlayerRef, id: u32, name: &str);
        fn leave_instance(&self, server: &GameServer, player: &PlayerRef);
    }

    fn apply_effect(
        &self,
        server: &GameServer,
        kind: crate::systems::skills::data::EffectKind,
        template: &crate::systems::skills::data::EffectTemplate,
        args: &EffectArgs<'_>,
    ) -> bool {
        if super::effects::apply_effect(server, kind, template, args) {
            return true;
        }
        self.inner().apply_effect(server, kind, template, args)
    }

    fn apply_special_effect(
        &self,
        server: &GameServer,
        special_type: i32,
        args: &EffectArgs<'_>,
        values: [i32; 4],
    ) -> bool {
        self.inner()
            .apply_special_effect(server, special_type, args, values)
    }

    fn unmount_active_mates(&self, server: &GameServer, player: &PlayerRef) {
        super::mates::unmount_active_mates_on_blink(server, player);
    }

    fn spawn_mount(&self, server: &GameServer, player: &PlayerRef, skill_item: &SkillCaster) {
        if let SkillCaster::Item { item_id, .. } = skill_item {
            super::mates::spawn_mount(server, player, *item_id);
        }
    }

    fn mate_is_max_level(&self, server: &GameServer, mate: &MateRef) -> bool {
        super::mates::is_max_level(server, &mate.data())
    }

    fn mate_add_exp(&self, server: &GameServer, mate: &MateRef, exp: i32) {
        super::mates::add_exp(server, mate, exp);
    }

    fn bind_slave(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        slave_obj_id: u32,
        attach_point: i32,
    ) {
        super::slaves::bind_slave(
            server,
            player,
            slave_obj_id,
            attach_point as u8,
            super::slaves::attach_reason::NEW_MASTER,
        );
    }

    fn spawn_slave(&self, server: &GameServer, player: &PlayerRef, skill_item: &SkillCaster) {
        super::slaves::create_from_skill(server, player, skill_item);
    }

    fn riders_escape(&self, server: &GameServer, player: &PlayerRef, target: &SkillCastTarget) {
        super::slaves::riders_escape(server, player, target);
    }

    fn character_slave(&self, server: &GameServer, player: &PlayerRef) -> Option<WorldObject> {
        super::slaves::character_slave(server, player)
    }
}

// ---------------------------------------------------------------------------------------------
// SocialHooks wrapper (`Character.ForceDismount`)
// ---------------------------------------------------------------------------------------------

/// The social hooks with `force_dismount` implemented here; everything else forwards to the
/// previously installed hooks (or the trait defaults).
pub struct VehicleSocialHooks {
    pub inner: Arc<dyn SocialHooks>,
}

impl SocialHooks for VehicleSocialHooks {
    fn spawn_doodad(
        &self,
        server: &GameServer,
        instance_id: u32,
        template_id: u32,
        pos: (f32, f32, f32),
        rot_z: f32,
    ) -> Option<DoodadRef> {
        self.inner
            .spawn_doodad(server, instance_id, template_id, pos, rot_z)
    }

    fn doodad_phase_changed(&self, server: &GameServer, doodad: &DoodadRef) -> Option<Vec<u8>> {
        self.inner.doodad_phase_changed(server, doodad)
    }

    fn terrain_height(&self, server: &GameServer, instance_id: u32, x: f32, y: f32) -> Option<f32> {
        self.inner.terrain_height(server, instance_id, x, y)
    }

    fn create_player_doodad(
        &self,
        server: &GameServer,
        owner: &PlayerRef,
        template_id: u32,
        pos: (f32, f32, f32),
        rot_z: f32,
        item_template_id: u32,
        custom_data: i32,
    ) -> Option<DoodadRef> {
        self.inner.create_player_doodad(
            server,
            owner,
            template_id,
            pos,
            rot_z,
            item_template_id,
            custom_data,
        )
    }

    fn evidence_func(&self, server: &GameServer, doodad: &DoodadRef) -> Option<EvidenceFunc> {
        self.inner.evidence_func(server, doodad)
    }

    fn doodad_change_phase(
        &self,
        server: &GameServer,
        doodad: &DoodadRef,
        caster: &PlayerRef,
        next_phase: i32,
    ) {
        self.inner
            .doodad_change_phase(server, doodad, caster, next_phase)
    }

    fn update_owned_housing_faction(
        &self,
        server: &GameServer,
        character_id: u32,
        faction_id: u32,
    ) {
        self.inner
            .update_owned_housing_faction(server, character_id, faction_id)
    }

    fn force_dismount(&self, server: &GameServer, player: &PlayerRef) {
        super::force_dismount(server, player, super::slaves::attach_reason::PREFAB_CHANGED);
    }

    fn post_update_current_hp(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        old_hp: i32,
        new_hp: i32,
    ) {
        self.inner
            .post_update_current_hp(server, player, old_hp, new_hp)
    }

    fn judge_obj_id(&self, server: &GameServer, court_room_id: u32) -> u32 {
        self.inner.judge_obj_id(server, court_room_id)
    }

    fn check_buff_tag(&self, server: &GameServer, player: &PlayerRef, tag: u32) -> bool {
        self.inner.check_buff_tag(server, player, tag)
    }

    fn remove_buffs_by_tag(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        kind: crate::data::skills::BuffKind,
        count: i32,
        tag: u32,
    ) {
        self.inner
            .remove_buffs_by_tag(server, player, kind, count, tag)
    }
}
