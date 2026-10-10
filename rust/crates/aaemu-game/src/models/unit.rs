//! The state every unit shares: port of the data members of `Models/Game/World/GameObject`,
//! `Models/Game/Units/BaseUnit` and `Models/Game/Units/Unit` (+ `UnitCooldowns`, `CombatBuffs`,
//! `Units/Static`, `UnitAttribute`, `UnitModifierType`, `Skills/Bonus`, `DynamicBonus`,
//! `SkillModifier(s)`, `BuffModifier(s)`), and the traits the object kinds implement.
//!
//! Design (see `CONTRIBUTING-PORT.md`, "Object model"):
//! * Every unit kind (`Character`, `Npc`, `Slave`, `Mate`, `House`, `Transfer`, `Shipyard`,
//!   `Gimmick`) embeds one [`UnitBase`] as `pub unit: UnitBase` and implements [`UnitData`]
//!   (normally with [`impl_unit_data!`]). Shared code (skills, buffs, combat, AI) takes
//!   `&mut dyn UnitData` / `&mut UnitBase` and works for every kind.
//! * `Doodad` is a `BaseUnit` but not a `Unit` in C#: it implements [`BaseUnitData`] only.
//! * C# `virtual` properties that a kind overrides with a template value (`Scale`, `RaceGender`,
//!   `MaxHp`, ...) are plain stored fields here, filled in by whoever creates the object (the
//!   spawner / manager of that kind). Behaviour (`ReduceCurrentHp`, `DoDie`, regen, aggro, ...)
//!   is ported by the subsystem that owns it, on top of these fields.
//!
//! Locking: these structs live inside the per-object `Mutex` of a world handle
//! (`world::object::Entity<D>` / `world::Player`). Never block on another object's lock while
//! holding one; collect what you need, drop the guard, then act (see `world::object`).

use std::collections::HashMap;

use crate::models::buff::Buffs;
use crate::models::transform::Transform;
use crate::models::unit_model::UnitCustomModelParams;

/// `BaseUnitType` (the `SCUnitStatePacket` type byte).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
#[repr(u8)]
pub enum BaseUnitType {
    Character = 0,
    Npc = 1,
    Slave = 2,
    Housing = 3,
    Transfer = 4,
    Mate = 5,
    Shipyard = 6,
    Invalid = 255,
}

/// `UnitTypeFlag` (bit flags, used by skill target filters).
pub mod unit_type_flag {
    pub const NONE: u32 = 0;
    pub const CHARACTER: u32 = 1;
    pub const NPC: u32 = 2;
    pub const SLAVE: u32 = 4;
    pub const HOUSING: u32 = 8;
    pub const TRANSFER: u32 = 16;
    pub const MATE: u32 = 32;
    pub const SHIPYARD: u32 = 64;
}

/// `ModelPostureType`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
#[repr(u8)]
pub enum ModelPostureType {
    #[default]
    None = 0,
    HouseState = 1,
    ActorModelState = 4,
    FarmfieldState = 7,
    TurretState = 8,
}

/// `AttachPointKind` (`DoodadObj.Static`), the values the object model needs by name.
pub mod attach_point {
    pub const NONE: u8 = 0;
    pub const DRIVER: u8 = 1;
    pub const PASSENGER0: u8 = 2;
    pub const SYSTEM: u8 = 255;
}

/// `UnitModifierType`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash, Default)]
#[repr(u8)]
pub enum UnitModifierType {
    #[default]
    Value = 0,
    Percent = 1,
}

impl UnitModifierType {
    pub fn from_u8(v: u8) -> Self {
        if v == 1 {
            Self::Percent
        } else {
            Self::Value
        }
    }
}

