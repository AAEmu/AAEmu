//! Unit attribute getters (`Unit` virtual properties, the `Character` overrides evaluated through
//! `FormulaManager` + `CalculateWithBonuses` + equipment, the `Npc` overrides through the npc
//! subsystem hook). One call locks the object once and evaluates any number of attributes.

use std::collections::HashMap;

use crate::data::formulas::{FormulaOwnerType, UnitFormulaKind};
use crate::models::character::Character;
use crate::models::unit::{UnitAttribute, UnitBase};
use crate::server::GameServer;
use crate::world::{now_ms, WorldObject};

use super::ext::{GearStats, HoldableInfo};

/// `EquipmentItemSlot` values used by the skill code.
pub mod equip_slot {
    pub const MAINHAND: i32 = 15;
    pub const OFFHAND: i32 = 16;
    pub const RANGED: i32 = 17;
    pub const MUSICAL: i32 = 18;
}

/// `EquipmentItemSlotType.Shield` / `TwoHanded` / `OneHanded` / `Mainhand` / `Offhand`.
pub mod slot_type {
    pub const MAINHAND: u32 = 14;
    pub const OFFHAND: u32 = 15;
    pub const TWO_HANDED: u32 = 16;
    pub const ONE_HANDED: u32 = 17;
    pub const SHIELD: u32 = 20;
}

/// The attribute getters of `Unit` / `Character` / `Npc`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum StatKind {
    Str,
    Dex,
    Sta,
    Int,
    Spi,
    Fai,
    MaxHp,
    MaxMp,
    HpRegen,
    MpRegen,
    PersistentHpRegen,
    PersistentMpRegen,
    GlobalCooldownMul,
    CastTimeMul,
    MoveSpeedMul,
    LevelDps,
    Dps,
    DpsInc,
    OffhandDps,
    RangedDps,
    RangedDpsInc,
    MDps,
    MDpsInc,
    HDps,
    HDpsInc,
    MeleeAccuracy,
    MeleeCritical,
    MeleeCriticalBonus,
    MeleeCriticalMul,
    RangedAccuracy,
    RangedCritical,
    RangedCriticalBonus,
    RangedCriticalMul,
    SpellAccuracy,
    SpellCritical,
    SpellCriticalBonus,
    SpellCriticalMul,
    HealCritical,
    HealCriticalBonus,
    HealCriticalMul,
    Armor,
    MagicResistance,
    DefensePenetration,
    MagicPenetration,
    DodgeRate,
    MeleeParryRate,
    RangedParryRate,
    BlockRate,
    BattleResist,
    BullsEye,
    Flexibility,
    Facets,
    MeleeDamageMul,
    RangedDamageMul,
    SpellDamageMul,
    IncomingHealMul,
    HealMul,
    IncomingDamageMul,
    IncomingMeleeDamageMul,
    IncomingRangedDamageMul,
    IncomingSpellDamageMul,
    AggroMul,
    IncomingAggroMul,
    LungCapacity,
}

