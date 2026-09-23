using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

/// <summary>
/// The H-window channel/index query (CS 0x199): the answer carries the dimension the picker picked —
/// the copy id resolves to that copy's channel — and an id nothing knows still gets an answer instead
/// of silence.
/// </summary>
[NotInParallel]
public sealed class CSRequestSysInstanceIndexPacketTests
{
    private const uint ZoneId = 930;
    private const uint ZoneKey = 931;
    private const uint ZoneGroup = 932;
    private const uint CatalogId = 940;
    private const uint ChannelledWorldId = 950;
    private const int ChannelledChannel = 2;
    private const uint CharacterId = 73_001;

    private readonly List<byte[]> _sent = [];
    private readonly GameConnection _connection;
    private readonly Character _character;
    private readonly SingletonScope<ZoneManager> _zones;
    private readonly SingletonScope<WorldManager> _worlds;
    private readonly SingletonScope<IndunManager> _indun;
    private readonly Zone _zone;
    private object _originalIndunZones;
    private Func<uint, uint, bool> _originalHostProbe;

    public CSRequestSysInstanceIndexPacketTests()
    {
        var session = Mock.Of<ISession>();
        session.SendPacket(Any<byte[]>()).Callback((byte[] bytes) => _sent.Add(bytes));
        _connection = new GameConnection(session.Object);
        _character = new Character(new UnitCustomModelParams())
        {
            Id = CharacterId,
            ObjId = CharacterId,
            Name = "ChannelPicker",
            Connection = _connection,
        };
        _connection.Characters.Add(_character.Id, _character);
        _connection.ActiveChar = _character;

        _zone = new Zone { Id = ZoneId, ZoneKey = ZoneKey, GroupId = ZoneGroup };

        var zoneManager = new ZoneManager(Mock.Of<IWorldManager>().Object);
        SetField(zoneManager, "_zones", new Dictionary<uint, Zone> { [ZoneKey] = _zone });
        SetField(zoneManager, "_zoneIdToKey", new Dictionary<uint, uint> { [ZoneId] = ZoneKey });
        _zones = new SingletonScope<ZoneManager>(zoneManager);

        var worldManager = new WorldManager(null, null, null, null, null);
        SetField(worldManager, "_worlds", new ConcurrentDictionary<uint, WorldInstance>
        {
            [ChannelledWorldId] = ChannelledCopy(),
        });
        _worlds = new SingletonScope<WorldManager>(worldManager);

        var worldStub = Mock.Of<IWorldManager>();
        worldStub.GetWorlds().Returns([]);
        _indun = new SingletonScope<IndunManager>(new IndunManager(
            Mock.Of<ITickManager>().Object,
            worldStub.Object,
            Mock.Of<IZoneManager>().Object,
            Mock.Of<ITeamManager>().Object));
    }

    [Before(Test)]
    public void SeedContentAndProbe()
    {
        _originalIndunZones = IndunZonesField().GetValue(IndunGameData.Instance);
        IndunZonesField().SetValue(IndunGameData.Instance, new Dictionary<uint, IndunZone>
        {
            [ZoneGroup] = new() { ZoneGroupId = ZoneGroup, InstanceCatalogId = CatalogId },
        });
        _originalHostProbe = WorldIntegration.IsZoneInstanceLoaded;
        WorldIntegration.IsZoneInstanceLoaded = null;
    }

    [After(Test)]
    public void RestoreProcessState()
    {
        WorldIntegration.IsZoneInstanceLoaded = _originalHostProbe;
        IndunZonesField().SetValue(IndunGameData.Instance, _originalIndunZones);
        _indun.Dispose();
        _worlds.Dispose();
        _zones.Dispose();
    }

    [Test]
    public async Task PickedDimension_IsAnsweredAndKeptForTheEntryThatFollows()
    {
        Request(ZoneKey, CatalogId);

        // The reply carries the picked copy's channel, and the same dimension is remembered for the
        // enter request that follows — the enter packet carries no channel of its own.
        var (opcode, body) = SentPacket.Read(_sent.Single());
        await Assert.That(opcode).IsEqualTo(SCOffsets.SCSysIndunIndexPacket);
        var stream = new PacketStream(body);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(ZoneKey);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(ChannelledWorldId);
        await Assert.That(stream.ReadUInt32()).IsEqualTo((uint)ChannelledChannel);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);

        await Assert.That(IndunManager.Instance.GetInstancePick(CharacterId))
            .IsEqualTo(new SysIndunPick(ZoneKey, ChannelledWorldId, ChannelledChannel));
    }

    [Test]
    public async Task UnknownInstanceAndUnknownZone_AreStillAnsweredWithNoCopy()
    {
        Request(777, 4242);

        var (opcode, body) = SentPacket.Read(_sent.Single());
        await Assert.That(opcode).IsEqualTo(SCOffsets.SCSysIndunIndexPacket);
        var stream = new PacketStream(body);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(777u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
        await Assert.That(IndunManager.Instance.GetInstancePick(CharacterId)).IsNull();
    }

    private void Request(uint zoneKey, uint catalogId) =>
        new CSRequestSysInstanceIndexPacket { Connection = _connection }
            .Read(new PacketStream().Write((short)0).Write((int)zoneKey).Write(catalogId));

    private static WorldInstance ChannelledCopy()
    {
        var world = new WorldInstance(
            new WorldTemplate { Name = "channelled_test_copy", ZoneKeys = [ZoneKey] },
            ChannelledChannel, dontFreeInstanceId: true, instanceId: ChannelledWorldId);
        world.DungeonInstance = (Dungeon)RuntimeHelpers.GetUninitializedObject(typeof(Dungeon));
        return world;
    }

    private static FieldInfo IndunZonesField() =>
        typeof(IndunGameData).GetField("_indunZones", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static void SetField(object owner, string name, object value) =>
        owner.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(owner, value);
}
