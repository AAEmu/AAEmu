//! Behaviour the NPC / doodad subsystem needs from the other phase-3 subsystems. Each trait is
//! implemented by the subsystem that owns the behaviour and installed into
//! `server.systems.npcs.hooks` from its `System::start`:
//!
//! ```ignore
//! server.systems.npcs.hooks.economy.install(Arc::new(MyEconomyBridge));
//! ```
//!
//! Every method defaults to the "not ported yet" behaviour (documented per method), so this
//! subsystem works on its own and the flows degrade the way the C# would without that manager's
//! effect. Methods are grouped by the C# call site; add new ones with a default body only.

use crate::server::GameServer;
use crate::systems::HookSlot;
use crate::world::{NpcRef, PlayerRef, WorldObject};

/// Implemented by the **skills** subsystem (`SkillManager`, `Skill.Use`, buffs, combat).
pub trait SkillsBridge: Send + Sync {
    /// `new Skill(SkillManager.GetSkillTemplate(skillId)).Use(caster, casterObj, targetObj)`
    /// used by doodad funcs / NPCs (`DoodadFuncSkillHit`, `DoodadFuncUse`, buffs from funcs).
    /// `caster` may be a doodad (`SkillCasterDoodad`). Returns false when not executed.
    fn use_skill(
        &self,
        _server: &GameServer,
        _caster: &WorldObject,
        _target: &WorldObject,
        _skill_id: u32,
    ) -> bool {
        false
    }

    /// `SkillManager.Instance.GetSkillTemplate(id)?.CrimePoint` (doodad theft evidence) — 0
    /// when unknown.
    fn skill_crime_point(&self, _server: &GameServer, _skill_id: u32) -> i32 {
        0
    }

    /// `Unit.DoDie` common part for an NPC: `InterruptSkills()`, `Events.OnDeath` /
    /// `ParentWorld.Events.OnUnitKilled` / `killer.Events.OnKill`, `Buffs.RemoveEffectsOnDeath()`.
    /// Called by `npc::death::do_die` at the start of `base.DoDie`; `npc::death` itself does the
    /// rest (`IsInBattle = false`, `SCUnitDeathPacket`, loot, target / aggro cleanup packets).
    fn on_unit_died(
        &self,
        _server: &GameServer,
        _unit: &WorldObject,
        _killer: Option<&WorldObject>,
    ) {
    }

    /// `Unit.UpdateGearBonuses(itemAdded, itemRemoved)` (gear `unit_modifiers` + gems under
    /// `Buffs.GearBonusesIndex`, `ApplyEquipEffects`, weapon-wield / armor-grade / item-set
    /// buffs). Called by `npc::manager::create` for every equipped item
    /// (`EquipmentContainer.OnEnterContainer`) and once with `(None, None)`. Default: no gear
    /// bonuses.
    fn update_gear_bonuses(
        &self,
        _server: &GameServer,
        _unit: &WorldObject,
        _item_added: Option<&crate::models::item::Item>,
        _item_removed: Option<&crate::models::item::Item>,
    ) {
    }

    /// `Buffs.CheckBuffTag(tagId)` on a unit (e.g. `PortalManager.UsePortal`'s
    /// `TagOverburdened` check). Default: no buff has tags (false).
    fn check_buff_tag(&self, _server: &GameServer, _unit: &WorldObject, _tag_id: u32) -> bool {
        false
    }

    /// `SkillTargetingUtil.IsRelationValid(relation, caster, target)` (`AreaTrigger.OnEnter`).
    /// Default: [`super::area_trigger::default_is_relation_valid`] (no faction relations).
    fn is_relation_valid(
        &self,
        _server: &GameServer,
        relation: u8,
        caster: &WorldObject,
        target: &WorldObject,
    ) -> bool {
        super::area_trigger::default_is_relation_valid(relation, caster, target)
    }

