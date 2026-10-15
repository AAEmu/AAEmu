using System.Net;
using System.Net.Sockets;

using AAEmu.Commons.Network.Core;
using AAEmu.World.Core.Network;
using AAEmu.World.Core.Packets.Wz;
using AAEmu.World.Core.Relay;
using AAEmu.World.Core.Zone;

namespace AAEmu.UnitTests.WorldServer;

/// <summary>
/// Forced corpse teardown (SpawnManager after ZoneDespawnAck) must not free the bcId
/// immediately — that recycled ids under the dedicate's spawn_delay and looked like a fast respawn.
/// </summary>
[NotInParallel]
public class NpcForcedTeardownTests
{
    private readonly List<ZoneConnection> _connections = [];
    private uint _nextSessionId = 9100;

    [After(Test)]
    public void Cleanup()
    {
        foreach (var connection in _connections)
            ZoneSession.Instance.Remove(connection.Id);
        _connections.Clear();
    }

    [Test]
    public async Task FinishForcedUnitRemoved_UnregistersBodyAndHoldsObjectId()
    {
        var session = new RecordingSession(_nextSessionId++);
        var connection = new ZoneConnection(session)
        {
            ZoneId = 257,
            InstanceId = 0,
            State = ZoneConnectionState.ZoneLoaded,
        };
        ZoneSession.Instance.Add(connection);
        ZoneSession.Instance.IndexByZoneId(connection);
        _connections.Add(connection);

        const uint bcId = 2707;
        connection.Units.RegisterWithId(bcId, [1, 2, 3]);
        NpcSpawnRelay.ForgetNpcState(connection.ZoneId, connection.InstanceId, bcId);

        NpcSpawnRelay.FinishForcedUnitRemoved(connection, bcId);

        await Assert.That(connection.Units.Contains(bcId)).IsFalse();
        await Assert.That(NpcSpawnRelay.IsObjectIdReleasePending(bcId)).IsTrue();
        await Assert.That(session.Packets.Count).IsEqualTo(1);
        await Assert.That(Opcode(session.Packets[0])).IsEqualTo(WzOpcodes.UnitRemoved);
    }

    [Test]
    public async Task FinishForcedUnitRemoved_NullConnection_StillHoldsObjectId()
    {
        const uint bcId = 2711;
        NpcSpawnRelay.FinishForcedUnitRemoved(null, bcId);
        await Assert.That(NpcSpawnRelay.IsObjectIdReleasePending(bcId)).IsTrue();
    }

    private static ushort Opcode(byte[] packet) => BitConverter.ToUInt16(packet, 2);

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
