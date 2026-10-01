-- W03 mates: tell a never-recorded recovery bar apart from a real zero.
--
-- mates.hp and mates.mp become NULL-able so the database's own NULL can mean "this row never
-- held a bar". Zero stays a real reading. Without this, an existing server keeps int NOT NULL and
-- every save of an unrecorded mate fails with MySQL error 1048 on the whole character.

ALTER TABLE `mates` MODIFY COLUMN `hp` int NULL COMMENT 'recorded health, NULL when never recorded';
ALTER TABLE `mates` MODIFY COLUMN `mp` int NULL COMMENT 'recorded mana, NULL when never recorded';

-- Rows that predate this carry a 0 that the old build wrote as a placeholder, so they read back as a
-- genuine empty bar. Restoring those to the mate's maximum gives the behaviour this change had before.
-- Left commented so it is an operator decision rather than a silent rewrite of every existing row.
-- UPDATE `mates` SET `hp` = NULL, `mp` = NULL WHERE `hp` = 0 AND `mp` = 0;