macro_rules! byte_enum {
    ($(#[$m:meta])* $name:ident { $($var:ident = $val:expr),* $(,)? }) => {
        $(#[$m])*
        #[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
        #[repr(u8)]
        pub enum $name { $($var = $val),* }
        impl $name {
            /// The enum value for a raw DB / wire value (`None` for values the C# enum lacks).
            pub fn from_u8(v: u8) -> Option<Self> {
                match v { $($val => Some(Self::$var),)* _ => None }
            }
        }
    };
}

byte_enum!(
    /// `UnitAttribute`.
    UnitAttribute {
        Str = 0, Dex = 1, Sta = 2, Int = 3, Spi = 4, Fai = 5, MaxHealth = 6, MaxMana = 7,
        Armor = 8, MoveSpeedMul = 10, HealthRegen = 11, ManaRegen = 12, Facets = 13,
        MeleeCritical = 16, MeleeCriticalBonus = 17, MeleeAntiMiss = 18, MeleeDodge = 20,
        MeleeBlock = 21, MeleeParry = 22, RangedAntiMiss = 23, RangedCritical = 25,
        RangedCriticalBonus = 26, SpellAntiMiss = 28, SpellCritical = 30, SpellCriticalBonus = 31,
        MeleeDpsInc = 33, RangedDpsInc = 34, SpellDpsInc = 35, MainhandSpeed = 36,
        MainhandDamageMin = 37, MainhandDamageMax = 38, OffhandSpeed = 41, OffhandDamageMin = 42,
        OffhandDamageMax = 43, RangedSpeed = 46, RangedDamageMin = 47, RangedDamageMax = 48,
        MeleeDamageMul = 51, RangedDamageMul = 52, SpellDamageMul = 53, MeleeSpeedMul = 54,
        RangedSpeedMul = 55, IncomingHealMul = 56, IgnoreArmor = 57, IncomingDamageMul = 58,
        RangedDodge = 59, RangedBlock = 60, AggroRangeMul = 61, IncomingAggroMul = 62,
        Hovering = 63, MagicResist = 64, MagicStability = 65, SwimSpeedMul = 66,
        PersistentHealthRegen = 67, PersistentManaRegen = 68, ArmorType = 69,
        ArmorTypeCoverage = 70, CastingTimeMul = 71, TurnSpeed = 72, GravityMul = 73,
        GlobalCooldownMul = 74, TwohandSpeedMul = 75, MeleeCriticalMul = 77,
        MeleeAntiMissMul = 78, MeleeDodgeMul = 79, MeleeBlockMul = 80, MeleeParryMul = 81,
        RangedCriticalMul = 82, RangedAntiMissMul = 83, RangedDodgeMul = 84, RangedBlockMul = 85,
        SpellCriticalMul = 86, SpellDps = 87, SpellAntiMissMul = 88, CastingTolerance = 89,
        AggroMul = 90, LungCapacity = 91, FallDamageMul = 92, LadderSpeedMul = 93,
        DetectStealthRangeMul = 94, ExpMul = 95, MainhandDps = 96, OffhandDps = 97,
        RangedDps = 98, ActabilityArchemy = 99, ActabilityArchitecture = 100,
        ActabilityCook = 101, ActabilityHandicraft = 102, ActabilityLivestock = 103,
        ActabilityFarm = 104, ActabilityFish = 105, ActabilityLumber = 106,
        ActabilityCollection = 107, ActabilityMachinery = 108, ActabilityMetal = 109,
        ActabilityPrint = 110, ActabilityMine = 111, ActabilityStonemason = 112,
        ActabilitySewing = 113, ActabilitySkin = 114, ActabilityWeapon = 115,
        ActabilityCarpentry = 116, ActabilityTheft = 117, ActabilityBusiness = 118,
        AttackAnimSpeedMul = 119, HealMul = 120, BackattackMeleeDamageMul = 121,
        BackattackRangedDamageMul = 122, BackattackSpellDamageMul = 123, FrictionMul = 124,
        HonorPointLoseMul = 125, HonorPointGainBattleField = 126,
        HonorPointGainBattleFieldMul = 127, HonorPointGainNpcKill = 128,
        HonorPointGainNpcKillMul = 129, HonorPointGainTrial = 130, HonorPointGainTrialMul = 131,
        HonorPointGainWar = 132, HonorPointGainWarMul = 133, HonorPointGainQuest = 134,
        HonorPointGainQuestMul = 135, LivingPointGain = 136, LivingPointGainMul = 137,
        ActabilityComposition = 138, UnderwaterSwimSpeedMul = 139, DropRateMul = 140,
        LootGoldMul = 141, IncomingMeleeDamageAdd = 142, IncomingMeleeDamageMul = 143,
        IncomingRangedDamageAdd = 144, IncomingRangedDamageMul = 145,
        IncomingSpellDamageAdd = 146, IncomingSpellDamageMul = 147,
        IncomingSiegeDamageAdd = 148, IncomingSiegeDamageMul = 149, SpellDamageCritical = 150,
        SpellDamageCriticalMul = 151, SpellDamageCriticalBonus = 152, RangedParry = 153,
        RangedParryMul = 154, HealDps = 173, HealCritical = 174, HealDpsInc = 175,
        HealCriticalBonus = 176, Block = 177, Dodge = 178, BlockMul = 179, DodgeMul = 180,
        BullsEye = 181, BattleResist = 182, Flexibility = 183, MagicPenetration = 184,
        HealCriticalMul = 185, ExpByLaborPowerMul = 186,
    }
);

byte_enum!(
    /// `SkillAttribute` (`Skills/Static`).
    SkillAttribute {
        ManaCost = 0, Cooldown = 1, Range = 2, AreaRadius = 3, CastTime = 4,
        IncomingDamageMul = 5, Damage = 10, ChannelingTime = 11, Heal = 12, SkillLevel = 13,
        HighAbilityResourceCost = 14,
    }
);

byte_enum!(
    /// `BuffAttribute` (`Skills/Static`).
    BuffAttribute {
        ReflectionTargetRatio = 1, Chance = 2, Damage = 3, Duration = 4, InDuration = 5,
        AuraRadius = 6, GlidingMoveSpeedSlow = 7, GlidingMoveSpeedNormal = 8,
        GlidingMoveSpeedFast = 9,
    }
);

/// `BonusTemplate` (unit_modifiers rows).
#[derive(Debug, Clone, PartialEq)]
pub struct BonusTemplate {
    pub attribute: UnitAttribute,
    pub modifier_type: UnitModifierType,
    pub value: i32,
    pub linear_level_bonus: i32,
}

/// `Bonus`: a static attribute bonus (value snapshotted when it is applied).
#[derive(Debug, Clone, PartialEq)]
pub struct Bonus {
    pub template: BonusTemplate,
    pub value: i32,
}

/// `LinearFuncTemplate`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct LinearFuncTemplate {
    pub id: u32,
    pub start_value: i32,
    pub end_value: i32,
}

impl LinearFuncTemplate {
    /// `GetValue(ratio)`.
    pub fn value(&self, ratio: f64) -> f64 {
        let ratio = ratio.clamp(0.0, 1.0);
        f64::from(self.start_value) + f64::from(self.end_value - self.start_value) * ratio
    }
}

/// `DynamicBonusTemplate` (dynamic_unit_modifiers rows).
#[derive(Debug, Clone, PartialEq)]
pub struct DynamicBonusTemplate {
    pub attribute: UnitAttribute,
    pub modifier_type: UnitModifierType,
    pub func_id: u32,
    pub func_type: String,
}

/// `DynamicBonus`: a bonus re-evaluated on every read from its source buff's elapsed time.
/// The C# holds the source `Buff`; here the two values `Evaluate` reads from it are copied
/// (`SourceBuff.StartTime` / `Duration`), which stay fixed while the bonus exists.
#[derive(Debug, Clone, PartialEq)]
pub struct DynamicBonus {
    pub template: DynamicBonusTemplate,
    /// `SourceBuff.Index` (for bookkeeping / removal).
    pub source_buff_index: u32,
    pub source_start_ms: i64,
    pub source_duration: i32,
    pub linear_func: Option<LinearFuncTemplate>,
}

impl DynamicBonus {
    /// `Evaluate(out value)`; `None` = not applicable.
    pub fn evaluate(&self, now_ms: i64) -> Option<f64> {
        match self.template.func_type.as_str() {
            "LinearFunc" => {
                let f = self.linear_func?;
                let mut ratio = 1.0;
                if self.source_duration > 0 {
                    let elapsed = (now_ms - self.source_start_ms) as f64;
                    ratio = (elapsed / f64::from(self.source_duration)).clamp(0.0, 1.0);
                }
                Some(f.value(ratio))
            }
            // "ManualFunc": not implemented in C# either.
            _ => None,
        }
    }
}

/// `Unit.Bonuses` + `Unit.DynamicBonuses` and `CalculateWithBonuses`.
/// Dictionary iteration order in C# is insertion order for these never-removed keys; a
/// `Vec` of (index, list) keeps that order.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct Bonuses {
    pub statics: Vec<(u32, Vec<Bonus>)>,
    pub dynamics: Vec<(u32, Vec<DynamicBonus>)>,
}

