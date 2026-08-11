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

    /// <summary>Crimson expand (scheduled noon Event Center row).</summary>
    private static TowerDef CrimsonExpand(float tod = 12f) => new()
    {
        Id = 171,
        Name = "징조의 틈 확장 (십자별 평원)",
        TimeOfDay = tod,
        TimeOfDayDayInterval = 1,
        TargetNpcSpawnId = 9846,
        ForceEndTime = 3600f
    };

    private static TowerDef CrimsonBase(float tod = 0f) => new()
    {
        Id = 3,
        Name = "징조의 틈(십자별 평원)",
        TimeOfDay = tod,
        TimeOfDayDayInterval = 1,
        TargetNpcSpawnId = 9846,
        ForceEndTime = 3600f
    };

    private static TowerDef GrimghastBase(float tod = 0f) => new()
    {
        Id = 13,
        Name = "전장의 안개(십자별 평원)",
        TimeOfDay = tod,
        TimeOfDayDayInterval = 1,
        TargetNpcSpawnId = 14335,
        ForceEndTime = 3600f
    };

    private static TowerDef GrimghastExpand(float tod = 0.1f) => new()
    {
        Id = 174,
        Name = "전장의 안개 확장 (십자별 평원)",
        TimeOfDay = tod,
        TimeOfDayDayInterval = 1,
        TargetNpcSpawnId = 14335,
        ForceEndTime = 3600f
    };

    [Test]
    public async Task IsGameTimeScheduled_RequiresTodIntervalSpawnerAndRiftName()
    {
        await Assert.That(CrimsonExpand().IsGameTimeScheduled).IsTrue();

        // Wall slot → not a Game Time event (Event Center lower strip is Server Time UTC).
        var wall = CrimsonExpand();
        wall.StartTimes[(int)DayOfWeek.Monday] = new TimeSpan(21, 0, 0);
        await Assert.That(wall.IsGameTimeScheduled).IsFalse();

        var noSpawner = CrimsonExpand();
        noSpawner.TargetNpcSpawnId = 0;
        await Assert.That(noSpawner.IsGameTimeScheduled).IsFalse();
    }

    [Test]
    public async Task IsGameTimeScheduled_CinderstoneYnystere_NightGrimghast_DayCrimsonExpand()
    {
        // Midnight: Grimghast base only — not base Crimson (legacy tod=0) or Grimghast expand.
        await Assert.That(GrimghastBase().IsGameTimeScheduled).IsTrue();
        await Assert.That(CrimsonBase().IsGameTimeScheduled).IsFalse();
        await Assert.That(GrimghastExpand().IsGameTimeScheduled).IsFalse();

        // Noon: Crimson expand only.
        await Assert.That(CrimsonExpand().IsGameTimeScheduled).IsTrue();

        var ynystereGrim = GrimghastBase();
        ynystereGrim.Id = 15;
        ynystereGrim.Name = "전장의 안개(이니스테르)";
        ynystereGrim.TargetNpcSpawnId = 14441;
        await Assert.That(ynystereGrim.IsGameTimeScheduled).IsTrue();

        var ynystereCrim = CrimsonExpand();
        ynystereCrim.Id = 172;
        ynystereCrim.Name = "징조의 틈 확장 (이니스테르)";
        ynystereCrim.TargetNpcSpawnId = 8939;
        await Assert.That(ynystereCrim.IsGameTimeScheduled).IsTrue();

        // Event Center: Crimson Rift (Auroria) triangle at game hour 18.
        var auroriaCrim = CrimsonExpand(18f);
        auroriaCrim.Id = 173;
        auroriaCrim.Name = "징조의 틈 확장 (원대륙)";
        auroriaCrim.TargetNpcSpawnId = 9998;
        await Assert.That(auroriaCrim.IsGameTimeScheduled).IsTrue();
        await Assert.That(auroriaCrim.CrossedGameStartHour(17.9f, 18.1f)).IsTrue();
    }

    [Test]
    [Arguments(11.9f, 12.0f, true)]
    [Arguments(12.0f, 12.1f, false)]
    [Arguments(23.9f, 0.1f, true)] // wrap across midnight onto tod=0
    [Arguments(0.0f, 0.5f, false)] // already past 0 for tod=0? old=0 new=0.5, trigger 0: old < 0 is false
    public async Task CrossedGameStartHour_FiresOncePerCrossing(float oldH, float newH, bool expected)
    {
        var tod = Math.Abs(oldH - 23.9f) < 0.01f ? 0f : 12f;
        var towerDef = tod < 1f ? GrimghastBase(tod) : CrimsonExpand(tod);
        await Assert.That(towerDef.CrossedGameStartHour(oldH, newH)).IsEqualTo(expected);
    }

    /// <summary>
    /// Documents the failure mode of large /time set before the ≤0.25h arm gate: an evening→morning
    /// snap is a wrap, so <c>CrossedGameStartHour</c> reports tod=0 Grimghast as "crossed" even though
    /// the GM never intended to pass midnight. Scheduler/TimeManager must refuse those jumps.
    /// </summary>
    [Test]
    public async Task LargeEveningToMorningSnap_CrossesMidnightGrimghastInMath()
    {
        var grimghast = GrimghastBase();
        // 22.5 → 11.75 is ~13.25h forward via midnight, or ~10.75h backward.
        await Assert.That(grimghast.CrossedGameStartHour(22.5f, 11.75f)).IsTrue();
        // Crimson noon is not yet "crossed" on that wrap (stop at 11.75).
        await Assert.That(CrimsonExpand(12f).CrossedGameStartHour(22.5f, 11.75f)).IsFalse();
    }
}
