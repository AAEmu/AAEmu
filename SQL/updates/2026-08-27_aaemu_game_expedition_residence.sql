USE aaemu_game;

-- Guild Residence: a universal per-guild clubhouse (item_housings designs 830/831/832, "Green/Red/Blue
-- Flag Residence of High Spirit"), unrelated to castle/dominion territory ownership - any guild may place
-- exactly one, in any normal housing zone. 0 = no residence placed yet.
ALTER TABLE `expeditions` ADD COLUMN `residence_house_id` INT UNSIGNED NOT NULL DEFAULT '0' AFTER `notice`;
