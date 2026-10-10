//! The attribute getters of `Mate`, `Slave`, `Transfer` and `Shipyard` (C# virtual properties
//! evaluated through `FormulaManager` + the unit's static bonuses). `skills::stats::stats`
//! routes these kinds here; attributes without an override fall back to the base `Unit`
//! value.
//!
//! A missing unit formula is a `NullReferenceException` in C#; here it evaluates to 0.

use std::collections::HashMap;

use crate::data::formulas::{FormulaOwnerType, UnitFormulaKind, UnitFormulaVariableType};
use crate::models::unit::{UnitAttribute, UnitBase, UnitModifierType};
use crate::server::GameServer;
use crate::systems::skills::stats::{base_unit_stat, StatKind};
use crate::world::WorldObject;

/// Which getter set a unit kind uses.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum VehicleKind {
    /// `Mate` (`mate_kind` = `Template.MateKindId`).
    Mate {
        mate_kind_id: i32,
    },
    Slave,
    Transfer,
    Shipyard,
}

impl VehicleKind {
    fn owner(self) -> FormulaOwnerType {
        match self {
            Self::Mate { .. } => FormulaOwnerType::Mate,
            Self::Slave => FormulaOwnerType::Slave,
            Self::Transfer => FormulaOwnerType::Transfer,
            Self::Shipyard => FormulaOwnerType::Shipyard,
        }
    }
}

/// `foreach (var bonus in GetBonuses(attr))` on an `int` result.
fn int_bonuses(u: &UnitBase, mut res: i32, attr: UnitAttribute) -> i32 {
    for b in u.bonuses.bonuses(attr) {
        if b.template.modifier_type == UnitModifierType::Percent {
            res += (res as f32 * b.value as f32 / 100.0) as i32;
        } else {
            res += b.value;
        }
    }
    res
}

/// `foreach (var bonus in GetBonuses(attr))` on a `double` result.
fn float_bonuses(u: &UnitBase, mut res: f64, attr: UnitAttribute) -> f64 {
    for b in u.bonuses.bonuses(attr) {
        if b.template.modifier_type == UnitModifierType::Percent {
            res += res * f64::from(b.value as f32 / 100.0);
        } else {
            res += f64::from(b.value);
        }
    }
    res
}

struct Eval<'a> {
    server: &'a GameServer,
    kind: VehicleKind,
    u: &'a UnitBase,
    now: i64,
}

