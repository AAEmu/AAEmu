-- Author: ZeromusXYZ - 2024/07/07
-- Fix for quest "To the Land for Eternity ( 2907 )"
-- Original value1 was 581
-- Fixes what SphereQuest needs to be check
UPDATE "unit_reqs" SET "value1"='1152' WHERE "owner_id"='12094' AND "owner_type"='Skill' AND "kind_id"='35'
