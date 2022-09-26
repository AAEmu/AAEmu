DROP TABLE IF EXISTS `accounts`;
CREATE TABLE `accounts` (
  `account_id` INT NOT NULL,
  `credits` INT NOT NULL DEFAULT 0,
  PRIMARY KEY (`account_id`));