impl Bonuses {
    /// `AddBonus(bonusIndex, bonus)`.
    pub fn add_bonus(&mut self, index: u32, bonus: Bonus) {
        match self.statics.iter_mut().find(|(i, _)| *i == index) {
            Some((_, l)) => l.push(bonus),
            None => self.statics.push((index, vec![bonus])),
        }
    }

    /// `RemoveBonus(bonusIndex, attribute)` (the emptied list stays, as in C#).
    pub fn remove_bonus(&mut self, index: u32, attribute: UnitAttribute) {
        if let Some((_, l)) = self.statics.iter_mut().find(|(i, _)| *i == index) {
            l.retain(|b| b.template.attribute != attribute);
        }
    }

    /// `GetBonuses(attribute)`.
    pub fn bonuses(&self, attribute: UnitAttribute) -> Vec<&Bonus> {
        self.statics
            .iter()
            .flat_map(|(_, l)| l.iter())
            .filter(|b| b.template.attribute == attribute)
            .collect()
    }

    /// `AddDynamicBonus(bonusIndex, bonus)`.
    pub fn add_dynamic_bonus(&mut self, index: u32, bonus: DynamicBonus) {
        match self.dynamics.iter_mut().find(|(i, _)| *i == index) {
            Some((_, l)) => l.push(bonus),
            None => self.dynamics.push((index, vec![bonus])),
        }
    }

    /// `RemoveDynamicBonus(bonusIndex, attribute)` (an emptied list is removed).
    pub fn remove_dynamic_bonus(&mut self, index: u32, attribute: UnitAttribute) {
        if let Some(pos) = self.dynamics.iter().position(|(i, _)| *i == index) {
            self.dynamics[pos]
                .1
                .retain(|b| b.template.attribute != attribute);
            if self.dynamics[pos].1.is_empty() {
                self.dynamics.remove(pos);
            }
        }
    }

