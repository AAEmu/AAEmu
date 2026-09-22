using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.UnitTests.Game.GameData;
using AAEmu.UnitTests.Utils.Mocks;
using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// Mail edge protocols: spam reporting, return/expiry preservation of attachments and coin,
/// authoritative content-driven charges, and cash-on-delivery money paths.
/// </summary>
[NotInParallel]
public sealed class MailEdgeProtocolTests
{
    private const uint ReceiverId = 1;
    private const uint SenderId = 2;
    private const string ReceiverName = "tester";
    private const string SenderName = "Sender";

    private CharacterMock _character;
    private CharacterMock _senderCharacter;
    private CharacterMails _mails;
    private MailManager _mailManager;
    private Mock<IWorldManager> _mockWorldManager;
    private RecordingSaveManager _saves;
    private FieldInfo _contentConfigField;
    private object _previousContentConfig;

    [Before(Test)]
    public void Setup()
    {
        _saves = new RecordingSaveManager();
        _character = new CharacterMock { AccountId = 1, Id = ReceiverId, Name = ReceiverName, Money = 1000 };
        _senderCharacter = new CharacterMock { AccountId = 1, Id = SenderId, Name = SenderName, Money = 0 };
        _mails = new CharacterMails(_character);
        _character.Mails = _mails;

        ContentConfigTestSeed.Mail();

        var nameManager = new NameManager();
        nameManager.Load([], [], []);
        nameManager.AddCharacter(ReceiverId, ReceiverName, 1);
        nameManager.AddCharacter(SenderId, SenderName, 1);

        _mockWorldManager = Mock.Of<IWorldManager>();
        _mailManager = new MailManager(
            new SequentialMailIdManager(),
            nameManager,
            Mock.Of<IItemManager>().Object,
            Mock.Of<ITaskManager>().Object,
            _mockWorldManager.Object,
            new Lazy<IHousingManager>(() => Mock.Of<IHousingManager>().Object),
            Mock.Of<ILocalizationManager>().Object);

        typeof(Singleton<MailManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, null);
        typeof(Singleton<NameManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, null);
        _contentConfigField = typeof(Singleton<ContentConfigGameData>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic);
        _previousContentConfig = _contentConfigField?.GetValue(null);

        var services = new ServiceCollection();
        services.AddSingleton(_mailManager);
        services.AddSingleton<IMailManager>(_mailManager);
        services.AddSingleton(nameManager);
        services.AddSingleton<ISaveManager>(_saves);
        SingletonContainer.ServiceProvider = services.BuildServiceProvider();

        _mailManager._allPlayerMails = [];
    }

    [After(Test)]
    public void Teardown()
    {
        SingletonContainer.ServiceProvider = null;
        typeof(Singleton<MailManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, null);
        typeof(Singleton<NameManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, null);
        _contentConfigField?.SetValue(null, _previousContentConfig);
        _character = null;
        _senderCharacter = null;
        _mails = null;
        _mailManager = null;
        _mockWorldManager = null;
        _saves = null;
    }

    #region Helpers

    private BaseMail SeedLetter(
        long id,
        DateTime? recvDate = null,
        MailStatus status = MailStatus.Unread,
        bool withItem = true)
    {
        var now = recvDate ?? DateTime.UtcNow;
        var mail = new BaseMail
        {
            Id = id,
            MailType = MailType.Normal,
            Title = "test",
            ReceiverName = ReceiverName,
            OpenDate = now,
        };
        mail.Header.SenderId = SenderId;
        mail.Header.SenderName = SenderName;
        mail.Header.ReceiverId = ReceiverId;
        mail.Header.Status = status;
        mail.Body.Text = "test";
        mail.Body.SendDate = now;
        mail.Body.RecvDate = now;
        if (withItem)
            mail.Body.Attachments.Add(new Item(1) { Id = (ulong)(9000 + id), Count = 1, OwnerId = ReceiverId, SlotType = SlotType.Mail });
        mail.Header.Attachments = mail.GetTotalAttachmentCount();
        _mailManager._allPlayerMails[id] = mail;
        return mail;
    }

    private BaseMail SeedCodLetter(long id, bool withItem)
    {
        var mail = SeedLetter(id, withItem: withItem);
        mail.AttachMoney(77, 500, 0);
        return mail;
    }

