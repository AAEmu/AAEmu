-- Ranking boards (`systems::social::ranking_manager` / `ranking_persistence`): one row per
-- character, board (`ranks.id`) and scoring period, plus a ledger of the periods whose rewards
-- were already paid.
--
-- `period_start` / `period_end` / `updated` are UTC Unix seconds. `period_start` is half of the
-- score's identity: a board reset starts a new row and the finished one stays for the frozen
-- standings and the payout. `v1` / `v2` are the signed board figures (arena score + wins, best
-- fishing length or total weight). `account` is the name the client shows next to the row (the
-- character name; the game database does not own the login account names). There is deliberately
-- no foreign key to `characters`: a deleted character's rows are simply never read again.

CREATE TABLE IF NOT EXISTS `character_rank_scores` (
  `rank_id`      int unsigned NOT NULL COMMENT 'ranks.id',
  `period_start` bigint NOT NULL COMMENT 'Unix seconds UTC, start of the scoring period',
  `character_id` int unsigned NOT NULL,
  `period_end`   bigint NOT NULL COMMENT 'Unix seconds UTC, end of the scoring period',
  `account_id`   int unsigned NOT NULL DEFAULT 0,
  `account`      varchar(16) NOT NULL DEFAULT '',
  `world_id`     tinyint unsigned NOT NULL DEFAULT 0,
  `v1`           int NOT NULL DEFAULT 0 COMMENT 'Primary figure (signed)',
  `v2`           int NOT NULL DEFAULT 0 COMMENT 'Secondary figure (signed)',
  `updated`      bigint NOT NULL DEFAULT 0 COMMENT 'Unix seconds UTC the figures last moved',
  PRIMARY KEY (`rank_id`, `period_start`, `character_id`),
  KEY `character_id` (`character_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Ranking board scores per character and period';

CREATE TABLE IF NOT EXISTS `rank_period_payouts` (
  `rank_id`      int unsigned NOT NULL COMMENT 'ranks.id',
  `period_start` bigint NOT NULL COMMENT 'Unix seconds UTC, start of the paid period',
  `paid_at`      bigint NOT NULL COMMENT 'Unix seconds UTC the rewards were mailed',
  PRIMARY KEY (`rank_id`, `period_start`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Ranking periods whose rewards were already mailed';
