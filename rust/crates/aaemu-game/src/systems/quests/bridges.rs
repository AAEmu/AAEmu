//! The quest subsystem's implementations of the other subsystems' hooks, and the
//! [`QuestHooks`] the quest code itself calls:
//!
//! * [`QuestsForSkills`]: `skills::ext::QuestSkillHooks` (item use / aggro / engage-combat
//!   quest events, quest state checks, quest area spheres, `AcceptQuestEffect`,
//!   `CinemaEffect`);
//! * [`QuestsForNpcs`]: `npcs::hooks::QuestsBridge` (kill credit + kill experience, doodad
//!   quests, quest sphere events);
//! * [`GameQuestHooks`]: the quest code's view of the world. The skills (`AddExp`,
//!   `ChangeGamePoints`, `ChangeLabor`, `UseSkill`, `SetFaction`, aggro ranking), social
//!   (`AddCrime`, team members) parts call those subsystems directly; the item / money / mail
//!   parts are forwarded to the economy subsystem's `QuestHooks` installed in
//!   `server.systems.quests.economy_hooks` (trait defaults until then).
//!
//! Installed by [`install`] from `System::start`.

use std::sync::Arc;

use crate::models::character::Character;
use crate::models::item::Item;
use crate::server::GameServer;
use crate::systems::npcs::hooks::QuestsBridge;
use crate::systems::npcs::sphere_quest::{QuestComponentInfo, SphereQuest as NpcSphereQuest};
use crate::systems::skills::data::{EffectKind, EffectTemplate};
use crate::systems::skills::effects::EffectArgs;
use crate::systems::skills::ext::QuestSkillHooks;
use crate::systems::skills::geometry::Target;
use crate::systems::skills::types::SkillCaster;

use crate::world::{NpcRef, PlayerRef, WorldObject};

use super::hooks::{DefaultQuestHooks, QuestHooks};
use super::model::ItemCreationDefinition;
use super::spheres::SphereQuest;

/// Installs this subsystem's hook implementations (keeps an already installed economy
/// forwarder).
pub fn install(server: &GameServer) {
    install_economy(server);
    server
        .systems
        .skills
        .ext
        .quests
        .install(Arc::new(QuestsForSkills));
    server
        .systems
        .npcs
        .hooks
        .quests
        .install(Arc::new(QuestsForNpcs));
    server
        .systems
        .quests
        .hooks
        .install(Arc::new(GameQuestHooks));
}

/// `Inventory.GetItemById(id)` of a character: (template id, count).
pub fn inventory_item(player: &PlayerRef, item_id: u64) -> Option<(u32, i32)> {
    let c = player.data();
    for st in crate::models::container::slot_type::ALL {
        let Some(container) = c.inventory.get(st) else {
            continue;
        };
        let g = container.lock().expect("container poisoned");
        if let Some(i) = g.get_item_by_id(item_id) {
            return Some((i.template_id, i.count));
        }
    }
    None
}

/// `Character.ItemUse(itemId)`: `Events.OnItemUse` for an item with a positive count.
pub fn item_use(server: &GameServer, player: &PlayerRef, item_id: u64) {
    if let Some((template_id, count)) = inventory_item(player, item_id) {
        if count > 0 {
            super::events::on_item_use(server, player, template_id);
        }
    }
}

/// `skills::ext::QuestSkillHooks` of the quest subsystem.
pub struct QuestsForSkills;

impl QuestSkillHooks for QuestsForSkills {
    fn item_use(&self, server: &GameServer, player: &PlayerRef, item_id: u64) {
        item_use(server, player, item_id);
    }

    fn on_aggro(&self, server: &GameServer, player: &PlayerRef, npc: &NpcRef) {
        super::events::on_aggro(server, player, npc);
    }

