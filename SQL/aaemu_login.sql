CREATE DATABASE IF NOT EXISTS `aaemu_login`;
USE `aaemu_login`;
-- ----------------------------------------------------------------------------------------------
-- Make sure to remove the above two lines if you want use your own DB/Schema names during import
-- This script is idempotent. It can be run multiple times without causing errors, and does not
-- clear data from existing tables.
-- ----------------------------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS `users` (
  `id` int unsigned NOT NULL AUTO_INCREMENT,
  `username` varchar(32) NOT NULL,
  `password` text COMMENT 'Hashed password of the user',
  `korea_challenge_hash` varchar(120) DEFAULT NULL COMMENT 'sha256_crypt $5$ hash used as AES-256 key for Korea challenge-response auth (V2).',
  `email` varchar(128) NOT NULL,
  `last_login` bigint unsigned NOT NULL DEFAULT '0',
  `last_ip` varchar(128) NOT NULL,
  `created_at` bigint unsigned NOT NULL DEFAULT '0',
  `updated_at` bigint unsigned NOT NULL DEFAULT '0',
  `banned` int NOT NULL DEFAULT '0',
  `ban_reason` int NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  KEY `username` (`username`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC COMMENT='Account login information';
