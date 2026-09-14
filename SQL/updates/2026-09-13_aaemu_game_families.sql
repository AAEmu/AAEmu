CREATE TABLE IF NOT EXISTS `families` (
  `id` int unsigned NOT NULL,
  `name` varchar(256) NOT NULL DEFAULT '',
  `notice` varchar(800) NOT NULL DEFAULT '',
  `level` int unsigned NOT NULL DEFAULT '1',
  `exp` int unsigned NOT NULL DEFAULT '0',
  `increased_member_count` int unsigned NOT NULL DEFAULT '0',
  `reset_time` bigint NOT NULL DEFAULT '0',
  `change_name_time` bigint NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Families';

ALTER TABLE `family_members`
  ADD COLUMN `role_update_time` bigint NOT NULL DEFAULT '0' AFTER `role`;

ALTER TABLE `family_members`
  ADD COLUMN `login_reward_time` bigint NOT NULL DEFAULT '0' AFTER `role_update_time`;

UPDATE `family_members` SET `title`='' WHERE `title` IS NULL;

ALTER TABLE `family_members`
  MODIFY COLUMN `title` varchar(104) NOT NULL DEFAULT '';

CREATE TABLE IF NOT EXISTS `family_act_sanctions` (
  `family_id` int unsigned NOT NULL,
  `type` tinyint unsigned NOT NULL,
  `end_time` bigint NOT NULL DEFAULT '0',
  PRIMARY KEY (`family_id`,`type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Family action sanctions';

ALTER TABLE `characters`
  ADD COLUMN `family_rejoin_until` bigint NOT NULL DEFAULT '0' AFTER `family`;

INSERT IGNORE INTO `families` (`id`)
SELECT DISTINCT `family`
FROM `characters`
WHERE `family` <> 0;
