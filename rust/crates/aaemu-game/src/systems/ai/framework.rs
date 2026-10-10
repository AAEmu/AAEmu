//! `Models/Game/AI/v2/Framework/**` (`NpcAi`, `Behavior` bookkeeping, `BehaviorKind`,
//! `Transition`, `TransitionEvent`), `Models/Game/AI/v2/AiCharacters/**` (the per-kind state
//! machines) and `AiUtils.GetAiByType`.
//!
//! An [`NpcAi`] is driven through an [`AiCtx`] (server + owner + the AI state). Behaviors keep
//! their per-instance state in a [`BehaviorSlot`] and never hold a borrow of it across a call
//! back into the AI, so the C# re-entrancy (a behavior's `Tick` / `Exit` calling `Ai.GoToX()`,
//! which exits and enters behaviors before returning) is reproduced exactly: transitions are
//! immediate and nested, like `NpcAi.SetCurrentBehavior`.

use std::collections::{BTreeMap, HashMap, VecDeque};
use std::sync::{Arc, Weak};

use crate::models::npc::Npc;
use crate::server::GameServer;
use crate::systems::skills::controllers::Vec3;
use crate::world::{now_ms, Entity, NpcRef, WorldObject};

use super::behaviors;
use super::geodata::astar::PathNode;
use super::params::{AiCommands, AiParamType, AiParams, AiSkill};
use super::path_handler::{AiPathHandler, AiPathPoint};

/// `BehaviorKind`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, PartialOrd, Ord)]
pub enum BehaviorKind {
    Alert,
    AlmightyAttack,
    Attack,
    Dead,
    Despawning,
    DoNothing,
    Dummy,
    FollowPath,
    FollowUnit,
    HoldPosition,
    Idle,
    ReturnState,
    Roaming,
    RunCommandSet,
    Spawning,
    Talk,
    ArcherAttack,
    BigMonsterAttack,
    FlytrapAlert,
    FlytrapAttack,
    WildBoarAttack,
}

/// `TransitionEvent`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum TransitionEvent {
    OnAggroTargetChanged,
    OnTalk,
    OnReturnToTalkPos,
    OnNoAggroTarget,
    ReturnToIdlePos,
}

/// The `NpcAi` subclasses (`AiCharacters`).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum AiKind {
    AlmightyNpc,
    ArcherHoldPosition,
    ArcherRoaming,
    BigMonsterHoldPosition,
    BigMonsterRoaming,
    Default,
    Dummy,
    Flytrap,
    HoldPosition,
    Roaming,
    TowerDefenseAttacker,
    WildBoarHoldPosition,
    WildBoarRoaming,
}

impl AiKind {
    /// `AiUtils.GetAiByType(type, owner)` (`None` = `null`, no AI).
    pub fn by_type(t: Option<AiParamType>) -> Option<Self> {
        use AiParamType as T;
        Some(match t? {
            T::AlmightyNpc => Self::AlmightyNpc,
            T::ArcherHoldPosition => Self::ArcherHoldPosition,
            T::ArcherRoaming => Self::ArcherRoaming,
            T::BigMonsterRoaming => Self::BigMonsterRoaming,
            T::BigMonsterHoldPosition => Self::BigMonsterHoldPosition,
            T::Default => Self::Default,
            T::Dummy => Self::Dummy,
            T::Flytrap => Self::Flytrap,
            T::HoldPosition => Self::HoldPosition,
            T::Roaming => Self::Roaming,
            T::TowerDefenseAttacker => Self::TowerDefenseAttacker,
            T::WildBoarHoldPosition => Self::WildBoarHoldPosition,
            T::WildBoarRoaming => Self::WildBoarRoaming,
            _ => return None,
        })
    }

