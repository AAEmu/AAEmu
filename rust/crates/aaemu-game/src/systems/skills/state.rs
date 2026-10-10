//! Runtime state the skill code keeps per unit / per character (the C# `Unit.SkillTask`,
//! `AutoAttackTask`, `ActivePlotState`, `AggroTable`, `ConditionChance`, tolerance counters,
//! `Character.SkillCancelled` / `LastCast` / `IsInPostCast` / `IsInAggroListOf` ...), and the
//! `Skill` instance shared between a cast and its timers.
//!
//! These structs are embedded in `UnitBase` / `Character` (under the `skills` markers), so they
//! are `Clone + PartialEq`; shared handles compare by pointer.

use std::collections::{BTreeMap, HashMap};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Mutex, MutexGuard};

use super::data::SkillTemplate;
use super::plots::PlotState;
use super::types::SkillHitType;

/// A cancellation flag shared by a scheduled task and its owner (`Task.Cancel()`).
#[derive(Debug, Clone, Default)]
pub struct CancelToken(Arc<AtomicBool>);

impl CancelToken {
    pub fn new() -> Self {
        Self::default()
    }
    pub fn cancel(&self) {
        self.0.store(true, Ordering::SeqCst);
    }
    pub fn is_cancelled(&self) -> bool {
        self.0.load(Ordering::SeqCst)
    }
}

impl PartialEq for CancelToken {
    fn eq(&self, other: &Self) -> bool {
        Arc::ptr_eq(&self.0, &other.0)
    }
}

/// The mutable part of a `Skill` instance.
#[derive(Debug, Default)]
pub struct SkillRunState {
    pub tl_id: u16,
    pub cancelled: bool,
    /// `HitTypes` (target obj id -> hit type, `TryAdd` semantics where the C# uses it).
    pub hit_types: HashMap<u32, SkillHitType>,
    /// `InitialTarget?.ObjId` (`u32::MAX` = a position unit).
    pub initial_target: u32,
    pub bypass_gcd: bool,
    /// `CastTimeMultiplier`.
    pub cast_time_multiplier: f32,
    /// `AutoAttackIndex`.
    pub auto_attack_index: i32,
    /// `ActivePlotState`.
    pub active_plot: Option<Arc<PlotState>>,
}

/// `Skill`: a template + level and the per-use state, shared by the cast and its tasks.
pub struct SkillRun {
    pub id: u32,
    pub template: Arc<SkillTemplate>,
    pub level: u8,
    state: Mutex<SkillRunState>,
    /// `Callback` (an arbitrary action run at the end of the skill).
    pub callback: Mutex<Option<Box<dyn FnOnce() + Send>>>,
}

pub type SkillRef = Arc<SkillRun>;

impl std::fmt::Debug for SkillRun {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("SkillRun")
            .field("id", &self.id)
            .field("level", &self.level)
            .field("state", &self.state)
            .finish()
    }
}

impl SkillRun {
    /// `new Skill(template)` (level 1) or `new Skill(template, owner)` (`level` computed by the
    /// caller from the owner's ability level).
    pub fn new(template: Arc<SkillTemplate>, level: u8) -> SkillRef {
        Arc::new(Self {
            id: template.id,
            template,
            level,
            state: Mutex::new(SkillRunState {
                cast_time_multiplier: 1.0,
                ..SkillRunState::default()
            }),
            callback: Mutex::new(None),
        })
    }

    /// `Skill(template, owner)` level: `(abLevel - AbilityLevel) / LevelStep + 1`.
    pub fn level_for(template: &SkillTemplate, ab_level: i32) -> u8 {
        if template.level_step > 0 {
            ((ab_level - template.ability_level) / template.level_step + 1) as u8
        } else {
            1
        }
    }

    pub fn state(&self) -> MutexGuard<'_, SkillRunState> {
        self.state.lock().expect("skill state poisoned")
    }

    pub fn tl_id(&self) -> u16 {
        self.state().tl_id
    }

    pub fn cancelled(&self) -> bool {
        self.state().cancelled
    }

    /// `SkillMissed(objId)` (an unknown target counts as missed, with an error log).
    pub fn skill_missed(&self, obj_id: u32) -> bool {
        match self.state().hit_types.get(&obj_id) {
            Some(h) => h.is_miss(),
            None => {
                tracing::error!("Unit[{obj_id}] was not found in the CbtDiceRolls.");
                true
            }
        }
    }

    /// `HitTypes.TryGetValue(objId)`.
    pub fn hit_type(&self, obj_id: u32) -> Option<SkillHitType> {
        self.state().hit_types.get(&obj_id).copied()
    }

    /// Runs and clears `Callback`.
    pub fn invoke_callback(&self) {
        let cb = self.callback.lock().expect("callback poisoned").take();
        if let Some(cb) = cb {
            cb();
        }
    }
}

/// `SkillTask` kinds a unit can hold (`Unit.SkillTask`).
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum SkillTaskKind {
    /// `CastTask`.
    Cast,
    /// `EndChannelingTask` (with the channeling doodad obj id, 0 = none).
    EndChanneling { channel_doodad: u32 },
}

/// `Unit.SkillTask`.
#[derive(Debug, Clone)]
pub struct SkillTaskSlot {
    pub kind: SkillTaskKind,
    pub skill: SkillRef,
    pub cancel: CancelToken,
}

