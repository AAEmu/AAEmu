-- Per-character reopen-box states (the "재개봉 랜박 상자" boxes). The client's requests address
-- the box ITEM instance (u64 itemId on the wire), so state is keyed by character and box item:
-- open counters spent against merchant_reopen_packs free_count/charge_count, the current
-- two-stage weighted draw, the life_time cooldown stamp, and the settled flag that makes each
-- roll's reward claimable exactly once (conditional 0 -> 1, same gate as
-- character_random_shop_offers.sold). Persist-first: the counter spend and the settle are the
-- durable steps, so a World kill can neither lose a reward nor hand one out twice.

CREATE TABLE IF NOT EXISTS `character_reopen_boxes` (
  `character_id` INT UNSIGNED NOT NULL,
  `item_id` BIGINT UNSIGNED NOT NULL COMMENT 'Box item instance id from the wire (u64)',
  `pack_id` INT UNSIGNED NOT NULL COMMENT 'merchant_reopen_packs.id the box draws from',
  `free_used` INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Free opens spent (pack free_count is the max)',
  `charge_used` INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Paid opens spent (pack charge_count is the max)',
  `rolled_at` DATETIME NOT NULL COMMENT 'When the current reward was rolled',
  `refresh_available_at` DATETIME NOT NULL COMMENT 'RolledAt + pack life_time minutes: next allowed roll',
  `opened_at` DATETIME NULL COMMENT 'When the first reward of this box was claimed (wire openDate)',
  `group_id` INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'merchant_reopen_groups.id of the current draw',
  `good_id` INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'merchant_reopen_goods.id of the current draw; 0 = no roll',
  `reward_item_id` INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Item granted by the current draw',
  `reward_grade` TINYINT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Grade of the granted item',
  `reward_count` INT NOT NULL DEFAULT 0 COMMENT 'merchant_reopen_goods.count of the current draw',
  `settled` TINYINT(1) NOT NULL DEFAULT 0 COMMENT 'Reward claimed exactly once: the claim flips 0 -> 1',
  PRIMARY KEY (`character_id`, `item_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Per-character reopen-box item state';
