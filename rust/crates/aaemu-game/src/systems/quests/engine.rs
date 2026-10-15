//! The quest state machine: `Quest` (+ `NewQuestCode`), `QuestStep`, `QuestComponent`,
//! `QuestAct`, `CharacterQuests` and the `QuestManager` helpers they call, run against one
//! locked character ([`Ctx`]). The per-act behaviour is in `acts.rs`.

use std::sync::Arc;

use super::data::QuestData;
use super::hooks::QuestHooks;
use super::model::{
    condition, detail, error_msg, item_task, kind, npc_ai, status, ObjectiveStatus,
    FACTION_MONSTROSITY,
};
use super::packets::{
    SCQuestContextCompletedPacket, SCQuestContextResetPacket, SCQuestContextStartedPacket,
    SCQuestContextUpdatedPacket, SCQuestRewardedByMailPacket,
};
use super::state::{
    ActPath, Quest, QuestActState, QuestComponentState, QuestHandle, QuestState, QuestStepState,
};
use super::templates::ActKind;
use super::unit_reqs::{ReqEnv, ReqSubject};
use crate::models::buff::{Buff, SkillCaster};
use crate::models::char_data::CompletedQuest;
use crate::models::character::Character;
use crate::models::container::slot_type;
use crate::net::packet::ServerPacket;
use crate::packets::world::SCErrorMsgPacket;
use crate::server::GameServer;
use crate::world::PlayerRef;

/// A C# exception thrown inside quest code (index out of range, division by zero, ...): it
/// aborts the current event invocation list / evaluation like the C# `throw` does.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Throw(pub String);

pub type QResult<T = ()> = Result<T, Throw>;

/// A snapshot of a unit taken before the character was locked (the current target).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct UnitSnapshot {
    pub obj_id: u32,
    /// `BaseUnitType` (0 = Character, 1 = Npc, ...).
    pub base_unit_type: u8,
    pub is_unit: bool,
    pub npc_template_id: Option<u32>,
    pub doodad_template_id: Option<u32>,
    pub level: u8,
    pub hpp: i32,
    pub in_battle: bool,
    pub under_water: bool,
    pub faction_id: u32,
    pub mother_faction_id: u32,
    pub buff_ids: Vec<u32>,
    pub zone_key: u32,
    pub position: (f32, f32, f32),
}

/// A unit taking part in a kill (`OnKillArgs` / `OnZoneKillArgs` members), captured when the
/// event is raised.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct KillUnitInfo {
    pub obj_id: u32,
    /// Set for player characters.
    pub character_id: Option<u32>,
    /// Set for NPCs.
    pub npc_template_id: Option<u32>,
    /// `Faction.Id`.
    pub faction_id: u32,
    pub level: u8,
    /// `Npc.GetAggroRatingInPercent(owner.ObjId)` for the event's owner (100 when not an NPC).
    pub aggro_percent: f32,
}

/// World facts an operation needs, captured before the character is locked.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct QuestEnv {
    /// `Owner.CurrentTarget` (None = no target / not found).
    pub target: Option<UnitSnapshot>,
    /// NPCs in the owner's neighbourhood: (template id, world x, y, z).
    pub nearby_npcs: Vec<(u32, f32, f32, f32)>,
    /// Owner world position.
    pub position: (f32, f32, f32),
    /// `TimeManager.GetTime`.
    pub time_of_day: f32,
}

impl QuestEnv {
    /// `Owner.CurrentTarget is Npc npc ? npc.TemplateId`.
    pub fn target_npc_template(&self) -> Option<u32> {
        self.target.as_ref().and_then(|t| t.npc_template_id)
    }
}

/// Work the operation leaves for after the character lock is released.
#[derive(Debug, Clone, PartialEq)]
pub enum Deferred {
    /// `Owner.UseSkill(skillId, Owner)`.
    UseSkillSelf {
        skill_id: u32,
    },
    /// `ParentWorld.GetNpcByTemplateId(npcId)?.UseSkill(skillId, npc)`.
    NpcUseSkill {
        npc_template_id: u32,
        skill_id: u32,
    },
    /// `ParentWorld.GetNpcByTemplateId(npcId)?.SetFaction(faction)`.
    NpcSetFaction {
        npc_template_id: u32,
        faction_id: u32,
    },
    NpcComponent {
        component_id: u32,
    },
    /// `teamMember.Character.Events.OnX(sender, args)` for team-shared acts.
    TeamEvent {
        event: super::events::QuestEvent,
    },
    /// `SendDebugMessage` / system chat line.
    SystemMessage {
        text: String,
    },
}

/// One quest operation on one locked character (`Quest.Owner` + `CharacterQuests`).
pub struct Ctx<'a> {
    pub server: &'a GameServer,
    pub data: &'a QuestData,
    pub hooks: Arc<dyn QuestHooks>,
    /// The player (packets, evaluation queue); `None` only in data-level tests.
    pub player: Option<PlayerRef>,
    pub c: &'a mut Character,
    pub st: &'a mut QuestState,
    pub env: QuestEnv,
    pub now_ms: i64,
    pub deferred: Vec<Deferred>,
    /// Buffs were added (dispatch the buff events once unlocked).
    pub buffs_changed: bool,
    /// Packets sent through [`Ctx::send`] when there is no player (tests).
    pub sent: Vec<(u16, Vec<u8>)>,
}

/// `C# Math.Round(double)` (banker's rounding).
fn round_even(v: f64) -> f64 {
    v.round_ties_even()
}

impl<'a> Ctx<'a> {
    // ----- plumbing -----

