-- ----------------------------------------------------------------------------------
-- Variables 
-- ----------------------------------------------------------------------------------
SET @SQLUpdateVersion = '21.7.24.1'; -- Update this with each update, This is used by Updates Manager for tracking (use YY.MM.DD.<revision> versioning scheme)
SET @SQLUpdateDescription = 'Base installation insertion';

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

-- Create table for server proprerties
CREATE TABLE IF NOT EXISTS `server_properites` (
  `ID` int unsigned NOT NULL AUTO_INCREMENT,
  `PropertyName` varchar(45) NOT NULL,
  `PropertyValue` varchar(256),
  PRIMARY KEY (`ID`),
  UNIQUE KEY `ID_UNIQUE` (`ID`),
  UNIQUE KEY `PropertyName_UNIQUE` (`PropertyName`)
) ENGINE=InnoDB AUTO_INCREMENT=0 DEFAULT CHARSET=utf8 Comment='Various Server Properties';
-- insert NewInstall Value and set to True (Checked for run of SQL files or not)
REPLACE INTO `server_properites` (`PropertyName`, `PropertyValue`) VALUES ('NewInstall', 'True');

-- Create tables for updates tracking via updates manager
-- DROP TABLE IF EXISTS `updates`;
CREATE TABLE IF NOT EXISTS `server_db_updates` (
  `ID` int NOT NULL AUTO_INCREMENT,
  `UpdateName` varchar(256) NOT NULL,
  `InstalledDate` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `UpdateDescription` varchar(256) NOT NULL DEFAULT 'No Notes Included',
  PRIMARY KEY (`ID`),
  UNIQUE KEY `ID_UNIQUE` (`ID`)
) ENGINE=InnoDB AUTO_INCREMENT=0 DEFAULT CHARSET=utf8 Comment='Installed Server Database Update Tracking';
