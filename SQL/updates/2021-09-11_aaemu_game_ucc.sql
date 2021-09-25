-- -------------------------------------------------
-- Add UCC fields
-- -------------------------------------------------
ALTER TABLE `items`
ADD `ucc` int unsigned NOT NULL DEFAULT '0';

ALTER TABLE `uccs`
CHANGE `data` `data` mediumblob NULL AFTER `type`;