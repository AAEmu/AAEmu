USE aaemu_game;

ALTER TABLE `character_rank_scores`
  ADD COLUMN `sub_data` varbinary(24) NULL
  COMMENT 'The line''s own detail block as the client reads it (kind byte and payload), or NULL when the board carries none'
  AFTER `bare_value`;
