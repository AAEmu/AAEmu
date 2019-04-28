/*
Navicat MySQL Data Transfer

Source Server         : aaemu
Source Server Version : 80012
Source Host           : localhost:3306
Source Database       : archeage_world2

Target Server Type    : MYSQL
Target Server Version : 80012
File Encoding         : 65001

Date: 2019-04-27 02:39:32
*/

SET FOREIGN_KEY_CHECKS=0;

-- ----------------------------
-- Table structure for loot_actability_groups
-- ----------------------------
DROP TABLE IF EXISTS `loot_actability_groups`;
CREATE TABLE `loot_actability_groups` (
  `id` int(8) NOT NULL,
  `loot_pack_id` int(8) DEFAULT NULL,
  `loot_group_id` int(8) DEFAULT NULL,
  `max_dice` int(8) DEFAULT NULL,
  `min_dice` int(8) DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- ----------------------------
-- Records of loot_actability_groups
-- ----------------------------
INSERT INTO `loot_actability_groups` VALUES ('7', '6359', '2', '783', '783');
INSERT INTO `loot_actability_groups` VALUES ('8', '6360', '2', '618', '618');
INSERT INTO `loot_actability_groups` VALUES ('9', '6361', '2', '737', '737');
INSERT INTO `loot_actability_groups` VALUES ('10', '6362', '2', '818', '818');
INSERT INTO `loot_actability_groups` VALUES ('11', '6363', '2', '899', '899');
INSERT INTO `loot_actability_groups` VALUES ('13', '6370', '2', '359', '359');
INSERT INTO `loot_actability_groups` VALUES ('14', '6371', '2', '714', '714');
INSERT INTO `loot_actability_groups` VALUES ('15', '6372', '3', '426', '426');
INSERT INTO `loot_actability_groups` VALUES ('16', '6376', '2', '426', '426');
INSERT INTO `loot_actability_groups` VALUES ('17', '6374', '2', '359', '359');
INSERT INTO `loot_actability_groups` VALUES ('18', '6375', '2', '714', '714');
INSERT INTO `loot_actability_groups` VALUES ('19', '6377', '2', '366', '366');
INSERT INTO `loot_actability_groups` VALUES ('20', '6378', '2', '721', '721');
INSERT INTO `loot_actability_groups` VALUES ('21', '6379', '2', '568', '568');
INSERT INTO `loot_actability_groups` VALUES ('26', '6384', '3', '455', '455');
INSERT INTO `loot_actability_groups` VALUES ('27', '6385', '3', '810', '810');
INSERT INTO `loot_actability_groups` VALUES ('28', '6386', '2', '710', '710');
INSERT INTO `loot_actability_groups` VALUES ('29', '6387', '3', '483', '483');
INSERT INTO `loot_actability_groups` VALUES ('30', '6388', '3', '838', '838');
INSERT INTO `loot_actability_groups` VALUES ('31', '6389', '2', '852', '852');
INSERT INTO `loot_actability_groups` VALUES ('32', '6391', '3', '1057', '1057');
INSERT INTO `loot_actability_groups` VALUES ('33', '6390', '3', '702', '702');
INSERT INTO `loot_actability_groups` VALUES ('34', '6393', '3', '702', '702');
INSERT INTO `loot_actability_groups` VALUES ('35', '6394', '3', '1057', '1057');
INSERT INTO `loot_actability_groups` VALUES ('36', '6372', '2', '426', '426');
INSERT INTO `loot_actability_groups` VALUES ('38', '6396', '4', '566', '566');
INSERT INTO `loot_actability_groups` VALUES ('40', '6397', '4', '923', '923');
INSERT INTO `loot_actability_groups` VALUES ('42', '6398', '4', '1009', '1009');
INSERT INTO `loot_actability_groups` VALUES ('44', '6399', '4', '600', '600');
INSERT INTO `loot_actability_groups` VALUES ('46', '6400', '4', '634', '634');
INSERT INTO `loot_actability_groups` VALUES ('48', '6401', '4', '634', '634');
INSERT INTO `loot_actability_groups` VALUES ('50', '6402', '4', '668', '668');
INSERT INTO `loot_actability_groups` VALUES ('52', '6403', '4', '668', '668');
INSERT INTO `loot_actability_groups` VALUES ('54', '6404', '4', '702', '702');
INSERT INTO `loot_actability_groups` VALUES ('55', '6405', '4', '702', '702');
INSERT INTO `loot_actability_groups` VALUES ('57', '6406', '4', '1349', '1349');
INSERT INTO `loot_actability_groups` VALUES ('59', '6407', '4', '736', '736');
INSERT INTO `loot_actability_groups` VALUES ('61', '6408', '4', '1134', '1134');
INSERT INTO `loot_actability_groups` VALUES ('63', '6410', '4', '668', '668');
INSERT INTO `loot_actability_groups` VALUES ('65', '6411', '4', '948', '948');
INSERT INTO `loot_actability_groups` VALUES ('67', '6413', '4', '1264', '1264');
INSERT INTO `loot_actability_groups` VALUES ('69', '6412', '4', '1264', '1264');
INSERT INTO `loot_actability_groups` VALUES ('71', '6415', '4', '1349', '1349');
INSERT INTO `loot_actability_groups` VALUES ('73', '6416', '4', '1349', '1349');
INSERT INTO `loot_actability_groups` VALUES ('75', '6417', '4', '1134', '1134');
INSERT INTO `loot_actability_groups` VALUES ('77', '6418', '4', '1134', '1134');
INSERT INTO `loot_actability_groups` VALUES ('79', '6414', '4', '1520', '1520');
INSERT INTO `loot_actability_groups` VALUES ('80', '6425', '2', '384', '384');
INSERT INTO `loot_actability_groups` VALUES ('81', '6426', '2', '384', '384');
INSERT INTO `loot_actability_groups` VALUES ('82', '6427', '2', '463', '463');
INSERT INTO `loot_actability_groups` VALUES ('83', '6428', '2', '497', '497');
INSERT INTO `loot_actability_groups` VALUES ('84', '6429', '2', '497', '497');
INSERT INTO `loot_actability_groups` VALUES ('85', '6430', '2', '600', '600');
INSERT INTO `loot_actability_groups` VALUES ('86', '6431', '2', '426', '426');
INSERT INTO `loot_actability_groups` VALUES ('87', '6432', '2', '824', '824');
INSERT INTO `loot_actability_groups` VALUES ('88', '6433', '2', '923', '923');
INSERT INTO `loot_actability_groups` VALUES ('89', '6434', '2', '923', '923');
INSERT INTO `loot_actability_groups` VALUES ('90', '6435', '2', '1094', '1094');
INSERT INTO `loot_actability_groups` VALUES ('91', '6436', '2', '887', '887');
INSERT INTO `loot_actability_groups` VALUES ('92', '6443', '2', '441', '441');
INSERT INTO `loot_actability_groups` VALUES ('93', '6438', '2', '441', '441');
INSERT INTO `loot_actability_groups` VALUES ('94', '6441', '2', '762', '762');
INSERT INTO `loot_actability_groups` VALUES ('95', '6439', '2', '566', '566');
INSERT INTO `loot_actability_groups` VALUES ('96', '6437', '2', '887', '887');
INSERT INTO `loot_actability_groups` VALUES ('97', '6440', '2', '887', '887');
INSERT INTO `loot_actability_groups` VALUES ('98', '6442', '2', '1264', '1264');
INSERT INTO `loot_actability_groups` VALUES ('99', '6444', '2', '702', '702');
INSERT INTO `loot_actability_groups` VALUES ('100', '6445', '2', '1264', '1264');
INSERT INTO `loot_actability_groups` VALUES ('101', '6446', '2', '702', '702');
INSERT INTO `loot_actability_groups` VALUES ('102', '6452', '2', '345', '345');
INSERT INTO `loot_actability_groups` VALUES ('103', '6452', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('104', '6453', '2', '345', '345');
INSERT INTO `loot_actability_groups` VALUES ('105', '6453', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('106', '6454', '2', '352', '352');
INSERT INTO `loot_actability_groups` VALUES ('107', '6454', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('108', '6455', '2', '352', '352');
INSERT INTO `loot_actability_groups` VALUES ('109', '6455', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('110', '6456', '2', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('111', '6456', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('112', '6457', '2', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('113', '6457', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('114', '6458', '2', '363', '363');
INSERT INTO `loot_actability_groups` VALUES ('115', '6458', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('116', '6459', '2', '363', '363');
INSERT INTO `loot_actability_groups` VALUES ('117', '6459', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('118', '6460', '2', '363', '363');
INSERT INTO `loot_actability_groups` VALUES ('119', '6460', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('120', '6461', '2', '370', '370');
INSERT INTO `loot_actability_groups` VALUES ('121', '6461', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('122', '6462', '2', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('123', '6462', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('124', '6463', '2', '348', '348');
INSERT INTO `loot_actability_groups` VALUES ('125', '6463', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('126', '6464', '2', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('127', '6464', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('128', '6465', '2', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('129', '6465', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('130', '6466', '2', '359', '359');
INSERT INTO `loot_actability_groups` VALUES ('131', '6466', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('132', '6467', '2', '359', '359');
INSERT INTO `loot_actability_groups` VALUES ('133', '6467', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('134', '6468', '2', '366', '366');
INSERT INTO `loot_actability_groups` VALUES ('135', '6468', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('136', '6469', '2', '366', '366');
INSERT INTO `loot_actability_groups` VALUES ('137', '6469', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('138', '6470', '2', '366', '366');
INSERT INTO `loot_actability_groups` VALUES ('139', '6470', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('140', '6471', '2', '373', '373');
INSERT INTO `loot_actability_groups` VALUES ('141', '6471', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('142', '6472', '2', '352', '352');
INSERT INTO `loot_actability_groups` VALUES ('143', '6472', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('144', '6473', '2', '352', '352');
INSERT INTO `loot_actability_groups` VALUES ('145', '6473', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('146', '6474', '2', '359', '359');
INSERT INTO `loot_actability_groups` VALUES ('147', '6474', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('148', '6475', '2', '359', '359');
INSERT INTO `loot_actability_groups` VALUES ('149', '6475', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('150', '6476', '2', '363', '363');
INSERT INTO `loot_actability_groups` VALUES ('151', '6476', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('152', '6477', '2', '363', '363');
INSERT INTO `loot_actability_groups` VALUES ('153', '6477', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('154', '6478', '2', '370', '370');
INSERT INTO `loot_actability_groups` VALUES ('155', '6478', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('156', '6479', '2', '370', '370');
INSERT INTO `loot_actability_groups` VALUES ('157', '6479', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('158', '6480', '2', '377', '377');
INSERT INTO `loot_actability_groups` VALUES ('159', '6480', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('160', '6481', '2', '377', '377');
INSERT INTO `loot_actability_groups` VALUES ('161', '6481', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('162', '6482', '2', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('164', '6483', '2', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('166', '6484', '2', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('168', '6485', '2', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('170', '6486', '2', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('172', '6487', '2', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('174', '6488', '2', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('176', '6489', '2', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('178', '6490', '2', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('180', '6491', '2', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('182', '6492', '2', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('183', '6492', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('184', '6493', '2', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('185', '6493', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('186', '6494', '2', '363', '363');
INSERT INTO `loot_actability_groups` VALUES ('187', '6494', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('188', '6495', '2', '363', '363');
INSERT INTO `loot_actability_groups` VALUES ('189', '6495', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('190', '6496', '2', '366', '366');
INSERT INTO `loot_actability_groups` VALUES ('191', '6496', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('192', '6497', '2', '366', '366');
INSERT INTO `loot_actability_groups` VALUES ('193', '6497', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('194', '6498', '2', '373', '373');
INSERT INTO `loot_actability_groups` VALUES ('195', '6498', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('196', '6499', '2', '373', '373');
INSERT INTO `loot_actability_groups` VALUES ('197', '6499', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('198', '6500', '2', '380', '380');
INSERT INTO `loot_actability_groups` VALUES ('199', '6500', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('200', '6501', '2', '380', '380');
INSERT INTO `loot_actability_groups` VALUES ('201', '6501', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('202', '6502', '2', '359', '359');
INSERT INTO `loot_actability_groups` VALUES ('203', '6502', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('204', '6503', '2', '366', '366');
INSERT INTO `loot_actability_groups` VALUES ('205', '6503', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('206', '6504', '2', '373', '373');
INSERT INTO `loot_actability_groups` VALUES ('207', '6504', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('208', '6505', '2', '380', '380');
INSERT INTO `loot_actability_groups` VALUES ('209', '6505', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('210', '6506', '2', '384', '384');
INSERT INTO `loot_actability_groups` VALUES ('211', '6506', '3', '355', '355');
INSERT INTO `loot_actability_groups` VALUES ('212', '6517', '2', '426', '426');
INSERT INTO `loot_actability_groups` VALUES ('213', '6518', '2', '426', '426');
INSERT INTO `loot_actability_groups` VALUES ('214', '6519', '2', '566', '566');
INSERT INTO `loot_actability_groups` VALUES ('215', '6520', '2', '634', '634');
INSERT INTO `loot_actability_groups` VALUES ('216', '6521', '2', '634', '634');
INSERT INTO `loot_actability_groups` VALUES ('217', '6522', '2', '668', '668');
INSERT INTO `loot_actability_groups` VALUES ('218', '6523', '2', '441', '441');
INSERT INTO `loot_actability_groups` VALUES ('219', '6524', '2', '949', '949');
INSERT INTO `loot_actability_groups` VALUES ('220', '6525', '2', '1264', '1264');
INSERT INTO `loot_actability_groups` VALUES ('221', '6526', '2', '1264', '1264');
INSERT INTO `loot_actability_groups` VALUES ('222', '6527', '2', '1349', '1349');
INSERT INTO `loot_actability_groups` VALUES ('223', '6528', '2', '1073', '1073');
INSERT INTO `loot_actability_groups` VALUES ('224', '6529', '2', '469', '469');
INSERT INTO `loot_actability_groups` VALUES ('225', '6530', '2', '469', '469');
INSERT INTO `loot_actability_groups` VALUES ('226', '6531', '2', '1011', '1011');
INSERT INTO `loot_actability_groups` VALUES ('227', '6532', '2', '702', '702');
INSERT INTO `loot_actability_groups` VALUES ('228', '6533', '2', '1073', '1073');
INSERT INTO `loot_actability_groups` VALUES ('229', '6534', '2', '1073', '1073');
INSERT INTO `loot_actability_groups` VALUES ('230', '6535', '2', '1435', '1435');
INSERT INTO `loot_actability_groups` VALUES ('231', '6536', '2', '770', '770');
INSERT INTO `loot_actability_groups` VALUES ('232', '6537', '2', '1520', '1520');
INSERT INTO `loot_actability_groups` VALUES ('233', '6538', '2', '804', '804');
INSERT INTO `loot_actability_groups` VALUES ('234', '6365', '3', '120', '120');
INSERT INTO `loot_actability_groups` VALUES ('235', '6366', '3', '120', '120');
INSERT INTO `loot_actability_groups` VALUES ('236', '6367', '3', '120', '120');
INSERT INTO `loot_actability_groups` VALUES ('237', '6368', '3', '120', '120');
INSERT INTO `loot_actability_groups` VALUES ('238', '6369', '3', '120', '120');
INSERT INTO `loot_actability_groups` VALUES ('239', '6364', '3', '120', '120');
INSERT INTO `loot_actability_groups` VALUES ('240', '7372', '2', '710', '710');
INSERT INTO `loot_actability_groups` VALUES ('241', '7477', '3', '634', '634');
INSERT INTO `loot_actability_groups` VALUES ('242', '7478', '3', '634', '634');
INSERT INTO `loot_actability_groups` VALUES ('268', '7529', '3', '702', '702');
INSERT INTO `loot_actability_groups` VALUES ('269', '7530', '3', '1057', '1057');
INSERT INTO `loot_actability_groups` VALUES ('270', '7531', '2', '852', '852');
INSERT INTO `loot_actability_groups` VALUES ('276', '7680', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('277', '7681', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('278', '7682', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('279', '7683', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('280', '7684', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('281', '7686', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('282', '7688', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('283', '7689', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('284', '7690', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('285', '7691', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('286', '7692', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('287', '7693', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('288', '7694', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('289', '7695', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('290', '7696', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('291', '7697', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('292', '7698', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('293', '7699', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('294', '7700', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('295', '7701', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('296', '7702', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('297', '7703', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('298', '7704', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('299', '7705', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('300', '7706', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('301', '7707', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('302', '7708', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('303', '7709', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('304', '7710', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('305', '7711', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('306', '7712', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('307', '7713', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('308', '7714', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('309', '7715', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('310', '7716', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('311', '7717', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('312', '7718', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('313', '7719', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('314', '7720', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('315', '7721', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('316', '7722', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('317', '7723', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('318', '7724', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('319', '7725', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('320', '7726', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('321', '7727', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('322', '7728', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('323', '7819', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('324', '7785', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('325', '7685', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('326', '7935', '2', '100', '20');
INSERT INTO `loot_actability_groups` VALUES ('327', '7943', '2', '852', '852');
INSERT INTO `loot_actability_groups` VALUES ('328', '7944', '3', '952', '952');
INSERT INTO `loot_actability_groups` VALUES ('329', '8013', '3', '1052', '1052');
INSERT INTO `loot_actability_groups` VALUES ('330', '6392', '2', '852', '852');
INSERT INTO `loot_actability_groups` VALUES ('331', '8079', '3', '702', '702');
INSERT INTO `loot_actability_groups` VALUES ('332', '8080', '3', '1057', '1057');
INSERT INTO `loot_actability_groups` VALUES ('333', '8081', '2', '852', '852');
INSERT INTO `loot_actability_groups` VALUES ('352', '8417', '2', '5000', '5000');
INSERT INTO `loot_actability_groups` VALUES ('354', '9462', '3', '150', '150');
SET FOREIGN_KEY_CHECKS=1;
