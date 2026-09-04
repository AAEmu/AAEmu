USE aaemu_game;

-- Recruitment-board "interest" tag bitmask shown as icons in the guild info panel and set via
-- CSExpeditionInterestUpatePacket (X2Faction:SetMyExpeditionInterest) - carried by SCExpeditionDescPacket's
-- "interest" field, previously always hardcoded to 0 since nothing could set it.
ALTER TABLE `expeditions` ADD COLUMN `interest` SMALLINT NOT NULL DEFAULT '0' AFTER `residence_house_id`;