    /// `GetDynamicBonuses(attribute)`.
    pub fn dynamic_bonuses(&self, attribute: UnitAttribute) -> Vec<&DynamicBonus> {
        self.dynamics
            .iter()
            .flat_map(|(_, l)| l.iter())
            .filter(|b| b.template.attribute == attribute)
            .collect()
    }

    /// `CalculateWithBonuses(value, attr)`: static flat, dynamic flat, static percent, dynamic
    /// percent. (`value * bonus.Value / 100f` is float arithmetic in C#.)
    pub fn calculate(&self, mut value: f64, attr: UnitAttribute, now_ms: i64) -> f64 {
        let statics = self.bonuses(attr);
        let dynamics = self.dynamic_bonuses(attr);
        for b in &statics {
            if b.template.modifier_type == UnitModifierType::Value {
                value += f64::from(b.value);
            }
        }
        for d in &dynamics {
            if d.template.modifier_type == UnitModifierType::Value {
                if let Some(v) = d.evaluate(now_ms) {
                    value += v;
                }
            }
        }
        for b in &statics {
            if b.template.modifier_type == UnitModifierType::Percent {
                value += value * f64::from(b.value) / 100.0;
            }
        }
        for d in &dynamics {
            if d.template.modifier_type == UnitModifierType::Percent {
                if let Some(v) = d.evaluate(now_ms) {
                    value += value * v / 100.0;
                }
            }
        }
        value
    }
}

/// `UnitCooldowns` (skill id -> end time, unix ms).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct UnitCooldowns {
    pub cooldowns: HashMap<u32, i64>,
}

impl UnitCooldowns {
    /// `AddCooldown(skillId, duration)` (an existing cooldown is kept).
    pub fn add_cooldown(&mut self, skill_id: u32, duration_ms: u32, now_ms: i64) {
        self.cooldowns
            .entry(skill_id)
            .or_insert(now_ms + i64::from(duration_ms));
    }

    /// `CheckCooldown(skillId)`: true while more than 250 ms are left; an (almost) expired
    /// cooldown is removed.
    pub fn check_cooldown(&mut self, skill_id: u32, now_ms: i64) -> bool {
        let Some(&end) = self.cooldowns.get(&skill_id) else {
            return false;
        };
        if end - now_ms > 250 {
            return true;
        }
        self.cooldowns.remove(&skill_id);
        false
    }

    /// `RemoveCooldown(skillId)`.
    pub fn remove_cooldown(&mut self, skill_id: u32) {
        self.cooldowns.remove(&skill_id);
    }
}

/// `SkillModifier` (skill_modifiers rows).
#[derive(Debug, Clone, PartialEq)]
pub struct SkillModifier {
    pub id: u32,
    pub owner_id: u32,
    pub tag_id: u32,
    pub skill_attribute: SkillAttribute,
    pub unit_modifier_type: UnitModifierType,
    pub value: i32,
    pub skill_id: u32,
    pub synergy: bool,
}

/// `BuffModifier` (buff_modifiers rows).
#[derive(Debug, Clone, PartialEq)]
pub struct BuffModifier {
    pub id: u32,
    pub owner_id: u32,
    pub tag_id: u32,
    pub buff_attribute: BuffAttribute,
    pub unit_modifier_type: UnitModifierType,
    pub value: i32,
    pub buff_id: u32,
    pub synergy: bool,
}

/// Shared shape of `SkillModifiers` / `BuffModifiers`: modifiers indexed by target id and tag.
trait ModifierRow: Clone + PartialEq {
    type Attr: Copy + PartialEq;
    fn target_id(&self) -> u32;
    fn tag_id(&self) -> u32;
    fn attr(&self) -> Self::Attr;
    fn kind(&self) -> UnitModifierType;
    fn value(&self) -> i32;
    fn row_id(&self) -> u32;
}

impl ModifierRow for SkillModifier {
    type Attr = SkillAttribute;
    fn target_id(&self) -> u32 {
        self.skill_id
    }
    fn tag_id(&self) -> u32 {
        self.tag_id
    }
    fn attr(&self) -> SkillAttribute {
        self.skill_attribute
    }
    fn kind(&self) -> UnitModifierType {
        self.unit_modifier_type
    }
    fn value(&self) -> i32 {
        self.value
    }
    fn row_id(&self) -> u32 {
        self.id
    }
}

impl ModifierRow for BuffModifier {
    type Attr = BuffAttribute;
    fn target_id(&self) -> u32 {
        self.buff_id
    }
    fn tag_id(&self) -> u32 {
        self.tag_id
    }
    fn attr(&self) -> BuffAttribute {
        self.buff_attribute
    }
    fn kind(&self) -> UnitModifierType {
        self.unit_modifier_type
    }
    fn value(&self) -> i32 {
        self.value
    }
    fn row_id(&self) -> u32 {
        self.id
    }
}

