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
