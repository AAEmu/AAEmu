using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Utils;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Plots;

public class PlotDirectionEngagedTests
{
    [Test]
    public async Task DirectionEngaged_IsOrOfEitherFacing()
    {
        var a = Place(0f, 0f, yawDegrees: 0f);
        var b = Place(0f, 5f, yawDegrees: 0f);

        var aFacesB = MathUtil.IsFront(a, b);
        var bFacesA = MathUtil.IsFront(b, a);

        await Assert.That(PlotConditionRules.DirectionEngaged(a, b))
            .IsEqualTo(aFacesB || bFacesA);
        await Assert.That(PlotConditionRules.DirectionEngaged(b, a))
            .IsEqualTo(aFacesB || bFacesA);
    }

    [Test]
    public async Task DirectionEngaged_BackAttack_PassesWhenOnlyAttackerFacesTarget()
    {
        // Find a yaw for the target such that the attacker is behind them, while the attacker
        // still looks at the target — the Relentless continuation case that one-way IsFront(target,
        // attacker) used to refuse.
        var target = Place(0f, 0f, yawDegrees: 0f);
        var attacker = Place(0f, -5f, yawDegrees: 0f);

        if (MathUtil.IsFront(target, attacker))
            target.Transform.Local.SetRotationDegree(0f, 0f, 180f);

        await Assert.That(MathUtil.IsFront(attacker, target)).IsTrue();
        await Assert.That(MathUtil.IsFront(target, attacker)).IsFalse();
        await Assert.That(PlotConditionRules.DirectionEngaged(target, attacker)).IsTrue();
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
