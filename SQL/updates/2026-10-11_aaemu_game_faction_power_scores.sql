USE aaemu_game;

-- Weekly faction power points; last week's totals decide the superior and inferior factions.

CREATE TABLE IF NOT EXISTS `faction_power_scores` (
  `week_num` int unsigned NOT NULL COMMENT 'weeks since Monday 1970-01-05 UTC',
  `faction_id` int unsigned NOT NULL,
  `siege_point` int unsigned NOT NULL DEFAULT '0',
  `siege_maintain_point` int unsigned NOT NULL DEFAULT '0',
  `competition_point` int unsigned NOT NULL DEFAULT '0',
  `quest_point` float NOT NULL DEFAULT '0',
  `raid_point` int unsigned NOT NULL DEFAULT '0',
  `raid_maintain_point` int unsigned NOT NULL DEFAULT '0',
  `raid_joint_point` int unsigned NOT NULL DEFAULT '0',
  `immigrate_point` int unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`week_num`,`faction_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Weekly faction power points';

-- One row per settled siege cycle that scored, written with the points so a settlement scores exactly once.

CREATE TABLE IF NOT EXISTS `faction_power_siege_awards` (
  `zone_group_id` smallint unsigned NOT NULL,
  `cycle_week_start` datetime NOT NULL,
  `faction_id` int unsigned NOT NULL,
  `week_num` int unsigned NOT NULL,
  `points` int unsigned NOT NULL,
  PRIMARY KEY (`zone_group_id`,`cycle_week_start`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Siege wins already counted for faction power';