    /// `SkillTargetingUtil.FilterWithRelation(relation, caster, units)`
    /// (`AreaTrigger.ApplyEffects`). Default:
    /// [`super::area_trigger::default_filter_with_relation`].
    fn filter_with_relation(
        &self,
        _server: &GameServer,
        relation: u8,
        caster: &WorldObject,
        units: Vec<WorldObject>,
    ) -> Vec<WorldObject> {
        super::area_trigger::default_filter_with_relation(relation, caster, units)
    }

    /// `AreaTrigger.OnEnter`: `InsideBuffTemplate.Apply(caster, SkillCasterUnit(caster), unit,
    /// SkillCastUnitTarget(unit), null, new EffectSource(), null, now)` for the first trigger
    /// the unit is inside. Default: no buff.
    fn apply_area_buff(
        &self,
        _server: &GameServer,
        _caster: &WorldObject,
        _unit: &WorldObject,
        _buff_id: u32,
    ) {
    }

    /// `AreaTrigger.ApplyEffects` for one unit inside: for every effect template of
    /// `effect_ids` (skipping `BuffEffect`s whose buff the unit has) `effect.Apply(caster, ...,
    /// CastBuff(unit's effect of buff_id) ?? CastSkill(skill_id, 0), ...)`. Default: nothing.
    fn apply_area_effects(
        &self,
        _server: &GameServer,
        _caster: &WorldObject,
        _unit: &WorldObject,
        _buff_id: u32,
        _skill_id: u32,
        _effect_ids: &[u32],
    ) {
    }

    /// `Gimmick.DoGimmickSkill`: schedules `UseSkillTask(new Skill(template), caster
    /// (= ParentWorld.GetUnit(SpawnerUnitId), may be null), new SkillDoodad(gimmick.ObjId),
    /// gimmick, SkillCastPositionTarget(pos), new SkillObject())`. Default: nothing.
    fn use_gimmick_skill(
        &self,
        _server: &GameServer,
        _caster: Option<&WorldObject>,
        _gimmick: &WorldObject,
        _skill_id: u32,
        _pos: (f32, f32, f32),
    ) {
    }

    // ---- doodad funcs / world interactions (`systems::npcs::doodad::{funcs, interactions}`) ----

    /// `DoodadFuncFakeUse`: `new Skill(GetSkillTemplate(skillId)).Use(caster, casterObj,
    /// targetObj, null, false, out _)` (`scheduled == false`, `target == None`), or
    /// `DoodadFuncUse`: `TaskManager.Schedule(new UseSkillTask(skill, caster, casterObj, target,
    /// targetObj, null), 0 ms)` (`scheduled == true`). Default: the skill is not used.
    #[allow(clippy::too_many_arguments)]
    fn use_doodad_func_skill(
        &self,
        _server: &GameServer,
        _caster: &WorldObject,
        _caster_obj: DoodadSkillCaster,
        _target: Option<&WorldObject>,
        _target_obj: DoodadSkillTarget,
        _skill_id: u32,
        _scheduled: bool,
    ) {
    }

    /// The character part of `DoodadFuncSkillHit.Use` (`SkillId > 0`): a `SkillItem` caster with
    /// a new obj id, `SkillCastDoodadTarget(doodad)`, and for every `SpecialEffect` of type
    /// `GainItem*` of the skill: `AcquireDefaultItem(Loot, Value1, 1, grade)` + `skill.Use`.
    /// Default: nothing.
    fn doodad_func_skill_hit(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _doodad: &WorldObject,
        _skill_id: u32,
    ) {
    }

    /// `target.Buffs.AddBuff(buffId, caster)` (`DoodadFuncBuff`). Default: no buff.
    fn add_buff(
        &self,
        _server: &GameServer,
        _target: &WorldObject,
        _buff_id: u32,
        _caster: &WorldObject,
    ) {
    }

    /// `character.SkillCancelled = true` (harvest protection in `DoodadFuncUse` /
    /// `CropHarvest`). Default: nothing.
    fn cancel_skill(&self, _server: &GameServer, _player: &PlayerRef) {}

