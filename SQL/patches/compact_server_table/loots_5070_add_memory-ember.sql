-- Author: Black Judge - 2024/09/12
-- Исправляем ошибку: В камине нельзя взять уголёк Id=36917 "Memory Ember"
-- Отсутствуют записи о луте для версий больше 1.2.
-- Fixing a bug: You cannot take ember from the fireplace Id=36917 "Memory Ember"
-- There are no records of loot for versions greater than 1.2.

-- ----------------------------
-- Records of loots
-- ----------------------------
INSERT INTO "loots" VALUES (80462, 1, 36917, 10000000, 1, 1, 9972, 0, 'f');

-- ----------------------------
-- Records of loot_groups
-- ----------------------------
INSERT INTO "loot_groups" VALUES (2051, 9972, 1, 10000000, 0);
