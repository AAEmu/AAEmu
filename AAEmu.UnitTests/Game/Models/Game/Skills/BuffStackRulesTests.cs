using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class BuffStackRulesTests
{
    /// <summary>The sail-trim family ceiling: one instance per sail, sixty applications each.</summary>
    private const int SailTrimMaxStack = 60;

    [Test]
    public async Task CanGrow_AcceptsApplicationsBelowTheCeiling()
    {
        await Assert.That(BuffStackRules.CanGrow(1, SailTrimMaxStack)).IsTrue();
        await Assert.That(BuffStackRules.CanGrow(59, SailTrimMaxStack)).IsTrue();
    }

    [Test]
    public async Task CanGrow_StopsAtTheCeiling()
    {
        await Assert.That(BuffStackRules.CanGrow(SailTrimMaxStack, SailTrimMaxStack)).IsFalse();
    }

    [Test]
    public async Task CanGrow_StopsAboveTheCeiling()
    {
        // A member restored from a save could exceed a ceiling that has since been lowered.
        await Assert.That(BuffStackRules.CanGrow(SailTrimMaxStack + 1, SailTrimMaxStack)).IsFalse();
    }

    [Test]
    public async Task CanGrow_RefusesFamiliesThatDoNotStack()
    {
        // Ceilings of zero and one both mean "one application"; growing either would let a single-stack
        // buff double its modifiers instead of simply refreshing.
        await Assert.That(BuffStackRules.CanGrow(1, 0)).IsFalse();
        await Assert.That(BuffStackRules.CanGrow(1, 1)).IsFalse();
    }

    [Test]
    public async Task ShouldTransform_FiresAtTheCeilingWhenATransformIsNamed()
    {
        // Tension 5793 → line-broken 5794 at 20. Sail trim has no transform and must stay put.
        await Assert.That(BuffStackRules.ShouldTransform(20, 20, 5794)).IsTrue();
        await Assert.That(BuffStackRules.ShouldTransform(19, 20, 5794)).IsFalse();
        await Assert.That(BuffStackRules.ShouldTransform(20, 20, 0)).IsFalse();
        await Assert.That(BuffStackRules.ShouldTransform(1, 1, 5794)).IsFalse();
    }

    [Test]
    public async Task ScaledModifier_GrowsSailTrimBySixPerStack()
    {
        // Sail trim is +6 move_speed_mul per application, ceiling 60. The first tick is +6;
        // sixty ticks are +360. A flat +36% on the first application is the ceiling, not the start.
        await Assert.That(BuffStackRules.ScaledModifier(6, 0, 1, 1)).IsEqualTo(6);
        await Assert.That(BuffStackRules.ScaledModifier(6, 0, 1, 2)).IsEqualTo(12);
        await Assert.That(BuffStackRules.ScaledModifier(6, 0, 1, 60)).IsEqualTo(360);
        await Assert.That(BuffStackRules.ScaledModifier(6, 0, 1, 0)).IsEqualTo(6);
    }

    [Test]
    public async Task Refresh_KeepsAPermanentInstance()
    {
        // Fishing 4053 is duration 0 / Refresh. A second apply must not overwrite.
        await Assert.That(BuffStackRules.ShouldOverwriteOnRefresh(0, 0)).IsFalse();
        await Assert.That(BuffStackRules.ShouldOverwriteOnRefresh(1500, 0)).IsTrue();
        await Assert.That(BuffStackRules.ShouldOverwriteOnRefresh(0, 1500)).IsTrue();
        await Assert.That(BuffStackRules.ShouldOverwriteOnRefresh(1500, 800)).IsTrue();
    }

    [Test]
    public async Task DispelTask_OnlyForTimedOrTicking()
    {
        await Assert.That(BuffStackRules.ShouldScheduleDispel(0, 0)).IsFalse();
        await Assert.That(BuffStackRules.ShouldScheduleDispel(1500, 0)).IsTrue();
        await Assert.That(BuffStackRules.ShouldScheduleDispel(0, 800)).IsTrue();
    }
}
