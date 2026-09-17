using AAEmu.Game.Models.Game.Rankings;

namespace AAEmu.UnitTests.Game.Models.Game.Rankings;

public class RankPeriodsTests
{
    [Test]
    public async Task Weekly_DaySeven_IsTheWindowTheWindowShows()
    {
        // The fishing, goods and garden boards name day 7. On 2026-09-17 the window displayed their
        // period as 2026-09-13 → 2026-09-20, and 2026-09-13 is a sunday.
        var period = RankPeriods.For(new DateTime(2026, 9, 17, 13, 30, 0, DateTimeKind.Utc), RankPeriods.Weekly, 7);

        await Assert.That(period.StartUtc).IsEqualTo(new DateTime(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(period.EndUtc).IsEqualTo(new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public async Task Weekly_OnTheStartDayItself_IsTheWindowThatOpensThatDay()
    {
        var period = RankPeriods.For(new DateTime(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc), RankPeriods.Weekly, 7);

        await Assert.That(period.StartUtc).IsEqualTo(new DateTime(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public async Task Weekly_FourthDay_StartsOnTheThursday()
    {
        // The festival battlefield board names day 4.
        var period = RankPeriods.For(new DateTime(2026, 9, 17, 13, 30, 0, DateTimeKind.Utc), RankPeriods.Weekly, 4);

        await Assert.That(period.StartUtc.DayOfWeek).IsEqualTo(DayOfWeek.Thursday);
        await Assert.That(period.StartUtc).IsEqualTo(new DateTime(2026, 9, 17, 0, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public async Task Monthly_IsTheCalendarMonthTheWindowShows()
    {
        // The game-point boards name no day; the window showed 2026-09-01 → 2026-10-01.
        var period = RankPeriods.For(new DateTime(2026, 9, 17, 13, 30, 0, DateTimeKind.Utc), RankPeriods.Monthly, RankPeriods.NoDay);

        await Assert.That(period.StartUtc).IsEqualTo(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(period.EndUtc).IsEqualTo(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public async Task Monthly_CrossesTheYearBoundary()
    {
        var period = RankPeriods.For(new DateTime(2026, 12, 31, 23, 0, 0, DateTimeKind.Utc), RankPeriods.Monthly, RankPeriods.NoDay);

        await Assert.That(period.StartUtc).IsEqualTo(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(period.EndUtc).IsEqualTo(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }
}
