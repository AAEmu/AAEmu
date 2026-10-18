using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

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

    [Test]
    public async Task PreferCastOrigin_UsesCastStartWhenPresent()
    {
        // Plot-only blink applies save_pos after move_to_ground; the standing Transform at buff
        // attach is the landing. The mark must stay at Use-time CastOrigin.
        var castOrigin = Marked(zoneId: 186, instanceId: 1) with { X = 100f, Y = 200f, Z = 30f };
        var standing = new BaseUnit { ObjId = 1 };
        standing.Transform.KeepZoneQuietly(186);
        standing.Transform.Local.Position = new System.Numerics.Vector3(999f, 888f, 77f);

        var chosen = SavedPositionRules.PreferCastOrigin(castOrigin, standing);

        await Assert.That(chosen.X).IsEqualTo(100f);
        await Assert.That(chosen.Y).IsEqualTo(200f);
        await Assert.That(chosen.Z).IsEqualTo(30f);
    }

    [Test]
    public async Task PreferCastOrigin_FallsBackToStandingWhenNoCastOrigin()
    {
        var standing = new BaseUnit { ObjId = 1 };
        standing.Transform.KeepZoneQuietly(186);
        standing.Transform.Local.Position = new System.Numerics.Vector3(50f, 60f, 70f);

        var chosen = SavedPositionRules.PreferCastOrigin(null, standing);

        await Assert.That(chosen.X).IsEqualTo(50f);
        await Assert.That(chosen.Y).IsEqualTo(60f);
        await Assert.That(chosen.Z).IsEqualTo(70f);
    }
}
