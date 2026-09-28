using AAEmu.Game.Models.Game.TowerDefs;

namespace AAEmu.UnitTests.Game.Models.Game.TowerDefs;

public class TowerDefScheduleMetadataTests
{
    private static TowerDef ToDRow(uint id, uint spawner, float tod = 0f, uint interval = 1) => new()
    {
        Id = id,
        TimeOfDay = tod,
        TimeOfDayDayInterval = interval,
        TargetNpcSpawnId = spawner,
        ForceEndTime = 3600f
    };

    private static TowerDef WallClockRow(uint id)
    {
        var towerDef = new TowerDef { Id = id, ForceEndTime = 3600f };
        towerDef.StartTimes[(int)DayOfWeek.Tuesday] = new TimeSpan(21, 30, 0);
        return towerDef;
    }

    [Test]
    public async Task Apply_MarksListedToDRowsGameTime()
    {
        var listed = ToDRow(171, 100);
        var omitted = ToDRow(3, 100);
        var result = TowerDefScheduleMetadata.Apply([listed, omitted], [171]);

        await Assert.That(listed.ScheduleMode).IsEqualTo(TowerDefScheduleMode.GameTime);
        await Assert.That(omitted.ScheduleMode).IsEqualTo(TowerDefScheduleMode.Manual);
        await Assert.That(result.AppliedGameTime).IsEqualTo(1);
        await Assert.That(result.UnlistedToDCandidates).IsEquivalentTo(new uint[] { 3 });
    }

    [Test]
    public async Task Apply_UnknownIds_AreReportedAndDoNotThrow()
    {
        var listed = ToDRow(13, 200);
        var result = TowerDefScheduleMetadata.Apply([listed], [13, 9999]);

        await Assert.That(listed.ScheduleMode).IsEqualTo(TowerDefScheduleMode.GameTime);
        await Assert.That(result.UnknownIds).IsEquivalentTo(new uint[] { 9999 });
    }

    [Test]
    public async Task Apply_WeekdaySlots_StayWallClockEvenIfListed()
    {
        var wall = WallClockRow(152);
        var result = TowerDefScheduleMetadata.Apply([wall], [152]);

        await Assert.That(wall.ScheduleMode).IsEqualTo(TowerDefScheduleMode.WallClock);
        await Assert.That(result.WallClockConflicts).IsEquivalentTo(new uint[] { 152 });
        await Assert.That(result.AppliedGameTime).IsEqualTo(0);
    }

    [Test]
    public async Task Apply_EmptyOverlay_LeavesToDRowsManualAndReportsThem()
    {
        var row = ToDRow(13, 200);
        var result = TowerDefScheduleMetadata.Apply([row], []);

        await Assert.That(row.ScheduleMode).IsEqualTo(TowerDefScheduleMode.Manual);
        await Assert.That(result.AppliedGameTime).IsEqualTo(0);
        await Assert.That(result.UnlistedToDCandidates).IsEquivalentTo(new uint[] { 13 });
    }

    [Test]
    public async Task Apply_DoesNotUseDisplayNames()
    {
        var row = ToDRow(171, 100);
        row.Name = "renamed-without-markers";
        TowerDefScheduleMetadata.Apply([row], [171]);
        await Assert.That(row.IsGameTimeScheduled).IsTrue();
        await Assert.That(row.Name).IsEqualTo("renamed-without-markers");
    }

    [Test]
    public async Task SharesPortalSpawnerWith_UsesLoadedSpawnerId()
    {
        var baseRow = ToDRow(3, 9846);
        var expand = ToDRow(171, 9846);
        var other = ToDRow(13, 14335);

        await Assert.That(baseRow.SharesPortalSpawnerWith(expand)).IsTrue();
        await Assert.That(expand.SharesPortalSpawnerWith(other)).IsFalse();
        await Assert.That(baseRow.SharesPortalSpawnerWith(null)).IsFalse();
    }

    [Test]
    public async Task Apply_ListedRowMissingGameTimeColumns_IsReportedIneligible()
    {
        var row = new TowerDef { Id = 50, ForceEndTime = 3600f };
        var result = TowerDefScheduleMetadata.Apply([row], [50]);

        await Assert.That(row.ScheduleMode).IsEqualTo(TowerDefScheduleMode.Manual);
        await Assert.That(result.IneligibleIds).IsEquivalentTo(new uint[] { 50 });
        await Assert.That(result.AppliedGameTime).IsEqualTo(0);
    }

    [Test]
    public async Task ApplyWallClockStartTimes_FillsEmptySlotsAndMarksWallClock()
    {
        var abyss = ToDRow(36, 16859);
        var overlay = new Dictionary<uint, Dictionary<string, string>>
        {
            [36] = new Dictionary<string, string>
            {
                ["Tuesday"] = "22:00",
                ["Thursday"] = "22:00",
                ["Saturday"] = "22:00"
            }
        };

        var wall = TowerDefScheduleMetadata.ApplyWallClockStartTimes([abyss], overlay);
        var result = TowerDefScheduleMetadata.Apply([abyss], []);

        await Assert.That(wall.AppliedSlots).IsEqualTo(3);
        await Assert.That(wall.Conflicts).IsEmpty();
        await Assert.That(abyss.StartTimeFor(DayOfWeek.Tuesday)).IsEqualTo(new TimeSpan(22, 0, 0));
        await Assert.That(abyss.StartTimeFor(DayOfWeek.Sunday)).IsNull();
        await Assert.That(abyss.ScheduleMode).IsEqualTo(TowerDefScheduleMode.WallClock);
        await Assert.That(result.UnlistedToDCandidates).IsEmpty();
    }

    [Test]
    public async Task ApplyWallClockStartTimes_DoesNotOverwriteExistingSlot()
    {
        var row = new TowerDef { Id = 36, ForceEndTime = 7200f, TargetNpcSpawnId = 16859 };
        row.StartTimes[(int)DayOfWeek.Tuesday] = new TimeSpan(21, 0, 0);
        var overlay = new Dictionary<uint, Dictionary<string, string>>
        {
            [36] = new Dictionary<string, string> { ["Tuesday"] = "22:00" }
        };

        var wall = TowerDefScheduleMetadata.ApplyWallClockStartTimes([row], overlay);
        await Assert.That(wall.AppliedSlots).IsEqualTo(0);
        await Assert.That(wall.Conflicts.Count).IsEqualTo(1);
        await Assert.That(row.StartTimeFor(DayOfWeek.Tuesday)).IsEqualTo(new TimeSpan(21, 0, 0));
    }

    [Test]
    public async Task ApplyWallClockStartTimes_AcceptsDayIndexKeys()
    {
        var row = new TowerDef { Id = 36, ForceEndTime = 7200f, TargetNpcSpawnId = 16859 };
        var overlay = new Dictionary<uint, Dictionary<string, string>>
        {
            [36] = new Dictionary<string, string> { ["2"] = "22:00" }
        };

        TowerDefScheduleMetadata.ApplyWallClockStartTimes([row], overlay);
        await Assert.That(row.StartTimeFor(DayOfWeek.Tuesday)).IsEqualTo(new TimeSpan(22, 0, 0));
    }

    [Test]
    public async Task ApplyFollowOn_LinksSourceToTargetById()
    {
        var fight = new TowerDef { Id = 36, ForceEndTime = 7200f, TargetNpcSpawnId = 16859 };
        var reward = new TowerDef { Id = 37, ForceEndTime = 3600f, TargetNpcSpawnId = 16918 };
        var result = TowerDefScheduleMetadata.ApplyFollowOn(
            [fight, reward],
            new Dictionary<uint, uint> { [36] = 37 });

        await Assert.That(fight.FollowOnTowerDefId).IsEqualTo(37u);
        await Assert.That(reward.FollowOnTowerDefId).IsEqualTo(0u);
        await Assert.That(result.Applied).IsEqualTo(1);
        await Assert.That(result.UnknownSourceIds).IsEmpty();
        await Assert.That(result.UnknownTargetIds).IsEmpty();
        await Assert.That(result.SelfRefs).IsEmpty();
    }

    [Test]
    public async Task ApplyFollowOn_ReportsUnknownAndSelfRefs()
    {
        var fight = new TowerDef { Id = 36, ForceEndTime = 7200f };
        var reward = new TowerDef { Id = 37, ForceEndTime = 3600f };
        var result = TowerDefScheduleMetadata.ApplyFollowOn(
            [fight, reward],
            new Dictionary<uint, uint>
            {
                [999] = 37,
                [36] = 888,
                [37] = 37
            });

        await Assert.That(fight.FollowOnTowerDefId).IsEqualTo(0u);
        await Assert.That(result.Applied).IsEqualTo(0);
        await Assert.That(result.UnknownSourceIds).IsEquivalentTo(new uint[] { 999 });
        await Assert.That(result.UnknownTargetIds).IsEquivalentTo(new uint[] { 888 });
        await Assert.That(result.SelfRefs).IsEquivalentTo(new uint[] { 37 });
    }

    // --- start_day_of_week_bit narrowing (bit N == StartTimes[N], bit 0 == Sunday) ---
    // The bit patterns below are synthetic fixtures for the bit mapping, not rows copied out of
    // any tower_defs table: ApplyStartDayOfWeekBit must derive the weekdays purely from the mask
    // carried on the row.

    private static readonly DayOfWeek[] AllDays =
    [
        DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday
    ];

    /// <summary>A synthetic row whose seven weekday slots are all populated.</summary>
    private static TowerDef EveryDayArmed()
    {
        var towerDef = new TowerDef { ForceEndTime = 3600f };
        for (var day = 0; day < AllDays.Length; day++)
            towerDef.StartTimes[day] = new TimeSpan(21, 30, 0);
        return towerDef;
    }

    [Test]
    public async Task ApplyStartDayOfWeekBit_ZeroMask_LeavesEverySlotUntouched()
    {
        // start_day_of_week_bit is optional: rows that leave it unset keep all seven weekdays.
        var towerDef = EveryDayArmed();
        towerDef.StartDayOfWeekBit = 0;

        TowerDefScheduleMetadata.ApplyStartDayOfWeekBit(towerDef);

        await Assert.That(towerDef.IsScheduled).IsTrue();
        foreach (var day in AllDays)
            await Assert.That(towerDef.StartTimeFor(day)).IsEqualTo(new TimeSpan(21, 30, 0));
    }

    [Test]
    public async Task ApplyStartDayOfWeekBit_NarrowMask_KeepsOnlyTheMaskedWeekdays()
    {
        // 0b1010000 = bits 4 and 6 = Thursday and Saturday. Without the narrowing the row would
        // arm 21:30 on all seven days and fire daily.
        var towerDef = EveryDayArmed();
        towerDef.StartDayOfWeekBit = 0b1010000u;

        TowerDefScheduleMetadata.ApplyStartDayOfWeekBit(towerDef);

        await Assert.That(towerDef.StartTimeFor(DayOfWeek.Thursday)).IsEqualTo(new TimeSpan(21, 30, 0));
        await Assert.That(towerDef.StartTimeFor(DayOfWeek.Saturday)).IsEqualTo(new TimeSpan(21, 30, 0));
        await Assert.That(towerDef.StartTimeFor(DayOfWeek.Sunday)).IsNull();
        await Assert.That(towerDef.StartTimeFor(DayOfWeek.Monday)).IsNull();
        await Assert.That(towerDef.StartTimeFor(DayOfWeek.Tuesday)).IsNull();
        await Assert.That(towerDef.StartTimeFor(DayOfWeek.Wednesday)).IsNull();
        await Assert.That(towerDef.StartTimeFor(DayOfWeek.Friday)).IsNull();
        await Assert.That(towerDef.IsScheduled).IsTrue();
    }

    [Test]
    [Arguments(0b0000000u, new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday })]
    [Arguments(0b0000001u, new[] { DayOfWeek.Sunday })]
    [Arguments(0b0000010u, new[] { DayOfWeek.Monday })]
    [Arguments(0b1000000u, new[] { DayOfWeek.Saturday })]
    [Arguments(0b1010000u, new[] { DayOfWeek.Thursday, DayOfWeek.Saturday })]
    [Arguments(0b1000100u, new[] { DayOfWeek.Tuesday, DayOfWeek.Saturday })]
    [Arguments(0b0100010u, new[] { DayOfWeek.Monday, DayOfWeek.Friday })]
    [Arguments(0b1001000u, new[] { DayOfWeek.Wednesday, DayOfWeek.Saturday })]
    [Arguments(0b1111111u, new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday })]
    public async Task AllowsWeekday_BitNIsStartTimesIndexN(uint mask, DayOfWeek[] expected)
    {
        // Bit 0 must be Sunday (the un-suffixed start_hour / start_minute pair) and bit 6 Saturday
        // (start_hour6) — the mapping the loader's start_hourN fill and StartTimes index share.
        foreach (var day in AllDays)
            await Assert.That(TowerDefScheduleMetadata.AllowsWeekday(mask, day))
                .IsEqualTo(expected.Contains(day));
    }
}
