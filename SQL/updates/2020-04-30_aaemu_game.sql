DROP TABLE IF EXISTS `mails`;
CREATE TABLE `mails` (
  `id` int(11) NOT NULL,
  `type` int(11) NOT NULL,
  `status` int(11) NOT NULL,
  `title` varchar(45) NOT NULL,
  `text` varchar(150) NOT NULL,
  `sender_name` varchar(45) NOT NULL,
  `attachments` int(11) NOT NULL,
  `receiver_name` varchar(45) NOT NULL,
  `open_date` datetime NOT NULL,
  `send_date` datetime NOT NULL,
  `received_date` datetime NOT NULL,
  `returned` int(11) NOT NULL,
  `extra` int(11) NOT NULL,
  `money_amount_1` int(11) NOT NULL,
  `money_amount_2` int(11) NOT NULL,
  `money_amount_3` int(11) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;

DROP TABLE IF EXISTS `mails_items`;
CREATE TABLE `mails_items` (
  `ID` int(11) NOT NULL AUTO_INCREMENT,
  `item0` int(11) DEFAULT NULL,
  `item1` int(11) DEFAULT NULL,
  `item2` int(11) DEFAULT NULL,
  `item3` int(11) DEFAULT NULL,
  `item4` int(11) DEFAULT NULL,
  `item5` int(11) DEFAULT NULL,
  `item6` int(11) DEFAULT NULL,
  `item7` int(11) DEFAULT NULL,
  `item8` int(11) DEFAULT NULL,
  `item9` int(11) DEFAULT NULL,
  PRIMARY KEY (`ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_general_ci;