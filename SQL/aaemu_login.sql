-- ----------------------------------------------------------------------------------
-- Variables 
-- ----------------------------------------------------------------------------------
SET @SQLUpdateVersion = '21.7.24.1'; -- Update this with each update, This is used by Updates Manager for tracking (use YY.MM.DD.<revision> versioning scheme)

CREATE DATABASE  IF NOT EXISTS `aaemu_login` ;
USE `aaemu_login`;
-- ----------------------------------------------------------------------------------------------
-- Make sure to remove the above two lines if you want use your own DB/Schema names during import
-- ----------------------------------------------------------------------------------------------

DROP TABLE IF EXISTS `game_servers`;
CREATE TABLE `game_servers` (
  `id` tinyint unsigned NOT NULL,
  `name` text NOT NULL,
  `host` varchar(128) NOT NULL,
  `port` int NOT NULL,
  `hidden` tinyint(1) NOT NULL DEFAULT '1',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC COMMENT='Server list';


DROP TABLE IF EXISTS `users`;
CREATE TABLE `users` (
  `id` int unsigned NOT NULL AUTO_INCREMENT,
  `username` varchar(32) NOT NULL,
  `password` text COMMENT 'Hashed password of the user',
  `email` varchar(128) NOT NULL,
  `last_login` bigint unsigned NOT NULL DEFAULT '0',
  `last_ip` varchar(128) NOT NULL,
  `created_at` bigint unsigned NOT NULL DEFAULT '0',
  `updated_at` bigint unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  KEY `username` (`username`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC COMMENT='Account login information';

-- Create database for updates tracking via updates manager, insert base installation value
-- DROP DATABASE IF EXISTS `aaemu_updates`;
CREATE DATABASE IF NOT EXISTS `aaemu_updates` /*!40100 DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci */ /*!80016 DEFAULT ENCRYPTION='N' */;
CREATE TABLE IF NOT EXISTS `aaemu_updates`.`login_updates` (
  `ID` int NOT NULL AUTO_INCREMENT,
  `UpdateType` varchar(45) NOT NULL,
  `UpdateID` varchar(45) NOT NULL,
  `InstalledDate` datetime NOT NULL,
  `UpdateDescription` varchar(45) NOT NULL DEFAULT 'No Notes Included',
  PRIMARY KEY (`ID`),
  UNIQUE KEY `ID_UNIQUE` (`ID`),
  UNIQUE KEY `UpdateID_UNIQUE` (`UpdateID`)
) ENGINE=InnoDB AUTO_INCREMENT=8 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
INSERT INTO `aaemu_updates`.`login_updates` (`UpdateType`, `UpdateID`, `InstalledDate`,`UpdateDescription`) VALUES ('SQL', @SQLUpdateVersion, CURRENT_TIMESTAMP(), 'Base installation insertion');
