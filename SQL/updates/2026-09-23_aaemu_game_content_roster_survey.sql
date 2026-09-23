-- GF-S18: content roster removal + survey-form reply state.
--
-- account_content_rosters: the account's content roster rows. CSContentRosterDelete (0x201)
-- carries the roster ids; ownership is validated against account_id before anything is removed.
-- account_survey_form_replies: survey_forms text pins a survey to one reply per account
-- ("one reply per account" in the shipped reward copy), so (account_id, survey_form_id) is the
-- primary key and doubles as the exactly-once guard.

CREATE TABLE IF NOT EXISTS `account_content_rosters` (
  `id` bigint unsigned NOT NULL AUTO_INCREMENT,
  `account_id` int unsigned NOT NULL,
  `save_title` varchar(255) NOT NULL DEFAULT '',
  `created_at` int unsigned NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`) USING BTREE,
  KEY `idx_account_content_rosters_account` (`account_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Account-scoped content roster rows (content roster save/delete)';

CREATE TABLE IF NOT EXISTS `account_content_roster_members` (
  `roster_id` bigint unsigned NOT NULL,
  `character_id` int unsigned NOT NULL,
  PRIMARY KEY (`roster_id`, `character_id`) USING BTREE,
  CONSTRAINT `fk_account_content_roster_members_roster` FOREIGN KEY (`roster_id`) REFERENCES `account_content_rosters` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Characters saved with a content roster';

CREATE TABLE IF NOT EXISTS `account_survey_form_replies` (
  `account_id` int unsigned NOT NULL,
  `survey_form_id` int unsigned NOT NULL,
  `character_id` int unsigned NOT NULL DEFAULT 0,
  `replied_at` int unsigned NOT NULL DEFAULT 0,
  PRIMARY KEY (`account_id`, `survey_form_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='One survey-form reply per account; the primary key is the exactly-once guard';
