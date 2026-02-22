-- Fix Elite (grade 2) NPC MaxHealth multiplier
-- Server calculates ~55,117 HP for Elite NPCs but client expects ~331,062 (6x more)
-- The unit_formula_variables npc_grade value for MaxHealth grade 2 needs to be 18 instead of 3
-- Formula: ( 5 * level^2.06 + ( (sta^1.05) * 9.3 ) ) * ( npc_template * npc_kind * npc_grade ) + 102

UPDATE unit_formula_variables
SET value = 18
WHERE unit_formula_id = 31
  AND variable_kind_id = 4
  AND key = 2;
