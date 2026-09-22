using System;
using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

/// <summary>
/// The H-window enter request (CS 0x198): an unknown instance fails loud with a definitive error, a
/// remembered channel pick decides which copy the entry is handed to (its refusal is definitive too),
/// and the pick is consumed by the entry that used it.
/// </summary>
[NotInParallel]
public sealed class CSEnterSysInstancePacketTests
{
    private const uint ZoneId = 930;
    private const uint ZoneKey = 931;
    private const uint ZoneGroup = 932;
    private const uint CatalogId = 940;
    private const uint ChannelledWorldId = 950;
    private const int ChannelledChannel = 2;
    private const uint CharacterId = 74_001;

    private readonly List<byte[]> _sent = [];
    private readonly GameConnection _connection;
    private readonly Character _character;
    private readonly SingletonScope<ZoneManager> _zones;
    private readonly SingletonScope<IndunManager> _indun;
    private readonly Zone _zone;
    private readonly IndunZone _indunZone;
    private object _originalIndunZones;
    private Func<uint, uint, bool> _originalHostProbe;

    public CSEnterSysInstancePacketTests()
    {
        var session = Mock.Of<ISession>();
        session.SendPacket(Any<byte[]>()).Callback((byte[] bytes) => _sent.Add(bytes));
        _connection = new GameConnection(session.Object);
        _character = new Character(new UnitCustomModelParams())
        {
            Id = CharacterId,
            ObjId = CharacterId,
            Name = "Enterer",
            Connection = _connection,
        };
        _connection.Characters.Add(_character.Id, _character);
        _connection.ActiveChar = _character;

        _zone = new Zone { Id = ZoneId, ZoneKey = ZoneKey, GroupId = ZoneGroup };
        _indunZone = new IndunZone
        {
            ZoneGroupId = ZoneGroup,
            InstanceCatalogId = CatalogId,
            SelectChannel = true,
            // The honoured-pick entry in this suite stops at the quota of the copy it picked, so the
            // copy must hold nobody while the daily cap and the level band let the entry that far.
            MaxPlayers = 0,
            EnterCount = 5,
            LevelMin = 0,
            LevelMax = 999,
        };

        var zoneManager = new ZoneManager(Mock.Of<IWorldManager>().Object);
        SetField(zoneManager, "_zones", new Dictionary<uint, Zone> { [ZoneKey] = _zone });
        SetField(zoneManager, "_zoneIdToKey", new Dictionary<uint, uint> { [ZoneId] = ZoneKey });
        _zones = new SingletonScope<ZoneManager>(zoneManager);

        var zoneStub = Mock.Of<IZoneManager>();
        zoneStub.GetZoneById(ZoneId).Returns(_zone);
        zoneStub.GetZoneKeysInZoneGroupById(ZoneGroup).Returns(new List<uint> { ZoneKey });
        var worldStub = Mock.Of<IWorldManager>();
        worldStub.GetWorldTemplateByZoneKey(ZoneKey).Returns(new WorldTemplate { Name = "enter_test" });
        worldStub.GetWorlds().Returns([ChannelledCopy()]);
        _indun = new SingletonScope<IndunManager>(new IndunManager(
            Mock.Of<ITickManager>().Object,
            worldStub.Object,
            zoneStub.Object,
            Mock.Of<ITeamManager>().Object));
    }

    [Before(Test)]
    public void SeedContentAndProbe()
    {
        _originalIndunZones = IndunZonesField().GetValue(IndunGameData.Instance);
        IndunZonesField().SetValue(IndunGameData.Instance, new Dictionary<uint, IndunZone>
        {
            [ZoneGroup] = _indunZone,
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
        _zones.Dispose();
    }

    [Test]
    public async Task UnknownInstanceId_FailsLoudWithADefinitiveError()
    {
        Enter(4242);

        await Assert.That(_sent.Count).IsEqualTo(1);
        await AssertError(0, ErrorMessageType.InvalidStateInstance);
        await Assert.That(IndunManager.Instance.GetInstancePick(CharacterId)).IsNull();
    }

    [Test]
    public async Task ZeroInstanceId_FailsLoudWithADefinitiveError()
    {
        Enter(0);

        await Assert.That(_sent.Count).IsEqualTo(1);
        await AssertError(0, ErrorMessageType.InvalidStateInstance);
    }

    [Test]
    public async Task PickedDimension_DecidesTheCopyAndTheEntryConsumesThePick()
    {
        IndunManager.Instance.RememberInstancePick(CharacterId,
            new SysIndunPick(ZoneKey, ChannelledWorldId, ChannelledChannel));

        Enter(CatalogId);

        // The entry announces itself, then hands the remembered copy on; the copy holds nobody in this
        // fixture, so it stops at that copy's quota with a definitive error rather than silently. Reaching
        // the quota at all means the pick selected this copy — an entry without the pick never asks it.
        await Assert.That(_sent.Count).IsEqualTo(2);
        var (first, body) = SentPacket.Read(_sent[0]);
        await Assert.That(first).IsEqualTo(SCOffsets.SCProcessingInstancePacket);
        var stream = new PacketStream(body);
        await Assert.That(stream.ReadInt32()).IsEqualTo((int)ZoneKey);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
        await AssertError(1, ErrorMessageType.InstanceQuota);

        // The pick belonged to this entry and may not decide a later one.
        await Assert.That(IndunManager.Instance.GetInstancePick(CharacterId)).IsNull();
    }

    [Test]
    public async Task PickNamingACopyThatIsGone_IsRefusedDefinitivelyNotSubstituted()
    {
        IndunManager.Instance.RememberInstancePick(CharacterId,
            new SysIndunPick(ZoneKey, 999, ChannelledChannel));

        Enter(CatalogId);

        await Assert.That(_sent.Count).IsEqualTo(2);
        var (first, _) = SentPacket.Read(_sent[0]);
        await Assert.That(first).IsEqualTo(SCOffsets.SCProcessingInstancePacket);
        await AssertError(1, ErrorMessageType.NoServerInstanceResource);
        await Assert.That(IndunManager.Instance.GetInstancePick(CharacterId)).IsNull();
    }

    private void Enter(uint instancesId) =>
        new CSEnterSysInstancePacket { Connection = _connection }
            .Read(new PacketStream().Write(instancesId).WriteBc(0));

    private async Task AssertError(int index, ErrorMessageType expected)
    {
        var (opcode, body) = SentPacket.Read(_sent[index]);
        await Assert.That(opcode).IsEqualTo(SCOffsets.SCErrorMsgPacket);
        var stream = new PacketStream(body);
        await Assert.That(stream.ReadInt16()).IsEqualTo((short)expected);
        await Assert.That(stream.ReadInt16()).IsEqualTo((short)expected);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    private static WorldInstance ChannelledCopy()
    {
        var world = new WorldInstance(
            new WorldTemplate { Name = "channelled_test_copy", ZoneKeys = [ZoneKey] },
            ChannelledChannel, dontFreeInstanceId: true, instanceId: ChannelledWorldId);
        TestDungeonWorld.CreateDungeon(
            new IndunZone
            {
                ZoneGroupId = ZoneGroup,
                InstanceCatalogId = CatalogId,
                SelectChannel = true,
                MaxPlayers = 0,
            },
            world);
        return world;
    }

    private static FieldInfo IndunZonesField() =>
        typeof(IndunGameData).GetField("_indunZones", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static void SetField(object owner, string name, object value) =>
        owner.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(owner, value);
}
