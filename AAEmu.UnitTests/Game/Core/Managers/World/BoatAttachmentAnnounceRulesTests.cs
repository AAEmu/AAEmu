using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

[NotInParallel]
public class BoatAttachmentAnnounceRulesTests
{
    [Test]
    public async Task UnitIdsToCreateInZone_HullThenParentThenChild()
    {
        var ids = BoatAttachmentAnnounceRules.UnitIdsToCreateInZone(2339, [2401, 2348]);
        await Assert.That(ids).IsEquivalentTo(new uint[] { 2339, 2348, 2401 });
    }

    [Test]
    public async Task UnitIdsToCreateInZone_IsTheReverseOfRemoveOrder()
    {
        uint[] deepestFirst = [2401, 2348];
        var create = BoatAttachmentAnnounceRules.UnitIdsToCreateInZone(2339, deepestFirst);
        var remove = BoatDespawnRules.UnitIdsToRemoveFromZone(2339, deepestFirst);
        await Assert.That(create).IsEquivalentTo(remove.Reverse());
    }

    [Test]
    public async Task UnitIdsToCreateInZone_EmptyChildrenIsJustTheHull()
    {
        await Assert.That(BoatAttachmentAnnounceRules.UnitIdsToCreateInZone(2339, [])).IsEquivalentTo(new uint[] { 2339 });
        await Assert.That(BoatAttachmentAnnounceRules.UnitIdsToCreateInZone(0, [2348])).IsEquivalentTo(new uint[] { 2348 });
    }

    [Test]
    public async Task AnnounceBoatAttachmentsToZone_CreatesDoodadsOnTheTargetZone()
    {
        var prevAuth = WorldIntegration.ZoneAuthority;
        var prevRelay = WorldIntegration.RelayCreateDoodadToZoneId;
        WorldIntegration.ZoneAuthority = true;
        var seen = new List<(uint Zone, uint Id)>();
        WorldIntegration.RelayCreateDoodadToZoneId = (zone, obj) =>
        {
            if (obj is Doodad doodad)
                seen.Add((zone, doodad.ObjId));
        };
        try
        {
            var hull = new Slave { ObjId = 2339 };
            hull.AttachedDoodads.Add(new Doodad { ObjId = 2342 });

            SlaveManager.AnnounceBoatAttachmentsToZone(hull, 186);

            await Assert.That(seen).IsEquivalentTo(new[] { (186u, 2342u) });
        }
        finally
        {
            WorldIntegration.ZoneAuthority = prevAuth;
            WorldIntegration.RelayCreateDoodadToZoneId = prevRelay;
        }
    }

    [Test]
    public async Task AnnounceBoatAttachmentsToZone_UnknownZoneDoesNothing()
    {
        var prevAuth = WorldIntegration.ZoneAuthority;
        var prevRelay = WorldIntegration.RelayCreateDoodadToZoneId;
        WorldIntegration.ZoneAuthority = true;
        var called = false;
        WorldIntegration.RelayCreateDoodadToZoneId = (_, _) => called = true;
        try
        {
            var hull = new Slave { ObjId = 2339 };
            hull.AttachedDoodads.Add(new Doodad { ObjId = 2342 });
            SlaveManager.AnnounceBoatAttachmentsToZone(hull, 0);
            await Assert.That(called).IsFalse();
        }
        finally
        {
            WorldIntegration.ZoneAuthority = prevAuth;
            WorldIntegration.RelayCreateDoodadToZoneId = prevRelay;
        }
    }
}