    pub fn send<P: ServerPacket + 'static>(&mut self, p: P) {
        match &self.player {
            Some(pl) => pl.send(p),
            None => {
                let mut s = aaemu_commons::PacketStream::new();
                p.write_body(&mut s);
                self.sent.push((p.opcode(), s.into_bytes()));
            }
        }
    }

    /// `Owner.SendErrorMessage(type)` (`type = 0, isNotify = true`).
    pub fn send_error(&mut self, error: i16) {
        self.send(SCErrorMsgPacket::new(error, 0, true));
    }

    /// `Owner.SendDebugMessage(message)`.
    pub fn send_debug_message(&mut self, text: String) {
        let cfg = &self.server.config;
        if cfg.debug_info && i64::from(self.c.access_level) >= i64::from(cfg.debug_info_level) {
            self.deferred.push(Deferred::SystemMessage { text });
        }
    }

    pub fn owner_id(&self) -> u32 {
        self.c.id
    }

    fn quest(&self, h: QuestHandle) -> Option<&Quest> {
        self.st.quests.get(&h)
    }

    fn quest_mut(&mut self, h: QuestHandle) -> Option<&mut Quest> {
        self.st.quests.get_mut(&h)
    }

    pub fn template_id_of(&self, h: QuestHandle) -> u32 {
        self.quest(h).map_or(0, |q| q.template_id)
    }

    fn act_ref(&self, h: QuestHandle, p: ActPath) -> Option<usize> {
        self.quest(h)
            .and_then(|q| q.steps.get(p.step))
            .and_then(|s| s.components.get(p.component))
            .and_then(|c| c.acts.get(p.act))
            .map(|a| a.template)
    }

    // ----- Quest (constructor, NewQuestCode) -----

    /// `new Quest(template, owner)`: `CreateQuestSteps` + `InitializeQuestActs`.
    pub fn new_quest(&mut self, template_id: u32, id: u32) -> QuestHandle {
        let data = self.data;
        let mut q = Quest::new_blank(id, template_id);
        if let Some(t) = data.template(template_id) {
            // CreateQuestSteps
            for cid in &t.components {
                let Some(ct) = data.component(*cid) else {
                    continue;
                };
                let si = match q.steps.iter().position(|s| s.kind == ct.kind_id) {
                    Some(i) => i,
                    None => {
                        q.steps.push(QuestStepState {
                            kind: ct.kind_id,
                            components: Vec::new(),
                        });
                        q.steps.len() - 1
                    }
                };
                q.steps[si].components.push(QuestComponentState {
                    template_id: *cid,
                    override_completed: false,
                    is_active: true,
                    acts: ct
                        .acts
                        .iter()
                        .map(|&a| QuestActState {
                            template: a,
                            override_completed: false,
                            sphere_inside: false,
                            supplied: false,
                        })
                        .collect(),
                });
            }
            if q.steps.is_empty() {
                tracing::warn!("Quest {template_id} does not seem to have any components!");
            }
        }
        let h = self.st.alloc(q);
        // InitializeQuestActs
        for p in self.all_paths(h) {
            self.act_initialize_quest(h, p);
        }
        h
    }

    /// Every act of a quest in `QuestSteps` / `Components` / `Acts` order.
    pub fn all_paths(&self, h: QuestHandle) -> Vec<ActPath> {
        let mut v = Vec::new();
        if let Some(q) = self.quest(h) {
            for (si, s) in q.steps.iter().enumerate() {
                for (ci, c) in s.components.iter().enumerate() {
                    for ai in 0..c.acts.len() {
                        v.push(ActPath {
                            step: si,
                            component: ci,
                            act: ai,
                        });
                    }
                }
            }
        }
        v
    }

    fn step_paths(&self, h: QuestHandle, si: usize) -> Vec<ActPath> {
        let mut v = Vec::new();
        if let Some(s) = self.quest(h).and_then(|q| q.steps.get(si)) {
            for (ci, c) in s.components.iter().enumerate() {
                for ai in 0..c.acts.len() {
                    v.push(ActPath {
                        step: si,
                        component: ci,
                        act: ai,
                    });
                }
            }
        }
        v
    }

    /// `FinalizeQuestActs`.
    pub fn finalize_quest_acts(&mut self, h: QuestHandle) {
        for p in self.all_paths(h) {
            self.act_finalize_quest(h, p);
        }
    }

    /// `StartQuest`.
    pub fn start_quest(&mut self, h: QuestHandle) -> bool {
        let Some(q) = self.quest(h) else {
            return false;
        };
        let template_id = q.template_id;
        let Some(si) = q.step_index(kind::START) else {
            tracing::warn!(
                "Tried to start a quest without a starter component Quest: {template_id}"
            );
            return false;
        };
        if !q.forced_start && !self.starter_accepts(q.template_id, q.acceptor_type, q.acceptor_id) {
            return false;
        }
        self.set_step(h, kind::START);
        // Send the first components, or the one that's used to start this ?
        let first = self
            .quest(h)
            .and_then(|q| q.steps[si].components.first())
            .map_or(0, |c| c.template_id);
        if let Some(q) = self.quest_mut(h) {
            q.component_id = first;
        }
        let row = self.quest(h).map(Quest::row);
        if let Some(row) = row {
            let now = self.now_ms;
            self.send(SCQuestContextStartedPacket {
                quest: row,
                component_id: first,
                now_ms: now,
            });
        }
        tracing::debug!(
            "StartQuest, Quest:{template_id}, Player {} ({})",
            self.c.unit.name,
            self.c.id
        );
        true
    }

    fn starter_accepts(&self, quest_id: u32, source: u8, id: u32) -> bool {
        let Some(template) = self.data.template(quest_id) else {
            return false;
        };
        template
            .components_of(&self.data.components, kind::START)
            .into_iter()
            .filter(|component| self.can_component_run(component.id))
            .any(|component| {
                let conditions: Vec<_> = component
                    .acts
                    .iter()
                    .filter_map(|&a| {
                        let at = &self.data.acts[a];
                        use ActKind::*;
                        Some(match at.kind {
                            ConAcceptNpc { npc_id }
                            | ConAcceptNpcKill { npc_id }
                            | ConAcceptNpcEmotion { npc_id, .. } => {
                                source == super::model::acceptor::NPC && id == npc_id
                            }
                            ConAcceptDoodad { doodad_id } => {
                                source == super::model::acceptor::DOODAD && id == doodad_id
                            }
                            ConAcceptItem { item_id, .. } => {
                                source == super::model::acceptor::ITEM
                                    && id == item_id
                                    && check_items(self.c, item_id, 1)
                            }
                            ConAcceptItemGain { item_id } => {
                                source == super::model::acceptor::ITEM
                                    && id == item_id
                                    && check_items(self.c, item_id, at.count)
                            }
                            ConAcceptItemEquip { item_id } => {
                                source == super::model::acceptor::ITEM
                                    && id == item_id
                                    && items_by_template(self.c, &[slot_type::EQUIPMENT], item_id).0
                            }
                            ConAcceptSkill { skill_id } => {
                                source == super::model::acceptor::SKILL && id == skill_id
                            }
                            ConAcceptBuff { buff_id } => {
                                source == super::model::acceptor::BUFF && id == buff_id
                            }
                            ConAcceptSphere { sphere_id } => {
                                source == super::model::acceptor::SPHERE && id == sphere_id
                            }
                            ConAcceptLevelUp { level } => self.c.unit.level >= level,
                            ConAutoComplete | ConAcceptComponent { .. } => true,
                            _ => return None,
                        })
                    })
                    .collect();
                conditions.is_empty() || conditions.iter().any(|valid| *valid)
            })
    }

    /// `RunCurrentStep`.
    pub fn run_current_step(&mut self, h: QuestHandle) -> bool {
        let Some(q) = self.quest(h) else {
            return false;
        };
        let Some(si) = q.step_index(q.step) else {
            return false;
        };
        let mut res = self.run_components(h, si);

        // HackFix: added to account for missing Ready step on Quests that use a Score + LetItBeDone
        let data = self.data;
        if let Some(q) = self.quest(h) {
            if let Some(t) = data.template(q.template_id) {
                if q.step == kind::PROGRESS
                    && t.score > 0
                    && t.let_it_done
                    && self.objective_percent(h) >= 1.0
                    && q.step_index(kind::READY).is_none()
                {
                    res = true;
                }
            }
        }

        if res {
            self.go_to_next_step(h);
        }

        // Send update to player
        if let Some(q) = self.quest(h) {
            if !q.skip_update {
                let (row, cid) = (q.row(), q.component_id);
                let now = self.now_ms;
                self.send(SCQuestContextUpdatedPacket::new(row, cid, now));
            }
        }
        res
    }

    /// `GoToNextStep`.
    pub fn go_to_next_step(&mut self, h: QuestHandle) {
        let Some(last_step) = self.quest(h).map(|q| q.step) else {
            return;
        };
        let template_id = self.template_id_of(h);
        let mut loop_lock_check = 0;
        loop {
            if loop_lock_check > 20 {
                tracing::error!(
                    "GoToNextStep got stuck in a infinite loop for Quest:{template_id}, Step:{} -> {}, Player {} ({}",
                    kind::name(last_step),
                    kind::name(self.quest(h).map_or(0, |q| q.step)),
                    self.c.unit.name,
                    self.c.id
                );
                break;
            }
            loop_lock_check += 1;
            let step = self.quest(h).map_or(kind::INVALID, |q| q.step);
            match step {
                kind::NONE => self.set_step(h, kind::SUPPLY),
                kind::START => self.set_step(h, kind::NONE),
                kind::SUPPLY => {
                    self.set_step(h, kind::PROGRESS);
                    self.set_status(h, status::PROGRESS);
                }
                kind::PROGRESS => {
                    self.set_step(h, kind::READY);
                    self.set_status(h, status::READY);
                }
                kind::FAIL => {
                    self.set_status(h, status::FAILED);
                    return;
                }
                kind::READY => {
                    self.set_step(h, kind::REWARD);
                    self.set_status(h, status::COMPLETED);
                }
                kind::DROP => {
                    self.drop_quest(template_id, true, false);
                    return;
                }
                kind::REWARD => {
                    // Mark quest as completed
                    let block = self.set_completed_quest_flag(template_id, true);
                    self.drop_quest(template_id, false, false);
                    let _ = self.dispatch(&super::events::QuestEvent::QuestComplete {
                        quest_id: template_id,
                    });
                    self.send(SCQuestContextCompletedPacket {
                        quest_id: template_id,
                        body: block.body8(),
                        component_id: 0,
                    });
                    return;
                }
                other => {
                    tracing::warn!(
                        "Quest GoToNextStep failed for Step:{}, Quest:{template_id}, Player:{} ({}",
                        kind::name(other),
                        self.c.unit.name,
                        self.c.id
                    );
                    return;
                }
            }
            // If the new current step is valid for this quest template, exit the loop
            if let Some(q) = self.quest(h) {
                if q.step_index(q.step).is_some() {
                    break;
                }
            }
        }
    }

    pub(super) fn set_status(&mut self, h: QuestHandle, s: u8) {
        if let Some(q) = self.quest_mut(h) {
            q.status = s;
        }
    }

    /// `Step` setter (`SetStep`).
    pub fn set_step(&mut self, h: QuestHandle, value: u8) {
        let Some(q) = self.quest(h) else {
            return;
        };
        if value == q.step {
            return;
        }
        // Finalize old Step (if any)
        if let Some(old) = q.step_index(q.step) {
            self.finalize_step(h, old);
        }
        let template_id = {
            let Some(q) = self.quest_mut(h) else {
                return;
            };
            q.step = value;
            // Reset active component (used by packet only)
            q.component_id = 0;
            q.template_id
        };
        // Initialize Acts for this Step (if any)
        if let Some(new) = self.quest(h).and_then(|q| q.step_index(value)) {
            self.initialize_step(h, new);
        }
        // Trigger OnQuestStepChanged event, even if this step is not available
        let _ = self.dispatch(&super::events::QuestEvent::QuestStepChanged {
            quest_id: template_id,
            step: value,
        });
        self.request_evaluation(h);
    }

    /// `RequestEvaluation` (`RequestEvaluationFlag = true`).
    pub fn request_evaluation(&mut self, h: QuestHandle) {
        let Some(q) = self.quest_mut(h) else {
            return;
        };
        if q.request_evaluation {
            return;
        }
        q.request_evaluation = true;
        if q.init_finished {
            self.enqueue_evaluation(h);
        }
    }

    /// `QuestInitialized`.
    pub fn quest_initialized(&mut self, h: QuestHandle) {
        let Some(q) = self.quest_mut(h) else {
            return;
        };
        q.init_finished = true;
        if q.request_evaluation {
            self.enqueue_evaluation(h);
        }
    }

    /// `StartingEvaluation`.
    pub fn starting_evaluation(&mut self, h: QuestHandle) {
        if let Some(q) = self.quest_mut(h) {
            q.request_evaluation = false;
        }
    }

    /// `QuestManager.EnqueueEvaluation(quest)`.
    fn enqueue_evaluation(&mut self, h: QuestHandle) {
        let template_id = self.template_id_of(h);
        tracing::info!(
            "EnqueueEvaluation, {} ({}), Quest {template_id}",
            self.c.unit.name,
            self.c.id
        );
        match &self.player {
            Some(p) => self.server.systems.quests.enqueue_evaluation(p, h),
            None => self.st.local_queue_push(h),
        }
    }

    /// `GetQuestObjectiveStatus`.
    pub fn objective_status(&self, h: QuestHandle) -> ObjectiveStatus {
        let data = self.data;
        let Some(q) = self.quest(h) else {
            return ObjectiveStatus::NotReady;
        };
        let Some(t) = data.template(q.template_id) else {
            return ObjectiveStatus::QuestComplete;
        };
        let Some(si) = q.step_index(kind::PROGRESS) else {
            return ObjectiveStatus::QuestComplete;
        };
        if !self.contains_objectives(h, si) {
            return ObjectiveStatus::QuestComplete;
        }
        let step = &q.steps[si];
        let obj = |idx: u8| q.objectives.get(usize::from(idx)).copied().unwrap_or(0);
        if t.score > 0 {
            let mut score = 0i32;
            for c in step.components.iter().filter(|c| c.is_active) {
                for a in &c.acts {
                    let at = &data.acts[a.template];
                    if at.objective_index == 0xFF {
                        continue;
                    }
                    score = score.wrapping_add(at.count.wrapping_mul(obj(at.objective_index)));
                }
            }
            let ceil = |f: f32| (t.score as f32 * f).ceil() as i32;
            if t.let_it_done && score >= ceil(3.0 / 2.0) {
                return ObjectiveStatus::Overachieved;
            }
            if t.let_it_done && score > t.score {
                return ObjectiveStatus::ExtraProgress;
            }
            if score >= t.score {
                return ObjectiveStatus::QuestComplete;
            }
            if t.let_it_done && score >= ceil(1.0 / 2.0) {
                return ObjectiveStatus::CanEarlyComplete;
            }
            return ObjectiveStatus::NotReady;
        }
        let mut min = ObjectiveStatus::Overachieved;
        let mut max = ObjectiveStatus::NotReady;
        for c in step.components.iter().filter(|c| c.is_active) {
            for a in &c.acts {
                let at = &data.acts[a.template];
                if at.objective_index == 0xFF {
                    continue;
                }
                let o = obj(at.objective_index);
                let s = if t.let_it_done && o >= at.count * 3 / 2 {
                    ObjectiveStatus::Overachieved
                } else if t.let_it_done && o > at.count {
                    ObjectiveStatus::ExtraProgress
                } else if o >= at.count {
                    ObjectiveStatus::QuestComplete
                } else if t.let_it_done && o >= at.count / 2 {
                    ObjectiveStatus::CanEarlyComplete
                } else {
                    ObjectiveStatus::NotReady
                };
                min = min.min(s);
                max = max.max(s);
            }
        }
        if t.selective {
            min.max(max)
        } else {
            min.min(max)
        }
    }

    /// `GetQuestObjectivePercent`.
    pub fn objective_percent(&self, h: QuestHandle) -> f32 {
        let data = self.data;
        let Some(q) = self.quest(h) else {
            return 1.0;
        };
        let Some(t) = data.template(q.template_id) else {
            return 1.0;
        };
        let Some(si) = q.step_index(kind::PROGRESS) else {
            return 1.0;
        };
        if !self.contains_objectives(h, si) {
            return 1.0;
        }
        let step = &q.steps[si];
        let obj = |idx: u8| q.objectives.get(usize::from(idx)).copied().unwrap_or(0);
        if t.score > 0 {
            let mut score = 0i32;
            for c in step.components.iter().filter(|c| c.is_active) {
                for a in &c.acts {
                    let at = &data.acts[a.template];
                    if at.objective_index == 0xFF {
                        continue;
                    }
                    score = score.wrapping_add(at.count.wrapping_mul(obj(at.objective_index)));
                }
            }
            let cap = (t.score as f32 * 3.0 / 2.0).ceil() as i32;
            if t.let_it_done && score >= cap {
                return cap as f32;
            }
            return 1.0 / t.score as f32 * score as f32;
        }
        let mut highest = 0f32;
        for c in step.components.iter().filter(|c| c.is_active) {
            for a in &c.acts {
                let at = &data.acts[a.template];
                if at.objective_index == 0xFF {
                    continue;
                }
                let o = obj(at.objective_index);
                if t.let_it_done && o >= at.count * 3 / 2 {
                    highest = highest.max(at.count as f32 * 3.0 / 2.0);
                    continue;
                }
                highest = highest.max(1.0 / at.count as f32 * o as f32);
            }
        }
        highest
    }

    // ----- Quest (rewards, drop, cleanup) -----

    /// `UseSkillAndBuff(component)`.
    pub fn use_skill_and_buff(&mut self, component_id: u32) {
        self.use_skill(component_id);
        self.use_buff(component_id);
    }

    fn use_buff(&mut self, component_id: u32) {
        let Some(ct) = self.data.component(component_id) else {
            return;
        };
        if ct.buff_id == 0 {
            return;
        }
        let Some(t) = self.server.game_data.skills.buffs.get(&ct.buff_id).cloned() else {
            tracing::warn!(
                "Quest component {component_id}: buff template {} not loaded",
                ct.buff_id
            );
            return;
        };
        let caster = Some((self.c.id, self.c.unit.level));
        let buff = Buff::new(t, SkillCaster::default(), caster, self.now_ms);
        self.c.unit.buffs.add_buff(buff, 0, 0, self.now_ms);
        self.buffs_changed = true;
    }

    /// `UseSkill(component)`.
    fn use_skill(&mut self, component_id: u32) {
        let Some(ct) = self.data.component(component_id) else {
            return;
        };
        if ct.skill_id > 0 {
            if ct.skill_self {
                self.deferred.push(Deferred::UseSkillSelf {
                    skill_id: ct.skill_id,
                });
            } else if ct.npc_id > 0 {
                self.deferred.push(Deferred::NpcUseSkill {
                    npc_template_id: ct.npc_id,
                    skill_id: ct.skill_id,
                });
            }
        }
    }

    /// `SetNpcAggro(component)`.
    pub fn set_npc_aggro(&mut self, component_id: u32) {
        let Some(ct) = self.data.component(component_id) else {
            return;
        };
        if ct.npc_ai_id == npc_ai::ATTACK_UNIT && ct.npc_id > 0 {
            self.deferred.push(Deferred::NpcSetFaction {
                npc_template_id: ct.npc_id,
                faction_id: FACTION_MONSTROSITY,
            });
        }
        if ct.npc_spawner_id > 0
            || matches!(
                ct.npc_ai_id,
                npc_ai::FOLLOW_UNIT
                    | npc_ai::FOLLOW_PATH
                    | npc_ai::GO_AWAY
                    | npc_ai::RUN_COMMAND_SET
            )
        {
            self.deferred.push(Deferred::NpcComponent { component_id });
        }
    }

    /// `DistributeRewards(addBaseQuestReward)`.
    pub fn distribute_rewards(&mut self, h: QuestHandle, add_base: bool) -> bool {
        let res = true;
        let hooks = self.hooks.clone();
        let player = self.player.clone();
        let Some(q) = self.quest(h) else {
            return true;
        };
        let template_id = q.template_id;
        // Distribute Items if needed
        if !q.reward_items.is_empty() && q.allow_item_rewards {
            let pool = q.reward_items.clone();
            let coins = q.reward_coins;
            let free = self
                .c
                .inventory
                .bag()
                .lock()
                .expect("bag poisoned")
                .free_slot_count();
            if free < pool.len() as i32 {
                let failed = hooks.send_quest_reward_mails(
                    self.server,
                    player.as_ref(),
                    self.c,
                    template_id,
                    &pool,
                    coins,
                );
                if failed > 0 {
                    self.send_error(error_msg::MAIL_UNKNOWN_FAILURE);
                    return false;
                }
                // Coins will be distributed in mail if any mail needed to be sent
                if let Some(q) = self.quest_mut(h) {
                    q.reward_coins = 0;
                }
                self.send(SCQuestRewardedByMailPacket {
                    quests: vec![template_id],
                });
            } else {
                for item in &pool {
                    if hooks.is_auto_equip_trade_pack(self.server, item.template_id) {
                        if !hooks.try_equip_new_backpack(
                            self.server,
                            player.as_ref(),
                            self.c,
                            item_task::QUEST_SUPPLY_ITEMS,
                            item.template_id,
                            item.count,
                            item.grade_id,
                        ) {
                            return false;
                        }
                    } else {
                        if !hooks.acquire_default_item(
                            self.server,
                            player.as_ref(),
                            self.c,
                            item_task::QUEST_SUPPLY_ITEMS,
                            item.template_id,
                            item.count,
                            item.grade_id,
                        ) {
                            return false;
                        }
                    }
                }
            }
            if let Some(q) = self.quest_mut(h) {
                q.reward_items.clear();
            }
        }

        // Add quest level based rewards
        let data = self.data;
        let (level, let_it_done) = data
            .template(template_id)
            .map_or((0, false), |t| (t.level, t.let_it_done));
        let step = self.quest(h).map_or(0, |q| q.step);
        if add_base && level > 0 && step == kind::REWARD {
            if let Some(lb) = data.supplies(level).copied() {
                let ratio = if let_it_done {
                    match self.objective_status(h) {
                        ObjectiveStatus::NotReady => 0f32,
                        ObjectiveStatus::CanEarlyComplete => 0.3f32,
                        ObjectiveStatus::QuestComplete => 1f32,
                        ObjectiveStatus::ExtraProgress => 1f32,
                        ObjectiveStatus::Overachieved => 1.2f32,
                    }
                } else {
                    1f32
                };
                if let Some(q) = self.quest_mut(h) {
                    q.reward_ratio = f64::from(ratio);
                    q.reward_exp = q.reward_exp.wrapping_add(lb.exp);
                    q.reward_coins = q.reward_coins.wrapping_add(lb.copper);
                }
            }
        }

        // Add XP
        let Some(q) = self.quest(h) else {
            return res;
        };
        let (exp_pool, coin_pool, ratio) = (q.reward_exp, q.reward_coins, q.reward_ratio);
        if exp_pool > 0 {
            let xp = round_even(f64::from(exp_pool) * ratio) as i32;
            if xp > 0 {
                hooks.add_exp(self.server, player.as_ref(), self.c, xp, true);
            }
            if let Some(q) = self.quest_mut(h) {
                q.reward_exp = 0;
            }
        }
        // Add copper coins
        if coin_pool > 0 {
            let copper = round_even(f64::from(coin_pool) * ratio) as i32;
            if copper > 0 {
                hooks.change_money(self.server, player.as_ref(), self.c, copper);
            }
            if let Some(q) = self.quest_mut(h) {
                q.reward_coins = 0;
            }
        }
        // Cleanup used Items from quest
        let cleanup = self
            .quest(h)
            .map(|q| q.cleanup_items.clone())
            .unwrap_or_default();
        if !cleanup.is_empty() {
            for item in &cleanup {
                hooks.consume_item(
                    self.server,
                    player.as_ref(),
                    self.c,
                    item_task::QUEST_COMPLETE,
                    item.template_id,
                    item.count,
                    None,
                );
            }
            if let Some(q) = self.quest_mut(h) {
                q.cleanup_items.clear();
            }
        }
        res
    }

    /// `Quest.Drop(update)`.
    pub fn quest_drop(&mut self, h: QuestHandle, update: bool) {
        let completing = self.quest(h).is_some_and(|q| q.status == status::COMPLETED);
        self.set_status(h, status::DROPPED);
        self.set_step(h, kind::DROP);
        let data = self.data;
        let template_id = self.template_id_of(h);
        let Some(t) = data.template(template_id) else {
            return;
        };
        for step in kind::READY..kind::REWARD {
            if let Some(c) = t.first_component(&data.components, step) {
                self.use_skill(c.id);
                self.use_buff(c.id);
            }
        }
        if update {
            if let Some(row) = self.quest(h).map(Quest::row) {
                let now = self.now_ms;
                self.send(SCQuestContextUpdatedPacket::new(row, 0, now));
            }
        }
        if !completing {
            for cid in &t.components {
                if let Some(c) = data.component(*cid) {
                    for &a in &c.acts {
                        self.act_quest_dropped(h, a);
                    }
                }
            }
        }
        // ClearObjectives
        if let Some(q) = self.quest_mut(h) {
            q.objectives = [0; 5];
        }
        self.request_evaluation(h);
    }

    /// `Quest.Cleanup()`.
    pub fn quest_cleanup(&mut self, h: QuestHandle) {
        let data = self.data;
        let Some(t) = data.template(self.template_id_of(h)) else {
            return;
        };
        for cid in &t.components {
            if let Some(c) = data.component(*cid) {
                for &a in &c.acts {
                    self.act_quest_cleanup(h, a);
                }
            }
        }
    }

    // ----- QuestStep / QuestComponent / QuestAct -----

    /// `QuestStep.InitializeStep`.
    fn initialize_step(&mut self, h: QuestHandle, si: usize) {
        // Distribute any rewards that may still be open so the pool is empty
        self.distribute_rewards(h, false);
        for p in self.step_paths(h, si) {
            self.act_initialize_action(h, p);
        }
    }

    /// `QuestStep.FinalizeStep`.
    fn finalize_step(&mut self, h: QuestHandle, si: usize) {
        for p in self.step_paths(h, si) {
            self.act_finalize_action(h, p);
        }
    }

    /// `QuestStep.ContainsObjectives`.
    pub fn contains_objectives(&self, h: QuestHandle, si: usize) -> bool {
        let data = self.data;
        self.quest(h)
            .and_then(|q| q.steps.get(si))
            .is_some_and(|s| {
                s.components
                    .iter()
                    .flat_map(|c| c.acts.iter())
                    .any(|a| data.acts[a.template].objective_index != 0xFF)
            })
    }

    /// `UnitRequirementsGameData.CanComponentRun(component, Owner)`.
    pub fn can_component_run(&self, component_id: u32) -> bool {
        let Some(ct) = self.data.component(component_id) else {
            return true;
        };
        let owner = OwnerView {
            c: self.c,
            st: self.st,
            server: self.server,
        };
        let env = EnvView {
            server: self.server,
            env: &self.env,
        };
        match &self.env.target {
            Some(t) => self.data.unit_reqs.can_component_run(
                component_id,
                ct.or_unit_reqs,
                &owner,
                &TargetView(t, self.server),
                &env,
            ),
            None => self.data.unit_reqs.can_component_run(
                component_id,
                ct.or_unit_reqs,
                &owner,
                &owner,
                &env,
            ),
        }
    }

    /// `QuestStep.RunComponents`.
    fn run_components(&mut self, h: QuestHandle, si: usize) -> bool {
        let data = self.data;
        let Some(q) = self.quest(h) else {
            return false;
        };
        let this_step = q.steps[si].kind;
        let Some(t) = data.template(q.template_id) else {
            return false;
        };
        let n = q.steps[si].components.len();
        // Cache which components are active
        for ci in 0..n {
            let cid = self
                .quest(h)
                .map_or(0, |q| q.steps[si].components[ci].template_id);
            let active = self.can_component_run(cid);
            if let Some(q) = self.quest_mut(h) {
                q.steps[si].components[ci].is_active = active;
            }
        }
        let mut res = true;
        if t.selective && this_step == kind::PROGRESS {
            // Require only one of the components to be true in the progress step
            res = false;
            for ci in 0..n {
                let Some((active, cid)) = self.component_info(h, si, ci) else {
                    continue;
                };
                if !active {
                    continue;
                }
                let r = self.run_component(h, si, ci);
                if r {
                    if let Some(q) = self.quest_mut(h) {
                        q.component_id = cid;
                    }
                }
                res |= r;
            }
        } else {
            // Requires all components to be true
            for ci in 0..n {
                let Some((active, cid)) = self.component_info(h, si, ci) else {
                    continue;
                };
                if !active {
                    res = false;
                    continue;
                }
                let r = self.run_component(h, si, ci);
                res &= r;
                if res {
                    if let Some(q) = self.quest_mut(h) {
                        q.component_id = cid;
                    }
                }
            }
        }

        // Override result for score quests
        if this_step == kind::PROGRESS && t.score > 0 {
            let mut score = 0i32;
            if let Some(q) = self.quest(h) {
                for c in q.steps[si].components.iter().filter(|c| c.is_active) {
                    for a in &c.acts {
                        let at = &data.acts[a.template];
                        // `questAct.GetObjective(Parent)` (index 0xFF would throw)
                        let o = q
                            .objectives
                            .get(usize::from(at.objective_index))
                            .copied()
                            .unwrap_or(0);
                        score = score.wrapping_add(o.wrapping_mul(at.count));
                    }
                }
            }
            res = score >= t.score;
            let os = self.objective_status(h);
            self.set_status(
                h,
                if os >= ObjectiveStatus::QuestComplete {
                    status::READY
                } else {
                    status::PROGRESS
                },
            );
        } else if this_step == kind::PROGRESS && t.let_it_done {
            let os = self.objective_status(h);
            res = os >= ObjectiveStatus::Overachieved;
            self.set_status(
                h,
                if os >= ObjectiveStatus::QuestComplete {
                    status::READY
                } else {
                    status::PROGRESS
                },
            );
        }

        // Handle Supply/Reward Distribution
        let d = self.distribute_rewards(h, true);
        res &= d;

        // LetItBeDone type of quests, are always forced forward using the Report Acts
        if this_step == kind::PROGRESS && t.let_it_done {
            res = false;
        }
        res
    }

    fn component_info(&self, h: QuestHandle, si: usize, ci: usize) -> Option<(bool, u32)> {
        self.quest(h)
            .and_then(|q| q.steps.get(si))
            .and_then(|s| s.components.get(ci))
            .map(|c| (c.is_active, c.template_id))
    }

    /// `QuestComponent.RunComponent`.
    pub fn run_component(&mut self, h: QuestHandle, si: usize, ci: usize) -> bool {
        let Some(q) = self.quest(h) else {
            return false;
        };
        let step_kind = q.steps[si].kind;
        let comp = &q.steps[si].components[ci];
        let n = comp.acts.len();
        let cid = comp.template_id;
        let or_check = (step_kind == kind::START || step_kind == kind::READY) && n > 0;
        let mut res = true;
        if or_check {
            // Or checks, needed to handle quests with multiple starters or multiple report NPCs
            res = false;
            for ai in 0..n {
                let r = self.run_act(
                    h,
                    ActPath {
                        step: si,
                        component: ci,
                        act: ai,
                    },
                );
                res |= r;
            }
        } else {
            for ai in 0..n {
                let r = self.run_act(
                    h,
                    ActPath {
                        step: si,
                        component: ci,
                        act: ai,
                    },
                );
                res &= r;
            }
        }
        res |= self
            .quest(h)
            .and_then(|q| q.steps.get(si))
            .and_then(|s| s.components.get(ci))
            .is_some_and(|c| c.override_completed);
        // If acts completed, handle skill and buff effects
        if res {
            self.use_skill_and_buff(cid);
            self.set_npc_aggro(cid);
        }
        res
    }

    /// `QuestAct.RunAct`.
    pub fn run_act(&mut self, h: QuestHandle, p: ActPath) -> bool {
        let Some(a) = self.act_ref(h, p) else {
            return false;
        };
        let at = &self.data.acts[a];
        let count = match self.quest(h) {
            Some(q)
                if at.component_kind == kind::PROGRESS
                    && usize::from(at.objective_index) < q.objectives.len() =>
            {
                q.objectives[usize::from(at.objective_index)]
            }
            _ => 0,
        };
        let supply = at.kind.class_name().starts_with("QuestActSupply");
        if supply
            && self
                .quest(h)
                .is_some_and(|q| q.steps[p.step].components[p.component].acts[p.act].supplied)
        {
            return true;
        }
        let r = self.act_run(h, p, count);
        if r && supply {
            if let Some(q) = self.quest_mut(h) {
                q.steps[p.step].components[p.component].acts[p.act].supplied = true;
            }
        }
        r || self.act_override(h, p)
    }

    /// `QuestAct.OverrideObjectiveCompleted` getter.
    pub fn act_override(&self, h: QuestHandle, p: ActPath) -> bool {
        self.quest(h)
            .and_then(|q| q.steps.get(p.step))
            .and_then(|s| s.components.get(p.component))
            .and_then(|c| c.acts.get(p.act))
            .is_some_and(|a| a.override_completed)
    }

    /// `QuestAct.OverrideObjectiveCompleted` setter.
    pub fn set_act_override(&mut self, h: QuestHandle, p: ActPath, value: bool) {
        let changed = {
            let Some(c) = self
                .quest_mut(h)
                .and_then(|q| q.steps.get_mut(p.step))
                .and_then(|s| s.components.get_mut(p.component))
            else {
                return;
            };
            let Some(a) = c.acts.get_mut(p.act) else {
                return;
            };
            if a.override_completed == value {
                false
            } else {
                a.override_completed = value;
                if value {
                    c.override_completed = true;
                }
                true
            }
        };
        if changed {
            self.request_evaluation(h);
        }
    }

    // ----- objective helpers (QuestActTemplate) -----

    /// `SetObjective(quest, value)`.
    pub fn set_objective(&mut self, h: QuestHandle, act: usize, value: i32) -> QResult {
        let data = self.data;
        let at = &data.acts[act];
        let Some(q) = self.quest(h) else {
            return Ok(());
        };
        let Some(t) = data.template(q.template_id) else {
            return Ok(());
        };
        let max = at
            .max_objective(t)
            .ok_or_else(|| Throw("DivideByZeroException in MaxObjective".into()))?;
        let value = value.min(max);
        let idx = usize::from(at.objective_index);
        let Some(q) = self.quest_mut(h) else {
            return Ok(());
        };
        let Some(slot) = q.objectives.get_mut(idx) else {
            return Err(Throw(format!(
                "IndexOutOfRangeException: Objectives[{idx}] (act {} {})",
                at.kind.class_name(),
                at.detail_id
            )));
        };
        if *slot != value {
            *slot = value;
            self.request_evaluation(h);
        }
        Ok(())
    }

    /// `AddObjective(quest, amount)`; returns the new value.
    pub fn add_objective(&mut self, h: QuestHandle, act: usize, amount: i32) -> QResult<i32> {
        let data = self.data;
        let at = &data.acts[act];
        let idx = usize::from(at.objective_index);
        let Some(q) = self.quest(h) else {
            return Ok(0);
        };
        if amount == 0 {
            return q
                .objectives
                .get(idx)
                .copied()
                .ok_or_else(|| Throw(format!("IndexOutOfRangeException: Objectives[{idx}]")));
        }
        let Some(t) = data.template(q.template_id) else {
            return Ok(0);
        };
        let max = at
            .max_objective(t)
            .ok_or_else(|| Throw("DivideByZeroException in MaxObjective".into()))?;
        let Some(q) = self.quest_mut(h) else {
            return Ok(0);
        };
        let Some(slot) = q.objectives.get_mut(idx) else {
            return Err(Throw(format!(
                "IndexOutOfRangeException: Objectives[{idx}]"
            )));
        };
        if max > 0 && slot.wrapping_add(amount) >= max {
            *slot = max;
        } else {
            *slot = slot.wrapping_add(amount);
        }
        let v = *slot;
        self.request_evaluation(h);
        Ok(v)
    }

    /// `GetObjective(quest)` (safe read: an out-of-range index reads 0).
    pub fn get_objective(&self, h: QuestHandle, act: usize) -> i32 {
        let idx = usize::from(self.data.acts[act].objective_index);
        self.quest(h)
            .and_then(|q| q.objectives.get(idx).copied())
            .unwrap_or(0)
    }

    // ----- CharacterQuests -----

    /// `HasQuest`.
    pub fn has_quest(&self, quest_id: u32) -> bool {
        self.st.has_quest(quest_id)
    }

    /// `HasQuestCompleted` / `IsQuestComplete`.
    pub fn has_quest_completed(&self, quest_id: u32) -> bool {
        has_quest_completed(&self.c.quests.completed, quest_id)
    }

    /// `SetCompletedQuestFlag`: returns the changed block.
    pub fn set_completed_quest_flag(&mut self, quest_id: u32, completed: bool) -> CompletedQuest {
        set_completed_quest_flag(&mut self.c.quests.completed, quest_id, completed)
    }

    /// `CanAcceptSupplyItems`.
    fn can_accept_supply_items(&mut self, template_id: u32) -> bool {
        let data = self.data;
        let Some(t) = data.template(template_id) else {
            return true;
        };
        for c in t.components_of(&data.components, kind::SUPPLY) {
            for &a in &c.acts {
                let ActKind::SupplyItem { item_id, .. } = data.acts[a].kind else {
                    continue;
                };
                if self.hooks.is_auto_equip_trade_pack(self.server, item_id)
                    && !self
                        .hooks
                        .can_replace_glider_in_backpack_slot(self.server, self.c)
                {
                    tracing::trace!(
                        "User {} ({}) cannot accept Quest {template_id}, backpack supply item {item_id} cannot be equipped",
                        self.c.unit.name,
                        self.c.id
                    );
                    self.send_error(error_msg::BACKPACK_OCCUPIED);
                    return false;
                }
            }
        }
        true
    }

    /// `AddQuest(questId, forcibly, questAcceptorType, acceptorId)`.
    pub fn add_quest(
        &mut self,
        quest_id: u32,
        forcibly: bool,
        acceptor_type: u8,
        acceptor_id: u32,
    ) -> bool {
        if self.has_quest(quest_id) {
            if forcibly {
                tracing::info!("[GM] quest {quest_id}, added!");
                self.drop_quest(quest_id, true, false);
            } else {
                tracing::info!("Duplicate quest {quest_id}, not added!");
                return false;
            }
        }
        let data = self.data;
        let Some(template) = data.template(quest_id) else {
            tracing::error!("Failed to start new Quest {quest_id}, invalid Id");
            return false;
        };
        // `game_schedule_quests`: a scheduled quest is acceptable only inside its window
        // (`forcibly` = the GM path, like the component requirements below).
        if !forcibly
            && !super::quest_schedule_allows(
                &self.server.game_data.systems.npcs.schedule,
                quest_id,
                crate::systems::npcs::spawn_manager::unix_now(),
            )
        {
            tracing::trace!(
                "User {} ({}) cannot start new Quest {quest_id}, its game schedule window is not in progress",
                self.c.unit.name,
                self.c.id
            );
            return false;
        }
        if !forcibly && !self.can_accept_supply_items(quest_id) {
            return false;
        }
        // Check if start step components are active
        for c in template.components_of(&data.components, kind::START) {
            if !self.can_component_run(c.id) {
                tracing::trace!(
                    "User {} ({}) does not meet requirements to start new Quest {quest_id}, ComponentId {}",
                    self.c.unit.name,
                    self.c.id,
                    c.id
                );
                if !forcibly {
                    return false;
                }
            }
        }
        if self.has_quest_completed(quest_id) {
            if forcibly {
                tracing::info!("[GM] quest {quest_id}, added!");
                self.set_completed_quest_flag(quest_id, false);
            } else if !template.repeatable {
                tracing::warn!(
                    "Quest {quest_id} already completed for {}, not added!",
                    self.c.unit.name
                );
                self.send_error(error_msg::QUEST_DAILY_LIMIT);
                return false;
            }
        }

        // Create new Quest Object (the object initializer runs after the constructor)
        let h = self.new_quest(quest_id, 0);
        let id = match self.server.ids.quest.get_next_id() {
            Ok(id) => id,
            Err(e) => {
                tracing::error!("QuestIdManager: {e}");
                self.st.quests.remove(&h);
                return false;
            }
        };
        if let Some(q) = self.quest_mut(h) {
            q.id = id;
            q.status = status::INVALID;
            q.condition = condition::PROGRESS;
            q.acceptor_type = acceptor_type;
            q.acceptor_id = acceptor_id;
            q.forced_start = forcibly;
        }

        // Cancel stale work before starting another instance of this quest.
        self.server
            .systems
            .quests
            .remove_quest_timer(self.c.id, quest_id);

        // Actually start the quest by setting step to Start and send the quest start packets
        if !self.start_quest(h) {
            self.finalize_quest_acts(h);
            self.st.quests.remove(&h);
            self.server.ids.quest.release_id(id);
            return false;
        }

        // Add it to the Active Quests
        self.st.active.add(quest_id, h);
        let msg = format!("[Quest] {}, quest {quest_id} added.", self.c.unit.name);
        self.send_debug_message(msg);

        // Execute the first Step
        self.run_current_step(h);
        self.quest_initialized(h);
        true
    }

    /// `DropQuest(questId, update, forcibly)`.
    pub fn drop_quest(&mut self, quest_id: u32, update: bool, forcibly: bool) {
        let Some(h) = self.st.active_handle(quest_id) else {
            return;
        };
        // make sure no further "update packets" are send to the player
        if let Some(q) = self.quest_mut(h) {
            q.skip_update = true;
        }
        if self.quest(h).is_some_and(|q| q.status == status::COMPLETED) {
            self.quest_cleanup(h);
        }
        self.quest_drop(h, update);
        self.finalize_quest_acts(h);
        self.st.active.remove(quest_id);
        self.c.quests.removed.push(quest_id);
        if forcibly {
            self.set_completed_quest_flag(quest_id, false);
        }
        let msg = format!(
            "[Quest] for player: {}, quest: {quest_id} removed.",
            self.c.unit.name
        );
        self.send_debug_message(msg.clone());
        tracing::warn!("{msg}");
        self.server
            .systems
            .quests
            .remove_quest_timer(self.c.id, quest_id);
        if let Some(id) = self.quest(h).map(|q| q.id) {
            self.server.ids.quest.release_id(id);
        }
    }

    /// `FailQuest(owner, questId)`.
    pub fn fail_quest(&mut self, quest_id: u32) {
        let Some(h) = self.st.active_handle(quest_id) else {
            tracing::warn!(
                "FailQuest triggered for a quest that isn't active, Quest:{quest_id}, Player:{} ({})",
                self.c.unit.name,
                self.c.id
            );
            return;
        };
        self.set_status(h, status::FAILED);
        self.set_step(h, kind::FAIL);
        tracing::debug!("[Quest] {}, quest {quest_id} failed.", self.c.unit.name);
    }

    /// `SetStep(questContextId, step, selectedReward)` (GM helper).
    pub fn gm_set_step(&mut self, quest_id: u32, step: u32, selected_reward: i32) -> bool {
        if step > 8 {
            return false;
        }
        let Some(h) = self.st.active_handle(quest_id) else {
            return false;
        };
        if selected_reward >= 0 {
            if let Some(q) = self.quest_mut(h) {
                q.selected_reward_index = selected_reward;
            }
        }
        self.set_step(h, step as u8);
        true
    }

    /// `TryCompleteQuestAsLetItDone(questId, selectedReward)`.
    pub fn try_complete_quest_as_let_it_done(&mut self, quest_id: u32, selected: i32) {
        let Some(h) = self.st.active_handle(quest_id) else {
            return;
        };
        let let_it_done = self.data.template(quest_id).is_some_and(|t| t.let_it_done);
        if !let_it_done {
            return;
        }
        if self.objective_status(h) < ObjectiveStatus::CanEarlyComplete {
            return;
        }
        if let Some(q) = self.quest_mut(h) {
            q.selected_reward_index = selected;
        }
        self.set_step(h, kind::REWARD);
    }

    /// `ResetQuests(questDetail, sendIfChanged)`.
    pub fn reset_quests(&mut self, details: &[u32], send_if_changed: bool) {
        let data = self.data;
        for bi in 0..self.c.quests.completed.len() {
            let block_id = self.c.quests.completed[bi].id;
            for idx in 0..64u32 {
                let quest_id = u32::from(block_id) * 64 + idx;
                let Some(t) = data.template(quest_id) else {
                    continue;
                };
                if self.has_quest(quest_id) {
                    continue;
                }
                for &qd in details {
                    let block = &mut self.c.quests.completed[bi];
                    if t.detail_id == qd && bit_get(&block.body, idx as usize) {
                        bit_set(&mut block.body, idx as usize, false);
                        let body = block.body8();
                        tracing::info!("QuestReset by {}, reset {quest_id}", self.c.unit.name);
                        if send_if_changed {
                            self.send(SCQuestContextResetPacket {
                                quest_id,
                                body,
                                component_id: u32::from(block_id),
                            });
                        }
                    }
                }
            }
        }
    }

    /// `ResetDailyQuests(sendPacketsIfChanged)`.
    pub fn reset_daily_quests(&mut self, send: bool) {
        self.reset_quests(
            &[
                detail::DAILY,
                detail::DAILY_GROUP,
                detail::DAILY_HUNT,
                detail::DAILY_LIVELIHOOD,
            ],
            send,
        );
    }

    /// `CheckDailyResetAtLogin` (UTC dates of now and `LeaveTime`).
    pub fn check_daily_reset_at_login(&mut self) {
        let today = self.now_ms.div_euclid(1000).div_euclid(86_400);
        let leave_day = self.c.leave_time.div_euclid(86_400);
        if today - leave_day >= 1 {
            self.reset_daily_quests(false);
        }
    }

    /// `CharacterQuests.Load` from the `quests` rows already read into `c.quests.active`.
    pub fn load_from_rows(&mut self) {
        let rows = std::mem::take(&mut self.c.quests.active);
        for row in rows {
            if self.data.template(row.template_id).is_none() {
                tracing::error!(
                    "Quest {} by {} does not exist",
                    row.template_id,
                    self.c.unit.name
                );
                continue;
            }
            if self.st.has_quest(row.template_id) {
                tracing::error!(
                    "Quest {} of {} is in the quests table twice",
                    row.template_id,
                    self.c.unit.name
                );
                continue;
            }
            let h = self.new_quest(row.template_id, row.id);
            if let Some(q) = self.quest_mut(h) {
                q.status = row.status;
                q.acceptor_type = row.acceptor_type;
                q.acceptor_id = row.acceptor_id;
                q.time_ms = row.time_ms;
            }
            self.st.active.add(row.template_id, h);
            // Initializers need the saved deadline, acceptor and active quest identity.
            self.set_step(h, row.step);
            if let Some(q) = self.quest_mut(h) {
                q.objectives = row.objectives;
                q.acceptor_type = row.acceptor_type;
                q.component_id = row.component_id;
                q.acceptor_id = row.acceptor_id;
                q.status = row.status;
            }
            // Timers in earlier components must also resume at a saved Progress step.
            if matches!(row.step, kind::START | kind::SUPPLY | kind::PROGRESS) {
                for path in self.all_paths(h) {
                    let Some(a) = self.act_ref(h, path) else {
                        continue;
                    };
                    if matches!(self.data.acts[a].kind, ActKind::CheckTimer { .. })
                        && self.data.acts[a].component_kind <= row.step
                        && !self
                            .server
                            .systems
                            .quests
                            .has_quest_timer(self.c.id, row.template_id)
                    {
                        self.act_initialize_action(h, path);
                    }
                }
                if self
                    .server
                    .systems
                    .quests
                    .has_quest_timer(self.c.id, row.template_id)
                    && self.quest(h).is_some_and(|q| q.time_ms <= self.now_ms)
                {
                    let _ = self.dispatch(&super::events::QuestEvent::TimerExpired {
                        quest_id: row.template_id,
                    });
                }
            }
            self.quest_initialized(h);
            self.request_evaluation(h);
        }
        self.st.loaded = true;
    }

    /// `OnQuestItemManuallyDestroyed(item)` (item template id + its `LootQuestId`).
    pub fn on_quest_item_manually_destroyed(&mut self, item_template_id: u32, loot_quest_id: u32) {
        if loot_quest_id == 0 {
            return;
        }
        let data = self.data;
        let mut do_drop = false;
        let handles: Vec<QuestHandle> = self.st.active.values().copied().collect();
        'quests: for h in handles {
            let Some(q) = self.quest(h) else {
                continue;
            };
            let Some(t) = data.template(q.template_id) else {
                continue;
            };
            let mut step = q.step;
            while step >= kind::START {
                for c in t.components_of(&data.components, step) {
                    for &a in &c.acts {
                        let at = &data.acts[a];
                        if let Some((item_id, drop)) = at.kind.generic_item() {
                            if item_id == item_template_id {
                                if drop {
                                    do_drop = true;
                                    break;
                                }
                                return;
                            }
                        }
                        match &at.kind {
                            ActKind::ObjItemGroupGather {
                                item_group_id,
                                drop_when_destroy,
                                ..
                            }
                            | ActKind::ObjItemGroupUse {
                                item_group_id,
                                drop_when_destroy,
                                ..
                            } if data.group_items.contains(*item_group_id, item_template_id) => {
                                if *drop_when_destroy {
                                    do_drop = true;
                                    break;
                                }
                                return;
                            }
                            ActKind::ObjItemUse {
                                item_id,
                                drop_when_destroy,
                                ..
                            } if *item_id == item_template_id => {
                                if *drop_when_destroy {
                                    do_drop = true;
                                    break;
                                }
                                return;
                            }
                            _ => {}
                        }
                    }
                    if do_drop {
                        break;
                    }
                }
                // C#: the step loop itself does not stop on doDropQuest
                step -= 1;
            }
            if do_drop {
                break 'quests;
            }
        }
        if do_drop {
            self.drop_quest(loot_quest_id, true, false);
        }
    }

    /// `GetActiveActsWithUseItem(itemId)`: component ids of the matching acts (quests whose
    /// current step has no components are skipped; the C# throws there).
    pub fn active_acts_with_use_item(&self, item_id: u32) -> Vec<(QuestHandle, ActPath)> {
        let data = self.data;
        let mut out = Vec::new();
        for h in self.st.active.values().copied() {
            let Some(q) = self.quest(h) else {
                continue;
            };
            let Some(si) = q.step_index(q.step) else {
                continue;
            };
            for (ci, c) in q.steps[si].components.iter().enumerate() {
                for (ai, a) in c.acts.iter().enumerate() {
                    if matches!(data.acts[a.template].kind, ActKind::ObjItemUse { item_id: i, .. } if i == item_id)
                    {
                        out.push((
                            h,
                            ActPath {
                                step: si,
                                component: ci,
                                act: ai,
                            },
                        ));
                    }
                }
            }
        }
        out
    }

    /// `AddQuestTimer(owner, quest, limitTime)`.
    pub fn add_quest_timer(&mut self, h: QuestHandle, limit_time: i32) -> bool {
        let template_id = self.template_id_of(h);
        let saved = self
            .quest(h)
            .map_or(super::state::DATETIME_MIN_MS, |q| q.time_ms);
        let deadline =
            if saved <= super::state::DATETIME_MIN_MS || saved >= super::state::DATETIME_MAX_MS {
                self.now_ms.saturating_add(i64::from(limit_time.max(0)))
            } else {
                saved
            };
        let remaining = deadline
            .saturating_sub(self.now_ms)
            .clamp(0, i64::from(limit_time.max(0))) as i32;
        let ok = match &self.player {
            Some(player) => {
                self.server
                    .systems
                    .quests
                    .add_player_quest_timer(player, template_id, remaining)
            }
            None => self
                .server
                .systems
                .quests
                .add_quest_timer(self.c.id, template_id, remaining),
        };
        if !ok {
            return false;
        }
        if let Some(q) = self.quest_mut(h) {
            q.time_ms = deadline;
        }
        let id = self.quest(h).map_or(0, |q| q.id);
        self.send_debug_message(format!(
            "[Quest] Quest ({id}) will end in {} minutes.",
            limit_time / 60000
        ));
        true
    }

    /// Re-derives `c.quests.active` from the live quests (after every operation).
    pub fn sync_rows(&mut self) {
        self.c.quests.active = self.st.rows();
    }
}

