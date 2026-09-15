using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.UnitTests.Game.Models.Game.World.Zones;

public class ConflictZoneScheduleRulesTests
{
    /// <summary>
    /// The shipped <c>conflict_zone_realtime_schedules</c> rows for zone group 147
    /// (o_western_prairie): friday and saturday 21:30 battle, 22:00 war, 23:00 battle.
    /// </summary>
    private static ConflictZoneScheduleEntry[] ShippedZone147() =>
    [
        new(6, 2130, ZoneConflictType.Conflict),
        new(6, 2200, ZoneConflictType.War),
        new(6, 2300, ZoneConflictType.Conflict),
        new(7, 2130, ZoneConflictType.Conflict),
        new(7, 2200, ZoneConflictType.War),
        new(7, 2300, ZoneConflictType.Conflict)
    ];

    [Test]
    public async Task DecodeTimeOfDay_ReadsMilitaryHhmm()
    {
        await Assert.That(ConflictZoneScheduleRules.DecodeTimeOfDay(20)).IsEqualTo(TimeSpan.FromMinutes(20));
        await Assert.That(ConflictZoneScheduleRules.DecodeTimeOfDay(600)).IsEqualTo(TimeSpan.FromHours(6));
        await Assert.That(ConflictZoneScheduleRules.DecodeTimeOfDay(2130)).IsEqualTo(new TimeSpan(21, 30, 0));
        await Assert.That(ConflictZoneScheduleRules.DecodeTimeOfDay(2359)).IsEqualTo(new TimeSpan(23, 59, 0));

        // Not a valid HHMM: minutes > 59 or hour > 23.
        await Assert.That(ConflictZoneScheduleRules.DecodeTimeOfDay(1260)).IsNull();
        await Assert.That(ConflictZoneScheduleRules.DecodeTimeOfDay(2400)).IsNull();
        await Assert.That(ConflictZoneScheduleRules.DecodeTimeOfDay(-1)).IsNull();
    }

    [Test]
    public async Task DecodeDayOfWeek_FollowsEnumDayOfWeeks()
    {
        await Assert.That(ConflictZoneScheduleRules.DecodeDayOfWeek(1)).IsEqualTo(DayOfWeek.Sunday);
        await Assert.That(ConflictZoneScheduleRules.DecodeDayOfWeek(6)).IsEqualTo(DayOfWeek.Friday);
        await Assert.That(ConflictZoneScheduleRules.DecodeDayOfWeek(7)).IsEqualTo(DayOfWeek.Saturday);

        // 8 is 'invalid' in enum_day_of_weeks.
        await Assert.That(ConflictZoneScheduleRules.DecodeDayOfWeek(8)).IsNull();
        await Assert.That(ConflictZoneScheduleRules.DecodeDayOfWeek(0)).IsNull();
    }

    [Test]
    public async Task Resolve_WalksTheShippedZone147Windows()
    {
        // 2026-09-04 is a friday, 2026-09-05 a saturday.
        var schedule = ShippedZone147();

        var beforeFirstEntry = ConflictZoneScheduleRules.Resolve(
            schedule, new DateTime(2026, 9, 4, 21, 15, 0));
        await Assert.That(beforeFirstEntry).IsNotNull();
        await Assert.That(beforeFirstEntry!.Value.State).IsEqualTo(ZoneConflictType.Conflict);
        await Assert.That(beforeFirstEntry.Value.NextChange).IsEqualTo(new DateTime(2026, 9, 4, 21, 30, 0));

        var battle = ConflictZoneScheduleRules.Resolve(schedule, new DateTime(2026, 9, 4, 21, 45, 0));
        await Assert.That(battle!.Value.State).IsEqualTo(ZoneConflictType.Conflict);
        await Assert.That(battle.Value.NextChange).IsEqualTo(new DateTime(2026, 9, 4, 22, 0, 0));

        var war = ConflictZoneScheduleRules.Resolve(schedule, new DateTime(2026, 9, 4, 22, 30, 0));
        await Assert.That(war!.Value.State).IsEqualTo(ZoneConflictType.War);
        await Assert.That(war.Value.NextChange).IsEqualTo(new DateTime(2026, 9, 4, 23, 0, 0));

        var lateFriday = ConflictZoneScheduleRules.Resolve(schedule, new DateTime(2026, 9, 4, 23, 30, 0));
        await Assert.That(lateFriday!.Value.State).IsEqualTo(ZoneConflictType.Conflict);
        await Assert.That(lateFriday.Value.NextChange).IsEqualTo(new DateTime(2026, 9, 5, 21, 30, 0));

        // After saturday's last entry the next change is the following friday.
        var lateSaturday = ConflictZoneScheduleRules.Resolve(schedule, new DateTime(2026, 9, 5, 23, 30, 0));
        await Assert.That(lateSaturday!.Value.State).IsEqualTo(ZoneConflictType.Conflict);
        await Assert.That(lateSaturday.Value.NextChange).IsEqualTo(new DateTime(2026, 9, 11, 21, 30, 0));
    }

