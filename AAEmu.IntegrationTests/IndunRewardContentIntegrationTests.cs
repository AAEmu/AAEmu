using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Indun;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AAEmu.IntegrationTests;

/// <summary>Opt-in check against the runtime compact catalog used by a real World process.</summary>
[Collection(IndunRewardContentCollection.Name)]
public sealed class IndunRewardContentIntegrationTests
{
    private const string EnvironmentVariable = "AAEMU_INDUN_REWARD_TEST_CONTENT";

    [Fact]
    public void RuntimeCompactLoadsTypedRewardKindsAndMailKinds()
    {
        var contentPath = Environment.GetEnvironmentVariable(EnvironmentVariable);
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(contentPath) && File.Exists(contentPath),
            $"Set {EnvironmentVariable} to a runtime compact.sqlite3 path.");

        using var connection = new SqliteConnection($"Data Source=file:{contentPath};Mode=ReadOnly");
        connection.Open();

        var data = IndunGameData.Instance;
        data.Load(connection);

        using var rewardCommand = connection.CreateCommand();
        rewardCommand.CommandText = "SELECT instance_reward_kind_id FROM instance_rewards ORDER BY id LIMIT 1";
        var rewardKindId = Convert.ToUInt32(rewardCommand.ExecuteScalar());
        Assert.False(string.IsNullOrWhiteSpace(data.GetInstanceRewardKindName(rewardKindId)));

        using var mailTextCommand = connection.CreateCommand();
        mailTextCommand.CommandText = "SELECT instance_id FROM instance_reward_mail_texts ORDER BY id LIMIT 1";
        var mailTextInstanceId = Convert.ToUInt32(mailTextCommand.ExecuteScalar());
        var mailText = data.GetInstanceRewardMailText(mailTextInstanceId);
        Assert.True(Enum.IsDefined(mailText.MailKind));

        using var bonusCommand = connection.CreateCommand();
        bonusCommand.CommandText = @"SELECT b.instance_reward_id
                                     FROM instance_reward_bonus_counts b
                                     JOIN instance_rewards r ON r.id=b.instance_reward_id
                                     JOIN buffs f ON f.id=b.buff_id
                                     WHERE b.count > 0
                                     ORDER BY b.id
                                     LIMIT 1";
        var bonusRewardId = Convert.ToUInt32(bonusCommand.ExecuteScalar());
        var bonusCounts = data.GetInstanceRewardBonusCounts(bonusRewardId);
        Assert.NotEmpty(bonusCounts);
        Assert.All(bonusCounts, bonus =>
        {
            Assert.Equal(bonusRewardId, bonus.InstanceRewardId);
            Assert.True(bonus.BuffId > 0);
            Assert.True(bonus.Count > 0);
        });
    }

    [Fact]
    public void RuntimeCompactReportsEveryRejectedOrphanBonusRow()
    {
        var contentPath = Environment.GetEnvironmentVariable(EnvironmentVariable);
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(contentPath) && File.Exists(contentPath),
            $"Set {EnvironmentVariable} to a runtime compact.sqlite3 path.");

        using var connection = new SqliteConnection($"Data Source=file:{contentPath};Mode=ReadOnly");
        connection.Open();
        IndunGameData.Instance.Load(connection);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT b.id, b.instance_reward_id
                                 FROM instance_reward_bonus_counts b
                                 LEFT JOIN instance_rewards r ON r.id=b.instance_reward_id
                                 WHERE r.id IS NULL
                                 ORDER BY b.id";
        var expectedRows = new List<(uint Id, uint RewardId)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                expectedRows.Add((Convert.ToUInt32(reader.GetValue(0)), Convert.ToUInt32(reader.GetValue(1))));
        }

        var diagnostics = IndunGameData.Instance.InstanceRewardBonusDiagnostics;
        Assert.Equal(expectedRows.Select(row => row.Id), diagnostics.OrphanRowIds);
        Assert.Equal(expectedRows.Select(row => row.RewardId).Distinct(), diagnostics.OrphanRewardIds);
    }
}