    fn engage_combat_quest(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        quest_id: u32,
        npc_obj_id: u32,
    ) {
        let (completed, active) = {
            let c = player.data();
            (
                super::events::has_quest_completed_in(&c, quest_id),
                super::events::has_quest_in(&c, quest_id),
            )
        };
        if !completed && !active {
            super::events::add_quest_from_npc(server, player, quest_id, npc_obj_id);
        }
    }

    fn quest_completed(&self, _server: &GameServer, player: &PlayerRef, quest_id: u32) -> bool {
        super::events::has_quest_completed_in(&player.data(), quest_id)
    }

    fn quest_step(&self, _server: &GameServer, player: &PlayerRef, quest_id: u32) -> Option<u8> {
        player
            .data()
            .quests
            .active
            .iter()
            .find(|q| q.template_id == quest_id)
            .map(|q| q.step)
    }

    fn inside_area_sphere(
        &self,
        server: &GameServer,
        sphere_id: u32,
        value2: u32,
        pos: (f32, f32, f32),
    ) -> bool {
        server
            .systems
            .quests
            .spheres
            .is_inside_area_sphere(&server.game_data.systems.quests, sphere_id, value2, pos, 0)
            .is_some()
    }

    fn apply_effect(
        &self,
        server: &GameServer,
        _kind: EffectKind,
        template: &EffectTemplate,
        args: &EffectArgs<'_>,
    ) -> bool {
        match template {
            EffectTemplate::AcceptQuest { quest_id, .. } => {
                tracing::trace!("AcceptQuestEffect");
                // Only allow Characters to start quests
                let Target::Obj(target) = &args.target else {
                    tracing::debug!("No target character given");
                    return true;
                };
                let Some(character) = target.as_player() else {
                    tracing::debug!("No target character given");
                    return true;
                };
                // `item.Template.ImplId == ItemImplEnum.AcceptQuest`: item templates do not
                // carry the impl id yet; a quest starter item is treated as one.
                let item = match args.caster_caster {
                    SkillCaster::Item {
                        item_template_id, ..
                    } => Some((
                        item_template_id,
                        server
                            .game_data
                            .systems
                            .quests
                            .quest_id_from_starter_item(item_template_id)
                            > 0,
                    )),
                    _ => None,
                };
                super::events::accept_quest_effect(server, character, *quest_id, item);
                true
            }
            EffectTemplate::Cinema { .. } => {
                // `CinemalEffect.Apply` only traces.
                tracing::trace!("CinemalEffect");
                true
            }
            _ => false,
        }
    }
}

/// The quest subsystem's [`SphereQuest`] for the npcs subsystem's copy of the same area.
fn quest_sphere(s: &NpcSphereQuest) -> SphereQuest {
    SphereQuest {
        zone_id: s.zone_id,
        world_id: s.world_id.clone(),
        quest_id: s.quest_id,
        component_id: s.component_id,
        radius: s.radius,
        xyz: (s.xyz[0], s.xyz[1], s.xyz[2]),
    }
}

/// `npcs::hooks::QuestsBridge` of the quest subsystem.
pub struct QuestsForNpcs;

impl QuestsBridge for QuestsForNpcs {
    fn has_quest(&self, _server: &GameServer, player: &PlayerRef, quest_id: u32) -> bool {
        super::events::has_quest(player, quest_id)
    }

    fn is_quest_completed(&self, _server: &GameServer, player: &PlayerRef, quest_id: u32) -> bool {
        super::events::has_quest_completed_in(&player.data(), quest_id)
    }

    fn start_quest(&self, server: &GameServer, player: &PlayerRef, quest_id: u32) -> bool {
        super::events::add_quest(server, player, quest_id, false) == Some(true)
    }

    fn on_interaction(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        _interaction: u32,
        target: &WorldObject,
    ) {
        if let Some(d) = target.as_doodad() {
            let template_id = d.data().template_id;
            super::events::on_doodad_interaction(server, player, player, template_id);
        }
    }

