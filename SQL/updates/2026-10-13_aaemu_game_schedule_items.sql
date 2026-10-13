-- Account-wide schedule-item progress (`systems::npcs::schedule_item_runtime` and
-- `schedule_item_manager`): the claim counters of the 1.2 HUD timer rows (`schedule_items`).
--
-- `account_id` is a login account (shared with `aaemu_login`), not a character: every character
-- of the account reads and advances the same row per `schedule_id`. There is deliberately no
-- foreign key to `characters` (the row is not owned by a character) and none to `accounts` (the
-- game database does not own the login accounts).
--
-- `gave` is the u8 receipt counter, `cumulated` the accrued online seconds and `updated` the UTC
-- Unix seconds of the last accrual / receipt / reset (also the row's UTC day key). Session clocks
-- and per-character baselines are transient and are never stored.

CREATE TABLE IF NOT EXISTS `account_schedule_items` (
  `account_id`  int unsigned NOT NULL COMMENT 'Login account id (not a character)',
  `schedule_id` int NOT NULL COMMENT 'schedule_items.id',
  `gave`        tinyint unsigned NOT NULL DEFAULT 0 COMMENT 'Receipts already delivered (u8)',
  `cumulated`   bigint NOT NULL DEFAULT 0 COMMENT 'Accrued online seconds toward the current term',
  `updated`     bigint NOT NULL DEFAULT 0 COMMENT 'Unix seconds UTC of the last accrual / receipt / reset',
  PRIMARY KEY (`account_id`, `schedule_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Account-wide schedule-item claim progress';
