USE aaemu_game;

-- GF-S15: cross-server departure / transfer / rollback / re-entry journal.
-- One row per character (primary key) is the "exactly once" guarantee: a second departure
-- collides on the key, a settled transfer is consumed by re-entry, and a restart mid-transfer
-- finds the row still `state = 1` (parked) and rolls it back deterministically.
-- `snapshot_json` holds the character state frozen at departure (money, money2, aa_point and the
-- item count/fingerprint witness) so a failed transfer restores the character instead of leaving
-- a half-migrated one behind.
CREATE TABLE IF NOT EXISTS `character_transfer_journals` (
  `character_id` INT UNSIGNED NOT NULL,
  `account_id` INT UNSIGNED NOT NULL,
  `source_server_key` VARCHAR(64) NOT NULL,
  `target_server_key` VARCHAR(64) NOT NULL,
  `state` TINYINT UNSIGNED NOT NULL COMMENT '1 parked, 2 transferred, 3 rolled back, 4 re-entered',
  `snapshot_json` TEXT NOT NULL,
  `created_at` DATETIME(6) NOT NULL,
  `updated_at` DATETIME(6) NOT NULL,
  PRIMARY KEY (`character_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Cross-server transfer journal: departure parks, settle/rollback/re-entry consume, one live transfer per character';
