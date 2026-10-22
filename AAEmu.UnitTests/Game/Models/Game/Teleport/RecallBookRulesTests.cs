using System.Numerics;

using AAEmu.Game.Models.Game.Teleport;

namespace AAEmu.UnitTests.Game.Models.Game.Teleport;

public class RecallBookRulesTests
{
    [Test]
    public async Task IsBookEligible_RequiresReturnDistrictMembership()
    {
        var eligible = new HashSet<uint> { 806, 1 };
        await Assert.That(RecallBookRules.IsBookEligible(806, eligible)).IsTrue();
        await Assert.That(RecallBookRules.IsBookEligible(1, eligible)).IsTrue();
        await Assert.That(RecallBookRules.IsBookEligible(999, eligible)).IsFalse();
        await Assert.That(RecallBookRules.IsBookEligible(0, eligible)).IsFalse();
        await Assert.That(RecallBookRules.IsBookEligible(806, null)).IsFalse();
    }

    [Test]
    public async Task SubZonesForReturnPoint_EmptyWhenZoneHasNoAreas()
    {
        var byZone = new Dictionary<uint, List<SubZoneAreaFileRules.LocalArea>>();
        await Assert.That(RecallBookRules.SubZonesForReturnPoint(10, 10, 183, byZone).Count).IsEqualTo(0);
        await Assert.That(RecallBookRules.SubZonesForReturnPoint(10, 10, 183, null).Count).IsEqualTo(0);
    }

    [Test]
    public async Task SubZonesForReturnPoint_ReturnsContainingAreaIds()
    {
        var square = new List<Vector2>
        {
            new(0, 0), new(10, 0), new(10, 10), new(0, 10)
        };
        var byZone = new Dictionary<uint, List<SubZoneAreaFileRules.LocalArea>>
        {
            [183] =
            [
                new SubZoneAreaFileRules.LocalArea(183, 42, square),
                new SubZoneAreaFileRules.LocalArea(183, 99,
                [
                    new Vector2(100, 100), new Vector2(110, 100),
                    new Vector2(110, 110), new Vector2(100, 110)
                ])
            ]
        };

        var inside = RecallBookRules.SubZonesForReturnPoint(5, 5, 183, byZone);
        await Assert.That(inside.Count).IsEqualTo(1);
        await Assert.That(inside[0]).IsEqualTo(42u);

        var outside = RecallBookRules.SubZonesForReturnPoint(50, 50, 183, byZone);
        await Assert.That(outside.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ReturnDistrictKindName_IsReturn()
    {
        await Assert.That(RecallBookRules.ReturnDistrictKindName).IsEqualTo("return");
    }

    [Test]
    public async Task BuildChildrenByParent_SkipsRootsAndZeros()
    {
        var map = RecallBookRules.BuildChildrenByParent(
        [
            (41, 37),
            (358, 37),
            (37, 0),
            (0, 5),
            (42, 37)
        ]);
        await Assert.That(map.ContainsKey(37)).IsTrue();
        await Assert.That(map[37].Count).IsEqualTo(3);
        await Assert.That(map.ContainsKey(0)).IsFalse();
    }

    [Test]
    public async Task DescendantSubZones_WalksNestedChildren()
    {
        // Marianople 37 → Jewel 41, Old Market 358; 358 has no further children here.
        var children = RecallBookRules.BuildChildrenByParent([(41, 37), (358, 37), (100, 41)]);
        var under37 = RecallBookRules.DescendantSubZones(37, children);
        await Assert.That(under37.Count).IsEqualTo(3);
        await Assert.That(under37.Contains(41u)).IsTrue();
        await Assert.That(under37.Contains(358u)).IsTrue();
        await Assert.That(under37.Contains(100u)).IsTrue();

        var under41 = RecallBookRules.DescendantSubZones(41, children);
        await Assert.That(under41.Count).IsEqualTo(1);
        await Assert.That(under41[0]).IsEqualTo(100u);

        await Assert.That(RecallBookRules.DescendantSubZones(999, children).Count).IsEqualTo(0);
    }
}
