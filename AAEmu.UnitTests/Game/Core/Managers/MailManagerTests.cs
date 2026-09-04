using System.Reflection;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class MailManagerTests
{
    private const uint ReceiverId = 42;
    private const string ReceiverName = "Receiver";

    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockMailId = Mock.Of<IMailIdManager>();
        var mockName = Mock.Of<INameManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockTask = Mock.Of<ITaskManager>();
        var mockWorld = Mock.Of<IWorldManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockLocale = Mock.Of<ILocalizationManager>();
        var manager = new MailManager(mockMailId.Object, mockName.Object, mockItem.Object, mockTask.Object, mockWorld.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockLocale.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockMailId);
        Mock.VerifyNoOtherCalls(mockName);
        Mock.VerifyNoOtherCalls(mockItem);
        Mock.VerifyNoOtherCalls(mockTask);
        Mock.VerifyNoOtherCalls(mockWorld);
        Mock.VerifyNoOtherCalls(mockHousing);
        Mock.VerifyNoOtherCalls(mockLocale);
    }

    [Test]
    public async Task ExistingItemPlan_BatchesAtWireLimit_WithoutMutatingLiveItems()
    {
        var mailIds = Mock.Of<IMailIdManager>();
        var nextMailId = 10_000u;
        mailIds.GetNextId().Returns(() => nextMailId++);
        var items = CreateItemManager();
        var manager = CreateManager(mailIds.Object, items.Object);
        var attachments = Enumerable.Range(0, MailBody.MaxMailAttachments + 1)
            .Select(index => new Item(1)
            {
                Id = (ulong)(500 + index),
                Count = index + 1,
                OwnerId = 7,
                SlotType = SlotType.System,
                Slot = 20 + index
            })
            .ToArray();
        var before = attachments.Select(item => (item.Id, item.Count, item.OwnerId, item.SlotType, item.Slot, item._holdingContainer)).ToArray();

        var created = manager.TryCreateExistingItemDeliveryPlan(
            attachments,
            (_, _) => CreateMail(),
            out var plan);

        await Assert.That(created).IsTrue();
        await Assert.That(plan.Mails.Count).IsEqualTo(2);
        await Assert.That(plan.Mails[0].Body.Attachments.Count).IsEqualTo(MailBody.MaxMailAttachments);
        await Assert.That(plan.Mails[1].Body.Attachments.Count).IsEqualTo(1);
        await Assert.That(plan.Mails[0].Id).IsEqualTo(10_000L);
        await Assert.That(plan.Mails[1].Id).IsEqualTo(10_001L);
        await Assert.That(plan.Mails.All(mail => mail.IsPendingPublish)).IsTrue();
        await Assert.That(plan.Batches[0].ItemSnapshots[9].Desired.ContainerId).IsEqualTo(0UL);
        await Assert.That(plan.Batches[0].ItemSnapshots[9].Desired.SlotType).IsEqualTo(SlotType.Mail);
        await Assert.That(plan.Batches[0].ItemSnapshots[9].Desired.Slot).IsEqualTo(9);
        await Assert.That(plan.Batches[0].ItemSnapshots[9].Desired.OwnerId).IsEqualTo((ulong)ReceiverId);
        await Assert.That(plan.Batches[1].ItemSnapshots[0].Desired.Slot).IsEqualTo(0);

        for (var i = 0; i < attachments.Length; i++)
        {
            await Assert.That(attachments[i].Id).IsEqualTo(before[i].Id);
            await Assert.That(attachments[i].Count).IsEqualTo(before[i].Count);
            await Assert.That(attachments[i].OwnerId).IsEqualTo(before[i].OwnerId);
            await Assert.That(attachments[i].SlotType).IsEqualTo(before[i].SlotType);
            await Assert.That(attachments[i].Slot).IsEqualTo(before[i].Slot);
            await Assert.That(attachments[i]._holdingContainer).IsSameReferenceAs(before[i]._holdingContainer);
        }

        plan.Dispose();

        mailIds.ReleaseId(10_000u).WasCalled(Times.Once);
        mailIds.ReleaseId(10_001u).WasCalled(Times.Once);
        items.ReleaseId(Any<ulong>()).WasCalled(Times.Never);
        await Assert.That(attachments.All(item => item.Id >= 500)).IsTrue();
        await Assert.That(plan.Mails.All(mail => mail.Id == 0 && mail.Body.Attachments.Count == 0)).IsTrue();
    }

    [Test]
    public async Task ExistingItemPlan_RejectsDuplicateItemIds_WithoutAllocatingMail()
    {
        var mailIds = Mock.Of<IMailIdManager>();
        var items = CreateItemManager();
        var manager = CreateManager(mailIds.Object, items.Object);
        var first = new Item(1) { Id = 900, Count = 1, OwnerId = 7, SlotType = SlotType.System };
        var duplicate = new Item(1) { Id = 900, Count = 2, OwnerId = 7, SlotType = SlotType.System };

        var created = manager.TryCreateExistingItemDeliveryPlan(
            [first, duplicate],
            (_, _) => CreateMail(),
            out var plan);

        await Assert.That(created).IsFalse();
        await Assert.That(plan).IsNull();
        mailIds.GetNextId().WasCalled(Times.Never);
        items.CapturePersistenceSnapshot(Any<Item>()).WasCalled(Times.Never);
    }

    [Test]
    public async Task ExistingItemPlan_FactoryFailure_RollsBackOnlyPreviouslyStagedMail()
    {
        var mailIds = Mock.Of<IMailIdManager>();
        var nextMailId = 12_000u;
        mailIds.GetNextId().Returns(() => nextMailId++);
        var items = CreateItemManager();
        var manager = CreateManager(mailIds.Object, items.Object);
        var attachments = Enumerable.Range(0, MailBody.MaxMailAttachments + 1)
            .Select(index => new Item(1)
            {
                Id = (ulong)(700 + index),
                Count = 1,
                OwnerId = 7,
                SlotType = SlotType.System,
                Slot = index
            })
            .ToArray();

        var created = manager.TryCreateExistingItemDeliveryPlan(
            attachments,
            (batch, _) => batch == 0 ? CreateMail() : throw new InvalidOperationException("factory failure"),
            out var plan);

        await Assert.That(created).IsFalse();
        await Assert.That(plan).IsNull();
        mailIds.ReleaseId(12_000u).WasCalled(Times.Once);
        await Assert.That(attachments.All(item => item.Id >= 700 && item.SlotType == SlotType.System)).IsTrue();
    }

    [Test]
    public async Task ExistingItemPlan_StageCollision_ReleasesTheUnregisteredMailId()
    {
        const uint occupiedMailId = 13_000;
        var mailIds = Mock.Of<IMailIdManager>();
        mailIds.GetNextId().Returns(occupiedMailId);
        var items = CreateItemManager();
        var manager = CreateManager(mailIds.Object, items.Object);
        manager._allPlayerMails[occupiedMailId] = CreateMail();
        var attachment = new Item(1) { Id = 950, Count = 1, OwnerId = 7, SlotType = SlotType.System };
        BaseMail failedMail = null;

        var created = manager.TryCreateExistingItemDeliveryPlan(
            [attachment],
            (_, _) => failedMail = CreateMail(),
            out var plan);

        await Assert.That(created).IsFalse();
        await Assert.That(plan).IsNull();
        mailIds.ReleaseId(occupiedMailId).WasCalled(Times.Once);
        await Assert.That(failedMail.Id).IsEqualTo(0L);
        await Assert.That(failedMail.Body.Attachments.Count).IsEqualTo(0);
        await Assert.That(attachment.Id).IsEqualTo(950UL);
        await Assert.That(attachment.SlotType).IsEqualTo(SlotType.System);
    }

    [Test]
    public async Task ExistingItemPlan_PostCommitApplyFailure_DoesNotReleaseDurableMailId()
    {
        const uint mailId = 14_000;
        var mailIds = Mock.Of<IMailIdManager>();
        mailIds.GetNextId().Returns(mailId);
        var items = CreateItemManager();
        items.ApplyCommittedSnapshot(Any<ItemPersistenceSnapshot>())
            .Throws(new InvalidOperationException("apply failure"));
        var manager = CreateManager(mailIds.Object, items.Object);
        var attachment = new Item(1) { Id = 975, Count = 1, OwnerId = 7, SlotType = SlotType.System };
        manager.TryCreateExistingItemDeliveryPlan([attachment], (_, _) => CreateMail(), out var plan);
        var stateField = typeof(ExistingItemMailDeliveryPlan)
            .GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic);
        stateField.SetValue(plan, Enum.Parse(stateField.FieldType, "Persisted"));

        Assert.Throws<InvalidOperationException>(() => plan.Commit());
        plan.Dispose();

        mailIds.ReleaseId(Any<uint>()).WasCalled(Times.Never);
        await Assert.That(plan.Mails[0].Id).IsEqualTo((long)mailId);
        await Assert.That(plan.Mails[0].Body.Attachments.Count).IsEqualTo(1);
        await Assert.That(plan.Mails[0].IsPendingPublish).IsTrue();
        await Assert.That(attachment.Id).IsEqualTo(975UL);
        await Assert.That(attachment.SlotType).IsEqualTo(SlotType.System);
    }

    private static Mock<IItemManager> CreateItemManager()
    {
        var items = Mock.Of<IItemManager>();
        items.CapturePersistenceSnapshot(Any<Item>())
            .Returns((Item item) => ItemPersistenceSnapshot.Capture(item));
        return items;
    }

    private static MailManager CreateManager(IMailIdManager mailIds, IItemManager items)
    {
        var names = Mock.Of<INameManager>();
        names.GetCharacterName(ReceiverId).Returns(ReceiverName);
        names.GetCharacterId(ReceiverName).Returns(ReceiverId);
        return new MailManager(
            mailIds,
            names.Object,
            items,
            Mock.Of<ITaskManager>().Object,
            Mock.Of<IWorldManager>().Object,
            new Lazy<IHousingManager>(() => Mock.Of<IHousingManager>().Object),
            Mock.Of<ILocalizationManager>().Object);
    }

    [Test]
    public async Task SendBatch_InvalidSecondReceiver_PublishesNoMail()
    {
        var nameManager = Mock.Of<INameManager>();
        nameManager.GetCharacterName(1).Returns("seller");
        nameManager.GetCharacterId("seller").Returns(1u);
        nameManager.GetCharacterName(2).Returns("crafter");
        nameManager.GetCharacterId("wrong-name").Returns(0u);
        var manager = CreateManager(nameManager);
        var sellerMail = CreateMail(1001, 1, "seller");
        var invalidCrafterMail = CreateMail(1002, 2, "wrong-name");

        var sent = manager.SendBatch([sellerMail, invalidCrafterMail]);

        await Assert.That(sent).IsFalse();
        await Assert.That(manager.AllPlayerMails).IsEmpty();
    }

    [Test]
    public async Task SendBatch_ValidBatch_PublishesEveryMail()
    {
        var nameManager = Mock.Of<INameManager>();
        nameManager.GetCharacterName(1).Returns("seller");
        nameManager.GetCharacterId("seller").Returns(1u);
        nameManager.GetCharacterName(2).Returns("crafter");
        nameManager.GetCharacterId("crafter").Returns(2u);
        var manager = CreateManager(nameManager);
        var sellerMail = CreateMail(1001, 1, "seller");
        var crafterMail = CreateMail(1002, 2, "crafter");

        var sent = manager.SendBatch([sellerMail, crafterMail]);

        await Assert.That(sent).IsTrue();
        await Assert.That(manager.AllPlayerMails.Count).IsEqualTo(2);
        await Assert.That(manager.AllPlayerMails[1001]).IsSameReferenceAs(sellerMail);
        await Assert.That(manager.AllPlayerMails[1002]).IsSameReferenceAs(crafterMail);
    }

    [Test]
    public async Task SendBatch_ExistingId_PublishesNoPartOfBatch()
    {
        var nameManager = Mock.Of<INameManager>();
        nameManager.GetCharacterName(1).Returns("seller");
        nameManager.GetCharacterId("seller").Returns(1u);
        var manager = CreateManager(nameManager);
        var existing = CreateMail(1002, 1, "seller");
        manager._allPlayerMails.Add(existing.Id, existing);

        var sent = manager.SendBatch([
            CreateMail(1001, 1, "seller"),
            CreateMail(1002, 1, "seller")
        ]);

        await Assert.That(sent).IsFalse();
        await Assert.That(manager.AllPlayerMails.Count).IsEqualTo(1);
        await Assert.That(manager.AllPlayerMails[1002]).IsSameReferenceAs(existing);
    }

    [Test]
    public async Task TryPrepareBatch_ValidBatch_DoesNotPublishUntilCommitted()
    {
        var nameManager = Mock.Of<INameManager>();
        nameManager.GetCharacterName(1).Returns("seller");
        nameManager.GetCharacterId("seller").Returns(1u);
        var manager = CreateManager(nameManager);
        var mail = CreateMail(1001, 1, "seller");

        var prepared = manager.TryPrepareBatch([mail], out var batch);

        await Assert.That(prepared).IsTrue();
        await Assert.That(manager.AllPlayerMails).IsEmpty();
        await Assert.That(manager.PublishPreparedBatch(batch, true)).IsTrue();
        await Assert.That(manager.AllPlayerMails[1001]).IsSameReferenceAs(mail);
        await Assert.That(mail.IsDirty).IsFalse();
    }

    [Test]
    public async Task CancelPreparedBatch_ReleasesReservationWithoutPublishing()
    {
        var nameManager = Mock.Of<INameManager>();
        nameManager.GetCharacterName(1).Returns("seller");
        nameManager.GetCharacterId("seller").Returns(1u);
        var manager = CreateManager(nameManager);
        var first = CreateMail(1001, 1, "seller");
        var second = CreateMail(1001, 1, "seller");

        await Assert.That(manager.TryPrepareBatch([first], out var cancelled)).IsTrue();
        manager.CancelPreparedBatch(cancelled);

        await Assert.That(manager.AllPlayerMails).IsEmpty();
        await Assert.That(manager.TryPrepareBatch([second], out var replacement)).IsTrue();
        manager.CancelPreparedBatch(replacement);
    }

    private static MailManager CreateManager(Mock<INameManager> nameManager)
    {
        var manager = new MailManager(
            Mock.Of<IMailIdManager>().Object,
            nameManager.Object,
            Mock.Of<IItemManager>().Object,
            Mock.Of<ITaskManager>().Object,
            Mock.Of<IWorldManager>().Object,
            new Lazy<IHousingManager>(() => Mock.Of<IHousingManager>().Object),
            Mock.Of<ILocalizationManager>().Object);
        manager._allPlayerMails = [];
        return manager;
    }

    private static BaseMail CreateMail() => new()
    {
        MailType = MailType.Normal,
        Title = "Returned farmhand items",
        ReceiverName = ReceiverName,
        Header =
        {
            SenderId = ReceiverId,
            SenderName = ReceiverName,
            ReceiverId = ReceiverId,
            Status = MailStatus.Unread
        },
        Body =
        {
            Text = "Farmhand storage return",
            SendDate = DateTime.UtcNow,
            RecvDate = DateTime.UtcNow
        }
    };
    private static BaseMail CreateMail(long id, uint receiverId, string receiverName)
    {
        return new BaseMail
        {
            Id = id,
            ReceiverName = receiverName,
            Header =
            {
                ReceiverId = receiverId
            },
            Body = { RecvDate = DateTime.UtcNow.AddHours(1) }
        };
    }
}
