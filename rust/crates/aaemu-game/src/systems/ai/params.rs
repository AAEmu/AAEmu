//! `Models/Game/AI/v2/Params/**`: `AiParams` (+ `CreateByType`) and the per-kind parameter
//! classes (`AlmightyNpcAiParams` with `AiSkillList` / `SkillList` / `AiSkill`,
//! `ArcherAiParams` / `ArcherCombatSkill`, `BigMonsterAiParams` / `BigMonsterCombatSkill`,
//! `FlytrapAiParams` / `FlytrapCombatSkill`, `WildBoarAiParams` / `WildBoarAiSpurtSkill`), the
//! `ai_commands` / `ai_command_sets` / `ai_events` / `npc_chat_bubbles` rows and the AI enums.
//!
//! The parameter strings are Lua (see [`super::lua`]); every `Parse` reads them exactly like the
//! C# does (`GetObjectFromPath` + `System.Convert`), so an exception anywhere drops the whole
//! entry (`AiGameData.Load` logs "Impossible to parse npc_ai_params").
//!
//! C# quirk mirrored: the parameter object is shared by every NPC of the template
//! (`Ai.Param = Ai.Owner.Template.AiParams`) and `RefreshSkillQueue` writes
//! `RestorationOnReturn` / `GoReturnState` into it, so those two are atomics here.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;

use super::lua::{
    convert_to_bool, convert_to_f32, convert_to_i32, convert_to_string, convert_to_u32,
    ConvertError, LuaError, LuaState, LuaTable, LuaValue,
};

/// `AiParamType` (`npcs.ai_file_id`).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, PartialOrd, Ord)]
pub enum AiParamType {
    None = 0,
    SiegeWeaponPlace = 11,
    Bomb = 12,
    Roaming = 13,
    HoldPosition = 15,
    TowerDefenseAttacker = 17,
    Flytrap = 18,
    BigMonsterHoldPosition = 19,
    BigMonsterRoaming = 20,
    ArcherHoldPosition = 21,
    ArcherRoaming = 22,
    WildBoarHoldPosition = 23,
    WildBoarRoaming = 24,
    Dummy = 25,
    Default = 26,
    AlmightyNpc = 27,
}

impl AiParamType {
    /// `(AiParamType)value` for the defined values (`None` for an undefined enum value, which
    /// the C# switches treat as `default`).
    pub fn from_u32(v: u32) -> Option<Self> {
        Some(match v {
            0 => Self::None,
            11 => Self::SiegeWeaponPlace,
            12 => Self::Bomb,
            13 => Self::Roaming,
            15 => Self::HoldPosition,
            17 => Self::TowerDefenseAttacker,
            18 => Self::Flytrap,
            19 => Self::BigMonsterHoldPosition,
            20 => Self::BigMonsterRoaming,
            21 => Self::ArcherHoldPosition,
            22 => Self::ArcherRoaming,
            23 => Self::WildBoarHoldPosition,
            24 => Self::WildBoarRoaming,
            25 => Self::Dummy,
            26 => Self::Default,
            27 => Self::AlmightyNpc,
            _ => return None,
        })
    }
}

/// `AiCommandCategory` (`ai_commands.cmd_id`).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum AiCommandCategory {
    FollowUnit,
    FollowPath,
    UseSkill,
    Timeout,
    /// Any other value (`throw new NotSupportedException` when executed).
    Other(u32),
}

impl AiCommandCategory {
    pub fn from_u32(v: u32) -> Self {
        match v {
            1 => Self::FollowUnit,
            2 => Self::FollowPath,
            3 => Self::UseSkill,
            4 => Self::Timeout,
            o => Self::Other(o),
        }
    }
}

/// `AiCommands` (`ai_commands` row).
#[derive(Debug, Clone, PartialEq)]
pub struct AiCommands {
    pub id: u32,
    pub cmd_set_id: u32,
    pub cmd_id: AiCommandCategory,
    pub param1: u32,
    pub param2: String,
}

/// `AiCommandSets` (`ai_command_sets` row).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AiCommandSets {
    pub id: u32,
    pub name: String,
    pub can_interact: bool,
}

/// `AiEvent` (`ai_events` row; `ignore_time` is read as the weight).
#[derive(Debug, Clone, PartialEq)]
pub struct AiEvent {
    pub id: i32,
    pub ignore_category_id: i32,
    pub weight: f32,
    pub event_name: String,
    pub npc_id: i32,
    pub or_unit_reqs: bool,
    pub skill_id: i32,
}

