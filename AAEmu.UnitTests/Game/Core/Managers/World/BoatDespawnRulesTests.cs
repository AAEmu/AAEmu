using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class BoatDespawnRulesTests
{
    private const uint Hull = 2339;
    private const uint Figurehead = 2346;
    private const uint AftSail = 2348;
    private const uint ForeSail = 2349;
    private const uint Ladder = 2365;
    private const uint ZoneLive = 186;
    private const uint ZonePending = 218;
    private const uint ZoneChild = 149;

    [Test]
    public async Task HostingZones_IncludeLivePendingAndAChildsOwnKey()
    {
        var zones = BoatDespawnRules.ZonesThatMayHoldAttachments(
            ZoneLive, ZonePending, [ZoneChild, 0]);

        await Assert.That(zones).IsEquivalentTo(new uint[] { ZoneChild, ZoneLive, ZonePending });
    }

    [Test]
    public async Task HostingZones_IgnoreEmptyKeys()
    {
        var zones = BoatDespawnRules.ZonesThatMayHoldAttachments(0, 0, [0]);

        await Assert.That(zones).IsEmpty();
    }

    [Test]
    public async Task HostingZones_DedupesTheSameKeyFromEverySource()
    {
        var zones = BoatDespawnRules.ZonesThatMayHoldAttachments(ZoneLive, ZoneLive, [ZoneLive]);

        await Assert.That(zones).IsEquivalentTo(new uint[] { ZoneLive });
    }

    [Test]
    public async Task UnitIds_ChildrenThenHull_SkipZeroAndHullDup()
    {
        var ids = BoatDespawnRules.UnitIdsToRemoveFromZone(
            Hull, [Figurehead, 0, Hull, AftSail, ForeSail, Figurehead]);

        await Assert.That(ids.Take(3)).IsEquivalentTo(new uint[] { Figurehead, AftSail, ForeSail });
        await Assert.That(ids[^1]).IsEqualTo(Hull);
    }

    [Test]
    public async Task DoodadIds_SkipZeroAndDupes()
    {
        var ids = BoatDespawnRules.DoodadIdsToRemoveFromZone([Ladder, 0, Ladder, 2366]);

        await Assert.That(ids).IsEquivalentTo(new uint[] { Ladder, 2366 });
    }

    [Test]
    public async Task HeldIds_CoverHullChildrenAndDoodadsUntilFinalize()
    {
        var held = BoatDespawnRules.ObjectIdsHeldUntilFinalize(
            Hull, [Figurehead, AftSail, ForeSail], [Ladder]);

        await Assert.That(held).IsEquivalentTo(
            new uint[] { Hull, Figurehead, AftSail, ForeSail, Ladder });
    }

    [Test]
    public async Task HeldIds_EmptyWhenNothingWasSpawned()
    {
        var held = BoatDespawnRules.ObjectIdsHeldUntilFinalize(0, null, null);

        await Assert.That(held).IsEmpty();
    }
}
