-- ----------------------------
-- Table structure for shipyards
-- ----------------------------
DROP TABLE IF EXISTS shipyards;
CREATE TABLE shipyards (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    main_model_id INTEGER NOT NULL,
    item_id INTEGER NOT NULL,
    ceremony_anim_time INTEGER NOT NULL,
    spawn_offset_front REAL NOT NULL,
    spawn_offset_z REAL NOT NULL,
    build_radius INTEGER NOT NULL,
    tax_duration INTEGER DEFAULT 0,
    origin_item_id INTEGER DEFAULT 0,
    taxation_id INTEGER NOT NULL
);

-- ----------------------------
-- Table structure for shipyard_steps
-- ----------------------------
DROP TABLE IF EXISTS shipyard_steps;
CREATE TABLE shipyard_steps (
    id INTEGER PRIMARY KEY,
    shipyard_id INTEGER NOT NULL,
    step INTEGER NOT NULL,
    model_id INTEGER NOT NULL,
    skill_id INTEGER NOT NULL,
    num_actions INTEGER NOT NULL,
    max_hp INTEGER NOT NULL,
    FOREIGN KEY (shipyard_id) REFERENCES shipyards(id)
);
