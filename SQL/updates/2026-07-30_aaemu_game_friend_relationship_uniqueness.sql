DELETE duplicate_row
FROM `friends` AS duplicate_row
INNER JOIN `friends` AS keeper
  ON keeper.`owner` = duplicate_row.`owner`
  AND keeper.`friend_id` = duplicate_row.`friend_id`
  AND keeper.`id` < duplicate_row.`id`;

ALTER TABLE `friends`
  ADD UNIQUE KEY `uk_friends_owner_friend` (`owner`,`friend_id`) USING BTREE;
