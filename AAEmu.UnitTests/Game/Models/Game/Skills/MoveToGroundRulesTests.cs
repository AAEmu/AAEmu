using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class MoveToGroundRulesTests
{
    [Test]
    public async Task LocationMarker_IsTheDestinationXy()
    {
        var mover = new BaseUnit { ObjId = 10 };
        mover.Transform = new Transform(mover, null, 100f, 200f, 50f);
        var marker = new BaseUnit { ObjId = uint.MaxValue };
        marker.Transform = new Transform(marker, null, 150f, 250f, 60f);

        var (x, y) = MoveToGroundRules.DestinationXy(mover, marker);
        await Assert.That(x).IsEqualTo(150f);
        await Assert.That(y).IsEqualTo(250f);
    }

    [Test]
    public async Task NoMarker_KeepsTheMoversXy()
    {
        var mover = new BaseUnit { ObjId = 10 };
        mover.Transform = new Transform(mover, null, 100f, 200f, 50f);
        var other = new BaseUnit { ObjId = 11 };
        other.Transform = new Transform(other, null, 150f, 250f, 60f);

        var (x, y) = MoveToGroundRules.DestinationXy(mover, other);
        await Assert.That(x).IsEqualTo(100f);
        await Assert.That(y).IsEqualTo(200f);
    }

    [Test]
    public async Task NullTarget_KeepsTheMoversXy()
    {
        var mover = new BaseUnit { ObjId = 10 };
        mover.Transform = new Transform(mover, null, 100f, 200f, 50f);

        var (x, y) = MoveToGroundRules.DestinationXy(mover, null);
        await Assert.That(x).IsEqualTo(100f);
        await Assert.That(y).IsEqualTo(200f);
    }

    [Test]
    public async Task CastAim_IsUsedWhenMarkerWasLost()
    {
        // Plot retarget to OriginalSource leaves the effect target as the caster (not MaxValue).
        // CastAim from Use still names the ground the player aimed at.
        var mover = new BaseUnit { ObjId = 10 };
        mover.Transform = new Transform(mover, null, 100f, 200f, 50f);
        var casterAsTarget = new BaseUnit { ObjId = 10 };
        casterAsTarget.Transform = new Transform(casterAsTarget, null, 100f, 200f, 50f);
        var aim = new SavedPosition(186, 1, 150f, 250f, 60f, 0f);

        var (x, y) = MoveToGroundRules.DestinationXy(mover, casterAsTarget, aim);
        await Assert.That(x).IsEqualTo(150f);
        await Assert.That(y).IsEqualTo(250f);
    }
}
