-- -------------------------------------------------
-- Add UCC fields
-- -------------------------------------------------
ALTER TABLE `items`
ADD `ucc` int unsigned NOT NULL DEFAULT '0';