impl Eval<'_> {
    fn formula(&self, kind: UnitFormulaKind, params: &HashMap<&str, f64>) -> f64 {
        match self
            .server
            .game_data
            .formulas
            .get_unit_formula(self.kind.owner(), kind)
        {
            Some(f) => f.evaluate(params),
            None => 0.0,
        }
    }

    fn level(&self) -> f64 {
        f64::from(self.u.level)
    }

    fn base_attr(&self, kind: UnitFormulaKind, attr: UnitAttribute) -> i32 {
        let p = HashMap::from([("level", self.level())]);
        int_bonuses(self.u, self.formula(kind, &p) as i32, attr)
    }

    fn str_(&self) -> i32 {
        self.base_attr(UnitFormulaKind::Str, UnitAttribute::Str)
    }
    fn dex(&self) -> i32 {
        self.base_attr(UnitFormulaKind::Dex, UnitAttribute::Dex)
    }
    fn sta(&self) -> i32 {
        self.base_attr(UnitFormulaKind::Sta, UnitAttribute::Sta)
    }
    fn int(&self) -> i32 {
        self.base_attr(UnitFormulaKind::Int, UnitAttribute::Int)
    }
    fn spi(&self) -> i32 {
        self.base_attr(UnitFormulaKind::Spi, UnitAttribute::Spi)
    }
    fn fai(&self) -> i32 {
        self.base_attr(UnitFormulaKind::Fai, UnitAttribute::Fai)
    }

    /// `level` + `str`..`fai` (transfers, shipyards, the mate combat getters).
    fn full_params(&self) -> HashMap<&'static str, f64> {
        HashMap::from([
            ("level", self.level()),
            ("str", f64::from(self.str_())),
            ("dex", f64::from(self.dex())),
            ("sta", f64::from(self.sta())),
            ("int", f64::from(self.int())),
            ("spi", f64::from(self.spi())),
            ("fai", f64::from(self.fai())),
        ])
    }

    /// The parameters of the HP / MP getters.
    fn pool_params(&self, variable: bool, kind: UnitFormulaKind) -> HashMap<&'static str, f64> {
        match self.kind {
            VehicleKind::Mate { mate_kind_id } => {
                let mate_kind = if variable {
                    let formulas = &self.server.game_data.formulas;
                    let id = formulas
                        .get_unit_formula(FormulaOwnerType::Mate, kind)
                        .map_or(0, |f| f.formula.id);
                    f64::from(formulas.get_unit_variable(
                        id,
                        UnitFormulaVariableType::MateKind,
                        mate_kind_id as u32,
                    ))
                } else {
                    f64::from(mate_kind_id)
                };
                HashMap::from([("level", self.level()), ("mate_kind", mate_kind)])
            }
            // `Slave` getters pass `level` + `str`..`fai` like transfers and shipyards.
            VehicleKind::Slave | VehicleKind::Transfer | VehicleKind::Shipyard => {
                self.full_params()
            }
        }
    }

    fn stat(&self, kind: StatKind) -> f64 {
        use StatKind as S;
        use UnitAttribute as A;
        use UnitFormulaKind as K;
        let mate = matches!(self.kind, VehicleKind::Mate { .. });
        let slave = self.kind == VehicleKind::Slave;
        let combat = mate || slave;
        let persistent_div = !slave;
        match kind {
            S::Str => f64::from(self.str_()),
            S::Dex => f64::from(self.dex()),
            S::Sta => f64::from(self.sta()),
            S::Int => f64::from(self.int()),
            S::Spi => f64::from(self.spi()),
            S::Fai => f64::from(self.fai()),
            S::MaxHp => {
                let res = self.formula(K::MaxHealth, &self.pool_params(true, K::MaxHealth)) as i32;
                if mate {
                    // `CalculateWithBonuses(res, MaxHealth)`
                    f64::from(
                        self.u
                            .calculate_with_bonuses(f64::from(res), A::MaxHealth, self.now)
                            as i32,
                    )
                } else {
                    f64::from(int_bonuses(self.u, res, A::MaxHealth))
                }
            }
            S::MaxMp => {
                let res = self.formula(K::MaxMana, &self.pool_params(true, K::MaxMana)) as i32;
                f64::from(int_bonuses(self.u, res, A::MaxMana))
            }
            S::HpRegen => {
                let mut res =
                    self.formula(K::HealthRegen, &self.pool_params(false, K::HealthRegen)) as i32;
                res += self.spi() / 10;
                f64::from(int_bonuses(self.u, res, A::HealthRegen))
            }
            S::MpRegen => {
                let mut res =
                    self.formula(K::ManaRegen, &self.pool_params(false, K::ManaRegen)) as i32;
                res += self.spi() / 10;
                f64::from(int_bonuses(self.u, res, A::ManaRegen))
            }
            S::PersistentHpRegen => {
                let mut res = self.formula(
                    K::PersistentHealthRegen,
                    &self.pool_params(false, K::PersistentHealthRegen),
                ) as i32;
                if persistent_div {
                    res /= 5;
                }
                f64::from(int_bonuses(self.u, res, A::PersistentHealthRegen))
            }
            S::PersistentMpRegen => {
                let mut res = self.formula(
                    K::PersistentManaRegen,
                    &self.pool_params(false, K::PersistentManaRegen),
                ) as i32;
                if persistent_div {
                    res /= 5;
                }
                f64::from(int_bonuses(self.u, res, A::PersistentManaRegen))
            }
            S::LevelDps if combat => {
                let ab = if mate { self.level() } else { 0.0 };
                let p = HashMap::from([("level", self.level()), ("ab_level", ab)]);
                f64::from(self.formula(K::LevelDps, &p) as f32)
            }
            S::DpsInc if combat => self.inc(K::MeleeDpsInc, A::MeleeDpsInc),
            S::MDpsInc if combat => self.inc(K::SpellDpsInc, A::SpellDpsInc),
            S::RangedDpsInc if slave => self.inc(K::RangedDpsInc, A::RangedDpsInc),
            S::Armor if combat => {
                let p = if mate {
                    self.full_params()
                } else {
                    HashMap::from([("level", self.level())])
                };
                f64::from(int_bonuses(
                    self.u,
                    self.formula(K::Armor, &p) as i32,
                    A::Armor,
                ))
            }
            S::MagicResistance if combat => {
                let p = if mate {
                    self.full_params()
                } else {
                    HashMap::from([("level", self.level())])
                };
                f64::from(int_bonuses(
                    self.u,
                    self.formula(K::MagicResist, &p) as i32,
                    A::MagicResist,
                ))
            }
            // Slave weapon dps: `(Weapon)Equipment.GetItemBySlot(..)?.Dps` (weapon stats are the
            // economy subsystem's: 0 here) + the attribute part.
            S::Dps if slave => self.weapon_dps(f64::from(self.str_()), A::MainhandDps),
            S::OffhandDps if slave => self.weapon_dps(f64::from(self.str_()), A::OffhandDps),
            S::RangedDps if slave => self.weapon_dps(f64::from(self.dex()), A::RangedDps),
            S::MDps if slave => self.weapon_dps(f64::from(self.int()), A::SpellDps),
            other => base_unit_stat(self.u, other, self.now),
        }
    }

    fn inc(&self, kind: UnitFormulaKind, attr: UnitAttribute) -> f64 {
        let p = if matches!(self.kind, VehicleKind::Mate { .. }) {
            self.full_params()
        } else {
            HashMap::from([("level", self.level())])
        };
        f64::from(float_bonuses(self.u, self.formula(kind, &p), attr) as i32)
    }

    fn weapon_dps(&self, attr_value: f64, attr: UnitAttribute) -> f64 {
        let mut res = 0f32 + (attr_value as f32) / 10.0;
        for b in self.u.bonuses.bonuses(attr) {
            if b.template.modifier_type == UnitModifierType::Percent {
                res += (res * b.value as f32 / 100.0) as i32 as f32;
            } else {
                res += b.value as f32;
            }
        }
        f64::from((res * 1000.0) as i32)
    }
}

