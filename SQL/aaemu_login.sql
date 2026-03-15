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


CREATE TABLE IF NOT EXISTS `user_2fa` (
  `user_id` int unsigned NOT NULL,
  `enabled_methods` tinyint unsigned NOT NULL DEFAULT '0' COMMENT 'Bitmask: 1=OTP, 2=PcCert, 4=ARS',

  -- OTP (TOTP)
  `otp_secret` varchar(64) DEFAULT NULL COMMENT 'Base32-encoded TOTP secret',
  `otp_verified` tinyint(1) NOT NULL DEFAULT '0',

  -- PcCert (PIN-based)
  `cert_pin_hash` text DEFAULT NULL COMMENT 'Hashed PIN',

  -- ARS (phone callback)
  `ars_phone_number` varchar(20) DEFAULT NULL,
  `ars_phone_verified` tinyint(1) NOT NULL DEFAULT '0',

  `created_at` bigint unsigned NOT NULL DEFAULT '0',
  `updated_at` bigint unsigned NOT NULL DEFAULT '0',

  PRIMARY KEY (`user_id`),
  CONSTRAINT `fk_user_2fa_user_id` FOREIGN KEY (`user_id`)
    REFERENCES `users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC COMMENT='Two-factor authentication settings for Korea auth';
