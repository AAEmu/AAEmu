-- Fix: synthesis dead-ends gear that is meant to keep growing.
--
-- APPLY THIS TO THE CLIENT DATABASE (client\game\db\compact.sqlite3), not the server's.
-- The cap this removes is enforced entirely client-side; see "Why the client" below.
--
-- The problem
-- -----------
-- item_rnd_attr_categories.max_evolving_grade is lower than the grade ladder the same category
-- actually defines. Erenor/Ipnir two-handed weapons (category 51) say 7 (Celestial), yet
-- item_rnd_attr_category_properties gives every grade above it a real cost:
--
--     Celestial 19,214 -> Divine 25,643 -> Epic 34,816
--     -> Legendary 45,214 -> Mythic 59,259 -> Eternal 77,742
--
-- The client reads max_evolving_grade (column 5 of its own
-- "SELECT id, currency_id, desc, item_rnd_attr_category_group_id, material_grade_limit,
--  max_evolving_grade, ... FROM item_rnd_attr_categories", kept as a byte at category desc +0x18)
-- and both prints it in the tooltip -- "Synthesis Available(~Celestial)" -- and refuses the
-- infusion once the item reaches it.
--
-- That dead-ends the gear: the Erenor Awakening Scroll (47032) requires Legendary or higher, and
-- synthesis is what raises the grade, so the item can be neither grown further nor awakened.
-- Retail shows these lines capping at Eternal. The 23.x content pack floating around
-- (backfill_missing_data.sql) independently ships max_evolving_grade = 12 on every Erenor growth
-- category it adds, which is the same conclusion from a dataset closer to retail.
--
-- The fix
-- -------
-- Set max_evolving_grade to the top of each category's own ladder: the highest grade_id that has
-- grade_exp > 0. Per-category, so a line that genuinely stops at Divine gets 8, not 12.
-- 338 categories change; the distribution of shipped -> ladder top is
--     7->12 (161)   7->11 (89)   7->9 (37)   7->8 (29)   7->10 (20)   1->11   5->6
--
-- max_evolving_grade >= 1 is deliberate. 22 further categories also carry grade_exp above their
-- cap but must be left alone:
--   * -1 means "not growable" -- 19 of these are the ipnir_exp / Obsidian MATERIAL lines, which
--     carry grade_exp only so the awakening routine can total the exp behind an item's grade
--     (X2::GetItemChangeMappingGrade). Raising them would let players synthesize the materials.
--   *  0 is item_test_category_2, event_test and a cosplay entry.
--
-- Why the client and not the server
-- ---------------------------------
-- AAEmu does not read this column: after the accompanying server fix, ItemManager.SpendEvolvingExp
-- gates purely on grade_exp > 0, the same condition the client's own canEvolve uses, and stops
-- because there is no grade above Eternal to step to. max_evolving_grade is loaded into
-- ItemRndAttrCategory.MaxEvolvingGrade and read by nothing. The server's compact.sqlite3 is also
-- gitignored, so it is each developer's own extracted copy. Patching the server changes nothing;
-- patching the client is what lifts the gate.
--
-- Notes
-- -----
--  * Idempotent -- re-running matches no rows.
--  * Back the database up first. The shipped values are overwritten in place and cannot be derived
--    from the patched database afterwards, so a copy is the only way back.
--  * Restart the client afterwards; the table is read once at startup.
--  * Every client connecting to a server that allows the higher grades needs this same patch,
--    otherwise those players stay capped.

UPDATE `item_rnd_attr_categories`
SET `max_evolving_grade` = (
        SELECT MAX(`p`.`grade_id`)
        FROM `item_rnd_attr_category_properties` `p`
        WHERE `p`.`item_rnd_attr_category_id` = `item_rnd_attr_categories`.`id`
          AND `p`.`grade_exp` > 0)
WHERE `max_evolving_grade` >= 1
  AND (
        SELECT MAX(`p`.`grade_id`)
        FROM `item_rnd_attr_category_properties` `p`
        WHERE `p`.`item_rnd_attr_category_id` = `item_rnd_attr_categories`.`id`
          AND `p`.`grade_exp` > 0) > `max_evolving_grade`;

-- Verification -- expected: 0 rows left to change, and category 51 reporting 12.
--
-- SELECT COUNT(*) AS still_capped
--   FROM item_rnd_attr_categories
--  WHERE max_evolving_grade >= 1
--    AND (SELECT MAX(p.grade_id)
--           FROM item_rnd_attr_category_properties p
--          WHERE p.item_rnd_attr_category_id = item_rnd_attr_categories.id
--            AND p.grade_exp > 0) > max_evolving_grade;
--
-- SELECT id, max_evolving_grade FROM item_rnd_attr_categories WHERE id = 51;
