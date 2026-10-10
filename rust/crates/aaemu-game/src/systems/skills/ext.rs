//! Hooks into the other phase-3 subsystems (npcs, economy, quests, social). The skill code calls
//! these where the C# reaches into `NpcManager` / `DoodadManager` / `ItemManager` /
//! `QuestManager` / `TeamManager` / ...; every method defaults to "not ported yet" (a no-op or the
//! base-`Unit` value), so the skills subsystem works stand-alone. The owning subsystem installs
//! its implementation in its `System::start`:
//!
//! ```text
//! server.systems.skills.ext.npcs.install(Arc::new(MyNpcSkillHooks));
//! ```
//!
//! Effects owned by another subsystem (spawns, loot, crafting, portals, quests, ...) are routed
//! through `apply_effect` / `apply_special_effect`: the first installed hook that returns `true`
//! handled it; if none does, the skills subsystem logs it (or runs its own fallback).

use crate::data::world_data::Portal;
use crate::models::item::Item;
use crate::models::movement::{GameStanceType, MoveTypeAlertness};
use crate::models::npc::Npc;
use crate::models::transform::PositionAndRotation;
use crate::server::GameServer;
use crate::systems::HookSlot;
use crate::world::{MateRef, NpcRef, PlayerRef, WorldObject};

use super::data::{EffectKind, EffectTemplate};
use super::effects::EffectArgs;
use super::stats::StatKind;
use super::types::{SkillCastTarget, SkillCaster};

/// Logs a hook whose owning subsystem has not installed an implementation yet.
fn not_ported(what: &str) {
    tracing::debug!("{what}: not ported (owning subsystem hook not installed)");
}

/// `Character.MainWorldPosition` (the position to return to when leaving an instance).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct MainWorldPosition {
    pub instance_id: u32,
    pub zone_id: u32,
    /// `MainWorldPosition.World` (rotation in radians).
    pub pos: PositionAndRotation,
}

/// Outcome of an item action that can cancel the skill and show an error (`skill.Cancelled =
/// true` + `SendErrorMessage`).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ItemActionResult {
    /// The action was done.
    Done,
    /// `skill.Cancelled = true` (and return).
    Cancel,
    /// `skill.Cancelled = true` + `character.SendErrorMessage(error)`.
    CancelWithError(i16),
}

/// The arguments of `GradeEnchant.Execute` the item subsystem needs.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct GradeEnchantRequest {
    /// `SkillItem.ItemId` / `ItemTemplateId` of the regrade scroll.
    pub scroll_item_id: u64,
    pub scroll_template_id: u32,
    /// `SkillCastItemTarget.Id` (the item to regrade).
    pub target_item_id: u64,
    /// `SkillObjectItemGradeEnchantingSupport.SupportItemId` (0 = no charm).
    pub support_item_id: u64,
    /// `value1 != 0`.
    pub is_lucky: bool,
    /// `value3` (the `GoldCost` item kind: 1 weapon, 2 armor, 24 accessory).
    pub item_type: i32,
}

/// Equipment contributions of a character (`EquipItem.Str` ... sums, weapon / armor values), as
/// computed by the item subsystem (`Item` stat formulas, grades, gems).
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct GearStats {
    pub str_: f64,
    pub dex: f64,
    pub sta: f64,
    pub int: f64,
    pub spi: f64,
    /// Sum of `Armor.BaseArmor` + `Weapon.Armor` + `Accessory.BaseArmor` (non-destroyed items).
    pub armor: i32,
    /// Sum of `Armor.BaseMagicResistance` + `Accessory.BaseMagicResistance`.
    pub magic_resistance: i32,
    /// `Weapon.Dps` of the mainhand / offhand / ranged slot.
    pub mainhand_dps: f32,
    pub offhand_dps: f32,
    pub ranged_dps: f32,
    /// `Weapon.MDps` / `Weapon.HDps` of the mainhand.
    pub mainhand_mdps: f32,
    pub mainhand_hdps: f32,
}

