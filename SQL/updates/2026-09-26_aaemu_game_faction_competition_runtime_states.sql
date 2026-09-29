USE aaemu_game;

-- Live faction-competition scores, one row per competition per faction.
--
-- A faction that has never scored has no row, so the table holds only points that were actually
-- awarded from faction_competitions.point_npc_kill_value / point_quest_complete_value via the
-- faction_competition_npc_infos and faction_competition_quest_infos eligibility catalogs. There is
-- no shipped column saying which competition scores survive a restart, so every awarded score is
-- written and the competition's own point_reset_id reset is the only thing that clears a row.
--
-- score is BIGINT because a competition accumulates across every eligible event; the catalog point
-- columns are ints and only bound one award.
CREATE TABLE IF NOT EXISTS `faction_competition_runtime_states` (
  `faction_competition_id` INT UNSIGNED NOT NULL COMMENT 'faction_competitions.id',
  `faction_id` INT UNSIGNED NOT NULL COMMENT 'the faction that earned the score',
  `score` BIGINT NOT NULL DEFAULT '0',
  PRIMARY KEY (`faction_competition_id`, `faction_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Durable faction-competition scores, reset by the competition point_reset_id policy';
