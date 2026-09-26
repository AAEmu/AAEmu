using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Indun;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AAEmu.IntegrationTests;

/// <summary>Opt-in check against the runtime compact catalog used by a real World process.</summary>
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
    }
}
