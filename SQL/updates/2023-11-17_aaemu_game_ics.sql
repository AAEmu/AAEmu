-- -------------------------------------------------
-- Add new In Game Cash shop tables
-- -------------------------------------------------

CREATE TABLE `ics_skus` (
    `sku` INT(10) UNSIGNED NOT NULL AUTO_INCREMENT,
    `shop_id` INT(10) UNSIGNED NULL DEFAULT '0' COMMENT 'Reference to the shop item',
    `position` INT(10) NULL DEFAULT '0' COMMENT 'Used for display order inside the item details',
    `item_id` INT(10) UNSIGNED NULL DEFAULT '0' COMMENT 'Item that is for sale',
    `item_count` INT(10) UNSIGNED NULL DEFAULT '1' COMMENT 'Number of items for this detail',
    `select_type` TINYINT(3) UNSIGNED NULL DEFAULT '0',
    `is_default` TINYINT(3) UNSIGNED NULL DEFAULT '0' COMMENT 'Is this the default selection?',
    `event_type` TINYINT(3) UNSIGNED NULL DEFAULT '0',
    `event_end_date` DATETIME NULL DEFAULT NULL,
    `currency` TINYINT(3) UNSIGNED NULL DEFAULT '0' COMMENT 'Credits(0), AAPoints(1), Loyalty(2), Coins(3)',
    `price` INT(10) UNSIGNED NULL DEFAULT '0' COMMENT 'Price of the item',
    `discount_price` INT(10) UNSIGNED NULL DEFAULT '0' COMMENT 'Discounted price (this is used if set)',
    `bonus_item_id` INT(10) UNSIGNED NULL DEFAULT '0' COMMENT 'Bonus item included for this purchase',
    `bonus_item_count` INT(10) UNSIGNED NULL DEFAULT '0' COMMENT 'Amount of bonus items included',
    PRIMARY KEY (`sku`) USING BTREE
)
COMMENT='Has the actual sales items for the details'
COLLATE='utf8mb4_0900_ai_ci'
ENGINE=InnoDB
AUTO_INCREMENT=1000000
;

CREATE TABLE `ics_shop_items` (
    `shop_id` INT(10) UNSIGNED NOT NULL AUTO_INCREMENT COMMENT 'SKU item id',
    `display_item_id` INT(10) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Item who\'s icon to use for displaying in the shop, leave 0 for first item in the group',
    `name` TEXT NULL DEFAULT NULL COMMENT 'Can be used to override the name in the shop' COLLATE 'utf8mb4_0900_ai_ci',
    `limited_type` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Enables limited stock mode if non-zero',
    `limited_stock_max` INT(10) UNSIGNED NOT NULL DEFAULT '100' COMMENT 'Number of items left in stock for this SKU if limited stock is enabled',
    `level_min` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Minimum level to buy the item (does not show on UI)',
    `level_max` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Maximum level to buy the item (does not show on UI)',
    `buy_restrict_type` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Buy restriction rule, none (0), level (1) or quest(2)',
    `buy_restrict_id` INT(10) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Level or QuestId for restrict rule',
    `is_sale` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0',
    `is_hidden` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0',
    `sale_start` DATETIME NULL DEFAULT NULL COMMENT 'Limited sale start time',
    `sale_end` DATETIME NULL DEFAULT NULL COMMENT 'Limited sale end time',
    `shop_buttons` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'All (0), NoCart (1), NoGift (2), OnlyBuy (3)',
    `remaining` INT(11) NOT NULL DEFAULT '-1' COMMENT 'Number of items remaining, only for tab 1-1 (limited)',
    PRIMARY KEY (`shop_id`) USING BTREE
)
COMMENT='Possible Item listings that are for sale'
COLLATE='utf8mb4_0900_ai_ci'
ENGINE=InnoDB
AUTO_INCREMENT=2000000
;

CREATE TABLE `ics_menu` (
    `main_tab` TINYINT(3) UNSIGNED NOT NULL DEFAULT '1' COMMENT 'Which main tab to display on',
    `sub_tab` TINYINT(3) UNSIGNED NOT NULL DEFAULT '1' COMMENT 'Which sub tab to display on',
    `tab_pos` INT(11) NOT NULL DEFAULT '0' COMMENT 'Used to change display order',
    `shop_id` INT(11) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Id of the item group for sale (shop item)',
    PRIMARY KEY (`main_tab`, `sub_tab`, `tab_pos`) USING BTREE
)
COMMENT='Contains what item will be displayed on which tab'
COLLATE='utf8mb4_0900_ai_ci'
ENGINE=InnoDB
;