/// `NpcChatBubble` (`npc_chat_bubbles` row).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NpcChatBubble {
    pub id: i32,
    pub ai_event_id: i32,
    pub bubble: String,
}

/// `AiSkill`.
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct AiSkill {
    pub skill_id: u32,
    pub strafe: bool,
    pub delay: f32,
}

/// `SkillList`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct SkillList {
    pub use_type: u32,
    pub dice: i32,
    pub health_range_min: i32,
    pub health_range_max: i32,
    pub time_range_start: f32,
    pub time_range_end: f32,
    pub skills: Vec<AiSkill>,
}

/// `AiSkillList`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct AiSkillList {
    pub pipe_name: String,
    pub phase_type: u32,
    pub use_type: u32,
    pub dice: i32,
    pub health_range_min: i32,
    pub health_range_max: i32,
    pub time_range_start: f32,
    pub time_range_end: f32,
    pub restoration: bool,
    pub go_return: bool,
    pub start_ai_skills: Vec<AiSkill>,
    pub skill_lists: Vec<SkillList>,
}

/// `ArcherCombatSkill`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct ArcherCombatSkill {
    pub melee: Vec<u32>,
    pub make_a_gap: Vec<u32>,
    pub ranged_def: Vec<u32>,
    pub ranged_strong: Vec<u32>,
}

/// `BigMonsterCombatSkill`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct BigMonsterCombatSkill {
    pub skill_type: u32,
    pub skill_delay: f32,
    pub strafe_during_delay: bool,
    pub health_range_min: i32,
    pub health_range_max: i32,
}

/// `FlytrapCombatSkill`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct FlytrapCombatSkill {
    pub melee: Vec<u32>,
    pub ranged: Vec<u32>,
}

/// `WildBoarAiSpurtSkill`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct WildBoarAiSpurtSkill {
    pub skill_type: u32,
    pub health_condition: u32,
}

/// The kind-specific half of an `AiParams` subclass.
#[derive(Debug, Clone, PartialEq)]
pub enum AiParamsKind {
    Almighty {
        msgs: Vec<String>,
        idle_ai: String,
        can_change_ai_unit_attr: i32,
        ai_phase_change_type: i32,
        ai_skill_lists: Vec<AiSkillList>,
        ai_path_skill_lists: Vec<AiSkillList>,
        ai_path_damage_skill_lists: Vec<AiSkillList>,
    },
    Archer {
        combat_skills: Vec<ArcherCombatSkill>,
    },
    BigMonster {
        combat_skills: Vec<BigMonsterCombatSkill>,
    },
    Flytrap {
        attack_end_distance: f32,
        combat_skills: FlytrapCombatSkill,
    },
    WildBoar {
        on_combat_start_skills: Vec<u32>,
        on_spurt_skills: Vec<WildBoarAiSpurtSkill>,
    },
}

/// `AiParams` (+ its subclass data).
#[derive(Debug)]
pub struct AiParams {
    pub alert_duration: f32,
    pub alert_to_attack: bool,
    pub alert_safe_target_remember_time: f32,
    pub always_teleport_on_return: bool,
    pub max_make_a_gap_count: i32,
    pub melee_attack_range: f32,
    pub prefered_combat_dist: f32,
    /// `RestorationOnReturn` (written by `RefreshSkillQueue`, shared by the template's NPCs).
    restoration_on_return: AtomicBool,
    /// `GoReturnState` (idem).
    go_return_state: AtomicBool,
    pub kind: AiParamsKind,
}

impl Clone for AiParams {
    fn clone(&self) -> Self {
        Self {
            alert_duration: self.alert_duration,
            alert_to_attack: self.alert_to_attack,
            alert_safe_target_remember_time: self.alert_safe_target_remember_time,
            always_teleport_on_return: self.always_teleport_on_return,
            max_make_a_gap_count: self.max_make_a_gap_count,
            melee_attack_range: self.melee_attack_range,
            prefered_combat_dist: self.prefered_combat_dist,
            restoration_on_return: AtomicBool::new(self.restoration_on_return()),
            go_return_state: AtomicBool::new(self.go_return_state()),
            kind: self.kind.clone(),
        }
    }
}

