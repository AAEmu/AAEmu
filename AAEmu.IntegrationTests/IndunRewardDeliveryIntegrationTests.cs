using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Mails;
using Moq;
using MySql.Data.MySqlClient;
using Xunit;

namespace AAEmu.IntegrationTests;

public sealed class IndunRewardDeliveryIntegrationTests(IndunRewardMySqlFixture fixture) : IClassFixture<IndunRewardMySqlFixture>
{
    // Synthetic fixture values; no shipped content IDs are used by this test.
    private const uint InstanceId = 41;
    private const uint RewardKindId = 6;
    private const uint TargetItemId = 73;
    private const uint FirstRecipientId = 101;
    private const uint SecondRecipientId = 202;
    private static long _nextMailId;

    private static readonly IReadOnlyList<InstanceReward> Rewards =
    [
        new(1, InstanceId, RewardKindId, 1, 1, 1, false, TargetItemId, InstanceRewardTargetType.Item, false, false)
    ];

    private static readonly IReadOnlyList<InstanceRewardBonusCount> BonusCounts =
    [
        new(1, Rewards[0].Id, 9001, 1),
        new(2, Rewards[0].Id, 9002, 2)
    ];

    private static readonly InstanceRewardMailText MailText =
        new(1, InstanceId, "fixture", "title", "body", 1, InstanceRewardMailKind.Basic);

    [Fact]
    public void CommitPersistsClaimAndMailInTheSameTransaction()
    {
        SkipUnlessEnabled();
        var mail = new MailHarness { RunId = "commit-run" };
        var service = CreateService(mail);

        var result = service.DeliverForRun("commit-run", InstanceId, RewardKindId, 1,
            [new IndunRewardRecipient(FirstRecipientId, "first")], Rewards, MailText);

        Assert.Equal(IndunRewardDeliveryResult.Delivered, result);
        using var connection = fixture.Open();
        Assert.Equal(1, Count(connection, "indun_reward_claims", "run_id=@value", "commit-run"));
        Assert.Equal(1, CountMail(connection, "commit-run"));
    }

    [Fact]
    public void CommitPersistsTypedBonusCountsInTheSameTransaction()
    {
        SkipUnlessEnabled();
        var mail = new MailHarness { RunId = "bonus-run" };
        var service = CreateService(mail);

        var result = service.DeliverForRun("bonus-run", InstanceId, RewardKindId, 1,
            [new IndunRewardRecipient(FirstRecipientId, "first")], Rewards, MailText, BonusCounts);

        Assert.Equal(IndunRewardDeliveryResult.Delivered, result);
        using var connection = fixture.Open();
        Assert.Equal(1, Count(connection, "indun_reward_claims", "run_id=@value", "bonus-run"));
        Assert.Equal(1, CountMail(connection, "bonus-run"));
        Assert.Equal(2, CountBonusGrants(connection, "bonus-run", FirstRecipientId));
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT buff_id, bonus_count FROM indun_reward_bonus_grants WHERE run_id='bonus-run' ORDER BY buff_id";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(9001L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.True(reader.Read());
        Assert.Equal(9002L, reader.GetInt64(0));
        Assert.Equal(2L, reader.GetInt64(1));
    }

    [Fact]
    public void AmbiguousCommitRetryDoesNotDuplicateBonusCounts()
    {
        SkipUnlessEnabled();
        var firstMail = new MailHarness { RunId = "bonus-ambiguous-run" };
        var first = CreateService(firstMail, transaction =>
        {
            transaction.Commit();
            throw new InvalidOperationException("simulated ambiguous commit");
        });
        var firstResult = first.DeliverForRun("bonus-ambiguous-run", InstanceId, RewardKindId, 1,
            [new IndunRewardRecipient(FirstRecipientId, "first")], Rewards, MailText, BonusCounts);
        Assert.Equal(IndunRewardDeliveryResult.Delivered, firstResult);

        var retryMail = new MailHarness { RunId = "bonus-ambiguous-run" };
        var retry = CreateService(retryMail);
        var retryResult = retry.DeliverForRun("bonus-ambiguous-run", InstanceId, RewardKindId, 1,
            [new IndunRewardRecipient(FirstRecipientId, "first")], Rewards, MailText, BonusCounts);

        Assert.Equal(IndunRewardDeliveryResult.AlreadyClaimed, retryResult);
        Assert.Equal(0, retryMail.PublishCalls);
        using var connection = fixture.Open();
        Assert.Equal(1, CountMail(connection, "bonus-ambiguous-run"));
        Assert.Equal(2, CountBonusGrants(connection, "bonus-ambiguous-run", FirstRecipientId));
    }