    [Test]
    public async Task Resolve_UsesThePreviousWeekWhenNowPrecedesTheFirstEntry()
    {
        // Only monday 06:00 battle and monday 12:00 war.
        ConflictZoneScheduleEntry[] schedule =
        [
            new(2, 600, ZoneConflictType.Conflict),
            new(2, 1200, ZoneConflictType.War)
        ];

        var monday = new DateTime(2026, 9, 7, 5, 0, 0);
        var position = ConflictZoneScheduleRules.Resolve(schedule, monday);

        // Wrapped to the previous monday's 12:00 war.
        await Assert.That(position).IsNotNull();
        await Assert.That(position!.Value.State).IsEqualTo(ZoneConflictType.War);
        await Assert.That(position.Value.StateStart).IsEqualTo(new DateTime(2026, 8, 31, 12, 0, 0));
        await Assert.That(position.Value.NextChange).IsEqualTo(new DateTime(2026, 9, 7, 6, 0, 0));
    }

    [Test]
    public async Task Resolve_AppliesTheEntryAtItsExactStart()
    {
        ConflictZoneScheduleEntry[] schedule =
        [
            new(2, 600, ZoneConflictType.Conflict),
            new(2, 1200, ZoneConflictType.War)
        ];

        var atStart = ConflictZoneScheduleRules.Resolve(schedule, new DateTime(2026, 9, 7, 6, 0, 0));
        await Assert.That(atStart!.Value.State).IsEqualTo(ZoneConflictType.Conflict);
        await Assert.That(atStart.Value.NextChange).IsEqualTo(new DateTime(2026, 9, 7, 12, 0, 0));
    }

    [Test]
    public async Task Resolve_IgnoresInvalidRows()
    {
        ConflictZoneScheduleEntry[] schedule =
        [
            new(8, 600, ZoneConflictType.War),   // day 8 = invalid
            new(2, 2400, ZoneConflictType.War)   // 24:00 = invalid HHMM
        ];

        await Assert.That(ConflictZoneScheduleRules.Resolve(schedule, new DateTime(2026, 9, 7, 7, 0, 0))).IsNull();
        await Assert.That(ConflictZoneScheduleRules.Resolve([], new DateTime(2026, 9, 7, 7, 0, 0))).IsNull();
    }

    [Test]
    public async Task AdvanceFromCounter_CascadesOnEqualThresholdsLikeTheKillCycle()
    {
        // Shipped zones 14/15/16/22/23/26/27 use 50 for all five levels.
        int[] thresholds = [50, 50, 50, 50, 50];

        await Assert.That(ConflictZoneScheduleRules.AdvanceFromCounter(ZoneConflictType.Tension, 50, thresholds))
            .IsEqualTo(ZoneConflictType.Tension);
        await Assert.That(ConflictZoneScheduleRules.AdvanceFromCounter(ZoneConflictType.Tension, 51, thresholds))
            .IsEqualTo(ZoneConflictType.Conflict);

        // Already in a trouble state: the counter keeps climbing from there.
        await Assert.That(ConflictZoneScheduleRules.AdvanceFromCounter(ZoneConflictType.Danger, 51, thresholds))
            .IsEqualTo(ZoneConflictType.Conflict);

        // Conflict/War/Peace are not reached by a counter.
        await Assert.That(ConflictZoneScheduleRules.AdvanceFromCounter(ZoneConflictType.War, 51, thresholds))
            .IsEqualTo(ZoneConflictType.War);
    }

    [Test]
    public async Task AdvanceFromCounter_DoesNothingWhenTheZoneHasNoThresholds()
    {
        int[] none = [0, 0, 0, 0, 0];

        await Assert.That(ConflictZoneScheduleRules.AdvanceFromCounter(ZoneConflictType.Tension, 10_000, none))
            .IsEqualTo(ZoneConflictType.Tension);
    }

    [Test]
    public async Task AdvanceByParticipation_TakesTheHighestCounter()
    {
        // Shipped zone 14: 50 pvp kills / 300 npc kills / 15 quest completions per level.
        int[] pvp = [50, 50, 50, 50, 50];
        int[] npc = [300, 300, 300, 300, 300];
        int[] quest = [15, 15, 15, 15, 15];

        // Below every threshold.
        await Assert.That(ConflictZoneScheduleRules.AdvanceByParticipation(
            ZoneConflictType.Tension, 49, pvp, 299, npc, 14, quest)).IsEqualTo(ZoneConflictType.Tension);

        // Only the quest counter crossed.
        await Assert.That(ConflictZoneScheduleRules.AdvanceByParticipation(
            ZoneConflictType.Tension, 0, pvp, 0, npc, 16, quest)).IsEqualTo(ZoneConflictType.Conflict);

        // Only the npc counter crossed.
        await Assert.That(ConflictZoneScheduleRules.AdvanceByParticipation(
            ZoneConflictType.Tension, 0, pvp, 301, npc, 0, quest)).IsEqualTo(ZoneConflictType.Conflict);
    }
}
