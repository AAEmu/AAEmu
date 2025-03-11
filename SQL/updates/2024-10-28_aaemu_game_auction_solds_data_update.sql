-- ----------------------------------
-- Table structure for auction_solds_data
-- ----------------------------------
DROP TABLE IF EXISTS `auction_sold`;
DROP TABLE IF EXISTS `auction_solds_data`;
CREATE TABLE auction_solds_data (
    item_id INT UNSIGNED NOT NULL,
    item_grade TINYINT NOT NULL,
    date datetime NOT NULL,
    min_copper BIGINT NOT NULL,
    max_copper BIGINT NOT NULL,
    avg_copper BIGINT NOT NULL,
    volume INT NOT NULL,
    weekly_avg_copper BIGINT NOT NULL,
    PRIMARY KEY (item_id, item_grade, date)
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;
