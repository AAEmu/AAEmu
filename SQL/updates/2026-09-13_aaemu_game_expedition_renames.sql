CREATE TABLE IF NOT EXISTS `expedition_renames` (
  `expedition_id` int unsigned NOT NULL,
  `last_renamed_at` datetime(6) NOT NULL,
  PRIMARY KEY (`expedition_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Guild rename cooldown state';

ALTER TABLE `characters`
  ADD COLUMN `expedition_rejoin_until` bigint NOT NULL DEFAULT '0' AFTER `expedition_id`;
