using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class BlinkStaleMoveRulesTests
{
    [Test]
    public async Task CastStand_FarFromLanding_IsStale()
    {
        await Assert.That(BlinkStaleMoveRules.IsStaleAwayFromLanding(
            100f, 200f, 150f, 250f)).IsTrue();
    }

    [Test]
    public async Task NearLanding_IsNotStale()
    {
        await Assert.That(BlinkStaleMoveRules.IsStaleAwayFromLanding(
            152f, 251f, 150f, 250f)).IsFalse();
    }

    [Test]
    public async Task ShouldDrop_OnlyInsideWindow_AndFar()
    {
        var now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        var until = now.AddMilliseconds(1000);

        await Assert.That(BlinkStaleMoveRules.ShouldDrop(
            now, until, 100f, 200f, 150f, 250f)).IsTrue();
        await Assert.That(BlinkStaleMoveRules.ShouldDrop(
            now, until, 152f, 251f, 150f, 250f)).IsFalse();
        await Assert.That(BlinkStaleMoveRules.ShouldDrop(
            until, until, 100f, 200f, 150f, 250f)).IsFalse();
        await Assert.That(BlinkStaleMoveRules.ShouldDrop(
            until.AddMilliseconds(1), until, 100f, 200f, 150f, 250f)).IsFalse();
    }
}
