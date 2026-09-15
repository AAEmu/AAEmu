-- Composition length steps the score window shows, mirroring the client data table of the same
-- name. The World enforces the top step when a player saves notes, so the value is read from data
-- rather than hardcoded in the manager.
CREATE TABLE IF NOT EXISTS `music_note_limits` (
  `id` int NOT NULL AUTO_INCREMENT,
  `step` int NOT NULL DEFAULT '0',
  `note_length` int NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  UNIQUE KEY `step` (`step`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Composition length per actability step';

INSERT INTO `music_note_limits` (`id`, `step`, `note_length`) VALUES
  (1, 0, 200),
  (2, 1, 400),
  (3, 2, 600),
  (4, 3, 800),
  (5, 4, 1000),
  (6, 5, 1200),
  (7, 6, 1400),
  (8, 7, 1600),
  (9, 8, 1800),
  (10, 9, 2000),
  (11, 10, 3000),
  (12, 11, 5000)
ON DUPLICATE KEY UPDATE `note_length` = VALUES(`note_length`);
