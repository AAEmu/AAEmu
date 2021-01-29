ALTER TABLE `characters` 
ADD COLUMN `pvp_honor` bigint NOT NULL DEFAULT 0 AFTER `deleted`;

ALTER TABLE `characters` 
ADD COLUMN `pvp_kills` bigint NOT NULL DEFAULT 0 AFTER `pvp_honor`;
