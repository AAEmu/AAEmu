USE aaemu_game;

-- Hero system: leadership, peer reputation, the election, and what a serving hero does.
--
-- Ordered by dependency: the columns on `characters` first, then the tables that reference them.
-- The same schema is applied to SQL/aaemu_game.sql for fresh installs, as SQL/updates/readme.txt asks.


-- ================================================================================================
-- Leadership on the character
-- ================================================================================================

-- Leadership ("통솔력") - the stat the Hero election is built on. It was never persisted: the
-- character had no field for it at all, and SCCharacterState wrote the two daily columns as
-- hardcoded zeroes, so nothing a player earned could survive a relog (or even reach the client).
--
-- Four columns rather than one, because the client already reads four distinct values:
--
--   leadership_point         current leadership, for the season in progress. hero_conditions gates
--                            voting on it (votable_leadership_point = 500) and candidacy on
--                            hero_candidate_min_point (also 500). Published as the i32
--                            leadershipPoint in SCTeamAskHandOverOwner, where it decides
--                            raid-leader handover. Client: the "Leadership" row.
--
--   leadership_period_point  the PREVIOUS season's final leadership - a closed record, not a running
--                            total. Client: its own "Last Season Leadership" row, fed by game-point
--                            slot 12 as periodLeadershipPointStr ("period" meaning the completed
--                            period). Only a season rollover should write it, by copying current
--                            leadership in and resetting; no HeroManager exists to do that yet.
--
--   daily_leadership_point   earned since the stamp below, for the retail daily cap. Serialized in
--                            SCCharacterState. The cap is not enforced yet.
--
--   last_daily_leadership_point_time
--                            when the daily counter last rolled over. The zero default reads as
--                            "never accrued", which is what a fresh character should be.
--
-- Signed ints, matching the client's i32 wire fields. The server clamps at 0 on the way down, so
-- the sign is never used in practice - but a width mismatch with the packet would be worse than an
-- unused sign bit.
ALTER TABLE `characters`
  ADD COLUMN `leadership_point` int NOT NULL DEFAULT 0
    COMMENT 'Lifetime leadership; hero voting/candidacy gate',
  ADD COLUMN `leadership_period_point` int NOT NULL DEFAULT 0
    COMMENT 'Leadership earned this ranking period; hero leaderboard sorts on this',
  ADD COLUMN `daily_leadership_point` int unsigned NOT NULL DEFAULT 0
    COMMENT 'Leadership earned since last_daily_leadership_point_time',
  ADD COLUMN `last_daily_leadership_point_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00'
    COMMENT 'When the daily leadership counter last rolled over';

-- ================================================================================================
-- Lifetime leadership
-- ================================================================================================

-- Lifetime leadership. The three leadership figures are genuinely different quantities and the client
-- shows two of them side by side, so the missing one could not be derived from what was stored:
--
--   leadership_point              earned in the CURRENT ranking period. This is what the leaderboard
--                                 sorts on, and what resets when a period closes.
--   accumulated_leadership_point  earned over the character's whole life. Never reset.
--   leadership_period_point       the PREVIOUS period's final figure, kept for its own client row.
--
-- The Hero window renders the first two together as "Current Record: <period>/<accumulated>", and each
-- ranking row repeats the pair. Retail rows like 3398/621999 make the split obvious: the list is
-- ordered by the first number while the second runs unordered beside it, which is only possible if the
-- second is a lifetime total rather than anything period-scoped.
--
-- Named after the client's own term - it calls this accumulated_leadership_point internally.
ALTER TABLE `characters`
  ADD COLUMN `accumulated_leadership_point` int NOT NULL DEFAULT 0
    COMMENT 'Lifetime leadership, never reset; client "Current Record" right-hand figure';

-- Existing characters have only ever accrued inside the current period, so their lifetime total is at
-- least what they hold now. Seeding it from the current figure keeps the pair consistent instead of
-- showing everyone a lifetime total of zero next to a non-zero period score.
UPDATE `characters`
   SET `accumulated_leadership_point` = `leadership_point` + `leadership_period_point`
 WHERE `accumulated_leadership_point` = 0;

-- ================================================================================================
-- Reputation, the input side of leadership
-- ================================================================================================

-- Reputation: the peer-commendation standing that feeds Leadership.
--
-- Players rate each other inside a party or raid; the rating raises the TARGET's reputation, and at
-- each Hero Qualification Evaluation the reputation ladder is converted into Leadership through the
-- reputation_rewards percentile table. The client's own help text states the rules:
--
--   "Reputation boosts a character's Leadership score.
--    Hero Qualification Evaluations: 12AM and 12PM
--    You can only rate a character once per day."
--   "Rate a player's contributions to a party or raid. Target a player in your party or raid to rate.
--    Requirements to Rate: Must be Lv$1+ with $2+ Leadership."
--
-- Held on the character rather than in its own table: it is one number per character with the same
-- lifetime as the leadership figures beside it, and the evaluation has to rank a whole faction by it,
-- which is a plain ORDER BY this way.
ALTER TABLE `characters`
  ADD COLUMN `reputation` int NOT NULL DEFAULT 0
    COMMENT 'Peer-rating standing; converted to leadership at each evaluation, then reset';

-- One row per rater/target pair, holding when that pair last rated.
--
-- Needed because the once-per-day rule is per PAIR, not per rater: you may rate many people in a day,
-- but each of them only once. A counter on the rater could not express that.
--
-- Rows are kept rather than cleared after an evaluation - the pair is the key, and the timestamp is
-- what the rule reads - so the table stays one row per relationship instead of growing per day.
CREATE TABLE IF NOT EXISTS `character_reputation_votes` (
  `voter_id` int unsigned NOT NULL,
  `target_id` int unsigned NOT NULL,
  `voted_at` datetime NOT NULL,
  PRIMARY KEY (`voter_id`, `target_id`),
  KEY `idx_reputation_votes_target` (`target_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Last time each rater rated each target';

