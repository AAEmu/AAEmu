-- ----------------------------
-- Table structure for divine_clock
-- ----------------------------
DROP TABLE IF EXISTS `divine_clock`;
CREATE TABLE `divine_clock`  (
  `id` int NOT NULL AUTO_INCREMENT,
  `account_id` bigint UNSIGNED NOT NULL,
  `schedule_item_id` INT UNSIGNED NOT NULL COMMENT 'This field corresponds to the id field in the schedule_items table',
  `gave` tinyint NOT NULL DEFAULT 0 COMMENT 'Number of clicks taken today',
  `cumulated` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Time that has been passed already',
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci;

-- ----------------------------
-- Table structure for accounts
-- ----------------------------
-- Удаление поля divine_clock_time
ALTER TABLE `accounts`
DROP COLUMN `divine_clock_time`;

-- Удаление поля divine_clock_taken
ALTER TABLE `accounts`
DROP COLUMN `divine_clock_taken`;