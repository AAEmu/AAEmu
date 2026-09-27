-- The cash shop checkout that converts wallet cash into AA points.
-- audit_ics_sales records goods lines, which carry a shop_item_id and a sku; an AA-point
-- checkout has neither, so it is journaled in its own ledger instead of being forced into that
-- shape. The row is staged on the same transaction as the debit and the credit, so a failure
-- anywhere leaves no log behind next to a charge that did not happen.
CREATE TABLE IF NOT EXISTS `audit_ics_aa_point_purchases` (
  `id` bigint unsigned NOT NULL AUTO_INCREMENT,
  `account_id` int unsigned NOT NULL,
  `character_id` int unsigned NOT NULL,
  `purchase_date` datetime(6) NOT NULL,
  `cash_spent` bigint NOT NULL,
  `aa_points` bigint NOT NULL,
  `exchange_ratio` int unsigned NOT NULL,
  PRIMARY KEY (`id`),
  KEY `idx_audit_ics_aa_point_purchases_account` (`account_id`, `purchase_date`),
  KEY `idx_audit_ics_aa_point_purchases_character` (`character_id`, `purchase_date`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Cash shop checkouts that convert wallet cash into AA points';
