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
    public async Task AttachmentDoodads_StayWorldSide()
    {
        // The dedicate physicalizes a Created doodad as an immovable collider parented to the
        // hull; the ladder proxies sit inside the hull mesh and capsize it (Ostera, 2026-09-02).
        await Assert.That(BoatAttachmentAnnounceRules.AnnounceDoodadsToZone).IsFalse();
    }

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
    public async Task AnnounceBoatAttachmentsToZone_KeepsAttachmentDoodadsOutOfTheZone()
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

            // Ladders/helm/anchor are colliders in the dedicate that capsize the hull; they stay World-side.
            await Assert.That(seen).IsEmpty();
        }
        finally
        {
            WorldIntegration.ZoneAuthority = prevAuth;
            WorldIntegration.RelayCreateDoodadToZoneId = prevRelay;
        }
    }

    [Test]
    public async Task ChildAttachesForZone_SkipsUnsetPointsAndTheHull()
    {
        var attaches = BoatAttachmentAnnounceRules.ChildAttachesForZone(
            2339,
            [(2348, 11), (2339, 12), (0, 5), (2401, -1), (2402, 8)]);
        await Assert.That(attaches).IsEquivalentTo(new[] { (2348u, 2339u, (byte)11), (2402u, 2339u, (byte)8) });
    }

    [Test]
    public async Task AnnounceBoatAttachmentsToZone_AttachesEquipmentChildrenAfterCreate()
    {
        var prevAuth = WorldIntegration.ZoneAuthority;
        var prevState = WorldIntegration.RelayUnitStateToZone;
        var prevAttach = WorldIntegration.RelayUnitAttachToZoneId;
        WorldIntegration.ZoneAuthority = true;
        var created = new List<uint>();
        var attached = new List<(uint Zone, uint Child, uint Hull, byte Point)>();
        WorldIntegration.RelayUnitStateToZone = (_, objId, _) => created.Add(objId);
        WorldIntegration.RelayUnitAttachToZoneId = (zone, child, hull, point, on) =>
        {
            if (on)
                attached.Add((zone, child, hull, point));
        };
        try
        {
            var hull = new Slave { ObjId = 2339 };
            hull.AttachedSlaves.Add(new Slave { ObjId = 2348, AttachPointId = 11, ParentObj = hull });

            SlaveManager.AnnounceBoatAttachmentsToZone(hull, 186);

            await Assert.That(created).IsEquivalentTo(new uint[] { 2348 });
            await Assert.That(attached).IsEquivalentTo(new[] { (186u, 2348u, 2339u, (byte)11) });
        }
        finally
        {
            WorldIntegration.ZoneAuthority = prevAuth;
            WorldIntegration.RelayUnitStateToZone = prevState;
            WorldIntegration.RelayUnitAttachToZoneId = prevAttach;
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
