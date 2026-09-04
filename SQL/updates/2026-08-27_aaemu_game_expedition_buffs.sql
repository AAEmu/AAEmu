USE aaemu_game;

-- Guild prestige-shop buffs: which grade of each expedition_buffs/expedition_buff_grades row (game data,
-- shipped in compact.sqlite3) a guild has purchased. 0/no row = not purchased at all. Paid for the same
-- way the existing Guild Contribution Shop (CSBuyItemsPacket, MerchantPackKind.ItemPoint) already spends
-- Contribution Points - straight from the PURCHASING CHARACTER's own contribution_point balance via
-- ExpeditionManager.TryChangeContributionPoints, not a separate pooled/guild-bank currency - but the
-- unlocked grade itself applies guild-wide, hence tracked per-expedition here rather than per-character.
CREATE TABLE IF NOT EXISTS `expedition_buff_purchases` (
  `expedition_id` int unsigned NOT NULL,
  `expedition_buff_id` int unsigned NOT NULL COMMENT 'expedition_buffs.id (game data)',
  `grade` tinyint unsigned NOT NULL DEFAULT '0' COMMENT 'highest purchased expedition_buff_grades.grade for this buff',
  PRIMARY KEY (`expedition_id`, `expedition_buff_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Guild-level prestige-shop buff purchases';
