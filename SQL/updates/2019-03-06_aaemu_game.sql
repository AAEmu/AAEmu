DROP TABLE IF EXISTS `housings`;
CREATE TABLE IF NOT EXISTS `housings` (
  `id` int(11) NOT NULL,
  `account_id` int(10) UNSIGNED NOT NULL,
  `owner` int(10) UNSIGNED NOT NULL,
  `template_id` int(10) UNSIGNED NOT NULL,
  `x` float NOT NULL,
  `y` float NOT NULL,
  `z` float NOT NULL,
  `rotation_z` tinyint(4) NOT NULL,
  `current_step` tinyint(4) NOT NULL,
  `permission` tinyint(4) NOT NULL,
  PRIMARY KEY (`account_id`,`owner`,`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;