    /// The C# class name (`npc.Ai?.GetType().Name`).
    pub fn class_name(self) -> &'static str {
        match self {
            Self::AlmightyNpc => "AlmightyNpcAiCharacter",
            Self::ArcherHoldPosition => "ArcherHoldPositionAiCharacter",
            Self::ArcherRoaming => "ArcherRoamingAiCharacter",
            Self::BigMonsterHoldPosition => "BigMonsterHoldPositionAiCharacter",
            Self::BigMonsterRoaming => "BigMonsterRoamingAiCharacter",
            Self::Default => "DefaultAiCharacter",
            Self::Dummy => "DummyAiCharacter",
            Self::Flytrap => "FlytrapAiCharacter",
            Self::HoldPosition => "HoldPositionAiCharacter",
            Self::Roaming => "RoamingAiCharacter",
            Self::TowerDefenseAttacker => "TowerDefenseAttackerAiCharacter",
            Self::WildBoarHoldPosition => "WildBoarHoldPositionAiCharacter",
            Self::WildBoarRoaming => "WildBoarRoamingAiCharacter",
        }
    }

    /// The behavior `GoToCombat()` enters (the per-character override).
    fn combat_behavior(self) -> BehaviorKind {
        match self {
            Self::AlmightyNpc => BehaviorKind::AlmightyAttack,
            Self::ArcherHoldPosition | Self::ArcherRoaming => BehaviorKind::ArcherAttack,
            Self::BigMonsterHoldPosition | Self::BigMonsterRoaming => {
                BehaviorKind::BigMonsterAttack
            }
            Self::Flytrap => BehaviorKind::FlytrapAttack,
            Self::WildBoarHoldPosition | Self::WildBoarRoaming => BehaviorKind::WildBoarAttack,
            _ => BehaviorKind::Attack,
        }
    }
}

/// `ArcherAttackBehavior.Phase`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum ArcherPhase {
    #[default]
    Base,
    TryingMeleeSkill,
    UsedMeleeSkill,
    TryingRangedDefSkill,
    UsedRangedDefSkill,
    TryingMakeAGapSkill,
    NeedMakeAGap,
}

/// The per-instance fields of a behavior object (`Behavior` + `BaseCombatBehavior` + the
/// fields of the concrete class).
#[derive(Debug, Clone, Default)]
pub struct BehaviorSlot {
    // ---- Behavior ----
    /// `_delayEnd` (unix ms).
    pub delay_end: i64,
    /// `_nextTimeToDelay` (seconds).
    pub next_time_to_delay: f32,
    pub min_weapon_range: f32,
    pub max_weapon_range: f32,
    /// `Throttle()`'s `_lastTick` (unix ms; `i64::MIN` = `DateTime.MinValue`).
    pub last_tick: Option<i64>,
    /// `this.CheckInterval(...)` per call site (next allowed unix ms).
    pub intervals: HashMap<u8, i64>,
    // ---- BaseCombatBehavior ----
    pub strafe_during_delay: bool,
    pub pipe_name: String,
    pub phase_type: u32,
    pub combat_start_time: i64,
    pub skill_queue: VecDeque<AiSkill>,
    pub starting_skill_already_used: bool,
    // ---- concrete classes ----
    /// `_enter` (most behaviors).
    pub enter: bool,
    /// `_isInitialized` (Idle, HoldPosition, Talk).
    pub initialized: bool,
    /// `_greeted` (Idle, HoldPosition, Talk): player obj id -> unix ms.
    pub greeted: BTreeMap<u32, i64>,
    /// `HoldPositionBehavior._lastSkillCheck`.
    pub last_skill_check: i64,
    /// `HoldPositionBehavior._isFollowingNpc`.
    pub following_npc: bool,
    /// `AlertBehavior._oldRotation` (radians).
    pub old_rotation: (f32, f32, f32),
    /// `ReturnStateBehavior._timeoutTime`.
    pub timeout_time: i64,
    /// `RoamingBehavior._targetRoamPosition` / `_nextRoaming`.
    pub roam_target: Vec3,
    pub next_roaming: i64,
    /// `SpawningBehavior._usedSpawnSkills`.
    pub used_spawn_skills: bool,
    /// `ArcherAttackBehavior.Phase` / `MakeAGapCount`.
    pub archer_phase: ArcherPhase,
    pub make_a_gap_count: i32,
    /// `WildBoarAttackBehavior._currHealth` / `_combatStartSkill`.
    pub curr_health: f32,
    pub combat_start_skill: bool,
    /// `FlytrapAttackBehavior._aiParams` / `AlmightyAttackBehavior._aiParams`.
    pub ai_params: Option<Arc<AiParams>>,
}

/// `Npc.CheckInterval` call sites (the NPC object is the caller).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum NpcIntervalSite {
    AttackBehavior,
    AlmightyAttackBehavior,
}

