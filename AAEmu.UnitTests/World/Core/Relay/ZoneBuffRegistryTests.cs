using AAEmu.Game.Models.Game.Skills;
using AAEmu.World.Core.Relay;

namespace AAEmu.UnitTests.World.Core.Relay;

public class ZoneBuffRegistryTests
{
    private static uint NextZone() => (uint)Interlocked.Increment(ref _zoneCounter);
    private static int _zoneCounter = 4100;

    [Test]
    public async Task Create_MakesUpdateAndRemoveVisibleOnlyForThatZone()
    {
        var zoneA = NextZone();
        var zoneB = NextZone();
        const uint owner = 1876;
        const uint index = 41;

        ZoneBuffRegistry.MarkCreated(zoneA, 0, owner, index);

        await Assert.That(ZoneBuffRegistry.WasCreated(zoneA, 0, owner, index)).IsTrue();
        await Assert.That(ZoneBuffRegistry.WasCreated(zoneB, 0, owner, index)).IsFalse();

        ZoneBuffRegistry.Clear(zoneA, 0, owner, index);
        await Assert.That(ZoneBuffRegistry.WasCreated(zoneA, 0, owner, index)).IsFalse();
    }

    [Test]
    public async Task SameIndexOnDifferentUnitsAndZonesIsIndependent()
    {
        var zone = NextZone();
        ZoneBuffRegistry.MarkCreated(zone, 0, ownerObjId: 1, buffIndex: 7);
        ZoneBuffRegistry.MarkCreated(zone, 0, ownerObjId: 2, buffIndex: 7);
        ZoneBuffRegistry.MarkCreated(zone + 1, 0, ownerObjId: 1, buffIndex: 7);
        ZoneBuffRegistry.MarkCreated(zone, instanceId: 5, ownerObjId: 1, buffIndex: 7);

        await Assert.That(ZoneBuffRegistry.WasCreated(zone, 0, 1, 7)).IsTrue();
        await Assert.That(ZoneBuffRegistry.WasCreated(zone, 0, 2, 7)).IsTrue();
        await Assert.That(ZoneBuffRegistry.WasCreated(zone + 1, 0, 1, 7)).IsTrue();
        await Assert.That(ZoneBuffRegistry.WasCreated(zone, 5, 1, 7)).IsTrue();

        // Clearing one cell leaves its siblings intact.
        ZoneBuffRegistry.Clear(zone, 0, 1, 7);
        await Assert.That(ZoneBuffRegistry.WasCreated(zone, 0, 2, 7)).IsTrue();
        await Assert.That(ZoneBuffRegistry.WasCreated(zone + 1, 0, 1, 7)).IsTrue();
        await Assert.That(ZoneBuffRegistry.WasCreated(zone, 5, 1, 7)).IsTrue();
    }

    [Test]
    public async Task ResetZoneDropsEveryEntryForThatInstance()
    {
        var zone = NextZone();
        ZoneBuffRegistry.MarkCreated(zone, 3, 10, 20);
        ZoneBuffRegistry.MarkCreated(zone, 3, 11, 21);
        ZoneBuffRegistry.MarkCreated(zone, 4, 12, 22); // other instance must survive

        ZoneBuffRegistry.ResetZone(zone, 3);

        await Assert.That(ZoneBuffRegistry.WasCreated(zone, 3, 10, 20)).IsFalse();
        await Assert.That(ZoneBuffRegistry.WasCreated(zone, 3, 11, 21)).IsFalse();
        await Assert.That(ZoneBuffRegistry.WasCreated(zone, 4, 12, 22)).IsTrue();
    }

    [Test]
    public async Task ClearUnitDropsOnlyThatUnitsEntries()
    {
        var zone = NextZone();
        ZoneBuffRegistry.MarkCreated(zone, 0, 389, 11);
        ZoneBuffRegistry.MarkCreated(zone, 0, 389, 12);
        ZoneBuffRegistry.MarkCreated(zone, 0, 390, 11);

        ZoneBuffRegistry.ClearUnit(zone, 0, 389);

        await Assert.That(ZoneBuffRegistry.WasCreated(zone, 0, 389, 11)).IsFalse();
        await Assert.That(ZoneBuffRegistry.WasCreated(zone, 0, 389, 12)).IsFalse();
        await Assert.That(ZoneBuffRegistry.WasCreated(zone, 0, 390, 11)).IsTrue();
    }

