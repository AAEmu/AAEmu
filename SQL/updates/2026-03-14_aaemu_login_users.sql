ALTER TABLE `users`
  ADD COLUMN `korea_challenge_hash` VARCHAR(120) DEFAULT NULL
    COMMENT 'sha256_crypt $5$ hash used as AES-256 key for Korea challenge-response auth (V2).'
    AFTER `password`;
