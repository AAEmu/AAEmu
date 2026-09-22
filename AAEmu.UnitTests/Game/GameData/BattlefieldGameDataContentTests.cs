using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.InstantGame;
using AAEmu.Game.Models.Game.InstantGame.Static;

using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// The content loaders behind rule-set scoring: game_score_rules grouping and the game_rule_sets
/// fields the lifecycle derives from. Seed rows carry the shape (and the values of the shipped
/// rows they stand for): rule set 17 is battlefield 1's (victory_score 0, victory_by_score 't',
/// kill score 3), rule set 35 adds victory_kill_count 10, rule set 37 mixes side-bound rows.
/// </summary>
public sealed class BattlefieldGameDataContentTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        using (var command = Connection.CreateCommand())
        {
            command.CommandText =
                "CREATE TABLE game_rule_sets (" +
                "id INTEGER PRIMARY KEY, " +
                "time_ending INTEGER NOT NULL DEFAULT 1, " +
                "time_playing INTEGER NOT NULL DEFAULT 1, " +
                "time_ready INTEGER NOT NULL DEFAULT 0, " +
                "time_resurrection_delay INTEGER NOT NULL DEFAULT 15, " +
                "victory_score INTEGER NOT NULL DEFAULT 0, " +
                "victory_kill_count INTEGER NOT NULL DEFAULT 0, " +
                "victory_by_score BOOLEAN NOT NULL DEFAULT 't');";
            command.ExecuteNonQuery();
        }

        using (var command = Connection.CreateCommand())
        {
            command.CommandText =
                "CREATE TABLE game_score_rules (" +
                "id INTEGER PRIMARY KEY, " +
                "rule_set_id INTEGER NOT NULL, " +
                "rule_set_corps INTEGER NOT NULL DEFAULT -1, " +
                "event_id INTEGER NOT NULL, " +
                "event_value INTEGER NOT NULL, " +
                "event_score INTEGER NOT NULL, " +
                "event_tag_id INTEGER NOT NULL DEFAULT 0, " +
                "available_target_soldier BOOLEAN DEFAULT 't', " +
                "available_target_corps BOOLEAN DEFAULT 't');";
            command.ExecuteNonQuery();
        }

        using (var command = Connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO game_rule_sets(id, time_ending, time_playing, time_ready, time_resurrection_delay, " +
                "victory_score, victory_kill_count, victory_by_score) VALUES " +
                "(17, 1, 15, 0, 15, 0, 0, 't'), " +
                "(35, 1, 5, 10, 5, 0, 10, 't'), " +
                "(22, 30, 7, 10, 5, 0, 0, 't');";
            command.ExecuteNonQuery();
        }

        using (var command = Connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO game_score_rules(id, rule_set_id, rule_set_corps, event_id, event_value, event_score, event_tag_id) VALUES " +
                "(53, 17, -1, 200, 0, 3, 0), " +
                "(125, 37, -1, 200, 0, 10, 0), " +
                "(129, 37, 2, 200, 0, 5, 0);";
            command.ExecuteNonQuery();
        }
    }

    [Test]
    public async Task LoadGameScoreRules_GroupsRowsByRuleSet()
    {
        var scoreRules = BattlefieldGameData.LoadGameScoreRules(Connection);

        await Assert.That(scoreRules.ContainsKey(17u)).IsTrue();
        await Assert.That(scoreRules.ContainsKey(37u)).IsTrue();
        await Assert.That(scoreRules.ContainsKey(22u)).IsFalse();

        var rule17 = scoreRules[17u].Single();
        await Assert.That(rule17.RuleSetCorps).IsEqualTo(-1);
        await Assert.That(rule17.EventId).IsEqualTo((int)GameScoreEventKind.KillEnemyUnitFinalHitter);
        await Assert.That(rule17.EventValue).IsEqualTo(0);
        await Assert.That(rule17.EventScore).IsEqualTo(3);
        await Assert.That(rule17.EventTagId).IsEqualTo(0u);

        await Assert.That(scoreRules[37u].Count).IsEqualTo(2);
    }

    [Test]
    public async Task LoadGameRuleSets_ReadsVictoryAndTimingFieldsAndAttachesScoreRules()
    {
        var ruleSets = BattlefieldGameData.LoadGameRuleSets(
            Connection,
            new Dictionary<uint, uint> { [17u] = 1u, [35u] = 18u },
            BattlefieldGameData.LoadGameScoreRules(Connection));

        var rule17 = ruleSets[17u];
        await Assert.That(rule17.BattlefieldId).IsEqualTo(1u);
        await Assert.That(rule17.TimePlaying).IsEqualTo(15);
        await Assert.That(rule17.TimeEnding).IsEqualTo(1);
        await Assert.That(rule17.TimeReady).IsEqualTo(0);
        await Assert.That(rule17.TimeResurrectionDelay).IsEqualTo(15);
        await Assert.That(rule17.VictoryScore).IsEqualTo(0);
        await Assert.That(rule17.VictoryKillCount).IsEqualTo(0);
        await Assert.That(rule17.VictoryByScore).IsTrue();
        await Assert.That(rule17.ScoreRules.Count).IsEqualTo(1);
        await Assert.That(rule17.TryGetEventScore(GameScoreEventKind.KillEnemyUnitFinalHitter, 0,
            (int)InstantCorps.Corps1, 0u, out var killScore)).IsTrue();
        await Assert.That(killScore).IsEqualTo(3);

        var rule35 = ruleSets[35u];
        await Assert.That(rule35.BattlefieldId).IsEqualTo(18u);
        await Assert.That(rule35.VictoryKillCount).IsEqualTo(10);
        await Assert.That(rule35.ScoreRules).IsEmpty();

        // A rule set with no battle field link stays unattached rather than vanishing.
        await Assert.That(ruleSets[22u].BattlefieldId).IsEqualTo(0u);
        await Assert.That(ruleSets[22u].ScoreRules).IsEmpty();
    }

    [Test]
    public async Task LoadGameScoreRules_EmptyTableYieldsNoRuleSets()
    {
        using (var command = Connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM game_score_rules";
            command.ExecuteNonQuery();
        }

        var scoreRules = BattlefieldGameData.LoadGameScoreRules(Connection);
        await Assert.That(scoreRules).IsEmpty();
    }
}
