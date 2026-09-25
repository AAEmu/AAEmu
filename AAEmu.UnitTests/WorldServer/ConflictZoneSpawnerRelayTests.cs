using System.Net;
using System.Net.Sockets;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.World.Core.Network;
using AAEmu.World.Core.Packets.Wz;
using AAEmu.World.Core.Relay;
using AAEmu.World.Core.Zone;

namespace AAEmu.UnitTests.WorldServer;

[NotInParallel]
public class ConflictZoneSpawnerRelayTests
{
    private const uint ZoneUnderTest = 100;

    private static uint _nextSessionId = 910000;
    private readonly List<ZoneConnection> _connections = [];

    [Before(Test)]
    public void Setup()
    {
        // Other relay test classes share the static ZoneSession; clear it so each of these tests
        // starts with exactly the zone it registers.
        foreach (var zone in ZoneSession.Instance.All.ToList())
            ZoneSession.Instance.Remove(zone.Id);
        RemoveConnections();
        ConflictZoneSpawnerRelay.ResetForTest();
        // One zone, always in the group under test; a second zone joins a different group below.
        ConflictZoneSpawnerRelay.ResolveZoneGroup = zoneId => zoneId == ZoneUnderTest ? 63u : 7u;
        ConflictZoneSpawnerRelay.ResolvePlacements = zoneId => zoneId == ZoneUnderTest ? [] : [];
        ConflictZoneSpawnerRelay.ResolveRows = _ => [];
    }

    [After(Test)]
    public void Cleanup()
    {
        foreach (var zone in ZoneSession.Instance.All.ToList())
            ZoneSession.Instance.Remove(zone.Id);
        RemoveConnections();
        ConflictZoneSpawnerRelay.ResetForTest();
    }

    [Test]
    public async Task EnteringWar_ArmsTheWarPlacementAndDeactivatesThePeaceOnes()
    {
        // Group 63's shipped shape: two armed peace rows, one armed war row.
        Rows(63,
            new ConflictZoneSpawnerEntry(169343, ConflictZoneStateKind.Peace, true, false),
            new ConflictZoneSpawnerEntry(169344, ConflictZoneStateKind.Peace, true, false),
            new ConflictZoneSpawnerEntry(200747, ConflictZoneStateKind.War, true, false));
        Placements((169343, 20779), (169344, 20779), (200747, 20800));
        var zone = AddZone(ZoneUnderTest, 0);

        ConflictZoneSpawnerRelay.Apply(63, (byte)ZoneConflictType.War);

        var packets = Activates(zone);
        // one armed (the war row) + two deactivated (the peace rows).
        await Assert.That(packets.Count(p => p.Activate)).IsEqualTo(1);
        await Assert.That(packets.Single(p => p.Activate).X).IsEqualTo(200747f);
        await Assert.That(packets.Count(p => !p.Activate)).IsEqualTo(2);
        await Assert.That(packets.Where(p => !p.Activate).Select(p => p.X).OrderBy(x => x).ToList())
            .IsEquivalentTo(new List<float> { 169343f, 169344f });
    }

    [Test]
    public async Task EnteringPeace_ArmsThePeacePlacementAndDeactivatesTheWarOne()
    {
        Rows(63,
            new ConflictZoneSpawnerEntry(169343, ConflictZoneStateKind.Peace, true, false),
            new ConflictZoneSpawnerEntry(200747, ConflictZoneStateKind.War, true, false));
        Placements((169343, 20779), (200747, 20800));
        var zone = AddZone(ZoneUnderTest, 0);

        ConflictZoneSpawnerRelay.Apply(63, (byte)ZoneConflictType.Peace);

        var packets = Activates(zone);
        await Assert.That(packets.Single(p => p.Activate).X).IsEqualTo(169343f);
        await Assert.That(packets.Where(p => !p.Activate).Select(p => p.X).ToList())
            .IsEquivalentTo(new List<float> { 200747f });
    }

    [Test]
    public async Task Group139_SpawnActivateFalse_ReArmsWhenLeavingPeace()
    {
        // Group 139's only row is a peace row with spawn_activate=false: suppressed in peace, and
        // re-armed once the group is in war because the row no longer applies.
        ConflictZoneSpawnerRelay.ResolveZoneGroup = zoneId => zoneId == ZoneUnderTest ? 139u : 7u;
        ConflictZoneSpawnerRelay.ResolveRows = group => group == 139
            ? [new ConflictZoneSpawnerEntry(213307, ConflictZoneStateKind.Peace, false, false)]
            : [];
        Placements((213307, 999));
        var zone = AddZone(ZoneUnderTest, 0);

        ConflictZoneSpawnerRelay.Apply(139, (byte)ZoneConflictType.War);

        var packets = Activates(zone);
        await Assert.That(packets.Count).IsEqualTo(1);
        await Assert.That(packets[0].Activate).IsTrue();
        await Assert.That(packets[0].X).IsEqualTo(213307f);
    }

