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

    /// `Unit.DoDie` common part for an NPC (`base.DoDie`: buffs removed on death, combat
    /// state, `SCUnitDeathPacket`). Called by `npc::death` before the NPC-specific part.
    fn on_unit_died(&self, _server: &GameServer, _unit: &WorldObject, _killer: Option<&WorldObject>) {}
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
}

/// The hook slots (`server.systems.npcs.hooks`).
#[derive(Debug, Default)]
pub struct NpcsHooks {
    pub skills: HookSlot<dyn SkillsBridge>,
    pub economy: HookSlot<dyn EconomyBridge>,
    pub quests: HookSlot<dyn QuestsBridge>,
}
