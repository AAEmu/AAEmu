using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.UnitTests.Game.Models.Game.Mails;

public class MailForSpecialityTests
{
    [Test]
    public async Task BuildBody_WritesTemplateIdAndAllEighteenFieldsInVerifiedOrder()
    {
        var body = MailForSpeciality.BuildBody(
            31832,
            130,
            90086,
            122968,
            0,
            122968,
            2,
            1,
            0,
            0,
            2d,
            103d,
            60);

        await Assert.That(body).IsEqualTo(
            "body(31832, 130, 90086, 122968, 0, 0, 122968, 2, 1, 0, 0, 0, 0, 0, 2, 103, 0, 60)");
    }
}
