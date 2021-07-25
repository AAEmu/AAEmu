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
REPLACE INTO `server_properites` (`PropertyName`, `PropertyValue`) VALUES ('NewInstall', 'False');

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