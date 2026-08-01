-- Persist the PvP-death flags across server restarts so post-revive debuffs
-- routing in CSResurrectCharacterPacket can recover the correct death context
-- when a player happens to be dead at restart time.
ALTER TABLE `characters`
  ADD COLUMN `died_in_pvp` tinyint(1) NOT NULL DEFAULT '0' AFTER `pvp_honor`,
  ADD COLUMN `died_in_pvp_war_zone` tinyint(1) NOT NULL DEFAULT '0' AFTER `died_in_pvp`;
