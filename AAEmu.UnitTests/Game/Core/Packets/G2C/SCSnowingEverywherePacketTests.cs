using System.Net;
using System.Net.Sockets;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCSnowingEverywherePacketTests
{
    private sealed class RecordingSession : ISession
    {
        public List<byte[]> Packets { get; } = [];
        public IPAddress Ip => IPAddress.Loopback;
        public uint SessionId => 1;
        public Socket Socket => null!;

        public void SendPacket(byte[] packet) => Packets.Add(packet);
        public void AddAttribute(string name, object attribute) { }
        public object GetAttribute(string name) => null;
        public void ClearAttribute(string name) { }
        public void Close() { }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Encode_UsesNativeOpcodeAndSingleBooleanBody(bool isSnowing)
    {
        var session = new RecordingSession();
        var connection = new GameConnection(session);

        connection.SendPacket(new SCSnowingEverywherePacket(isSnowing));

        await Assert.That(session.Packets).HasSingleItem();
        await AssertSnowPacket(session.Packets[0], isSnowing);
    }

    internal static async Task AssertSnowPacket(byte[] bytes, bool expected)
    {
        var stream = new PacketStream(bytes);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)(stream.Count - 2));
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0xDD);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)1);
        stream.ReadByte(); // Unused checksum.
        stream.ReadByte(); // Unused counter.
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)0xE9);
        await Assert.That(stream.LeftBytes).IsEqualTo(1);
        await Assert.That(stream.ReadBoolean()).IsEqualTo(expected);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }
}
