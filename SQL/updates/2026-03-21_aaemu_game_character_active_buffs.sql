-- ============================================================================
-- Buff Persistence System — MySQL Migration
-- ============================================================================
-- Run this on your `aaemu_game` database before starting the server.
-- This table stores active buffs when a player logs out so they can be
-- restored on the next login.
-- ============================================================================

CREATE TABLE IF NOT EXISTS `character_active_buffs` (
  `character_id`  INT UNSIGNED NOT NULL COMMENT 'Character who owns this buff',
  `buff_id`       INT UNSIGNED NOT NULL COMMENT 'BuffTemplate.Id from game data',
  `caster_id`     INT UNSIGNED NOT NULL COMMENT 'Character who originally cast this buff',
  `skill_id`      INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Source skill template ID (0 if none)',
  `ab_level`      INT UNSIGNED NOT NULL DEFAULT 1 COMMENT 'Ability level for bonus scaling',
  `duration`      INT NOT NULL COMMENT 'Total duration in milliseconds',
  `time_left`     INT NOT NULL COMMENT 'Remaining time in milliseconds at save',
  `charge`        INT NOT NULL DEFAULT 0 COMMENT 'Current charge count',
  `stack_count`   INT NOT NULL DEFAULT 1 COMMENT 'Number of stacks',
  `real_time`     TINYINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '1=timer ticks offline, 0=timer paused offline',
  `saved_at`      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'UTC timestamp when buff was saved',
  PRIMARY KEY (`character_id`, `buff_id`),
  INDEX `idx_character_id` (`character_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Stores active buffs across player sessions';
