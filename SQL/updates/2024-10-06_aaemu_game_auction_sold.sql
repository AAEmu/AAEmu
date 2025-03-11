-- ----------------------------------
-- Table structure for auction_sold
-- ----------------------------------
DROP TABLE IF EXISTS `auction_sold`;
CREATE TABLE auction_sold (
    id INT AUTO_INCREMENT PRIMARY KEY,
    item_id INT UNSIGNED NOT NULL,
    day INT NOT NULL,
    min_copper BIGINT NOT NULL,
    max_copper BIGINT NOT NULL,
    avg_copper BIGINT NOT NULL,
    volume INT NOT NULL,
    item_grade TINYINT NOT NULL,
    weekly_avg_copper BIGINT NOT NULL
);