impl PartialEq for AiParams {
    fn eq(&self, o: &Self) -> bool {
        self.alert_duration == o.alert_duration
            && self.alert_to_attack == o.alert_to_attack
            && self.alert_safe_target_remember_time == o.alert_safe_target_remember_time
            && self.always_teleport_on_return == o.always_teleport_on_return
            && self.max_make_a_gap_count == o.max_make_a_gap_count
            && self.melee_attack_range == o.melee_attack_range
            && self.prefered_combat_dist == o.prefered_combat_dist
            && self.restoration_on_return() == o.restoration_on_return()
            && self.go_return_state() == o.go_return_state()
            && self.kind == o.kind
    }
}

/// Why a parameter string could not be parsed (the C# exception).
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
pub enum ParamsError {
    #[error("{0}")]
    Lua(String),
    #[error("{0}")]
    Convert(String),
}

impl From<LuaError> for ParamsError {
    fn from(e: LuaError) -> Self {
        Self::Lua(e.0)
    }
}

impl From<ConvertError> for ParamsError {
    fn from(e: ConvertError) -> Self {
        Self::Convert(e.to_string())
    }
}

type PResult<T> = Result<T, ParamsError>;

/// The `AiLua` constructor's constants.
const AI_LUA_CONSTANTS: [(&str, i64); 8] = [
    ("USE_SEQUENCE", 1),
    ("USE_RANDOM", 2),
    ("AGGRO_TOTAL", 1),
    ("AGGRO_HEAL", 2),
    ("PHASE_TYPE_NONE", 0),
    ("PHASE_TYPE_SEQUENCE", 1),
    ("PHASE_DRAGON_GROUND", 1),
    ("PHASE_DRAGON_HOVERING", 2),
];

impl AiParams {
    fn with_defaults(kind: AiParamsKind) -> Self {
        Self {
            alert_duration: 3.0,
            alert_to_attack: true,
            alert_safe_target_remember_time: 5.0,
            always_teleport_on_return: false,
            max_make_a_gap_count: 3,
            melee_attack_range: 4.0,
            prefered_combat_dist: 0.0,
            restoration_on_return: AtomicBool::new(true),
            go_return_state: AtomicBool::new(true),
            kind,
        }
    }

    pub fn restoration_on_return(&self) -> bool {
        self.restoration_on_return.load(Ordering::Relaxed)
    }

    pub fn set_restoration_on_return(&self, v: bool) {
        self.restoration_on_return.store(v, Ordering::Relaxed);
    }

    pub fn go_return_state(&self) -> bool {
        self.go_return_state.load(Ordering::Relaxed)
    }

    pub fn set_go_return_state(&self, v: bool) {
        self.go_return_state.store(v, Ordering::Relaxed);
    }

    /// `AiParams.CreateByType(type, aiParamsString)` (`Ok(None)` = the C# `null`).
    pub fn create_by_type(t: Option<AiParamType>, data: &str) -> PResult<Option<Self>> {
        use AiParamType as T;
        Ok(Some(match t {
            Some(T::AlmightyNpc) => Self::almighty(data)?,
            Some(T::ArcherHoldPosition | T::ArcherRoaming) => Self::archer(data)?,
            Some(T::BigMonsterHoldPosition | T::BigMonsterRoaming) => Self::big_monster(data)?,
            Some(T::Flytrap) => Self::flytrap(data)?,
            Some(T::WildBoarRoaming | T::WildBoarHoldPosition) => Self::wild_boar(data)?,
            _ => return Ok(None),
        }))
    }

    /// `using var aiParams = new AiLua(); aiParams.DoString($"data = {{\n{data}\n}}")`.
    fn run(data: &str) -> PResult<LuaState> {
        let mut l = LuaState::with_globals(&AI_LUA_CONSTANTS);
        l.do_string(&format!("data = {{\n{data}\n}}"))?;
        Ok(l)
    }

