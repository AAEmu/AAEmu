//! Port of `ExperienceManager` (+ `SqliteExperienceLevelTemplateLoader` validation) and
//! `Character.ValidateAndFixExpAndLevel`, which `Character.Load` runs before the character list
//! is written (it can change the level that is sent).

/// `ExperienceManager.PlayerLevelCap`.
pub const PLAYER_LEVEL_CAP: u8 = 55;
/// `ExperienceManager.MateLevelCap`.
pub const MATE_LEVEL_CAP: u8 = 50;

/// One `levels` row (`ExperienceLevelTemplate`).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct LevelTemplate {
    pub level: u8,
    pub total_exp: i32,
    pub total_mate_exp: i32,
    pub skill_points: i32,
}

/// The exceptions thrown by the C# loader / lookups.
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
pub enum ExperienceError {
    #[error("Experience data is missing level {0}")]
    MissingLevel(u32),
    #[error("Experience data is not sorted by total_exp")]
    NotSortedByExp,
    #[error("Experience data is not sorted by total_mate_exp")]
    NotSortedByMateExp,
    /// `ArgumentOutOfRangeException.ThrowIfZero(currentLevel)`.
    #[error("currentLevel must not be zero")]
    ZeroLevel,
    /// `ArgumentOutOfRangeException.ThrowIfNegative(exp)`.
    #[error("exp must not be negative ({0})")]
    NegativeExp(i32),
    /// `ArgumentOutOfRangeException.ThrowIfZero(playerLevelCap / mateLevelCap)` in `Load`.
    #[error("level cap must not be zero")]
    ZeroLevelCap,
}

#[derive(Debug, Clone, Default)]
pub struct ExperienceTable {
    /// `_levelTemplatesByLevel` (index = level - 1).
    templates: Vec<LevelTemplate>,
    max_player_level: u8,
    max_mate_level: u8,
}

impl ExperienceTable {
    /// `ExperienceManager.Load(loader, PlayerLevelCap, MateLevelCap)` with the loader's checks
    /// (levels must start at 1 without gaps, experience strictly increasing).
    pub fn from_templates(templates: Vec<LevelTemplate>) -> Result<Self, ExperienceError> {
        let (mut last_exp, mut last_mate_exp) = (-1i32, -1i32);
        for (expected, t) in (1u32..).zip(&templates) {
            if u32::from(t.level) != expected {
                tracing::error!("Experience data is missing level {expected}");
                return Err(ExperienceError::MissingLevel(expected));
            }
            if t.total_exp <= last_exp {
                return Err(ExperienceError::NotSortedByExp);
            }
            if t.total_mate_exp <= last_mate_exp {
                return Err(ExperienceError::NotSortedByMateExp);
            }
            last_exp = t.total_exp;
            last_mate_exp = t.total_mate_exp;
        }
        let mut table = Self::default();
        table.load(templates, PLAYER_LEVEL_CAP, MATE_LEVEL_CAP)?;
        Ok(table)
    }

    /// `ExperienceManager.Load(loader, playerLevelCap, mateLevelCap)` with the loader's rows
    /// (no validation: that is the SQLite loader's job, see [`Self::from_templates`]). Replaces
    /// the previous state; the max levels are the lower of the row count and the caps.
    pub fn load(
        &mut self,
        templates: impl IntoIterator<Item = LevelTemplate>,
        player_level_cap: u8,
        mate_level_cap: u8,
    ) -> Result<(), ExperienceError> {
        if player_level_cap == 0 || mate_level_cap == 0 {
            return Err(ExperienceError::ZeroLevelCap);
        }
        self.templates.clear();
        self.templates.extend(templates);
        let count = self.templates.len();
        self.max_player_level = count.min(usize::from(player_level_cap)) as u8;
        self.max_mate_level = count.min(usize::from(mate_level_cap)) as u8;
        Ok(())
    }

    pub fn max_player_level(&self) -> u8 {
        self.max_player_level
    }

    pub fn max_mate_level(&self) -> u8 {
        self.max_mate_level
    }

    fn template(&self, level: u8) -> Option<&LevelTemplate> {
        if level == 0 {
            return None;
        }
        self.templates.get(usize::from(level) - 1)
    }

    /// `GetExpForLevel(level, mate)`: 0 for an invalid level.
    pub fn exp_for_level(&self, level: u8, mate: bool) -> i32 {
        self.template(level)
            .map_or(0, |t| if mate { t.total_mate_exp } else { t.total_exp })
    }

    /// `GetLevelFromExp(exp, currentLevel, out overflow, mate)`. Returns `(level, overflow)`.
    pub fn level_from_exp(
        &self,
        exp: i32,
        current_level: u8,
        mate: bool,
    ) -> Result<(u8, i32), ExperienceError> {
        if current_level == 0 {
            return Err(ExperienceError::ZeroLevel);
        }
        self.level_from_exp_min(exp, mate, current_level)
    }

    /// `GetLevelFromExp(exp, out overflow, mate)` (no minimum level).
    pub fn level_from_exp_any(&self, exp: i32, mate: bool) -> Result<(u8, i32), ExperienceError> {
        self.level_from_exp_min(exp, mate, 0)
    }

    /// `GetExpNeededToGivenLevel(currentExp, targetLevel, mate)`: 0 when the target level is
    /// invalid or already reached.
    pub fn exp_needed_to_given_level(
        &self,
        current_exp: i32,
        target_level: u8,
        mate: bool,
    ) -> Result<i32, ExperienceError> {
        if current_exp < 0 {
            return Err(ExperienceError::NegativeExp(current_exp));
        }
        let target_exp = self.exp_for_level(target_level, mate);
        Ok(0.max(target_exp - current_exp))
    }

