USE aaemu_game;

-- war_is_declarer was originally folded into 2026-09-02_aaemu_game_expedition_war.sql, but
-- MySqlDatabaseUpdater tracks completed updates by filename only (not content), so any server that had
-- already run an earlier revision of that file (before this column was added) would have it marked
-- installed and silently never pick up war_is_declarer at all. Split into its own file so it always
-- applies regardless of when a server first ran the original migration.
--
-- Was in-memory only (set at DeclareWar, never persisted), so a World restart during an active war lost
-- which side had declared - both expeditions' WarIsDeclarer then defaulted to false, and EndWar's
-- declarer/defender split (only the defender gets post-war protection) came out wrong for every war that
-- happened to be running across a restart.
--
-- Guarded (IF NOT EXISTS check, not a plain ALTER) because a server that ran the earlier revision of the
-- 2026-09-02 file already has this column - an unconditional ADD COLUMN would hard-fail with a duplicate
-- column error on those servers and abort startup. The cleanup UPDATE only runs inside that same
-- not-yet-added branch: if the column already existed, any active wars on it already have real,
-- server-produced declarer data (from having run the fixed code since), so there is nothing to clear.
DROP PROCEDURE IF EXISTS `aaemu_migrate_war_is_declarer`;

CREATE PROCEDURE `aaemu_migrate_war_is_declarer`()
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM `INFORMATION_SCHEMA`.`COLUMNS`
        WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = 'expeditions' AND `COLUMN_NAME` = 'war_is_declarer'
    ) THEN
        ALTER TABLE `expeditions` ADD COLUMN `war_is_declarer` TINYINT(1) NOT NULL DEFAULT '0' AFTER `war_kill_score`;

        -- There is no source of truth to reconstruct who declared a war that was already active before
        -- this column existed (war_declared_at is set identically on both sides). Rather than leave such
        -- a war with both sides defaulted to war_is_declarer=false - which would grant post-war
        -- protection to BOTH guilds instead of just the defender - clear its in-flight war state so
        -- stale/unknown data can't be acted on. Participants can simply redeclare.
        UPDATE `expeditions`
        SET `war_enemy_expedition_id` = 0, `war_declared_at` = NULL, `war_protected_until` = NULL, `war_ends_at` = NULL, `war_kill_score` = 0
        WHERE `war_enemy_expedition_id` != 0;
    END IF;
END;

CALL `aaemu_migrate_war_is_declarer`();

DROP PROCEDURE IF EXISTS `aaemu_migrate_war_is_declarer`;
