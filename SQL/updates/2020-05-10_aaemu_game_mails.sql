-- ----------------------------
-- items update
-- ----------------------------
ALTER TABLE `mails` 
CHANGE COLUMN `attachments` `attachment_count` INT(11) NOT NULL DEFAULT 0 ,
ADD COLUMN `sender_id` INT(11) NOT NULL DEFAULT 0 AFTER `text`,
ADD COLUMN `receiver_id` INT(11) NOT NULL DEFAULT 0 AFTER `attachment_count`,
ADD COLUMN `attachment0` BIGINT(20) NOT NULL DEFAULT 0 AFTER `money_amount_3`,
ADD COLUMN `attachment1` BIGINT(20) NOT NULL DEFAULT 0 AFTER `attachment0`,
ADD COLUMN `attachment2` BIGINT(20) NOT NULL DEFAULT 0 AFTER `attachment1`,
ADD COLUMN `attachment3` BIGINT(20) NOT NULL DEFAULT 0 AFTER `attachment2`,
ADD COLUMN `attachment4` BIGINT(20) NOT NULL DEFAULT 0 AFTER `attachment3`,
ADD COLUMN `attachment5` BIGINT(20) NOT NULL DEFAULT 0 AFTER `attachment4`,
ADD COLUMN `attachment6` BIGINT(20) NOT NULL DEFAULT 0 AFTER `attachment5`,
ADD COLUMN `attachment7` BIGINT(20) NOT NULL DEFAULT 0 AFTER `attachment6`,
ADD COLUMN `attachment8` BIGINT(20) NOT NULL DEFAULT 0 AFTER `attachment7`,
ADD COLUMN `attachment9` BIGINT(20) NOT NULL DEFAULT 0 AFTER `attachment8`;

-- We no longer need the old items table
DROP TABLE `mails_items`;