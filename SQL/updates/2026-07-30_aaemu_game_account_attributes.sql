-- Account-scoped attributes granted by AccountAttributeEffect.
--
-- enum_account_attribute_kinds is 1 auction_post, 2 account_buff, 3 ulc. These belong to the account rather
-- than to a character, and account_attribute_effects.bind_world distinguishes an entry confined to one shard
-- from one that follows the account everywhere — world_id 0 means account-wide.

CREATE TABLE IF NOT EXISTS `account_attributes` (
    `account_id` INT UNSIGNED   NOT NULL,
    `kind_id`    INT UNSIGNED   NOT NULL,
    `kind_value` INT UNSIGNED   NOT NULL DEFAULT 0,
    `world_id`   INT UNSIGNED   NOT NULL DEFAULT 0,
    `count`      INT            NOT NULL DEFAULT 0,
    `expires`    DATETIME       NOT NULL DEFAULT '9999-12-31 23:59:59',
    PRIMARY KEY (`account_id`, `kind_id`, `kind_value`, `world_id`),
    INDEX `idx_account_attributes_account` (`account_id`)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;