/// `WeaponTemplate.HoldableTemplate` facts the skill code reads.
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct HoldableInfo {
    pub holdable_id: u32,
    pub slot_type_id: u32,
    pub min_range: f32,
    pub max_range: f32,
    pub damage_scale: f32,
    /// `Speed` (ms, 0 = unknown).
    pub speed: f32,
}

/// NPC / doodad / gimmick / spawner behaviour (subsystem `npcs`).
pub trait NpcSkillHooks: Send + Sync {
    /// `Npc` attribute overrides (`MaxHp`, `Dps`, `Armor`, ... from the npc template / formulas).
    /// `None` = the base `Unit` value.
    fn npc_attribute(&self, _server: &GameServer, _npc: &Npc, _attr: StatKind) -> Option<f64> {
        None
    }

    /// `Npc.DoDie` before `base.DoDie` (tagging, exp, monster hunt quests).
    fn npc_before_death(&self, _server: &GameServer, _npc: &NpcRef, _killer: Option<&WorldObject>) {
    }

    /// `Npc.DoDie` after `base.DoDie` (aggro / combat state clean up, `Spawner.DoDespawn`,
    /// `Ai.GoToDead`).
    fn npc_after_death(&self, _server: &GameServer, _npc: &NpcRef, _killer: Option<&WorldObject>) {}

    /// `Npc.Spawner.RespawnTime` (death packet `resurrectWaitTime`).
    fn npc_respawn_time(&self, _server: &GameServer, _npc: &NpcRef) -> i32 {
        0
    }

    /// `Npc.OnDamageReceived(attacker, amount)` (AI).
    fn npc_damage_received(
        &self,
        _server: &GameServer,
        _npc: &NpcRef,
        _attacker: &WorldObject,
        _amount: i32,
    ) {
    }

    /// `npc.Ai?.OnNoAggroTarget()`.
    fn npc_no_aggro_target(&self, _server: &GameServer, _npc: &NpcRef) {}

    /// `Npc.CheckIfEmptyAggroToReturn(unit)` (after an aggro entry was removed).
    fn npc_aggro_removed(&self, _server: &GameServer, _npc: &NpcRef, _abuser: u32) {}

    /// `npc.Template.EngageCombatGiveQuestId`.
    fn npc_engage_combat_quest(&self, _server: &GameServer, _npc: &Npc) -> u32 {
        0
    }

    /// `Doodad.OnSkillHit(caster, skillId)`.
    fn doodad_skill_hit(
        &self,
        _server: &GameServer,
        _doodad: &WorldObject,
        _caster: &WorldObject,
        _skill_id: u32,
    ) {
    }

    /// `DoodadManager.Create` + spawn of a channeling doodad at the caster; returns its obj id.
    fn spawn_channel_doodad(
        &self,
        _server: &GameServer,
        _caster: &WorldObject,
        _doodad_template_id: u32,
    ) -> Option<u32> {
        None
    }

    /// `channelDoodad.Delete()`.
    fn delete_doodad(&self, _server: &GameServer, _obj_id: u32) {}

    /// An effect template owned by the npc subsystem (`SpawnEffect`, `NpcSpawnerSpawnEffect`,
    /// `NpcSpawnerDespawnEffect`, `NpcControlEffect`, `KillNpcWithoutCorpseEffect`,
    /// `SpawnGimmickEffect`, `InteractionEffect`, `ScopedFEffect`, `PutDownBackpackEffect`,
    /// `SpawnFishEffect`, `PhysicalExplosionEffect`, ...). `true` = handled.
    fn apply_effect(
        &self,
        _server: &GameServer,
        _kind: EffectKind,
        _template: &EffectTemplate,
        _args: &EffectArgs<'_>,
    ) -> bool {
        false
    }

