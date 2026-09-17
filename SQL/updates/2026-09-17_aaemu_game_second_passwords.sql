USE aaemu_game;

CREATE TABLE IF NOT EXISTS `account_second_passwords` (
  `account_id` int unsigned NOT NULL COMMENT 'Account the password belongs to',
  `salt` varchar(64) NOT NULL COMMENT 'Base64 per-password salt the key was derived with',
  `hash` varchar(128) NOT NULL COMMENT 'Base64 PBKDF2-SHA256 derived key, never the password',
  `failed_count` int NOT NULL DEFAULT 0 COMMENT 'Wrong answers accumulated, reset on a correct one',
  `updated_at` datetime NOT NULL COMMENT 'When the row was last written',
  PRIMARY KEY (`account_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Account second passwords; the actions they guard outlive a World process, so they are kept here';
