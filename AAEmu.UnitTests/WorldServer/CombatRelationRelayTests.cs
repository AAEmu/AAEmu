using System.Net;
using System.Net.Sockets;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.World.Core.Network;
using AAEmu.World.Core.Packets.Wz;
using AAEmu.World.Core.Relay;
using AAEmu.World.Core.Zone;

namespace AAEmu.UnitTests.WorldServer;

[NotInParallel]
public class CombatRelationRelayTests
{
    private static uint _nextSessionId = 900000;
    private readonly List<ZoneConnection> _connections = [];

    [Before(Test)]
    public void ResetRelay()
    {
        CombatRelationRelay.Reset();
        RemoveConnections();
    }

    [After(Test)]
    public void CleanupRelay()
    {
        RemoveConnections();
        CombatRelationRelay.Reset();
    }

    [Test]
    public async Task Fanout_SendsTheCanonicalPublicationToEveryLoadedZone()
    {
        var first = AddZone(100, 0);
        var second = AddZone(200, 1);
        var publication = new CombatRelationPublication(
            1,
            CombatRelationPublicationKind.FullState,
            [new CombatRelationEntry(10, 20, 3, 7)]);

        CombatRelationRelay.PublishCvF(publication);

        await AssertEntries(first.Session.Packets, 10, 20, 3, 7);
        await AssertEntries(second.Session.Packets, 10, 20, 3, 7);
    }

    [Test]
    public async Task DeltaTombstone_IsAppliedAndReplayedAsCurrentState()
    {
        var zone = AddZone(100, 0);
        CombatRelationRelay.PublishCvF(new CombatRelationPublication(
            1,
            CombatRelationPublicationKind.FullState,
            [
                new CombatRelationEntry(10, 20, 1, 5),
                new CombatRelationEntry(30, 40, 2, 6),
            ]));
        zone.Session.Packets.Clear();

        CombatRelationRelay.PublishCvF(new CombatRelationPublication(
            2,
            CombatRelationPublicationKind.Delta,
            [new CombatRelationEntry(10, 20, 0, 0)]));

        var delta = await ReadEntries(zone.Session.Packets.Single());
        await Assert.That(delta.Count).IsEqualTo(2);
        await Assert.That(delta.Any(entry => entry.Faction1 == 10 && entry.Faction2 == 20 && entry.RelationType == 0)).IsTrue();
        await Assert.That(delta.Any(entry => entry.Faction1 == 30 && entry.Faction2 == 40 && entry.RelationType == 2)).IsTrue();

        zone.Session.Packets.Clear();
        CombatRelationRelay.PublishToZone(zone.Connection);
        var replay = await ReadEntries(zone.Session.Packets.Single());
        await Assert.That(replay.Count).IsEqualTo(2);
        await Assert.That(replay.Any(entry => entry.Faction1 == 10 && entry.Faction2 == 20 && entry.RelationType == 0)).IsTrue();
        await Assert.That(replay.Any(entry => entry.Faction1 == 30 && entry.Faction2 == 40 && entry.RelationType == 2)).IsTrue();
    }

    [Test]
    public async Task ReinsertedRelation_RemovesItsOldTombstone()
    {
        var zone = AddZone(100, 0);
        CombatRelationRelay.PublishCvF(new CombatRelationPublication(
            1,
            CombatRelationPublicationKind.FullState,
            [new CombatRelationEntry(10, 20, 1, 5)]));
        CombatRelationRelay.PublishCvF(new CombatRelationPublication(
            2,
            CombatRelationPublicationKind.Delta,
            [new CombatRelationEntry(10, 20, 0, 0)]));
        CombatRelationRelay.PublishCvF(new CombatRelationPublication(
            3,
            CombatRelationPublicationKind.Delta,
            [new CombatRelationEntry(10, 20, 2, 6)]));
        zone.Session.Packets.Clear();

        CombatRelationRelay.PublishToZone(zone.Connection);
        var replay = await ReadEntries(zone.Session.Packets.Single());
        await Assert.That(replay.Count).IsEqualTo(1);
        await Assert.That(replay[0].RelationType).IsEqualTo(2u);
    }

    [Test]
    public async Task FullReplacement_EmitsTombstonesForRemovedPairs()
    {
        var zone = AddZone(100, 0);
        CombatRelationRelay.PublishCvF(new CombatRelationPublication(
            1,
            CombatRelationPublicationKind.FullState,
            [new CombatRelationEntry(10, 20, 1, 5)]));
        zone.Session.Packets.Clear();

        CombatRelationRelay.PublishCvF(new CombatRelationPublication(
            2,
            CombatRelationPublicationKind.FullState,
            []));

        var entries = await ReadEntries(zone.Session.Packets.Single());
        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Faction1).IsEqualTo(10u);
        await Assert.That(entries[0].Faction2).IsEqualTo(20u);
        await Assert.That(entries[0].RelationType).IsEqualTo(0u);