    /// `SpawnDoodad`: `DoodadManager.Create(world, 0, templateId, caster, true)` placed at `pos`
    /// (the C# then replaces `pos.z` with `GeoData.GetHeight`), `InitDoodad`, then `Spawn` after
    /// `delay_ms` (the C# sleeps the effect thread when `delay > 0`).
    fn spawn_doodad(
        &self,
        _server: &GameServer,
        _caster: &WorldObject,
        _template_id: u32,
        _zone_id: u32,
        _pos: PositionAndRotation,
        _delay_ms: i32,
    ) {
        not_ported("SpawnDoodad");
    }

    /// `npc.Template.PetItemId` (0 = none).
    fn npc_pet_item_id(&self, _server: &GameServer, _npc: &NpcRef) -> u32 {
        0
    }

    /// `npc.DoDespawn(npc)`.
    fn despawn_npc(&self, _server: &GameServer, _npc: &NpcRef) {
        not_ported("Npc.DoDespawn");
    }

    /// `npc.Template.NonPushableByActor`.
    fn npc_non_pushable_by_actor(&self, _server: &GameServer, _npc: &NpcRef) -> bool {
        false
    }

    /// `npc.CurrentGameStance` / `npc.CurrentAlertness` (C# defaults: `Combat`, `Idle`).
    fn npc_move_state(
        &self,
        _server: &GameServer,
        _npc: &NpcRef,
    ) -> (GameStanceType, MoveTypeAlertness) {
        (GameStanceType::COMBAT, MoveTypeAlertness::IDLE)
    }

    /// `Blink`: `if (character.IsRiding)` every `MateManager.GetActiveMates(character.Id)` is
    /// `UnMountMate(character, mate.TlId, AttachPointKind.Driver, AttachUnitReason.None)`.
    fn unmount_active_mates(&self, _server: &GameServer, _player: &PlayerRef) {}

    /// `owner.Mates.SpawnMount(skillItem)` (`SpawnPet`).
    fn spawn_mount(&self, _server: &GameServer, _player: &PlayerRef, _skill_item: &SkillCaster) {
        not_ported("Mates.SpawnMount");
    }

    /// `mate.IsMaxLevel`.
    fn mate_is_max_level(&self, _server: &GameServer, _mate: &MateRef) -> bool {
        false
    }

    /// `mate.AddExp(exp)`.
    fn mate_add_exp(&self, _server: &GameServer, _mate: &MateRef, _exp: i32) {
        not_ported("Mate.AddExp");
    }

    /// `SlaveManager.BindSlave(character, slaveObjId, (AttachPointKind)attachPoint,
    /// AttachUnitReason.NewMaster)` (`AttachTo`).
    fn bind_slave(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _slave_obj_id: u32,
        _attach_point: i32,
    ) {
        not_ported("SlaveManager.BindSlave");
    }

    /// `SlaveManager.Create(owner, skillItem)` (`SpawnSlave`).
    fn spawn_slave(&self, _server: &GameServer, _player: &PlayerRef, _skill_item: &SkillCaster) {
        not_ported("SlaveManager.Create");
    }

    /// `SlaveManager.RidersEscape(player, positionTarget)` (`EscapeMySlave`).
    fn riders_escape(&self, _server: &GameServer, _player: &PlayerRef, _target: &SkillCastTarget) {
        not_ported("SlaveManager.RidersEscape");
    }

    /// `SlaveManager.GetIsMounted(character.ObjId) ?? GetActiveSlaveByOwnerObjId(character.ObjId)`
    /// (`ImpulseEffect`).
    fn character_slave(&self, _server: &GameServer, _player: &PlayerRef) -> Option<WorldObject> {
        None
    }

    /// `PortalManager.GetWorldGatesById(id)` (`Data/Portal/worldgates.json`).
    fn world_gate(&self, _server: &GameServer, _id: u32) -> Option<Portal> {
        None
    }

    /// `PortalManager.GetClosestReturnPortal(character)` (respawn points). `None` = no respawn
    /// point qualified (the C# then returns its fail-safe portal at the current position).
    fn closest_return_portal(&self, _server: &GameServer, _player: &PlayerRef) -> Option<Portal> {
        None
    }

    /// `character.MainWorldPosition` (`None` = null).
    fn main_world_position(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
    ) -> Option<MainWorldPosition> {
        None
    }

