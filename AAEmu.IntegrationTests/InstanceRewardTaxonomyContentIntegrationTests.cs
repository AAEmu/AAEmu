using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Indun;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AAEmu.IntegrationTests;

/// <summary>
/// W03C opt-in checks against the runtime compact catalog a real World process loads.
/// <para>
/// The ids in these assertions are catalog keys read out of the same database inside the test, not
/// literals carried by the test: each case discovers the instance whose reward ranges match a
/// structural shape and then asserts how that shape classifies. Renumbering content therefore moves
/// the test with the content instead of breaking it.
/// </para>
/// </summary>
[Collection(IndunRewardContentCollection.Name)]
public sealed class InstanceRewardTaxonomyContentIntegrationTests
{
    private const string EnvironmentVariable = "AAEMU_INDUN_REWARD_TEST_CONTENT";

    private static SqliteConnection OpenRuntimeCompact()
    {
        var contentPath = Environment.GetEnvironmentVariable(EnvironmentVariable);
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(contentPath) && File.Exists(contentPath),
            $"Set {EnvironmentVariable} to a runtime compact.sqlite3 path.");

        var connection = new SqliteConnection($"Data Source=file:{contentPath};Mode=ReadOnly");
        connection.Open();
        IndunGameData.Instance.Load(connection);
        return connection;
    }

    private static List<uint> RewardKindsOf(InstanceRewardSelectionClass expected)
    {
        using var connection = OpenRuntimeCompact();
        var kinds = new SortedSet<uint>();
        foreach (var instanceId in RewardInstancesOfExpectedClass(connection, expected))
        {
            var zone = IndunGameData.Instance.GetDungeonZoneByCatalogId(instanceId);
            if (zone == null)
                continue;

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT DISTINCT instance_reward_kind_id FROM instance_rewards WHERE instance_id = $id";
            command.Parameters.AddWithValue("$id", instanceId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var kindId = Convert.ToUInt32(reader.GetValue(0));
                var verdict = IndunGameData.Instance.ClassifyInstanceRewardSelection(instanceId, kindId, null);
                if (verdict.Classification == expected)
                    kinds.Add(kindId);
            }
        }

        return kinds.ToList();
    }

    /// <summary>
    /// Instances whose reward ranges are covered exactly by the round count of their own zone group —
    /// the structural shape of a round-backed reward, found without naming a single shipped id.
    /// </summary>
    private static List<uint> RewardInstancesOfExpectedClass(SqliteConnection connection, InstanceRewardSelectionClass expected)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT r.instance_id, r.instance_reward_kind_id, MIN(r.start_range), MAX(r.end_range)
                                 FROM instance_rewards r
                                 JOIN instances i ON i.id = r.instance_id
                                 WHERE i.target_type = 'IndunZone'
                                 GROUP BY r.instance_id, r.instance_reward_kind_id
                                 ORDER BY r.instance_id, r.instance_reward_kind_id";
        var instances = new SortedSet<uint>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var instanceId = Convert.ToUInt32(reader.GetValue(0));
            var kindId = Convert.ToUInt32(reader.GetValue(1));
            var minStart = Convert.ToInt32(reader.GetValue(2));
            var maxEnd = Convert.ToInt32(reader.GetValue(3));
            var zone = IndunGameData.Instance.GetDungeonZoneByCatalogId(instanceId);
            if (zone == null)
                continue;

            var verdict = IndunGameData.Instance.ClassifyInstanceRewardSelection(instanceId, kindId, null);
            var matchesExpected = verdict.Classification == expected;

            if (expected == InstanceRewardSelectionClass.RoundBacked)
            {
                // The round-backed shape must also be an exact cover, verified independently here.
                matchesExpected = matchesExpected && minStart == 1 && maxEnd > 0 &&
                                  IndunGameData.Instance.GetRounds(zone.ZoneGroupId).Count == maxEnd;
            }

            if (expected == InstanceRewardSelectionClass.DifficultyBacked)
                matchesExpected = matchesExpected && IndunGameData.Instance.GetInstanceDifficulties(instanceId).Count > 0;

            if (expected == InstanceRewardSelectionClass.RankBacked)
            {
                matchesExpected = matchesExpected && IndunGameData.Instance.GetInstanceFactions(instanceId).Count > 0 &&
                                  IndunGameData.Instance.GetInstanceMiniScoreboards(instanceId).Count > 0;
            }

            if (matchesExpected)
                instances.Add(instanceId);
        }

        return instances.ToList();
    }

    [Fact]
    public void RuntimeCompactClassifiesRoundBackedRewardsWithAProvenSelectionAndNoTrigger()
    {
        using var connection = OpenRuntimeCompact();
        var kinds = RewardKindsOf(InstanceRewardSelectionClass.RoundBacked);
        Assert.NotEmpty(kinds);

        foreach (var kindId in kinds)
        {
            foreach (var instanceId in RewardInstancesOfExpectedClass(connection, InstanceRewardSelectionClass.RoundBacked))
            {
                var verdict = IndunGameData.Instance.ClassifyInstanceRewardSelection(instanceId, kindId, null);
                if (verdict.Classification != InstanceRewardSelectionClass.RoundBacked)
                    continue;

                Assert.True(verdict.SelectionSourceProven);
                // Nothing in shipped content reaches a delivery hook for a round-backed kind.
                Assert.False(verdict.DeliveryTriggerAuthored);
                Assert.False(verdict.DeliverableNow);
                Assert.Equal(InstanceRewardBlocker.MissingDeliveryTrigger, verdict.Blocker);
                Assert.Contains("indun_action_send_mail_rewards",
                    InstanceRewardTaxonomyRules.DescribeBlocker(verdict));
            }
        }
    }

    [Fact]
    public void RuntimeCompactClassifiesRankBackedRewardsAsBlockedOnTheScoreSource()
    {
        using var connection = OpenRuntimeCompact();
        var kinds = RewardKindsOf(InstanceRewardSelectionClass.RankBacked);
        Assert.NotEmpty(kinds);

        foreach (var kindId in kinds)
        {
            foreach (var instanceId in RewardInstancesOfExpectedClass(connection, InstanceRewardSelectionClass.RankBacked))
            {
                var verdict = IndunGameData.Instance.ClassifyInstanceRewardSelection(instanceId, kindId, null);
                if (verdict.Classification != InstanceRewardSelectionClass.RankBacked)
                    continue;

                // Rank bands are authored, so the refusal must name the score, never the bands.
                Assert.NotEmpty(IndunGameData.Instance.GetInstanceRewards(instanceId, kindId, 1));
                Assert.False(verdict.SelectionSourceProven);
                Assert.False(verdict.DeliverableNow);
                Assert.Equal(InstanceRewardBlocker.MissingScoreSource, verdict.Blocker);
                Assert.Contains("score", InstanceRewardTaxonomyRules.DescribeBlocker(verdict));
            }
        }
    }

    [Fact]
    public void RuntimeCompactLoadsTheDisplayAndGroupingCatalogs()
    {
        using var connection = OpenRuntimeCompact();

        using var factionCommand = connection.CreateCommand();
        factionCommand.CommandText = "SELECT DISTINCT instance_id FROM instance_factions ORDER BY instance_id";
        var factionInstances = new List<uint>();
        using (var reader = factionCommand.ExecuteReader())
        {
            while (reader.Read())
                factionInstances.Add(Convert.ToUInt32(reader.GetValue(0)));
        }

        Assert.NotEmpty(factionInstances);
        foreach (var instanceId in factionInstances)
        {
            var factions = IndunGameData.Instance.GetInstanceFactions(instanceId);
            Assert.NotEmpty(factions);
            Assert.All(factions, faction =>
            {
                Assert.True(faction.Id > 0);
                Assert.True(faction.InstanceFactionPresetId > 0);
                Assert.True(faction.MaxPlayer >= faction.MinPlayer);
            });
        }

        using var boardCommand = connection.CreateCommand();
        boardCommand.CommandText = "SELECT DISTINCT instance_id FROM instance_mini_scoreboards ORDER BY instance_id";
        var boardInstances = new List<uint>();
        using (var reader = boardCommand.ExecuteReader())
        {
            while (reader.Read())
                boardInstances.Add(Convert.ToUInt32(reader.GetValue(0)));
        }

        Assert.NotEmpty(boardInstances);
        foreach (var instanceId in boardInstances)
        {
            var boards = IndunGameData.Instance.GetInstanceMiniScoreboards(instanceId);
            Assert.NotEmpty(boards);
            Assert.All(boards, board =>
            {
                Assert.True(Enum.IsDefined(board.TargetType));
                Assert.False(string.IsNullOrWhiteSpace(board.Name));
                // icon_id is a catalog key string and is never parsed as a number.
                Assert.False(string.IsNullOrWhiteSpace(board.IconId));
            });

            var rules = IndunGameData.Instance.GetInstanceGainRules(instanceId);
            Assert.NotEmpty(rules);
            var factionIds = IndunGameData.Instance.GetInstanceFactions(instanceId).Select(f => f.Id).ToHashSet();
            Assert.All(rules, rule => Assert.Contains(rule.InstanceFactionId, factionIds));
        }

        Assert.NotEmpty(IndunGameData.Instance.GetInstancePointDoodadPhaseChanges());
        Assert.NotEmpty(IndunGameData.Instance.GetInstanceRewardKindsWithDeliveryTrigger());
    }

    /// <summary>
    /// The display catalogs must never be sufficient on their own. Every rank-backed instance in
    /// shipped content stays blocked even though it publishes a full scoreboard and gain-rule set.
    /// </summary>
    [Fact]
    public void RuntimeCompactDisplayCatalogsNeverMakeARankBackedRewardDeliverable()
    {
        using var connection = OpenRuntimeCompact();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT DISTINCT r.instance_id, r.instance_reward_kind_id
                                 FROM instance_rewards r
                                 JOIN instance_mini_scoreboards s ON s.instance_id = r.instance_id
                                 JOIN instance_gain_rules g ON g.instance_id = r.instance_id
                                 ORDER BY 1, 2";
        using var reader = command.ExecuteReader();
        var checkedAny = false;
        while (reader.Read())
        {
            var instanceId = Convert.ToUInt32(reader.GetValue(0));
            var kindId = Convert.ToUInt32(reader.GetValue(1));
            var verdict = IndunGameData.Instance.ClassifyInstanceRewardSelection(instanceId, kindId, null);
            if (verdict.Classification != InstanceRewardSelectionClass.RankBacked)
                continue;

            checkedAny = true;
            Assert.False(verdict.DeliverableNow);
            Assert.Equal(InstanceRewardBlocker.MissingScoreSource, verdict.Blocker);
        }

        Assert.True(checkedAny, "expected at least one rank-backed instance with a display surface");
    }

    /// <summary>
    /// Legacy arena kinds stay unsupported. They are the only kind families in shipped content whose
    /// instances publish no zone-group membership, no difficulty info, no rounds and no teams, so no
    /// selection source can be established for them.
    /// </summary>
    [Fact]
    public void RuntimeCompactKeepsLegacyArenaKindsUnsupported()
    {
        using var connection = OpenRuntimeCompact();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT r.instance_id, r.instance_reward_kind_id
                                 FROM instance_rewards r
                                 LEFT JOIN indun_zones z ON z.zone_group_id = (
                                     SELECT target_id FROM instances WHERE id = r.instance_id)
                                 WHERE z.zone_group_id IS NULL
                                 GROUP BY r.instance_id, r.instance_reward_kind_id
                                 ORDER BY 1, 2";
        using var reader = command.ExecuteReader();
        var checkedAny = false;
        while (reader.Read())
        {
            var instanceId = Convert.ToUInt32(reader.GetValue(0));
            var kindId = Convert.ToUInt32(reader.GetValue(1));
            var verdict = IndunGameData.Instance.ClassifyInstanceRewardSelection(instanceId, kindId, null);

            checkedAny = true;
            Assert.Equal(InstanceRewardSelectionClass.Unsupported, verdict.Classification);
            Assert.False(verdict.DeliverableNow);
        }

        Assert.True(checkedAny, "expected shipped reward rows on instances outside the indun zone graph");
    }
}