    [Fact]
    public void AmbiguousCommitWithMismatchedBonusRowsStaysUnpublished()
    {
        SkipUnlessEnabled();
        var mail = new MailHarness { RunId = "bonus-mismatch-run" };
        var service = CreateService(mail, transaction =>
        {
            var connection = transaction.Connection;
            transaction.Commit();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM indun_reward_bonus_grants WHERE run_id='bonus-mismatch-run' AND buff_id=9002";
            command.ExecuteNonQuery();
            throw new InvalidOperationException("simulated ambiguous commit with a mismatched grant");
        });

        var result = service.DeliverForRun("bonus-mismatch-run", InstanceId, RewardKindId, 1,
            [new IndunRewardRecipient(FirstRecipientId, "first")], Rewards, MailText, BonusCounts);

        Assert.Equal(IndunRewardDeliveryResult.Failed, result);
        Assert.Equal(0, mail.PublishCalls);
        using var connectionAfter = fixture.Open();
        Assert.Equal(1, Count(connectionAfter, "indun_reward_claims", "run_id=@value", "bonus-mismatch-run"));
        Assert.Equal(1, CountMail(connectionAfter, "bonus-mismatch-run"));
        Assert.Equal(1, CountBonusGrants(connectionAfter, "bonus-mismatch-run", FirstRecipientId));
    }

    [Fact]
    public void MailFailureRollsBackClaimMailAndBonusCounts()
    {
        SkipUnlessEnabled();
        var mail = new MailHarness { RunId = "bonus-rollback-run" };
        mail.Deliver = (message, connection, transaction) =>
        {
            mail.WriteMailRow(message, connection, transaction);
            return false;
        };
        var service = CreateService(mail);

        var result = service.DeliverForRun("bonus-rollback-run", InstanceId, RewardKindId, 1,
            [new IndunRewardRecipient(FirstRecipientId, "first")], Rewards, MailText, BonusCounts);

        Assert.Equal(IndunRewardDeliveryResult.Failed, result);
        using var connection = fixture.Open();
        Assert.Equal(0, Count(connection, "indun_reward_claims", "run_id=@value", "bonus-rollback-run"));
        Assert.Equal(0, CountMail(connection, "bonus-rollback-run"));
        Assert.Equal(0, CountBonusGrants(connection, "bonus-rollback-run", FirstRecipientId));
    }

    [Fact]
    public void MailFailureRollsBackClaimAndMailMarker()
    {
        SkipUnlessEnabled();
        var mail = new MailHarness { RunId = "rollback-run" };
        mail.Deliver = (message, connection, transaction) =>
        {
            mail.WriteMailRow(message, connection, transaction);
            return false;
        };
        var service = CreateService(mail);

        var result = service.DeliverForRun("rollback-run", InstanceId, RewardKindId, 1,
            [new IndunRewardRecipient(FirstRecipientId, "first")], Rewards, MailText);

        Assert.Equal(IndunRewardDeliveryResult.Failed, result);
        using var connection = fixture.Open();
        Assert.Equal(0, Count(connection, "indun_reward_claims", "run_id=@value", "rollback-run"));
        Assert.Equal(0, CountMail(connection, "rollback-run"));
    }

    [Fact]
    public void AmbiguousCommitLeavesOneDurableClaimAndRetryIsRefused()
    {
        SkipUnlessEnabled();
        var firstMail = new MailHarness { RunId = "ambiguous-run" };
        var first = CreateService(firstMail, transaction =>
        {
            transaction.Commit();
            throw new InvalidOperationException("simulated ambiguous commit");
        });

        var firstResult = first.DeliverForRun("ambiguous-run", InstanceId, RewardKindId, 1,
            [new IndunRewardRecipient(FirstRecipientId, "first")], Rewards, MailText);
        Assert.Equal(IndunRewardDeliveryResult.Delivered, firstResult);
        Assert.Equal(1, firstMail.PublishCalls);

        var retryMail = new MailHarness { RunId = "ambiguous-run" };
        var retry = CreateService(retryMail);
        var retryResult = retry.DeliverForRun("ambiguous-run", InstanceId, RewardKindId, 1,
            [new IndunRewardRecipient(FirstRecipientId, "first")], Rewards, MailText);

        Assert.Equal(IndunRewardDeliveryResult.AlreadyClaimed, retryResult);
        Assert.Equal(0, retryMail.PublishCalls);
        using var connection = fixture.Open();
        Assert.Equal(1, Count(connection, "indun_reward_claims", "run_id=@value", "ambiguous-run"));
        Assert.Equal(1, CountMail(connection, "ambiguous-run"));
    }