    /// `caster.InterruptSkills()` (`RecoverItem` interaction failure). Default: nothing.
    fn interrupt_skills(&self, _server: &GameServer, _unit: &WorldObject) {}

    // ---- doodad phase machine / DoodadManager runtime (`systems::npcs::doodad::{phase, manager}`) ----

    /// `CrimeManager.GenerateEvidenceFromTheft(criminal, doodad)` (`Doodad.Use` on a doodad owned
    /// by another character). Default: no evidence.
    fn generate_theft_evidence(
        &self,
        _server: &GameServer,
        _criminal: &PlayerRef,
        _doodad: &crate::world::DoodadRef,
    ) {
    }

    /// `SkillManager.GetSkillTemplate(skillId)?.ConsumeLaborPower` (`CSCreateDoodadPacket` labor
    /// cost). Default: 0.
    fn skill_consume_labor_power(&self, _server: &GameServer, _skill_id: u32) -> i32 {
        0
    }
}

/// `SkillCaster` of a skill used by a doodad func.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DoodadSkillCaster {
    /// `SkillCasterUnit { ObjId }`.
    Unit(u32),
    /// `SkillCasterDoodad { ObjId }`.
    Doodad(u32),
}

/// `SkillCastTarget` of a skill used by a doodad func.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DoodadSkillTarget {
    /// `SkillCastUnitTarget { ObjId }`.
    Unit(u32),
    /// `SkillCastDoodadTarget { ObjId }`.
    Doodad(u32),
}

/// Implemented by the **economy** subsystem (items, loot, coffers, merchants, labor, money).
pub trait EconomyBridge: Send + Sync {
    /// `Npc.DoDie` loot: `LootingContainer` creation / `LootGameData` drops for `npc` killed
    /// by `killer`; returns whether loot is left (`HasLootLeft` / `SCLootableStatePacket`).
    fn on_npc_loot(
        &self,
        _server: &GameServer,
        _npc: &NpcRef,
        _killer: Option<&PlayerRef>,
    ) -> bool {
        false
    }

    /// Drops the `LootingContainer` of `obj_id`: a new NPC / doodad is created with that
    /// (recycled) object id, and the C# container is a per-unit field that starts empty.
    fn forget_loot(&self, _server: &GameServer, _obj_id: u32) {}

    /// `ItemManager.Instance.GetItemByItemId(itemId)?.TemplateId` / `.UccId`.
    fn item_template_and_ucc(&self, _server: &GameServer, _item_id: u64) -> Option<(u32, u64)> {
        None
    }

    /// Gives `count` of `template_id` (grade) to `player` (`Inventory.Bag.AcquireDefaultItem`
    /// with `task`). Returns false when it could not be given (bag full).
    fn acquire_item(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _task: u8,
        _template_id: u32,
        _count: i32,
        _grade: i32,
    ) -> bool {
        false
    }

    /// `Inventory.ConsumeItem(..)` / `GetItemsCount`: consumes `count` of `template_id`;
    /// returns the number consumed.
    fn consume_item(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _task: u8,
        _template_id: u32,
        _count: i32,
    ) -> i32 {
        0
    }

    /// `Inventory.GetItemsCount(templateId)`.
    fn item_count(&self, _server: &GameServer, _player: &PlayerRef, _template_id: u32) -> i32 {
        0
    }

    /// Doodad loot (`DoodadFuncLootItem` / `LootPack` / `ItemManager.LootPack`): gives the pack
    /// to `player`; returns false if nothing was given.
    fn give_loot_pack(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _loot_pack_id: u32,
    ) -> bool {
        false
    }

    /// `Character.ChangeLabor(change, actabilityId)`.
    fn change_labor(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _change: i16,
        _actability: i32,
    ) {
    }

    /// `Character.ChangeMoney(...)`/`SubtractMoney`; returns false if not enough money.
    fn change_money(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _amount: i64,
        _task: u8,
    ) -> bool {
        false
    }

