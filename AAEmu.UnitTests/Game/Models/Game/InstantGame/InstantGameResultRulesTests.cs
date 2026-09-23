using AAEmu.Game.Models.Game.InstantGame;
using AAEmu.Game.Models.Game.InstantGame.Static;

namespace AAEmu.UnitTests.Game.Models.Game.InstantGame;

/// <summary>
/// Score and ending derivation against values as they appear in the shipped content database
/// (read-only compact.sqlite3): rule set 17 is battlefield 1's row (victory_score 0,
/// victory_kill_count 0, victory_by_score 't', kill score 3), rule set 35 carries
/// victory_kill_count 10, rule set 37 mixes side-agnostic and side-bound score rows, rule set 34
/// only scores tagged kills, and rule set 22 ships no game_score_rules rows at all.
/// </summary>
public class InstantGameResultRulesTests
{
    private static GameScoreRule Kill(int corps, int value, int score, uint tag = 0) =>
        new(corps, (int)GameScoreEventKind.KillEnemyUnitFinalHitter, value, score, tag);

    [Test]
    public async Task ScoreVictory_IsDisabledWhenContentLeavesVictoryScoreAtZero()
    {
        // game_rule_sets row 17 (battlefield 1): victory_score = 0.
        await Assert.That(InstantGameResultRules.IsScoreVictory(teamScore: 0, victoryScore: 0)).IsFalse();
        await Assert.That(InstantGameResultRules.IsScoreVictory(teamScore: 900, victoryScore: 0)).IsFalse();
        await Assert.That(InstantGameResultRules.IsScoreVictory(teamScore: 100, victoryScore: 100)).IsTrue();
        await Assert.That(InstantGameResultRules.IsScoreVictory(teamScore: 99, victoryScore: 100)).IsFalse();
    }

    [Test]
    public async Task KillVictory_FiresAtTheContentKillCount()
    {
        // game_rule_sets row 35: victory_kill_count = 10.
        await Assert.That(InstantGameResultRules.IsKillVictory(teamTotalKills: 10, victoryKillCount: 10)).IsTrue();
        await Assert.That(InstantGameResultRules.IsKillVictory(teamTotalKills: 9, victoryKillCount: 10)).IsFalse();
        // A 0 threshold (rows without one) never ends the match on zero kills.
        await Assert.That(InstantGameResultRules.IsKillVictory(teamTotalKills: 0, victoryKillCount: 0)).IsFalse();
    }

    [Test]
    public async Task TimeOver_ByScore_ComparesScoresAndReportsTheMatchReason()
    {
        var ahead = InstantGameResultRules.DeriveTimeOver(score1: 3, score2: 0, kills1: 1, kills2: 1,
            victoryByScore: true);
        await Assert.That(ahead.Reason).IsEqualTo(BattlefieldEndingReason.TimeoverCompareScore);
        await Assert.That(ahead.Corps1State).IsEqualTo(VictoryState.Win);
        await Assert.That(ahead.Corps2State).IsEqualTo(VictoryState.Lose);

        var behind = InstantGameResultRules.DeriveTimeOver(0, 3, 5, 1, victoryByScore: true);
        await Assert.That(behind.Reason).IsEqualTo(BattlefieldEndingReason.TimeoverCompareScore);
        await Assert.That(behind.Corps1State).IsEqualTo(VictoryState.Lose);
        await Assert.That(behind.Corps2State).IsEqualTo(VictoryState.Win);
    }

    [Test]
    public async Task TimeOver_EvenTallyIsADraw()
    {
        var draw = InstantGameResultRules.DeriveTimeOver(score1: 7, score2: 7, kills1: 2, kills2: 2,
            victoryByScore: true);
        await Assert.That(draw.Reason).IsEqualTo(BattlefieldEndingReason.TimeoverDraw);
        await Assert.That(draw.Corps1State).IsEqualTo(VictoryState.Draw);
        await Assert.That(draw.Corps2State).IsEqualTo(VictoryState.Draw);
    }

    [Test]
    public async Task TimeOver_WithoutVictoryByScoreComparesKillCounts()
    {
        // victory_by_score = 'f': the kill tally decides, even when the scores say otherwise.
        var byKills = InstantGameResultRules.DeriveTimeOver(score1: 3, score2: 0, kills1: 1, kills2: 4,
            victoryByScore: false);
        await Assert.That(byKills.Reason).IsEqualTo(BattlefieldEndingReason.TimeoverCompareKillCount);
        await Assert.That(byKills.Corps1State).IsEqualTo(VictoryState.Lose);
        await Assert.That(byKills.Corps2State).IsEqualTo(VictoryState.Win);
    }

    [Test]
    public async Task KillScore_ComesFromTheRuleSetScoreRule()
    {
        // game_score_rules id 53: rule_set_id 17, rule_set_corps -1, event 200, value 0, score 3.
        var rules = new List<GameScoreRule> { Kill(-1, 0, 3) };

        var found = InstantGameScoreRules.TryGetScore(rules, GameScoreEventKind.KillEnemyUnitFinalHitter,
            eventValue: 0, corps: (int)InstantCorps.Corps1, eventTagId: 0, out var score);

        await Assert.That(found).IsTrue();
        await Assert.That(score).IsEqualTo(3);
    }

    [Test]
    public async Task KillScore_SideBoundRowOverridesTheAnySideRow()
    {
        // game_score_rules id 125/129: rule_set_id 37 scores event 200 value 0 with 10 for any
        // side (-1) and with 5 for its bound sides.
        var rules = new List<GameScoreRule> { Kill(-1, 0, 10), Kill(2, 0, 5), Kill(3, 0, 5) };

        await Assert.That(TryScore(rules, (int)InstantCorps.Corps1, out var anySide)).IsTrue();
        await Assert.That(anySide).IsEqualTo(10);

        await Assert.That(TryScore(rules, 2, out var bound)).IsTrue();
        await Assert.That(bound).IsEqualTo(5);
    }

    [Test]
    public async Task KillScore_TaggedRowsOnlyMatchTheirOwnTag()
    {
        // game_score_rules id 122/123: rule_set_id 34 kills unit (event 201, value 2) for 1 point
        // under tag 3427 and 20 points under tag 3444. An untagged kill has no row.
        var rules = new List<GameScoreRule>
        {
            new(-1, (int)GameScoreEventKind.KillUnit, 2, 1, 3427),
            new(-1, (int)GameScoreEventKind.KillUnit, 2, 20, 3444),
        };

        var unit = GameScoreEventKind.KillUnit;
        await Assert.That(InstantGameScoreRules.TryGetScore(rules, unit, 2, 0, 3444, out var tagged)).IsTrue();
        await Assert.That(tagged).IsEqualTo(20);
        await Assert.That(InstantGameScoreRules.TryGetScore(rules, unit, 2, 0, 0, out _)).IsFalse();
    }

    [Test]
    public async Task KillScore_MissingRuleSetScoresNothing()
    {
        // game_rule_sets row 22 (battlefield 7) ships no game_score_rules rows.
        await Assert.That(InstantGameScoreRules.TryGetScore([], GameScoreEventKind.KillEnemyUnitFinalHitter,
            0, 0, 0, out _)).IsFalse();
        await Assert.That(InstantGameScoreRules.TryGetScore(null, GameScoreEventKind.KillEnemyUnitFinalHitter,
            0, 0, 0, out _)).IsFalse();
    }

    private static bool TryScore(IReadOnlyList<GameScoreRule> rules, int corps, out int score) =>
        InstantGameScoreRules.TryGetScore(rules, GameScoreEventKind.KillEnemyUnitFinalHitter, 0, corps, 0,
            out score);
}