impl StatKind {
    /// `Unit.GetAttribute(UnitAttribute)`: the getter tagged with `[UnitAttribute(attr)]`.
    pub fn from_unit_attribute(a: UnitAttribute) -> Option<Self> {
        use StatKind as S;
        use UnitAttribute as U;
        Some(match a {
            U::Str => S::Str,
            U::Dex => S::Dex,
            U::Sta => S::Sta,
            U::Int => S::Int,
            U::Spi => S::Spi,
            U::Fai => S::Fai,
            U::MaxHealth => S::MaxHp,
            U::MaxMana => S::MaxMp,
            U::HealthRegen => S::HpRegen,
            U::ManaRegen => S::MpRegen,
            U::PersistentHealthRegen => S::PersistentHpRegen,
            U::PersistentManaRegen => S::PersistentMpRegen,
            U::GlobalCooldownMul => S::GlobalCooldownMul,
            U::CastingTimeMul => S::CastTimeMul,
            U::MoveSpeedMul => S::MoveSpeedMul,
            U::MainhandDps => S::Dps,
            U::MeleeDpsInc => S::DpsInc,
            U::OffhandDps => S::OffhandDps,
            U::RangedDps => S::RangedDps,
            U::RangedDpsInc => S::RangedDpsInc,
            U::SpellDps => S::MDps,
            U::SpellDpsInc => S::MDpsInc,
            U::HealDps => S::HDps,
            U::HealDpsInc => S::HDpsInc,
            U::MeleeAntiMiss | U::MeleeAntiMissMul => S::MeleeAccuracy,
            U::MeleeCritical => S::MeleeCritical,
            U::MeleeCriticalBonus => S::MeleeCriticalBonus,
            U::MeleeCriticalMul => S::MeleeCriticalMul,
            U::RangedAntiMiss => S::RangedAccuracy,
            U::RangedCritical => S::RangedCritical,
            U::RangedCriticalBonus => S::RangedCriticalBonus,
            U::RangedCriticalMul => S::RangedCriticalMul,
            U::SpellAntiMiss => S::SpellAccuracy,
            U::SpellCritical => S::SpellCritical,
            U::SpellCriticalBonus => S::SpellCriticalBonus,
            U::SpellCriticalMul => S::SpellCriticalMul,
            U::HealCritical => S::HealCritical,
            U::HealCriticalBonus => S::HealCriticalBonus,
            U::HealCriticalMul => S::HealCriticalMul,
            U::Armor => S::Armor,
            U::MagicResist => S::MagicResistance,
            U::IgnoreArmor => S::DefensePenetration,
            U::MagicPenetration => S::MagicPenetration,
            U::Dodge => S::DodgeRate,
            U::MeleeParry => S::MeleeParryRate,
            U::RangedParry => S::RangedParryRate,
            U::Block => S::BlockRate,
            U::BattleResist => S::BattleResist,
            U::BullsEye => S::BullsEye,
            U::Flexibility => S::Flexibility,
            U::Facets => S::Facets,
            U::MeleeDamageMul => S::MeleeDamageMul,
            U::RangedDamageMul => S::RangedDamageMul,
            U::SpellDamageMul => S::SpellDamageMul,
            U::IncomingHealMul => S::IncomingHealMul,
            U::HealMul => S::HealMul,
            U::IncomingDamageMul => S::IncomingDamageMul,
            U::IncomingMeleeDamageMul => S::IncomingMeleeDamageMul,
            U::IncomingRangedDamageMul => S::IncomingRangedDamageMul,
            U::IncomingSpellDamageMul => S::IncomingSpellDamageMul,
            U::AggroMul => S::AggroMul,
            U::IncomingAggroMul => S::IncomingAggroMul,
            U::LungCapacity => S::LungCapacity,
            _ => return None,
        })
    }

    /// Whether the C# getter is an `int` (the others are `float`).
    pub fn is_int(self) -> bool {
        use StatKind as S;
        matches!(
            self,
            S::Str
                | S::Dex
                | S::Sta
                | S::Int
                | S::Spi
                | S::Fai
                | S::MaxHp
                | S::MaxMp
                | S::HpRegen
                | S::MpRegen
                | S::PersistentHpRegen
                | S::PersistentMpRegen
                | S::Dps
                | S::DpsInc
                | S::OffhandDps
                | S::RangedDps
                | S::RangedDpsInc
                | S::MDps
                | S::MDpsInc
                | S::HDps
                | S::HDpsInc
                | S::Armor
                | S::MagicResistance
                | S::DefensePenetration
                | S::MagicPenetration
                | S::BattleResist
                | S::BullsEye
                | S::Flexibility
                | S::Facets
                | S::LungCapacity
        )
    }
}

