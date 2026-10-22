USE aaemu_game;

-- Permanent bonus skill points from SpecialType.AdditionalSkillPoint (achievement stone / test item).
-- Added to levels.skill_points for the learn budget and mirrored on SCUpdateAdditionalSkillPoint / CharacterState.
ALTER TABLE `characters`
  ADD COLUMN `additional_skill_point` INT UNSIGNED NOT NULL DEFAULT 0 AFTER `jury_point`;
