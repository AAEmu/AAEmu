-- Label the first history type by the content-backed instance_rank_details identity associated
-- with instances.id. Native proves the field shape; this identity mapping is relational evidence.
ALTER TABLE `expedition_instance_histories`
  CHANGE COLUMN `battlefield_type` `instance_rank_detail_id` INT UNSIGNED NOT NULL
  COMMENT 'Content-backed instance_rank_details.id used for the first history/rating type';
