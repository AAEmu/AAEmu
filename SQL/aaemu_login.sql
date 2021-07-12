CREATE DATABASE  IF NOT EXISTS `aaemu_login` ;
USE `aaemu_login`;
-- ----------------------------------------------------------------------------------------------
-- Make sure to remove the above two lines if you want use your own DB/Schema names during import
-- ----------------------------------------------------------------------------------------------

DROP TABLE IF EXISTS `game_servers`;
CREATE TABLE `game_servers` (
  `id` tinyint unsigned NOT NULL,
  `name` text NOT NULL,
  `host` varchar(128) NOT NULL,
  `port` int NOT NULL,
  `hidden` tinyint(1) NOT NULL DEFAULT '1',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC COMMENT='Server list';


DROP TABLE IF EXISTS `users`;
CREATE TABLE `users` (
  `id` int unsigned NOT NULL AUTO_INCREMENT,
  `username` varchar(32) NOT NULL,
  `password` text COMMENT 'Hashed password of the user',
  `email` varchar(128) NOT NULL,
  `last_login` bigint unsigned NOT NULL DEFAULT '0',
  `last_ip` varchar(128) NOT NULL,
  `created_at` bigint unsigned NOT NULL DEFAULT '0',
  `updated_at` bigint unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  KEY `username` (`username`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC COMMENT='Account login information';
