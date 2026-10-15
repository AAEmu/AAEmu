-- Fix: Halloween gourd (doodad 7422) despawns permanently after its first harvest cycle.
-- Final row 3554 is the DoodadFuncFinal of doodad 7422 phase group 20270 only.
-- With respawn = 'f' the gourd despawns forever after its first 600s harvest window
-- (phase timers 8751/8752), making the daily Squash Head (6036) gather source impossible
-- until the next server restart.
--
-- This patch sets respawn = 't' so the spawner re-spawns the gourd after the row's 60s
-- delay, keeping the Squash Head (6036) gather source cycling all season.

UPDATE `doodad_func_finals` SET `respawn` = 't' WHERE `id` = 3554;
