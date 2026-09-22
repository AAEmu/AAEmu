USE aaemu_game;

-- Collection/encyclopedia discovery. The shipped content decides what an entry is (the collection
-- achievement kind, the item watch records and the item-guide tables); what a character has discovered
-- is the only thing stored here. One row per character per discovered entry: the first-seen set that
-- makes a replayed discovery a no-op, and the ledger the world-entry replay reads from.

CREATE TABLE IF NOT EXISTS `character_collections` (
  `owner` int unsigned NOT NULL,
  `item_type_id` int unsigned NOT NULL,
  PRIMARY KEY (`owner`, `item_type_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Discovered collection/encyclopedia entries per character';
