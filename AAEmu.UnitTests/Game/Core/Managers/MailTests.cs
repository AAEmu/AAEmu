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

    [Before(Test)]
    public void Setup()
    {
        _character = new CharacterMock { AccountId = 1, Id = 1, Name = "tester", Money = 1000 };

        _mails = new CharacterMails(_character);

        var nameManager = new NameManager();
        nameManager.Load([], [], []);
        nameManager.AddCharacter(_character.Id, _character.Name, 1);

        var mailIdManager = new MailIdManager();
        mailIdManager.Initialize();

        _mailManager = new MailManager(
            mailIdManager,
            nameManager,
            Mock.Of<IItemManager>().Object,
            Mock.Of<ITaskManager>().Object,
            Mock.Of<IWorldManager>().Object,
            new Lazy<IHousingManager>(() => Mock.Of<IHousingManager>().Object),
            Mock.Of<ILocalizationManager>().Object);

        // Reset singleton caches so Instance properties resolve via ServiceProvider
        typeof(Singleton<MailManager>)
            .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, null);
        typeof(Singleton<NameManager>)
            .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)
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

        SingletonContainer.ServiceProvider = null;
        typeof(Singleton<MailManager>)
            .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, null);
        typeof(Singleton<NameManager>)
            .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)
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
        var money0 = 500;
        var money1 = 0;
        var money2 = 0;
        var extra = 0;
        var itemSlots = new List<(SlotType slotType, byte slot)>();

        await Assert.That(_mails.SendMailToPlayer(type, receiverCharName, title, text, attachments, money0, money1, money2, extra, itemSlots)).IsEqualTo(MailResult.Success);
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
        var money0 = 500;
        var money1 = 0;
        var money2 = 0;
        var extra = 0;
        var itemSlots = new List<(SlotType slotType, byte slot)>();

        await Assert.That(_mails.SendMailToPlayer(type, receiverCharName, title, text, attachments, money0, money1, money2, extra, itemSlots)).IsNotEqualTo(MailResult.Success);
        await Assert.That(_character.Money).IsEqualTo(1000);
    }

    [Test]
    public async Task GetAttached_MissingMail_ReturnsFalse()
    {
        await Assert.That(_mails.GetAttached(999, true, true, true)).IsFalse();
    }

    [Test]
    public async Task ReadMail_MissingMail_DoesNotThrow()
    {
        _mails.ReadMail(false, 999);
        await Assert.That(_mailManager._allPlayerMails.ContainsKey(999)).IsFalse();
    }

    [Test]
    public async Task DeleteMail_TrashAttachmentWithoutContainer_StillRemovesMail()
    {
        var mail = new BaseMail
        {
            Id = 42,
            ReceiverName = "tester",
            Header = { ReceiverId = 1, SenderId = 0 },
        };
        mail.Body.Attachments.Add(new Item(99) { SlotType = SlotType.Mail });
        _mailManager._allPlayerMails[42] = mail;

        await Assert.That(_mailManager.DeleteMail(mail, trashItems: true)).IsTrue();
        await Assert.That(_mailManager._allPlayerMails.ContainsKey(42)).IsFalse();
    }
}