    fn on_npc_killed(&self, server: &GameServer, player: &PlayerRef, npc: &NpcRef) {
        super::events::on_npc_killed(server, player, npc);
    }

    fn add_exp(&self, server: &GameServer, player: &PlayerRef, exp: i32, _target_obj_id: u32) {
        crate::systems::skills::progress::add_exp(server, player, exp, true);
    }

    fn quest_component(
        &self,
        server: &GameServer,
        component_id: u32,
    ) -> Option<QuestComponentInfo> {
        let data = &server.game_data.systems.quests;
        let c = data.component(component_id)?;
        let accept_sphere = data.acts_in_component(component_id).iter().any(|&a| {
            matches!(
                data.acts[a].kind,
                super::templates::ActKind::ConAcceptSphere { .. }
            )
        });
        Some(QuestComponentInfo {
            quest_id: c.quest_id,
            accept_sphere,
        })
    }

    fn can_trigger_sphere(&self, server: &GameServer, sphere_id: u32, unit: &WorldObject) -> bool {
        let Some(p) = unit.as_player() else {
            return true;
        };
        let or_unit_reqs = server
            .game_data
            .systems
            .quests
            .spheres
            .get(&sphere_id)
            .is_some_and(|s| s.or_unit_reqs);
        super::events::with_quests(server, p, None, |ctx| {
            ctx.can_trigger_sphere(sphere_id, or_unit_reqs)
        })
        .unwrap_or(false)
    }

    fn on_enter_quest_starter_sphere(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        quest_template_id: u32,
        sphere_id: u32,
        _sphere: &NpcSphereQuest,
        _last_check: [f32; 3],
    ) {
        super::events::on_enter_quest_starter_sphere(server, player, quest_template_id, sphere_id);
    }

    fn on_enter_sphere(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        _quest_template_id: u32,
        sphere: &NpcSphereQuest,
        _last_check: [f32; 3],
    ) {
        super::events::on_enter_sphere(server, player, quest_sphere(sphere));
    }

    fn on_exit_sphere(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        _quest_template_id: u32,
        sphere: &NpcSphereQuest,
        _last_check: [f32; 3],
    ) {
        super::events::on_exit_sphere(server, player, quest_sphere(sphere));
    }

    fn do_report_events(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        quest_id: u32,
        npc_obj_id: u32,
        doodad_obj_id: u32,
        selective: i32,
    ) {
        super::events::on_report(
            server,
            player,
            quest_id,
            npc_obj_id,
            doodad_obj_id,
            selective,
        );
    }
}

/// `Npc.GetAggroRatingInPercent(objId)`: the position of `obj_id` in the NPC's aggro table
/// sorted by ascending total aggro, as `1 / count * position` (C#: despite the name a 0..1
/// fraction, and the *lowest* aggro comes first); 100 when not in the table.
pub fn aggro_rating_percent(npc: &NpcRef, obj_id: u32) -> f32 {
    let mut sorted: Vec<(u32, i32)> = npc
        .data()
        .unit
        .skill_state
        .aggro_table
        .iter()
        .map(|(id, a)| (*id, a.total()))
        .collect();
    sorted.sort_by_key(|&(id, total)| (total, id));
    match sorted.iter().position(|(id, _)| *id == obj_id) {
        Some(pos) => 1.0 / sorted.len() as f32 * pos as f32,
        None => 100.0,
    }
}

/// The [`QuestHooks`] of the running server. The item / money / mail methods forward to
/// `server.systems.quests.economy_hooks` (the economy subsystem installs its `QuestHooks`
/// there; the trait defaults until then).
pub struct GameQuestHooks;

fn economy(server: &GameServer) -> Arc<dyn QuestHooks> {
    server
        .systems
        .quests
        .economy_hooks
        .get()
        .unwrap_or_else(|| Arc::new(DefaultQuestHooks))
}