    /// `character.Portals.AddPrivatePortal(x, y, z, zRot, zoneId, name)` (`SavePortal`).
    #[allow(clippy::too_many_arguments)]
    fn add_private_portal(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _x: f32,
        _y: f32,
        _z: f32,
        _z_rot: f32,
        _zone_id: u32,
        _name: &str,
    ) {
        not_ported("Portals.AddPrivatePortal");
    }

    /// `character.Portals.ChangePrivatePortalName(id, name)` (`SavePortal`).
    fn rename_private_portal(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _id: u32,
        _name: &str,
    ) {
        not_ported("Portals.ChangePrivatePortalName");
    }

    /// `IndunManager.RequestLeaveInstance(character)` (`ExitArchemall`).
    fn leave_instance(&self, _server: &GameServer, _player: &PlayerRef) {
        not_ported("IndunManager.RequestLeaveInstance");
    }

    /// A `SpecialEffect` action owned by the npc subsystem (`SpawnDoodad`, `RemoveDoodad`,
    /// `NpcDespawn`, ...). `true` = handled.
    fn apply_special_effect(
        &self,
        _server: &GameServer,
        _special_type: i32,
        _args: &EffectArgs<'_>,
        _values: [i32; 4],
    ) -> bool {
        false
    }
}

/// Items, inventory, loot, crafting, mail, money (subsystem `economy`).
pub trait ItemSkillHooks: Send + Sync {
    /// Equipment contributions to a character's stats.
    fn gear_stats(&self, _server: &GameServer, _player: &PlayerRef) -> GearStats {
        GearStats::default()
    }

    /// The holdable of the weapon in an equipment slot of a unit (`None` = empty slot / not a
    /// weapon).
    fn weapon_holdable(
        &self,
        _server: &GameServer,
        _unit: &WorldObject,
        _slot: i32,
    ) -> Option<HoldableInfo> {
        None
    }

    /// `HoldableTemplate.GetAttackAnimId(index, leftHand)`.
    fn weapon_attack_anim(
        &self,
        _server: &GameServer,
        _holdable_id: u32,
        _index: i32,
        _left_hand: bool,
    ) -> u32 {
        if _index % 2 == 0 {
            1
        } else {
            2
        }
    }

    /// `Equipment.GetItemBySlot(slot) != null`.
    fn has_equipped(&self, _server: &GameServer, _player: &PlayerRef, _slot: i32) -> bool {
        false
    }

    /// `Inventory.GetItemsCount(templateId)` (bag + equipment as the C# call site uses).
    fn item_count(&self, _server: &GameServer, _player: &PlayerRef, _template_id: u32) -> i32 {
        0
    }

    /// `ItemManager.GetItemByItemId(id)` owned by `player` (template id, count) or `None`.
    fn owned_item(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _item_id: u64,
    ) -> Option<(u32, i32)> {
        None
    }

    /// `ItemTemplate.UseSkillAsReagent`.
    fn use_skill_as_reagent(&self, _server: &GameServer, _template_id: u32) -> bool {
        false
    }

    /// `ItemTemplate.UseSkillId` and `BindType == BindOnPickup` of an item template.
    fn item_use_skill(&self, _server: &GameServer, _template_id: u32) -> Option<(u32, bool)> {
        None
    }

    /// `holdingContainer.ConsumeItem(SkillReagents, templateId, amount, item)`.
    fn consume_item_by_id(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _item_id: u64,
        _amount: i32,
    ) {
    }

    /// `Inventory.ConsumeItem(null, SkillEffectConsumption, templateId, amount, null)`.
    fn consume_item_template(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _template_id: u32,
        _amount: i32,
    ) {
    }

    /// `Inventory.Bag.AcquireDefaultItem(SkillEffectGainItem, itemId, amount)`.
    fn acquire_item(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _template_id: u32,
        _amount: i32,
    ) {
    }

