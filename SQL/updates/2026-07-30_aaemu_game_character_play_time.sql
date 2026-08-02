-- Accumulated seconds a character has been played, reported by SCPlayerGameData.
--
-- The 10.0.2.13 serializer names that packet's first field totalPlayTime, and nothing was tracking it — the
-- server sent a client-data revision number in its place, so the figure the client showed was meaningless.

ALTER TABLE `characters`
    ADD COLUMN `total_play_time` INT UNSIGNED NOT NULL DEFAULT 0 AFTER `created_at`;
