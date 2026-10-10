//! The `Character` attribute getters needed for the HP/MP clamps and the state packets
//! (`Str`, `Dex`, `Sta`, `Int`, `Spi`, `Fai`, `MaxHp`, `MaxMp`), evaluated through the
//! `unit_formulas` of `FormulaManager`.
//!
//! Approximations (later phase):
//! * `CalculateWithBonuses` is the identity: buff / gear / passive bonuses are not applied
//!   (the bonus system is part of the skill & buff effects port).
//! * equipment attribute contributions (`EquipItem.Str` etc., which depend on the item stat
//!   formulas and attribute modifiers) are 0.
//! * a missing formula evaluates to 0 (the C# would throw a `NullReferenceException`).

use std::collections::HashMap;

use crate::data::formulas::{FormulaManager, FormulaOwnerType, UnitFormulaKind};

use super::character::Character;

/// `Character` base attributes for a given level.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct CharacterStats {
    pub str_: i32,
    pub dex: i32,
    pub sta: i32,
    pub int: i32,
    pub spi: i32,
    pub fai: i32,
    pub max_hp: i32,
    pub max_mp: i32,
}

fn eval(f: &FormulaManager, kind: UnitFormulaKind, params: &HashMap<&str, f64>) -> f64 {
    match f.get_unit_formula(FormulaOwnerType::Character, kind) {
        Some(u) => u.formula.evaluate(params),
        None => {
            tracing::debug!("No character formula for {kind:?}");
            0.0
        }
    }
}

impl CharacterStats {
    /// Evaluates the attribute getters of `Character` for `level`.
    pub fn compute(f: &FormulaManager, level: u8) -> Self {
        let lv = HashMap::from([("level", f64::from(level))]);
        let base = |k| eval(f, k, &lv) as i32;
        let (str_, dex, sta, int, spi, fai) = (
            base(UnitFormulaKind::Str),
            base(UnitFormulaKind::Dex),
            base(UnitFormulaKind::Sta),
            base(UnitFormulaKind::Int),
            base(UnitFormulaKind::Spi),
            base(UnitFormulaKind::Fai),
        );
        let all = HashMap::from([
            ("level", f64::from(level)),
            ("str", f64::from(str_)),
            ("dex", f64::from(dex)),
            ("sta", f64::from(sta)),
            ("int", f64::from(int)),
            ("spi", f64::from(spi)),
            ("fai", f64::from(fai)),
        ]);
        Self {
            str_,
            dex,
            sta,
            int,
            spi,
            fai,
            max_hp: eval(f, UnitFormulaKind::MaxHealth, &all) as i32,
            max_mp: eval(f, UnitFormulaKind::MaxMana, &all) as i32,
        }
    }
}

impl Character {
    /// `MaxHp` / `MaxMp` and the base attributes.
    pub fn stats(&self, f: &FormulaManager) -> CharacterStats {
        CharacterStats::compute(f, self.unit.level)
    }

    /// `RestoreSavedHpMp`: `Hp = Min(_savedHp, MaxHp)`, `Mp = Min(_savedMp, MaxMp)`.
    pub fn restore_saved_hp_mp(&mut self) {
        // `MaxHp` / `MaxMp` are the full getters (gear bonuses, buffs): use the cached values
        // the skills subsystem keeps up to date, not the bare formulas.
        self.unit.hp = self.saved_hp.min(self.unit.max_hp);
        self.unit.mp = self.saved_mp.min(self.unit.max_mp);
    }

    /// The `Character.Load` clamp: `if (Hp > MaxHp) Hp = MaxHp` (same for Mp).
    pub fn clamp_hp_mp(&mut self, f: &FormulaManager) {
        let s = self.stats(f);
        (self.unit.max_hp, self.unit.max_mp) = (s.max_hp, s.max_mp);
        if self.unit.hp > s.max_hp {
            self.unit.hp = s.max_hp;
        }
        if self.unit.mp > s.max_mp {
            self.unit.mp = s.max_mp;
        }
    }
}
