//! The `Npc` attribute getters (`Models/Game/NPChar/Npc.cs`, `#region Attributes`): `Str`,
//! `Dex`, `Sta`, `Int`, `Spi`, `Fai`, `MaxHp`, `HpRegen`, `PersistentHpRegen`, `MaxMp`,
//! `MpRegen`, `PersistentMpRegen`, `LevelDps`, `Dps`, `DpsInc`, `OffhandDps`, `RangedDps`,
//! `RangedDpsInc`, `MDps`, `MDpsInc`, `Armor`, `MagicResistance`, `KillExp`.
//!
//! Each getter evaluates the `FormulaOwnerType.Npc` unit formula with `level`, the base
//! attributes (derived getters) and the formula's `npc_template` / `npc_kind` / `npc_grade`
//! variables, then applies the unit's static bonuses (`GetBonuses(attr)`, in insertion order)
//! one by one with the C# arithmetic: an int getter adds `(int)(res * value / 100f)` for a
//! percent bonus (float math), a double getter adds `res * value / 100f`. Dynamic bonuses are not
//! read by these getters (the C# does not call `CalculateWithBonuses` here).
//!
//! Deviation: a missing formula evaluates to 0 (C#: `NullReferenceException` on `formula.Id`),
//! and a non-weapon item in a weapon slot counts as no weapon (C#: `InvalidCastException`).

use std::collections::HashMap;

use crate::data::formulas::{
    FormulaManager, FormulaOwnerType, UnitFormulaKind, UnitFormulaVariableType,
};
use crate::data::GameData;
use crate::models::container::equip_slot;
use crate::models::item::Item;
use crate::models::npc::Npc;
use crate::models::unit::{Bonuses, UnitAttribute, UnitModifierType};

use super::data::{NpcData, NpcTemplate};

/// The values of every NPC attribute getter.
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct NpcStats {
    pub str_: i32,
    pub dex: i32,
    pub sta: i32,
    pub int: i32,
    pub spi: i32,
    pub fai: i32,
    pub max_hp: i32,
    pub hp_regen: i32,
    pub persistent_hp_regen: i32,
    pub max_mp: i32,
    pub mp_regen: i32,
    pub persistent_mp_regen: i32,
    pub level_dps: f32,
    pub dps: i32,
    pub dps_inc: i32,
    pub offhand_dps: i32,
    pub ranged_dps: i32,
    pub ranged_dps_inc: i32,
    pub mdps: i32,
    pub mdps_inc: i32,
    pub armor: i32,
    pub magic_resistance: i32,
    pub kill_exp: i32,
}

/// `Weapon.Dps` / `Weapon.MDps` of an equipped item (`None` = no weapon).
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct WeaponDps {
    pub dps: f32,
    pub mdps: f64,
}

/// `Weapon.Dps` and `Weapon.MDps` for `item` (`None` when it is not a weapon).
pub fn weapon_dps(data: &NpcData, item: &Item) -> Option<WeaponDps> {
    let t = data.weapons.get(&item.template_id)?;
    let (gd, gm) = data
        .grade_holdable_dps
        .get(&i32::from(item.grade))
        .copied()
        .unwrap_or((0.0, 0.0));
    let level = f64::from(t.level);
    let p = HashMap::from([("item_level", level), ("item_grade", f64::from(gd))]);
    let mut dps = t.formula_dps.evaluate(&p);
    if item.details.temper_physical > 100 {
        dps *= f64::from(f32::from(item.details.temper_physical) / 100.0);
    }
    let p = HashMap::from([("item_level", level), ("item_grade", f64::from(gm))]);
    let mut mdps = t.formula_mdps.evaluate(&p);
    if item.details.temper_magical > 100 {
        mdps *= f64::from(f32::from(item.details.temper_magical) / 100.0);
    }
    Some(WeaponDps {
        dps: dps as f32,
        mdps,
    })
}

/// Inputs of the getters: the template, the unit's level and static bonuses, and the weapons.
pub struct NpcStatCalc<'a> {
    pub formulas: &'a FormulaManager,
    pub template: &'a NpcTemplate,
    pub level: u8,
    pub bonuses: &'a Bonuses,
    pub mainhand: Option<WeaponDps>,
    pub offhand: Option<WeaponDps>,
    pub ranged: Option<WeaponDps>,
}