/// The base `Unit` getter values (no override).
pub fn base_unit_stat(u: &UnitBase, kind: StatKind, now: i64) -> f64 {
    use StatKind as S;
    match kind {
        S::MoveSpeedMul => {
            (u.calculate_with_bonuses(1000.0, UnitAttribute::MoveSpeedMul, now) as f32 / 1000.0)
                as f64
        }
        S::GlobalCooldownMul => f64::from(u.global_cooldown_mul),
        S::MaxHp => f64::from(u.max_hp),
        S::MaxMp => f64::from(u.max_mp),
        S::HpRegen => f64::from(u.hp_regen),
        S::MpRegen => f64::from(u.mp_regen),
        S::PersistentHpRegen => f64::from(u.persistent_hp_regen),
        S::PersistentMpRegen => f64::from(u.persistent_mp_regen),
        S::CastTimeMul => f64::from(u.cast_time_mul),
        S::MeleeAccuracy | S::RangedAccuracy | S::SpellAccuracy => 100.0,
        S::MeleeCriticalMul | S::RangedCriticalMul | S::SpellCriticalMul => 1.0,
        S::MeleeDamageMul
        | S::RangedDamageMul
        | S::SpellDamageMul
        | S::IncomingHealMul
        | S::HealMul
        | S::IncomingDamageMul
        | S::IncomingMeleeDamageMul
        | S::IncomingRangedDamageMul
        | S::IncomingSpellDamageMul => 1.0,
        S::AggroMul => u.calculate_with_bonuses(100.0, UnitAttribute::AggroMul, now) as f32 as f64,
        S::IncomingAggroMul => {
            u.calculate_with_bonuses(100.0, UnitAttribute::IncomingAggroMul, now) as f32 as f64
        }
        _ => 0.0,
    }
}

/// Inputs of the `Character` getters besides the character itself.
#[derive(Debug, Clone, Copy, Default)]
pub struct CharInputs {
    pub gear: GearStats,
    /// The offhand slot: `None` = empty, `Some(None)` = an item that is no weapon,
    /// `Some(Some(holdable))` = a weapon.
    pub offhand: Option<Option<HoldableInfo>>,
}

/// Lazy evaluator of the `Character` attribute getters.
pub struct CharStats<'a> {
    server: &'a GameServer,
    c: &'a Character,
    inputs: CharInputs,
    now: i64,
    primaries: Option<[i32; 6]>,
    facets: Option<i32>,
}

impl<'a> CharStats<'a> {
    pub fn new(server: &'a GameServer, c: &'a Character, inputs: CharInputs, now: i64) -> Self {
        Self {
            server,
            c,
            inputs,
            now,
            primaries: None,
            facets: None,
        }
    }

    fn cwb(&self, v: f64, a: UnitAttribute) -> f64 {
        self.c.unit.calculate_with_bonuses(v, a, self.now)
    }

    fn formula(&self, kind: UnitFormulaKind, params: &HashMap<&str, f64>) -> f64 {
        match self
            .server
            .game_data
            .formulas
            .get_unit_formula(FormulaOwnerType::Character, kind)
        {
            Some(f) => f.formula.evaluate(params),
            None => {
                tracing::debug!("No character formula for {kind:?}");
                0.0
            }
        }
    }

    fn level(&self) -> f64 {
        f64::from(self.c.unit.level)
    }

    /// `Str` .. `Fai` (`[str, dex, sta, int, spi, fai]`).
    pub fn primaries(&mut self) -> [i32; 6] {
        if let Some(p) = self.primaries {
            return p;
        }
        let lv = HashMap::from([("level", self.level())]);
        let g = self.inputs.gear;
        let p = [
            self.cwb(
                self.formula(UnitFormulaKind::Str, &lv) + g.str_,
                UnitAttribute::Str,
            ) as i32,
            self.cwb(
                self.formula(UnitFormulaKind::Dex, &lv) + g.dex,
                UnitAttribute::Dex,
            ) as i32,
            self.cwb(
                self.formula(UnitFormulaKind::Sta, &lv) + g.sta,
                UnitAttribute::Sta,
            ) as i32,
            self.cwb(
                self.formula(UnitFormulaKind::Int, &lv) + g.int,
                UnitAttribute::Int,
            ) as i32,
            self.cwb(
                self.formula(UnitFormulaKind::Spi, &lv) + g.spi,
                UnitAttribute::Spi,
            ) as i32,
            self.cwb(self.formula(UnitFormulaKind::Fai, &lv), UnitAttribute::Fai) as i32,
        ];
        self.primaries = Some(p);
        p
    }