impl QuestHooks for GameQuestHooks {
    fn add_exp(
        &self,
        server: &GameServer,
        player: Option<&PlayerRef>,
        c: &mut Character,
        exp: i32,
        should_add_ability_exp: bool,
    ) {
        use crate::systems::skills::progress;
        let Some(gain) = progress::add_exp_locked(server, c, exp, should_add_ability_exp) else {
            return;
        };
        if let Some(p) = player {
            progress::send_exp_changed(p, &gain);
            let p = p.clone();
            super::events::after_unlock(server, c.id, move |s| {
                progress::add_exp_world_tail(s, &p, &gain)
            });
        }
    }

    fn change_money(
        &self,
        server: &GameServer,
        player: Option<&PlayerRef>,
        c: &mut Character,
        amount: i32,
    ) {
        economy(server).change_money(server, player, c, amount);
    }

    fn change_game_points(
        &self,
        _server: &GameServer,
        player: Option<&PlayerRef>,
        c: &mut Character,
        kind: u8,
        amount: i32,
    ) {
        let packet = crate::systems::skills::progress::change_game_points_locked(c, kind, amount);
        if let (Some(p), Some(packet)) = (player, packet) {
            p.send(packet);
        }
    }

    fn change_labor(
        &self,
        server: &GameServer,
        player: Option<&PlayerRef>,
        c: &mut Character,
        amount: i16,
        actability_id: i32,
    ) {
        use crate::systems::skills::progress;
        let Some((packet, gain)) = progress::change_labor_locked(server, c, amount, actability_id)
        else {
            return;
        };
        let Some(p) = player else {
            return;
        };
        if let Some(gain) = gain {
            progress::send_exp_changed(p, &gain);
            let pc = p.clone();
            super::events::after_unlock(server, c.id, move |s| {
                progress::add_exp_world_tail(s, &pc, &gain)
            });
        }
        p.send(packet);
    }

    fn add_crime(
        &self,
        server: &GameServer,
        player: Option<&PlayerRef>,
        c: &mut Character,
        points: i16,
    ) {
        let packet = crate::systems::social::crime::add_crime(
            server,
            c,
            points,
            chrono::Utc::now().timestamp_millis(),
        );
        if let Some(p) = player {
            p.send(packet);
            // Wanted / crime buffs added by the crime point setter.
            let p = p.clone();
            super::events::after_unlock(server, c.id, move |s| {
                crate::systems::dispatch_buff_events(s, &WorldObject::from(&p))
            });
        }
    }

    fn acquire_default_item(
        &self,
        server: &GameServer,
        player: Option<&PlayerRef>,
        c: &mut Character,
        task: u8,
        template_id: u32,
        count: i32,
        grade_id: u8,
    ) -> bool {
        economy(server).acquire_default_item(server, player, c, task, template_id, count, grade_id)
    }

    fn try_equip_new_backpack(
        &self,
        server: &GameServer,
        player: Option<&PlayerRef>,
        c: &mut Character,
        task: u8,
        template_id: u32,
        count: i32,
        grade_id: u8,
    ) -> bool {
        economy(server).try_equip_new_backpack(
            server,
            player,
            c,
            task,
            template_id,
            count,
            grade_id,
        )
    }

    fn consume_item(
        &self,
        server: &GameServer,
        player: Option<&PlayerRef>,
        c: &mut Character,
        task: u8,
        template_id: u32,
        count: i32,
        preferred_item_id: Option<u64>,
    ) -> i32 {
        economy(server).consume_item(
            server,
            player,
            c,
            task,
            template_id,
            count,
            preferred_item_id,
        )
    }

    fn send_quest_reward_mails(
        &self,
        server: &GameServer,
        player: Option<&PlayerRef>,
        c: &mut Character,
        quest_template_id: u32,
        items: &[ItemCreationDefinition],
        copper: i32,
    ) -> usize {
        economy(server).send_quest_reward_mails(server, player, c, quest_template_id, items, copper)
    }

