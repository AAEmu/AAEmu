DROP TABLE IF EXISTS `uccs`;
CREATE TABLE `uccs` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `uploader_id` int(11) NOT NULL,
  `type` tinyint(3) NOT NULL,
  `data` blob,
  `pattern1` int(11) unsigned NOT NULL,
  `pattern2` int(11) unsigned NOT NULL,
  `color1R` int(11) unsigned NOT NULL,
  `color1G` int(11) unsigned NOT NULL,
  `color1B` int(11) unsigned NOT NULL,
  `color2R` int(11) unsigned NOT NULL,
  `color2G` int(11) unsigned NOT NULL,
  `color2B` int(11) unsigned NOT NULL,
  `color3R` int(11) unsigned NOT NULL,
  `color3G` int(11) unsigned NOT NULL,
  `color3B` int(11) unsigned NOT NULL,
  `modified` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=3 DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;
