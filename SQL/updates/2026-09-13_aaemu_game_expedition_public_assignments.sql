ALTER TABLE `expeditions`
    ADD COLUMN `last_assignment_update_time` DATETIME NOT NULL DEFAULT '1970-01-01 00:00:00' AFTER `last_contribution_point_added`;

CREATE TABLE `character_today_board_reset_counts` (
    `owner` INT UNSIGNED NOT NULL,
    `sort_id` INT NOT NULL,
    `day_key` DATE NOT NULL,
    `resets_used` INT UNSIGNED NOT NULL DEFAULT 0,
    PRIMARY KEY (`owner`,`sort_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `expedition_public_assignments` (
    `expedition_id` INT NOT NULL,
    `period_start` DATETIME NOT NULL,
    `real_step` INT UNSIGNED NOT NULL,
    `group_id` INT UNSIGNED NOT NULL,
    `quest_context_id` INT UNSIGNED NOT NULL,
    `status` TINYINT NOT NULL,
    `objectives` JSON NOT NULL,
    `version` INT UNSIGNED NOT NULL DEFAULT 0,
    `selection_generation` INT UNSIGNED NOT NULL DEFAULT 1,
    `completed_at` DATETIME NULL,
    `guild_rewarded` BOOLEAN NOT NULL DEFAULT FALSE,
    PRIMARY KEY (`expedition_id`, `period_start`, `real_step`),
    CONSTRAINT `fk_public_assignment_expedition` FOREIGN KEY (`expedition_id`) REFERENCES `expeditions` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `expedition_public_assignment_contributors` (
    `expedition_id` INT NOT NULL,
    `period_start` DATETIME NOT NULL,
    `real_step` INT UNSIGNED NOT NULL,
    `character_id` INT UNSIGNED NOT NULL,
    `character_name` VARCHAR(128) NOT NULL,
    `contribution` BIGINT UNSIGNED NOT NULL DEFAULT 0,
    PRIMARY KEY (`expedition_id`, `period_start`, `real_step`, `character_id`),
    CONSTRAINT `fk_public_contributor_assignment` FOREIGN KEY (`expedition_id`,`period_start`,`real_step`) REFERENCES `expedition_public_assignments` (`expedition_id`,`period_start`,`real_step`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `expedition_public_assignment_claims` (
    `expedition_id` INT NOT NULL,
    `period_start` DATETIME NOT NULL,
    `real_step` INT UNSIGNED NOT NULL,
    `character_id` INT UNSIGNED NOT NULL,
    `character_name` VARCHAR(128) NOT NULL,
    `delivered_at` DATETIME NULL,
    PRIMARY KEY (`expedition_id`, `period_start`, `real_step`, `character_id`),
    CONSTRAINT `fk_public_claim_assignment` FOREIGN KEY (`expedition_id`,`period_start`,`real_step`) REFERENCES `expedition_public_assignments` (`expedition_id`,`period_start`,`real_step`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
