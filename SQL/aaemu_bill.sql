-- Bill Server cash + product catalog (MySQL stand-in for retail MSSQL cash_db).
-- Retail wire still uses AA_CASH / AA_BONUS_CASH priceTypes 0 / 5.

CREATE DATABASE IF NOT EXISTS `aaemu_bill` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;
USE `aaemu_bill`;

CREATE TABLE IF NOT EXISTS `cash_balance` (
  `account_id` BIGINT NOT NULL PRIMARY KEY,
  `cash` INT NOT NULL DEFAULT 0 COMMENT 'AA_CASH (priceType 0)',
  `bonus_cash` INT NOT NULL DEFAULT 0 COMMENT 'AA_BONUS_CASH (priceType 5)',
  `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `cash_ledger` (
  `ledger_id` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
  `op_id` VARCHAR(128) NOT NULL,
  `kind` VARCHAR(8) NOT NULL,
  `account_id` BIGINT NOT NULL,
  `char_id` INT NOT NULL DEFAULT 0,
  `world_id` INT NOT NULL DEFAULT 0,
  `amount` INT NOT NULL,
  `price_type` INT NOT NULL DEFAULT 0,
  `source` VARCHAR(64) NULL,
  `aux1` INT NULL,
  `aux2` INT NULL,
  `remain_cash` INT NOT NULL DEFAULT 0,
  `remain_bonus` INT NOT NULL DEFAULT 0,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY `uk_op_id` (`op_id`),
  KEY `idx_account` (`account_id`)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS `cash_buy_request` (
  `request_id` BIGINT NOT NULL,
  `slot` INT NOT NULL,
  `account_id` BIGINT NOT NULL,
  `char_id` INT NOT NULL DEFAULT 0,
  `buy_source` INT NOT NULL DEFAULT 1,
  `cash_shop_id` INT NOT NULL,
  `price_type` INT NOT NULL DEFAULT 0,
  `price` INT NOT NULL DEFAULT 0,
  `limit_type` INT NOT NULL DEFAULT 0,
  `buy_limit` INT NOT NULL DEFAULT 0,
  `source` VARCHAR(64) NULL,
  `confirmed` TINYINT NOT NULL DEFAULT 0,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`request_id`, `slot`),
  KEY `idx_product` (`account_id`, `cash_shop_id`)
) ENGINE=InnoDB;

-- Catalog controlled by BillManager (available 0/1, price, purchase limits).
-- Publish (admin API) can sync available rows into aaemu_game ics_* for World CashShopManager.
CREATE TABLE IF NOT EXISTS `bill_products` (
  `shop_id` INT UNSIGNED NOT NULL PRIMARY KEY COMMENT 'ICS shop_id',
  `sku` INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'ICS sku (0 = shop_id + 1000000 base)',
  `item_id` INT UNSIGNED NOT NULL DEFAULT 0,
  `item_count` INT UNSIGNED NOT NULL DEFAULT 1,
  `name` VARCHAR(128) NOT NULL DEFAULT '',
  `available` TINYINT NOT NULL DEFAULT 1 COMMENT '1=visible/purchasable, 0=hidden',
  `price` INT UNSIGNED NOT NULL DEFAULT 0,
  `discount_price` INT UNSIGNED NOT NULL DEFAULT 0,
  `price_type` SMALLINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '0=AA_CASH, 5=AA_BONUS; World ICS currency maps separately',
  `ics_currency` TINYINT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'CashShopCurrencyType: Credits=0 AaPoints=1 Loyalty=2 Coins=3',
  `buy_limit` INT UNSIGNED NOT NULL DEFAULT 0 COMMENT '0=unlimited',
  `limit_type` TINYINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '0=none 1=account 2=character',
  `main_tab` TINYINT UNSIGNED NOT NULL DEFAULT 1,
  `sub_tab` TINYINT UNSIGNED NOT NULL DEFAULT 1,
  `tab_pos` INT NOT NULL DEFAULT 0,
  `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB;

INSERT IGNORE INTO `bill_products`
  (`shop_id`,`sku`,`item_id`,`item_count`,`name`,`available`,`price`,`discount_price`,`price_type`,`ics_currency`,`buy_limit`,`limit_type`,`main_tab`,`sub_tab`,`tab_pos`)
VALUES
  (2000000, 1000000, 29176, 1, 'Starter Pack Credit Test', 1, 100, 0, 0, 0, 0, 0, 1, 1, 0),
  (2000001, 1000001, 29177, 1, 'Limited Mount Coupon', 1, 500, 400, 0, 0, 3, 1, 1, 1, 1),
  (2000002, 1000002, 29178, 5, 'Hidden Glider (off)', 0, 250, 0, 0, 0, 0, 0, 1, 2, 0);
