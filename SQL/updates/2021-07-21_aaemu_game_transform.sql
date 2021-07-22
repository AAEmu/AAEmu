-- -------------------------------------------------
-- Remove old rotations
-- -------------------------------------------------
ALTER TABLE `characters`
DROP `rotation_x`,
DROP `rotation_y`,
DROP `rotation_z`;

ALTER TABLE `housings`
DROP `rotation_x`,
DROP `rotation_y`,
DROP `rotation_z`;

ALTER TABLE `doodads`
DROP `rotation_x`,
DROP `rotation_y`,
DROP `rotation_z`;
