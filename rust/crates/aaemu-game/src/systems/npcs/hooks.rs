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
    fn on_unit_died(&self, _server: &GameServer, _unit: &WorldObject, _killer: Option<&WorldObject>) {}

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
    fn is_relation_valid(&self, _server: &GameServer, relation: u8, caster: &WorldObject, target: &WorldObject) -> bool {
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
    fn apply_area_buff(&self, _server: &GameServer, _caster: &WorldObject, _unit: &WorldObject, _buff_id: u32) {}

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
}

/// Implemented by the **economy** subsystem (items, loot, coffers, merchants, labor, money).
pub trait EconomyBridge: Send + Sync {
    /// `Npc.DoDie` loot: `LootingContainer` creation / `LootGameData` drops for `npc` killed
    /// by `killer`; returns whether loot is left (`HasLootLeft` / `SCLootableStatePacket`).
    fn on_npc_loot(&self, _server: &GameServer, _npc: &NpcRef, _killer: Option<&PlayerRef>) -> bool {
        false
    }

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
    fn give_loot_pack(&self, _server: &GameServer, _player: &PlayerRef, _loot_pack_id: u32) -> bool {
        false
    }

    /// `Character.ChangeLabor(change, actabilityId)`.
    fn change_labor(&self, _server: &GameServer, _player: &PlayerRef, _change: i16, _actability: i32) {}

    /// `Character.ChangeMoney(...)`/`SubtractMoney`; returns false if not enough money.
    fn change_money(&self, _server: &GameServer, _player: &PlayerRef, _amount: i64, _task: u8) -> bool {
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
}

/// Implemented by the **quests** subsystem (`QuestManager`, quest acts / components).
pub trait QuestsBridge: Send + Sync {
    /// `Character.Quests.HasQuest(questId)` (in progress).
    fn has_quest(&self, _server: &GameServer, _player: &PlayerRef, _quest_id: u32) -> bool {
        false
    }

    /// `Character.Quests.IsQuestComplete(questId)` (completed before).
    fn is_quest_completed(&self, _server: &GameServer, _player: &PlayerRef, _quest_id: u32) -> bool {
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
    fn can_trigger_sphere(&self, _server: &GameServer, _sphere_id: u32, _unit: &WorldObject) -> bool {
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
}

/// The hook slots (`server.systems.npcs.hooks`).
#[derive(Debug, Default)]
pub struct NpcsHooks {
    pub skills: HookSlot<dyn SkillsBridge>,
    pub economy: HookSlot<dyn EconomyBridge>,
    pub quests: HookSlot<dyn QuestsBridge>,
}