    /// `ItemManager.GetItemSet(id)` items: (template id, count).
    fn item_set(&self, _server: &GameServer, _item_set_id: u32) -> Vec<(u32, i32)> {
        Vec::new()
    }

    /// `ApplyDurabilityLossToEquipment(amount, targets, rate)` (`targets` = `DurabilityLossTargets`).
    fn durability_loss(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _amount: i32,
        _targets: u8,
        _rate: f32,
    ) {
    }

    /// `ItemManager.GetDurabilityDecrementChance()`.
    fn durability_decrement_chance(&self, server: &GameServer) -> f32 {
        server.game_data.items.config.durability_decrement_chance
    }

    /// `SCItemTaskSuccessPacket(ItemUnlock, ...)` after an item-cast plot.
    fn unlock_item(&self, _server: &GameServer, _player: &PlayerRef, _item_id: u64) {}

    /// `UpdateGearBonuses` buff part (`ApplyEquipEffects`, weapon wield / armor grade / item set
    /// buffs) for `player` (the unit-modifier part is done by the skills subsystem).
    fn apply_gear_buffs(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _added: Option<&Item>,
        _removed: Option<&Item>,
    ) {
    }

    /// An effect template owned by the item subsystem (`GainLootPackItemEffect`, `CraftEffect`,
    /// `ConversionEffect`, `ImprintUccEffect`, `CleanupUccEffect`, `RecoverExpEffect`,
    /// `RepairSlaveEffect`, `TrainCraftEffect`, ...). `true` = handled.
    fn apply_effect(
        &self,
        _server: &GameServer,
        _kind: EffectKind,
        _template: &EffectTemplate,
        _args: &EffectArgs<'_>,
    ) -> bool {
        false
    }

    /// `character.Inventory.TryAddNewItem(taskType, templateId, count, grade)` (`GainItem`).
    fn try_add_new_item(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _task_type: u8,
        _template_id: u32,
        _count: i32,
        _grade: u8,
    ) -> bool {
        not_ported("Inventory.TryAddNewItem");
        false
    }

    /// `ItemManager.GetTemplate(templateId) != null`.
    fn item_template_exists(&self, _server: &GameServer, _template_id: u32) -> bool {
        false
    }

    /// `Inventory.Bag.AcquireDefaultItem(taskType, templateId, amount)`.
    fn acquire_default_item(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _task_type: u8,
        _template_id: u32,
        _amount: i32,
    ) -> bool {
        not_ported("Bag.AcquireDefaultItem");
        false
    }

    /// `Inventory.Bag.ConsumeItem(taskType, templateId, amount, null)`: the consumed amount.
    fn consume_bag_item(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _task_type: u8,
        _template_id: u32,
        _amount: i32,
    ) -> i32 {
        not_ported("Bag.ConsumeItem");
        0
    }

    /// `ItemManager.GetItemByItemId(sourceItemId)` and, when it exists,
    /// `UccManager.CreateStamp(player, sourceItem)` (`GainItemWithEmblemImprint`).
    fn ucc_create_stamp(&self, _server: &GameServer, _player: &PlayerRef, _source_item_id: u64) {
        not_ported("UccManager.CreateStamp");
    }

    /// `ItemConversion` from `Bag.GetItemByItemId(itemId)` on (disenchantable check, reagent /
    /// product lookup, roll, `AcquireDefaultItem(Conversion)`, `ConsumeItem(Conversion)`, the
    /// `BagFull` error). `true` = `skill.Cancelled = true`.
    fn item_conversion(&self, _server: &GameServer, _player: &PlayerRef, _item_id: u64) -> bool {
        not_ported("ItemConversion");
        true
    }

    /// `Dyeing` from `Inventory.GetItemById(targetItemId)` on (dye category check, `DyeItemId`,
    /// `SCItemTaskSuccessPacket(Dyeing)`).
    fn dye_item(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _dye_item_id: u64,
        _dye_template_id: u32,
        _target_item_id: u64,
    ) {
        not_ported("Dyeing");
    }

    /// `FishingLoot`: `LootGameData.GetPack(zoneGroup.FishingLandLootPackId` (`land`) or
    /// `FishingSeaLootPackId)` + `GiveLootPack(character, Fishing, SkillEffectGainItem)`.
    /// `None` = no pack / an empty pack, `Some(false)` = the bag was full.
    fn give_fishing_loot(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _zone_group_id: u32,
        _land: bool,
    ) -> Option<bool> {
        not_ported("FishingLoot");
        None
    }

    /// `ItemCapScale`: `GetItemById(targetItemId)` as `EquipItem`, random tempers from
    /// `GetItemCapScale(skillId)`, `SCItemTaskSuccessPacket(EnchantPhysical)`. Returns the
    /// (physical, magical) tempers, `None` when the item was not found.
    fn item_cap_scale(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _target_item_id: u64,
        _skill_id: u32,
    ) -> Option<(u16, u16)> {
        not_ported("ItemCapScale");
        None
    }

    /// `ItemCapScaleReset`: tempers set to 0 + `SCItemTaskSuccessPacket(EnchantPhysical)`.
    /// `false` when the item was not found.
    fn item_cap_scale_reset(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _target_item_id: u64,
    ) -> bool {
        not_ported("ItemCapScaleReset");
        false
    }

    /// `CleanupLookConvert` from the item lookups on (`ImageItemTemplateId` check / reset, the
    /// powder consumption, `SCItemTaskSuccessPacket(ConvertItemLook)`). `powder_item_id` is
    /// `None` when the caster is not a `SkillItem` (checked after the target item checks).
    fn cleanup_look_convert(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _target_item_id: u64,
        _powder_item_id: Option<u64>,
    ) -> ItemActionResult {
        not_ported("CleanupLookConvert");
        ItemActionResult::Cancel
    }

    /// `Skinize` from the item lookups on (`ItemFlag.Skinized` check / set, the powder
    /// consumption, `SCItemTaskSuccessPacket(Skinize)`). `powder_item_id` is `None` when the
    /// caster is not a `SkillItem` (checked by the C# after the target item checks).
    fn skinize(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _target_item_id: u64,
        _powder_item_id: Option<u64>,
    ) -> ItemActionResult {
        not_ported("Skinize");
        ItemActionResult::Cancel
    }

    /// `ItemSocketing` from the item lookups on (lunagem / dawnstone roll,
    /// `SCItemTaskSuccessPacket(Socketing)`, `SCItemSocketingLunagemResultPacket`). `true` = the
    /// C# reached the end (the skills subsystem then broadcasts `SCSkillEndedPacket`).
    fn item_socketing(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _gem_item_id: u64,
        _target_item_id: u64,
    ) -> bool {
        not_ported("ItemSocketing");
        false
    }

    /// `GradeEnchant` from the item lookup on (cost, scroll / charm checks, roll, break / grade
    /// change, money, charm consumption, `SCGradeEnchantResultPacket`, the server-wide
    /// `SCGradeEnchantBroadcastPacket`). `true` = the C# reached `SCSkillEndedPacket` (sent by
    /// the skills subsystem).
    fn grade_enchant(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _request: &GradeEnchantRequest,
    ) -> bool {
        not_ported("GradeEnchant");
        false
    }

    /// `Equipment.GetItemBySlot(slot)?.Template.CategoryId` (`None` = empty slot).
    fn equipped_item_category(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _slot: i32,
    ) -> Option<u32> {
        None
    }

    /// `MusicManager.GetMidiCache(characterId)` (`systems::services::music`).
    fn midi_cache(&self, server: &GameServer, character_id: u32) -> Vec<u8> {
        server.systems.services.music.get_midi_cache(character_id)
    }

    /// `MusicManager.CreateSheetMusic(player, ItemManager.GetItemByItemId(itemId))`.
    fn create_sheet_music(&self, _server: &GameServer, _player: &PlayerRef, _item_id: u64) -> bool {
        not_ported("MusicManager.CreateSheetMusic");
        false
    }