impl PartialEq for SkillTaskSlot {
    fn eq(&self, other: &Self) -> bool {
        self.kind == other.kind
            && Arc::ptr_eq(&self.skill, &other.skill)
            && self.cancel == other.cancel
    }
}

/// `Unit.AutoAttackTask` (`UseAutoAttackSkillTask`).
#[derive(Debug, Clone)]
pub struct AutoAttackSlot {
    pub skill: SkillRef,
    pub cancel: CancelToken,
}

impl PartialEq for AutoAttackSlot {
    fn eq(&self, other: &Self) -> bool {
        Arc::ptr_eq(&self.skill, &other.skill) && self.cancel == other.cancel
    }
}

/// `AggroKind`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
#[repr(u8)]
pub enum AggroKind {
    Damage = 0,
    Heal = 1,
    Etc = 2,
}

/// `Aggro` (one entry of `Unit.AggroTable`, keyed by the abuser's obj id).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct Aggro {
    pub damage_aggro: i32,
    pub heal_aggro: i32,
}

impl Aggro {
    /// `TotalAggro`.
    pub fn total(&self) -> i32 {
        self.damage_aggro.wrapping_add(self.heal_aggro)
    }

    /// `AddAggro(kind, amount)` (heal aggro counts 60 %).
    pub fn add(&mut self, kind: AggroKind, amount: i32) {
        match kind {
            AggroKind::Damage => self.damage_aggro = self.damage_aggro.wrapping_add(amount),
            AggroKind::Heal => {
                self.heal_aggro = self
                    .heal_aggro
                    .wrapping_add((amount as f32 * 0.6f32) as i32)
            }
            AggroKind::Etc => {}
        }
    }
}

/// `Tagging` (`CharacterTagging`): who earned the kill credit.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Tagging {
    /// `Tagger` (character obj id, 0 = none).
    pub tagger: u32,
    /// `TagTeam` (team id, 0 = none).
    pub tag_team: u32,
    /// `_taggers`: damage per character obj id, in first-hit order.
    pub taggers: Vec<(u32, i32)>,
}

/// `BuffToleranceCounter`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ToleranceCounter {
    pub tolerance_id: u32,
    /// (step id, hit chance, time reduction).
    pub current_step: (u32, u32, u32),
    pub last_step_ms: i64,
}

/// Per-unit skill / combat state (`UnitBase.skill_state`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct UnitSkillState {
    /// `SkillTask`.
    pub skill_task: Option<SkillTaskSlot>,
    /// `AutoAttackTask`.
    pub auto_attack: Option<AutoAttackSlot>,
    /// `ActivePlotState`.
    pub active_plot: Option<PlotStateRef>,
    /// `ConditionChance`.
    pub condition_chance: bool,
    /// `AggroTable` (insertion ordered by obj id of the abuser).
    pub aggro_table: BTreeMap<u32, Aggro>,
    /// `CharacterTagging`.
    pub tagging: Tagging,
    /// `Buffs._toleranceCounters` (by tolerance id).
    pub tolerance_counters: HashMap<u32, ToleranceCounter>,
    /// `ActiveSkillController` (the running controller; compares by pointer).
    pub active_skill_controller: Option<super::controllers::SkillControllerRef>,
    /// `GetAbLevel` override for units whose ability levels are not the unit level (set by the
    /// owner of the unit kind; characters compute it from their abilities).
    pub ab_level_override: Option<u8>,
    /// `Unit.DisplacedUntil` (unix ms, 0 = `null`): set by knock back / impulse / blink effects
    /// so the NPC AI does not walk the unit back while the client plays the displacement.
    pub displaced_until_ms: i64,
}

/// `ActivePlotState` handle (compares by pointer).
#[derive(Debug, Clone)]
pub struct PlotStateRef(pub Arc<PlotState>);

impl PartialEq for PlotStateRef {
    fn eq(&self, other: &Self) -> bool {
        Arc::ptr_eq(&self.0, &other.0)
    }
}

/// Per-character skill / combat state (`Character.skill_char`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct CharacterSkillState {
    /// `SkillCancelled`.
    pub skill_cancelled: bool,
    /// `LastCast` (unix ms).
    pub last_cast_ms: i64,
    /// `IsInPostCast`.
    pub is_in_post_cast: bool,
    /// `IgnoreSkillCooldowns` (GM flag).
    pub ignore_skill_cooldowns: bool,
    /// `IsInAggroListOf` (npc obj ids).
    pub is_in_aggro_list_of: Vec<u32>,
    /// `AssaultedBy` / `AssaultOn` (character ids).
    pub assaulted_by: Vec<u32>,
    pub assault_on: Vec<u32>,
    /// `_hostilePlayers` (attacker obj id -> last hostile action, unix ms).
    pub hostile_players: Vec<(u32, i64)>,
    /// `ResurrectHpPercent` / `ResurrectMpPercent` (set by the `Resurrection` special effect).
    pub resurrect_hp_percent: u32,
    pub resurrect_mp_percent: u32,
}

/// A process-wide counter for task generations (dispel tasks of refreshed buffs become no-ops).
static TASK_GEN: AtomicU64 = AtomicU64::new(1);

/// Next task generation.
pub fn next_task_gen() -> u64 {
    TASK_GEN.fetch_add(1, Ordering::Relaxed)
}