    private void ConfigureSenderOnline()
    {
        _senderCharacter.IsOnline = true;
        _mockWorldManager.GetCharacterById(SenderId).Returns(_senderCharacter);
    }

    #endregion

    #region Authoritative charges

    [Test]
    public async Task GetMailFee_ReadsEveryChargeFromTheCatalogRows()
    {
        ContentConfigGameData.Instance.SetForTest(MailFeeRules.ExpressMailCostKey, 10);
        ContentConfigGameData.Instance.SetForTest(MailFeeRules.ExpressAttachmentCostKey, 20);

        var mail = new MailPlayerToPlayer(_character, SenderName) { MailType = MailType.Express };
        mail.Body.Attachments.Add(new Item(1) { Id = 1, Count = 1 });
        mail.Body.Attachments.Add(new Item(1) { Id = 2, Count = 1 });
        mail.Body.Attachments.Add(new Item(1) { Id = 3, Count = 1 });

        // 10 base + (3 attachments - 1 free slot) * 20
        await Assert.That(mail.GetMailFee()).IsEqualTo(50);
    }

    [Test]
    public async Task GetMailFee_MissingChargeRow_FailsLoudlyInsteadOfFallingBack()
    {
        _contentConfigField.SetValue(null, null);
        try
        {
            var mail = new MailPlayerToPlayer(_character, SenderName) { MailType = MailType.Normal };
            Assert.Throws<InvalidOperationException>(() => mail.GetMailFee());
        }
        finally
        {
            _contentConfigField.SetValue(null, _previousContentConfig);
        }

        await Assert.That(ContentConfigGameData.Instance.RequireInt(MailFeeRules.NormalMailCostKey)).IsEqualTo(50);
    }

