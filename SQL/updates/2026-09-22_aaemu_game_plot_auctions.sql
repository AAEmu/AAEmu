USE aaemu_game;

-- GF-E12 plot auctions. The state machine (which auction is settled, its price base) and the
-- per-character escrow rows (one held bid amount each) are live server state; the content
-- (prices, windows, winner count, rewards) stays in compact.sqlite3's plot_auction_config.

CREATE TABLE IF NOT EXISTS `plot_auctions` (
  `id` INT UNSIGNED NOT NULL COMMENT 'plot_auction_config.id',
  `activity_id` INT UNSIGNED NOT NULL COMMENT 'game_activities.id the auction belongs to',
  `settled` TINYINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '1 once settlement consumed every escrow row',
  `base_price` BIGINT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Standing leading bid the floor is computed from; 0 before the first bid',
  `updated_unix` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Plot (housing land) auction state machine rows';

CREATE TABLE IF NOT EXISTS `character_plot_auction_bids` (
  `auction_id` INT UNSIGNED NOT NULL COMMENT 'plot_auction_config.id',
  `character_id` INT UNSIGNED NOT NULL COMMENT 'Bidding character',
  `bid_amount` BIGINT UNSIGNED NOT NULL COMMENT 'Escrowed copper held for this standing bid',
  `bid_time_unix` BIGINT NOT NULL COMMENT 'When the standing bid was placed (tie-break)',
  PRIMARY KEY (`auction_id`,`character_id`),
  KEY `idx_character_plot_auction_bids_character` (`character_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Plot auction bid escrow: exactly one held amount per character per auction';
