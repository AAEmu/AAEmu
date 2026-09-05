-- Widen listing money to match the 10.0.2.13 s64 wire and keep sold-record history
-- next to the same items/mail rows.

ALTER TABLE `auction_house`
	MODIFY COLUMN `start_money` BIGINT(20) NOT NULL,
	MODIFY COLUMN `direct_money` BIGINT(20) NOT NULL,
	MODIFY COLUMN `bid_money` BIGINT(20) NOT NULL,
	MODIFY COLUMN `extra` BIGINT(20) NOT NULL;

ALTER TABLE `auction_house`
	ADD COLUMN `asked` BIGINT(20) UNSIGNED NOT NULL DEFAULT 0 AFTER `direct_money`,
	ADD COLUMN `charge_percent` INT(11) NOT NULL DEFAULT 0 AFTER `asked`,
	ADD COLUMN `deposit_percent` INT(11) NOT NULL DEFAULT 0 AFTER `charge_percent`,
	ADD COLUMN `service_kind` TINYINT(4) NOT NULL DEFAULT 0 AFTER `deposit_percent`,
	ADD COLUMN `min_stack` INT(11) NOT NULL DEFAULT 1 AFTER `extra`,
	ADD COLUMN `max_stack` INT(11) NOT NULL DEFAULT 1 AFTER `min_stack`;

CREATE TABLE IF NOT EXISTS `auction_sold_records` (
	`id` BIGINT(20) NOT NULL AUTO_INCREMENT,
	`item_template_id` INT UNSIGNED NOT NULL,
	`item_grade` TINYINT UNSIGNED NOT NULL,
	`sold_at` DATETIME NOT NULL,
	`price` BIGINT(20) NOT NULL,
	`stack` INT(11) NOT NULL,
	PRIMARY KEY (`id`) USING BTREE,
	INDEX `idx_sold_lookup` (`item_template_id`, `item_grade`, `sold_at`)
)
COMMENT='Auction house sold-price history'
COLLATE='utf8mb4_general_ci'
ENGINE=InnoDB
ROW_FORMAT=DYNAMIC
;