#[derive(Debug, Clone, PartialEq)]
struct ModifierCache<M> {
    by_id: HashMap<u32, Vec<M>>,
    by_tag: HashMap<u32, Vec<M>>,
}

impl<M> Default for ModifierCache<M> {
    fn default() -> Self {
        Self {
            by_id: HashMap::new(),
            by_tag: HashMap::new(),
        }
    }
}

impl<M: ModifierRow> ModifierCache<M> {
    fn apply(&self, target_id: u32, tags: &[u32], attr: M::Attr, base: f64) -> f64 {
        let mut mods: Vec<&M> = self
            .by_id
            .get(&target_id)
            .map(|l| l.iter().filter(|m| m.attr() == attr).collect())
            .unwrap_or_default();
        mods.sort_by_key(|m| m.kind()); // OrderBy (stable)
        for tag in tags {
            if let Some(l) = self.by_tag.get(tag) {
                mods.extend(l.iter().filter(|m| m.attr() == attr));
            }
        }
        let mut end = base;
        for m in mods {
            match m.kind() {
                UnitModifierType::Percent => end += end * f64::from(m.value() as f32 / 100.0f32),
                UnitModifierType::Value => end += f64::from(m.value()),
            }
        }
        end
    }

    fn add(&mut self, m: M) {
        if m.target_id() > 0 {
            self.by_id.entry(m.target_id()).or_default().push(m.clone());
        }
        if m.tag_id() > 0 {
            self.by_tag.entry(m.tag_id()).or_default().push(m);
        }
    }

    fn remove(&mut self, m: &M) {
        for list in [
            self.by_id.get_mut(&m.target_id()),
            self.by_tag.get_mut(&m.tag_id()),
        ]
        .into_iter()
        .flatten()
        {
            if let Some(i) = list.iter().position(|x| x.row_id() == m.row_id()) {
                list.remove(i);
            }
        }
    }
}

/// `SkillModifiers` (`BaseUnit.SkillModifiersCache`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct SkillModifiers(ModifierCache<SkillModifier>);

impl SkillModifiers {
    /// `ApplyModifiers(skill, attribute, baseValue)`; `skill_tags` =
    /// `SkillManager.GetSkillTags(skill.Template.Id)`.
    pub fn apply(&self, skill_id: u32, skill_tags: &[u32], attr: SkillAttribute, base: f64) -> f64 {
        self.0.apply(skill_id, skill_tags, attr, base)
    }

    /// `AddModifiers(buffId)` with the rows `BuffGameData.GetModifiersForSkill(buffId)` returns.
    pub fn add_modifiers(&mut self, rows: &[SkillModifier]) {
        for m in rows {
            self.0.add(m.clone());
        }
    }

    /// `RemoveModifiers(buffId)`.
    pub fn remove_modifiers(&mut self, rows: &[SkillModifier]) {
        for m in rows {
            self.0.remove(m);
        }
    }
}

/// `BuffModifiers` (`BaseUnit.BuffModifiersCache`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct BuffModifiers(ModifierCache<BuffModifier>);

impl BuffModifiers {
    /// `ApplyModifiers(buff, attribute, value)`; `buff_tags` = `SkillManager.GetBuffTags(buff.Id)`.
    pub fn apply(&self, buff_id: u32, buff_tags: &[u32], attr: BuffAttribute, base: f64) -> f64 {
        self.0.apply(buff_id, buff_tags, attr, base)
    }

    /// `AddModifiers(buffId)` with the rows `BuffGameData.GetModifiersForBuff(buffId)` returns.
    pub fn add_modifiers(&mut self, rows: &[BuffModifier]) {
        for m in rows {
            self.0.add(m.clone());
        }
    }

    /// `RemoveModifiers(buffId)`.
    pub fn remove_modifiers(&mut self, rows: &[BuffModifier]) {
        for m in rows {
            self.0.remove(m);
        }
    }
}

/// `CombatBuffTemplate` (combat_buffs rows).
#[derive(Debug, Clone, PartialEq)]
pub struct CombatBuffTemplate {
    pub id: u32,
    pub hit_skill_id: u32,
    /// `SkillHitType`.
    pub hit_type: u8,
    pub req_skill_id: u32,
    pub buff_id: u32,
    pub buff_from_source: bool,
    pub buff_to_source: bool,
    pub req_buff_id: u32,
    pub is_heal_spell: bool,
}

/// `CombatBuffs` (per-unit, keyed by hit type). `TriggerCombatBuffs` is behaviour: TODO(skills).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct CombatBuffs {
    pub by_hit_type: HashMap<u8, Vec<CombatBuffTemplate>>,
}