/// `NpcAi` (one per NPC with an `AiFileId` the factory knows).
#[derive(Debug)]
pub struct NpcAi {
    pub kind: AiKind,
    /// `Owner` (`null` after `Npc.Delete`, or once the NPC object is gone).
    pub owner: Option<Weak<Entity<Npc>>>,
    pub owner_obj_id: u32,
    /// `ShouldTick` (kept for parity; the C# tick does not read it).
    pub should_tick: bool,
    pub already_targeted: bool,
    pub idle_position: Vec3,
    pub home_position: Vec3,
    /// `Param` (`null` until a behavior assigns `Owner.Template.AiParams`).
    pub param: Option<Arc<AiParams>>,
    /// `PathNode` (A* state for the geodata chase).
    pub path_node: PathNode,
    behaviors: BTreeMap<BehaviorKind, BehaviorSlot>,
    transitions: BTreeMap<BehaviorKind, Vec<(TransitionEvent, BehaviorKind)>>,
    current: Option<BehaviorKind>,
    default: Option<BehaviorKind>,
    /// `_nextAlertCheckTime` / `_alertEndTime` (unix ms, 0 = `MinValue`).
    pub next_alert_check_time: i64,
    pub alert_end_time: i64,
    /// `AiCommandsQueue`, `AiCurrentCommand`, `AiCurrentCommandStartTime`,
    /// `AiCurrentCommandRunTime` (ms, may be negative).
    pub commands_queue: VecDeque<AiCommands>,
    pub current_command: Option<AiCommands>,
    pub current_command_start_time: i64,
    pub current_command_run_time_ms: i64,
    pub path_handler: AiPathHandler,
    /// `AiFollowUnitObj`.
    pub follow_unit: Option<WorldObject>,
    pub ai_file_name: String,
    pub ai_file_name2: String,
    pub ai_skill_id: u32,
    pub ai_time_out: u32,
    /// `Owner.CheckInterval(...)` per call site.
    pub npc_intervals: HashMap<NpcIntervalSite, i64>,
    /// The NPC was spawned by `NpcSpawnerNpc.Spawn` (`npc.Simulation != null`).
    pub has_simulation: bool,
    /// `npc.RegisterNpcEvents()` ran (`NpcSpawnerNpc.Spawn`, before `GoToSpawn`).
    pub events_registered: bool,
    /// The NPC has been in a region at least once (used to detect `Npc.Delete`).
    pub was_in_world: bool,
}

/// The context every AI method runs with.
pub struct AiCtx<'a> {
    pub server: &'a GameServer,
    pub npc: NpcRef,
    pub obj: WorldObject,
    pub ai: &'a mut NpcAi,
}

impl NpcAi {
    /// `AiUtils.GetAiByType(type, owner)` + `NpcAi.Start()` (`Build` + `CheckValid`).
    pub fn new(kind: AiKind, owner: &NpcRef) -> Self {
        let mut ai = Self {
            kind,
            owner: Some(Arc::downgrade(owner)),
            owner_obj_id: owner.obj_id,
            should_tick: false,
            already_targeted: false,
            idle_position: Vec3::default(),
            home_position: Vec3::default(),
            param: None,
            path_node: PathNode::default(),
            behaviors: BTreeMap::new(),
            transitions: BTreeMap::new(),
            current: None,
            default: None,
            next_alert_check_time: 0,
            alert_end_time: 0,
            commands_queue: VecDeque::new(),
            current_command: None,
            current_command_start_time: 0,
            current_command_run_time_ms: 0,
            path_handler: AiPathHandler::default(),
            follow_unit: None,
            ai_file_name: String::new(),
            ai_file_name2: String::new(),
            ai_skill_id: 0,
            ai_time_out: 0,
            npc_intervals: HashMap::new(),
            has_simulation: false,
            events_registered: false,
            was_in_world: false,
        };
        ai.build();
        ai.check_valid();
        ai
    }

    fn add_behavior(&mut self, kind: BehaviorKind) {
        self.behaviors.insert(kind, BehaviorSlot::default());
    }

    fn add_transition(&mut self, from: BehaviorKind, on: TransitionEvent, to: BehaviorKind) {
        self.transitions.entry(from).or_default().push((on, to));
    }