    [Fact]
    public async Task ConcurrentClaimsForOneRunAndRecipientProduceOneMail()
    {
        SkipUnlessEnabled();
        var mail = new MailHarness { RunId = "concurrent-run" };
        var service = CreateService(mail);
        var recipient = new[] { new IndunRewardRecipient(FirstRecipientId, "first") };

        var results = await Task.WhenAll(
            Task.Run(() => service.DeliverForRun("concurrent-run", InstanceId, RewardKindId, 1, recipient, Rewards, MailText)),
            Task.Run(() => service.DeliverForRun("concurrent-run", InstanceId, RewardKindId, 1, recipient, Rewards, MailText)));

        Assert.Equal(1, results.Count(result => result == IndunRewardDeliveryResult.Delivered));
        Assert.Equal(1, results.Count(result => result == IndunRewardDeliveryResult.AlreadyClaimed));
        using var connection = fixture.Open();
        Assert.Equal(1, Count(connection, "indun_reward_claims", "run_id=@value", "concurrent-run"));
        Assert.Equal(1, CountMail(connection, "concurrent-run"));
    }

    [Fact]
    public void PartialRecipientsRetryOnlyTheFailedRecipient()
    {
        SkipUnlessEnabled();
        var failedOnce = 0;
        var mail = new MailHarness { RunId = "partial-run" };
        mail.Deliver = (message, connection, transaction) =>
        {
            if (message.Header.ReceiverId == SecondRecipientId && Interlocked.Exchange(ref failedOnce, 1) == 0)
                return false;
            return mail.WriteMailRow(message, connection, transaction);
        };
        var service = CreateService(mail);
        var recipients = new[]
        {
            new IndunRewardRecipient(FirstRecipientId, "first"),
            new IndunRewardRecipient(SecondRecipientId, "second")
        };

        var first = service.DeliverForRun("partial-run", InstanceId, RewardKindId, 1, recipients, Rewards, MailText);
        Assert.Equal(IndunRewardDeliveryResult.Failed, first);
        using (var afterFirst = fixture.Open())
        {
            Assert.Equal(1, Count(afterFirst, "indun_reward_claims", "run_id=@value", "partial-run"));
            Assert.Equal(1, CountMail(afterFirst, "partial-run"));
        }

        var second = service.DeliverForRun("partial-run", InstanceId, RewardKindId, 1, recipients, Rewards, MailText);
        Assert.Equal(IndunRewardDeliveryResult.Delivered, second);
        using var afterRetry = fixture.Open();
        Assert.Equal(2, Count(afterRetry, "indun_reward_claims", "run_id=@value", "partial-run"));
        Assert.Equal(2, CountMail(afterRetry, "partial-run"));
    }

    [Fact]
    public void PublishFailureDoesNotUndoCommittedClaimOrMail()
    {
        SkipUnlessEnabled();
        var mail = new MailHarness
        {
            RunId = "publish-run",
            OnPublish = _ => throw new InvalidOperationException("simulated publish failure")
        };
        var service = CreateService(mail);

        var result = service.DeliverForRun("publish-run", InstanceId, RewardKindId, 1,
            [new IndunRewardRecipient(FirstRecipientId, "first")], Rewards, MailText);

        Assert.Equal(IndunRewardDeliveryResult.Delivered, result);
        Assert.Equal(1, mail.PublishCalls);
        using var connection = fixture.Open();
        Assert.Equal(1, Count(connection, "indun_reward_claims", "run_id=@value", "publish-run"));
        Assert.Equal(1, CountMail(connection, "publish-run"));
    }