    [Test]
    public async Task Group139_SpawnActivateFalse_IsDeactivatedWhileInPeace()
    {
        ConflictZoneSpawnerRelay.ResolveZoneGroup = zoneId => zoneId == ZoneUnderTest ? 139u : 7u;
        ConflictZoneSpawnerRelay.ResolveRows = group => group == 139
            ? [new ConflictZoneSpawnerEntry(213307, ConflictZoneStateKind.Peace, false, false)]
            : [];
        Placements((213307, 999));
        var zone = AddZone(ZoneUnderTest, 0);

        ConflictZoneSpawnerRelay.Apply(139, (byte)ZoneConflictType.Peace);

        var packets = Activates(zone);
        await Assert.That(packets.Count).IsEqualTo(1);
        await Assert.That(packets[0].Activate).IsFalse();
    }

    [Test]
    public async Task Fanout_ReachesEveryZoneInTheGroup_AndSkipsOtherGroups()
    {
        ConflictZoneSpawnerRelay.ResolveZoneGroup = zoneId => zoneId switch
        {
            ZoneUnderTest => 15u,
            200 => 15u,
            _ => 99u
        };
        ConflictZoneSpawnerRelay.ResolveRows = group => group == 15
            ? [new ConflictZoneSpawnerEntry(11, ConflictZoneStateKind.War, true, false)]
            : [];
        ConflictZoneSpawnerRelay.ResolvePlacements = _ => [new ZoneSpawnerPlacementCatalog.SpawnerPlacement(11, 5, 11f, 0f, 0f, 0f)];
        var a = AddZone(ZoneUnderTest, 0);
        var b = AddZone(200, 0);
        var other = AddZone(300, 0); // different group, must stay silent

        ConflictZoneSpawnerRelay.Apply(15, (byte)ZoneConflictType.War);

        await Assert.That(a.Session.Packets.Count).IsEqualTo(1);
        await Assert.That(b.Session.Packets.Count).IsEqualTo(1);
        await Assert.That(other.Session.Packets.Count).IsEqualTo(0);
    }

    [Test]
    public async Task MissingPlacement_IsSkippedWithoutAFabricatedPacket()
    {
        Rows(63,
            new ConflictZoneSpawnerEntry(200747, ConflictZoneStateKind.War, true, false),
            new ConflictZoneSpawnerEntry(424242, ConflictZoneStateKind.War, true, false));
        Placements((200747, 20800)); // 424242 is absent from the zone's .g
        var zone = AddZone(ZoneUnderTest, 0);

        ConflictZoneSpawnerRelay.Apply(63, (byte)ZoneConflictType.War);

        var packets = Activates(zone);
        await Assert.That(packets.Count).IsEqualTo(1);
        await Assert.That(packets[0].X).IsEqualTo(200747f);
    }

    [Test]
    public async Task UseDespawn_RetiresOnlyTheExactIdAndType_AndKeepsTheBcIdRegistered()
    {
        // Type 20779 is shared by two placements, so matching on type alone would retire both.
        // Only the exact (id,type) pair may be despawned, and the bcId must stay registered until
        // the Zone confirms ZWRemoveNpc.
        Rows(63, new ConflictZoneSpawnerEntry(169343, ConflictZoneStateKind.Peace, true, true));
        Placements((169343, 20779), (169344, 20779), (200747, 20800));
        var zone = AddZone(ZoneUnderTest, 0);
        zone.Connection.Units.RegisterWithId(1, CreateSpawnBody(169343, 20779));
        zone.Connection.Units.RegisterWithId(2, CreateSpawnBody(169344, 20779));
        zone.Connection.Units.RegisterWithId(3, CreateSpawnBody(200747, 20800));

        // Enter war: only the peace placement 169343 is deactivated.
        ConflictZoneSpawnerRelay.Apply(63, (byte)ZoneConflictType.War);

        var despawns = zone.Session.Packets
            .Where(p => Opcode(p) == (ushort)WzOpcodes.NpcStartDespawn)
            .Select(BcId)
            .ToList();
        await Assert.That(despawns).IsEquivalentTo(new List<uint> { 1 });

        // Deferred removal: every bcId is still registered (no premature ObjectId release).
        await Assert.That(zone.Connection.Units.Snapshot().Select(e => e.Key).OrderBy(k => k).ToList())
            .IsEquivalentTo(new List<uint> { 1, 2, 3 });
    }

    [Test]
    public async Task NoDespawn_WhenUseDespawnIsFalse()
    {
        Rows(63, new ConflictZoneSpawnerEntry(169343, ConflictZoneStateKind.Peace, true, false));
        Placements((169343, 20779));
        var zone = AddZone(ZoneUnderTest, 0);
        zone.Connection.Units.RegisterWithId(1, CreateSpawnBody(169343, 20779));

        ConflictZoneSpawnerRelay.Apply(63, (byte)ZoneConflictType.War);

        await Assert.That(zone.Session.Packets.Count(p => Opcode(p) == (ushort)WzOpcodes.NpcStartDespawn))
            .IsEqualTo(0);
    }