impl CombatBuffs {
    /// `AddCombatBuffs(buffId)` with `SkillManager.GetCombatBuffs(buffId)`.
    pub fn add_combat_buffs(&mut self, rows: &[CombatBuffTemplate]) {
        for r in rows {
            self.by_hit_type
                .entry(r.hit_type)
                .or_default()
                .push(r.clone());
        }
    }

    /// `RemoveCombatBuff(buffId)`.
    pub fn remove_combat_buffs(&mut self, rows: &[CombatBuffTemplate]) {
        for r in rows {
            if let Some(l) = self.by_hit_type.get_mut(&r.hit_type) {
                if let Some(i) = l.iter().position(|x| x == r) {
                    l.remove(i);
                }
            }
        }
    }
}

/// The `GameObject` + `BaseUnit` + `Unit` data members shared by every unit kind.
#[derive(Debug, Clone, PartialEq)]
pub struct UnitBase {
    // --- GameObject ---
    pub transform: Transform,
    // --- BaseUnit ---
    pub template_id: u32,
    pub name: String,
    /// `Faction.Id` (0 = null faction).
    pub faction_id: u32,
    /// `OriginFaction.Id`.
    pub origin_faction_id: u32,
    /// `Scale` (kinds whose C# getter reads the template store the template value here).
    pub scale: f32,
    pub buffs: Buffs,
    pub skill_modifiers: SkillModifiers,
    pub buff_modifiers: BuffModifiers,
    pub combat_buffs: CombatBuffs,
    // --- Unit ---
    pub model_id: u32,
    pub level: u8,
    pub hp: i32,
    pub mp: i32,
    /// `MaxHp` / `MaxMp` and the other formula attributes are computed by the kind (the
    /// character formulas in `models::stats`, NPC formulas: TODO(npcs)); these are the cached
    /// values shared code reads.
    pub max_hp: i32,
    pub max_mp: i32,
    pub hp_regen: i32,
    pub mp_regen: i32,
    pub persistent_hp_regen: i32,
    pub persistent_mp_regen: i32,
    pub global_cooldown_mul: f32,
    pub cast_time_mul: f32,
    pub model_params: UnitCustomModelParams,
    pub active_weapon: u8,
    pub idle_status: bool,
    pub force_attack: bool,
    pub invisible: bool,
    /// `OwnerId` (character id of the owner/master).
    pub owner_id: u32,
    /// `TlId` (slave / mate / house / transfer ids from the `TlIdManager`).
    pub tl_id: u16,
    /// `Expedition?.Id ?? 0`.
    pub expedition_id: u32,
    pub is_in_battle: bool,
    pub is_in_duel: bool,
    pub is_in_patrol: bool,
    pub is_auto_attack: bool,
    pub is_under_water: bool,
    pub has_loot_left: bool,
    pub summarize_damage: i32,
    /// `CurrentTarget?.ObjId ?? 0`.
    pub current_target: u32,
    /// `CurrentInteractionObject?.ObjId ?? 0`.
    pub current_interaction_object: u32,
    /// Unix ms (`DateTime.MinValue` = 0).
    pub last_combat_activity_ms: i64,
    pub global_cooldown_ms: i64,
    pub skill_last_used_ms: i64,
    pub displaced_until_ms: Option<i64>,
    pub cooldowns: UnitCooldowns,
    pub bonuses: Bonuses,
    /// `HpTriggerPointsPercent`.
    pub hp_trigger_points_percent: Vec<i32>,
    /// `IncrementTriggerCount` / `GetTriggerCount` (buff id -> count).
    pub trigger_counts: HashMap<u32, i32>,
    // ---- phase 3 / skills: add this subsystem's per-unit fields below ----
    /// Skill / combat runtime state (`SkillTask`, `ActivePlotState`, `AggroTable`, ...).
    pub skill_state: crate::systems::skills::state::UnitSkillState,
    // ---- phase 3 / npcs: add this subsystem's per-unit fields below ----

    // ---- phase 3 / economy: add this subsystem's per-unit fields below ----

    // ---- phase 3 / social: add this subsystem's per-unit fields below ----

    // ---- phase 3 / quests: add this subsystem's per-unit fields below ----
}