    /// The `Build()` of every AI character.
    fn build(&mut self) {
        use BehaviorKind as B;
        use TransitionEvent as E;
        let k = self.kind;
        self.add_behavior(B::Spawning);
        if k == AiKind::Dummy {
            self.add_behavior(B::Dummy);
            self.default = Some(B::Dummy);
            self.add_behavior(B::Dead);
            self.add_behavior(B::Despawning);
            return;
        }
        let attack = k.combat_behavior();
        // The default ("idle") behavior of the character.
        let idle_kind = match k {
            AiKind::AlmightyNpc => B::Idle,
            AiKind::Default => B::DoNothing,
            AiKind::ArcherRoaming
            | AiKind::BigMonsterRoaming
            | AiKind::Roaming
            | AiKind::WildBoarRoaming => B::Roaming,
            _ => B::HoldPosition,
        };
        self.add_behavior(idle_kind);
        self.default = Some(idle_kind);
        self.add_transition(idle_kind, E::OnAggroTargetChanged, attack);
        // Roaming / DoNothing defaults have no ReturnToIdlePos transition.
        if !matches!(idle_kind, B::Roaming | B::DoNothing) {
            self.add_transition(idle_kind, E::ReturnToIdlePos, B::ReturnState);
        }
        self.add_transition(idle_kind, E::OnTalk, B::Talk);

        self.add_behavior(B::RunCommandSet);
        self.add_transition(B::RunCommandSet, E::OnAggroTargetChanged, attack);
        self.add_transition(B::RunCommandSet, E::OnTalk, B::Talk);
        self.add_behavior(B::Talk);
        self.add_transition(B::Talk, E::OnReturnToTalkPos, B::ReturnState);
        self.add_transition(B::Talk, E::OnAggroTargetChanged, attack);
        if k == AiKind::Flytrap {
            self.add_behavior(B::FlytrapAlert);
            self.add_transition(B::FlytrapAlert, E::OnAggroTargetChanged, attack);
            // FlytrapAttack has no OnNoAggroTarget transition, and Flytrap has no
            // FollowPath / FollowUnit behaviors.
            self.add_behavior(B::FlytrapAttack);
        } else {
            self.add_behavior(B::Alert);
            self.add_transition(B::Alert, E::OnAggroTargetChanged, attack);
            self.add_behavior(attack);
            self.add_transition(attack, E::OnNoAggroTarget, B::ReturnState);
            self.add_behavior(B::FollowPath);
            self.add_transition(B::FollowPath, E::OnTalk, B::Talk);
            self.add_behavior(B::FollowUnit);
            self.add_transition(B::FollowUnit, E::OnAggroTargetChanged, attack);
            self.add_transition(B::FollowUnit, E::OnTalk, B::Talk);
        }
        self.add_behavior(B::ReturnState);
        self.add_behavior(B::Dead);
        self.add_behavior(B::Despawning);
        // Every character except Almighty (whose default is Idle) also adds Idle at the end.
        if idle_kind != B::Idle {
            self.add_behavior(B::Idle);
        }
    }

    /// `CheckValid()`: logs transitions to behaviors the character does not have.
    fn check_valid(&self) {
        for list in self.transitions.values() {
            for (on, kind) in list {
                if !self.behaviors.contains_key(kind) {
                    tracing::error!(
                        "Transition is invalid. Type {kind:?} missing, while used in transition on {on:?}"
                    );
                }
            }
        }
    }

    /// `GetCurrentBehavior()`.
    pub fn current_behavior(&self) -> Option<BehaviorKind> {
        self.current
    }

    /// `GetAiBehaviorList()` keys.
    pub fn behavior_kinds(&self) -> Vec<BehaviorKind> {
        self.behaviors.keys().copied().collect()
    }

    /// `_defaultBehavior`.
    pub fn default_behavior(&self) -> Option<BehaviorKind> {
        self.default
    }

    /// The transitions of a behavior (tests / diagnostics).
    pub fn transitions_of(&self, kind: BehaviorKind) -> Vec<(TransitionEvent, BehaviorKind)> {
        self.transitions.get(&kind).cloned().unwrap_or_default()
    }

    pub fn slot(&mut self, kind: BehaviorKind) -> &mut BehaviorSlot {
        self.behaviors.entry(kind).or_default()
    }

