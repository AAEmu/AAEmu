USE aaemu_game;

-- Guild War: CSDeclareExpeditionWarPacket was a fully-parsed no-op stub - X2Faction:DeclareExpeditionWar
-- (and the whole guild-vs-guild war flow) never got any server-side reaction ("declare war does nothing").
-- War state is tracked per-expedition (each side sees its own war against `war_enemy_expedition_id`) so it
-- survives independently of the other side's row on reload/disband.
ALTER TABLE `expeditions` ADD COLUMN `war_enemy_expedition_id` INT UNSIGNED NOT NULL DEFAULT '0' AFTER `interest`;
ALTER TABLE `expeditions` ADD COLUMN `war_declared_at` DATETIME NULL DEFAULT NULL AFTER `war_enemy_expedition_id`;
ALTER TABLE `expeditions` ADD COLUMN `war_protected_until` DATETIME NULL DEFAULT NULL AFTER `war_declared_at`;
ALTER TABLE `expeditions` ADD COLUMN `war_ends_at` DATETIME NULL DEFAULT NULL AFTER `war_protected_until`;
ALTER TABLE `expeditions` ADD COLUMN `war_kill_score` INT UNSIGNED NOT NULL DEFAULT '0' AFTER `war_ends_at`;
-- Was in-memory only (set at DeclareWar, never persisted), so a World restart during an active war lost
-- which side had declared - both expeditions' WarIsDeclarer then defaulted to false, and EndWar's
-- declarer/defender split (only the defender gets post-war protection) came out wrong for every war that
-- happened to be running across a restart.
ALTER TABLE `expeditions` ADD COLUMN `war_is_declarer` TINYINT(1) NOT NULL DEFAULT '0' AFTER `war_kill_score`;
