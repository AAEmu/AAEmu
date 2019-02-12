-- MySQL Workbench Forward Engineering

SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0;
SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0;
SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='ONLY_FULL_GROUP_BY,STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION';

-- -----------------------------------------------------
-- Schema mydb
-- -----------------------------------------------------
-- -----------------------------------------------------
-- Schema aaemu_game
-- -----------------------------------------------------

-- -----------------------------------------------------
-- Schema aaemu_game
-- -----------------------------------------------------
CREATE SCHEMA IF NOT EXISTS `aaemu_game` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci ;
USE `aaemu_game` ;

-- -----------------------------------------------------
-- Table `aaemu_game`.`abilities`
-- -----------------------------------------------------
CREATE TABLE IF NOT EXISTS `aaemu_game`.`abilities` (
  `id` TINYINT(3) UNSIGNED NOT NULL,
  `exp` INT(11) NOT NULL,
  `owner` INT(11) UNSIGNED NOT NULL,
  PRIMARY KEY (`id`, `owner`))
ENGINE = InnoDB
DEFAULT CHARACTER SET = utf8;


-- -----------------------------------------------------
-- Table `aaemu_game`.`actabilities`
-- -----------------------------------------------------
CREATE TABLE IF NOT EXISTS `aaemu_game`.`actabilities` (
  `id` INT(10) UNSIGNED NOT NULL,
  `point` INT(10) UNSIGNED NOT NULL DEFAULT '0',
  `step` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0',
  `owner` INT(10) UNSIGNED NOT NULL,
  PRIMARY KEY (`owner`, `id`))
ENGINE = InnoDB
DEFAULT CHARACTER SET = utf8;


-- -----------------------------------------------------
-- Table `aaemu_game`.`appellations`
-- -----------------------------------------------------
CREATE TABLE IF NOT EXISTS `aaemu_game`.`appellations` (
  `id` INT(10) UNSIGNED NOT NULL,
  `active` TINYINT(1) NOT NULL DEFAULT '0',
  `owner` INT(10) UNSIGNED NOT NULL,
  PRIMARY KEY (`id`, `owner`))
ENGINE = InnoDB
DEFAULT CHARACTER SET = utf8;


-- -----------------------------------------------------
-- Table `aaemu_game`.`characters`
-- -----------------------------------------------------
CREATE TABLE IF NOT EXISTS `aaemu_game`.`characters` (
  `id` INT(11) UNSIGNED NOT NULL,
  `account_id` INT(11) UNSIGNED NOT NULL,
  `name` VARCHAR(128) NOT NULL,
  `race` TINYINT(2) NOT NULL,
  `gender` TINYINT(1) NOT NULL,
  `unit_model_params` BLOB NOT NULL,
  `level` TINYINT(4) NOT NULL,
  `expirience` INT(11) NOT NULL,
  `recoverable_exp` INT(11) NOT NULL,
  `hp` INT(11) NOT NULL,
  `mp` INT(11) NOT NULL,
  `labor_power` MEDIUMINT(9) NOT NULL,
  `labor_power_modified` DATETIME NOT NULL DEFAULT '0001-01-01 00:00:00',
  `consumed_lp` INT(11) NOT NULL,
  `ability1` TINYINT(4) NOT NULL,
  `ability2` TINYINT(4) NOT NULL,
  `ability3` TINYINT(4) NOT NULL,
  `zone_id` INT(11) UNSIGNED NOT NULL,
  `x` FLOAT NOT NULL,
  `y` FLOAT NOT NULL,
  `z` FLOAT NOT NULL,
  `rotation_x` TINYINT(4) NOT NULL,
  `rotation_y` TINYINT(4) NOT NULL,
  `rotation_z` TINYINT(4) NOT NULL,
  `faction_id` INT(11) UNSIGNED NOT NULL,
  `faction_name` VARCHAR(128) NOT NULL,
  `family` INT(11) UNSIGNED NOT NULL,
  `dead_count` MEDIUMINT(8) UNSIGNED NOT NULL,
  `dead_time` DATETIME NOT NULL DEFAULT '0001-01-01 00:00:00',
  `rez_wait_duration` INT(11) NOT NULL,
  `rez_time` DATETIME NOT NULL DEFAULT '0001-01-01 00:00:00',
  `rez_penalty_duration` INT(11) NOT NULL,
  `leave_time` DATETIME NOT NULL DEFAULT '0001-01-01 00:00:00',
  `money` BIGINT(20) NOT NULL,
  `money2` BIGINT(20) NOT NULL,
  `honor_point` INT(11) NOT NULL,
  `vocation_point` INT(11) NOT NULL,
  `crime_point` INT(11) NOT NULL,
  `crime_record` INT(11) NOT NULL,
  `delete_request_time` DATETIME NOT NULL DEFAULT '0001-01-01 00:00:00',
  `transfer_request_time` DATETIME NOT NULL DEFAULT '0001-01-01 00:00:00',
  `delete_time` DATETIME NOT NULL DEFAULT '0001-01-01 00:00:00',
  `bm_point` INT(11) NOT NULL,
  `auto_use_aapoint` TINYINT(1) NOT NULL,
  `prev_point` INT(11) NOT NULL,
  `point` INT(11) NOT NULL,
  `gift` INT(11) NOT NULL,
  `num_inv_slot` TINYINT(3) UNSIGNED NOT NULL DEFAULT '50',
  `num_bank_slot` SMALLINT(5) UNSIGNED NOT NULL DEFAULT '50',
  `expanded_expert` TINYINT(4) NOT NULL,
  `slots` BLOB NOT NULL,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME NOT NULL DEFAULT '0001-01-01 00:00:00',
  PRIMARY KEY (`id`, `account_id`))