    pub fn has_behavior(&self, kind: BehaviorKind) -> bool {
        self.behaviors.contains_key(&kind)
    }

    /// `HasPersistentAi()`.
    pub fn has_persistent_ai(&self) -> bool {
        !self.path_handler.ai_path_points.is_empty()
            || !self.path_handler.ai_path_points_remaining.is_empty()
            || self.follow_unit.is_some()
            || !self.commands_queue.is_empty()
    }

    /// The owner, while it exists.
    pub fn owner(&self) -> Option<NpcRef> {
        self.owner.as_ref().and_then(Weak::upgrade)
    }
}

impl<'a> AiCtx<'a> {
    /// The state of behavior `kind`.
    pub fn st(&mut self, kind: BehaviorKind) -> &mut BehaviorSlot {
        self.ai.slot(kind)
    }

    /// `SetCurrentBehavior(Behavior behavior)` (`None` = `null`).
    pub fn set_current(&mut self, behavior: Option<BehaviorKind>) {
        if self.ai.current == behavior {
            return;
        }
        tracing::trace!(
            "Npc {}:{} leaving behavior {:?}, Entering behavior {:?}",
            self.npc.data().unit.template_id,
            self.npc.obj_id,
            self.ai.current,
            behavior
        );
        if let Some(cur) = self.ai.current {
            behaviors::exit(self, cur);
        }
        self.ai.current = behavior;
        self.npc.data().ai_hold_or_idle = matches!(
            behavior,
            Some(BehaviorKind::HoldPosition | BehaviorKind::Idle)
        );
        if let Some(b) = behavior {
            behaviors::enter(self, b);
        }
    }

    /// `SetCurrentBehavior(BehaviorKind kind)` (ignored when the character lacks it).
    pub fn set_current_kind(&mut self, kind: BehaviorKind) {
        if !self.ai.has_behavior(kind) {
            tracing::trace!(
                "Trying to set Npc {}:{} current behavior, but it is not valid. Missing behavior: {kind:?}",
                self.npc.data().unit.template_id,
                self.npc.obj_id
            );
            return;
        }
        self.set_current(Some(kind));
    }

    /// `Transition(on)`.
    pub fn transition(&mut self, on: TransitionEvent) {
        let Some(cur) = self.ai.current else {
            return;
        };
        let Some(list) = self.ai.transitions.get(&cur) else {
            return;
        };
        let Some(&(_, kind)) = list.iter().find(|(e, _)| *e == on) else {
            return;
        };
        let target = self.ai.has_behavior(kind).then_some(kind);
        self.set_current(target);
    }

    /// `OnNoAggroTarget()`.
    pub fn on_no_aggro_target(&mut self) {
        self.transition(TransitionEvent::OnNoAggroTarget);
    }

    /// `OnAggroTargetChanged()`.
    pub fn on_aggro_target_changed(&mut self) {
        self.transition(TransitionEvent::OnAggroTargetChanged);
    }

    // ---- GoToX (forced state changes; the AI characters override a few) ----

    pub fn go_to_spawn(&mut self) {
        self.set_current_kind(BehaviorKind::Spawning);
    }

    pub fn go_to_idle(&mut self) {
        if self.ai.kind == AiKind::Dummy {
            return self.set_current_kind(BehaviorKind::Dummy);
        }
        self.set_current_kind(BehaviorKind::Idle);
    }

    pub fn go_to_run_command_set(&mut self) {
        if self.ai.kind == AiKind::Dummy {
            return self.set_current_kind(BehaviorKind::Dummy);
        }
        self.set_current_kind(BehaviorKind::RunCommandSet);
    }

    pub fn go_to_talk(&mut self) {
        self.set_current_kind(BehaviorKind::Talk);
    }

    pub fn go_to_alert(&mut self) {
        if self.ai.kind == AiKind::Flytrap {
            return self.set_current_kind(BehaviorKind::FlytrapAlert);
        }
        self.set_current_kind(BehaviorKind::Alert);
    }

    pub fn go_to_combat(&mut self) {
        let k = if self.ai.kind == AiKind::Dummy {
            BehaviorKind::Attack
        } else {
            self.ai.kind.combat_behavior()
        };
        self.set_current_kind(k);
    }

    pub fn go_to_follow_path(&mut self) {
        self.set_current_kind(BehaviorKind::FollowPath);
    }

