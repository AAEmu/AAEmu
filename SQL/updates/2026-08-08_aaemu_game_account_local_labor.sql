USE aaemu_game;

-- The LOCAL ("Online"/server) labor pool belongs to the account, not to a single character.
-- 10.0.2.13 carries it inside the per-character lobby record only because that record is a full
-- snapshot; at runtime the client keeps ONE labor manager per session with two counters
-- (account +0xE58, local +0xE68). Keeping the pool in characters.local_lp made the character-select
-- header show whichever character the client happened to bind - in practice always the first one,
-- so an account whose first character had never gathered any read "Online Labor 0" for every slot.
ALTER TABLE `accounts`
  ADD COLUMN `local_labor` INT UNSIGNED NOT NULL DEFAULT 0 AFTER `labor`;

-- Carry the existing balances over. The pool is shared, so the largest per-character balance is the
-- one the account actually earned; summing would hand out labor that was never granted.
UPDATE `accounts` a
  SET a.`local_labor` = COALESCE((
    SELECT MAX(c.`local_lp`) FROM `characters` c WHERE c.`account_id` = a.`account_id`
  ), 0);

-- characters.local_lp is left in place so this migration stays reversible, but nothing reads or
-- writes it any more. Character saves use REPLACE INTO, so each character resets it to 0 the first
-- time it is saved after this update - the balances above are the ones that carry over.
