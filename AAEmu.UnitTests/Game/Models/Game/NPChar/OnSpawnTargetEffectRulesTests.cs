using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.UnitTests.Game.Models.Game.NPChar;

public class OnSpawnTargetEffectRulesTests
{
    /// <summary>An event start script: plotless, plot_only, buff gated on the target's buff tag.</summary>
    private static OnSpawnTargetEffectRules.TargetEffect[] StartScriptEffects() =>
        [new OnSpawnTargetEffectRules.TargetEffect(TargetBuffTagId: 4181, TargetNpcTagId: 0, CheckTargetTagSrc: false)];

    [Test]
    public async Task EventStartScript_TargetBuffTag_Runs()
    {
        await Assert.That(OnSpawnTargetEffectRules.ShouldWorldRun(
            zoneAuthority: true,
            isZoneMirror: true,
            isPriorityMirror: true,
            hasPlot: false,
            effects: StartScriptEffects())).IsTrue();
    }

    [Test]
    public async Task EventStartScript_WithPlot_IsLeftToThePlotPath()
    {
        await Assert.That(OnSpawnTargetEffectRules.ShouldWorldRun(
            zoneAuthority: true,
            isZoneMirror: true,
            isPriorityMirror: true,
            hasPlot: true,
            effects: StartScriptEffects())).IsFalse();
    }

    [Test]
    public async Task SelfBuff_NoTargetTag_IsNotRun()
    {
        await Assert.That(OnSpawnTargetEffectRules.ShouldWorldRun(
            zoneAuthority: true,
            isZoneMirror: true,
            isPriorityMirror: true,
            hasPlot: false,
            effects: [new OnSpawnTargetEffectRules.TargetEffect(0, 0, false)])).IsFalse();
    }

    [Test]
    public async Task ChainSkill_TagReadOffTheCaster_IsNotRun()
    {
        await Assert.That(OnSpawnTargetEffectRules.ShouldWorldRun(
            zoneAuthority: true,
            isZoneMirror: true,
            isPriorityMirror: true,
            hasPlot: false,
            effects: [new OnSpawnTargetEffectRules.TargetEffect(4181, 0, CheckTargetTagSrc: true)])).IsFalse();
    }

    [Test]
    public async Task NpcTagTarget_Runs()
    {
        await Assert.That(OnSpawnTargetEffectRules.ShouldWorldRun(
            zoneAuthority: true,
            isZoneMirror: true,
            isPriorityMirror: true,
            hasPlot: false,
            effects: [new OnSpawnTargetEffectRules.TargetEffect(0, 1234, false)])).IsTrue();
    }

    [Test]
    public async Task NoEffects_IsNotRun()
    {
        await Assert.That(OnSpawnTargetEffectRules.ShouldWorldRun(
            zoneAuthority: true,
            isZoneMirror: true,
            isPriorityMirror: true,
            hasPlot: false,
            effects: [])).IsFalse();
    }

    [Test]
    public async Task NotAPriorityMirror_IsNotRun()
    {
        await Assert.That(OnSpawnTargetEffectRules.ShouldWorldRun(
            zoneAuthority: true,
            isZoneMirror: true,
            isPriorityMirror: false,
            hasPlot: false,
            effects: StartScriptEffects())).IsFalse();
    }

    [Test]
    public async Task WithoutZoneAuthority_IsNotRun()
    {
        await Assert.That(OnSpawnTargetEffectRules.ShouldWorldRun(
            zoneAuthority: false,
            isZoneMirror: true,
            isPriorityMirror: true,
            hasPlot: false,
            effects: StartScriptEffects())).IsFalse();
    }
}