    pub fn go_to_follow_unit(&mut self) {
        self.set_current_kind(BehaviorKind::FollowUnit);
    }

    pub fn go_to_return(&mut self) {
        self.set_current_kind(BehaviorKind::ReturnState);
    }

    pub fn go_to_dead(&mut self) {
        self.set_current_kind(BehaviorKind::Dead);
    }

    pub fn go_to_despawn(&mut self) {
        self.set_current_kind(BehaviorKind::Despawning);
    }

    pub fn go_to_dummy(&mut self) {
        self.set_current_kind(BehaviorKind::Dummy);
    }

    /// `GoToDefaultBehavior()`.
    pub fn go_to_default_behavior(&mut self) {
        if let Some(d) = self.ai.default {
            self.set_current(Some(d));
        }
    }

    /// `EnqueueAiCommands(list, addOnly)`.
    pub fn enqueue_ai_commands(&mut self, commands: &[AiCommands], add_only: bool) {
        self.ai.commands_queue.extend(commands.iter().cloned());
        if add_only {
            return;
        }
        if !self.ai.commands_queue.is_empty() {
            self.go_to_run_command_set();
        }
    }

    /// `LoadAiPathPoints(aiPathFileName, addToQueueOnly)`.
    pub fn load_ai_path_points(&mut self, name: &str, add_to_queue_only: bool) -> bool {
        let points = self.server.systems.ai.paths.load_ai_path_points(name);
        if points.is_empty() {
            return false;
        }
        let h = &mut self.ai.path_handler;
        if !add_to_queue_only {
            h.ai_path_points.clear();
            // Set to true to trigger initial loading, need to enable it again in the .path file
            h.ai_path_looping = true;
            h.ai_path_points.extend(points.iter().cloned());
        } else {
            h.ai_path_points_remaining.extend(points.iter().cloned());
        }
        true
    }

    /// Queues a path point (`AiPathPointsRemaining.Enqueue`).
    pub fn enqueue_path_point(&mut self, p: AiPathPoint) {
        self.ai.path_handler.ai_path_points_remaining.push_back(p);
    }

    /// `DoFollowDefaultNearestNpc()`.
    pub fn do_follow_default_nearest_npc(&mut self) -> bool {
        let follow = self
            .server
            .systems
            .npcs
            .spawns
            .npc_spawner_of(self.npc.obj_id)
            .map_or(0, |s| s.follow_npc);
        if follow > 0 {
            return self.do_follow_nearest_npc(follow, 100.0);
        }
        false
    }

    /// `DoFollowNearestNpc(followNpc, maxRange)`.
    pub fn do_follow_nearest_npc(&mut self, follow_npc: u32, max_range: f32) -> bool {
        let around = super::npc::around(self.server, &self.obj, max_range, true);
        let mut nearest: Option<WorldObject> = None;
        let mut closest = max_range;
        for n in around {
            let Some(npc) = n.as_npc() else { continue };
            if npc.data().unit.template_id != follow_npc {
                continue;
            }
            let dist = super::npc::distance_to(self.server, &n, &self.obj, false);
            if dist < closest {
                closest = dist;
                nearest = Some(n);
            }
        }
        if let Some(n) = nearest {
            self.ai.follow_unit = Some(n);
            self.go_to_follow_unit();
            return true;
        }
        false
    }

    /// `this.CheckInterval(interval)` of the NPC object at one call site.
    pub fn npc_check_interval(&mut self, site: NpcIntervalSite, interval_ms: i64) -> bool {
        let now = now_ms();
        let next = self.ai.npc_intervals.get(&site).copied().unwrap_or(now);
        if next > now {
            return false;
        }
        self.ai.npc_intervals.insert(site, now + interval_ms);
        true
    }

