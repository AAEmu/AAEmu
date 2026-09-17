using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.UnitTests.Game.Models.Game.Housing;

/// <summary>
/// Where a rebuilt house lands, and which skills its owner is taught for it. A remodel is not finished
/// work: the target's completion stage is what finishes it, and the client's window says so.
/// </summary>
public class HousingRebuildLandingTests
{
    [Test]
    public async Task NamesAHouse_ReadsTheWindowsNoHouseIdAsAHouse()
    {
        await Assert.That(HousingRebuildLanding.NamesAHouse(12)).IsTrue();
        await Assert.That(HousingRebuildLanding.NamesAHouse(HousingRebuildLanding.NoHouseId)).IsFalse();
        await Assert.That(HousingRebuildLanding.NamesAHouse(0)).IsFalse();
    }

    [Test]
    public async Task LandingStep_LeavesATargetWithStepsOnItsCompletionStage()
    {
        await Assert.That(HousingRebuildLanding.LandingStep(1)).IsEqualTo(0);
        await Assert.That(HousingRebuildLanding.LandingStep(3)).IsEqualTo(0);
    }

    [Test]
    public async Task LandingStep_FinishesATargetThatHasNoSteps()
    {
        await Assert.That(HousingRebuildLanding.LandingStep(0)).IsEqualTo(-1);
    }

    [Test]
    public async Task RequiredSkills_AddsTheStartAndCompletionSkillsWithoutRepeating()
    {
        var skills = HousingRebuildLanding.RequiredSkills([28829, 28828], [29291, 28829, 0]);

        await Assert.That(skills).IsEquivalentTo(new uint[] { 28829, 28828, 29291 });
    }

    [Test]
    public async Task RequiredSkills_KeepsTargetsThatShareAStartSkill()
    {
        // Three targets of one pack share the skill that starts them: the skill is learned once, and the
        // target is named by the cast, not by the skill.
        var skills = HousingRebuildLanding.RequiredSkills([28829, 28829, 28829], [29291]);

        await Assert.That(skills).IsEquivalentTo(new uint[] { 28829, 29291 });
    }

    [Test]
    public async Task MissingSkills_KeepsOnlyWhatTheOwnerDoesNotKnow()
    {
        var known = new HashSet<uint> { 29291 };

        var missing = HousingRebuildLanding.MissingSkills([28829, 28828, 29291], known.Contains);

        await Assert.That(missing).IsEquivalentTo(new uint[] { 28829, 28828 });
    }

    [Test]
    public async Task MissingSkills_IsEmptyWhenEverythingIsKnown()
    {
        var missing = HousingRebuildLanding.MissingSkills([28829], _ => true);

        await Assert.That(missing).IsEmpty();
    }
}

/// <summary>
/// Skill range is measured to the house origin. The plot garden radius is the building the player
/// is standing next to; without it a 4 m remodel skill dies at the door.
/// </summary>
public class HousingDistanceRulesTests
{
    [Test]
    public async Task OccupiedRadius_IsThePlotTimesTheHouseScale()
    {
        await Assert.That(HousingDistanceRules.OccupiedRadius(11f, 1f)).IsEqualTo(11f);
        await Assert.That(HousingDistanceRules.OccupiedRadius(11f, 2f)).IsEqualTo(22f);
    }

    [Test]
    public async Task OccupiedRadius_DoesNotGoNegative()
    {
        await Assert.That(HousingDistanceRules.OccupiedRadius(-3f, 1f)).IsEqualTo(0f);
        await Assert.That(HousingDistanceRules.OccupiedRadius(11f, -1f)).IsEqualTo(0f);
    }

    [Test]
    public async Task FarmhouseDoor_IsInsideAFourMetreRebuildSkill()
    {
        var fromDoorToOrigin = 4.1030974f;
        var afterPlot = fromDoorToOrigin - HousingDistanceRules.OccupiedRadius(11f, 1f);

        await Assert.That(afterPlot).IsLessThan(4f);
    }
}
