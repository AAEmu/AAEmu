USE aaemu_game;

CREATE TABLE IF NOT EXISTS `specialty_market_stock_event_checks` (
  `trigger_id` int unsigned NOT NULL,
  `next_check` bigint NOT NULL,
  PRIMARY KEY (`trigger_id`),
  CHECK (`trigger_id` > 0 AND `next_check` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

CREATE TABLE IF NOT EXISTS `specialty_market_stock_events` (
  `event_id` int unsigned NOT NULL,
  `started_at` bigint NOT NULL,
  `expires_at` bigint NOT NULL,
  PRIMARY KEY (`event_id`),
  CHECK (`event_id` > 0),
  CHECK (`started_at` >= 0 AND `expires_at` > `started_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;