/// The int-getter bonus loop.
fn apply_int(bonuses: &Bonuses, attr: UnitAttribute, mut res: i32) -> i32 {
    for b in bonuses.bonuses(attr) {
        if b.template.modifier_type == UnitModifierType::Percent {
            res = res.wrapping_add((res.wrapping_mul(b.value) as f32 / 100.0) as i32);
        } else {
            res = res.wrapping_add(b.value);
        }
    }
    res
}

/// The double-getter bonus loop (`DpsInc`, `RangedDpsInc`, `MDpsInc`).
fn apply_f64(bonuses: &Bonuses, attr: UnitAttribute, mut res: f64) -> f64 {
    for b in bonuses.bonuses(attr) {
        if b.template.modifier_type == UnitModifierType::Percent {
            res += res * f64::from(b.value) / 100.0;
        } else {
            res += f64::from(b.value);
        }
    }
    res
}

/// Base attributes passed to the derived formulas.
#[derive(Clone, Copy)]
struct Base {
    str_: i32,
    dex: i32,
    sta: i32,
    int: i32,
    spi: i32,
    fai: i32,
}

impl NpcStatCalc<'_> {
    /// `FormulaManager.GetUnitFormula(Npc, kind).Evaluate(parameters)`.
    fn eval(&self, kind: UnitFormulaKind, base: Option<Base>, ab_level: bool) -> f64 {
        let Some(formula) = self.formulas.get_unit_formula(FormulaOwnerType::Npc, kind) else {
            tracing::debug!("No npc formula for {kind:?}");
            return 0.0;
        };
        let id = formula.formula.id;
        let var = |ty, key: u8| f64::from(self.formulas.get_unit_variable(id, ty, u32::from(key)));
        let mut p: HashMap<&str, f64> = HashMap::from([("level", f64::from(self.level))]);
        if let Some(b) = base {
            p.insert("str", f64::from(b.str_));
            p.insert("dex", f64::from(b.dex));
            p.insert("sta", f64::from(b.sta));
            p.insert("int", f64::from(b.int));
            p.insert("spi", f64::from(b.spi));
            p.insert("fai", f64::from(b.fai));
        }
        if ab_level {
            p.insert("ab_level", 0.0);
        }
        p.insert(
            "npc_template",
            var(
                UnitFormulaVariableType::NpcTemplate,
                self.template.npc_template_id,
            ),
        );
        p.insert(
            "npc_kind",
            var(UnitFormulaVariableType::NpcKind, self.template.npc_kind_id),
        );
        p.insert(
            "npc_grade",
            var(
                UnitFormulaVariableType::NpcGrade,
                self.template.npc_grade_id,
            ),
        );
        formula.evaluate(&p)
    }

    fn base_attr(&self, kind: UnitFormulaKind, attr: UnitAttribute) -> i32 {
        apply_int(self.bonuses, attr, self.eval(kind, None, false) as i32)
    }

    fn base(&self) -> Base {
        Base {
            str_: self.base_attr(UnitFormulaKind::Str, UnitAttribute::Str),
            dex: self.base_attr(UnitFormulaKind::Dex, UnitAttribute::Dex),
            sta: self.base_attr(UnitFormulaKind::Sta, UnitAttribute::Sta),
            int: self.base_attr(UnitFormulaKind::Int, UnitAttribute::Int),
            spi: self.base_attr(UnitFormulaKind::Spi, UnitAttribute::Spi),
            fai: self.base_attr(UnitFormulaKind::Fai, UnitAttribute::Fai),
        }
    }

    fn derived_int(&self, b: Base, kind: UnitFormulaKind, attr: UnitAttribute, add: i32) -> i32 {
        let res = (self.eval(kind, Some(b), false) as i32).wrapping_add(add);
        apply_int(self.bonuses, attr, res)
    }

    fn derived_f64(&self, b: Base, kind: UnitFormulaKind, attr: UnitAttribute) -> i32 {
        apply_f64(self.bonuses, attr, self.eval(kind, Some(b), false)) as i32
    }

    /// `Dps` / `OffhandDps` / `RangedDps`: float `weapon.Dps + stat / 10f` with the bonuses,
    /// `(int)(res * 1000)`.
    fn weapon_getter(&self, w: Option<WeaponDps>, stat: i32, attr: UnitAttribute) -> i32 {
        let mut res: f32 = w.map_or(0.0, |w| w.dps);
        res += stat as f32 / 10.0;
        for b in self.bonuses.bonuses(attr) {
            if b.template.modifier_type == UnitModifierType::Percent {
                res += ((res * b.value as f32) / 100.0) as i32 as f32;
            } else {
                res += b.value as f32;
            }
        }
        (res * 1000.0) as i32
    }

    /// Every getter.
    pub fn compute(&self) -> NpcStats {
        use UnitAttribute as A;
        use UnitFormulaKind as K;
        let b = self.base();
        let mdps = {
            let mut res: f64 = self.mainhand.map_or(0.0, |w| w.mdps);
            res += f64::from(b.int as f32 / 10.0);
            for bo in self.bonuses.bonuses(A::SpellDps) {
                if bo.template.modifier_type == UnitModifierType::Percent {
                    res += (res * f64::from(bo.value) / 100.0) as i32 as f64;
                } else {
                    res += f64::from(bo.value);
                }
            }
            (res * 1000.0) as i32
        };
        let kill_exp = if self.template.no_exp {
            0
        } else {
            let mut res = self.eval(K::KillExp, Some(b), false);
            res *= f64::from(self.template.exp_multiplier);
            res += f64::from(self.template.exp_adder);
            res as i32
        };
        NpcStats {
            str_: b.str_,
            dex: b.dex,
            sta: b.sta,
            int: b.int,
            spi: b.spi,
            fai: b.fai,
            max_hp: self.derived_int(b, K::MaxHealth, A::MaxHealth, 0),
            hp_regen: self.derived_int(b, K::HealthRegen, A::HealthRegen, b.spi / 10),
            persistent_hp_regen: self.derived_int(
                b,
                K::PersistentHealthRegen,
                A::PersistentHealthRegen,
                0,
            ),
            max_mp: self.derived_int(b, K::MaxMana, A::MaxMana, 0),
            mp_regen: self.derived_int(b, K::ManaRegen, A::ManaRegen, b.spi / 10),
            persistent_mp_regen: self.derived_int(
                b,
                K::PersistentManaRegen,
                A::PersistentManaRegen,
                0,
            ),
            level_dps: self.eval(K::LevelDps, Some(b), true) as f32,
            dps: self.weapon_getter(self.mainhand, b.str_, A::MainhandDps),
            dps_inc: self.derived_f64(b, K::MeleeDpsInc, A::MeleeDpsInc),
            offhand_dps: self.weapon_getter(self.offhand, b.str_, A::OffhandDps),
            ranged_dps: self.weapon_getter(self.ranged, b.dex, A::RangedDps),
            ranged_dps_inc: self.derived_f64(b, K::RangedDpsInc, A::RangedDpsInc),
            mdps,
            mdps_inc: self.derived_f64(b, K::SpellDpsInc, A::SpellDpsInc),
            armor: self.derived_int(b, K::Armor, A::Armor, 0),
            magic_resistance: self.derived_int(b, K::MagicResist, A::MagicResist, 0),
            kill_exp,
        }
    }
}

