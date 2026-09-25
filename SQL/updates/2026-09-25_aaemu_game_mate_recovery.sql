-- GF-C09A: persist the content-backed mate recovery snapshot.
-- Columns stay nullable so legacy rows are not assigned a guessed recovery value.
-- CharacterMates fills them from item_summon_mates -> npcs on the next summon.

ALTER TABLE `mates`
  ADD COLUMN `mate_revive_delay` int NULL COMMENT 'Snapshot of npcs.mate_revive_delay' AFTER `owner`,
  ADD COLUMN `mate_revive_hp_percent` int NULL COMMENT 'Snapshot of npcs.mate_revive_hp_percent' AFTER `mate_revive_delay`,
  ADD COLUMN `mate_revive_mp_percent` int NULL COMMENT 'Snapshot of npcs.mate_revive_mp_percent' AFTER `mate_revive_hp_percent`;
