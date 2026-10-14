using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.UnitTests.Game.Models.Game.NPChar;

public class OnSpawnPlotWorldGateTests
{
    [Test]
    public async Task CrimsonStage_PlotOnly_Runs()
    {
        await Assert.That(OnSpawnPlotWorldGate.ShouldRun(
            zoneAuthority: true,
            isZoneMirror: true,
            isPriorityMirror: true,
            hasPlot: true,
            plotOnly: true,
            directSkillEffectCount: 0)).IsTrue();
    }

    [Test]
    public async Task LuscaStage_PlotWithoutPlotOnly_Runs()
    {
        await Assert.That(OnSpawnPlotWorldGate.ShouldRun(
            zoneAuthority: true,
            isZoneMirror: true,
            isPriorityMirror: true,
            hasPlot: true,
            plotOnly: false,
            directSkillEffectCount: 0)).IsTrue();
    }

    [Test]
    public async Task SeedOpenFx_PlotPlusDirectEffects_Skipped()
    {
        await Assert.That(OnSpawnPlotWorldGate.ShouldRun(
            zoneAuthority: true,
            isZoneMirror: true,
            isPriorityMirror: true,
            hasPlot: true,
            plotOnly: false,
            directSkillEffectCount: 1)).IsFalse();
    }

    [Test]
    public async Task Recruiter_NoPlot_Skipped()
    {
        await Assert.That(OnSpawnPlotWorldGate.ShouldRun(
            zoneAuthority: true,
            isZoneMirror: true,
            isPriorityMirror: true,
            hasPlot: false,
            plotOnly: false,
            directSkillEffectCount: 14)).IsFalse();
    }

    [Test]
    public async Task AmbientNpc_NotPriority_Skipped()
    {
        await Assert.That(OnSpawnPlotWorldGate.ShouldRun(
            zoneAuthority: true,
            isZoneMirror: true,
            isPriorityMirror: false,
            hasPlot: true,
            plotOnly: false,
            directSkillEffectCount: 0)).IsFalse();
    }

    [Test]
    public async Task EventScript_PlotOnly_RunsWithoutPriority()
    {
        // The hunter summoned onto an army stands still unless its plot_only OnSpawn graph runs:
        // the dedic never fires plot_only skills, and it is not a tower-priority mirror.
        await Assert.That(OnSpawnPlotWorldGate.ShouldRun(
            zoneAuthority: true,
            isZoneMirror: true,
            isPriorityMirror: false,
            hasPlot: true,
            plotOnly: true,
            directSkillEffectCount: 0)).IsTrue();
    }

    [Test]
    public async Task NotAZoneMirror_PlotOnly_Skipped()
    {
        await Assert.That(OnSpawnPlotWorldGate.ShouldRun(
            zoneAuthority: true,
            isZoneMirror: false,
            isPriorityMirror: false,
            hasPlot: true,
            plotOnly: true,
            directSkillEffectCount: 0)).IsFalse();
    }

    [Test]
    public async Task WithoutZoneAuthority_Skipped()
    {
        await Assert.That(OnSpawnPlotWorldGate.ShouldRun(
            zoneAuthority: false,
            isZoneMirror: true,
            isPriorityMirror: true,
            hasPlot: true,
            plotOnly: true,
            directSkillEffectCount: 0)).IsFalse();
    }
}
