-- Account payment/tier records, one per account.
--
-- The account's tier is what SCAccountInfo publishes on the 10.x connection path, and what the
-- paid entitlements (online/offline labor, credits and loyalty tick rates, the account labor cap)
-- branch on. It is per-account data, so it is persisted here instead of being asserted in code.
--
-- payment_method is a PaymentMethodType wire value: 1 premium, 3 demo, 5 none. 5 is the seeded
-- state - an account that exists without a purchase carries no paid entitlement, which is what
-- PremiumState must report. pay_start/pay_end are the unix epoch for that same reason: there is no
-- subscription window, and the client only paints a remaining-day count from a real one.
--
-- Seeded from `accounts` so every existing account owns a row from the first boot after this
-- migration. A missing row is not silently treated as free: the loader logs an error naming the
-- account and fails closed to the no-entitlement state.

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