    [Fact]
    public void RestartWithTheSamePersistedRunIdRefusesDuplicateButNewRunIsNewLogicalCopy()
    {
        SkipUnlessEnabled();
        var firstMail = new MailHarness { RunId = "rehydrated-run" };
        var first = CreateService(firstMail);
        Assert.Equal(IndunRewardDeliveryResult.Delivered, first.DeliverForRun("rehydrated-run", InstanceId, RewardKindId, 1,
            [new IndunRewardRecipient(FirstRecipientId, "first")], Rewards, MailText));

        var secondMail = new MailHarness { RunId = "rehydrated-run" };
        var second = CreateService(secondMail);
        Assert.Equal(IndunRewardDeliveryResult.AlreadyClaimed, second.DeliverForRun("rehydrated-run", InstanceId, RewardKindId, 1,
            [new IndunRewardRecipient(FirstRecipientId, "first")], Rewards, MailText));
        secondMail.RunId = "new-live-run";
        Assert.Equal(IndunRewardDeliveryResult.Delivered, second.DeliverForRun("new-live-run", InstanceId, RewardKindId, 1,
            [new IndunRewardRecipient(FirstRecipientId, "first")], Rewards, MailText));

        using var connection = fixture.Open();
        Assert.Equal(1, Count(connection, "indun_reward_claims", "run_id=@value", "rehydrated-run"));
        Assert.Equal(1, Count(connection, "indun_reward_claims", "run_id=@value", "new-live-run"));
        Assert.Equal(1, CountMail(connection, "rehydrated-run"));
        Assert.Equal(1, CountMail(connection, "new-live-run"));
    }

    private IndunRewardDeliveryService CreateService(MailHarness mail, Action<MySqlTransaction> commit = null)
    {
        var itemManager = new Mock<IItemManager>();
        var template = new ItemTemplate { Id = TargetItemId, FixedGrade = 0 };
        var nextItemId = 0L;
        itemManager.Setup(manager => manager.GetTemplate(TargetItemId)).Returns(template);
        itemManager.Setup(manager => manager.Create(It.IsAny<uint>(), It.IsAny<int>(), It.IsAny<byte>(), It.IsAny<bool>()))
            .Returns((uint templateId, int count, byte grade, bool generateId) =>
                new Item(1, (ulong)Interlocked.Increment(ref nextItemId), template, count));

        return new IndunRewardDeliveryService(
            () =>
            {
                var connection = new MySqlConnection(fixture.ConnectionString);
                connection.Open();
                return connection;
            },
            mail.CreateMock().Object,
            itemManager.Object,
            commit);
    }

    private void SkipUnlessEnabled() =>
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_INDUN_REWARD_TEST_MYSQL to run the isolated MySQL fixture.");

    private static int CountMail(MySqlConnection connection, string runId) =>
        Count(connection, "indun_reward_test_mail", "run_id=@value", runId);

    private static int CountBonusGrants(MySqlConnection connection, string runId, uint characterId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM indun_reward_bonus_grants WHERE run_id=@run_id AND character_id=@character_id";
        command.Parameters.AddWithValue("@run_id", runId);
        command.Parameters.AddWithValue("@character_id", characterId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static int Count(MySqlConnection connection, string table, string predicate = null, object value = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM `{table}`" + (predicate == null ? string.Empty : $" WHERE {predicate}");
        if (value != null)
            command.Parameters.AddWithValue("@value", value);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private sealed class MailHarness
    {
        public string RunId { get; set; } = string.Empty;
        public Func<BaseMail, MySqlConnection, MySqlTransaction, bool> Deliver { get; set; }
        public Action<BaseMail> OnPublish { get; set; }
        public int PublishCalls { get; private set; }

        public Mock<IMailManager> CreateMock()
        {
            var mock = new Mock<IMailManager>();
            mock.Setup(manager => manager.TryDeliverOn(It.IsAny<BaseMail>(), It.IsAny<MySqlConnection>(), It.IsAny<MySqlTransaction>()))
                .Returns((BaseMail mail, MySqlConnection connection, MySqlTransaction transaction) =>
                    Deliver?.Invoke(mail, connection, transaction) ?? WriteMailRow(mail, connection, transaction));
            mock.Setup(manager => manager.PublishDelivered(It.IsAny<BaseMail>()))
                .Callback<BaseMail>(mail =>
                {
                    PublishCalls++;
                    OnPublish?.Invoke(mail);
                });
            return mock;
        }

        public bool WriteMailRow(BaseMail mail, MySqlConnection connection, MySqlTransaction transaction)
        {
            mail.Id = Interlocked.Increment(ref _nextMailId);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO indun_reward_test_mail(mail_id, run_id, receiver_id) VALUES(@mail_id, @run_id, @receiver_id)";
            command.Parameters.AddWithValue("@mail_id", mail.Id);
            command.Parameters.AddWithValue("@run_id", RunId);
            command.Parameters.AddWithValue("@receiver_id", mail.Header.ReceiverId);
            command.ExecuteNonQuery();
            return true;
        }
    }
}
