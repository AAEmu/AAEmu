-- -------------------------------------------------
-- Add music table
-- -------------------------------------------------
CREATE TABLE `music` (
  `id` int NOT NULL AUTO_INCREMENT,
  `author` int NOT NULL COMMENT 'PlayerId',
  `title` varchar(128) NOT NULL,
  `song` text NOT NULL COMMENT 'Song MML',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='User Created Content (music)';