    /// `npc.LootingContainer != null && npc.LootingContainer.Items.Count > 0` (the
    /// `NpcSpawner.DoDespawn` despawn-time extension by `LootDespawnExtensionTime`).
    fn npc_has_loot_items(&self, _server: &GameServer, _npc: &NpcRef) -> bool {
        false
    }

    /// The `SpawnManager.CheckRespawns` loot part: `World.GetNpcsToMakePublicLooting()` +
    /// `LootingContainer.MakeLootPublic()` for every NPC of world instance `world_id`
    /// (called once per respawn tick).
    fn make_loot_public_tick(&self, _server: &GameServer, _world_id: u32) {}

    /// `SpecialtyManager.SellSpecialty` mails: `new MailForSpeciality(...)` +
    /// `FinalizeForSeller()` (`for_crafter == false`) / `FinalizeForCrafter()` + `Send()`.
    /// Returns `Send()`'s result. Default: mails are not ported (false -> the sale stops with
    /// `MailUnknownFailure` for the seller mail, as a failed send does in C#).
    fn send_specialty_mail(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _mail: &crate::systems::npcs::specialty::SpecialtyMail,
        _for_crafter: bool,
    ) -> bool {
        false
    }

    /// `Inventory.Equipment.ConsumeItem(task, templateId, count, preferredItem)` (selling the
    /// equipped trade pack). Returns the number consumed.
    fn consume_equipment_item(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _task: u8,
        _template_id: u32,
        _count: i32,
        _preferred_item_id: u64,
    ) -> i32 {
        0
    }

    // ---- doodad funcs / world interactions / coffers (`systems::npcs::doodad`) ----

    /// `ItemManager.IsAutoEquipTradePack(templateId)` (`BackpackTemplate` not bound on equip).
    fn is_auto_equip_trade_pack(&self, _server: &GameServer, _template_id: u32) -> bool {
        false
    }

    /// `Inventory.TakeoffBackpack(task, glidersOnly)` (true when nothing is equipped). Default:
    /// true when the backpack slot is empty, else false (moving items is not ported).
    fn takeoff_backpack(
        &self,
        _server: &GameServer,
        player: &PlayerRef,
        _task: u8,
        _gliders_only: bool,
    ) -> bool {
        backpack_slot_empty(player)
    }

    /// `Inventory.CanReplaceGliderInBackpackSlot()`. Default: true when the backpack slot is
    /// empty (glider templates are not known here).
    fn can_replace_glider_in_backpack_slot(
        &self,
        _server: &GameServer,
        player: &PlayerRef,
    ) -> bool {
        backpack_slot_empty(player)
    }

    /// `ItemManager.Create(templateId, count, 0)` + `Inventory.Equipment.AddOrMoveExistingItem(
    /// task, item, Backpack)` (`DoodadFuncLootItem` trade packs).
    fn create_item_in_backpack_slot(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _task: u8,
        _template_id: u32,
        _count: i32,
    ) -> bool {
        false
    }

    /// `Inventory.Equipment.AddOrMoveExistingItem(task, item, Backpack)` of an existing item
    /// (`DoodadFuncRecoverItem`).
    fn move_item_to_backpack_slot(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _task: u8,
        _item_id: u64,
    ) -> bool {
        false
    }

    /// `Inventory.Bag.AddOrMoveExistingItem(task, item)` of an existing item.
    fn move_item_to_bag(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _task: u8,
        _item_id: u64,
    ) -> bool {
        false
    }

    /// `Inventory.SystemContainer.AddOrMoveExistingItem(task, item)` (`DoodadFuncConsumeChanger`).
    fn move_item_to_system_container(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _task: u8,
        _item_id: u64,
    ) -> bool {
        false
    }

    /// `Equipment.RemoveItem(task, item, true)` (`DoodadFuncBuyFish` / `ConvertFish`).
    fn remove_equipment_item(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _task: u8,
        _item_id: u64,
    ) -> bool {
        false
    }

