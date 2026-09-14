using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.UnitTests.Game.Models.Game.Expeditions;

public class WeeklyContributionRulesTests
{
    [Test]
    public async Task CurrentValue_PreservesValueWithinPersistedWeek()
    {
        var week = new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc);

        await Assert.That(WeeklyContributionRules.CurrentValue(1234, week, week)).IsEqualTo(1234u);
    }

    [Test]
    public async Task CurrentValue_ResetsValueAcrossRestartedWeek()
    {
        var previousWeek = new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc);
        var currentWeek = previousWeek.AddDays(7);

        await Assert.That(WeeklyContributionRules.CurrentValue(1234, previousWeek, currentWeek)).IsEqualTo(0u);
    }
}
