CREATE DATABASE  IF NOT EXISTS `aaemu_login` /*!40100 DEFAULT CHARACTER SET latin1 */;
USE `aaemu_login`;
-- ----------------------------------------------------------------------------------------
-- Make sure to remove the above two lines if you want use your own DB names during import
-- ----------------------------------------------------------------------------------------

--
-- Table structure for table `game_servers`
--

DROP TABLE IF EXISTS `game_servers`;
CREATE TABLE `game_servers` (
  `id` tinyint(3) unsigned NOT NULL,
  `name` text NOT NULL,
  `host` varchar(128) NOT NULL,
  `port` int(11) NOT NULL,
  `hidden` tinyint(1) NOT NULL DEFAULT '1',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

--
-- Table structure for table `users`
--

DROP TABLE IF EXISTS `users`;
CREATE TABLE `users` (
  `id` int(11) unsigned NOT NULL AUTO_INCREMENT,
  `username` varchar(32) NOT NULL,
  `password` text NOT NULL,
  `email` varchar(128) NOT NULL,
  `last_login` bigint(20) unsigned NOT NULL DEFAULT '0',
  `last_ip` varchar(128) NOT NULL,
  `created_at` bigint(20) unsigned NOT NULL DEFAULT '0',
  `updated_at` bigint(20) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  KEY `username` (`username`)
) ENGINE=InnoDB AUTO_INCREMENT=5 DEFAULT CHARSET=utf8 ROW_FORMAT=COMPACT;

