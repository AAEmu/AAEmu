ALTER TABLE `doodads`
    ADD COLUMN `interaction_counts` TEXT NULL
    COMMENT 'Completed interactions in the current doodad phase (JSON)';
