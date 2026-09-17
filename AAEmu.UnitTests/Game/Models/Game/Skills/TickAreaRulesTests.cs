using System.Numerics;

using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The tick-area shape columns: <c>tick_area_angle</c>, <c>tick_area_front_angle</c> and
/// <c>tick_area_max_count</c>. The shipped content is overwhelmingly "no filter" (539 of the 603 tick
/// radii carry no wedge, 481 no cap), so most of these tests pin the no-op, which is what keeps those
/// buffs behaving exactly as they did before the columns were read.
/// </summary>
public class TickAreaRulesTests
{
    private static Unit At(Unit self, float x, float y, float z, float yaw = 0f)
    {
        self.Transform = new Transform(self, null, x, y, z, 0f, 0f, yaw);
        return self;
    }

    private static List<Unit> Units(params (float X, float Y, float Yaw)[] positions)
    {
        var units = new List<Unit>();
        foreach (var (x, y, yaw) in positions)
        {
            var unit = new Unit();
            units.Add(At(unit, x, y, 0f, yaw));
        }

        return units;
    }

    [Test]
    public async Task ConeSweep_FullCircleAndUnset_AreBothNoFilter()
    {
        // 536 of the 603 tick radii ship 360 and 3 ship 0; neither may filter anything.
        await Assert.That(TickAreaRules.ConeSweep(0)).IsEqualTo(0f);
        await Assert.That(TickAreaRules.ConeSweep(360)).IsEqualTo(0f);
        await Assert.That(TickAreaRules.HasCone(360, 0)).IsFalse();
    }

    [Test]
    [Arguments(20)]
    [Arguments(40)]
    [Arguments(90)]
    [Arguments(120)]
    [Arguments(180)]
    public async Task ConeSweep_RealWedge_IsTheColumnValue(int angle)
    {
        await Assert.That(TickAreaRules.ConeSweep(angle)).IsEqualTo(angle);
        await Assert.That(TickAreaRules.HasCone(angle, 0)).IsTrue();
    }

