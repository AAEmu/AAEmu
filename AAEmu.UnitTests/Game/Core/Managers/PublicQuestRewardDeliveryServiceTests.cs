using System.Runtime.CompilerServices;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Quests;
using MySql.Data.MySqlClient;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class PublicQuestRewardDeliveryServiceTests
{
    [Test]
    public async Task DisposeBeforeCommit_DiscardsStagedMailAndReleasesFreshItems()
    {
        var state = CreateService();
        using var connection = new MySqlConnection();
        var transaction = UninitializedTransaction();

        var staged = state.Service.TryStageRewards(Bundle(), [new(10, "Alice")], connection, transaction,
            out var delivery);
        delivery.Dispose();

        await Assert.That(staged).IsTrue();
        state.Mail.DiscardUnpersisted(Any<BaseMail>()).WasCalled(Times.Once);
        await Assert.That(state.Released).IsEquivalentTo([1ul]);
        state.Mail.PublishDelivered(Any<BaseMail>()).WasCalled(Times.Never);
    }

    [Test]
    public async Task Publish_WhenOneMailThrows_PublishesOthersAndRetriesOnlyTheFailure()
    {
        var state = CreateService();
        var attempts = new Dictionary<uint, int>();
        var published = new List<uint>();
        state.Mail.PublishDelivered(Any<BaseMail>()).Callback((BaseMail mail) =>
        {
            var receiverId = mail.Header.ReceiverId;
            attempts[receiverId] = attempts.GetValueOrDefault(receiverId) + 1;
            if (receiverId == 10 && attempts[receiverId] == 1)
                throw new InvalidOperationException("Controlled notification failure.");
            published.Add(receiverId);
        });
        using var connection = new MySqlConnection();
        var transaction = UninitializedTransaction();
        await Assert.That(state.Service.TryStageRewards(
            Bundle(), [new(10, "Alice"), new(11, "Bob")], connection, transaction, out var delivery)).IsTrue();

        delivery.MarkCommitted();
        delivery.Publish();
        delivery.Publish();
        delivery.Dispose();

        state.Mail.PublishDelivered(Any<BaseMail>()).WasCalled(Times.Exactly(3));
        await Assert.That(published).IsEquivalentTo([10u, 11u]);
        await Assert.That(attempts[10]).IsEqualTo(2);
        await Assert.That(attempts[11]).IsEqualTo(1);
        state.Mail.DiscardUnpersisted(Any<BaseMail>()).WasCalled(Times.Never);
        await Assert.That(state.Released).IsEmpty();
    }

    [Test]
    public async Task StagingFailure_RollsBackPriorMailAndEveryFreshItem()
    {
        var state = CreateService();
        var stagingAttempt = 0;
        state.Mail.TryDeliverOn(Any<BaseMail>(), Any<MySqlConnection>(), Any<MySqlTransaction>())
            .Returns((BaseMail _, MySqlConnection _, MySqlTransaction _) => ++stagingAttempt == 1);
        using var connection = new MySqlConnection();
        var transaction = UninitializedTransaction();

        var staged = state.Service.TryStageRewards(Bundle(), [new(10, "Alice"), new(11, "Bob")],
            connection, transaction, out var delivery);

        await Assert.That(staged).IsFalse();
        await Assert.That(delivery).IsNull();
        state.Mail.DiscardUnpersisted(Any<BaseMail>()).WasCalled(Times.Once);
        await Assert.That(state.Released).IsEquivalentTo([1ul, 2ul]);
        state.Mail.PublishDelivered(Any<BaseMail>()).WasCalled(Times.Never);
    }

    private static TestState CreateService()
    {
        var mail = Mock.Of<IMailManager>();
        mail.TryDeliverOn(Any<BaseMail>(), Any<MySqlConnection>(), Any<MySqlTransaction>()).Returns(true);
        var items = Mock.Of<IItemManager>();
        var template = new ItemTemplate { Id = 9001, MaxCount = 100 };
        var live = new Dictionary<ulong, Item>();
        var released = new List<ulong>();
        ulong nextId = 1;
        items.GetTemplate(template.Id).Returns(template);
        items.Create(template.Id, Any<int>(), Any<byte>(), true)
            .Returns((uint unusedTemplateId, int count, byte unusedGrade, bool unusedGenerateId) =>
            {
                var item = new Item(nextId++, template, count);
                live[item.Id] = item;
                return item;
            });
        items.GetItemByItemId(Any<ulong>())
            .Returns((ulong id) => live.GetValueOrDefault(id));
        items.ReleaseId(Any<ulong>()).Callback((ulong id) =>
        {
            released.Add(id);
            live.Remove(id);
        });
        return new TestState(new PublicQuestRewardDeliveryService(mail.Object, items.Object), mail, released);
    }

    private static PublicQuestRewardBundle Bundle() =>
        new(7001, "Assignment", [new PublicQuestItemReward(9001, 1, 0)], 1600, 1600);

    private static MySqlTransaction UninitializedTransaction() =>
        (MySqlTransaction)RuntimeHelpers.GetUninitializedObject(typeof(MySqlTransaction));

    private sealed record TestState(
        PublicQuestRewardDeliveryService Service,
        Mock<IMailManager> Mail,
        List<ulong> Released);
}
