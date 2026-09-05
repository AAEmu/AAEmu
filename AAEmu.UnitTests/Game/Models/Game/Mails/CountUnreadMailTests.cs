using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.UnitTests.Game.Models.Game.Mails;

public class CountUnreadMailTests
{
    [Test]
    public async Task UpdateReceived_Charged_LightsPortraitAndCommercial()
    {
        var count = new CountUnreadMail();

        count.AddTotal(MailType.Charged);
        count.UpdateReceived(MailType.Charged, 1);

        await Assert.That(count.CommercialReceived).IsEqualTo(1);
        await Assert.That(count.TotalCommercialReceived).IsEqualTo(1);
        await Assert.That(count.Received).IsEqualTo(1);
        await Assert.That(count.TotalReceived).IsEqualTo(0);
    }

    [Test]
    public async Task UpdateReceived_Promotion_MatchesCharged()
    {
        var count = new CountUnreadMail();

        count.AddTotal(MailType.Promotion);
        count.UpdateReceived(MailType.Promotion, 1);

        await Assert.That(count.CommercialReceived).IsEqualTo(1);
        await Assert.That(count.Received).IsEqualTo(1);
        await Assert.That(count.TotalReceived).IsEqualTo(0);
    }

    [Test]
    public async Task UpdateReceived_NormalLetter_DoesNotTouchCommercial()
    {
        var count = new CountUnreadMail();

        count.AddTotal(MailType.Normal);
        count.UpdateReceived(MailType.Normal, 1);

        await Assert.That(count.Received).IsEqualTo(1);
        await Assert.That(count.TotalReceived).IsEqualTo(1);
        await Assert.That(count.CommercialReceived).IsEqualTo(0);
        await Assert.That(count.TotalCommercialReceived).IsEqualTo(0);
    }

    [Test]
    public async Task UpdateReceived_ReadCharged_ClearsBothUnreadSlots()
    {
        var count = new CountUnreadMail();
        count.UpdateReceived(MailType.Charged, 1);

        count.UpdateReceived(MailType.Charged, -1);

        await Assert.That(count.CommercialReceived).IsEqualTo(0);
        await Assert.That(count.Received).IsEqualTo(0);
    }
}
