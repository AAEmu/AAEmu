-- W03A inserts the claim before staging the letter; mail_id is filled on the same transaction.
-- run_id is the caller's persisted logical run identity; instance_id keeps the ledger explicit about content.
-- The current live-dungeon fallback is explicitly not an automatic process-restart recovery mechanism.
CREATE TABLE IF NOT EXISTS `indun_reward_claims` (
  `run_id` varchar(128) NOT NULL,
  `instance_id` int unsigned NOT NULL,
  `instance_reward_kind_id` int unsigned NOT NULL,
  `character_id` int unsigned NOT NULL,
  `mail_id` bigint unsigned NULL,
  `claimed_at` datetime(6) NOT NULL,
  PRIMARY KEY (`run_id`, `instance_id`, `character_id`, `instance_reward_kind_id`),
  KEY `idx_indun_reward_claims_instance` (`instance_id`, `instance_reward_kind_id`),
  KEY `idx_indun_reward_claims_character` (`character_id`, `claimed_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='W03A indun mail reward claims, one per character and logical run';