impl Default for UnitBase {
    fn default() -> Self {
        Self {
            transform: Transform::default(),
            template_id: 0,
            name: String::new(),
            faction_id: 0,
            origin_faction_id: 0,
            scale: 1.0,
            buffs: Buffs::default(),
            skill_modifiers: SkillModifiers::default(),
            buff_modifiers: BuffModifiers::default(),
            combat_buffs: CombatBuffs::default(),
            model_id: 0,
            level: 0,
            hp: 0,
            mp: 0,
            max_hp: 0,
            max_mp: 0,
            hp_regen: 0,
            mp_regen: 0,
            persistent_hp_regen: 30,
            persistent_mp_regen: 30,
            global_cooldown_mul: 100.0,
            cast_time_mul: 1.0,
            model_params: UnitCustomModelParams::default(),
            active_weapon: 0,
            idle_status: false,
            force_attack: false,
            invisible: false,
            owner_id: 0,
            tl_id: 0,
            expedition_id: 0,
            is_in_battle: false,
            is_in_duel: false,
            is_in_patrol: false,
            is_auto_attack: false,
            is_under_water: false,
            has_loot_left: false,
            summarize_damage: 0,
            current_target: 0,
            current_interaction_object: 0,
            last_combat_activity_ms: 0,
            global_cooldown_ms: 0,
            skill_last_used_ms: 0,
            displaced_until_ms: None,
            cooldowns: UnitCooldowns::default(),
            bonuses: Bonuses::default(),
            hp_trigger_points_percent: Vec::new(),
            trigger_counts: HashMap::new(),
            // ---- phase 3 / skills ----
            skill_state: Default::default(),
            // ---- phase 3 / npcs ----

            // ---- phase 3 / economy ----

            // ---- phase 3 / social ----

            // ---- phase 3 / quests ----
        }
    }
}

impl UnitBase {
    /// `Hpp`.
    pub fn hpp(&self) -> i32 {
        if self.max_hp <= 0 {
            return 0;
        }
        ((self.hp as f32 * 100.0 / self.max_hp as f32).ceil() as i32).clamp(0, 100)
    }

    /// `IsDead`.
    pub fn is_dead(&self) -> bool {
        self.hp <= 0
    }

    /// `NeedsRegen`.
    pub fn needs_regen(&self) -> bool {
        self.hp < self.max_hp || self.mp < self.max_mp
    }

    /// `CalculateWithBonuses(value, attr)`.
    pub fn calculate_with_bonuses(&self, value: f64, attr: UnitAttribute, now_ms: i64) -> f64 {
        self.bonuses.calculate(value, attr, now_ms)
    }

    /// `IncrementTriggerCount` / `DecrementTriggerCount` / `GetTriggerCount`.
    pub fn increment_trigger_count(&mut self, buff_id: u32) {
        *self.trigger_counts.entry(buff_id).or_insert(0) += 1;
    }

    pub fn decrement_trigger_count(&mut self, buff_id: u32) {
        if let Some(c) = self.trigger_counts.get_mut(&buff_id) {
            *c -= 1;
        }
    }

    pub fn trigger_count(&self, buff_id: u32) -> i32 {
        self.trigger_counts.get(&buff_id).copied().unwrap_or(0)
    }
}

/// `GameObject` data: anything placed in a region.
pub trait GameObjectData {
    fn transform(&self) -> &Transform;
    fn transform_mut(&mut self) -> &mut Transform;
}

/// `BaseUnit` / `IBaseUnit` data (units and doodads).
pub trait BaseUnitData: GameObjectData {
    fn name(&self) -> &str;
    fn template_id(&self) -> u32;
    fn faction_id(&self) -> u32;
    fn scale(&self) -> f32;
    fn buffs(&self) -> &Buffs;
    fn buffs_mut(&mut self) -> &mut Buffs;
}

/// `Unit` / `IUnit` data, implemented by every unit kind (see [`impl_unit_data!`]).
pub trait UnitData: BaseUnitData {
    fn unit(&self) -> &UnitBase;
    fn unit_mut(&mut self) -> &mut UnitBase;
    /// `BaseUnitType`.
    fn base_unit_type(&self) -> BaseUnitType;
    /// `TypeFlag` ([`unit_type_flag`]).
    fn type_flag(&self) -> u32;
    /// `ModelPostureType` (virtual in C#).
    fn model_posture_type(&self) -> ModelPostureType {
        ModelPostureType::None
    }
    /// `RaceGender` (virtual in C#; 0 for kinds without race).
    fn race_gender(&self) -> u8 {
        0
    }
}

/// Implements [`GameObjectData`], [`BaseUnitData`] and the accessor half of [`UnitData`] for a
/// struct with a `unit: UnitBase` field. Kind-specific overrides go in the optional block:
///
/// ```ignore
/// impl_unit_data!(Npc, BaseUnitType::Npc, unit_type_flag::NPC, {
///     fn model_posture_type(&self) -> ModelPostureType { ... }
/// });
/// ```
#[macro_export]
macro_rules! impl_unit_data {
    ($ty:ty, $kind:expr, $flag:expr) => {
        $crate::impl_unit_data!($ty, $kind, $flag, {});
    };
    ($ty:ty, $kind:expr, $flag:expr, { $($extra:tt)* }) => {
        impl $crate::models::unit::GameObjectData for $ty {
            fn transform(&self) -> &$crate::models::transform::Transform {
                &self.unit.transform
            }
            fn transform_mut(&mut self) -> &mut $crate::models::transform::Transform {
                &mut self.unit.transform
            }
        }
        impl $crate::models::unit::BaseUnitData for $ty {
            fn name(&self) -> &str {
                &self.unit.name
            }
            fn template_id(&self) -> u32 {
                self.unit.template_id
            }
            fn faction_id(&self) -> u32 {
                self.unit.faction_id
            }
            fn scale(&self) -> f32 {
                self.unit.scale
            }
            fn buffs(&self) -> &$crate::models::buff::Buffs {
                &self.unit.buffs
            }
            fn buffs_mut(&mut self) -> &mut $crate::models::buff::Buffs {
                &mut self.unit.buffs
            }
        }
        impl $crate::models::unit::UnitData for $ty {
            fn unit(&self) -> &$crate::models::unit::UnitBase {
                &self.unit
            }
            fn unit_mut(&mut self) -> &mut $crate::models::unit::UnitBase {
                &mut self.unit
            }
            fn base_unit_type(&self) -> $crate::models::unit::BaseUnitType {
                $kind
            }
            fn type_flag(&self) -> u32 {
                $flag
            }
            $($extra)*
        }
    };
}