/// The getters for `npc` (`None` when its template does not exist).
pub fn npc_stats(gd: &GameData, npc: &Npc) -> Option<NpcStats> {
    let data = &gd.systems.npcs.npc;
    let template = data.template(npc.unit.template_id)?;
    let weapon = |slot: i32| {
        npc.equipment_item(slot as usize)
            .and_then(|i| weapon_dps(data, i))
    };
    Some(
        NpcStatCalc {
            formulas: &gd.formulas,
            template,
            level: npc.unit.level,
            bonuses: &npc.unit.bonuses,
            mainhand: weapon(equip_slot::MAINHAND),
            offhand: weapon(equip_slot::OFFHAND),
            ranged: weapon(equip_slot::RANGED),
        }
        .compute(),
    )
}

/// Caches `MaxHp`, `MaxMp` and the regen getters in `npc.unit` (shared code reads those).
pub fn refresh_cached_stats(gd: &GameData, npc: &mut Npc) {
    let Some(s) = npc_stats(gd, npc) else {
        return;
    };
    let u = &mut npc.unit;
    u.max_hp = s.max_hp;
    u.max_mp = s.max_mp;
    u.hp_regen = s.hp_regen;
    u.mp_regen = s.mp_regen;
    u.persistent_hp_regen = s.persistent_hp_regen;
    u.persistent_mp_regen = s.persistent_mp_regen;
}

