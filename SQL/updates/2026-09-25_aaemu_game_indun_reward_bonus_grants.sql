-- W03B: durable typed bonus-count grants for one indun mail delivery transaction.
CREATE TABLE IF NOT EXISTS `indun_reward_bonus_grants` (
  `run_id` varchar(128) NOT NULL,
  `instance_id` int unsigned NOT NULL,
  `instance_reward_kind_id` int unsigned NOT NULL,
  `character_id` int unsigned NOT NULL,
  `instance_reward_id` int unsigned NOT NULL,
  `buff_id` int unsigned NOT NULL,
  `bonus_count` int unsigned NOT NULL,
  `granted_at` datetime(6) NOT NULL,
  PRIMARY KEY (`run_id`, `instance_id`, `instance_reward_kind_id`, `character_id`, `instance_reward_id`, `buff_id`),
  KEY `idx_indun_reward_bonus_buff` (`buff_id`, `granted_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='W03B typed instance reward bonus counts committed with the mail claim';
