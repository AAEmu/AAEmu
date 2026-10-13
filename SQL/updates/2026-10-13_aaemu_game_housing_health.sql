-- NULL preserves authored construction health for pre-migration properties. Subsequent
-- world saves persist damage/repairs instead of healing buildings on every restart.
ALTER TABLE `housings` ADD COLUMN `hp` int DEFAULT NULL AFTER `allow_recover`;