    /// `Inventory.Bag.SpaceLeftForItem(templateId)`.
    fn bag_space_left_for_item(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _template_id: u32,
    ) -> i32 {
        0
    }

    /// `Inventory.Bag.ConsumeItem(task, templateId, count, null)`; returns the number consumed.
    fn bag_consume_item(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _task: u8,
        _template_id: u32,
        _count: i32,
    ) -> i32 {
        0
    }

    /// `Inventory.TryEquipNewBackPack(task, templateId, count)`.
    fn try_equip_new_backpack(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _task: u8,
        _template_id: u32,
        _count: i32,
    ) -> bool {
        false
    }

    /// `DoodadFuncLootPack`: `actAbility = GetSkillActAbility(skillId)`, `GetPack(lootPackId)
    /// .GeneratePack(character, actAbility)`; when `Bag.FreeSlotCount >= contents.Count`:
    /// `GiveLootPack(character, actAbility, task, contents)` and `Some(true)`; else `Some(false)`
    /// (bag full). `None` when the pack does not exist (C# `NullReferenceException`). Default:
    /// `None`.
    fn give_doodad_loot_pack(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _loot_pack_id: u32,
        _skill_id: u32,
        _task: u8,
    ) -> Option<bool> {
        None
    }

    /// `DoodadFuncConvertFish`: `GetLootConvertFish(backpackTemplateId)`, first trophy ->
    /// `FishDetailsGameData.Create` + `Bag.AddOrMoveExistingItem(task, fish)`. Default: nothing.
    fn give_convert_fish_trophy(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _backpack_template_id: u32,
        _task: u8,
    ) {
    }

    /// Consume the exact equipped fish and pay its static refund in one inventory operation.
    fn try_sell_equipped_fish(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _backpack_id: u64,
        _backpack_template_id: u32,
        _buy_func_id: u32,
    ) -> bool {
        false
    }

    /// Deliver a permitted trophy and consume the exact equipped fish atomically.
    fn try_convert_equipped_fish(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _backpack_id: u64,
        _backpack_template_id: u32,
        _convert_func_id: u32,
        _task: u8,
    ) -> bool {
        false
    }

    /// `HousingManager.GetHouseById(houseId)` + `house.AllowedToInteract(player)`: `None` when
    /// the house does not exist. Default: the housing subsystem.
    fn house_allowed_to_interact(
        &self,
        server: &GameServer,
        house_db_id: u32,
        player: &PlayerRef,
    ) -> Option<bool> {
        crate::systems::housing::allowed_to_interact_by_id(server, house_db_id, player)
    }

    /// `HousingManager.Demolish(connection, house, false, false)` (`Demolish` interaction).
    /// Default: the housing subsystem.
    fn demolish_house(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        house: &crate::world::HouseRef,
    ) {
        crate::systems::housing::demolish_by_player(server, player, house);
    }

    /// `CofferContainer.CofferType` (`ChestType`) of coffer container `container_id`.
    fn coffer_chest_type(&self, _server: &GameServer, _container_id: u64) -> u8 {
        0
    }

    /// `FamilyManager.GetFamilyOfCharacter(characterId)` (coffer / house permissions of an
    /// offline owner). Default: 0.
    fn family_of_character(&self, _server: &GameServer, _character_id: u32) -> u32 {
        0
    }

    /// `ExpeditionManager.GetExpeditionOfCharacter(characterId)`. Default: 0.
    fn expedition_of_character(&self, _server: &GameServer, _character_id: u32) -> u32 {
        0
    }

    /// `MagicalEnchant` interaction: `Bag.GetItemByItemId(targetItemId) is EquipItem` ->
    /// `RuneId = runeTemplateId`, `SCItemSocketingLunastoneResultPacket` +
    /// `SCItemTaskSuccessPacket(EnchantMagical, [ItemUpdate])`. Default: nothing.
    fn magical_enchant(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _target_item_id: u64,
        _rune_template_id: u32,
    ) {
    }