// ----- completed quest blocks (BitArray over the stored bytes) -----

pub(crate) fn bit_get(body: &[u8], idx: usize) -> bool {
    body.get(idx / 8).is_some_and(|b| b >> (idx % 8) & 1 == 1)
}

pub(crate) fn bit_set(body: &mut Vec<u8>, idx: usize, v: bool) {
    if body.len() <= idx / 8 {
        body.resize(idx / 8 + 1, 0);
    }
    if v {
        body[idx / 8] |= 1 << (idx % 8);
    } else {
        body[idx / 8] &= !(1 << (idx % 8));
    }
}

/// `HasQuestCompleted(questId)`.
pub fn has_quest_completed(blocks: &[CompletedQuest], quest_id: u32) -> bool {
    let block_id = (quest_id / 64) as u16;
    blocks
        .iter()
        .find(|b| b.id == block_id)
        .is_some_and(|b| bit_get(&b.body, (quest_id % 64) as usize))
}

/// `SetCompletedQuestFlag(questId, isCompleted)`.
pub fn set_completed_quest_flag(
    blocks: &mut Vec<CompletedQuest>,
    quest_id: u32,
    completed: bool,
) -> CompletedQuest {
    let block_id = (quest_id / 64) as u16;
    let idx = (quest_id % 64) as usize;
    let i = match blocks.iter().position(|b| b.id == block_id) {
        Some(i) => i,
        None => {
            blocks.push(CompletedQuest::new(block_id));
            blocks.len() - 1
        }
    };
    bit_set(&mut blocks[i].body, idx, completed);
    blocks[i].clone()
}