    fn all_params(&mut self) -> HashMap<&'static str, f64> {
        let p = self.primaries();
        HashMap::from([
            ("level", self.level()),
            ("str", f64::from(p[0])),
            ("dex", f64::from(p[1])),
            ("sta", f64::from(p[2])),
            ("int", f64::from(p[3])),
            ("spi", f64::from(p[4])),
            ("fai", f64::from(p[5])),
        ])
    }

    fn facets(&mut self) -> i32 {
        if let Some(f) = self.facets {
            return f;
        }
        let lv = HashMap::from([("level", self.level())]);
        let f = self.cwb(
            self.formula(UnitFormulaKind::Facet, &lv),
            UnitAttribute::Facets,
        ) as i32;
        self.facets = Some(f);
        f
    }

    /// `(1f - (Facets / 10f - res) * (1f / Facets)) * 100f` clamped to [0, 100].
    fn accuracy(&mut self, res: f64) -> f64 {
        let facets = self.facets() as f32;
        let mut res = (1.0 - (f64::from(facets / 10.0) - res) * f64::from(1.0f32 / facets)) * 100.0;
        res = (res + 100.0 - (res - 100.0).abs()) / 2.0;
        res = (res.abs() + res) / 2.0;
        res as f32 as f64
    }

    fn rate(&mut self, res: f64) -> f64 {
        let facets = self.facets() as f32;
        res * f64::from(1.0f32 / facets) * 100.0
    }

    /// One `Character` getter.
    pub fn get(&mut self, kind: StatKind) -> f64 {
        use StatKind as S;
        use UnitAttribute as A;
        use UnitFormulaKind as F;
        let p = self.primaries();
        let [str_, dex, sta, int, spi, _fai] = p.map(f64::from);
        match kind {
            S::Str => str_,
            S::Dex => dex,
            S::Sta => sta,
            S::Int => int,
            S::Spi => spi,
            S::Fai => f64::from(p[5]),
            S::GlobalCooldownMul => {
                let res = self.cwb(0.0, A::GlobalCooldownMul);
                f64::from((100000.0f64 / (res + 1000.0)) as i32)
            }
            S::MaxHp => {
                let pa = self.all_params();
                f64::from(self.cwb(self.formula(F::MaxHealth, &pa), A::MaxHealth) as i32)
            }
            S::HpRegen => {
                let pa = self.all_params();
                f64::from(self.cwb(self.formula(F::HealthRegen, &pa), A::HealthRegen) as i32)
            }
            S::PersistentHpRegen => {
                let pa = self.all_params();
                let res = self.cwb(
                    self.formula(F::PersistentHealthRegen, &pa),
                    A::PersistentHealthRegen,
                ) / 5.0;
                f64::from(res as i32)
            }
            S::MaxMp => {
                let pa = self.all_params();
                f64::from(self.cwb(self.formula(F::MaxMana, &pa), A::MaxMana) as i32)
            }
            S::MpRegen => {
                let pa = self.all_params();
                let res = self.formula(F::ManaRegen, &pa) + spi / 10.0;
                f64::from(self.cwb(res, A::ManaRegen) as i32)
            }
            S::PersistentMpRegen => {
                let pa = self.all_params();
                let res = self.formula(F::PersistentManaRegen, &pa) / 5.0;
                f64::from(self.cwb(res, A::PersistentManaRegen) as i32)
            }
            S::IncomingDamageMul => {
                (1.0 + self.cwb(0.0, A::IncomingDamageMul) / 1000.0) as f32 as f64
            }
            S::IncomingMeleeDamageMul => {
                let res = self.cwb(
                    self.cwb(0.0, A::IncomingMeleeDamageMul),
                    A::IncomingDamageMul,
                );
                (1.0 + res / 1000.0) as f32 as f64
            }
            S::IncomingRangedDamageMul => {
                let res = self.cwb(
                    self.cwb(0.0, A::IncomingRangedDamageMul),
                    A::IncomingDamageMul,
                );
                (1.0 + res / 1000.0) as f32 as f64
            }
            S::IncomingSpellDamageMul => {
                let res = self.cwb(
                    self.cwb(0.0, A::IncomingSpellDamageMul),
                    A::IncomingDamageMul,
                );
                (1.0 + res / 1000.0) as f32 as f64
            }
            S::CastTimeMul => {
                let res = (self.cwb(0.0, A::CastingTimeMul) + 1000.0) / 1000.0;
                res.max(0.0) as f32 as f64
            }
            S::MeleeDamageMul => {
                ((self.cwb(0.0, A::MeleeDamageMul) + 1000.0) / 1000.0) as f32 as f64
            }
            S::RangedDamageMul => {
                ((self.cwb(0.0, A::RangedDamageMul) + 1000.0) / 1000.0) as f32 as f64
            }
            S::SpellDamageMul => {
                ((self.cwb(0.0, A::SpellDamageMul) + 1000.0) / 1000.0) as f32 as f64
            }
            S::IncomingHealMul => {
                ((self.cwb(0.0, A::IncomingHealMul) + 1000.0) / 1000.0) as f32 as f64
            }
            S::HealMul => ((self.cwb(0.0, A::HealMul) + 1000.0) / 1000.0) as f32 as f64,
            S::LevelDps => {
                let mut pa = self.all_params();
                pa.insert("ab_level", self.level());
                self.formula(F::LevelDps, &pa) as f32 as f64
            }
            S::Dps => {
                let res = self.inputs.gear.mainhand_dps * 1000.0 + str_ as f32 / 5.0 * 1000.0;
                f64::from(self.cwb(f64::from(res), A::MainhandDps) as f32 as i32)
            }
            S::DpsInc => {
                let pa = self.all_params();
                f64::from(self.cwb(self.formula(F::MeleeDpsInc, &pa), A::MeleeDpsInc) as i32)
            }
            S::OffhandDps => {
                let res = self.cwb(f64::from(self.inputs.gear.offhand_dps), A::OffhandDps) as f32;
                f64::from((res * 1000.0) as i32)
            }
            S::RangedDps => {
                let res = self.inputs.gear.ranged_dps * 1000.0 + dex as f32 / 5.0 * 1000.0;
                f64::from(self.cwb(f64::from(res), A::RangedDps) as f32 as i32)
            }
            S::RangedDpsInc => {
                let pa = self.all_params();
                f64::from(self.cwb(self.formula(F::RangedDpsInc, &pa), A::RangedDpsInc) as i32)
            }
            S::MDps => {
                let res = self.inputs.gear.mainhand_mdps * 1000.0 + int as f32 / 5.0 * 1000.0;
                f64::from(self.cwb(f64::from(res), A::SpellDps) as f32 as i32)
            }
            S::MDpsInc => {
                let pa = self.all_params();
                f64::from(self.cwb(self.formula(F::SpellDpsInc, &pa), A::SpellDpsInc) as i32)
            }
            S::HDps => {
                let res = f64::from(self.inputs.gear.mainhand_hdps * 1000.0)
                    + f64::from(spi as f32 / 5.0 * 1000.0);
                f64::from(self.cwb(res, A::HealDps) as i32)
            }
            S::HDpsInc => {
                let pa = HashMap::from([("level", self.level()), ("spi", spi)]);
                f64::from(self.cwb(self.formula(F::HealDpsInc, &pa), A::HealDpsInc) as i32)
            }
            S::MeleeAccuracy => {
                let pa = HashMap::from([("str", str_), ("spi", spi)]);
                let res = self.cwb(self.formula(F::MeleeAntiMiss, &pa), A::MeleeAntiMiss);
                self.accuracy(res)
            }
            S::MeleeCritical => {
                let pa = HashMap::from([("str", str_), ("dex", dex)]);
                let res = self.cwb(self.formula(F::MeleeCritical, &pa), A::MeleeCritical);
                let res = self.rate(res) + self.get(S::MeleeCriticalMul) / 10.0;
                res as f32 as f64
            }
            S::MeleeCriticalBonus => {
                let res = self.cwb(1500.0, A::MeleeCriticalBonus) as f32;
                f64::from((res - 1000.0) / 10.0)
            }
            S::MeleeCriticalMul => self.cwb(0.0, A::MeleeCriticalMul) as f32 as f64,
            S::RangedAccuracy => {
                let pa = HashMap::from([("dex", dex), ("spi", spi)]);
                let res = self.cwb(self.formula(F::RangedAntiMiss, &pa), A::RangedAntiMiss);
                self.accuracy(res)
            }
            S::RangedCritical => {
                let pa = HashMap::from([("dex", dex), ("int", int)]);
                let res = self.cwb(self.formula(F::RangedCritical, &pa), A::RangedCritical);
                let res = self.rate(res) + self.get(S::RangedCriticalMul) / 10.0;
                res as f32 as f64
            }
            S::RangedCriticalBonus => {
                let res = self.cwb(1500.0, A::RangedCriticalBonus) as f32;
                f64::from((res - 1000.0) / 10.0)
            }
            S::RangedCriticalMul => self.cwb(0.0, A::RangedCriticalMul) as f32 as f64,
            S::SpellAccuracy => {
                let pa = HashMap::from([("int", int), ("spi", spi)]);
                let res = self.cwb(self.formula(F::SpellAntiMiss, &pa), A::SpellAntiMiss);
                self.accuracy(res)
            }
            S::SpellCritical => {
                let pa = HashMap::from([("int", int)]);
                let res = self.cwb(self.formula(F::SpellCritical, &pa), A::SpellCritical);
                let res = self.cwb(res, A::SpellDamageCritical) as f32 as f64;
                let res = self.rate(res) + self.get(S::SpellCriticalMul) / 10.0;
                res as f32 as f64
            }
            S::SpellCriticalBonus => {
                let res = self.cwb(1500.0, A::SpellCriticalBonus) as f32;
                let res = self.cwb(f64::from(res), A::SpellDamageCriticalBonus) as f32;
                f64::from((res - 1000.0) / 10.0)
            }
            S::SpellCriticalMul => {
                let res = self.cwb(0.0, A::SpellCriticalMul);
                self.cwb(res, A::SpellDamageCriticalMul) as f32 as f64
            }
            S::HealCritical => {
                let pa = HashMap::from([("spi", spi)]);
                let res = self.cwb(self.formula(F::HealCritical, &pa), A::HealCritical);
                let res = self.rate(res) + self.get(S::HealCriticalMul) / 10.0;
                res as f32 as f64
            }
            S::HealCriticalBonus => {
                let res = self.cwb(1500.0, A::HealCriticalBonus) as f32;
                f64::from((res - 1000.0) / 10.0)
            }
            S::HealCriticalMul => self.cwb(0.0, A::HealCriticalMul) as f32 as f64,
            S::Armor => {
                let pa = self.all_params();
                let res = self.formula(F::Armor, &pa) as i32 + self.inputs.gear.armor;
                f64::from(self.cwb(f64::from(res), A::Armor) as i32)
            }
            S::MagicResistance => {
                let pa = self.all_params();
                let res =
                    self.formula(F::MagicResist, &pa) as i32 + self.inputs.gear.magic_resistance;
                f64::from(self.cwb(f64::from(res), A::MagicResist) as i32)
            }
            S::DefensePenetration => f64::from(self.cwb(0.0, A::IgnoreArmor) as i32),
            S::MagicPenetration => f64::from(self.cwb(0.0, A::MagicPenetration) as i32),
            S::BattleResist => f64::from(self.cwb(0.0, A::BattleResist) as i32),
            S::BullsEye => f64::from(self.cwb(0.0, A::BullsEye) as i32),
            S::Flexibility => f64::from(self.cwb(0.0, A::Flexibility) as i32),
            S::Facets => f64::from(self.facets()),
            S::DodgeRate => {
                let pa = HashMap::from([("dex", dex), ("int", int)]);
                let res = self.cwb(self.formula(F::Dodge, &pa), A::Dodge);
                let res = self.rate(res) + self.cwb(0.0, A::DodgeMul) / 10.0;
                res as f32 as f64
            }
            S::MeleeParryRate => {
                let pa = HashMap::from([("str", str_), ("sta", sta)]);
                let res = self.cwb(self.formula(F::MeleeParry, &pa), A::MeleeParry);
                let res = self.rate(res) + self.cwb(0.0, A::MeleeParryMul) / 10.0;
                res as f32 as f64
            }
            S::RangedParryRate => {
                let res = self.cwb(0.0, A::RangedParry);
                let res = self.rate(res) + self.cwb(0.0, A::RangedParryMul) / 10.0;
                res as f32 as f64
            }
            S::BlockRate => {
                match self.inputs.offhand {
                    None => return 0.0,
                    Some(Some(h)) if h.slot_type_id != slot_type::SHIELD => return 0.0,
                    _ => {}
                }
                let pa = HashMap::from([("str", str_)]);
                let res = self.cwb(self.formula(F::Block, &pa), A::Block);
                let res = self.rate(res) + self.cwb(0.0, A::BlockMul) / 10.0;
                res as f32 as f64
            }
            S::LungCapacity => f64::from(self.cwb(60000.0, A::LungCapacity) as u32),
            S::MoveSpeedMul | S::AggroMul | S::IncomingAggroMul => {
                base_unit_stat(&self.c.unit, kind, self.now)
            }
        }
    }
}

