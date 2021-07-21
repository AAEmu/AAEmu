-- -------------------------------------------------
-- Remove old rotations
-- -------------------------------------------------
ALTER TABLE `characters`
DROP `rotation_x`,
DROP `rotation_y`,
DROP `rotation_z`;