ENGINE = InnoDB
DEFAULT CHARACTER SET = utf8;


-- -----------------------------------------------------
-- Table `aaemu_game`.`family_members`
-- -----------------------------------------------------
CREATE TABLE IF NOT EXISTS `aaemu_game`.`family_members` (
  `character_id` INT(11) NOT NULL,
  `family_id` INT(11) NOT NULL,
  `name` VARCHAR(45) NOT NULL,
  `role` TINYINT(1) NOT NULL DEFAULT '0',
  `title` VARCHAR(45) NULL DEFAULT NULL,
  PRIMARY KEY (`family_id`, `character_id`))
ENGINE = InnoDB
DEFAULT CHARACTER SET = utf8;


-- -----------------------------------------------------
-- Table `aaemu_game`.`items`
-- -----------------------------------------------------
CREATE TABLE IF NOT EXISTS `aaemu_game`.`items` (
  `id` BIGINT(20) UNSIGNED NOT NULL,
  `type` VARCHAR(100) NOT NULL,
  `template_id` INT(11) UNSIGNED NOT NULL,
  `slot_type` ENUM('Equipment', 'Inventory', 'Bank') NOT NULL,
  `slot` INT(11) NOT NULL,
  `count` INT(11) NOT NULL,
  `details` BLOB NULL DEFAULT NULL,
  `lifespan_mins` INT(11) NOT NULL,
  `unsecure_time` DATETIME NOT NULL DEFAULT '0001-01-01 00:00:00',
  `unpack_time` DATETIME NOT NULL DEFAULT '0001-01-01 00:00:00',
  `owner` INT(11) UNSIGNED NOT NULL,
  `grade` TINYINT(1) NULL DEFAULT '0',
  `created_at` DATETIME NOT NULL DEFAULT '0001-01-01 00:00:00',
  PRIMARY KEY (`id`),
  INDEX `owner` (`owner` ASC) VISIBLE)
ENGINE = InnoDB
DEFAULT CHARACTER SET = utf8;


-- -----------------------------------------------------
-- Table `aaemu_game`.`options`
-- -----------------------------------------------------
CREATE TABLE IF NOT EXISTS `aaemu_game`.`options` (
  `key` VARCHAR(100) NOT NULL,
  `value` TEXT NOT NULL,
  `owner` INT(11) UNSIGNED NOT NULL,
  PRIMARY KEY (`key`, `owner`))
ENGINE = InnoDB
DEFAULT CHARACTER SET = utf8;


-- -----------------------------------------------------
-- Table `aaemu_game`.`portal_book_coords`
-- -----------------------------------------------------
CREATE TABLE IF NOT EXISTS `aaemu_game`.`portal_book_coords` (
  `id` INT(11) NOT NULL,
  `name` VARCHAR(128) NOT NULL,
  `x` INT(11) NULL DEFAULT '0',
  `y` INT(11) NULL DEFAULT '0',
  `z` INT(11) NULL DEFAULT '0',
  `zone_id` INT(11) NULL DEFAULT '0',
  `z_rot` INT(11) NULL DEFAULT '0',
  `sub_zone_id` INT(11) NULL DEFAULT '0',
  `owner` INT(11) NOT NULL,
  PRIMARY KEY (`id`, `owner`))
ENGINE = InnoDB
DEFAULT CHARACTER SET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;


-- -----------------------------------------------------
-- Table `aaemu_game`.`portal_visited_district`
-- -----------------------------------------------------
CREATE TABLE IF NOT EXISTS `aaemu_game`.`portal_visited_district` (
  `id` INT(11) NOT NULL,
  `subzone` INT(11) NOT NULL,
  `owner` INT(11) NOT NULL,
  PRIMARY KEY (`id`, `subzone`, `owner`))
ENGINE = InnoDB
DEFAULT CHARACTER SET = utf8mb4
COLLATE = utf8mb4_0900_ai_ci;


-- -----------------------------------------------------
-- Table `aaemu_game`.`skills`
-- -----------------------------------------------------
CREATE TABLE IF NOT EXISTS `aaemu_game`.`skills` (
  `id` INT(11) UNSIGNED NOT NULL,
  `level` TINYINT(4) NOT NULL,
  `type` ENUM('Skill', 'Buff') NOT NULL,
  `owner` INT(11) UNSIGNED NOT NULL,
  PRIMARY KEY (`id`, `owner`))
ENGINE = InnoDB
DEFAULT CHARACTER SET = utf8;


SET SQL_MODE=@OLD_SQL_MODE;
SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS;
SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS;