    [Test]
    public async Task EscalationState_SendsNoPackets()
    {
        Rows(63, new ConflictZoneSpawnerEntry(200747, ConflictZoneStateKind.War, true, false));
        Placements((200747, 20800));
        var zone = AddZone(ZoneUnderTest, 0);

        ConflictZoneSpawnerRelay.Apply(63, (byte)ZoneConflictType.Conflict);

        await Assert.That(zone.Session.Packets.Count).IsEqualTo(0);
    }

    [Test]
    public async Task UnknownWarStateByte_IsIgnored()
    {
        Rows(63, new ConflictZoneSpawnerEntry(200747, ConflictZoneStateKind.War, true, false));
        Placements((200747, 20800));
        var zone = AddZone(ZoneUnderTest, 0);

        ConflictZoneSpawnerRelay.Apply(63, 200);

        await Assert.That(zone.Session.Packets.Count).IsEqualTo(0);
    }

    // ---- helpers ----

    private static void Rows(ushort groupId, params ConflictZoneSpawnerEntry[] rows) =>
        ConflictZoneSpawnerRelay.ResolveRows = g => g == groupId ? rows : [];

    /// <summary>Places the zone's placements; X is the placement id so assertions read the content id.</summary>
    private void Placements(params (uint Id, uint Type)[] placements) =>
        ConflictZoneSpawnerRelay.ResolvePlacements = zoneId => zoneId == ZoneUnderTest
            ? placements.Select(p => new ZoneSpawnerPlacementCatalog.SpawnerPlacement(p.Id, p.Type, p.Id, 0f, 0f, 0f)).ToList()
            : [];

    private static List<ActivatePacket> Activates(ZoneFixture zone) =>
        zone.Session.Packets
            .Where(p => Opcode(p) == (ushort)WzOpcodes.ActivateNpcSpawnersInArea)
            .Select(ReadActivate)
            .ToList();

    private ZoneFixture AddZone(uint zoneId, uint instanceId, ZoneConnectionState state = ZoneConnectionState.ZoneLoaded)
    {
        var session = new RecordingSession(_nextSessionId++);
        var connection = new ZoneConnection(session)
        {
            ZoneId = zoneId,
            InstanceId = instanceId,
            State = state,
        };
        ZoneSession.Instance.Add(connection);
        ZoneSession.Instance.IndexByZoneId(connection);
        _connections.Add(connection);
        return new ZoneFixture(connection, session);
    }

    private void RemoveConnections()
    {
        foreach (var connection in _connections)
            ZoneSession.Instance.Remove(connection.Id);
        _connections.Clear();
    }

    private readonly record struct ActivatePacket(float X, float Y, float Z, float Radius, bool Activate);

    private static ActivatePacket ReadActivate(byte[] packet)
    {
        var stream = new PacketStream(packet);
        stream.ReadUInt16();
        stream.ReadUInt16();
        var x = stream.ReadSingle();
        var y = stream.ReadSingle();
        var z = stream.ReadSingle();
        var radius = stream.ReadSingle();
        var activate = stream.ReadByte();
        return new ActivatePacket(x, y, z, radius, activate != 0);
    }

    /// <summary>WZ frame is [u16 length][u16 opcode][body], so the opcode sits at offset 2.</summary>
    private static ushort Opcode(byte[] packet) => BitConverter.ToUInt16(packet, 2);

    /// <summary>WZNpcStartDespawn body: a bcId (WriteBc encoding) after the two u16 header words.</summary>
    private static uint BcId(byte[] packet)
    {
        var stream = new PacketStream(packet);
        stream.ReadUInt16();
        stream.ReadUInt16();
        return stream.ReadBc();
    }

    /// <summary>
    /// ZWSpawnNpc body: u32 sid, u32 sType, u8 mIdx, u8 pIdx, u16 tIdx, u32 templateId, u32 groupType,
    /// u32 groupId, u8 groupMemberIdx, f32 x, y, z, f32 zRot, f32 scale, then a zeroed tail.
    /// </summary>
    private static byte[] CreateSpawnBody(uint spawnerId, uint spawnerType) =>
        new PacketStream()
            .Write(spawnerId)
            .Write(spawnerType)
            .Write((byte)0)
            .Write((byte)0)
            .Write((ushort)0)
            .Write(1001u)
            .Write(0u)
            .Write(0u)
            .Write((byte)0)
            .Write(1000f)
            .Write(2000f)
            .Write(50f)
            .Write(0f)
            .Write(1f)
            .Write(new byte[37])
            .GetBytes();

    private sealed record ZoneFixture(ZoneConnection Connection, RecordingSession Session);

    private sealed class RecordingSession(uint sessionId) : ISession
    {
        public List<byte[]> Packets { get; } = [];
        public IPAddress Ip => IPAddress.Loopback;
        public uint SessionId { get; } = sessionId;
        public Socket Socket { get; } = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        public void SendPacket(byte[] packet) => Packets.Add(packet);
        public void AddAttribute(string name, object attribute) { }
        public object GetAttribute(string name) => null;
        public void ClearAttribute(string name) { }
        public void Close() { }
    }
}