#[cfg(test)]
pub(crate) mod tests {
    use super::*;
    use crate::models::unit::{Bonus, BonusTemplate};
    use rusqlite::Connection;

    /// The 1.2 NPC unit formulas used by the tests (a subset of `unit_formulas`, owner 1) plus
    /// variables for npc_template 9, npc_kind 1, npc_grade 1.
    pub(crate) fn npc_formulas() -> FormulaManager {
        let c = Connection::open_in_memory().unwrap();
        c.execute_batch(
            "CREATE TABLE unit_formulas (id INTEGER, formula TEXT, kind_id INTEGER, owner_type_id INTEGER);
             CREATE TABLE unit_formula_variables (id INTEGER, unit_formula_id INTEGER, variable_kind_id INTEGER, key INTEGER, value REAL);
             CREATE TABLE wearable_formulas (id INTEGER, kind_id INTEGER, formula TEXT);
             CREATE TABLE formulas (id INTEGER, formula TEXT);
             INSERT INTO unit_formulas VALUES
              (128, '( level * 4 + 10 ) * npc_template', 22, 1),
              (134, '( level * 4 + 10 ) * npc_template', 23, 1),
              (140, '( level * 4 + 10 ) * npc_template', 24, 1),
              (146, '( level * 4 + 10 ) * npc_template', 25, 1),
              (152, '( level * 4 + 10 ) * npc_template', 26, 1),
              (158, '11', 27, 1),
              (31, '( 5 * level^2.06 + ( (sta^1.05) * 9.3 ) ) * ( npc_template * npc_kind * npc_grade ) + 102', 14, 1),
              (32, '(level * 20 + int * 10 + 130) * ( npc_kind * npc_grade )', 15, 1),
              (33, 'spi * 0.1 + 15', 16, 1),
              (34, 'spi * 0.07 + 15', 17, 1),
              (212, '0', 31, 1),
              (219, '0', 32, 1),
              (104, '( 935 / 50 * level ) * ( npc_template * npc_kind * npc_grade )', 18, 1),
              (110, '( 935 / 50 * level ) * ( npc_template * npc_kind * npc_grade )', 19, 1),
              (122, '( floor ( ( ( level + 2 ) / 3 ) * 17.5 + ( level ^ 2 / 15 ) ) + ( str / 3 ) ) * 1000 * ( npc_template * npc_kind * npc_grade )', 21, 1),
              (164, '( floor ( ( ( level + 2 ) / 3 ) * 17.5 + ( level ^ 2 / 15 ) ) + ( dex / 3 ) ) * 1000 * ( npc_template * npc_kind * npc_grade )', 28, 1),
              (170, '( floor ( ( ( level + 2 ) / 3 ) * 17.5 + ( level ^ 2 / 15 ) ) + ( int / 3 ) ) * 1000 * ( npc_template * npc_kind * npc_grade )', 29, 1),
              (29, '( floor( ( ab_level + 2 ) / 3 ) * 24 - 8 ) * ( npc_template * npc_kind * npc_grade )', 12, 1),
              (233, '( ( level * 5 + 90) + if_negative( level - 51, 0, ( level - 50 ) * 980 ) ) * npc_grade', 34, 1);",
        )
        .unwrap();
        let mut vars = String::from("INSERT INTO unit_formula_variables VALUES ");
        let ids = [
            128, 134, 140, 146, 152, 158, 31, 32, 33, 34, 212, 219, 104, 110, 122, 164, 170, 29,
            233,
        ];
        let mut n = 0;
        for id in ids {
            for (kind, key, v) in [(2, 9, 1.0), (3, 1, 1.0), (4, 1, 1.0), (4, 2, 2.5)] {
                n += 1;
                vars.push_str(&format!("({n}, {id}, {kind}, {key}, {v}),"));
            }
        }
        vars.pop();
        c.execute_batch(&vars).unwrap();
        FormulaManager::load(&c).unwrap()
    }

    fn template(grade: u8) -> NpcTemplate {
        NpcTemplate {
            id: 1,
            npc_template_id: 9,
            npc_kind_id: 1,
            npc_grade_id: grade,
            exp_multiplier: 1.5,
            exp_adder: 7,
            ..NpcTemplate::default()
        }
    }