    /// The `// general` block every subclass' `Parse` starts with.
    fn parse_general(&mut self, l: &LuaState) -> PResult<()> {
        let obj = |p: &str| l.get_object_from_path(p);
        if obj("data.alertDuration").is_some() {
            self.alert_duration = l.get_number("data.alertDuration")? as f32;
        }
        if let Some(v) = obj("data.alertToAttack") {
            self.alert_to_attack = convert_to_bool(&v)?;
        }
        if obj("data.alertSafeTargetRememberTime").is_some() {
            self.alert_safe_target_remember_time =
                l.get_number("data.alertSafeTargetRememberTime")? as f32;
        }
        if let Some(v) = obj("data.alwaysTeleportOnReturn") {
            self.always_teleport_on_return = convert_to_bool(&v)?;
        }
        if obj("data.maxMakeAGapCount").is_some() {
            self.max_make_a_gap_count = l.get_integer("data.maxMakeAGapCount")?;
        }
        if obj("data.meleeAttackRange").is_some() {
            self.melee_attack_range = l.get_number("data.meleeAttackRange")? as f32;
        }
        if obj("data.preferedCombatDist").is_some() {
            self.prefered_combat_dist = l.get_number("data.preferedCombatDist")? as f32;
        }
        if let Some(v) = obj("data.restorationOnReturn") {
            self.set_restoration_on_return(convert_to_bool(&v)?);
        }
        Ok(())
    }

    /// `new AlmightyNpcAiParams(data)`.
    pub fn almighty(data: &str) -> PResult<Self> {
        let l = Self::run(data)?;
        let mut p = Self::with_defaults(AiParamsKind::Almighty {
            msgs: Vec::new(),
            idle_ai: "hold_position".into(),
            can_change_ai_unit_attr: 0,
            ai_phase_change_type: 0,
            ai_skill_lists: Vec::new(),
            ai_path_skill_lists: Vec::new(),
            ai_path_damage_skill_lists: Vec::new(),
        });
        p.parse_general(&l)?;
        let ai_phase_change_type = l.get_integer("data.aiPhaseChangeType")?;
        let can_change_ai_unit_attr = l.get_integer("data.canChangeAiUnitAttr")?;
        let idle_ai = l.get_string_cast("data.idle_ai")?.unwrap_or_default();
        let lists = |path: &str| -> PResult<Vec<AiSkillList>> {
            let mut out = Vec::new();
            if let Some(t) = l.get_table(path)? {
                for v in t.values() {
                    if let LuaValue::Table(st) = v {
                        out.push(AiSkillList::parse_lua(st)?);
                    }
                }
            }
            Ok(out)
        };
        let ai_skill_lists = lists("data.aiSkillLists")?;
        let ai_path_skill_lists = lists("data.aiPathSkillLists")?;
        let ai_path_damage_skill_lists = lists("data.aiPathDamageSkillLists")?;
        p.kind = AiParamsKind::Almighty {
            msgs: Vec::new(),
            idle_ai,
            can_change_ai_unit_attr,
            ai_phase_change_type,
            ai_skill_lists,
            ai_path_skill_lists,
            ai_path_damage_skill_lists,
        };
        Ok(p)
    }

    /// `new ArcherAiParams(data)`.
    pub fn archer(data: &str) -> PResult<Self> {
        let l = Self::run(data)?;
        let mut p = Self::with_defaults(AiParamsKind::Archer {
            combat_skills: Vec::new(),
        });
        p.parse_general(&l)?;
        let mut combat_skills = Vec::new();
        if let Some(t) = l.get_table("data.combatSkills")? {
            combat_skills.push(ArcherCombatSkill::parse_lua(&t)?);
        }
        p.kind = AiParamsKind::Archer { combat_skills };
        Ok(p)
    }

    /// `new BigMonsterAiParams(data)`.
    pub fn big_monster(data: &str) -> PResult<Self> {
        let l = Self::run(data)?;
        let mut p = Self::with_defaults(AiParamsKind::BigMonster {
            combat_skills: Vec::new(),
        });
        p.parse_general(&l)?;
        let mut combat_skills = Vec::new();
        if let Some(t) = l.get_table("data.combatSkills")? {
            for v in t.values() {
                if let LuaValue::Table(st) = v {
                    combat_skills.push(BigMonsterCombatSkill::parse_lua(st)?);
                }
            }
        }
        p.kind = AiParamsKind::BigMonster { combat_skills };
        Ok(p)
    }