    [Test]
    public async Task Apply_NoColumns_ReturnsTheGatheredListUntouched()
    {
        var owner = At(new Unit(), 0f, 0f, 0f);
        // A target directly behind the owner: what a wedge would have to drop.
        var behind = Units((0f, -50f, 0f));
        var ahead = Units((0f, 50f, 0f));
        var gathered = new List<Unit> { behind[0], ahead[0] };

        var result = TickAreaRules.Apply(owner, gathered, 360, 0, 0);

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result).Contains(behind[0]);
        await Assert.That(result).Contains(ahead[0]);
    }

    [Test]
    public async Task InCone_WedgeInFront_KeepsTheTargetAheadAndDropsTheOneBehind()
    {
        // Yaw 0 faces +Y (MathUtil.CalculateAngleFrom subtracts 90 from the world bearing), so "ahead" is
        // (0, 50) and "behind" is (0, -50). Buff 1045's 120° tick wedge.
        var owner = At(new Unit(), 0f, 0f, 0f);
        var ahead = Units((0f, 50f, 0f))[0];
        var behind = Units((0f, -50f, 0f))[0];

        var result = TickAreaRules.InCone(owner, new List<Unit> { ahead, behind }, TickAreaRules.ConeSweep(120));

        await Assert.That(result).Contains(ahead);
        await Assert.That(result).DoesNotContain(behind);
    }

    [Test]
    public async Task InCone_WedgeIsTheFullSweep_So120IsHalfWayToTheSide()
    {
        var owner = At(new Unit(), 0f, 0f, 0f);
        var ahead = Units((0f, 50f, 0f))[0];
        var side = Units((50f, 0f, 0f))[0];

        // The column is the full sweep, so a 40 is a ±20° wedge and a 120 is ±60°: a target square to
        // the caster's side (90°) is outside both. A 180 is ±90° and is the wedge that reaches it.
        var wide = TickAreaRules.InCone(owner, new List<Unit> { ahead, side }, 120f);
        var narrow = TickAreaRules.InCone(owner, new List<Unit> { ahead, side }, 40f);
        var half = TickAreaRules.InCone(owner, new List<Unit> { ahead, side }, 180f);

        await Assert.That(wide).Contains(ahead);
        await Assert.That(wide).DoesNotContain(side);
        await Assert.That(narrow).Contains(ahead);
        await Assert.That(narrow).DoesNotContain(side);
        await Assert.That(half).Contains(side);
    }

    [Test]
    public async Task InCone_OwnerFacingTurns_FollowsTheOwnersYaw()
    {
        var ahead = Units((0f, 50f, 0f))[0];
        var facingAhead = At(new Unit(), 0f, 0f, 0f);
        // Same 120° wedge, owner turned 180°: the unit that was ahead is now behind.
        var facingAway = At(new Unit(), 0f, 0f, 0f, MathF.PI);

        var kept = TickAreaRules.InCone(facingAhead, new List<Unit> { ahead }, 120f);
        var dropped = TickAreaRules.InCone(facingAway, new List<Unit> { ahead }, 120f);

        await Assert.That(kept).Contains(ahead);
        await Assert.That(dropped).IsEmpty();
    }

    [Test]
    public async Task InCone_TheShipped180Wedge_IsAHalfCircle()
    {
        // buffs 21546/21547 carry tick_area_angle 180. As the full sweep that is ±90°, so a unit square
        // to the caster's side is inside it and one directly behind is not.
        var owner = At(new Unit(), 0f, 0f, 0f);
        var side = Units((50f, 0f, 0f))[0];
        var behind = Units((0f, -50f, 0f))[0];

        var sideways = TickAreaRules.InCone(owner, new List<Unit> { side }, TickAreaRules.ConeSweep(180));
        var backwards = TickAreaRules.InCone(owner, new List<Unit> { behind }, TickAreaRules.ConeSweep(180));

        await Assert.That(sideways).Contains(side);
        await Assert.That(backwards).DoesNotContain(behind);
    }

    [Test]
    public async Task Apply_BothWedges_AppliesTheNarrowerOne()
    {
        var owner = At(new Unit(), 0f, 0f, 0f);
        var ahead = Units((0f, 50f, 0f))[0];
        var side = Units((50f, 0f, 0f))[0];

        var angleNarrower = TickAreaRules.Apply(owner, new List<Unit> { ahead, side }, 40, 120, 0);
        var frontNarrower = TickAreaRules.Apply(owner, new List<Unit> { ahead, side }, 120, 40, 0);

        await Assert.That(angleNarrower.Count).IsEqualTo(1);
        await Assert.That(frontNarrower.Count).IsEqualTo(1);
        await Assert.That(angleNarrower[0]).IsEqualTo(ahead);
        await Assert.That(frontNarrower[0]).IsEqualTo(ahead);
    }

    [Test]
    public async Task MaxTargets_UnsetOrNegative_IsNoCap()
    {
        await Assert.That(TickAreaRules.MaxTargets(0)).IsEqualTo(0);
        await Assert.That(TickAreaRules.MaxTargets(-5)).IsEqualTo(0);
        await Assert.That(TickAreaRules.MaxTargets(30)).IsEqualTo(30);
    }

    [Test]
    public async Task Nearest_CapKeepsTheClosestUnitsInDistanceOrder()
    {
        var owner = At(new Unit(), 0f, 0f, 0f);
        var far = Units((90f, 0f, 0f))[0];
        var near = Units((10f, 0f, 0f))[0];
        var middle = Units((40f, 0f, 0f))[0];

        var result = TickAreaRules.Nearest(owner, new List<Unit> { far, near, middle }, 2);

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result[0]).IsEqualTo(near);
        await Assert.That(result[1]).IsEqualTo(middle);
    }

    [Test]
    public async Task Nearest_AtOrUnderTheCap_KeepsTheGatherOrder()
    {
        var owner = At(new Unit(), 0f, 0f, 0f);
        var far = Units((90f, 0f, 0f))[0];
        var near = Units((10f, 0f, 0f))[0];
        var gathered = new List<Unit> { far, near };

        // Exactly at the cap: buff 24611 caps at 1, but a list that already fits is not re-sorted.
        var result = TickAreaRules.Nearest(owner, gathered, 2);

        await Assert.That(result).IsSameReferenceAs(gathered);
    }

    [Test]
    public async Task Apply_WedgeThenCap_DropsTheFarUnitBehindAndCapsTheRest()
    {
        var owner = At(new Unit(), 0f, 0f, 0f);
        var behind = Units((0f, -10f, 0f))[0];
        var nearAhead = Units((0f, 20f, 0f))[0];
        var farAhead = Units((0f, 60f, 0f))[0];

        var result = TickAreaRules.Apply(
            owner, new List<Unit> { behind, farAhead, nearAhead }, 40, 0, 1);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0]).IsEqualTo(nearAhead);
    }

    [Test]
    public async Task Apply_NoOrigin_LeavesTheListAlone()
    {
        // A tick whose owner is not a Unit cannot be faced from; dropping its targets would be a much
        // bigger change than skipping the wedge.
        var ahead = Units((0f, 50f, 0f));
        var behind = Units((0f, -50f, 0f));
        var gathered = new List<Unit> { ahead[0], behind[0] };

        var result = TickAreaRules.Apply(null, gathered, 90, 0, 0);

        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Apply_EmptyGather_StaysEmpty()
    {
        var owner = At(new Unit(), 0f, 0f, 0f);

        var result = TickAreaRules.Apply(owner, new List<Unit>(), 90, 0, 5);

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Transform_PositionSurvivesTheTestHarness()
    {
        // Guards the helper above: the wedge and the cap both read Transform.World.Position.
        var unit = At(new Unit(), 3f, 4f, 5f, 0.5f);

        await Assert.That(unit.Transform.World.Position).IsEqualTo(new Vector3(3f, 4f, 5f));
    }
}
