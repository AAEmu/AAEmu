using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.UnitTests.Utils.Mocks;
using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.UnitTests.Game.Core.Managers;

[NotInParallel]
public sealed class MailTests
{
    private CharacterMock _character;
    private CharacterMails _mails;
    private MailManager _mailManager;
    private Mock<IWorldManager> _mockWorldManager;

    [Before(Test)]
    public void Setup()
    {
        _character = new CharacterMock { AccountId = 1, Id = 1, Name = "tester", Money = 1000 };

        _mails = new CharacterMails(_character);

        var nameManager = new NameManager();
        nameManager.Load([], [], []);
        nameManager.AddCharacter(_character.Id, _character.Name, 1);
        nameManager.AddCharacter(2u, "Sender", 1);

        var mailIdManager = new MailIdManager();
        mailIdManager.Initialize();

        _mockWorldManager = Mock.Of<IWorldManager>();
        _mailManager = new MailManager(
            mailIdManager,
            nameManager,
            Mock.Of<IItemManager>().Object,
            Mock.Of<ITaskManager>().Object,
            _mockWorldManager.Object,
            new Lazy<IHousingManager>(() => Mock.Of<IHousingManager>().Object),
            Mock.Of<ILocalizationManager>().Object);

        // Reset singleton caches so Instance properties resolve via ServiceProvider
        typeof(Singleton<MailManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, null);
        typeof(Singleton<NameManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, null);

        var services = new ServiceCollection();
        services.AddSingleton(_mailManager);
        services.AddSingleton(nameManager);
        SingletonContainer.ServiceProvider = services.BuildServiceProvider();

        _mailManager._allPlayerMails = [];
    }

    [After(Test)]
    public void Teardown()
    {
        _mailManager._allPlayerMails = null;
        _character = null;
        _mails = null;
        _mailManager = null;
        _mockWorldManager = null;

        SingletonContainer.ServiceProvider = null;
        typeof(Singleton<MailManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, null);
        typeof(Singleton<NameManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, null);
    }

    [Test]
    public async Task MoneyTest()
    {
        var type = MailType.Express;
        var receiverCharName = "tester".NormalizeName();
        var title = "test";
        var text = "test";
        var attachments = (byte)0;
        var money0 = 500ul;
        var money1 = 0ul;
        var money2 = 0ul;
        var money3 = 0u;
        var extra = 0;
        var itemSlots = new List<(SlotType slotType, byte slot)>();

        await Assert.That(_mails.SendMailToPlayer(type, receiverCharName, title, text, attachments, money0, money1, money2, money3, extra, itemSlots)).IsEqualTo(MailResult.Success);
        await Assert.That(_character.Money).IsEqualTo(400);
    }

    [Test]
    public async Task PlayerNotFoundTest()
    {

        var type = MailType.Express;
        var receiverCharName = "bob";
        var title = "test";
        var text = "test";
        var attachments = (byte)0;
        var money0 = 500ul;
        var money1 = 0ul;
        var money2 = 0ul;
        var money3 = 0u;
        var extra = 0;
        var itemSlots = new List<(SlotType slotType, byte slot)>();

        await Assert.That(_mails.SendMailToPlayer(type, receiverCharName, title, text, attachments, money0, money1, money2, money3, extra, itemSlots)).IsNotEqualTo(MailResult.Success);
        await Assert.That(_character.Money).IsEqualTo(1000);
    }

    private BaseMail SeedInboxMail(long id, MailStatus status = MailStatus.Unread)
    {
        var now = DateTime.UtcNow;
        var mail = new BaseMail
        {
            Id = id,
            MailType = MailType.Normal,
            Title = "test",
            ReceiverName = _character.Name,
            OpenDate = now,
        };
        mail.Header.SenderId = 2u;
        mail.Header.SenderName = "Sender";
        mail.Header.ReceiverId = _character.Id;
        mail.Header.Status = status;
        mail.Body.Text = "test";
        mail.Body.SendDate = now.AddMinutes(-31);
        mail.Body.RecvDate = now.AddMinutes(-1);
        _mailManager.AllPlayerMails[id] = mail;
        return mail;
    }

    private void MakeReturnable()
    {
        // TryReturnToSenderCore refreshes the returner's counters through the world character,
        // so the mock world has to resolve it as online. CharacterMock never runs DB Load (the
        // only place Character.Mails is assigned), so wire it explicitly as well.
        _character.Mails = new CharacterMails(_character);
        _mockWorldManager.GetCharacterById(Any<uint>()).Returns(_character);
        _character.IsOnline = true;
    }

    [Test]
    public async Task ReturnMail_LastUnreadLetter_CountsGoToZero()
    {
        MakeReturnable();
        var mail = SeedInboxMail(5001L);
        _character.Mails.RefreshAllMailCounts();
        await Assert.That(_character.Mails.UnreadMailCount.TotalReceived).IsEqualTo(1);
        await Assert.That(_character.Mails.UnreadMailCount.Received).IsEqualTo(1);

        _character.Mails.ReturnMail(5001L);

        // Returning turns the letter around; it is not deleted.
        await Assert.That(mail.Header.ReceiverId).IsEqualTo(2u);
        await Assert.That(mail.Header.Returned).IsTrue();
        await Assert.That(_character.Mails.UnreadMailCount.TotalReceived).IsEqualTo(0);
        await Assert.That(_character.Mails.UnreadMailCount.Received).IsEqualTo(0);
    }

    [Test]
    public async Task ReturnMail_ReadLetter_TotalDecrementsUnreadUnchanged()
    {
        MakeReturnable();
        SeedInboxMail(5002L, MailStatus.Read);
        _character.Mails.RefreshAllMailCounts();
        await Assert.That(_character.Mails.UnreadMailCount.TotalReceived).IsEqualTo(1);
        await Assert.That(_character.Mails.UnreadMailCount.Received).IsEqualTo(0);

        _character.Mails.ReturnMail(5002L);

        await Assert.That(_character.Mails.UnreadMailCount.TotalReceived).IsEqualTo(0);
        await Assert.That(_character.Mails.UnreadMailCount.Received).IsEqualTo(0);
    }

    [Test]
    public async Task ReturnMail_WithAnotherLetterRemaining_OnlyReturnedLeaves()
    {
        MakeReturnable();
        SeedInboxMail(5003L);
        var remaining = SeedInboxMail(5004L);
        _character.Mails.RefreshAllMailCounts();
        await Assert.That(_character.Mails.UnreadMailCount.TotalReceived).IsEqualTo(2);
        await Assert.That(_character.Mails.UnreadMailCount.Received).IsEqualTo(2);

        _character.Mails.ReturnMail(5003L);

        await Assert.That(_character.Mails.UnreadMailCount.TotalReceived).IsEqualTo(1);
        await Assert.That(_character.Mails.UnreadMailCount.Received).IsEqualTo(1);
        await Assert.That(remaining.Header.ReceiverId).IsEqualTo(_character.Id);
    }
}