    /// `new FlytrapAiParams(data)`.
    pub fn flytrap(data: &str) -> PResult<Self> {
        let l = Self::run(data)?;
        let mut p = Self::with_defaults(AiParamsKind::Flytrap {
            attack_end_distance: 50.0,
            combat_skills: FlytrapCombatSkill::default(),
        });
        p.parse_general(&l)?;
        let mut attack_end_distance = 50.0;
        if l.get_object_from_path("data.attackEndDistance").is_some() {
            attack_end_distance = l.get_number("data.attackEndDistance")? as f32;
        }
        let mut combat_skills = FlytrapCombatSkill::default();
        if let Some(t) = l.get_table("data.combatSkills")? {
            combat_skills = FlytrapCombatSkill::parse_lua(&t)?;
        }
        p.kind = AiParamsKind::Flytrap {
            attack_end_distance,
            combat_skills,
        };
        Ok(p)
    }

    /// `new WildBoarAiParams(data)`.
    pub fn wild_boar(data: &str) -> PResult<Self> {
        let l = Self::run(data)?;
        let mut p = Self::with_defaults(AiParamsKind::WildBoar {
            on_combat_start_skills: Vec::new(),
            on_spurt_skills: Vec::new(),
        });
        p.parse_general(&l)?;
        let mut on_combat_start_skills = Vec::new();
        if let Some(t) = l.get_table("data.onCombatStartSkill")? {
            for v in t.values() {
                on_combat_start_skills.push(convert_to_u32(v)?);
            }
        }
        let mut on_spurt_skills = Vec::new();
        if let Some(t) = l.get_table("data.onSpurtSkill")? {
            for v in t.values() {
                if let LuaValue::Table(st) = v {
                    on_spurt_skills.push(WildBoarAiSpurtSkill {
                        skill_type: convert_to_u32(st.field("skillType"))?,
                        health_condition: convert_to_u32(st.field("healthCondition"))?,
                    });
                }
            }
        }
        p.kind = AiParamsKind::WildBoar {
            on_combat_start_skills,
            on_spurt_skills,
        };
        Ok(p)
    }

    /// The fresh instance a behavior creates when the template has no parameters
    /// (`Ai.Param ??= new XxxAiParams("")`).
    pub fn empty(t: AiParamType) -> Arc<Self> {
        // An empty data string always parses.
        Arc::new(
            Self::create_by_type(Some(t), "")
                .ok()
                .flatten()
                .unwrap_or_else(|| {
                    Self::with_defaults(AiParamsKind::Archer {
                        combat_skills: Vec::new(),
                    })
                }),
        )
    }
}

fn ai_skill_from(t: &LuaTable) -> PResult<AiSkill> {
    Ok(AiSkill {
        skill_id: convert_to_u32(t.field("skillType"))?,
        delay: convert_to_f32(t.field("delay"))?,
        strafe: convert_to_bool(t.field("strafe"))?,
    })
}

/// `healthRange` / `timeRange` pairs (`table[1]`, `table[2]`).
fn range_i32(t: &LuaTable, name: &str) -> PResult<Option<(i32, i32)>> {
    match t.field(name) {
        LuaValue::Table(r) => Ok(Some((convert_to_i32(r.at(1))?, convert_to_i32(r.at(2))?))),
        _ => Ok(None),
    }
}

fn range_f32(t: &LuaTable, name: &str) -> PResult<Option<(f32, f32)>> {
    match t.field(name) {
        LuaValue::Table(r) => Ok(Some((convert_to_f32(r.at(1))?, convert_to_f32(r.at(2))?))),
        _ => Ok(None),
    }
}

fn u32_list(t: &LuaTable, name: &str) -> PResult<Vec<u32>> {
    let mut out = Vec::new();
    if let LuaValue::Table(l) = t.field(name) {
        for v in l.values() {
            out.push(convert_to_u32(v)?);
        }
    }
    Ok(out)
}