/// Character inputs from the item subsystem (taken before the character is locked).
pub fn char_inputs(server: &GameServer, p: &crate::world::PlayerRef) -> CharInputs {
    let items = server.systems.skills.ext.items();
    let obj = WorldObject::from(p);
    let offhand = if items.has_equipped(server, p, equip_slot::OFFHAND) {
        Some(items.weapon_holdable(server, &obj, equip_slot::OFFHAND))
    } else {
        None
    };
    CharInputs {
        gear: items.gear_stats(server, p),
        offhand,
    }
}

/// Evaluates `kinds` for one object (locks it once). Doodads and non-units give 0.
pub fn stats(server: &GameServer, obj: &WorldObject, kinds: &[StatKind]) -> Vec<f64> {
    let now = now_ms();
    match obj {
        WorldObject::Player(p) => {
            let inputs = char_inputs(server, p);
            let c = p.data();
            let mut cs = CharStats::new(server, &c, inputs, now);
            kinds.iter().map(|k| cs.get(*k)).collect()
        }
        WorldObject::Npc(n) => {
            let hook = server.systems.skills.ext.npcs();
            let npc = n.data();
            kinds
                .iter()
                .map(|k| {
                    hook.npc_attribute(server, &npc, *k)
                        .unwrap_or_else(|| base_unit_stat(&npc.unit, *k, now))
                })
                .collect()
        }
        other => other
            .with_unit(|u| {
                kinds
                    .iter()
                    .map(|k| base_unit_stat(u.unit(), *k, now))
                    .collect()
            })
            .unwrap_or_else(|| vec![0.0; kinds.len()]),
    }
}

