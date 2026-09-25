USE aaemu_game;

-- How a zone group's siege ended, once per siege cycle.
--
-- siege_scores is the running counter during a siege; this is the outcome it produced, kept so the settlement is
-- a fact that can be read back instead of a re-derivation, and so a tick that runs twice (or a World restart
-- between the phase change and the write) cannot settle the same cycle twice - the unique key on
-- (zone_id, cycle_week_start) is what makes the write idempotent.
--
-- The scores are stored as they stood at the end; the winner is an alliance id from siege_factions, or 0 when
-- the defender held the dominion or two sides reached their win points in the same siege.
CREATE TABLE IF NOT EXISTS `siege_settlements` (
  `id` int unsigned NOT NULL AUTO_INCREMENT,
  `zone_id` smallint unsigned NOT NULL COMMENT 'zone_group_id, matches dominions/siege_zones',
  `cycle_week_start` datetime NOT NULL COMMENT 'siege_plans.week_start of the cycle that was fought',
  `settled_at` datetime NOT NULL,
  `outlaw_point` int unsigned NOT NULL DEFAULT '0',
  `defense_point` int unsigned NOT NULL DEFAULT '0',
  `offense_point` int unsigned NOT NULL DEFAULT '0',
  `outcome` tinyint unsigned NOT NULL COMMENT '1 defense held, 2 offense broke through, 3 outlaw broke through, 4 contested',
  `defender_faction_id` int unsigned NOT NULL COMMENT 'siege_factions.faction_id that held the ground during the siege',
  `winner_faction_id` int unsigned NOT NULL DEFAULT '0' COMMENT 'alliance that took the dominion; 0 when the defender held it or the siege was contested',
  `reason` varchar(255) NOT NULL DEFAULT '',
  PRIMARY KEY (`id`) USING BTREE,
  UNIQUE KEY `uq_siege_settlements_zone_cycle` (`zone_id`, `cycle_week_start`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='How each zone group''s siege ended, once per siege cycle';
