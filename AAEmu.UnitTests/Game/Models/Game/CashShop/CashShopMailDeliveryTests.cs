using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.CashShop;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.StaticValues;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.CashShop;

public sealed class CashShopMailDeliveryTests
{
    [Test]
    public async Task UncommittedDelivery_DiscardsMailAndReleasesFreshItems()
    {
        var state = CreateState();
        var delivery = CreateDelivery(state, out var createdItemIds);
        await Assert.That(delivery.Mails).HasCount().EqualTo(1);
        await Assert.That(createdItemIds.Count > 0).IsTrue();

        delivery.Dispose();

        state.Mail.DiscardUnpersisted(Any<BaseMail>()).WasCalled(Times.Once);
        state.Mail.PublishDelivered(Any<BaseMail>()).WasCalled(Times.Never);
        await Assert.That(state.Released).IsEmpty();
    }

    [Test]
    public async Task CompleteCommit_WhenPostCommitStateThrows_StillPublishesAndNeverDiscards()
    {
        var state = CreateState();
        var delivery = CreateDelivery(state, out _);
        var order = new List<string>();
        state.Mail.PublishDelivered(Any<BaseMail>()).Callback((BaseMail _) => order.Add("publish"));

        delivery.CompleteCommit(() =>
        {
            order.Add("state");
            throw new InvalidOperationException("Injected post-commit state failure.");
        });
        delivery.Dispose();

        await Assert.That(order).IsEquivalentTo(new[] { "state", "publish" });
        state.Mail.PublishDelivered(Any<BaseMail>()).WasCalled(Times.Once);
        state.Mail.DiscardUnpersisted(Any<BaseMail>()).WasCalled(Times.Never);
        await Assert.That(state.Released).IsEmpty();
    }

    [Test]
    public async Task CommittedDelivery_PublishesAfterTheTransactionAndDoesNotDiscard()
    {
        var state = CreateState();
        var delivery = CreateDelivery(state, out _);

        delivery.MarkCommitted();
        delivery.Publish();
        delivery.Dispose();

        state.Mail.PublishDelivered(Any<BaseMail>()).WasCalled(Times.Once);
        state.Mail.DiscardUnpersisted(Any<BaseMail>()).WasCalled(Times.Never);
        await Assert.That(state.Released).IsEmpty();
    }

    private static CashShopMailDelivery CreateDelivery(TestState state, out List<ulong> createdItemIds)
    {
        var plan = new CashShopPurchasePlan(
            [new CashShopPurchaseLine(1, 2, 0, 3, 5, 0, 0, CashShopCurrencyType.AaPoints, 7, "Shop item")],
            new Dictionary<CashShopCurrencyType, long> { [CashShopCurrencyType.AaPoints] = 7 },
            new Dictionary<uint, long> { [2] = 5 });
        var delivery = CashShopMailDeliveryFactory.Create(
            plan,
            new CharacterMock { Id = 10, AccountId = 1, Name = "buyer" },
            new CharacterMock { Id = 20, AccountId = 2, Name = "target" },
            state.Items.Object,
            state.Mail.Object);
        createdItemIds = state.CreatedItemIds.ToList();
        return delivery;
    }

    private static TestState CreateState()
    {
        var mail = Mock.Of<IMailManager>();
        var items = Mock.Of<IItemManager>();
        var template = new ItemTemplate { Id = 3, MaxCount = 2 };
        var live = new Dictionary<ulong, Item>();
        var created = new List<ulong>();
        var released = new List<ulong>();
        ulong nextId = 1;
        items.GetTemplate(3u).Returns(template);
        items.Create(3u, Any<int>(), Any<byte>(), true)
            .Returns((uint _, int count, byte __, bool ___) =>
            {
                var item = new Item(nextId++, template, count);
                live[item.Id] = item;
                created.Add(item.Id);
                return item;
            });
        items.GetItemByItemId(Any<ulong>())
            .Returns((ulong id) => live.GetValueOrDefault(id));
        items.ReleaseId(Any<ulong>()).Callback((ulong id) =>
        {
            released.Add(id);
            live.Remove(id);
        });
        mail.DiscardUnpersisted(Any<BaseMail>()).Callback((BaseMail pending) =>
        {
            foreach (var item in pending.Body.Attachments)
            {
                if (item?.Id > 0)
                    items.ReleaseId(item.Id);
            }
            pending.Body.Attachments.Clear();
        });
        return new TestState(mail, items, created, released);
    }

    private sealed record TestState(
        Mock<IMailManager> Mail,
        Mock<IItemManager> Items,
        List<ulong> CreatedItemIds,
        List<ulong> Released);
}
