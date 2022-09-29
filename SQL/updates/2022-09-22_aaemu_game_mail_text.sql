-- -------------------------------------------------
-- Mail Title and Text size
-- -------------------------------------------------
ALTER TABLE `mails` CHANGE `title` `title` TEXT CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL;
ALTER TABLE `mails` CHANGE `text` `text` TEXT CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL;