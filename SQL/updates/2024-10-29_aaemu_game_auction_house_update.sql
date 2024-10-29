-- Удаление ненужных столбцов
ALTER TABLE `auction_house`
DROP COLUMN `object_id`,
DROP COLUMN `grade`,
DROP COLUMN `flags`,
DROP COLUMN `detail_type`,
DROP COLUMN `creation_time`,
DROP COLUMN `lifespan_mins`,
DROP COLUMN `type_1`,
DROP COLUMN `unsecure_date_time`,
DROP COLUMN `unpack_date_time`,
DROP COLUMN `world_id_2`;

-- Изменение типа данных столбца `id` на bigint
ALTER TABLE `auction_house`
MODIFY COLUMN `id` bigint NOT NULL AUTO_INCREMENT;

-- Изменение типа данных столбца `item_id` на bigint
ALTER TABLE `auction_house`
MODIFY COLUMN `item_id` bigint NOT NULL;

-- Изменение типа данных столбца `bid_world_id` на int
ALTER TABLE `auction_house`
MODIFY COLUMN `bid_world_id` int NOT NULL;

-- Изменение кодировки и сортировки столбцов `client_name` и `bidder_name`
ALTER TABLE `auction_house`
MODIFY COLUMN `client_name` varchar(45) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
MODIFY COLUMN `bidder_name` varchar(45) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL;

-- Изменение комментария и параметров таблицы
ALTER TABLE `auction_house`
COMMENT = 'Listed AH Items',
ROW_FORMAT = DYNAMIC;

-- Установка начального значения для AUTO_INCREMENT
ALTER TABLE `auction_house`
AUTO_INCREMENT = 1;