#[cfg(test)]
mod tests {
    use super::*;

    fn bonus(attr: UnitAttribute, kind: UnitModifierType, v: i32) -> Bonus {
        Bonus {
            template: BonusTemplate {
                attribute: attr,
                modifier_type: kind,
                value: v,
                linear_level_bonus: 0,
            },
            value: v,
        }
    }

    #[test]
    fn calculate_with_bonuses_order() {
        let mut b = Bonuses::default();
        b.add_bonus(
            3,
            bonus(UnitAttribute::MaxHealth, UnitModifierType::Percent, 10),
        );
        b.add_bonus(
            3,
            bonus(UnitAttribute::MaxHealth, UnitModifierType::Value, 100),
        );
        b.add_bonus(4, bonus(UnitAttribute::Armor, UnitModifierType::Value, 7));
        b.add_dynamic_bonus(
            5,
            DynamicBonus {
                template: DynamicBonusTemplate {
                    attribute: UnitAttribute::MaxHealth,
                    modifier_type: UnitModifierType::Value,
                    func_id: 1,
                    func_type: "LinearFunc".into(),
                },
                source_buff_index: 2,
                source_start_ms: 0,
                source_duration: 1000,
                linear_func: Some(LinearFuncTemplate {
                    id: 1,
                    start_value: 0,
                    end_value: 100,
                }),
            },
        );
        // (1000 + 100 + 50) * 1.1 at half time
        let v = b.calculate(1000.0, UnitAttribute::MaxHealth, 500);
        assert!((v - 1265.0).abs() < 1e-9, "{v}");
        b.remove_bonus(3, UnitAttribute::MaxHealth);
        b.remove_dynamic_bonus(5, UnitAttribute::MaxHealth);
        assert_eq!(b.calculate(1000.0, UnitAttribute::MaxHealth, 500), 1000.0);
        assert!(b.dynamics.is_empty());
        assert_eq!(b.bonuses(UnitAttribute::Armor).len(), 1);
    }

    #[test]
    fn cooldowns() {
        let mut c = UnitCooldowns::default();
        c.add_cooldown(5, 1000, 0);
        c.add_cooldown(5, 9000, 0); // kept
        assert!(c.check_cooldown(5, 700));
        assert!(!c.check_cooldown(5, 800)); // 200 ms left -> expired and removed
        assert!(c.cooldowns.is_empty());
    }

    #[test]
    fn modifier_caches() {
        let mut m = BuffModifiers::default();
        let rows = [
            BuffModifier {
                id: 1,
                owner_id: 100,
                tag_id: 0,
                buff_attribute: BuffAttribute::Duration,
                unit_modifier_type: UnitModifierType::Percent,
                value: 50,
                buff_id: 7,
                synergy: false,
            },
            BuffModifier {
                id: 2,
                owner_id: 100,
                tag_id: 9,
                buff_attribute: BuffAttribute::Duration,
                unit_modifier_type: UnitModifierType::Value,
                value: 100,
                buff_id: 0,
                synergy: false,
            },
        ];
        m.add_modifiers(&rows);
        // id 7: +50% -> 1500; tag 9: +100 -> 1600
        assert_eq!(m.apply(7, &[9], BuffAttribute::Duration, 1000.0), 1600.0);
        assert_eq!(m.apply(7, &[], BuffAttribute::InDuration, 1000.0), 1000.0);
        m.remove_modifiers(&rows);
        assert_eq!(m.apply(7, &[9], BuffAttribute::Duration, 1000.0), 1000.0);
        assert_eq!(
            UnitAttribute::from_u8(186),
            Some(UnitAttribute::ExpByLaborPowerMul)
        );
        assert_eq!(UnitAttribute::from_u8(9), None);
    }

    #[test]
    fn hpp() {
        let u = UnitBase {
            hp: 1,
            max_hp: 3,
            ..UnitBase::default()
        };
        assert_eq!(u.hpp(), 34);
        assert_eq!(UnitBase::default().hpp(), 0);
    }
}
