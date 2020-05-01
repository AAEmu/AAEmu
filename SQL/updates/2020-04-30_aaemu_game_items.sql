-- ----------------------------
-- items update
-- ----------------------------
ALTER TABLE `items`
ADD COLUMN `bounded` tinyint(1) DEFAULT '0' AFTER `created_at`;