/// One attribute.
pub fn stat(server: &GameServer, obj: &WorldObject, kind: StatKind) -> f64 {
    stats(server, obj, &[kind])[0]
}

/// Recomputes the cached `MaxHp` / `MaxMp` (and the other cached getter values shared code
/// reads: regen, cast time and global cooldown multipliers) of a unit after its bonuses
/// changed. HP / MP are not clamped (the C# getters never clamp either).
pub fn refresh_cached_stats(server: &GameServer, obj: &WorldObject) {
    if !obj.is_unit() {
        return;
    }
    let v = stats(
        server,
        obj,
        &[
            StatKind::MaxHp,
            StatKind::MaxMp,
            StatKind::HpRegen,
            StatKind::MpRegen,
            StatKind::PersistentHpRegen,
            StatKind::PersistentMpRegen,
            StatKind::GlobalCooldownMul,
            StatKind::CastTimeMul,
        ],
    );
    if obj.as_player().is_none() && obj.as_npc().is_none() {
        return; // base units: the cached fields are the getters themselves
    }
    obj.with_unit(|u| {
        let u = u.unit_mut();
        u.max_hp = v[0] as i32;
        u.max_mp = v[1] as i32;
        u.hp_regen = v[2] as i32;
        u.mp_regen = v[3] as i32;
        u.persistent_hp_regen = v[4] as i32;
        u.persistent_mp_regen = v[5] as i32;
        u.global_cooldown_mul = v[6] as f32;
        u.cast_time_mul = v[7] as f32;
    });
}
/// The `Bonuses[GearBonusesIndex]` list of `UpdateGearBonuses`: the unit modifiers of the
/// equipped, non-destroyed items and their gems.
pub fn gear_bonuses<'a>(
    server: &GameServer,
    items: impl IntoIterator<Item = &'a crate::models::item::Item>,
) -> Vec<crate::models::unit::Bonus> {
    use crate::models::unit::Bonus;
    let data = &server.game_data;
    let mut bonuses = Vec::new();
    for item in items {
        if !item.class.is_equip() {
            continue;
        }
        let max = data
            .items
            .templates
            .get(&item.template_id)
            .and_then(|t| data.items.max_durability(t, i32::from(item.grade)));
        let not_destroyed = max.is_none_or(|m| m == 0) || item.details.durability > 0;
        if !not_destroyed {
            continue;
        }
        let mods = &data.systems.skills.item_unit_modifiers;
        for t in mods.get(&item.template_id).into_iter().flatten() {
            bonuses.push(Bonus {
                template: t.clone(),
                value: t.value,
            });
        }
        for gem in item.details.gem_ids {
            for t in mods.get(&gem).into_iter().flatten() {
                bonuses.push(Bonus {
                    template: t.clone(),
                    value: t.value,
                });
            }
        }
    }
    bonuses
}

