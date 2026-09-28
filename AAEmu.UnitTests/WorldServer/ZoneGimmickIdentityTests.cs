using System.Numerics;
using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.World.Core.Packets.Wz;
using AAEmu.World.Core.Relay;

namespace AAEmu.UnitTests.WorldServer;

/// <summary>
/// The identity half of a zone-requested static gimmick: a spawn request arrives from a zone without
/// an object id, because those are world-issued. Taking the request's id at face value filed every
/// such gimmick under the same registry key and announced an object nothing can address.
/// </summary>
[NotInParallel]
public class ZoneGimmickIdentityTests
{
    // The value that must never reach the zone or the grasp registry: the id field of a spawn
    // request, which a zone never fills in.
    private const uint UnsetObjectId = 0;
    private const uint StaticZoneId = 186;
    private const uint RequestingZoneId = 186;

    [After(Test)]
    public void ResetSharedState()
    {
        ZoneStaticGimmickAuthority.Clear();
        ClearInstance<NonUnitObjectIdManager>();
    }

    [Test]
    public async Task ZoneRequestedStaticGimmick_IsAnnouncedUnderAWorldObjectId()
    {
        var ids = InstallAllocator(0x00F0_0001);

        var adopted = ZoneStaticGimmickAuthority.AdoptZoneRequest(ZoneRequest(), ids.Next);

        await Assert.That(adopted.Id).IsEqualTo(ids.Last);
        await Assert.That(adopted.Id).IsNotEqualTo(UnsetObjectId);
    }

    [Test]
    public async Task ZoneRequestedStaticGimmick_IsTrackedUnderTheAllocatedIdNotTheUnsetOne()
    {
        var ids = InstallAllocator(0x00F0_0001);
        var adopted = ZoneStaticGimmickAuthority.AdoptZoneRequest(ZoneRequest(), ids.Next);

        await Assert.That(ZoneStaticGimmickAuthority.IsTracked(adopted.Id)).IsTrue();
        await Assert.That(ZoneStaticGimmickAuthority.IsTracked(UnsetObjectId)).IsFalse();
    }

    [Test]
    public async Task TwoZoneRequestedGimmicks_DoNotCollideOnOneRegistryEntry()
    {
        // Both requests carry the same unset id, so without a fresh id the second overwrote the first
        // and only one of the two could ever be grasped.
        var ids = InstallAllocator(0x00F0_0001);

        var first = ZoneStaticGimmickAuthority.AdoptZoneRequest(ZoneRequest(), ids.Next);
        var second = ZoneStaticGimmickAuthority.AdoptZoneRequest(ZoneRequest(), ids.Next);

        await Assert.That(second.Id).IsNotEqualTo(first.Id);
        await Assert.That(ZoneStaticGimmickAuthority.IsTracked(first.Id)).IsTrue();
        await Assert.That(ZoneStaticGimmickAuthority.IsTracked(second.Id)).IsTrue();
    }

    [Test]
    public async Task AnnouncedToTheZone_TheCreateRecordCarriesTheAllocatedId()
    {
        // The echoed create is what a zone and every client address the object by.
        var ids = InstallAllocator(0x00F0_0001);
        var adopted = ZoneStaticGimmickAuthority.AdoptZoneRequest(ZoneRequest(), ids.Next);

        var frame = new PacketStream(new WZGimmickCreatedPacket(adopted, unchecked((int)RequestingZoneId)).Encode());
        frame.ReadUInt16();
        frame.ReadUInt16();

        await Assert.That(GimmickSpawnData.TryRead(frame, out var announced)).IsTrue();
        await Assert.That(announced.Id).IsEqualTo(ids.Last);
        await Assert.That(announced.Id).IsNotEqualTo(UnsetObjectId);
        await Assert.That(frame.ReadUInt32()).IsEqualTo(RequestingZoneId);
        await Assert.That(frame.Pos).IsEqualTo(frame.Count);
    }

    [Test]
    public async Task Adopted_KeepsTheRestOfTheRequestIntact()
    {
        // Only identity is the world's to decide; the zone's placement has to survive the handover.
        var request = ZoneRequest() with
        {
            ModelPath = "gameobjects/marianople/lift.ddf",
            Type = 1234,
            Z = 87.5f,
        };
        var ids = InstallAllocator(0x00F0_0001);

        var adopted = ZoneStaticGimmickAuthority.AdoptZoneRequest(request, ids.Next);

        await Assert.That(adopted.ModelPath).IsEqualTo(request.ModelPath);
        await Assert.That(adopted.Type).IsEqualTo(request.Type);
        await Assert.That(adopted.Z).IsEqualTo(request.Z);
        await Assert.That(adopted.X).IsEqualTo(request.X);
        await Assert.That(adopted.Y).IsEqualTo(request.Y);
        await Assert.That(adopted.StaticZoneId).IsEqualTo(request.StaticZoneId);
    }

    [Test]
    public async Task AnAllocatorThatCannotProduceAnId_FailsLoudlyAndRegistersNothing()
    {
        // A silent fallback here is what put an unaddressable object on the wire in the first place.
        await Assert.That(
                () => ZoneStaticGimmickAuthority.AdoptZoneRequest(ZoneRequest(), () => throw new InvalidOperationException("pool exhausted")))
            .Throws<InvalidOperationException>();
        await Assert.That(ZoneStaticGimmickAuthority.IsTracked(UnsetObjectId)).IsFalse();
    }

    /// <summary>A request exactly as a zone sends it: an object-id field it never fills in.</summary>
    private static GimmickSpawnData ZoneRequest() => new(
        UnsetObjectId,
        Type: 0,
        EntityGuid: 0,
        Type2: 0,
        SpawnerUnitId: 0,
        GrasperUnitId: 0,
        StaticZoneId: StaticZoneId,
        ModelPath: "gameobjects/test/static_gimmick.ddf",
        X: Helpers.ConvertLongX(1234.5f),
        Y: Helpers.ConvertLongY(5678.25f),
        Z: 30f,
        Rotation: Quaternion.Identity,
        Scale: 1f,
        Velocity: Vector3.Zero,
        AngularVelocity: Vector3.Zero,
        ScaleVelocity: 0f);

    /// <summary>Installs a real allocator primed with no reservations, and records what it hands out.</summary>
    private static RecordingAllocator InstallAllocator(uint firstId)
    {
        var allocator = new NonUnitObjectIdManager();
        if (!allocator.Initialize())
            throw new InvalidOperationException("the object id allocator could not be primed for the test");
        SetInstance(allocator);
        return new RecordingAllocator(allocator, firstId);
    }

    /// <summary>
    /// Takes a real id so the pool is genuinely in use, then hands out a known band so the issued id
    /// is assertable. What is asserted is <see cref="Last"/>, so a change that stops using the
    /// allocator entirely still fails.
    /// </summary>
    private sealed class RecordingAllocator(NonUnitObjectIdManager allocator, uint firstId)
    {
        private readonly NonUnitObjectIdManager _allocator = allocator;
        private uint _next = firstId;

        public uint Last { get; private set; }

        public uint Next()
        {
            _ = _allocator.GetNextId();
            Last = _next++;
            return Last;
        }
    }

    private static void SetInstance<T>(T value) where T : class =>
        typeof(T).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, value);

    private static void ClearInstance<T>() where T : class =>
        typeof(T).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, null);
}