        var replacement = AddZone(200, 0);
        CombatRelationRelay.PublishToZone(replacement.Connection);
        var replay = await ReadEntries(replacement.Session.Packets.Single());
        await Assert.That(replay.Count).IsEqualTo(1);
        await Assert.That(replay[0].RelationType).IsEqualTo(0u);
    }

    [Test]
    public async Task SequentialPublications_AreDeliveredInVersionOrder()
    {
        var zone = AddZone(100, 0);
        CombatRelationRelay.PublishCvF(new CombatRelationPublication(
            1,
            CombatRelationPublicationKind.FullState,
            [new CombatRelationEntry(10, 20, 1, 5)]));
        CombatRelationRelay.PublishCvF(new CombatRelationPublication(
            2,
            CombatRelationPublicationKind.Delta,
            [new CombatRelationEntry(30, 40, 2, 6)]));

        await Assert.That(zone.Session.Packets.Count).IsEqualTo(2);
        var first = await ReadEntries(zone.Session.Packets[0]);
        var second = await ReadEntries(zone.Session.Packets[1]);
        await Assert.That(first.Any(entry => entry.Faction1 == 10 && entry.Faction2 == 20)).IsTrue();
        await Assert.That(second.Any(entry => entry.Faction1 == 10 && entry.Faction2 == 20)).IsTrue();
        await Assert.That(second.Any(entry => entry.Faction1 == 30 && entry.Faction2 == 40)).IsTrue();
    }

    [Test]
    public async Task FvFPublication_UsesTheFvFPacketFamily()
    {
        var zone = AddZone(100, 0);
        CombatRelationRelay.PublishFvF(new CombatRelationPublication(
            1,
            CombatRelationPublicationKind.FullState,
            [new CombatRelationEntry(10, 20, 1, 5)]));

        var frame = new PacketStream(zone.Session.Packets.Single());
        frame.ReadUInt16();
        await Assert.That(frame.ReadUInt16()).IsEqualTo(WzOpcodes.FvFCombatRelationship);
        var entries = WZCombatRelationPacket.Decode(frame);
        await Assert.That(entries.Count).IsEqualTo(1);
    }

    [Test]
    public void StalePublication_IsRejectedWithoutChangingCanonicalState()
    {
        AddZone(100, 0);
        CombatRelationRelay.PublishCvF(new CombatRelationPublication(
            2,
            CombatRelationPublicationKind.FullState,
            [new CombatRelationEntry(10, 20, 2, 4)]));

        Assert.Throws<InvalidOperationException>(() => CombatRelationRelay.PublishCvF(
            new CombatRelationPublication(
                1,
                CombatRelationPublicationKind.FullState,
                [new CombatRelationEntry(30, 40, 1, 1)])));
    }

    [Test]
    public async Task DisconnectAndReplay_AreIsolatedPerAuthority()
    {
        var first = AddZone(100, 0);
        var second = AddZone(200, 1);
        CombatRelationRelay.PublishCvF(new CombatRelationPublication(
            1,
            CombatRelationPublicationKind.FullState,
            [new CombatRelationEntry(10, 20, 1, 5)]));
        first.Session.Packets.Clear();
        second.Session.Packets.Clear();

        ZoneSession.Instance.Remove(first.Connection.Id);
        CombatRelationRelay.PublishCvF(new CombatRelationPublication(
            2,
            CombatRelationPublicationKind.Delta,
            [new CombatRelationEntry(30, 40, 2, 6)]));

        await Assert.That(first.Session.Packets).IsEmpty();
        var secondEntries = await ReadEntries(second.Session.Packets.Single());
        await Assert.That(secondEntries.Count).IsEqualTo(2);
        await Assert.That(secondEntries.Any(entry => entry.Faction1 == 10 && entry.Faction2 == 20 && entry.RelationType == 1)).IsTrue();
        await Assert.That(secondEntries.Any(entry => entry.Faction1 == 30 && entry.Faction2 == 40 && entry.RelationType == 2)).IsTrue();

        var replacement = AddZone(300, 0);
        CombatRelationRelay.PublishToZone(replacement.Connection);
        var replay = await ReadEntries(replacement.Session.Packets.Single());
        await Assert.That(replay.Count).IsEqualTo(2);
    }

    [Test]
    public async Task SendFailure_DoesNotPreventOtherZones()
    {
        var failing = AddZone(100, 0);
        var healthy = AddZone(200, 1);
        failing.Session.ThrowOnSend = true;

        CombatRelationRelay.PublishCvF(new CombatRelationPublication(
            1,
            CombatRelationPublicationKind.FullState,
            [new CombatRelationEntry(10, 20, 1, 5)]));

        await Assert.That(failing.Session.Packets).IsEmpty();
        await AssertEntries(healthy.Session.Packets, 10, 20, 1, 5);

        failing.Session.Packets.Clear();
        healthy.Session.Packets.Clear();
        failing.Session.ThrowOnSend = true;
        CombatRelationRelay.PublishToZone(failing.Connection);
        await Assert.That(failing.Session.Packets).IsEmpty();
        CombatRelationRelay.PublishToZone(healthy.Connection);
        await AssertEntries(healthy.Session.Packets, 10, 20, 1, 5);
    }

    [Test]
    public async Task Reset_ClearsCanonicalStateAndFutureReplay()
    {
        var zone = AddZone(100, 0);
        CombatRelationRelay.PublishCvF(new CombatRelationPublication(
            1,
            CombatRelationPublicationKind.FullState,
            [new CombatRelationEntry(10, 20, 1, 5)]));
        zone.Session.Packets.Clear();

        CombatRelationRelay.Reset();
        CombatRelationRelay.PublishToZone(zone.Connection);
        await Assert.That(zone.Session.Packets).IsEmpty();

        CombatRelationRelay.PublishCvF(new CombatRelationPublication(
            1,
            CombatRelationPublicationKind.FullState,
            [new CombatRelationEntry(50, 60, 3, 4)]));
        await AssertEntries(zone.Session.Packets, 50, 60, 3, 4);
    }

    [Test]
    public async Task ReplayOversize_DoesNotCommitThePublication()
    {
        var zone = AddZone(100, 0);
        var full = Enumerable.Range(1, 200)
            .Select(i => new CombatRelationEntry((uint)i, 1000, 1, 1))
            .ToArray();
        CombatRelationRelay.PublishCvF(new CombatRelationPublication(
            1,
            CombatRelationPublicationKind.FullState,
            full));
        zone.Session.Packets.Clear();

        for (var i = 0; i < 56; i++)
        {
            var publication = new CombatRelationPublication(
                (ulong)(2 + i),
                CombatRelationPublicationKind.Delta,
                [new CombatRelationEntry((uint)(10000 + i), 20000, 0, 0)]);
            if (i == 55)
                Assert.Throws<InvalidOperationException>(() => CombatRelationRelay.PublishCvF(publication));
            else
                CombatRelationRelay.PublishCvF(publication);
        }

        zone.Session.Packets.Clear();
        CombatRelationRelay.PublishToZone(zone.Connection);
        var replay = await ReadEntries(zone.Session.Packets.Single());
        await Assert.That(replay.Count).IsEqualTo(255);
        await Assert.That(replay.Any(entry => entry.Faction1 == 10055 && entry.Faction2 == 20000)).IsFalse();
    }

    [Test]
    public void JoinedZone_CannotReceiveReplay()
    {
        var zone = AddZone(100, 0, ZoneConnectionState.Joined);
        Assert.Throws<InvalidOperationException>(() => CombatRelationRelay.PublishToZone(zone.Connection));
    }

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
        var fixture = new ZoneFixture(connection, session);
        _connections.Add(connection);
        return fixture;
    }

    private void RemoveConnections()
    {
        foreach (var connection in _connections)
            ZoneSession.Instance.Remove(connection.Id);
        _connections.Clear();
    }

    private static async Task AssertEntries(List<byte[]> packets, uint faction1, uint faction2, uint relationType, uint flags)
    {
        await Assert.That(packets.Count).IsEqualTo(1);
        var entries = await ReadEntries(packets[0]);
        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Faction1).IsEqualTo(faction1);
        await Assert.That(entries[0].Faction2).IsEqualTo(faction2);
        await Assert.That(entries[0].RelationType).IsEqualTo(relationType);
        await Assert.That(entries[0].Flags).IsEqualTo(flags);
    }

    private static async Task<IReadOnlyList<CombatRelationEntry>> ReadEntries(byte[] packet)
    {
        var stream = new PacketStream(packet);
        stream.ReadUInt16();
        stream.ReadUInt16();
        return WZCombatRelationPacket.Decode(stream);
    }

    private sealed record ZoneFixture(ZoneConnection Connection, RecordingSession Session);

    private sealed class RecordingSession(uint sessionId) : ISession
    {
        public List<byte[]> Packets { get; } = [];
        public bool ThrowOnSend { get; set; }
        public IPAddress Ip => IPAddress.Loopback;
        public uint SessionId { get; } = sessionId;
        public Socket Socket { get; } = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        public void SendPacket(byte[] packet)
        {
            if (ThrowOnSend)
                throw new IOException("simulated zone send failure");
            Packets.Add(packet);
        }

        public void AddAttribute(string name, object attribute) { }
        public object GetAttribute(string name) => null;
        public void ClearAttribute(string name) { }
        public void Close() { }
    }
}
