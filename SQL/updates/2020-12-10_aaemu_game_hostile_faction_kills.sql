ALTER TABLE `aaemu_game`.`characters`
ADD COLUMN `pvp_honor` int(11) NOT NULL DEFAULT 0 AFTER `crime_record`,
ADD COLUMN `hostile_faction_kills` int(11) NOT NULL DEFAULT 0 AFTER `crime_record`;