// ----- UnitReqs views -----

/// The owner (`BaseUnit` = the locked character).
pub struct OwnerView<'a> {
    pub c: &'a Character,
    pub st: &'a QuestState,
    pub server: &'a GameServer,
}

impl ReqSubject for OwnerView<'_> {
    fn is_player(&self) -> bool {
        true
    }
    fn level(&self) -> u8 {
        self.c.unit.level
    }
    fn ability_level(&self, ability: u8) -> Option<u8> {
        let exp = self
            .c
            .abilities
            .abilities
            .iter()
            .find(|a| a.id == ability)
            .map(|a| a.exp);
        Some(exp.map_or(0, |e| {
            self.server
                .game_data
                .experience
                .level_from_exp_any(e, false)
                .map_or(0, |(l, _)| l)
        }))
    }
    fn race(&self) -> Option<u8> {
        Some(self.c.race)
    }
    fn gender(&self) -> Option<u8> {
        Some(self.c.gender)
    }
    fn equip_slot_used(&self, slot: i32) -> bool {
        self.c
            .inventory
            .equipment()
            .lock()
            .expect("equipment poisoned")
            .get_item_by_slot(slot)
            .is_some()
    }
    fn has_equipped(&self, template_id: u32) -> bool {
        !self
            .c
            .inventory
            .equipment()
            .lock()
            .expect("equipment poisoned")
            .items_by_template(template_id, -1)
            .0
            .is_empty()
    }
    fn owns_item(&self, template_id: u32) -> bool {
        items_by_template(self.c, &[], template_id).0
    }
    fn has_skill(&self, skill_id: u32) -> bool {
        self.c.skills.skills.iter().any(|s| s.id == skill_id)
    }
    fn is_in_battle(&self) -> bool {
        self.c.unit.is_in_battle
    }
    fn has_buff(&self, buff_id: u32) -> bool {
        self.c.unit.buffs.check_buff(buff_id)
    }
    fn has_buff_tag(&self, tag: u32) -> bool {
        // Buffs.CheckBuffTag(tag)
        has_buff_tag_in(
            self.server,
            self.c.unit.buffs.effects.iter().map(|b| b.template.id),
            tag,
        )
    }
    fn hpp(&self) -> i32 {
        self.c.unit.hpp()
    }
    fn base_unit_type(&self) -> u8 {
        0
    }
    fn npc_template_id(&self) -> Option<u32> {
        None
    }
    fn is_under_water(&self) -> bool {
        self.c.unit.is_under_water
    }
    fn faction_id(&self) -> u32 {
        self.c.unit.faction_id
    }
    fn mother_faction_id(&self) -> u32 {
        self.server
            .game_data
            .factions
            .get(self.c.unit.faction_id)
            .map_or(0, |f| f.mother_id)
    }
    fn quest_completed(&self, quest_id: u32) -> Option<bool> {
        Some(has_quest_completed(&self.c.quests.completed, quest_id))
    }
    fn quest_step(&self, quest_id: u32) -> Option<Option<u8>> {
        Some(self.st.active_quest(quest_id).map(|q| q.step))
    }
    fn actability_point(&self, id: u32) -> Option<i32> {
        self.c
            .actability
            .actabilities
            .iter()
            .find(|a| a.id == id)
            .map(|a| a.point)
    }
    fn honor_point(&self) -> Option<i32> {
        Some(self.c.honor_point)
    }
    fn jury_point(&self) -> Option<i32> {
        Some(self.c.jury_point)
    }
    fn vocation_point(&self) -> Option<i32> {
        Some(self.c.vocation_point)
    }
    fn has_appellation(&self, id: u32) -> Option<bool> {
        Some(self.c.appellations.appellations.contains(&id))
    }
    fn zone_key(&self) -> u32 {
        self.c.unit.transform.zone_id
    }
    fn position(&self) -> (f32, f32, f32) {
        let t = &self.c.unit.transform;
        (t.x, t.y, t.z)
    }
    fn labor_margin(&self) -> Option<i32> {
        // TimedRewardsManager.GetMaxLabor(Connection.Payment.PremiumState) - LaborPower
        Some(crate::systems::skills::unit_reqs::max_labor() - i32::from(self.c.labor_power))
    }
    fn expedition(&self) -> Option<(u32, u32)> {
        (self.c.unit.expedition_id > 0).then_some((self.c.unit.expedition_id, 0))
    }
    fn character_id(&self) -> Option<u32> {
        Some(self.c.id)
    }
}

