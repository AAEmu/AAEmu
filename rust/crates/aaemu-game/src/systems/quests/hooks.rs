//! Behaviour the quest code needs from other subsystems (rewards, items, skills, NPCs,
//! teams). Install an implementation with `server.systems.quests.hooks.install(..)`; every
//! method defaults to "not ported" (a no-op / neutral answer).
//!
//! Two groups:
//!
//! * **Locked hooks** get `&mut Character`: they run while the quest code holds the
//!   character's lock (the C# calls them synchronously in the middle of a step). They must not
//!   lock that player again or call into the world; to raise a quest event from inside one
//!   (e.g. `DoItemsAcquiredEvents` after creating an item, `DoOnLevelUpEvents` after a level
//!   up) just call the normal `quests::events::*` function: a call nested in a quest operation
//!   of the same character is queued and handled before the operation ends.
//! * **World hooks** run after the character lock is released.

use crate::models::character::Character;
use crate::models::item::Item;
use crate::server::GameServer;
use crate::world::{NpcRef, PlayerRef, WorldObject};

use super::model::ItemCreationDefinition;

/// The quest subsystem's view of the other subsystems.
pub trait QuestHooks: Send + Sync {
    // ---- locked hooks ----

    /// `Character.AddExp(exp, shouldAddAbilityExp)`.
    fn add_exp(
        &self,
        _server: &GameServer,
        _player: Option<&PlayerRef>,
        _c: &mut Character,
        _exp: i32,
        _should_add_ability_exp: bool,
    ) {
    }

    /// `Character.ChangeMoney(SlotType.None, SlotType.Inventory, amount)`.
    fn change_money(
        &self,
        _server: &GameServer,
        _player: Option<&PlayerRef>,
        _c: &mut Character,
        _amount: i32,
    ) {
    }

    /// `Character.ChangeGamePoints(kind, amount)` (`GamePointKind`: 0 honor, 1 vocation).
    fn change_game_points(
        &self,
        _server: &GameServer,
        _player: Option<&PlayerRef>,
        _c: &mut Character,
        _kind: u8,
        _amount: i32,
    ) {
    }

    /// `Character.ChangeLabor(amount, actabilityId)`.
    fn change_labor(
        &self,
        _server: &GameServer,
        _player: Option<&PlayerRef>,
        _c: &mut Character,
        _amount: i16,
        _actability_id: i32,
    ) {
    }

    /// `Character.AddCrime(points)`.
    fn add_crime(
        &self,
        _server: &GameServer,
        _player: Option<&PlayerRef>,
        _c: &mut Character,
        _points: i16,
    ) {
    }

    /// `Inventory.Bag.AcquireDefaultItem(task, templateId, count, gradeId)`.
    #[allow(clippy::too_many_arguments)]
    fn acquire_default_item(
        &self,
        _server: &GameServer,
        _player: Option<&PlayerRef>,
        _c: &mut Character,
        _task: u8,
        _template_id: u32,
        _count: i32,
        _grade_id: u8,
    ) -> bool {
        false
    }

    /// `Inventory.TryEquipNewBackPack(task, templateId, count, gradeId)`.
    #[allow(clippy::too_many_arguments)]
    fn try_equip_new_backpack(
        &self,
        _server: &GameServer,
        _player: Option<&PlayerRef>,
        _c: &mut Character,
        _task: u8,
        _template_id: u32,
        _count: i32,
        _grade_id: u8,
    ) -> bool {
        false
    }

    /// `Inventory.ConsumeItem(null, task, templateId, count, preferredItem)`; returns the
    /// number of units removed.
    #[allow(clippy::too_many_arguments)]
    fn consume_item(
        &self,
        _server: &GameServer,
        _player: Option<&PlayerRef>,
        _c: &mut Character,
        _task: u8,
        _template_id: u32,
        _count: i32,
        _preferred_item_id: Option<u64>,
    ) -> i32 {
        0
    }

    /// `MailManager.CreateQuestRewardMails(owner, quest, items, copper)` + `mail.Send()` for
    /// each; returns the number of mails whose `Send()` failed.
    fn send_quest_reward_mails(
        &self,
        _server: &GameServer,
        _player: Option<&PlayerRef>,
        _c: &mut Character,
        _quest_template_id: u32,
        _items: &[ItemCreationDefinition],
        _copper: i32,
    ) -> usize {
        0
    }

    /// `ItemManager.IsAutoEquipTradePack(templateId)`.
    fn is_auto_equip_trade_pack(&self, _server: &GameServer, _template_id: u32) -> bool {
        false
    }

    /// `Inventory.CanReplaceGliderInBackpackSlot()`.
    fn can_replace_glider_in_backpack_slot(&self, _server: &GameServer, _c: &Character) -> bool {
        true
    }

    /// `SummonMate.DetailLevel` of an item (`None` = not a `SummonMate`). The default reads the
    /// loaded item details.
    fn summon_mate_level(&self, _server: &GameServer, item: &Item) -> Option<u8> {
        (item.class == crate::models::item::ItemClass::SummonMate)
            .then_some(item.details.mate_level)
    }

    // ---- world hooks (character unlocked) ----

    /// `Unit.UseSkill(skillId, target)` (quest component skills: the player on itself, or an
    /// NPC of the component on itself).
    fn use_skill(
        &self,
        _server: &GameServer,
        _caster: &WorldObject,
        _skill_id: u32,
        _target: &WorldObject,
    ) {
    }

    /// `Npc.SetFaction(factionId)` (`QuestNpcAiName.AttackUnit` components).
    fn set_npc_faction(&self, _server: &GameServer, _npc: &NpcRef, _faction_id: u32) {}

    /// `TeamManager.GetTeamByObjId(player.ObjId)?.Members` (other members are sent the
    /// team-shared quest events).
    fn team_members(&self, _server: &GameServer, _player: &PlayerRef) -> Vec<PlayerRef> {
        Vec::new()
    }

    /// `Npc.GetAggroRatingInPercent(objId)` (`QuestActObjAggro`).
    fn npc_aggro_rating_percent(&self, _server: &GameServer, _npc: &NpcRef, _obj_id: u32) -> f32 {
        100.0
    }

    /// `TimedRewardsManager.DoDailyAccountLogin(accountId)` (daily reset task).
    fn daily_account_login(&self, _server: &GameServer, _account_id: u32) {}
}

/// The hooks used when nothing is installed.
#[derive(Debug, Default)]
pub struct DefaultQuestHooks;

impl QuestHooks for DefaultQuestHooks {}
