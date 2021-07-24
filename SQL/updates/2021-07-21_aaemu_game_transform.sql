-- ----------------------------------------------------------------------------------
-- Variables 
-- ----------------------------------------------------------------------------------
SET @SQLUpdateVersion = '21.7.24.1'; -- Update this with each update, This is used by Updates Manager for tracking (use YY.MM.DD.<revision> versioning scheme)
SET @SQLUpdateDescription = '2021-07-21_aaemu_game_transform';
SET @GameServerSelection = 'game'; -- Values: Game, Login (Not used yet, but want to make the Insert statement dynamic eventually based on a concat with this for table selection)
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

-- Updates Manager Tracking (Make sure game_updates or login_updates is correct below depending on what you are updating)
INSERT INTO `aaemu_updates`.`game_updates` (`UpdateType`, `UpdateID`, `InstalledDate`,`UpdateDescription`) VALUES ('SQL', @SQLUpdateVersion, CURRENT_TIMESTAMP(), @SQLUpdateDescription);

