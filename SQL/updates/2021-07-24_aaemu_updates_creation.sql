-- ----------------------------------------------------------------------------------
-- Variables 
-- ----------------------------------------------------------------------------------
SET @SQLUpdateVersion = '21.7.24.1'; -- Update this with each update, This is used by Updates Manager for tracking (use YY.MM.DD.<revision> versioning scheme)
SET @SQLUpdateDescription = 'Base installation insertion';

-- ------------------------------------------------------------------------------------------------
-- Updates Manager DB Creation
-- Adds database for use by Updates Manager feature for tracking installed updates to SQL and Code
-- ------------------------------------------------------------------------------------------------

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
INSERT INTO `aaemu_updates`.`login_updates` (`UpdateType`, `UpdateID`, `InstalledDate`,`UpdateDescription`) VALUES ('SQL', @SQLUpdateVersion, CURRENT_TIMESTAMP(), @SQLUpdateDescription);

-- Create database for updates tracking via updates manager, insert base installation value
-- DROP DATABASE IF EXISTS `aaemu_updates`;
CREATE DATABASE IF NOT EXISTS `aaemu_updates` /*!40100 DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci */ /*!80016 DEFAULT ENCRYPTION='N' */;
CREATE TABLE IF NOT EXISTS `aaemu_updates`.`game_updates` (
  `ID` int NOT NULL AUTO_INCREMENT,
  `UpdateType` varchar(45) NOT NULL,
  `UpdateID` varchar(45) NOT NULL,
  `InstalledDate` datetime NOT NULL,
  `UpdateDescription` varchar(45) NOT NULL DEFAULT 'No Notes Included',
  PRIMARY KEY (`ID`),
  UNIQUE KEY `ID_UNIQUE` (`ID`),
  UNIQUE KEY `UpdateID_UNIQUE` (`UpdateID`)
) ENGINE=InnoDB AUTO_INCREMENT=8 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
INSERT INTO `aaemu_updates`.`game_updates` (`UpdateType`, `UpdateID`, `InstalledDate`,`UpdateDescription`) VALUES ('SQL', @SQLUpdateVersion, CURRENT_TIMESTAMP(), @SQLUpdateDescription);