    fn calc<'a>(f: &'a FormulaManager, t: &'a NpcTemplate, b: &'a Bonuses, level: u8) -> NpcStats {
        NpcStatCalc {
            formulas: f,
            template: t,
            level,
            bonuses: b,
            mainhand: Some(WeaponDps {
                dps: 30.5,
                mdps: 12.25,
            }),
            offhand: None,
            ranged: None,
        }
        .compute()
    }

    #[test]
    fn level_50_normal() {
        let f = npc_formulas();
        let t = template(1);
        let b = Bonuses::default();
        let s = calc(&f, &t, &b, 50);
        // str..spi = (50*4 + 10) * 1 = 210, fai = 11
        assert_eq!(
            (s.str_, s.dex, s.sta, s.int, s.spi, s.fai),
            (210, 210, 210, 210, 210, 11)
        );
        // MaxHp = (5*50^2.06 + 210^1.05*9.3) * 1 + 102
        let hp = (5.0 * 50f64.powf(2.06) + 210f64.powf(1.05) * 9.3) + 102.0;
        assert_eq!(s.max_hp, hp as i32);
        // MaxMp = (50*20 + 210*10 + 130) * 1 = 3230
        assert_eq!(s.max_mp, 3230);
        // HpRegen = (int)(21 + 15) + 210/10 = 36 + 21
        assert_eq!(s.hp_regen, 57);
        // MpRegen = (int)(14.7 + 15) + 21 = 29 + 21
        assert_eq!(s.mp_regen, 50);
        assert_eq!((s.persistent_hp_regen, s.persistent_mp_regen), (0, 0));
        // Armor = 935 / 50 * 50 = 935
        assert_eq!((s.armor, s.magic_resistance), (935, 935));
        // DpsInc = (floor(52/3*17.5 + 2500/15) + 70) * 1000 = (floor(303.33 + 166.67) + 70) * 1000
        let base = ((52.0f64 / 3.0) * 17.5 + 2500.0 / 15.0).floor();
        assert_eq!(s.dps_inc, ((base + 70.0) * 1000.0) as i32);
        // LevelDps with ab_level = 0: (floor(2/3)*24 - 8) = -8
        assert_eq!(s.level_dps, -8.0);
        // Dps = (30.5 + 21) * 1000, MDps = (12.25 + 21) * 1000
        assert_eq!(s.dps, 51500);
        assert_eq!(s.mdps, 33250);
        assert_eq!(s.offhand_dps, 21000);
        assert_eq!(s.ranged_dps, 21000);
        // KillExp = (50*5 + 90 + 0) * 1 * 1.5 + 7 = 517
        assert_eq!(s.kill_exp, 517);
    }

    #[test]
    fn grade_variable_and_bonuses() {
        let f = npc_formulas();
        let t = template(2);
        let mut b = Bonuses::default();
        let bonus = |attr, kind, v| Bonus {
            template: BonusTemplate {
                attribute: attr,
                modifier_type: kind,
                value: v,
                linear_level_bonus: 0,
            },
            value: v,
        };
        b.add_bonus(0, bonus(UnitAttribute::Str, UnitModifierType::Value, 5));
        b.add_bonus(
            0,
            bonus(UnitAttribute::MaxMana, UnitModifierType::Percent, 15),
        );
        b.add_bonus(0, bonus(UnitAttribute::MaxMana, UnitModifierType::Value, 3));
        b.add_bonus(
            0,
            bonus(UnitAttribute::MainhandDps, UnitModifierType::Percent, 10),
        );
        let s = calc(&f, &t, &b, 10);
        assert_eq!(s.str_, 55); // 50 + 5
                                // MaxMp = (200 + 500 + 130) * 2.5 = 2075; +15% -> (int)(311.25) = 311 -> 2386; +3
        assert_eq!(s.max_mp, 2389);
        // Dps: 30.5 + 5.5 = 36; +(int)3.6 = 3 -> 39 -> 39000
        assert_eq!(s.dps, 39000);
        // KillExp = (10*5 + 90) * 2.5 * 1.5 + 7 = 532
        assert_eq!(s.kill_exp, 532);
        let mut t = t;
        t.no_exp = true;
        assert_eq!(calc(&f, &t, &b, 10).kill_exp, 0);
    }
}
