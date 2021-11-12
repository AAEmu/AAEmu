-- -------------------------------------------------
-- Fix INDEXES
-- -------------------------------------------------
ALTER TABLE `housings` ADD PRIMARY KEY `PRIMARY` (`id`), DROP INDEX `PRIMARY`;

ALTER TABLE `expeditions` ADD PRIMARY KEY `PRIMARY` (`id`), DROP INDEX `PRIMARY`;

ALTER TABLE `characters` ADD PRIMARY KEY `PRIMARY` (`id`), DROP INDEX `PRIMARY`;