    // ---- doodad phase machine / DoodadManager runtime (`systems::npcs::doodad::{manager, db}`) ----

    /// The item `item_id` of `player` (`Inventory.GetItemById` when `bag_only == false`,
    /// `Inventory.Bag.GetItemByItemId` otherwise). Default: `None` (no items known).
    fn doodad_source_item(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _item_id: u64,
        _bag_only: bool,
    ) -> Option<DoodadSourceItem> {
        None
    }

    /// `ItemManager.GetItemIdsFromDoodad(doodadTemplateId)`. Default: none.
    fn item_ids_from_doodad(&self, _server: &GameServer, _doodad_template_id: u32) -> Vec<u32> {
        Vec::new()
    }

    /// `CreatePlayerDoodad` per item of `GetItemIdsFromDoodad`: `character.ItemUse(preferredItem)`
    /// + `Inventory.ConsumeItem([Inventory], DoodadCreate, templateId, 1, preferredItem)`.
    fn consume_doodad_source_item(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _template_id: u32,
        _preferred_item_id: Option<u64>,
    ) {
    }

    /// Consume exactly the owned placement item and its labor cost, after checking that the
    /// item creates this doodad. Failure leaves both inventory and labor unchanged.
    fn try_consume_doodad_source_item(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _doodad_template_id: u32,
        _item_id: u64,
        _labor_cost: i16,
    ) -> bool {
        false
    }

    /// `DoodadCoffer.InitializeCoffer(ownerId)`: creates the coffer container and stores its id
    /// in `doodad.coffer.container_id`. Default: nothing.
    fn initialize_coffer(
        &self,
        _server: &GameServer,
        _doodad: &crate::world::DoodadRef,
        _owner_character_id: u32,
    ) {
    }

    /// `Doodad.Delete()` item cleanup: `GetItemByItemId(itemId)` held by a `None` / `System`
    /// container -> `RemoveItem(Invalid, item, true)`. Default: nothing.
    fn remove_expired_doodad_item(&self, _server: &GameServer, _item_id: u64) {}

    /// `DeleteDoodadById` item part: the attached item loses its container and its id is
    /// released; the attached container is deleted. Default: nothing.
    fn release_deleted_doodad_items(
        &self,
        _server: &GameServer,
        _item_id: u64,
        _container_id: u64,
    ) {
    }

    /// `HousingManager.GetHouseAtLocation(x, y)` (database id of the house whose land covers
    /// the point). Default: the housing subsystem.
    fn house_at_location(&self, server: &GameServer, x: f32, y: f32) -> Option<u32> {
        crate::systems::housing::house_at_location(server, x, y).map(|h| h.data().id)
    }
}

/// The item fields `CSCreateDoodadPacket` / `CreatePlayerDoodad` read.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct DoodadSourceItem {
    pub item_id: u64,
    pub template_id: u32,
    /// `Template.MaxCount`.
    pub max_count: i32,
    /// `Template.UseSkillId`.
    pub use_skill_id: u32,
}

/// `GetEquippedBySlot(Backpack) == null`.
pub fn backpack_slot_empty(player: &PlayerRef) -> bool {
    let equipment = player.data().inventory.equipment().clone();
    let c = equipment.lock().expect("container poisoned");
    c.get_item_by_slot(crate::models::container::equip_slot::BACKPACK)
        .is_none()
}

/// `HousingManager.GetHouseById(id)` (the housing subsystem's `_houses`; houses only placed in
/// a world instance, e.g. by tests, are found there as a fallback).
pub fn house_by_db_id(server: &GameServer, id: u32) -> Option<crate::world::HouseRef> {
    crate::systems::housing::house_by_id(server, id).or_else(|| {
        server.world.worlds().iter().find_map(|w| {
            w.all_of_kind(crate::world::ObjectKind::House)
                .into_iter()
                .filter_map(|o| o.as_house().cloned())
                .find(|h| h.data().id == id)
        })
    })
}

/// Implemented by the **quests** subsystem (`QuestManager`, quest acts / components).
pub trait QuestsBridge: Send + Sync {
    /// `Character.Quests.HasQuest(questId)` (in progress).
    fn has_quest(&self, _server: &GameServer, _player: &PlayerRef, _quest_id: u32) -> bool {
        false
    }

    /// `Character.Quests.IsQuestComplete(questId)` (completed before).
    fn is_quest_completed(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _quest_id: u32,
    ) -> bool {
        false
    }

    /// `Character.Quests.AddQuest(questId)` (doodad / NPC quest givers). Returns success.
    fn start_quest(&self, _server: &GameServer, _player: &PlayerRef, _quest_id: u32) -> bool {
        false
    }

    /// `Character.Quests.OnInteraction(type, target)` / `OnDoodadUse` style triggers (quest
    /// objectives counting doodad use / NPC talk / kills).
    fn on_interaction(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _interaction: u32,
        _target: &WorldObject,
    ) {
    }

    /// Escort quest failure, including NPC deaths without a player killer.
    fn on_npc_died(&self, _server: &GameServer, _npc: &NpcRef) {}

    /// `QuestManager` kill objective counting (`Npc.DoDie` → `OnKill`).
    fn on_npc_killed(&self, _server: &GameServer, _player: &PlayerRef, _npc: &NpcRef) {}

    /// `Npc.DoDie` experience reward hook is the skills / quests concern: the killer's
    /// `AddExp(KillExp)` — implemented by quests (`Character.AddExp` lives with progression).
    fn add_exp(&self, _server: &GameServer, _player: &PlayerRef, _exp: i32, _target_obj_id: u32) {}

    /// `QuestManager.GetComponent(componentId)` reduced to what the sphere code reads
    /// (`ParentQuestTemplate.Id`, has a `QuestActConAcceptSphere` act). Default: unknown.
    fn quest_component(
        &self,
        _server: &GameServer,
        _component_id: u32,
    ) -> Option<super::sphere_quest::QuestComponentInfo> {
        None
    }

    /// `UnitRequirementsGameData.CanTriggerSphere(sphere, unit)`. Default: no requirements.
    fn can_trigger_sphere(
        &self,
        _server: &GameServer,
        _sphere_id: u32,
        _unit: &WorldObject,
    ) -> bool {
        true
    }

    /// `QuestManager.DoOnEnterQuestStarterSphere(character, starter, lastCheckLocation)`.
    fn on_enter_quest_starter_sphere(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _quest_template_id: u32,
        _sphere_id: u32,
        _sphere: &super::sphere_quest::SphereQuest,
        _last_check: [f32; 3],
    ) {
    }

    /// `QuestManager.DoOnEnterSphereEvents(owner, sphere, lastCheckLocation)` (a
    /// `SphereQuestTrigger` of `quest_template_id`).
    fn on_enter_sphere(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _quest_template_id: u32,
        _sphere: &super::sphere_quest::SphereQuest,
        _last_check: [f32; 3],
    ) {
    }

    /// `QuestManager.DoOnExitSphereEvents(owner, sphere, lastCheckLocation)`.
    fn on_exit_sphere(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _quest_template_id: u32,
        _sphere: &super::sphere_quest::SphereQuest,
        _last_check: [f32; 3],
    ) {
    }

    /// `QuestManager.DoReportEvents(character, questId, npcObjId, doodadObjId, selective)`
    /// (`DoodadFuncQuest` on a quest in progress).
    fn do_report_events(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _quest_id: u32,
        _npc_obj_id: u32,
        _doodad_obj_id: u32,
        _selective: i32,
    ) {
    }
}

/// The hook slots (`server.systems.npcs.hooks`).
#[derive(Debug, Default)]
pub struct NpcsHooks {
    pub skills: HookSlot<dyn SkillsBridge>,
    pub economy: HookSlot<dyn EconomyBridge>,
    pub quests: HookSlot<dyn QuestsBridge>,
}
