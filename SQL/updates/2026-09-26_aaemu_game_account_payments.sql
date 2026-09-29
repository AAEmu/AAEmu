-- Account payment/tier records, one per account.
--
-- The account's tier is what SCAccountInfo publishes on the 10.x connection path, and what the
-- paid entitlements (online/offline labor, credits and loyalty tick rates, the account labor cap)
-- branch on. It is per-account data, so it is persisted here instead of being asserted in code.
--
-- Seeded from `accounts` so every existing account owns a row from the first boot after this
-- migration. A missing row is not silently treated as free: the loader logs an error naming the
-- account and fails closed to the no-entitlement state.
--
-- The row written below is a PLACEHOLDER, not a decision, and it is deliberately the no-entitlement
-- tier on purpose. A migration cannot read the server's configuration, so it cannot seed the tier an
-- operator asked for, and seeding the free tier as if it were the answer is what dropped every
-- existing account to free. Instead the loader recognises exactly this row - method 5, no location, no
-- window, no recorded purchase - and replaces it once, from Account.SeededPaymentMethod and
-- Account.SeededPaymentDays, then persists the result.
--
-- That is also why no duration is written here. A period hardcoded in SQL would be a second copy of
-- the configured default that could disagree with it, and it would expire on its own and silently
-- return every account to free. Keeping the window in configuration is what makes the upgrade a
-- one-time step rather than a scheduled demotion.
--
-- payment_method is a PaymentMethodType wire value: 1 premium, 3 demo, 5 none. pay_start/pay_end are
-- the unix epoch because the placeholder has no subscription window, and the client only paints a
-- remaining-day count from a real one.

CREATE TABLE IF NOT EXISTS `account_payments` (
    `account_id`       INT UNSIGNED NOT NULL,
    `payment_method`   INT          NOT NULL,
    `payment_location` INT          NOT NULL,
    `pay_start`        DATETIME     NOT NULL,
    `pay_end`          DATETIME     NOT NULL,
    `buy_count`        INT          NOT NULL,
    PRIMARY KEY (`account_id`)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COMMENT = 'Per-account payment tier driving paid entitlements';

INSERT INTO `account_payments`
    (`account_id`, `payment_method`, `payment_location`, `pay_start`, `pay_end`, `buy_count`)
SELECT `account_id`, 5, 0, '1970-01-01 00:00:00', '1970-01-01 00:00:00', 0
FROM `accounts`
WHERE `account_id` NOT IN (SELECT `account_id` FROM `account_payments`);

-- Every row above is the placeholder the loader upgrades. Do not "fix" it by writing a paid method
-- and a fixed pay_end here: that is the duration the configuration owns, and an account whose row
-- still matches the placeholder exactly is the only one the loader will replace.
