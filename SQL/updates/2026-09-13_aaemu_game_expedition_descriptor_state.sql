ALTER TABLE `expeditions`
    ADD COLUMN `war_deposit` int unsigned NOT NULL DEFAULT '0' AFTER `interest`,
    ADD COLUMN `war_wins` int unsigned NOT NULL DEFAULT '0' AFTER `war_deposit`,
    ADD COLUMN `war_losses` int unsigned NOT NULL DEFAULT '0' AFTER `war_wins`,
    ADD COLUMN `war_draws` int unsigned NOT NULL DEFAULT '0' AFTER `war_losses`,
    ADD COLUMN `daily_contribution_point` int unsigned NOT NULL DEFAULT '0' AFTER `war_draws`,
    ADD COLUMN `last_contribution_point_added` datetime NOT NULL DEFAULT '1970-01-01 00:00:00'
        AFTER `daily_contribution_point`;
