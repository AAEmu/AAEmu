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
    private RecordingSaveManager _saves;

    [Before(Test)]
    public void Setup()
    {
        _saves = new RecordingSaveManager();
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
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, null);
        typeof(Singleton<NameManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, null);

        var services = new ServiceCollection();
        services.AddSingleton(_mailManager);
        services.AddSingleton(nameManager);
        services.AddSingleton<ISaveManager>(_saves);
        SingletonContainer.ServiceProvider = services.BuildServiceProvider();

        _mailManager._allPlayerMails = [];
    }

    [After(Test)]
    public void Teardown()
    {
        _character = null;
        _mails = null;
        _mailManager = null;
        _saves = null;

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
        var money0 = 500;
        var money1 = 0;
        var money2 = 0;
        var extra = 0;
        var itemSlots = new List<(SlotType slotType, byte slot)>();

        await Assert.That(_mails.SendMailToPlayer(type, receiverCharName, title, text, attachments, money0, money1, money2, extra, itemSlots)).IsEqualTo(MailResult.Success);
        await Assert.That(_character.Money).IsEqualTo(400);
    }

    /// <summary>
    /// The save a sent letter forces must see the sender already charged. Saving from inside
    /// Send() committed the paid letter next to the pre-charge balance, and a restart before the
    /// next tick gave the sender their coin back while the recipient kept the letter.
    /// </summary>
    [Test]
    public async Task SendMailToPlayer_PersistsOnceAfterTheFeeIsCharged()
    {
        var committedMoney = new List<long>();
        var committedMails = new List<int>();
        _saves.OnSave = () =>
        {
            committedMoney.Add(_character.Money);
            committedMails.Add(_mailManager._allPlayerMails.Count);
        };

        var result = _mails.SendMailToPlayer(
            MailType.Express, "tester".NormalizeName(), "test", "test", 0, 500, 0, 0, 0, []);

        await Assert.That(result).IsEqualTo(MailResult.Success);
        await Assert.That(_saves.SaveCount).IsEqualTo(1);
        await Assert.That(committedMoney).IsEquivalentTo([400L]);
        await Assert.That(committedMails).IsEquivalentTo([1]);
    }

    [Test]
    public async Task DeferPersist_NestedScopes_FlushOnceAtTheOutermost()
    {
        using (_mailManager.DeferPersist())
        {
            _mailManager.PersistNow();
            using (_mailManager.DeferPersist())
                _mailManager.PersistNow();
            await Assert.That(_saves.SaveCount).IsEqualTo(0);
        }

        await Assert.That(_saves.SaveCount).IsEqualTo(1);

        using (_mailManager.DeferPersist())
        {
        }

        await Assert.That(_saves.SaveCount).IsEqualTo(1);

        _mailManager.PersistNow();
        await Assert.That(_saves.SaveCount).IsEqualTo(2);
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
