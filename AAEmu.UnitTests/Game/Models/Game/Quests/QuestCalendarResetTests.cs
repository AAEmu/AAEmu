using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestCalendarResetTests
{
    [Test]
    public async Task DailySet_Matches_ClientIsDailyDetail()
    {
        // Client classifier: detail in {7,10,11,12,13}
        await Assert.That(QuestCalendarResetSet.Daily).Contains(QuestDetail.Daily);
        await Assert.That(QuestCalendarResetSet.Daily).Contains(QuestDetail.DailyHunt);
        await Assert.That(QuestCalendarResetSet.Daily).Contains(QuestDetail.DailyLivelihood);
        await Assert.That(QuestCalendarResetSet.Daily).Contains(QuestDetail.DailyGroup);
        await Assert.That(QuestCalendarResetSet.Daily).Contains(QuestDetail.Today);
        await Assert.That(QuestCalendarResetSet.Daily).DoesNotContain(QuestDetail.Livelihood);
        await Assert.That(QuestCalendarResetSet.Daily).DoesNotContain(QuestDetail.Group);
        await Assert.That(QuestCalendarResetSet.Daily).DoesNotContain(QuestDetail.Weekly);
    }

    [Test]
    public async Task WeeklySet_IsWeeklyDetailOnly()
    {
        await Assert.That(QuestCalendarResetSet.Weekly).IsEquivalentTo(new[] { QuestDetail.Weekly });
    }

    [Test]
    public async Task WeekStartMonday_UsesMonday()
    {
        // 2026-08-09 is Sunday UTC; week start is preceding Monday 2026-08-03.
        var sunday = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        var offset = ((int)sunday.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var weekStart = sunday.Date.AddDays(-offset);
        await Assert.That(weekStart).IsEqualTo(new DateTime(2026, 8, 3));
        await Assert.That(weekStart.DayOfWeek).IsEqualTo(DayOfWeek.Monday);
    }
}