/// The getter values of `kinds` for one vehicle unit (`None` when `obj` is not one).
pub fn stats(server: &GameServer, obj: &WorldObject, kinds: &[StatKind]) -> Option<Vec<f64>> {
    let now = crate::world::now_ms();
    let eval = |kind: VehicleKind, u: &UnitBase| -> Vec<f64> {
        let e = Eval {
            server,
            kind,
            u,
            now,
        };
        kinds.iter().map(|k| e.stat(*k)).collect()
    };
    Some(match obj {
        WorldObject::Mate(m) => {
            let m = m.data();
            eval(
                VehicleKind::Mate {
                    mate_kind_id: m.mate_kind_id,
                },
                &m.unit,
            )
        }
        WorldObject::Slave(s) => eval(VehicleKind::Slave, &s.data().unit),
        WorldObject::Transfer(t) => eval(VehicleKind::Transfer, &t.data().unit),
        WorldObject::Shipyard(s) => eval(VehicleKind::Shipyard, &s.data().unit),
        _ => return None,
    })
}

/// Evaluates the getters on a detached unit (before it is a world object).
pub fn stats_of(
    server: &GameServer,
    kind: VehicleKind,
    u: &UnitBase,
    kinds: &[StatKind],
) -> Vec<f64> {
    let e = Eval {
        server,
        kind,
        u,
        now: crate::world::now_ms(),
    };
    kinds.iter().map(|k| e.stat(*k)).collect()
}

/// Recomputes the cached pool values (`max_hp`, `max_mp`, regen) of a detached unit.
pub fn refresh_pools(server: &GameServer, kind: VehicleKind, u: &mut UnitBase) {
    let v = stats_of(
        server,
        kind,
        u,
        &[
            StatKind::MaxHp,
            StatKind::MaxMp,
            StatKind::HpRegen,
            StatKind::MpRegen,
            StatKind::PersistentHpRegen,
            StatKind::PersistentMpRegen,
        ],
    );
    u.max_hp = v[0] as i32;
    u.max_mp = v[1] as i32;
    u.hp_regen = v[2] as i32;
    u.mp_regen = v[3] as i32;
    u.persistent_hp_regen = v[4] as i32;
    u.persistent_mp_regen = v[5] as i32;
}

