-- Durable favorite entries for the character portal book. sort_order preserves the
-- server-side order in which entries were added; relog reapplies those membership
-- flags without changing the portal book's own row order.

CREATE TABLE IF NOT EXISTS `character_favorite_portals` (
  `owner` INT UNSIGNED NOT NULL,
  `portal_type` TINYINT UNSIGNED NOT NULL,
  `portal_id` INT UNSIGNED NOT NULL,
  `sort_order` INT UNSIGNED NOT NULL,
  PRIMARY KEY (`owner`, `portal_type`, `portal_id`),
  KEY `ix_character_favorite_portals_order` (`owner`, `sort_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Ordered favorite portal-book entries per character';