    [Test]
    public async Task SendMailToPlayer_PostageAndDeliveryDelayComeFromTheCatalog()
    {
        ContentConfigGameData.Instance.SetForTest(MailFeeRules.ExpressMailCostKey, 123);
        ContentConfigGameData.Instance.SetForTest(MailFeeRules.AttachmentDelayByTargetKey, 7);

        var expressResult = _mails.SendMailToPlayer(
            MailType.Express, ReceiverName.NormalizeName(), "t", "b", 0, 0, 0, 0, 0, 0, []);
        await Assert.That(expressResult).IsEqualTo(MailResult.Success);
        await Assert.That(_character.Money).IsEqualTo(1000 - 123);

        var before = DateTime.UtcNow;
        var normalResult = _mails.SendMailToPlayer(
            MailType.Normal, ReceiverName.NormalizeName(), "t", "b", 0, 0, 0, 0, 0, 0, []);
        await Assert.That(normalResult).IsEqualTo(MailResult.Success);
        await Assert.That(_character.Money).IsEqualTo(1000 - 123 - 50);

        // The normal letter lands after mail_attachment_delay_by_target seconds.
        var delayed = _mailManager._allPlayerMails.Values
            .Where(m => m.MailType == MailType.Normal)
            .Single();
        var delta = delayed.Body.RecvDate - before;
        await Assert.That(delta).IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(5));
        await Assert.That(delta).IsLessThanOrEqualTo(TimeSpan.FromSeconds(15));
    }

    #endregion

    #region Spam report

    [Test]
    public async Task ReportSpam_AcceptedOnce_RetypesTheLetterAndKeepsCountsAndContents()
    {
        var mail = SeedLetter(2001L);
        _character.Mails.RefreshAllMailCounts();
        var totalBefore = _character.Mails.UnreadMailCount.TotalReceived;
        var unreadBefore = _character.Mails.UnreadMailCount.Received;
        await Assert.That(totalBefore).IsEqualTo(1);
        await Assert.That(unreadBefore).IsEqualTo(1);

        _mails.ReportSpam(mail.Id, SenderName);
        await Assert.That(mail.MailType).IsEqualTo(MailType.Spam);
        await Assert.That(mail.Body.CopperCoins).IsEqualTo(0);
        await Assert.That(mail.Body.Attachments.Count).IsEqualTo(1);
        await Assert.That(mail.Header.Attachments).IsEqualTo(mail.GetTotalAttachmentCount());

        // The spam bucket feeds the same inbox counters, so reporting never reshapes them.
        _character.Mails.RefreshAllMailCounts();
        await Assert.That(_character.Mails.UnreadMailCount.TotalReceived).IsEqualTo(totalBefore);
        await Assert.That(_character.Mails.UnreadMailCount.Received).IsEqualTo(unreadBefore);

        // A second report is accepted but must change nothing a second time.
        _mails.ReportSpam(mail.Id, SenderName);
        await Assert.That(mail.MailType).IsEqualTo(MailType.Spam);
        _character.Mails.RefreshAllMailCounts();
        await Assert.That(_character.Mails.UnreadMailCount.TotalReceived).IsEqualTo(totalBefore);
        await Assert.That(_character.Mails.UnreadMailCount.Received).IsEqualTo(unreadBefore);
        await Assert.That(mail.Body.Attachments.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ReportSpam_LeavesTheLetterReturnable()
    {
        var mail = SeedCodLetter(2004L, withItem: true);

        _mails.ReportSpam(mail.Id, SenderName);
        await Assert.That(mail.MailType).IsEqualTo(MailType.Spam);

        // A reported letter cannot be deleted while it holds attachments, so returning it is the
        // way out that does not require paying its charge.
        _mails.ReturnMail(mail.Id);

        await Assert.That(mail.Header.ReceiverId).IsEqualTo(SenderId);
        await Assert.That(mail.Header.Returned).IsTrue();
        await Assert.That(mail.Body.BillingAmount).IsEqualTo(0);
        await Assert.That(mail.Body.CopperCoins).IsEqualTo(77);
        await Assert.That(mail.Body.Attachments.Count).IsEqualTo(1);
        await Assert.That(_character.Money).IsEqualTo(1000);
    }

    [Test]
    public async Task ReportSpam_OnALetterAddressedToSomebodyElse_IsRefused()
    {
        var mail = SeedLetter(2002L);
        mail.Header.ReceiverId = SenderId;
        mail.ReceiverName = SenderName;

        _mails.ReportSpam(mail.Id, ReceiverName);

        await Assert.That(mail.MailType).IsEqualTo(MailType.Normal);
    }

    [Test]
    public async Task ReportSpam_WithASenderNameThatDoesNotMatchTheLetter_IsRefused()
    {
        var mail = SeedLetter(2003L);

        _mails.ReportSpam(mail.Id, "SomebodyElse");

        await Assert.That(mail.MailType).IsEqualTo(MailType.Normal);
    }

    #endregion

    #region Return

    [Test]
    public async Task ReturnMail_ClearsAnUnpaidChargeAndCarriesTheContentsBackIntact()
    {
        var mail = SeedCodLetter(3001L, withItem: true);
        await Assert.That((int)mail.Header.Attachments).IsEqualTo(3); // coin + charge + item

        _mails.ReturnMail(mail.Id);

        await Assert.That(mail.Header.ReceiverId).IsEqualTo(SenderId);
        await Assert.That(mail.Header.Returned).IsTrue();
        await Assert.That(mail.Body.BillingAmount).IsEqualTo(0);
        await Assert.That(mail.Body.CopperCoins).IsEqualTo(77);
        await Assert.That(mail.Body.Attachments.Count).IsEqualTo(1);
        await Assert.That((int)mail.Header.Attachments).IsEqualTo(2); // coin + item, charge gone
        await Assert.That(_character.Money).IsEqualTo(1000);
    }

    [Test]
    public async Task ReturnMail_OfAnAlreadyReturnedLetter_IsRefused()
    {
        var mail = SeedLetter(3002L);

        _mails.ReturnMail(mail.Id);
        await Assert.That(mail.Header.ReceiverId).IsEqualTo(SenderId);

        _mails.ReturnMail(mail.Id);

        await Assert.That(mail.Header.ReceiverId).IsEqualTo(SenderId);
        await Assert.That(mail.Header.Returned).IsTrue();
        await Assert.That(mail.Body.Attachments.Count).IsEqualTo(1);
        await Assert.That(mail.Body.CopperCoins).IsEqualTo(0);
    }

    #endregion

    #region Expiry

    [Test]
    public async Task Expiry_AtAUtcDayBoundary_AgreesAcrossTimestampKindsAndReturnsContents()
    {
        var boundary = new DateTime(2030, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var kinds = new[]
        {
            DateTimeKind.Utc,
            DateTimeKind.Unspecified,
            DateTimeKind.Local,
        };

        var id = 4000L;
        foreach (var kind in kinds)
        {
            id++;
            // Unread player mail is kept MailRetentionRules.UnreadRetention (30 days).
            var utcRecv = boundary.AddDays(-30);
            var recv = kind switch
            {
                DateTimeKind.Utc => DateTime.SpecifyKind(utcRecv, DateTimeKind.Utc),
                DateTimeKind.Local => utcRecv.ToLocalTime(),
                _ => DateTime.SpecifyKind(utcRecv, DateTimeKind.Unspecified),
            };
            var mail = SeedCodLetter(id, withItem: true);
            mail.Body.RecvDate = recv;
            mail.Body.SendDate = boundary.AddDays(-1); // sender side out of the way

            // One tick before the boundary the letter is still whole and still in the inbox.
            _mailManager.ExpireDueMails(boundary.AddTicks(-1));
            await Assert.That(mail.Header.ReceiverId).IsEqualTo(ReceiverId);
            await Assert.That(mail.Header.Returned).IsFalse();
            await Assert.That(mail.Body.CopperCoins).IsEqualTo(77);
            await Assert.That(mail.Body.Attachments.Count).IsEqualTo(1);

            // Exactly on the boundary it turns around with every attachment and no charge left.
            _mailManager.ExpireDueMails(boundary);
            await Assert.That(mail.Header.ReceiverId).IsEqualTo(SenderId);
            await Assert.That(mail.Header.Returned).IsTrue();
            await Assert.That(mail.Body.BillingAmount).IsEqualTo(0);
            await Assert.That(mail.Body.CopperCoins).IsEqualTo(77);
            await Assert.That(mail.Body.Attachments.Count).IsEqualTo(1);
            await Assert.That(_character.Money).IsEqualTo(1000);
            await Assert.That(_senderCharacter.Money).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Expiry_OfAReadLetter_ReleasesItsContentsWithoutTouchingAnyBalance()
    {
        var boundary = new DateTime(2030, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var mail = SeedLetter(4101L, withItem: true);
        mail.AttachMoney(77, 0, 0);
        mail.Header.Status = MailStatus.Read;
        mail.OpenDate = boundary.AddDays(-5); // ReadRetention window
        mail.Body.RecvDate = boundary.AddDays(-10);
        mail.Body.SendDate = boundary.AddDays(-1);

        _mailManager.ExpireDueMails(boundary);

        await Assert.That(mail.Header.Returned).IsFalse();
        await Assert.That(mail.Body.CopperCoins).IsEqualTo(0);
        await Assert.That(mail.Body.Attachments.Count).IsEqualTo(0);
        await Assert.That((int)mail.Header.Attachments).IsEqualTo(0);
        await Assert.That(_character.Money).IsEqualTo(1000);
    }

    [Test]
    public async Task Expiry_OfAReadLetterWithAnUnpaidCharge_GoesBackToTheSender()
    {
        // The receiver opened it but never paid: they could neither take the goods nor delete it,
        // so the sweep returns it instead of destroying the sender's items and coin.
        var boundary = new DateTime(2030, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var mail = SeedCodLetter(4102L, withItem: true);
        mail.Header.Status = MailStatus.Read;
        mail.OpenDate = boundary.AddDays(-5); // ReadRetention window
        mail.Body.RecvDate = boundary.AddDays(-10);
        mail.Body.SendDate = boundary.AddDays(-1);

        _mailManager.ExpireDueMails(boundary);

        await Assert.That(mail.Header.ReceiverId).IsEqualTo(SenderId);
        await Assert.That(mail.Header.Returned).IsTrue();
        await Assert.That(mail.ReceiverDeleted).IsFalse();
        await Assert.That(mail.Body.BillingAmount).IsEqualTo(0);
        await Assert.That(mail.Body.CopperCoins).IsEqualTo(77);
        await Assert.That(mail.Body.Attachments.Count).IsEqualTo(1);
        await Assert.That(_character.Money).IsEqualTo(1000);
        await Assert.That(_senderCharacter.Money).IsEqualTo(0);
    }

    [Test]
    public async Task ReturnsOnExpiry_UnreadOrUnpaid()
    {
        var unread = SeedLetter(4103L);
        var read = SeedLetter(4104L);
        read.Header.Status = MailStatus.Read;
        var readUnpaid = SeedCodLetter(4105L, withItem: false);
        readUnpaid.Header.Status = MailStatus.Read;

        await Assert.That(MailRetentionRules.ReturnsOnExpiry(unread)).IsTrue();
        await Assert.That(MailRetentionRules.ReturnsOnExpiry(read)).IsFalse();
        await Assert.That(MailRetentionRules.ReturnsOnExpiry(readUnpaid)).IsTrue();
    }

    [Test]
    public async Task Expiry_SenderSideAtTheDayBoundary_KeepsTheReceivedCopyAndContents()
    {
        var boundary = new DateTime(2030, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var mail = SeedCodLetter(4201L, withItem: true);
        mail.Body.SendDate = boundary.AddDays(-30); // SentMailExpiry lands on the boundary
        mail.Body.RecvDate = boundary.AddDays(-1);  // receiver side still fresh

        _mailManager.ExpireDueMails(boundary);

        await Assert.That(mail.SenderDeleted).IsTrue();
        await Assert.That(mail.ReceiverDeleted).IsFalse();
        await Assert.That(mail.Header.ReceiverId).IsEqualTo(ReceiverId);
        await Assert.That(mail.Body.CopperCoins).IsEqualTo(77);
        await Assert.That(mail.Body.Attachments.Count).IsEqualTo(1);
        await Assert.That(_character.Money).IsEqualTo(1000);
    }

    [Test]
    public async Task Expiry_SenderDeletedTheirCopyBeforeTheSweep_StillReturnsTheContentsOnce()
    {
        var boundary = new DateTime(2030, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var mail = SeedCodLetter(4301L, withItem: true);
        mail.Body.RecvDate = boundary.AddDays(-30);
        mail.Body.SendDate = boundary.AddDays(-1);

        _mailManager.DeleteForSender(mail); // the sender clears their Sent history first

        _mailManager.ExpireDueMails(boundary);

        await Assert.That(_mailManager._allPlayerMails.ContainsKey(mail.Id)).IsTrue();
        await Assert.That(mail.Header.ReceiverId).IsEqualTo(SenderId);
        await Assert.That(mail.Body.CopperCoins).IsEqualTo(77);
        await Assert.That(mail.Body.Attachments.Count).IsEqualTo(1);
        await Assert.That(mail.Body.BillingAmount).IsEqualTo(0);
    }

    #endregion

    #region Cash on delivery

    [Test]
    public async Task GetAttached_WhileTheChargeIsUnpaid_RefusesAndKeepsEverything()
    {
        var mail = SeedCodLetter(5001L, withItem: true);

        var claimed = _mails.GetAttached(mail.Id, takeMoney: true, takeItems: true, takeAllSelected: true);

        await Assert.That(claimed).IsFalse();
        await Assert.That(_character.Money).IsEqualTo(1000);
        await Assert.That(mail.Body.CopperCoins).IsEqualTo(77);
        await Assert.That(mail.Body.BillingAmount).IsEqualTo(500);
        await Assert.That(mail.Body.Attachments.Count).IsEqualTo(1);
        await Assert.That((int)mail.Header.Attachments).IsEqualTo(3);
    }

    [Test]
    public async Task PayChargeMoney_ForAnOnlineSender_MovesTheChargeExactlyOnce()
    {
        ConfigureSenderOnline();
        var mail = SeedCodLetter(5002L, withItem: false);

        var paid = _mailManager.PayChargeMoney(_character, mail.Id, autoUseAAPoint: false);

        await Assert.That(paid).IsTrue();
        await Assert.That(_character.Money).IsEqualTo(500);
        await Assert.That(_senderCharacter.Money).IsEqualTo(500);
        await Assert.That(mail.Body.BillingAmount).IsEqualTo(0);
        await Assert.That(mail.Body.CopperCoins).IsEqualTo(77);
        await Assert.That(mail.Header.Attachments).IsEqualTo(mail.GetTotalAttachmentCount());

        // The charge is gone; a second payment must not collect again.
        var paidAgain = _mailManager.PayChargeMoney(_character, mail.Id, autoUseAAPoint: false);
        await Assert.That(paidAgain).IsFalse();
        await Assert.That(_character.Money).IsEqualTo(500);
        await Assert.That(_senderCharacter.Money).IsEqualTo(500);
    }

    [Test]
    public async Task PayChargeMoney_ForAnOfflineSender_ParksThePaymentOnAReceiptLetter()
    {
        var mail = SeedCodLetter(5003L, withItem: false);

        var paid = _mailManager.PayChargeMoney(_character, mail.Id, autoUseAAPoint: false);

        await Assert.That(paid).IsTrue();
        await Assert.That(_character.Money).IsEqualTo(500);
        await Assert.That(mail.Body.BillingAmount).IsEqualTo(0);
        await Assert.That(mail.Body.CopperCoins).IsEqualTo(77);

        var receipts = Receipts();
        await Assert.That(receipts.Count).IsEqualTo(1);
        await Assert.That(receipts[0].Header.ReceiverId).IsEqualTo(SenderId);
        await Assert.That(receipts[0].Body.CopperCoins).IsEqualTo(500);
        await Assert.That(MailDeliveryRules.IsPublished(receipts[0])).IsTrue();

        // The client's own charge-paid letter: its locale entry supplies the sender, title and body.
        await Assert.That(receipts[0].MailType).IsEqualTo(MailType.SysExpress);
        await Assert.That(receipts[0].Header.SenderId).IsEqualTo(0u);
        await Assert.That(receipts[0].Title).IsEqualTo("title");
        await Assert.That(receipts[0].Body.Text).IsEqualTo("body");
    }

    [Test]
    public async Task PayChargeMoney_WithAPointPaymentRequest_IsRefusedWithoutMovingMoney()
    {
        var mail = SeedCodLetter(5004L, withItem: false);

        var paid = _mailManager.PayChargeMoney(_character, mail.Id, autoUseAAPoint: true);

        await Assert.That(paid).IsFalse();
        await Assert.That(_character.Money).IsEqualTo(1000);
        await Assert.That(mail.Body.BillingAmount).IsEqualTo(500);
    }

    [Test]
    public async Task PayChargeMoney_AfterTheLetterWasReturned_IsRefused()
    {
        var mail = SeedCodLetter(5005L, withItem: true);
        _mails.ReturnMail(mail.Id);
        await Assert.That(mail.Header.ReceiverId).IsEqualTo(SenderId);

        var paid = _mailManager.PayChargeMoney(_character, mail.Id, autoUseAAPoint: false);

        await Assert.That(paid).IsFalse();
        await Assert.That(_character.Money).IsEqualTo(1000);
        await Assert.That(Receipts()).IsEmpty();
    }

    [Test]
    public async Task PayChargeMoney_ThenAReturn_MovesNothingTwice()
    {
        var mail = SeedCodLetter(5006L, withItem: false);

        await Assert.That(_mailManager.PayChargeMoney(_character, mail.Id, autoUseAAPoint: false)).IsTrue();
        await Assert.That(_character.Money).IsEqualTo(500);

        _mails.ReturnMail(mail.Id);

        // Settled once: the receipt still holds exactly the paid coin, the returned letter
        // still holds exactly its own attachment coin, and nobody was charged twice.
        await Assert.That(mail.Header.ReceiverId).IsEqualTo(SenderId);
        await Assert.That(mail.Body.CopperCoins).IsEqualTo(77);
        await Assert.That(mail.Body.BillingAmount).IsEqualTo(0);
        var receipts = Receipts();
        await Assert.That(receipts.Count).IsEqualTo(1);
        await Assert.That(receipts[0].Body.CopperCoins).IsEqualTo(500);
        await Assert.That(_character.Money).IsEqualTo(500);
        await Assert.That(_mailManager._allPlayerMails.Values.Sum(m => (long)m.Body.CopperCoins)).IsEqualTo(577);
    }

    /// <summary>The charge-paid letters parked for the sender.</summary>
    private List<BaseMail> Receipts() =>
        _mailManager._allPlayerMails.Values
            .Where(m => m.Header.SenderName == ".chargePay")
            .ToList();

    #endregion
}