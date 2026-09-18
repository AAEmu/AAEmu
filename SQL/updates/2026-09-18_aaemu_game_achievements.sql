USE aaemu_game;

-- Achievement progress. The list, its objectives and its rewards are content (achievements,
-- achievement_objectives, char_records and pre_completed_achievements in the client's data); what a
-- character has done towards them is the only thing stored here.
--
-- character_records holds the value a character has reached for a char_records row (which names a kind and
-- its parameters). Records are the counters achievements are evaluated against, and they are kept as a
-- high-water mark: a record can fall — ability levels fall on a respec — and an achievement that has been
-- earned is not taken back.

CREATE TABLE IF NOT EXISTS `character_records` (
  `owner` int unsigned NOT NULL,
  `record_id` int unsigned NOT NULL,
  `value` int NOT NULL DEFAULT 0,
  PRIMARY KEY (`owner`, `record_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Per-character value of each char_records counter';

-- One row per character per achievement they have made progress on. `amount` is how far they have got —
-- the record total the achievement asks for, or how many of its objectives are done, depending on what its
-- complete_num counts; `completed_at` is the completion time the client's packets carry, and NULL means
-- still in progress.

CREATE TABLE IF NOT EXISTS `character_achievements` (
  `owner` int unsigned NOT NULL,
  `achievement_id` int unsigned NOT NULL,
  `amount` int NOT NULL DEFAULT 0,
  `completed_at` datetime NULL DEFAULT NULL,
  PRIMARY KEY (`owner`, `achievement_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Achievement progress and completion per character';
