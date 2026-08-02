ALTER TABLE `friends`
  ADD COLUMN `status` tinyint unsigned NOT NULL DEFAULT '0' AFTER `owner`,
  ADD COLUMN `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP AFTER `status`;
