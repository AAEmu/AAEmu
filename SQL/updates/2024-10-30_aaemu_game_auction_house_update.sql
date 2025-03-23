ALTER TABLE `auction_house`
ADD COLUMN `post_date` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Time when the auction item was put up for sale (in UTC)' AFTER `item_id`;