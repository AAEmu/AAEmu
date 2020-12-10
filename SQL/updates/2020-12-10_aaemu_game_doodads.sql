DROP TABLE IF EXISTS `aaemu_game`.`doodads`;
CREATE TABLE `aaemu_game`.`doodads` (
  `id` int NOT NULL AUTO_INCREMENT,
  `owner_id` int,
  `owner_type` tinyint(4) unsigned DEFAULT 255,
  `template_id` int NOT NULL,
  `current_phase_id` int NOT NULL,
  `plant_time` datetime NOT NULL,
  `growth_time` datetime NOT NULL,
  `phase_time` datetime NOT NULL,
  `x` float NOT NULL,
  `y` float NOT NULL,
  `z` float NOT NULL,
  `rotation_x` tinyint(4) NOT NULL,
  `rotation_y` tinyint(4) NOT NULL,
  `rotation_z` tinyint(4) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=3 DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;