    /// A `SpecialEffect` action owned by the item subsystem (`GainItem`, `ItemSocketing`,
    /// `GradeEnchant`, `ItemCapScale`, `Dyeing`, ...). `true` = handled.
    fn apply_special_effect(
        &self,
        _server: &GameServer,
        _special_type: i32,
        _args: &EffectArgs<'_>,
        _values: [i32; 4],
    ) -> bool {
        false
    }
}

/// Quest events and checks (subsystem `quests`).
pub trait QuestSkillHooks: Send + Sync {
    /// `Character.ItemUse(itemId)` (quest item-use objectives).
    fn item_use(&self, _server: &GameServer, _player: &PlayerRef, _item_id: u64) {}

    /// `QuestManager.DoOnAggroEvents(player, npc)`.
    fn on_aggro(&self, _server: &GameServer, _player: &PlayerRef, _npc: &NpcRef) {}

    /// `player.Quests.AddQuestFromNpc(questId, npcObjId)` when not completed / not active.
    fn engage_combat_quest(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _quest_id: u32,
        _npc_obj_id: u32,
    ) {
    }

    /// `Quests.HasQuestCompleted(id)`.
    fn quest_completed(&self, _server: &GameServer, _player: &PlayerRef, _quest_id: u32) -> bool {
        false
    }

    /// `Quests.ActiveQuests[id].Step` (`QuestComponentKind`, `None` = not active).
    fn quest_step(&self, _server: &GameServer, _player: &PlayerRef, _quest_id: u32) -> Option<u8> {
        None
    }

    /// `SphereGameData.IsInsideAreaSphere(sphereId, inside, position)`.
    fn inside_area_sphere(
        &self,
        _server: &GameServer,
        _sphere_id: u32,
        _value2: u32,
        _pos: (f32, f32, f32),
    ) -> bool {
        false
    }

    /// Effects owned by the quest subsystem (`AcceptQuestEffect`, `CinemaEffect`, ...).
    fn apply_effect(
        &self,
        _server: &GameServer,
        _kind: EffectKind,
        _template: &EffectTemplate,
        _args: &EffectArgs<'_>,
    ) -> bool {
        false
    }

    /// `SpecialEffect` actions owned by the quest subsystem (`NotifyQuest`, ...).
    fn apply_special_effect(
        &self,
        _server: &GameServer,
        _special_type: i32,
        _args: &EffectArgs<'_>,
        _values: [i32; 4],
    ) -> bool {
        false
    }
}

/// Teams, guilds, crime, duels (subsystem `social`).
pub trait SocialSkillHooks: Send + Sync {
    /// `TeamManager.AreTeamMembers(characterId1, characterId2)`.
    fn are_team_members(&self, _server: &GameServer, _char1: u32, _char2: u32) -> bool {
        false
    }

    /// `TeamManager.GetTeamByObjId(objId)?.Id` (0 = no team).
    fn team_of(&self, _server: &GameServer, _obj_id: u32) -> u32 {
        0
    }

    /// `team.IsObjMember(objId)`.
    fn is_team_obj_member(&self, _server: &GameServer, _team_id: u32, _obj_id: u32) -> bool {
        false
    }

    /// `team.IsParty` / member obj ids of a team.
    fn team_members(&self, _server: &GameServer, _team_id: u32) -> (bool, Vec<u32>) {
        (true, Vec::new())
    }

    /// `CrimeManager.GenerateEvidenceFromDamage(caster, target)`.
    fn evidence_from_damage(
        &self,
        _server: &GameServer,
        _caster: &WorldObject,
        _target: &WorldObject,
    ) {
    }

    /// Effects owned by the social subsystem (`ReportCrimeEffect`, ...).
    fn apply_effect(
        &self,
        _server: &GameServer,
        _kind: EffectKind,
        _template: &EffectTemplate,
        _args: &EffectArgs<'_>,
    ) -> bool {
        false
    }