impl AiSkillList {
    /// `AiSkillList.ParseLua(table)`.
    pub fn parse_lua(t: &LuaTable) -> PResult<Self> {
        let mut s = Self {
            pipe_name: convert_to_string(t.field("pipeName")),
            phase_type: convert_to_u32(t.field("phaseType"))?,
            use_type: convert_to_u32(t.field("useType"))?,
            dice: convert_to_i32(t.field("dice"))?,
            ..Self::default()
        };
        if let Some((a, b)) = range_i32(t, "healthRange")? {
            s.health_range_min = a;
            s.health_range_max = b;
        }
        if let Some((a, b)) = range_f32(t, "timeRange")? {
            s.time_range_start = a;
            s.time_range_end = b;
        }
        if let LuaValue::Table(o) = t.field("options") {
            s.restoration = convert_to_bool(o.field("restoration"))?;
            s.go_return = convert_to_bool(o.field("goReturn"))?;
        }
        if let LuaValue::Table(st) = t.field("startAiSkill") {
            s.start_ai_skills.push(ai_skill_from(st)?);
        }
        if let LuaValue::Table(lists) = t.field("skillLists") {
            for v in lists.values() {
                if let LuaValue::Table(sl) = v {
                    s.skill_lists.push(SkillList::parse_lua(sl)?);
                }
            }
        } else if let LuaValue::Table(skills) = t.field("skills") {
            let mut list = SkillList::default();
            for v in skills.values() {
                if let LuaValue::Table(st) = v {
                    list.skills.push(ai_skill_from(st)?);
                }
            }
            s.skill_lists.push(list);
        } else {
            s.skill_lists.push(SkillList {
                skills: vec![ai_skill_from(t)?],
                ..SkillList::default()
            });
        }
        Ok(s)
    }
}

impl SkillList {
    /// `SkillList.ParseLua(table)`.
    pub fn parse_lua(t: &LuaTable) -> PResult<Self> {
        let mut s = Self {
            use_type: convert_to_u32(t.field("useType"))?,
            dice: convert_to_i32(t.field("dice"))?,
            ..Self::default()
        };
        if let Some((a, b)) = range_i32(t, "healthRange")? {
            s.health_range_min = a;
            s.health_range_max = b;
        }
        if let Some((a, b)) = range_f32(t, "timeRange")? {
            s.time_range_start = a;
            s.time_range_end = b;
        }
        if let LuaValue::Table(skills) = t.field("skills") {
            for v in skills.values() {
                if let LuaValue::Table(st) = v {
                    s.skills.push(ai_skill_from(st)?);
                }
            }
        }
        Ok(s)
    }
}

impl ArcherCombatSkill {
    /// `ArcherCombatSkill.ParseLua(table)`.
    pub fn parse_lua(t: &LuaTable) -> PResult<Self> {
        Ok(Self {
            melee: u32_list(t, "melee")?,
            make_a_gap: u32_list(t, "makeAGap")?,
            ranged_def: u32_list(t, "rangedDef")?,
            ranged_strong: u32_list(t, "rangedStrong")?,
        })
    }
}

impl BigMonsterCombatSkill {
    /// `BigMonsterCombatSkill.ParseLua(table)`.
    pub fn parse_lua(t: &LuaTable) -> PResult<Self> {
        let mut s = Self::default();
        if let Some((a, b)) = range_i32(t, "healthRange")? {
            s.health_range_min = a;
            s.health_range_max = b;
        }
        s.skill_type = convert_to_u32(t.field("skillType"))?;
        s.skill_delay = convert_to_f32(t.field("skillDelay"))?;
        s.strafe_during_delay = convert_to_bool(t.field("strafeDuringDelay"))?;
        Ok(s)
    }
}

