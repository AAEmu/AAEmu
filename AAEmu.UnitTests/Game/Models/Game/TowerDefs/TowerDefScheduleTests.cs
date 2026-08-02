using AAEmu.Game.Models.Game.TowerDefs;

namespace AAEmu.UnitTests.Game.Models.Game.TowerDefs;

/// <summary>
/// Pins the weekday-slot reading of <c>tower_defs</c>. Getting this wrong either leaves the world
/// bosses dead or runs them every day instead of once a week.
/// </summary>
public class TowerDefScheduleTests
{
    /// <summary>Kraken: one populated slot (Tuesday 21:30), a one-hour window.</summary>
    private static TowerDef Kraken()
    {
        var towerDef = new TowerDef { Id = 152, Name = "크라켄의 출현", ForceEndTime = 3600f };
        towerDef.StartTimes[(int)DayOfWeek.Tuesday] = new TimeSpan(21, 30, 0);
        return towerDef;
    }

    [Test]
    public async Task IsScheduled_TrueWhenAnySlotIsSet()
    {
        await Assert.That(Kraken().IsScheduled).IsTrue();
        await Assert.That(new TowerDef { Id = 1 }.IsScheduled).IsFalse();
    }

    [Test]
    public async Task Duration_UsesForceEndTime()
    {
        await Assert.That(Kraken().Duration).IsEqualTo(TimeSpan.FromHours(1));
    }

    [Test]
    public async Task StartTimeFor_OnlyAnswersOnItsDay()
    {
        var towerDef = Kraken();
        await Assert.That(towerDef.StartTimeFor(DayOfWeek.Tuesday)).IsEqualTo(new TimeSpan(21, 30, 0));
        await Assert.That(towerDef.StartTimeFor(DayOfWeek.Wednesday)).IsNull();
    }

    // 2026-07-28 is a Tuesday, 2026-07-29 a Wednesday.
    [Test]
    [Arguments("2026-07-28 21:29:59", false)] // a minute early
    [Arguments("2026-07-28 21:30:00", true)]  // opens
    [Arguments("2026-07-28 22:29:59", true)]  // last second of the hour
    [Arguments("2026-07-28 22:30:00", false)] // closed
    [Arguments("2026-07-29 21:30:00", false)] // right time, wrong day
    [Arguments("2026-07-27 21:30:00", false)] // Monday
    public async Task IsWithinWindow_OpensOnlyInsideItsWeekdaySlot(string nowText, bool expected)
    {
        var now = DateTime.Parse(nowText);

        var open = Kraken().IsWithinWindow(now);

        await Assert.That(open).IsEqualTo(expected);
    }

    [Test]
    public async Task IsWithinWindow_WindowRunningPastMidnightStaysOwnedByItsStartDay()
    {
        // 23:30 Saturday + 2h runs into Sunday; the Sunday slot is empty, so only the carry-over
        // from Saturday may keep it open.
        var towerDef = new TowerDef { Id = 900, ForceEndTime = 7200f };
        towerDef.StartTimes[(int)DayOfWeek.Saturday] = new TimeSpan(23, 30, 0);

        // 2026-08-01 is a Saturday.
        var inside = DateTime.Parse("2026-08-02 00:30:00"); // Sunday, 1h in
        var outside = DateTime.Parse("2026-08-02 01:30:00"); // Sunday, 2h in — expired

        await Assert.That(towerDef.IsWithinWindow(inside)).IsTrue();
        await Assert.That(towerDef.IsWithinWindow(outside)).IsFalse();
    }

    [Test]
    public async Task IsWithinWindow_UnscheduledEventNeverOpens()
    {
        var towerDef = new TowerDef { Id = 901, ForceEndTime = 3600f };

        for (var hour = 0; hour < 24; hour++)
        {
            var now = new DateTime(2026, 7, 29, hour, 0, 0);
            await Assert.That(towerDef.IsWithinWindow(now)).IsFalse();
        }
    }
}
