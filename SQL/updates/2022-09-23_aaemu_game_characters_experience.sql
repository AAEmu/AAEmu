-- -------------------------------------------------
-- Character experience typo fix
-- -------------------------------------------------
ALTER TABLE `characters` CHANGE `expirience` `experience` TEXT CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL;