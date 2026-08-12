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
}
