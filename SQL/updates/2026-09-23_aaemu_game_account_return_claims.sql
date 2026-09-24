-- Account-return ("welcome back") reward claims: one claim per account.
--
-- The gate for CSTakeReturnAccountItem (0x1EF). The primary key is the exactly-once ledger: the claim
-- transaction inserts here first, and a duplicate primary key refuses the second claim before any reward
-- is staged. return_account_* days and the reward item type stay in content_configs (enum 274-276).

CREATE TABLE IF NOT EXISTS `account_return_claims` (
    `account_id`       INT UNSIGNED NOT NULL,
    `claimed_at`       DATETIME     NOT NULL,
    `reward_item_type` INT UNSIGNED NOT NULL,
    PRIMARY KEY (`account_id`)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = 'Account-return reward claims, one per account';

ALTER TABLE `accounts`
    ADD COLUMN `return_qualifying_login` DATETIME NULL DEFAULT NULL;
