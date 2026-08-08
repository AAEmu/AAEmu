USE aaemu_game;

-- The account's nominated main character ("represent character" in the client's own wording).
-- At most one character per account carries it; CharacterManager clears the account before setting
-- one, so the rule holds even if a previous nomination is stale.
--
-- Character select used to name the first character on every account instead, with success=true.
-- The client stores whatever id it is given as THE represent character, and its delete dialog then
-- refuses that character with "Must deselect as Main Character before deleting." - so slot one was
-- undeletable on every account, for a choice nobody had made.
ALTER TABLE `characters`
  ADD COLUMN `represent` tinyint(1) NOT NULL DEFAULT 0
  COMMENT 'Is this the account main (represent) character';