/// [`refresh_pools`] for a world object of a vehicle kind.
pub fn refresh_object_pools(server: &GameServer, obj: &WorldObject) {
    let kind = match obj {
        WorldObject::Mate(m) => VehicleKind::Mate {
            mate_kind_id: m.data().mate_kind_id,
        },
        WorldObject::Slave(_) => VehicleKind::Slave,
        WorldObject::Transfer(_) => VehicleKind::Transfer,
        WorldObject::Shipyard(_) => VehicleKind::Shipyard,
        _ => return,
    };
    obj.with_unit(|u| refresh_pools(server, kind, u.unit_mut()));
}

#[cfg(test)]
pub(crate) mod tests {
    use super::*;
    use crate::data::formulas::FormulaManager;
    use crate::models::unit::{Bonus, BonusTemplate};
    use rusqlite::Connection;

    /// A formula manager from `(id, formula, kind_id, owner_type_id)` rows.
    pub(crate) fn formulas(
        rows: &[(u32, &str, u8, u8)],
        vars: &[(u32, u8, u32, f32)],
    ) -> FormulaManager {
        let c = Connection::open_in_memory().unwrap();
        c.execute_batch(
            "CREATE TABLE unit_formulas (id INTEGER, formula TEXT, kind_id INTEGER, owner_type_id INTEGER);
             CREATE TABLE unit_formula_variables (id INTEGER, unit_formula_id INTEGER, variable_kind_id INTEGER, key INTEGER, value REAL);
             CREATE TABLE wearable_formulas (id INTEGER, kind_id INTEGER, formula TEXT);
             CREATE TABLE formulas (id INTEGER, formula TEXT);",
        )
        .unwrap();
        for (id, f, k, o) in rows {
            c.execute(
                "INSERT INTO unit_formulas VALUES (?1, ?2, ?3, ?4)",
                rusqlite::params![id, f, k, o],
            )
            .unwrap();
        }
        for (i, (fid, kind, key, v)) in vars.iter().enumerate() {
            c.execute(
                "INSERT INTO unit_formula_variables VALUES (?1, ?2, ?3, ?4, ?5)",
                rusqlite::params![i as u32 + 1, fid, kind, key, v],
            )
            .unwrap();
        }
        FormulaManager::load(&c).unwrap()
    }

    #[tokio::test]
    async fn slave_transfer_and_mate_pools_follow_the_csharp_getters() {
        let mut data = crate::data::GameData::empty();
        data.formulas = formulas(
            &[
                (1, "level * 100", 14, 2),
                (2, "level * 2", 26, 2),
                (3, "10", 16, 2),
                (4, "50", 31, 2),
                (5, "level * 10 + spi", 14, 4),
                (6, "level * 3", 26, 4),
                (7, "50", 31, 4),
                (8, "level * mate_kind", 14, 5),
                (9, "mate_kind", 16, 5),
                (10, "50", 31, 5),
            ],
            &[(8, 5, 3, 7.0)],
        );
        let server = crate::test_support::test_server_with(data);
        let mut u = UnitBase {
            level: 10,
            ..UnitBase::default()
        };
        u.bonuses.add_bonus(
            0,
            Bonus {
                template: BonusTemplate {
                    attribute: UnitAttribute::MaxHealth,
                    modifier_type: UnitModifierType::Percent,
                    value: 10,
                    linear_level_bonus: 0,
                },
                value: 10,
            },
        );
        let v = stats_of(
            &server,
            VehicleKind::Slave,
            &u,
            &[
                StatKind::MaxHp,
                StatKind::HpRegen,
                StatKind::PersistentHpRegen,
            ],
        );
        // 1000 + 10%; regen 10 + spi(20)/10; persistent: no /5 for slaves.
        assert_eq!(v, vec![1100.0, 12.0, 50.0]);
        let v = stats_of(
            &server,
            VehicleKind::Transfer,
            &u,
            &[StatKind::MaxHp, StatKind::PersistentHpRegen],
        );
        // (100 + 30) + 10%; persistent / 5.
        assert_eq!(v, vec![143.0, 10.0]);
        let v = stats_of(
            &server,
            VehicleKind::Mate { mate_kind_id: 3 },
            &u,
            &[
                StatKind::MaxHp,
                StatKind::HpRegen,
                StatKind::PersistentHpRegen,
            ],
        );
        // MaxHp uses the mate_kind variable (7) then CalculateWithBonuses (+10%); the regen
        // getters pass the raw MateKindId (3).
        assert_eq!(v, vec![77.0, 3.0, 10.0]);
    }
}
