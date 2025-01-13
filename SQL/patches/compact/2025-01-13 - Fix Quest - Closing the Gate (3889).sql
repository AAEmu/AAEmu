-- Author: ZeromusXYZ - 2025/01/13
-- Fix for Elf Main quest "Closing the Gate ( 3889 )"
-- Original quest_id was 4280
-- Fixes what SphereQuest needs to be check
UPDATE "main"."sphere_quests" SET "quest_id"=3889 WHERE "id"='789'