    /// `GetSkillPointsForLevel`.
    pub fn skill_points_for_level(&self, level: u8) -> i32 {
        self.template(level).map_or(0, |t| t.skill_points)
    }

    fn level_from_exp_min(
        &self,
        exp: i32,
        mate: bool,
        min_level: u8,
    ) -> Result<(u8, i32), ExperienceError> {
        if exp < 0 {
            return Err(ExperienceError::NegativeExp(exp));
        }
        let max_level = if mate {
            self.max_mate_level
        } else {
            self.max_player_level
        };
        if min_level >= max_level {
            let overflow = 0.max(exp - self.exp_for_level(max_level, mate));
            return Ok((max_level, overflow));
        }

        let exp_by_level: Vec<i32> = self
            .templates
            .iter()
            .map(|t| if mate { t.total_mate_exp } else { t.total_exp })
            .collect();
        // `List.BinarySearch(minLevel, count, exp)` with C#'s count. Note: the count is
        // `Min(Count - minLevel, maxLevel)`, so the searched range may extend past maxLevel when
        // the table has more rows than the cap (C# quirk, reproduced).
        let start = usize::from(min_level);
        let count = (exp_by_level.len() - start).min(usize::from(max_level));
        let index = match exp_by_level[start..start + count].binary_search(&exp) {
            Ok(i) => return Ok(((start + i + 1) as u8, 0)),
            Err(i) => start + i,
        };
        if index < exp_by_level.len() {
            let level = index as u8;
            return Ok((level, exp - self.exp_for_level(level, mate)));
        }
        let overflow = 0.max(exp - self.exp_for_level(max_level, mate));
        Ok((max_level, overflow))
    }

    /// `Character.ValidateAndFixExpAndLevel()` on `(level, experience)`; returns the fixed pair.
    /// The level-up side effect (`Expedition?.OnCharacterRefresh`) is a later phase.
    pub fn validate_and_fix(
        &self,
        level: u8,
        experience: i32,
    ) -> Result<(u8, i32), ExperienceError> {
        let need = self.exp_for_level(level, false);
        if experience < need {
            return Ok((level, need));
        }
        let (new_level, overflow) = self.level_from_exp(experience, level, false)?;
        let experience = if new_level >= self.max_player_level {
            experience - overflow
        } else {
            experience
        };
        Ok((new_level, experience))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn table(n: u8) -> ExperienceTable {
        let t = (1..=n)
            .map(|l| LevelTemplate {
                level: l,
                total_exp: (i32::from(l) - 1) * 100,
                total_mate_exp: (i32::from(l) - 1) * 10,
                skill_points: 0,
            })
            .collect();
        ExperienceTable::from_templates(t).unwrap()
    }

    #[test]
    fn loader_validation() {
        let bad = vec![LevelTemplate {
            level: 2,
            total_exp: 0,
            total_mate_exp: 0,
            skill_points: 0,
        }];
        assert_eq!(
            ExperienceTable::from_templates(bad).unwrap_err(),
            ExperienceError::MissingLevel(1)
        );
        let unsorted = vec![
            LevelTemplate {
                level: 1,
                total_exp: 5,
                total_mate_exp: 0,
                skill_points: 0,
            },
            LevelTemplate {
                level: 2,
                total_exp: 5,
                total_mate_exp: 1,
                skill_points: 0,
            },
        ];
        assert_eq!(
            ExperienceTable::from_templates(unsorted).unwrap_err(),
            ExperienceError::NotSortedByExp
        );
        let t = table(60);
        assert_eq!((t.max_player_level(), t.max_mate_level()), (55, 50));
    }

    #[test]
    fn level_lookup_matches_csharp() {
        let t = table(55);
        // exact hit on level 10's total exp
        assert_eq!(t.level_from_exp(900, 1, false).unwrap(), (10, 0));
        // between level 10 and 11
        assert_eq!(t.level_from_exp(950, 1, false).unwrap(), (10, 50));
        // beyond the table: max level with overflow
        assert_eq!(t.level_from_exp(9999, 1, false).unwrap(), (55, 9999 - 5400));
        // current level at/over the cap
        assert_eq!(t.level_from_exp(5500, 55, false).unwrap(), (55, 100));
        assert_eq!(
            t.level_from_exp(1, 0, false).unwrap_err(),
            ExperienceError::ZeroLevel
        );
        assert_eq!(
            t.level_from_exp(-1, 1, false).unwrap_err(),
            ExperienceError::NegativeExp(-1)
        );
    }

    #[test]
    fn validate_and_fix() {
        let t = table(55);
        // too little exp for the level: exp raised, level kept
        assert_eq!(t.validate_and_fix(10, 0).unwrap(), (10, 900));
        // enough exp for a higher level: level raised
        assert_eq!(t.validate_and_fix(10, 1234).unwrap(), (13, 1234));
        // capped: exp trimmed to the max level's total
        assert_eq!(t.validate_and_fix(50, 6000).unwrap(), (55, 5400));
        // no experience data (C# with an empty `levels` table): every level becomes 0
        assert_eq!(
            ExperienceTable::default().validate_and_fix(10, 5).unwrap(),
            (0, 0)
        );
        // level 0 throws in C#
        assert!(t.validate_and_fix(0, 5).is_err());
    }
}

#[cfg(test)]
#[path = "experience_tests.rs"]
mod experience_tests;