/// Replaces the `GearBonusesIndex` static bonus list of a unit.
fn set_gear_bonuses(
    unit: &mut crate::models::unit::UnitBase,
    bonuses: Vec<crate::models::unit::Bonus>,
) {
    use crate::models::buff::GEAR_BONUSES_INDEX;
    let statics = &mut unit.bonuses.statics;
    match statics.iter_mut().find(|(i, _)| *i == GEAR_BONUSES_INDEX) {
        Some((_, l)) => *l = bonuses,
        None => statics.push((GEAR_BONUSES_INDEX, bonuses)),
    }
}

/// `Unit.UpdateGearBonuses(itemAdded, itemRemoved)`: the unit modifiers of the equipped,
/// non-destroyed items and their gems (`Bonuses[GearBonusesIndex]`), then the gear buffs
/// (equip effects, weapon wield, armor grade, item sets: item subsystem hook).
pub fn update_gear_bonuses(
    server: &GameServer,
    p: &crate::world::PlayerRef,
    added: Option<&crate::models::item::Item>,
    removed: Option<&crate::models::item::Item>,
) {
    let items: Vec<crate::models::item::Item> = {
        let c = p.data();
        c.inventory
            .equipment_slots()
            .into_iter()
            .flatten()
            .collect()
    };
    let bonuses = gear_bonuses(server, &items);
    set_gear_bonuses(&mut p.data().unit, bonuses);
    server
        .systems
        .skills
        .ext
        .items()
        .apply_gear_buffs(server, p, added, removed);
    refresh_cached_stats(server, &WorldObject::from(p));
}

/// `Unit.UpdateGearBonuses` for an NPC (its `Equipment`): the gear unit modifiers only (the
/// item-side gear buffs are the item subsystem's and only apply to characters). The cached
/// stats are refreshed by the NPC subsystem's getters.
pub fn update_npc_gear_bonuses(server: &GameServer, npc: &crate::world::NpcRef) {
    let mut n = npc.data();
    let bonuses = gear_bonuses(server, n.equipment.iter().flatten());
    set_gear_bonuses(&mut n.unit, bonuses);
    crate::systems::npcs::npc::stats::refresh_cached_stats(&server.game_data, &mut n);
}