    [Test]
    public async Task ClearUnitEverywhere_StopsARecycledIdInheritingBuffRecords()
    {
        // Object ids are reused. A unit that inherits another's entries has its own buff Creates dropped
        // as duplicates and ends up running with none of them — which left a hull with no speed buffs in
        // its zone and unable to move at all.
        var zoneA = NextZone();
        var zoneB = NextZone();
        ZoneBuffRegistry.MarkCreated(zoneA, 0, 389, 11);
        ZoneBuffRegistry.MarkCreated(zoneB, 0, 389, 11);
        ZoneBuffRegistry.MarkCreated(zoneB, 0, 391, 11);

        ZoneBuffRegistry.ClearUnitEverywhere(389);

        await Assert.That(ZoneBuffRegistry.WasCreated(zoneA, 0, 389, 11)).IsFalse();
        await Assert.That(ZoneBuffRegistry.WasCreated(zoneB, 0, 389, 11)).IsFalse();
        await Assert.That(ZoneBuffRegistry.WasCreated(zoneB, 0, 391, 11)).IsTrue();
    }

    [Test]
    public async Task ZeroZoneIdAndUnknownLookupsAreSafe()
    {
        ZoneBuffRegistry.MarkCreated(0, 0, 1, 2);
        await Assert.That(ZoneBuffRegistry.WasCreated(0, 0, 1, 2)).IsFalse();
        await Assert.That(ZoneBuffRegistry.WasCreated(NextZone(), 0, 999, 999)).IsFalse();
        ZoneBuffRegistry.Clear(NextZone(), 9, 1, 1);
        ZoneBuffRegistry.ResetZone(0, 0);
        await Assert.That(Task.CompletedTask).IsEqualTo(Task.CompletedTask); // no throws above
    }
}

public class BuffCreatedWireIndexTests
{
    // Every caster writes a type byte plus a 3-byte bc, then whatever its subclass adds; the body then
    // carries a u64 cast id and a 3-byte target bc before the index. So the index sits at
    // 4 + extra + 11. These offsets were previously asserted as a flat 16 or 17, which matched no caster
    // type at all and silently broke every buff Update and Remove bound for a zone.
    private const int UnitIndexOffset = 4 + 8 + 3;                  // 15
    private const int MountIndexOffset = (4 + 4) + 8 + 3;            // 19
    private const int ItemIndexOffset = (4 + 8 + 4 + 1 + 8) + 8 + 3; // 36

    [Test]
    public async Task TryGetBuffIndex_ReadsUnitCasterLayout()
    {
        var body = new byte[UnitIndexOffset + 4];
        body[0] = 0;
        BitConverter.GetBytes((uint)777).CopyTo(body, UnitIndexOffset);

        await Assert.That(BuffCreatedWire.TryGetBuffIndex(body, out var index)).IsTrue();
        await Assert.That(index).IsEqualTo(777u);
    }

    [Test]
    public async Task TryGetBuffIndex_ReadsItemCasterLayout()
    {
        // Item(2) adds item id, template, type1 and type2 ahead of the cast id.
        var body = new byte[ItemIndexOffset + 4];
        body[0] = 2;
        BitConverter.GetBytes((uint)55).CopyTo(body, ItemIndexOffset);

        await Assert.That(BuffCreatedWire.TryGetBuffIndex(body, out var index)).IsTrue();
        await Assert.That(index).IsEqualTo(55u);
    }

    [Test]
    public async Task TryGetBuffIndex_ReadsMountCasterLayout()
    {
        // Mount(3) adds the mount skill template id.
        var body = new byte[MountIndexOffset + 4];
        body[0] = 3;
        BitConverter.GetBytes((uint)31).CopyTo(body, MountIndexOffset);

        await Assert.That(BuffCreatedWire.TryGetBuffIndex(body, out var index)).IsTrue();
        await Assert.That(index).IsEqualTo(31u);
    }

    [Test]
    public async Task TryGetBuffIndex_ReadsDoodadCasterLayout()
    {
        // Doodad(4) adds nothing, so it shares the unit layout.
        var body = new byte[UnitIndexOffset + 4];
        body[0] = 4;
        BitConverter.GetBytes((uint)99).CopyTo(body, UnitIndexOffset);

        await Assert.That(BuffCreatedWire.TryGetBuffIndex(body, out var index)).IsTrue();
        await Assert.That(index).IsEqualTo(99u);
    }

    [Test]
    public async Task TryGetBuffIndex_RejectsShortOrEmptyBodies()
    {
        await Assert.That(BuffCreatedWire.TryGetBuffIndex(null, out _)).IsFalse();
        await Assert.That(BuffCreatedWire.TryGetBuffIndex([], out _)).IsFalse();
        await Assert.That(BuffCreatedWire.TryGetBuffIndex(new byte[10], out _)).IsFalse();

        // An item caster needs 40 bytes before its index is readable.
        var truncated = new byte[ItemIndexOffset + 3];
        truncated[0] = 2;
        await Assert.That(BuffCreatedWire.TryGetBuffIndex(truncated, out _)).IsFalse();
    }
}
