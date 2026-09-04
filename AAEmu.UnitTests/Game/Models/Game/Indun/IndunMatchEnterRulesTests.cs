using AAEmu.Game.Models.Game.Indun.Matching;

namespace AAEmu.UnitTests.Game.Models.Game.Indun;

public class IndunMatchEnterRulesTests
{
    [Test]
    public async Task CanAdmit_AllowsARejoinThatAlreadyPaid()
    {
        await Assert.That(IndunMatchEnterRules.CanAdmit(alreadyChargedThisCopy: true, dailyEntryAllowed: false))
            .IsTrue();
    }

    [Test]
    public async Task CanAdmit_RefusesAFreshVisitAtTheDailyLimit()
    {
        await Assert.That(IndunMatchEnterRules.CanAdmit(alreadyChargedThisCopy: false, dailyEntryAllowed: false))
            .IsFalse();
    }

    [Test]
    public async Task ShouldPublishEnter_OnlyWhenSomeoneWasAdmitted()
    {
        await Assert.That(IndunMatchEnterRules.ShouldPublishEnter(0)).IsFalse();
        await Assert.That(IndunMatchEnterRules.ShouldPublishEnter(2)).IsTrue();
    }
}