    fn is_auto_equip_trade_pack(&self, server: &GameServer, template_id: u32) -> bool {
        economy(server).is_auto_equip_trade_pack(server, template_id)
    }

    fn can_replace_glider_in_backpack_slot(&self, server: &GameServer, c: &Character) -> bool {
        economy(server).can_replace_glider_in_backpack_slot(server, c)
    }

    fn summon_mate_level(&self, server: &GameServer, item: &Item) -> Option<u8> {
        economy(server).summon_mate_level(server, item)
    }

    fn use_skill(
        &self,
        server: &GameServer,
        caster: &WorldObject,
        skill_id: u32,
        target: &WorldObject,
    ) {
        crate::systems::skills::skill::unit_use_skill(server, caster, skill_id, target);
    }

    fn set_npc_faction(&self, server: &GameServer, npc: &NpcRef, faction_id: u32) {
        crate::systems::skills::combat::set_faction(server, &WorldObject::from(npc), faction_id);
    }

    fn team_members(&self, server: &GameServer, player: &PlayerRef) -> Vec<PlayerRef> {
        server
            .systems
            .social
            .teams
            .team_by_obj_id(player.obj_id)
            .map(|t| {
                t.members
                    .iter()
                    .flatten()
                    .map(|m| m.character.clone())
                    .collect()
            })
            .unwrap_or_default()
    }

    fn npc_aggro_rating_percent(&self, _server: &GameServer, npc: &NpcRef, obj_id: u32) -> f32 {
        aggro_rating_percent(npc, obj_id)
    }

    fn daily_account_login(&self, server: &GameServer, account_id: u32) {
        // `TimedRewardsManager` (services) is not ported.
        economy(server).daily_account_login(server, account_id);
    }
}

// ---------------------------------------------------------------------------------------------
// Economy hooks (item quest events, loot / cash shop quest checks)
// ---------------------------------------------------------------------------------------------

/// The quest subsystem's implementations of the economy hooks: `QuestItemHooks`
/// (`Inventory.OnAcquiredItem` / `OnConsumedItem` / `OnItemManuallyDestroyed`,
/// `Character.ItemUse`), `LootQuestHooks` and `CashShopQuestHooks`.
pub struct QuestsForEconomy;

/// Installs the quest subsystem's implementations of the economy hooks.
pub fn install_economy(server: &GameServer) {
    let e = &server.systems.economy;
    let me = Arc::new(QuestsForEconomy);
    e.hooks.quests.install(me.clone());
    e.loot.hooks.quests.install(me.clone());
    e.cash_shop.quests.install(me);
}

impl crate::systems::economy::hooks::QuestItemHooks for QuestsForEconomy {
    fn on_items_acquired(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        template_id: u32,
        count: i32,
    ) {
        super::events::on_items_acquired(server, player, template_id, count);
    }

    fn on_items_consumed(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        template_id: u32,
        count: i32,
    ) {
        super::events::on_items_consumed(server, player, template_id, count);
    }

    fn on_quest_item_manually_destroyed(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        item: &Item,
        loot_quest_id: u32,
    ) {
        super::events::on_quest_item_manually_destroyed(
            server,
            player,
            item.template_id,
            loot_quest_id,
        );
    }

    fn on_item_use(&self, server: &GameServer, player: &PlayerRef, template_id: u32) {
        super::events::on_item_use(server, player, template_id);
    }
}

impl crate::systems::economy::looting::LootQuestHooks for QuestsForEconomy {
    fn has_quest(&self, _server: &GameServer, player: &PlayerRef, quest_id: u32) -> bool {
        super::events::has_quest(player, quest_id)
    }
}

impl crate::systems::economy::cash_shop::CashShopQuestHooks for QuestsForEconomy {
    fn has_quest_completed(&self, _server: &GameServer, player: &PlayerRef, quest_id: u32) -> bool {
        super::events::has_quest_completed_in(&player.data(), quest_id)
    }
}
