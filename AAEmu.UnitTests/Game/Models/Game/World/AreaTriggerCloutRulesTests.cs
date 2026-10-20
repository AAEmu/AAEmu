using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.World;

public class AreaTriggerCloutRulesTests
{
    [Test]
    [Arguments(true, false, true)]
    [Arguments(true, true, true)]
    [Arguments(false, true, true)]
    [Arguments(false, false, false)]
    public async Task TracksOccupantsWithoutBuff_AllowsEffectOnlyClouts(
        bool hasInsideBuff, bool hasEffects, bool expected)
    {
        await Assert.That(AreaTriggerCloutRules.TracksOccupantsWithoutBuff(hasInsideBuff, hasEffects))
            .IsEqualTo(expected);
    }

    [Test]
    [Arguments(true, false, true)]
    [Arguments(false, true, true)]
    [Arguments(false, false, false)]
    public async Task AppliesEffectsOnTick_AllowsEffectOnlyClouts(
        bool hasInsideBuff, bool hasEffects, bool expected)
    {
        await Assert.That(AreaTriggerCloutRules.AppliesEffectsOnTick(hasInsideBuff, hasEffects))
            .IsEqualTo(expected);
    }

    [Test]
    [Arguments(120, 200, true)]
    [Arguments(100, 200, true)]
    [Arguments(200, 200, true)]
    [Arguments(201, 200, false)]
    [Arguments(0, 200, false)]
    [Arguments(10000, 200, false)]
    public async Task NeedsImmediatePulse_WhenDurationFitsInManagerTick(
        int durationMs, int managerTickMs, bool expected)
    {
        await Assert.That(AreaTriggerCloutRules.NeedsImmediatePulse(durationMs, managerTickMs))
            .IsEqualTo(expected);
    }

    [Test]
    [Arguments(false, true)]
    [Arguments(true, false)]
    public async Task WaitUntilOwnerVisible_SkipsThePulseBeforeSpawn(bool ownerVisible, bool expected)
    {
        await Assert.That(AreaTriggerCloutRules.WaitUntilOwnerVisible(ownerVisible)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(false, true)]
    [Arguments(true, false)]
    public async Task NeedsPostSpawnPulse_WhenArmRanBeforeVisible(bool ownerWasVisibleAtArm, bool expected)
    {
        await Assert.That(AreaTriggerCloutRules.NeedsPostSpawnPulse(ownerWasVisibleAtArm)).IsEqualTo(expected);
    }

}