/// `Buffs.CheckBuffTag(tag)` over the template ids of a unit's buffs.
fn has_buff_tag_in(server: &GameServer, mut buff_ids: impl Iterator<Item = u32>, tag: u32) -> bool {
    match server.game_data.systems.skills.buffs_by_tag(tag) {
        Some(ids) => buff_ids.any(|id| ids.contains(&id)),
        None => false,
    }
}

/// The current target, from its snapshot.
pub struct TargetView<'a>(pub &'a UnitSnapshot, pub &'a GameServer);

impl ReqSubject for TargetView<'_> {
    fn is_unit(&self) -> bool {
        self.0.is_unit
    }
    fn is_player(&self) -> bool {
        false
    }
    fn level(&self) -> u8 {
        self.0.level
    }
    fn ability_level(&self, _ability: u8) -> Option<u8> {
        None
    }
    fn race(&self) -> Option<u8> {
        None
    }
    fn gender(&self) -> Option<u8> {
        None
    }
    fn equip_slot_used(&self, _slot: i32) -> bool {
        false
    }
    fn has_equipped(&self, _template_id: u32) -> bool {
        false
    }
    fn owns_item(&self, _template_id: u32) -> bool {
        false
    }
    fn has_skill(&self, _skill_id: u32) -> bool {
        false
    }
    fn is_in_battle(&self) -> bool {
        self.0.in_battle
    }
    fn has_buff(&self, buff_id: u32) -> bool {
        self.0.buff_ids.contains(&buff_id)
    }
    fn has_buff_tag(&self, tag: u32) -> bool {
        has_buff_tag_in(self.1, self.0.buff_ids.iter().copied(), tag)
    }
    fn hpp(&self) -> i32 {
        self.0.hpp
    }
    fn base_unit_type(&self) -> u8 {
        self.0.base_unit_type
    }
    fn npc_template_id(&self) -> Option<u32> {
        self.0.npc_template_id
    }
    fn doodad_template_id(&self) -> Option<u32> {
        self.0.doodad_template_id
    }
    fn is_under_water(&self) -> bool {
        self.0.under_water
    }
    fn faction_id(&self) -> u32 {
        self.0.faction_id
    }
    fn mother_faction_id(&self) -> u32 {
        self.0.mother_faction_id
    }
    fn quest_completed(&self, _quest_id: u32) -> Option<bool> {
        None
    }
    fn quest_step(&self, _quest_id: u32) -> Option<Option<u8>> {
        None
    }
    fn actability_point(&self, _id: u32) -> Option<i32> {
        None
    }
    fn honor_point(&self) -> Option<i32> {
        None
    }
    fn jury_point(&self) -> Option<i32> {
        None
    }
    fn vocation_point(&self) -> Option<i32> {
        None
    }
    fn has_appellation(&self, _id: u32) -> Option<bool> {
        None
    }
    fn zone_key(&self) -> u32 {
        self.0.zone_key
    }
    fn position(&self) -> (f32, f32, f32) {
        self.0.position
    }
    fn labor_margin(&self) -> Option<i32> {
        None
    }
    fn expedition(&self) -> Option<(u32, u32)> {
        None
    }
    fn character_id(&self) -> Option<u32> {
        None
    }
}

/// World / data facts for `UnitReqs.Validate`.
pub struct EnvView<'a> {
    pub server: &'a GameServer,
    pub env: &'a QuestEnv,
}

impl ReqEnv for EnvView<'_> {
    fn zone_id_of_key(&self, zone_key: u32) -> Option<u32> {
        self.server
            .game_data
            .zones
            .zone_by_key(zone_key)
            .map(|z| z.id)
    }
    fn time_of_day(&self) -> f32 {
        self.env.time_of_day
    }
    fn inside_area_sphere(&self, sphere_id: u32, value2: u32, pos: (f32, f32, f32)) -> bool {
        self.server
            .systems
            .quests
            .spheres
            .is_inside_area_sphere(
                &self.server.game_data.systems.quests,
                sphere_id,
                value2,
                pos,
                0,
            )
            .is_some()
    }
}

/// `Inventory.GetAllItemsByTemplate(containers, templateId, -1, ..)` -> (found, units);
/// empty `containers` = Inventory + Equipment + Bank.
pub fn items_by_template(c: &Character, containers: &[u8], template_id: u32) -> (bool, i32) {
    let default = [slot_type::INVENTORY, slot_type::EQUIPMENT, slot_type::BANK];
    let list: &[u8] = if containers.is_empty() {
        &default
    } else {
        containers
    };
    let mut found = false;
    let mut units = 0i32;
    for st in list {
        if let Some(cont) = c.inventory.get(*st) {
            let (ids, n) = cont
                .lock()
                .expect("container poisoned")
                .items_by_template(template_id, -1);
            found |= !ids.is_empty();
            units = units.wrapping_add(n);
        }
    }
    (found, units)
}

/// `Inventory.GetItemsCount(templateId)`.
pub fn items_count(c: &Character, template_id: u32) -> i32 {
    let (found, units) = items_by_template(c, &[], template_id);
    if found {
        units
    } else {
        0
    }
}

/// `Inventory.CheckItems(SlotType.Inventory, templateId, count)`.
pub fn check_items(c: &Character, template_id: u32, count: i32) -> bool {
    let (_, units) = items_by_template(c, &[slot_type::INVENTORY], template_id);
    units >= count
}
