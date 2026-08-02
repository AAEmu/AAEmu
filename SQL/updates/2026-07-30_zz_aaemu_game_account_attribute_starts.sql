-- Preserve the grant time carried by the native SCAccountAttributeList/Updated payloads.
-- The database updater records this migration and executes it exactly once.
ALTER TABLE `account_attributes`
    ADD COLUMN `starts` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP AFTER `count`;