    /// `NpcAi.Tick(delta)` (`region_activity` = `Owner.Region.HasPlayerActivity()`).
    pub fn tick(&mut self, delta_ms: i64, region_activity: bool) {
        if !(self.ai.has_persistent_ai() || region_activity) {
            return;
        }
        if let Some(k) = self.ai.current {
            behaviors::tick(self, k, delta_ms);
        }
        // If aggro table is populated, check if current aggro targets need to be cleared
        let abusers: Vec<u32> = self
            .npc
            .data()
            .unit
            .skill_state
            .aggro_table
            .keys()
            .copied()
            .collect();
        if abusers.is_empty() {
            if super::npc::is_dead(&self.obj) || self.ai.current == Some(BehaviorKind::Dead) {
                return;
            }
            self.on_no_aggro_target();
            return;
        }
        let mut to_remove = Vec::new();
        for id in abusers {
            match self.server.world.object(id) {
                Some(abuser) => {
                    if crate::systems::skills::buffs::check_buff_tag(
                        self.server,
                        &abuser,
                        super::npc::tags::NO_FIGHT,
                    ) || crate::systems::skills::buffs::check_buff_tag(
                        self.server,
                        &abuser,
                        super::npc::tags::RETURNING,
                    ) || !super::combat::can_attack(self.server, &self.obj, &abuser)
                    {
                        to_remove.push(Ok(abuser));
                    }
                }
                None => to_remove.push(Err(id)),
            }
        }
        for u in to_remove {
            match u {
                Ok(abuser) => super::combat::clear_aggro_of_unit(self.server, &self.obj, &abuser),
                Err(id) => super::combat::clear_aggro_of_missing(self.server, &self.obj, id),
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::models::npc::Npc;
    use crate::world::Entity;

    fn ai(kind: AiKind) -> NpcAi {
        let npc = Entity::new(0x100, Npc::new(1));
        NpcAi::new(kind, &npc)
    }

    #[test]
    fn characters_build_the_csharp_state_machines() {
        use BehaviorKind as B;
        use TransitionEvent as E;
        let a = ai(AiKind::Roaming);
        assert_eq!(a.default_behavior(), Some(B::Roaming));
        assert_eq!(
            a.transitions_of(B::Roaming),
            vec![(E::OnAggroTargetChanged, B::Attack), (E::OnTalk, B::Talk)]
        );
        assert_eq!(
            a.transitions_of(B::Attack),
            vec![(E::OnNoAggroTarget, B::ReturnState)]
        );
        assert!(a.has_behavior(B::Idle));
        let h = ai(AiKind::HoldPosition);
        assert_eq!(h.default_behavior(), Some(B::HoldPosition));
        assert_eq!(
            h.transitions_of(B::HoldPosition),
            vec![
                (E::OnAggroTargetChanged, B::Attack),
                (E::ReturnToIdlePos, B::ReturnState),
                (E::OnTalk, B::Talk)
            ]
        );
        let d = ai(AiKind::Default);
        assert_eq!(d.default_behavior(), Some(B::DoNothing));
        assert_eq!(
            d.transitions_of(B::DoNothing),
            vec![(E::OnAggroTargetChanged, B::Attack), (E::OnTalk, B::Talk)]
        );
        let f = ai(AiKind::Flytrap);
        assert!(f.has_behavior(B::FlytrapAlert) && f.has_behavior(B::FlytrapAttack));
        assert!(!f.has_behavior(B::Alert) && !f.has_behavior(B::FollowPath));
        assert!(f.transitions_of(B::FlytrapAttack).is_empty());
        let m = ai(AiKind::AlmightyNpc);
        assert_eq!(m.default_behavior(), Some(B::Idle));
        assert!(m.has_behavior(B::AlmightyAttack) && !m.has_behavior(B::Attack));
        let w = ai(AiKind::WildBoarRoaming);
        assert_eq!(
            w.transitions_of(B::WildBoarAttack),
            vec![(E::OnNoAggroTarget, B::ReturnState)]
        );
        let du = ai(AiKind::Dummy);
        assert_eq!(
            du.behavior_kinds(),
            vec![B::Dead, B::Despawning, B::Dummy, B::Spawning]
        );
    }

    #[test]
    fn get_ai_by_type() {
        assert_eq!(
            AiKind::by_type(AiParamType::from_u32(15)),
            Some(AiKind::HoldPosition)
        );
        assert_eq!(AiKind::by_type(AiParamType::from_u32(11)), None);
        assert_eq!(AiKind::by_type(AiParamType::from_u32(12)), None);
        assert_eq!(AiKind::by_type(AiParamType::from_u32(14)), None);
        assert_eq!(
            AiKind::by_type(AiParamType::from_u32(27)),
            Some(AiKind::AlmightyNpc)
        );
    }
}
