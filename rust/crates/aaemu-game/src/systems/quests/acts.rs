//! The `QuestActTemplate` subclasses' behaviour (`Models/Game/Quests/Acts/*`, `UnusedActs/*`):
//! `RunAct`, `InitializeAction` / `FinalizeAction`, `InitializeQuest` / `FinalizeQuest`,
//! `QuestCleanup` / `QuestDropped` and the event handlers, plus the quest event dispatcher
//! (`Owner.Events.OnX(..)` for the quest subscriptions).

use super::engine::{check_items, items_by_template, items_count, Ctx, Deferred, QResult, Throw};
use super::events::QuestEvent;
use super::model::{
    acceptor, game_point, item_task, kind, sphere_trigger, ItemCreationDefinition, ObjectiveStatus,
};
use super::packets::SCAppellationGainedPacket;
use super::state::{ActPath, EventKind, QuestHandle};
use super::templates::ActKind;
use crate::models::container::slot_type;

impl Ctx<'_> {
    fn act_at(&self, h: QuestHandle, p: ActPath) -> Option<usize> {
        self.st
            .quest(h)
            .and_then(|q| q.steps.get(p.step))
            .and_then(|s| s.components.get(p.component))
            .and_then(|c| c.acts.get(p.act))
            .map(|a| a.template)
    }

    fn component_template_of(&self, h: QuestHandle, p: ActPath) -> u32 {
        self.st
            .quest(h)
            .and_then(|q| q.steps.get(p.step))
            .and_then(|s| s.components.get(p.component))
            .map_or(0, |c| c.template_id)
    }

    fn log_throw(&self, r: QResult, what: &str) {
        if let Err(Throw(e)) = r {
            tracing::error!("{what}: {e} (Player {} ({}))", self.c.unit.name, self.c.id);
        }
    }

    fn item_group_count(&self, group: u32) -> i32 {
        let mut seen = std::collections::HashSet::new();
        self.data
            .group_items
            .members(group)
            .iter()
            .filter(|id| seen.insert(**id))
            .fold(0i32, |count, id| {
                count.saturating_add(items_count(self.c, *id).max(0))
            })
    }

    /// Consume the objective quantity across group members in the loaded table order.
    fn consume_item_group(&mut self, group: u32, mut left: i32) {
        let mut seen = std::collections::HashSet::new();
        let hooks = self.hooks.clone();
        let player = self.player.clone();
        for &item in self.data.group_items.members(group) {
            if left <= 0 {
                break;
            }
            if !seen.insert(item) {
                continue;
            }
            let count = left.min(items_count(self.c, item).max(0));
            if count > 0 {
                let removed = hooks.consume_item(
                    self.server,
                    player.as_ref(),
                    self.c,
                    item_task::QUEST_REMOVE_SUPPLIES,
                    item,
                    count,
                    None,
                );
                left = left.saturating_sub(removed.clamp(0, count));
            }
        }
    }

    /// `QuestActTemplate.RunAct(quest, questAct, currentObjectiveCount)`.
    pub(crate) fn act_run(&mut self, h: QuestHandle, p: ActPath, count: i32) -> bool {
        let data = self.data;
        let Some(a) = self.act_at(h, p) else {
            return false;
        };
        let at = &data.acts[a];
        let Some(q) = self.st.quest(h) else {
            return false;
        };
        let (acc_type, acc_id, template_id) = (q.acceptor_type, q.acceptor_id, q.template_id);
        let Some(t) = data.template(template_id) else {
            return false;
        };
        let hooks = self.hooks.clone();
        let player = self.player.clone();
        use ActKind::*;
        match &at.kind {
            CheckCompleteComponent { complete_component } => {
                tracing::debug!(
                    "QuestActCheckCompleteComponent({}).RunAct: Quest {template_id}, Complete Component {complete_component}",
                    at.detail_id
                );
                let target = self.st.quest(h).and_then(|q| {
                    q.steps[p.step]
                        .components
                        .iter()
                        .position(|c| c.template_id == *complete_component)
                });
                match target {
                    // Basically the Same as doing a RunAct on all acts of this component
                    Some(ci) => self.run_component(h, p.step, ci),
                    None => {
                        tracing::error!(
                            "QuestActCheckCompleteComponent({}).RunAct: Quest {template_id}, Complete Component {complete_component} NOT FOUND!",
                            at.detail_id
                        );
                        false
                    }
                }
            }
            CheckDistance {
                npc_id, distance, ..
            } => {
                // There is actually no quest left that still uses this
                let (px, py, pz) = self.env.position;
                let r2 = (*distance as f32) * (*distance as f32);
                self.env.nearby_npcs.iter().any(|&(tid, x, y, z)| {
                    let (dx, dy) = (x - px, y - py);
                    let in_around = dx * dx + dy * dy < r2;
                    let dz = z - pz;
                    let d3 = ((f64::from(dx)).powi(2)
                        + (f64::from(dy)).powi(2)
                        + (f64::from(dz)).powi(2))
                    .sqrt() as f32;
                    in_around && tid == *npc_id && d3 <= *distance as f32
                })
            }
            CheckGuard { npc_id } => {
                tracing::warn!(
                    "QuestActCheckGuard({}).RunAct: Quest {template_id}, NpcId {npc_id}",
                    at.detail_id
                );
                true
            }
            CheckSphere { .. } => self
                .st
                .quest(h)
                .and_then(|q| q.steps.get(p.step))
                .and_then(|s| s.components.get(p.component))
                .and_then(|c| c.acts.get(p.act))
                .is_some_and(|a| a.sphere_inside),
            CheckTimer { .. } => true,
            ConAcceptBuff { buff_id } => acc_type == acceptor::BUFF && acc_id == *buff_id,
            ConAcceptComponent { .. } => true,
            ConAcceptDoodad { doodad_id } => acc_type == acceptor::DOODAD && acc_id == *doodad_id,
            ConAcceptItemEquip { item_id } => acc_type == acceptor::ITEM && acc_id == *item_id,
            ConAcceptItemGain { item_id } => {
                acc_type == acceptor::ITEM
                    && acc_id == *item_id
                    && check_items(self.c, *item_id, at.count)
            }
            ConAcceptItem { item_id, .. } => {
                acc_type == acceptor::ITEM && acc_id == *item_id && check_items(self.c, *item_id, 1)
            }
            ConAcceptLevelUp { level } => self.c.unit.level >= *level,
            ConAcceptNpcEmotion { npc_id, .. }
            | ConAcceptNpcKill { npc_id }
            | ConAcceptNpc { npc_id } => acc_type == acceptor::NPC && acc_id == *npc_id,
            ConAcceptSkill { skill_id } => acc_type == acceptor::SKILL && acc_id == *skill_id,
            ConAcceptSphere { sphere_id } => acc_type == acceptor::SPHERE && acc_id == *sphere_id,
            ConAutoComplete => true,
            ConReportDoodad { .. } => self.act_override(h, p),
            ConReportJournal => true,
            ConReportNpc { .. } => self.act_override(h, p),
            EtcItemObtain { .. } => true,
            ObjAbilityLevel {
                ability_id, level, ..
            } => {
                let ab_level = |id: u8| {
                    self.c
                        .abilities
                        .abilities
                        .iter()
                        .find(|a| a.id == id)
                        .map(|a| {
                            self.server
                                .game_data
                                .experience
                                .level_from_exp_any(a.exp, false)
                                .map_or(0, |(l, _)| l)
                        })
                };
                let ready = if *ability_id > 0 {
                    // Single Ability check (a missing key throws KeyNotFoundException)
                    ab_level(*ability_id).is_some_and(|l| l >= *level)
                } else {
                    // All abilities check
                    (1..11u8).all(|i| ab_level(i).is_some_and(|l| l >= *level))
                };
                let r = self.set_objective(h, a, i32::from(ready));
                self.log_throw(r, "QuestActObjAbilityLevel.RunAct");
                ready
            }
            ObjAggro { .. } => count > 0,
            ObjCinema { .. } => count >= 1,
            ObjCompleteQuest { quest_id, .. } => {
                if count <= 0 && self.has_quest_completed(*quest_id) {
                    let r = self.set_objective(h, a, 1);
                    self.log_throw(r, "QuestActObjCompleteQuest.RunAct");
                }
                self.get_objective(h, a) > 0
            }
            ObjCraft { .. } => {
                if t.score > 0 {
                    count.wrapping_mul(at.count) > t.score
                } else {
                    count >= at.count
                }
            }
            ObjDistance {
                within,
                npc_id,
                distance,
                ..
            } => {
                let res = self.distance_objective(*within, *npc_id, *distance);
                let r = self.set_objective(h, a, res);
                self.log_throw(r, "QuestActObjDistance.RunAct");
                res > 0
            }
            ObjExpressFire { .. }
            | ObjInteraction { .. }
            | ObjItemUse { .. }
            | ObjItemGroupUse { .. }
            | ObjMonsterGroupHunt { .. }
            | ObjMonsterHunt { .. } => count >= at.count,
            ObjItemGather { item_id, .. } => {
                let n = items_count(self.c, *item_id);
                let r = self.set_objective(h, a, n);
                self.log_throw(r, "QuestActObjItemGather.RunAct");
                self.get_objective(h, a) >= at.count
            }
            ObjItemGroupGather { item_group_id, .. } => {
                let n = self.item_group_count(*item_group_id);
                let r = self.set_objective(h, a, n);
                self.log_throw(r, "QuestActObjItemGroupGather.RunAct");
                self.get_objective(h, a) >= at.count
            }
            ObjLevel { level, .. } => {
                let res = self.c.unit.level >= *level;
                let r = self.set_objective(h, a, i32::from(res));
                self.log_throw(r, "QuestActObjLevel.RunAct");
                res
            }
            ObjMateLevel { .. } => self.mate_level_objective(h, a) > 0,
            ObjSphere { .. } | ObjTalkNpcGroup { .. } | ObjTalk { .. } => count > 0,
            ObjZoneKill {
                count_player_kill,
                count_npc,
                ..
            } => {
                (*count_npc > 0 && count >= *count_npc)
                    || (*count_player_kill > 0 && count >= *count_player_kill)
            }
            ObjZoneMonsterHunt { .. } => {
                if t.score > 0 {
                    count.wrapping_mul(at.count) >= t.score
                } else {
                    count > at.count
                }
            }
            SupplyAppellation { appellation_id } => {
                // CharacterAppellations.Add
                if self.c.appellations.appellations.contains(appellation_id) {
                    tracing::warn!("Duplicate add {appellation_id}, ownerId {}", self.c.id);
                } else {
                    self.c.appellations.appellations.push(*appellation_id);
                    self.send(SCAppellationGainedPacket {
                        appellation_id: *appellation_id,
                    });
                }
                true
            }
            SupplyCopper { amount } => {
                if let Some(q) = self.st.quest_mut(h) {
                    q.reward_coins = q.reward_coins.wrapping_add(*amount);
                }
                true
            }
            SupplyCrimePoint { point } => {
                hooks.add_crime(self.server, player.as_ref(), self.c, *point);
                true
            }
            SupplyExp { exp } => {
                hooks.add_exp(self.server, player.as_ref(), self.c, *exp, true);
                true
            }
            SupplyHonorPoint { point } => {
                hooks.change_game_points(
                    self.server,
                    player.as_ref(),
                    self.c,
                    game_point::HONOR,
                    *point,
                );
                true
            }
            SupplyItem {
                item_id, grade_id, ..
            } => {
                let mut to_add = at.count;
                if at.component_kind < kind::REWARD {
                    let (found, units) = items_by_template(self.c, &[], *item_id);
                    if found {
                        to_add = to_add.wrapping_sub(units);
                    }
                }
                if to_add <= 0 {
                    return true;
                }
                // If a backpack, directly handle it, otherwise use the reward pool
                if hooks.is_auto_equip_trade_pack(self.server, *item_id) {
                    return hooks.try_equip_new_backpack(
                        self.server,
                        player.as_ref(),
                        self.c,
                        item_task::QUEST_SUPPLY_ITEMS,
                        *item_id,
                        to_add,
                        *grade_id,
                    );
                }
                if let Some(q) = self.st.quest_mut(h) {
                    q.reward_items.push(ItemCreationDefinition {
                        template_id: *item_id,
                        count: to_add,
                        grade_id: *grade_id,
                    });
                }
                true
            }
            SupplyJuryPoint { point } => {
                self.c.jury_point = self.c.jury_point.wrapping_add(*point);
                true
            }
            SupplyLivingPoint { point } => {
                hooks.change_game_points(
                    self.server,
                    player.as_ref(),
                    self.c,
                    game_point::VOCATION,
                    *point,
                );
                true
            }
            SupplyLp { labor_power } => {
                hooks.change_labor(self.server, player.as_ref(), self.c, *labor_power as i16, 0);
                true
            }
            SupplyRemoveItem { item_id } => {
                let (_, units) = items_by_template(self.c, &[slot_type::INVENTORY], *item_id);
                let to_remove = units.min(at.count);
                let removed = hooks.consume_item(
                    self.server,
                    player.as_ref(),
                    self.c,
                    item_task::QUEST_REMOVE_SUPPLIES,
                    *item_id,
                    to_remove,
                    None,
                );
                if removed < at.count {
                    tracing::debug!(
                        "QuestActSupplyRemoveItem({}).RunAct: Did not have enough items to remove Quest: {template_id}, ItemId {item_id}, Count {removed}/{to_remove}(of {}) (found {units})",
                        at.detail_id,
                        at.count
                    );
                }
                true
            }
            SupplySelectiveItem { item_id, grade_id } => {
                let selected = self.st.quest(h).map_or(0, |q| q.selected_reward_index);
                if selected == at.selective_index {
                    if let Some(q) = self.st.quest_mut(h) {
                        q.reward_items.push(ItemCreationDefinition {
                            template_id: *item_id,
                            count: at.count,
                            grade_id: *grade_id,
                        });
                    }
                }
                true
            }
            // The unused acts call `base.RunAct`: "not implemented"
            ConFail { .. }
            | ObjCondition { .. }
            | ObjDoodadPhaseCheck { .. }
            | ObjEffectFire { .. }
            | ObjSendMail { .. }
            | ObjZoneNpcTalk { .. }
            | ObjZoneQuestComplete { .. }
            | SupplyAaPoint { .. }
            | SupplyInteraction { .. }
            | SupplySkill { .. } => {
                tracing::error!(
                    "{}({}).RunAct, Quest {template_id}, Owner: {}, not implemented!",
                    at.kind.class_name(),
                    at.detail_id,
                    self.c.unit.name
                );
                false
            }
        }
    }

    /// `QuestActObjDistance.CalculateObjective` (`GetAround<Npc>(owner, Distance, true)`; model
    /// sizes are 0: no model data).
    fn distance_objective(&self, within: bool, npc_id: u32, distance: i32) -> i32 {
        if distance <= 0 {
            return if within { 0 } else { 1 };
        }
        let (px, py, _) = self.env.position;
        let r2 = (distance as f32) * (distance as f32);
        let found = self.env.nearby_npcs.iter().any(|&(tid, x, y, _)| {
            let dx = (x - px) * (x - px);
            if dx > r2 {
                return false;
            }
            let dy = (y - py) * (y - py);
            dx + dy < r2 && tid == npc_id
        });
        if within {
            i32::from(found)
        } else {
            i32::from(!found)
        }
    }

    /// `QuestActObjMateLevel.CalculateObjective`.
    fn mate_level_objective(&mut self, h: QuestHandle, a: usize) -> u8 {
        let ActKind::ObjMateLevel {
            item_id,
            level,
            cleanup,
            ..
        } = self.data.acts[a].kind
        else {
            return 0;
        };
        let mut items = Vec::new();
        for st in [slot_type::INVENTORY, slot_type::EQUIPMENT, slot_type::BANK] {
            if let Some(cont) = self.c.inventory.get(st) {
                let g = cont.lock().expect("container poisoned");
                items.extend(g.items.iter().filter(|i| i.template_id == item_id).cloned());
            }
        }
        if items.is_empty() {
            let r = self.set_objective(h, a, 0);
            self.log_throw(r, "QuestActObjMateLevel");
            return 0;
        }
        let hooks = self.hooks.clone();
        let player = self.player.clone();
        for item in items {
            let Some(detail_level) = hooks.summon_mate_level(self.server, &item) else {
                continue;
            };
            if detail_level >= level {
                let r = self.set_objective(h, a, 1);
                self.log_throw(r, "QuestActObjMateLevel");
                if cleanup {
                    // Delete the mate if objective met
                    let removed = hooks.consume_item(
                        self.server,
                        player.as_ref(),
                        self.c,
                        item_task::QUEST_REMOVE_SUPPLIES,
                        item.template_id,
                        1,
                        Some(item.id),
                    );
                    if removed < 1 {
                        tracing::warn!(
                            "QuestActObjMateLevel.CalculateObjective: failed to remove SummonMate item {}",
                            item.id
                        );
                    }
                }
                return detail_level;
            }
        }
        let r = self.set_objective(h, a, 0);
        self.log_throw(r, "QuestActObjMateLevel");
        0
    }

    /// `InitializeAction(quest, questAct)` (step activated).
    pub(crate) fn act_initialize_action(&mut self, h: QuestHandle, p: ActPath) {
        let data = self.data;
        let Some(a) = self.act_at(h, p) else {
            return;
        };
        let at = &data.acts[a];
        tracing::info!(
            "{}.InitializeAction({}) Owner {} ({})",
            at.kind.class_name(),
            at.detail_id,
            self.c.unit.name,
            self.c.id
        );
        use ActKind::*;
        match &at.kind {
            CheckGuard { .. } => self.st.subscribe(EventKind::GuardNpcDied, h, p),
            CheckSphere { .. } => {
                let component_id = at.component_id;
                self.add_sphere_triggers(h, component_id, 0);
                self.st.subscribe(EventKind::EnterSphere, h, p);
                self.st.subscribe(EventKind::ExitSphere, h, p);
            }
            CheckTimer { limit_time, .. } => {
                if !self.add_quest_timer(h, *limit_time) {
                    tracing::warn!(
                        "QuestActCheckTimer({}).InitializeAction Timer Already running, Quest {}, Owner {} ({})",
                        at.detail_id,
                        self.template_id_of(h),
                        self.c.unit.name,
                        self.c.id
                    );
                } else {
                    self.st.subscribe(EventKind::TimerExpired, h, p);
                }
                self.st.subscribe(EventKind::QuestStepChanged, h, p);
            }
            ConReportJournal => self.st.subscribe(EventKind::ReportJournal, h, p),
            EtcItemObtain { .. } => self.st.subscribe(EventKind::ItemGather, h, p),
            ObjAggro { .. } => self.st.subscribe(EventKind::Kill, h, p),
            ObjAbilityLevel { .. } => self.st.subscribe(EventKind::AbilityLevelUp, h, p),
            ObjCompleteQuest { .. } => self.st.subscribe(EventKind::QuestComplete, h, p),
            ObjCinema { .. } => {
                self.st.subscribe(EventKind::CinemaStarted, h, p);
                self.st.subscribe(EventKind::CinemaEnded, h, p);
            }
            ObjCraft { .. } => self.st.subscribe(EventKind::Craft, h, p),
            ObjExpressFire { .. } => self.st.subscribe(EventKind::ExpressFire, h, p),
            ObjInteraction { .. } => self.st.subscribe(EventKind::Interaction, h, p),
            ObjItemGroupGather { item_group_id, .. } => {
                let n = self.item_group_count(*item_group_id);
                let r = self.set_objective(h, a, n);
                self.log_throw(r, "QuestActObjItemGroupGather.InitializeAction");
                self.st.subscribe(EventKind::ItemGroupGather, h, p);
            }
            ObjItemGather { item_id, .. } => {
                let n = items_count(self.c, *item_id);
                let r = self.set_objective(h, a, n);
                self.log_throw(r, "QuestActObjItemGather.InitializeAction");
                self.st.subscribe(EventKind::ItemGather, h, p);
            }
            ObjItemUse { .. } | ObjItemGroupUse { .. } => {
                self.st.subscribe(EventKind::ItemUse, h, p)
            }
            ObjLevel { .. } => self.st.subscribe(EventKind::LevelUp, h, p),
            ObjMateLevel { .. } => self.st.subscribe(EventKind::MateLevelUp, h, p),
            ObjMonsterGroupHunt { .. } => self.st.subscribe(EventKind::MonsterGroupHunt, h, p),
            ObjMonsterHunt { .. } => self.st.subscribe(EventKind::MonsterHunt, h, p),
            ObjSphere { npc_id, .. } => {
                self.st.subscribe(EventKind::EnterSphere, h, p);
                self.st.subscribe(EventKind::ExitSphere, h, p);
                let component_id = self.component_template_of(h, p);
                self.add_sphere_triggers(h, component_id, *npc_id);
            }
            ObjTalkNpcGroup { .. } => self.st.subscribe(EventKind::TalkNpcGroupMade, h, p),
            ObjTalk { .. } => self.st.subscribe(EventKind::TalkMade, h, p),
            ObjZoneKill { .. } | ObjZoneMonsterHunt { .. } => {
                self.st.subscribe(EventKind::ZoneKill, h, p)
            }
            _ => {}
        }
    }

    /// `FinalizeAction(quest, questAct)` (step deactivated).
    pub(crate) fn act_finalize_action(&mut self, h: QuestHandle, p: ActPath) {
        let data = self.data;
        let Some(a) = self.act_at(h, p) else {
            return;
        };
        let at = &data.acts[a];
        use ActKind::*;
        match &at.kind {
            CheckGuard { .. } => self.st.unsubscribe(EventKind::GuardNpcDied, h, p),
            CheckSphere { .. } => {
                let template_id = self.template_id_of(h);
                self.server
                    .systems
                    .quests
                    .spheres
                    .remove_triggers(self.c.id, template_id);
                self.st.unsubscribe(EventKind::EnterSphere, h, p);
                self.st.unsubscribe(EventKind::ExitSphere, h, p);
            }
            // CheckTimer: the timer persists as long as the quest is open
            ConReportJournal => self.st.unsubscribe(EventKind::ReportJournal, h, p),
            EtcItemObtain { .. } | ObjItemGather { .. } => {
                self.st.unsubscribe(EventKind::ItemGather, h, p)
            }
            ObjItemGroupGather { .. } => self.st.unsubscribe(EventKind::ItemGroupGather, h, p),
            ObjAggro { .. } => self.st.unsubscribe(EventKind::Kill, h, p),
            ObjAbilityLevel { .. } => self.st.unsubscribe(EventKind::AbilityLevelUp, h, p),
            ObjCompleteQuest { .. } => self.st.unsubscribe(EventKind::QuestComplete, h, p),
            ObjCinema { .. } => {
                self.st.unsubscribe(EventKind::CinemaEnded, h, p);
                self.st.unsubscribe(EventKind::CinemaStarted, h, p);
            }
            ObjCraft { .. } => self.st.unsubscribe(EventKind::Craft, h, p),
            ObjExpressFire { .. } => self.st.unsubscribe(EventKind::ExpressFire, h, p),
            ObjInteraction { .. } => self.st.unsubscribe(EventKind::Interaction, h, p),
            ObjItemUse { .. } | ObjItemGroupUse { .. } => {
                self.st.unsubscribe(EventKind::ItemUse, h, p)
            }
            ObjLevel { .. } => self.st.unsubscribe(EventKind::LevelUp, h, p),
            ObjMateLevel { .. } => self.st.unsubscribe(EventKind::MateLevelUp, h, p),
            ObjMonsterGroupHunt { .. } => self.st.unsubscribe(EventKind::MonsterGroupHunt, h, p),
            ObjMonsterHunt { .. } => self.st.unsubscribe(EventKind::MonsterHunt, h, p),
            ObjSphere { .. } => {
                let template_id = self.template_id_of(h);
                self.server
                    .systems
                    .quests
                    .spheres
                    .remove_triggers(self.c.id, template_id);
                self.st.unsubscribe(EventKind::ExitSphere, h, p);
                self.st.unsubscribe(EventKind::EnterSphere, h, p);
            }
            ObjTalkNpcGroup { .. } => self.st.unsubscribe(EventKind::TalkNpcGroupMade, h, p),
            ObjTalk { .. } => self.st.unsubscribe(EventKind::TalkMade, h, p),
            ObjZoneKill { .. } | ObjZoneMonsterHunt { .. } => {
                self.st.unsubscribe(EventKind::ZoneKill, h, p)
            }
            _ => {}
        }
        tracing::info!(
            "{}.FinalizeAction({}) Owner {} ({})",
            at.kind.class_name(),
            at.detail_id,
            self.c.unit.name,
            self.c.id
        );
    }

    /// `InitializeQuest(quest, questAct)` (quest object created).
    pub(crate) fn act_initialize_quest(&mut self, h: QuestHandle, p: ActPath) {
        let Some(a) = self.act_at(h, p) else {
            return;
        };
        match self.data.acts[a].kind {
            ActKind::ConReportDoodad { .. } => self.st.subscribe(EventKind::ReportDoodad, h, p),
            ActKind::ConReportNpc { .. } => self.st.subscribe(EventKind::ReportNpc, h, p),
            _ => {}
        }
    }

    /// `FinalizeQuest(quest, questAct)` (quest removed).
    pub(crate) fn act_finalize_quest(&mut self, h: QuestHandle, p: ActPath) {
        let Some(a) = self.act_at(h, p) else {
            return;
        };
        match self.data.acts[a].kind {
            ActKind::ConReportDoodad { .. } => self.st.unsubscribe(EventKind::ReportDoodad, h, p),
            ActKind::ConReportNpc { .. } => self.st.unsubscribe(EventKind::ReportNpc, h, p),
            _ => {}
        }
    }

    /// `QuestCleanup(quest)` (template-level: called for every act of the quest template).
    pub(crate) fn act_quest_cleanup(&mut self, h: QuestHandle, a: usize) {
        let data = self.data;
        let at = &data.acts[a];
        let hooks = self.hooks.clone();
        let player = self.player.clone();
        let consume = |ctx: &mut Self, item: u32, count: i32| {
            hooks.consume_item(
                ctx.server,
                player.as_ref(),
                ctx.c,
                item_task::QUEST_REMOVE_SUPPLIES,
                item,
                count,
                None,
            );
        };
        match &at.kind {
            ActKind::ConAcceptItem {
                item_id, cleanup, ..
            } if *cleanup => consume(self, *item_id, at.count),
            ActKind::EtcItemObtain {
                item_id, cleanup, ..
            } if *cleanup => consume(self, *item_id, at.count),
            ActKind::ObjItemGather {
                item_id, cleanup, ..
            } if *cleanup => {
                let n = self.cleanup_count(h, a);
                consume(self, *item_id, n);
            }
            ActKind::ObjItemGroupGather {
                item_group_id,
                cleanup,
                ..
            } if *cleanup => {
                let n = self.cleanup_count(h, a);
                self.consume_item_group(*item_group_id, n);
            }
            ActKind::SupplyItem {
                item_id, cleanup, ..
            } if *cleanup && at.component_kind != kind::REWARD => consume(self, *item_id, at.count),
            _ => {}
        }
    }

    /// `Math.Min(GetObjective(quest), MaxObjective())`.
    fn cleanup_count(&self, h: QuestHandle, a: usize) -> i32 {
        let data = self.data;
        let max = self
            .st
            .quest(h)
            .and_then(|q| data.template(q.template_id))
            .and_then(|t| data.acts[a].max_objective(t))
            .unwrap_or(0);
        self.get_objective(h, a).min(max)
    }

    /// `QuestDropped(quest)`.
    pub(crate) fn act_quest_dropped(&mut self, h: QuestHandle, a: usize) {
        let data = self.data;
        let at = &data.acts[a];
        let hooks = self.hooks.clone();
        let player = self.player.clone();
        let (item, count) = match &at.kind {
            ActKind::ConAcceptItem {
                item_id,
                destroy_when_drop,
                ..
            } if *destroy_when_drop => (*item_id, at.count),
            ActKind::ObjItemGather {
                item_id,
                destroy_when_drop,
                ..
            } if *destroy_when_drop => (*item_id, self.cleanup_count(h, a)),
            ActKind::ObjItemGroupGather {
                item_group_id,
                destroy_when_drop,
                ..
            } if *destroy_when_drop => {
                self.consume_item_group(*item_group_id, self.cleanup_count(h, a));
                return;
            }
            ActKind::SupplyItem {
                item_id,
                destroy_when_drop,
                ..
            } if *destroy_when_drop && at.component_kind != kind::REWARD => (*item_id, at.count),
            _ => return,
        };
        hooks.consume_item(
            self.server,
            player.as_ref(),
            self.c,
            item_task::QUEST_REMOVE_SUPPLIES,
            item,
            count,
            None,
        );
    }

    /// `SphereQuestManager.AddSphereQuestTriggers(owner, quest, componentId, npcTemplateId)`.
    fn add_sphere_triggers(&mut self, h: QuestHandle, component_id: u32, npc_template: u32) {
        let template_id = self.template_id_of(h);
        self.server.systems.quests.spheres.add_triggers(
            self.c.id,
            template_id,
            component_id,
            npc_template,
        );
    }

    // ----- events -----

    /// `Owner.Events.OnX(sender, args)`: runs the quest subscribers of the event in
    /// subscription order; a throwing handler aborts the rest (`Err`).
    pub fn dispatch(&mut self, ev: &QuestEvent) -> QResult {
        let subs = self.st.subscribers(ev.kind());
        for s in subs {
            if self.st.quest(s.quest).is_none() {
                continue;
            }
            self.act_event(s.quest, s.path, ev)?;
        }
        Ok(())
    }

    /// The act's `OnX` handler.
    fn act_event(&mut self, h: QuestHandle, p: ActPath, ev: &QuestEvent) -> QResult {
        let data = self.data;
        let Some(a) = self.act_at(h, p) else {
            return Ok(());
        };
        let at = &data.acts[a];
        let quest_template = at.quest_id;
        use ActKind::*;
        match (&at.kind, ev) {
            (CheckGuard { npc_id }, QuestEvent::GuardNpcDied { npc_id: dead }) => {
                if dead == npc_id {
                    self.set_status(h, super::model::status::FAILED);
                    self.set_step(h, kind::FAIL);
                }
            }

            (
                ObjCompleteQuest { quest_id, .. },
                QuestEvent::QuestComplete {
                    quest_id: completed,
                },
            ) => {
                if quest_id == completed {
                    self.set_objective(h, a, 1)?;
                }
            }
            (ObjAbilityLevel { .. }, QuestEvent::AbilityLevelUp) => {
                self.act_run(h, p, self.get_objective(h, a));
            }
            (ObjMonsterHunt { npc_id, .. }, QuestEvent::MonsterHunt { npc_id: n, count }) => {
                if n != npc_id {
                    return Ok(());
                }
                self.add_objective(h, a, *count as i32)?;
            }
            (
                ObjMonsterGroupHunt {
                    quest_monster_group_id,
                    ..
                },
                QuestEvent::MonsterGroupHunt { group_id, count },
            ) => {
                // NpcId here is actually the group Id
                if quest_monster_group_id == group_id {
                    self.add_objective(h, a, *count as i32)?;
                }
            }
            (
                ObjTalk {
                    npc_id, team_share, ..
                },
                QuestEvent::TalkMade {
                    npc_id: n,
                    source_player_id,
                    ..
                },
            ) => {
                if n != npc_id {
                    return Ok(());
                }
                self.set_objective(h, a, 1)?;
                if self.c.id == *source_player_id && *team_share {
                    // Delegate also to other team members
                    self.deferred
                        .push(Deferred::TeamEvent { event: ev.clone() });
                }
            }
            (
                ObjTalkNpcGroup { npc_group_id, .. },
                QuestEvent::TalkNpcGroupMade {
                    npc_group_id: g, ..
                },
            ) => {
                if g != npc_group_id {
                    return Ok(());
                }
                self.set_objective(h, a, 1)?;
            }
            (EtcItemObtain { .. }, QuestEvent::ItemGather { .. }) => {
                // Disabled in C#: "there is no check for removing for EtcItemObtain"
            }
            (ObjItemGather { item_id, .. }, QuestEvent::ItemGather { item_id: i, .. }) => {
                if i != item_id {
                    return Ok(());
                }
                let n = items_count(self.c, *item_id);
                self.set_objective(h, a, n)?;
            }
            (
                ObjItemGroupGather { item_group_id, .. },
                QuestEvent::ItemGroupGather {
                    item_id, group_id, ..
                },
            ) => {
                if group_id != item_group_id || !data.group_items.contains(*item_group_id, *item_id)
                {
                    return Ok(());
                }
                self.set_objective(h, a, self.item_group_count(*item_group_id))?;
            }
            (ObjItemGroupUse { item_group_id, .. }, QuestEvent::ItemUse { item_id }) => {
                if !data.group_items.contains(*item_group_id, *item_id) {
                    return Ok(());
                }
                self.add_objective(h, a, 1)?;
            }
            (ObjItemUse { item_id, .. }, QuestEvent::ItemUse { item_id: i }) => {
                if i != item_id {
                    return Ok(());
                }
                self.add_objective(h, a, 1)?;
            }
            (
                ObjInteraction {
                    doodad_id,
                    team_share,
                    ..
                },
                QuestEvent::Interaction {
                    doodad_id: d,
                    source_player_id,
                },
            ) => {
                if d != doodad_id {
                    return Ok(());
                }
                self.add_objective(h, a, 1)?;
                if self.c.id == *source_player_id && *team_share {
                    self.deferred
                        .push(Deferred::TeamEvent { event: ev.clone() });
                }
            }
            (
                ObjExpressFire {
                    express_key_id,
                    npc_group_id,
                    ..
                },
                QuestEvent::ExpressFire { npc_id, emotion_id },
            ) => {
                if emotion_id != express_key_id {
                    return Ok(());
                }
                if data.group_npcs.contains(*npc_group_id, *npc_id) {
                    self.add_objective(h, a, 1)?;
                }
            }
            (ObjLevel { level, .. }, QuestEvent::LevelUp) => {
                let res = self.c.unit.level >= *level;
                self.set_objective(h, a, i32::from(res))?;
            }
            (ObjMateLevel { .. }, QuestEvent::MateLevelUp) => {
                self.mate_level_objective(h, a);
            }
            (CheckSphere { .. }, QuestEvent::EnterSphere { sphere }) => {
                if sphere.quest_id != quest_template || sphere.component_id != at.component_id {
                    return Ok(());
                }
                self.set_sphere_presence(h, p, true);
            }
            (CheckSphere { .. }, QuestEvent::ExitSphere { sphere }) => {
                if sphere.quest_id != quest_template || sphere.component_id != at.component_id {
                    return Ok(());
                }
                self.set_sphere_presence(h, p, false);
            }
            (ObjSphere { .. }, QuestEvent::EnterSphere { sphere }) => {
                if sphere.component_id != self.component_template_of(h, p) {
                    return Ok(());
                }
                self.set_objective(h, a, 1)?;
            }
            (ObjSphere { sphere_id, .. }, QuestEvent::ExitSphere { sphere }) => {
                if sphere.component_id != self.component_template_of(h, p) {
                    return Ok(());
                }
                if data.spheres.get(sphere_id).map(|s| s.trigger_condition_id)
                    == Some(sphere_trigger::TRIGGER_EVERY_N_TIME_AFTER)
                {
                    return Ok(());
                }
                self.set_objective(h, a, 0)?;
            }
            (ObjCraft { craft_id, .. }, QuestEvent::Craft { craft_id: c }) => {
                if c == craft_id {
                    self.add_objective(h, a, 1)?;
                }
            }
            (
                ObjZoneKill {
                    zone_id,
                    count_player_kill,
                    count_npc,
                    team_share,
                    lvl_min,
                    lvl_max,
                    is_party,
                    lvl_min_npc,
                    lvl_max_npc,
                    pc_faction_id,
                    pc_faction_exclusive,
                    npc_faction_id,
                    npc_faction_exclusive,
                    ..
                },
                QuestEvent::ZoneKill {
                    zone_group_id,
                    killer,
                    victim,
                },
            ) => {
                if *zone_id != 0 && zone_id != zone_group_id {
                    return Ok(());
                }
                // If Party kills is not allowed, only allow kills from self
                if !*is_party && killer.character_id != Some(self.c.id) {
                    return Ok(());
                }
                // Ignore if victim is the killer (e.g. death from fall-damage)
                if killer.obj_id == victim.obj_id {
                    return Ok(());
                }
                let mut valid = false;
                if *count_npc > 0 && victim.npc_template_id.is_some() {
                    valid = *npc_faction_id == 0;
                    if *npc_faction_id > 0 {
                        if *npc_faction_exclusive && victim.faction_id != *npc_faction_id {
                            valid = true;
                        }
                        if !*npc_faction_exclusive && victim.faction_id == *npc_faction_id {
                            valid = true;
                        }
                    }
                    if i32::from(victim.level) < *lvl_min_npc
                        || i32::from(victim.level) > *lvl_max_npc
                    {
                        valid = false;
                    }
                }
                if *count_player_kill > 0 && victim.character_id.is_some() {
                    valid = *pc_faction_id == 0;
                    if *pc_faction_id > 0 {
                        if *pc_faction_exclusive && victim.faction_id != *pc_faction_id {
                            valid = true;
                        }
                        if !*pc_faction_exclusive && victim.faction_id == *pc_faction_id {
                            valid = true;
                        }
                    }
                    if i32::from(victim.level) < *lvl_min || i32::from(victim.level) > *lvl_max {
                        valid = false;
                    }
                }
                if valid {
                    self.add_objective(h, a, 1)?;
                    if *team_share {
                        self.deferred
                            .push(Deferred::TeamEvent { event: ev.clone() });
                    }
                }
            }
            (
                ObjZoneMonsterHunt { zone_id, .. },
                QuestEvent::ZoneKill {
                    zone_group_id,
                    victim,
                    ..
                },
            ) => {
                if zone_group_id != zone_id {
                    return Ok(());
                }
                if victim.npc_template_id.is_none() {
                    return Ok(());
                }
                self.add_objective(h, a, 1)?;
            }
            (ObjCinema { cinema_id, .. }, QuestEvent::CinemaStarted { .. }) => {
                self.st.currently_playing_cinema_id = *cinema_id;
            }
            (ObjCinema { cinema_id, .. }, QuestEvent::CinemaEnded { .. }) => {
                if self.st.currently_playing_cinema_id != *cinema_id {
                    return Ok(());
                }
                self.set_objective(h, a, 1)?;
                self.st.currently_playing_cinema_id = 0;
            }
            (
                ConReportNpc { npc_id, .. },
                QuestEvent::ReportNpc {
                    quest_id,
                    npc_id: n,
                    selected,
                },
            ) => {
                if *quest_id != quest_template || npc_id != n {
                    return Ok(());
                }
                if !self.report_ready(h, quest_template) {
                    return Ok(());
                }
                if let Some(q) = self.st.quest_mut(h) {
                    q.selected_reward_index = *selected;
                }
                self.set_act_override(h, p, true);
                if self.st.quest(h).is_some_and(|q| q.step <= kind::PROGRESS) {
                    self.set_step(h, kind::READY);
                }
                self.request_evaluation(h);
            }
            (
                ConReportDoodad { doodad_id, .. },
                QuestEvent::ReportDoodad {
                    quest_id,
                    doodad_id: d,
                    selected,
                },
            ) => {
                if *quest_id != quest_template || d != doodad_id {
                    return Ok(());
                }
                if !self.report_ready(h, quest_template) {
                    return Ok(());
                }
                if let Some(q) = self.st.quest_mut(h) {
                    q.selected_reward_index = *selected;
                }
                self.set_act_override(h, p, true);
                if self.st.quest(h).is_some_and(|q| q.step == kind::PROGRESS) {
                    self.set_step(h, kind::READY);
                }
                self.request_evaluation(h);
            }
            (ConReportJournal, QuestEvent::ReportJournal) => {
                self.set_act_override(h, p, true);
                if self.st.quest(h).is_some_and(|q| q.step == kind::PROGRESS) {
                    self.set_step(h, kind::READY);
                }
                self.request_evaluation(h);
            }
            (
                ObjAggro {
                    rank1,
                    rank1_ratio,
                    rank1_item,
                    rank2,
                    rank2_ratio,
                    rank2_item,
                    rank3,
                    rank3_ratio,
                    rank3_item,
                    ..
                },
                QuestEvent::Kill { target, victim, .. },
            ) => {
                let Some(q) = self.st.quest(h) else {
                    return Ok(());
                };
                let (acc_type, acc_id) = (q.acceptor_type, q.acceptor_id);
                let Some(t) = victim.as_ref().or(target.as_ref()) else {
                    return Ok(());
                };
                let Some(npc_template) = t.npc_template_id else {
                    return Ok(());
                };
                if acc_type != acceptor::NPC || npc_template != acc_id {
                    return Ok(());
                }
                let rate = t.aggro_percent;
                let set = |ctx: &mut Self, rank: i32, ratio: i32, item: bool| -> QResult {
                    ctx.set_objective(h, a, rank)?;
                    if let Some(q) = ctx.st.quest_mut(h) {
                        q.reward_ratio = f64::from(ratio) / 100.0;
                        q.allow_item_rewards = item;
                    }
                    Ok(())
                };
                if let Some(q) = self.st.quest_mut(h) {
                    q.allow_item_rewards = false;
                    q.reward_ratio = 0.0;
                }
                if rate <= *rank1 as f32 {
                    return set(self, 1, *rank1_ratio, *rank1_item);
                }
                if rate <= *rank2 as f32 {
                    return set(self, 2, *rank2_ratio, *rank2_item);
                }
                if rate <= *rank3 as f32 {
                    return set(self, 3, *rank3_ratio, *rank3_item);
                }
            }
            (CheckTimer { .. }, QuestEvent::TimerExpired { quest_id }) => {
                if *quest_id != quest_template {
                    return Ok(());
                }
                tracing::debug!(
                    "QuestActCheckTimer({}).OnTimerExpired Quest {quest_template}",
                    at.detail_id
                );
                self.fail_quest(self.template_id_of(h));
            }
            (CheckTimer { .. }, QuestEvent::QuestStepChanged { quest_id, step }) => {
                if *quest_id != quest_template {
                    return Ok(());
                }
                match *step {
                    kind::NONE | kind::START | kind::SUPPLY | kind::PROGRESS => {}
                    kind::FAIL | kind::READY | kind::DROP | kind::REWARD => {
                        self.st.unsubscribe(EventKind::QuestStepChanged, h, p);
                        self.st.unsubscribe(EventKind::TimerExpired, h, p);
                        let tid = self.template_id_of(h);
                        self.server
                            .systems
                            .quests
                            .remove_quest_timer(self.c.id, tid);
                    }
                    _ => {
                        return Err(Throw(
                            "ArgumentOutOfRangeException: Step option out of range".into(),
                        ))
                    }
                }
            }
            _ => {}
        }
        Ok(())
    }

    fn set_sphere_presence(&mut self, h: QuestHandle, p: ActPath, inside: bool) {
        let Some(act) = self
            .st
            .quest_mut(h)
            .and_then(|q| q.steps.get_mut(p.step))
            .and_then(|s| s.components.get_mut(p.component))
            .and_then(|c| c.acts.get_mut(p.act))
        else {
            return;
        };
        if act.sphere_inside != inside {
            act.sphere_inside = inside;
            self.request_evaluation(h);
        }
    }

    /// The "is the quest ready to be turned in" check of the report acts.
    fn report_ready(&self, h: QuestHandle, quest_template: u32) -> bool {
        let let_it_done = self
            .data
            .template(quest_template)
            .is_some_and(|t| t.let_it_done);
        let minimum = if let_it_done {
            ObjectiveStatus::CanEarlyComplete
        } else {
            ObjectiveStatus::QuestComplete
        };
        self.objective_status(h) >= minimum
    }
}
