using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Utils;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Plots;

public class PlotDirectionEngagedTests
{
    [Test]
    public async Task DirectionEngaged_IsTargetFacesSource()
    {
        var source = Place(0f, 0f, yawDegrees: 0f);
        var target = Place(0f, 5f, yawDegrees: 180f);

        await Assert.That(MathUtil.IsFront(target, source)).IsTrue();
        await Assert.That(PlotConditionRules.DirectionEngaged(source, target)).IsTrue();
    }

    [Test]
    public async Task DirectionEngaged_BackAttack_FailsWhenTargetLooksAway()
    {
        // Attacker behind the target: target does not face source → direction fails (NOT branch wins).
        var target = Place(0f, 0f, yawDegrees: 0f);
        var attacker = Place(0f, -5f, yawDegrees: 0f);

        if (MathUtil.IsFront(target, attacker))
            target.Transform.Local.SetRotationDegree(0f, 0f, 180f);

        await Assert.That(MathUtil.IsFront(attacker, target)).IsTrue();
        await Assert.That(MathUtil.IsFront(target, attacker)).IsFalse();
        await Assert.That(PlotConditionRules.DirectionEngaged(attacker, target)).IsFalse();
    }

    [Test]
    public async Task DirectionEngaged_Null_IsFalse()
    {
        var unit = Place(0f, 0f, yawDegrees: 0f);
        await Assert.That(PlotConditionRules.DirectionEngaged(null, unit)).IsFalse();
        await Assert.That(PlotConditionRules.DirectionEngaged(unit, null)).IsFalse();
    }

    private static Unit Place(float x, float y, float yawDegrees)
    {
        var unit = new Unit { ObjId = (uint)Math.Abs(x * 1000 + y * 10 + yawDegrees + 1) };
        unit.Transform = new Transform(unit, null);
        unit.Transform.Local.SetPosition(x, y, 0f);
        unit.Transform.Local.SetRotationDegree(0f, 0f, yawDegrees);
        return unit;
    }
}
