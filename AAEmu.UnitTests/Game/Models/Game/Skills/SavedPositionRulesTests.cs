using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SavedPositionRulesTests
{
    private static SavedPosition Marked(uint zoneId = 186, uint instanceId = 1) =>
        new(ZoneId: zoneId, InstanceId: instanceId, X: 1234.5f, Y: 6789.25f, Z: 42.5f, YawRad: 1.5f);

    [Test]
    public async Task NoMarkingBuff_IsRefused()
    {
        await Assert.That(SavedPositionRules.CanReturnTo(null, 186, 1)).IsFalse();
    }

    [Test]
    public async Task MarkFromTheSameZone_IsAllowed()
    {
        await Assert.That(SavedPositionRules.CanReturnTo(Marked(), 186, 1)).IsTrue();
    }

    [Test]
    public async Task MarkFromAnotherZone_IsRefused()
    {
        // 41487 급습 marks in the zone the dash happens in; walking through a portal to 187 with the
        // marking buff still up must not write those coordinates into 187's transform.
        await Assert.That(SavedPositionRules.CanReturnTo(Marked(zoneId: 186), 187, 1)).IsFalse();
    }

    [Test]
    public async Task MarkFromAnotherInstance_IsRefused()
    {
        await Assert.That(SavedPositionRules.CanReturnTo(Marked(instanceId: 3), 186, 1)).IsFalse();
    }

    [Test]
    public async Task MarkKeepsTheCoordinatesAndRotationItCaptured()
    {
        var marked = Marked();

        await Assert.That(marked.X).IsEqualTo(1234.5f);
        await Assert.That(marked.Y).IsEqualTo(6789.25f);
        await Assert.That(marked.Z).IsEqualTo(42.5f);
        await Assert.That(marked.YawRad).IsEqualTo(1.5f);
    }
}