-- ================================================================================================
-- Serving heroes
-- ================================================================================================

-- Serving heroes. This is the election's output: whoever wins a season is written here, and the Hero
-- window's "Current Heroes" tab plus every IsHero() gate in the client reads from it.
--
-- Keyed on the character, because a character serves for at most one nation at a time. faction_id is
-- the NATION (the top-level system_factions row, e.g. 148 Nuia Alliance) rather than the character's
-- own member faction, since that is the granularity the client asks about.
--
-- grade is a hero_grades row: 1 Eperium, 2 Delphinad, 3 Ayanad, 4 Erenor. Retail seats six per nation
-- in a 1/2/3 pyramid - one Erenor, two Ayanad, three Delphinad - which the client lays out from the
-- grade alone.
--
-- season is the heros row the term belongs to, so a later rollover can retire a term without losing
-- the record of who held it.
CREATE TABLE IF NOT EXISTS `heroes` (
  `character_id` int unsigned NOT NULL,
  `faction_id` int unsigned NOT NULL COMMENT 'Nation (top-level system_factions id)',
  `grade` tinyint unsigned NOT NULL DEFAULT 1 COMMENT 'hero_grades row: 1 Eperium .. 4 Erenor',
  `season` int unsigned NOT NULL DEFAULT 0 COMMENT 'heros row this term belongs to',
  `elected_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`character_id`),
  KEY `idx_heroes_faction` (`faction_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Currently serving heroes per nation';

-- ================================================================================================
-- The frozen candidate list
-- ================================================================================================

-- The candidates standing in one nation's hero election, frozen.
--
-- The ballot cannot be derived from the live leadership ladder. Leadership keeps accruing while voting
-- is open, so a list computed per request would reorder itself between one player opening the window
-- and the next, and a candidate could drop off the bottom after votes had already been cast for them.
--
-- The snapshot is taken when the ballot opens. Retail takes it when the leadership_ranking phase ends,
-- and says so: the client announces "Finished collecting Leadership information for Hero Candidate
-- selection. Hero Candidates will be announced in 10 minutes." - that gap is the snapshot being taken.
--
-- The leadership figures are copied rather than read back from `characters` for the same reason: the
-- ballot should show what the candidate stood on, not what they have earned since. Reputation is copied
-- on the same grounds, and additionally because the twice-daily evaluation wipes it - a live read would
-- show every candidate on zero the moment an evaluation ran mid-election.
--
-- The guild is deliberately NOT here. It is identity rather than qualification: a candidate who changes
-- expedition during the election has not changed what they stood on, but the ballot naming their old
-- guild is simply wrong. It is resolved at send time, the same as the name.
CREATE TABLE IF NOT EXISTS `hero_election_candidates` (
  `season`        int unsigned NOT NULL COMMENT 'heros.id - the season this ballot belongs to',
  `faction_id`    int unsigned NOT NULL COMMENT 'The NATION, not the member faction',
  `character_id`  int unsigned NOT NULL,
  `ranking`       int NOT NULL DEFAULT 0 COMMENT 'Placing on the ladder when the snapshot was taken',
  `score`         int NOT NULL DEFAULT 0 COMMENT 'Leadership earned in the ranking period',
  `accum_point`   int NOT NULL DEFAULT 0 COMMENT 'Lifetime leadership at snapshot time',
  `reputation`    int NOT NULL DEFAULT 0,
  `abstained`     tinyint(1) NOT NULL DEFAULT 0 COMMENT 'Withdrew during the hero_abstain phase',
  `vote_count`    int NOT NULL DEFAULT 0,
  `frozen_at`     datetime NOT NULL,
  PRIMARY KEY (`season`, `character_id`),
  KEY `idx_hero_candidates_faction` (`season`, `faction_id`, `ranking`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Frozen hero election candidate list, one row per candidate';

-- ================================================================================================
-- Ballots
-- ================================================================================================

-- One row per candidate a voter picked, for one season's election.
--
-- The ballot is multi-select: the client lets a voter tick up to as many candidates as their nation
-- elects (6 for Nuia and Haranya, 3 for the Pirates) and sends them as one set, so a vote is a set of
-- picks rather than a single choice. Storing the picks individually is what makes the count a GROUP BY
-- and what lets "has this character already voted" be a plain EXISTS.
--
-- The primary key stops the same voter backing the same candidate twice inside one ballot; the
-- application refuses a second ballot outright, since retail allows one submission per election and the
-- client hides its own Vote button afterwards.
CREATE TABLE IF NOT EXISTS `hero_election_votes` (
  `season`       int unsigned NOT NULL COMMENT 'heros.id - the election this ballot belongs to',
  `voter_id`     int unsigned NOT NULL,
  `candidate_id` int unsigned NOT NULL,
  `voted_at`     datetime NOT NULL,
  PRIMARY KEY (`season`, `voter_id`, `candidate_id`),
  KEY `idx_hero_votes_candidate` (`season`, `candidate_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Hero election ballots, one row per pick';

-- ================================================================================================
-- Leadership roll bookkeeping
-- ================================================================================================

-- Records that a season's leadership roll has been done, so it happens once and not once per entry.
--
-- The roll moves every character's current-period leadership into the historical column and clears it,
-- which is what starts a fresh ladder. It belongs to the beginning of a leadership_ranking window, but
-- "entered leadership_ranking" is not the same event as "a new season began": a GM stepping the phases
-- back and forth, or a server restart inside the window, would otherwise wipe the ladder each time.
--
-- One row per season is enough to tell those apart.
CREATE TABLE IF NOT EXISTS `hero_season_rolls` (
  `season`    int unsigned NOT NULL COMMENT 'heros.id whose ranking period has been opened',
  `rolled_at` datetime NOT NULL,
  `characters_rolled` int NOT NULL DEFAULT 0 COMMENT 'How many rows the roll touched, for the record',
  PRIMARY KEY (`season`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Which seasons have had their leadership roll applied';

-- ================================================================================================
-- Mobilization orders
-- ================================================================================================

-- How many mobilization orders a hero has issued: today, and across the current term.
--
-- Two counters with different lifetimes, which is why both live here rather than on `characters`. The
-- daily one is a budget the client also tracks and shows as "Mobilization Orders Rem. n/5"; the term one
-- is progress toward the fifty the hero bonus asks for, and is the "n/50" on the Hero window's Mission
-- Status tab.
--
-- `day` and `season` are stored so the rollovers can be decided on read. The daily count resets at
-- midnight UTC and the term count resets when the season changes, and checking on read rather than from
-- a scheduled job means a server that was down over either boundary still rolls over correctly.
CREATE TABLE IF NOT EXISTS `hero_mobilization_orders` (
  `character_id` int unsigned NOT NULL,
  `season`       int unsigned NOT NULL COMMENT 'heros.id the total belongs to; a new season restarts it',
  `day`          date NOT NULL COMMENT 'UTC day the daily count belongs to',
  `today_count`  int NOT NULL DEFAULT 0 COMMENT 'Issued on `day`, capped at 5',
  `total_count`  int NOT NULL DEFAULT 0 COMMENT 'Issued during `season`, counts toward the bonus',
  PRIMARY KEY (`character_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Mobilization orders issued per hero';
