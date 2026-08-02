-- Character privacy is a server-authoritative, per-character option.
-- The 10.0.2.13 wire type is signed i8 and its UI exposes 0 (off) and 1 (on).

ALTER TABLE `characters`
  ADD COLUMN `privacy_status` TINYINT NOT NULL DEFAULT 0 AFTER `total_play_time`;