impl FlytrapCombatSkill {
    /// `FlytrapCombatSkill.ParseLua(table)`.
    pub fn parse_lua(t: &LuaTable) -> PResult<Self> {
        Ok(Self {
            melee: u32_list(t, "melee")?,
            ranged: u32_list(t, "ranged")?,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn archer_params_from_the_real_1001_row() {
        let data = "alertDuration = 10,\r\nalertToAttack = 3,\r\nalertSafeTargetRememberTime = 5,\r\nmeleeAttackRange = 15,\r\ncombatSkills = {\r\n  melee = { 2 },\r\n  makeAGap = { 12340 },\r\n  rangedDef = { 15935},\r\n  rangedStrong = { 12508 }\r\n},\r\npreferedCombatDist = 18,\r\nmaxMakeAGapCount = 3,";
        let p = AiParams::create_by_type(Some(AiParamType::ArcherRoaming), data)
            .unwrap()
            .unwrap();
        assert_eq!(p.alert_duration, 10.0);
        assert!(p.alert_to_attack);
        assert_eq!(p.melee_attack_range, 15.0);
        assert_eq!(p.prefered_combat_dist, 18.0);
        assert_eq!(p.max_make_a_gap_count, 3);
        assert!(p.restoration_on_return());
        assert_eq!(
            p.kind,
            AiParamsKind::Archer {
                combat_skills: vec![ArcherCombatSkill {
                    melee: vec![2],
                    make_a_gap: vec![12340],
                    ranged_def: vec![15935],
                    ranged_strong: vec![12508],
                }]
            }
        );
    }

    #[test]
    fn almighty_skill_lists() {
        let data = "alertDuration = 0,\r\nalertToAttack = true,\r\naiPhaseChangeType = PHASE_TYPE_SEQUENCE,\r\naiSkillLists = {\r\n  {\r\n    pipeName = \"hold_fight\",\r\n    useType = USE_SEQUENCE,\r\n    dice = 100,\r\n    healthRange = {0, 100},\r\n    timeRange = { 5, 0 },\r\n    options = { restoration = false, goReturn = true },\r\n    startAiSkill = { skillType = 1, delay = 2, strafe = true },\r\n    skills = {\r\n      { skillType = 23060, delay = 1, strafe = true }, --comment\r\n    },\r\n  },\r\n  { skillType = 9, delay = 1.5 },\r\n}";
        let p = AiParams::almighty(data).unwrap();
        let AiParamsKind::Almighty {
            ai_skill_lists,
            ai_phase_change_type,
            idle_ai,
            ..
        } = &p.kind
        else {
            panic!()
        };
        assert_eq!(*ai_phase_change_type, 1);
        assert_eq!(idle_ai, "");
        assert_eq!(ai_skill_lists.len(), 2);
        let a = &ai_skill_lists[0];
        assert_eq!(a.pipe_name, "hold_fight");
        assert_eq!((a.use_type, a.dice), (1, 100));
        assert_eq!((a.health_range_min, a.health_range_max), (0, 100));
        assert_eq!((a.time_range_start, a.time_range_end), (5.0, 0.0));
        assert!(!a.restoration && a.go_return);
        assert_eq!(
            a.start_ai_skills,
            vec![AiSkill {
                skill_id: 1,
                strafe: true,
                delay: 2.0
            }]
        );
        assert_eq!(a.skill_lists.len(), 1);
        assert_eq!(a.skill_lists[0].skills[0].skill_id, 23060);
        // A list that is a skill itself.
        let b = &ai_skill_lists[1];
        assert_eq!(b.pipe_name, "");
        assert_eq!(
            b.skill_lists[0].skills,
            vec![AiSkill {
                skill_id: 9,
                strafe: false,
                delay: 1.5
            }]
        );
    }

    #[test]
    fn bad_rows_fail_like_the_csharp() {
        // npc_ai_params 835: Lua syntax error
        assert!(AiParams::flytrap("attackEndDistance = 25,\r\nmeleeAttackRange = 5,\r\ncombatSkills = {\r\n  melee = { [2,12279] },\r\n}").is_err());
        // a float where GetInteger expects a Lua integer
        assert!(AiParams::archer("maxMakeAGapCount = 2.5").is_err());
        // not one of the parsed types
        assert_eq!(
            AiParams::create_by_type(Some(AiParamType::HoldPosition), "x = 1").unwrap(),
            None
        );
    }

    #[test]
    fn wild_boar_and_big_monster() {
        let w = AiParams::wild_boar(
            "onCombatStartSkill = { 15625 },\r\nonSpurtSkill = {\r\n { skillType = 14038, healthCondition = 70 },\r\n},",
        )
        .unwrap();
        assert_eq!(
            w.kind,
            AiParamsKind::WildBoar {
                on_combat_start_skills: vec![15625],
                on_spurt_skills: vec![WildBoarAiSpurtSkill {
                    skill_type: 14038,
                    health_condition: 70
                }]
            }
        );
        let b = AiParams::big_monster("alertDuration = 6.6,\r\ncombatSkills =\r\n  {\r\n    { skillType = 15027, skillDelay = 1, strafeDuringDelay = true, healthRange = { 0, 100 } }, --x\r\n  }").unwrap();
        assert_eq!(b.alert_duration, 6.6);
        assert_eq!(
            b.kind,
            AiParamsKind::BigMonster {
                combat_skills: vec![BigMonsterCombatSkill {
                    skill_type: 15027,
                    skill_delay: 1.0,
                    strafe_during_delay: true,
                    health_range_min: 0,
                    health_range_max: 100
                }]
            }
        );
    }
}