    /// `CrimeManager.ReportBot(suspect, reporter, msg)`.
    fn report_bot(
        &self,
        _server: &GameServer,
        _suspect: &PlayerRef,
        _reporter: &PlayerRef,
        _msg: &str,
    ) -> bool {
        not_ported("CrimeManager.ReportBot");
        false
    }

    /// `CrimeManager.ReportBotExpired(player)`.
    fn report_bot_expired(&self, _server: &GameServer, _player: &PlayerRef) -> bool {
        not_ported("CrimeManager.ReportBotExpired");
        false
    }

    /// `DeclareDominion` after the expedition / lodestone checks: the new `DominionData`, the
    /// server-wide `SCDominionDataPacket(dominion, true, true)` and (caster is a character) the
    /// backpack consumed from the equipment (`ConsumeItem(SkillReagents, ...)`).
    fn declare_dominion(
        &self,
        _server: &GameServer,
        _caster: &WorldObject,
        _expedition_id: u32,
        _lodestone: &WorldObject,
    ) {
        not_ported("DeclareDominion");
    }

    /// `character.Appellations.Add(appellationId)`.
    fn give_appellation(&self, _server: &GameServer, _player: &PlayerRef, _appellation_id: u32) {
        not_ported("Appellations.Add");
    }

    /// `SpecialEffect` actions owned by the social subsystem (expedition / family / crime ones).
    fn apply_special_effect(
        &self,
        _server: &GameServer,
        _special_type: i32,
        _args: &EffectArgs<'_>,
        _values: [i32; 4],
    ) -> bool {
        false
    }
}

/// The hook slots (`server.systems.skills.ext`).
#[derive(Debug, Default)]
pub struct ExtHooks {
    pub npcs: HookSlot<dyn NpcSkillHooks>,
    pub items: HookSlot<dyn ItemSkillHooks>,
    pub quests: HookSlot<dyn QuestSkillHooks>,
    pub social: HookSlot<dyn SocialSkillHooks>,
}

/// The default (unported) behaviour of each hook trait.
struct Defaults;
impl NpcSkillHooks for Defaults {}
impl ItemSkillHooks for Defaults {}
impl QuestSkillHooks for Defaults {}
impl SocialSkillHooks for Defaults {}

impl ExtHooks {
    pub fn npcs(&self) -> std::sync::Arc<dyn NpcSkillHooks> {
        self.npcs
            .get()
            .unwrap_or_else(|| std::sync::Arc::new(Defaults))
    }
    pub fn items(&self) -> std::sync::Arc<dyn ItemSkillHooks> {
        self.items
            .get()
            .unwrap_or_else(|| std::sync::Arc::new(Defaults))
    }
    pub fn quests(&self) -> std::sync::Arc<dyn QuestSkillHooks> {
        self.quests
            .get()
            .unwrap_or_else(|| std::sync::Arc::new(Defaults))
    }
    pub fn social(&self) -> std::sync::Arc<dyn SocialSkillHooks> {
        self.social
            .get()
            .unwrap_or_else(|| std::sync::Arc::new(Defaults))
    }

    /// Routes an effect owned by another subsystem: `true` when a hook handled it.
    pub fn route_effect(
        &self,
        server: &GameServer,
        kind: EffectKind,
        template: &EffectTemplate,
        args: &EffectArgs<'_>,
    ) -> bool {
        self.npcs().apply_effect(server, kind, template, args)
            || self.items().apply_effect(server, kind, template, args)
            || self.quests().apply_effect(server, kind, template, args)
            || self.social().apply_effect(server, kind, template, args)
    }

    /// Routes a `SpecialEffect` action owned by another subsystem.
    pub fn route_special(
        &self,
        server: &GameServer,
        special_type: i32,
        args: &EffectArgs<'_>,
        values: [i32; 4],
    ) -> bool {
        self.npcs()
            .apply_special_effect(server, special_type, args, values)
            || self
                .items()
                .apply_special_effect(server, special_type, args, values)
            || self
                .quests()
                .apply_special_effect(server, special_type, args, values)
            || self
                .social()
                .apply_special_effect(server, special_type, args, values)
    }
}
