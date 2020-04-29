/*
Navicat MySQL Data Transfer

Source Server         : archeage
Source Server Version : 80012
Source Host           : localhost:3306
Source Database       : aaemu_game12

Target Server Type    : MYSQL
Target Server Version : 80012
File Encoding         : 65001

Date: 2020-04-30 02:26:04
*/

SET FOREIGN_KEY_CHECKS=0;

-- ----------------------------
-- Table structure for items
-- ----------------------------
DROP TABLE IF EXISTS `items`;
CREATE TABLE `items` (
  `id` bigint(20) unsigned NOT NULL,
  `type` varchar(100) NOT NULL,
  `template_id` int(11) unsigned NOT NULL,
  `slot_type` enum('Equipment','Inventory','Bank') NOT NULL,
  `slot` int(11) NOT NULL,
  `count` int(11) NOT NULL,
  `details` blob,
  `lifespan_mins` int(11) NOT NULL,
  `made_unit_id` int(11) unsigned NOT NULL DEFAULT '0',
  `unsecure_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `unpack_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `owner` int(11) unsigned NOT NULL,
  `grade` tinyint(1) DEFAULT '0',
  `created_at` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `bounded` tinyint(1) DEFAULT '0',
  PRIMARY KEY (`id`) USING BTREE,
  KEY `owner` (`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;
SET FOREIGN_KEY_CHECKS=1;
