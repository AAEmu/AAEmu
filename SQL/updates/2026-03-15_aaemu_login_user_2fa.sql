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
