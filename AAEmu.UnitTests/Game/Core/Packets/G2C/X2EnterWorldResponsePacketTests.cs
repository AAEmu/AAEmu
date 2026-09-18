using System.Net;
using System.Net.Sockets;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

[NotInParallel]
public class X2EnterWorldResponsePacketTests
{
    private sealed class Session(bool gm) : ISession
    {
        public IPAddress Ip => IPAddress.Loopback;
        public uint SessionId => 0xf0000001;
        public Socket Socket => null!;
        public void SendPacket(byte[] packet) { }
        public void AddAttribute(string name, object attribute) { }
        public object GetAttribute(string name) => name == "gmFlag" && gm ? true : null;
        public void ClearAttribute(string name) { }
        public void Close() { }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Write_ProvidesKeyExchangeAndPlayerAuthorityEvenForGm(bool gm)
    {
        var connection = new GameConnection(new Session(gm)) { AccountId = 0xf0000001 };
        var body = new PacketStream();
        new X2EnterWorldResponsePacket(0, 0x12345678, 1250, connection).Write(body);

        var reader = new PacketStream(body.GetBytes());
        await Assert.That(reader.ReadUInt16()).IsEqualTo((ushort)0);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(0x12345678u);
        await Assert.That(reader.ReadUInt16()).IsEqualTo((ushort)1250);
        _ = reader.ReadUInt64(); // server time
        _ = reader.ReadUInt32(); // timezone
        await Assert.That(reader.ReadUInt16()).IsEqualTo((ushort)260);
        await Assert.That(reader.ReadUInt16()).IsEqualTo((ushort)260);
        await Assert.That(reader.ReadUInt32()).IsEqualTo(1024u); // RSA key size
        _ = reader.ReadBytes(256); // remaining public key blob
        await Assert.That(reader.ReadUInt32()).IsEqualTo(0x0100007fu);
        await Assert.That(reader.ReadUInt16()).IsEqualTo((ushort)1250);
        var authority = reader.ReadUInt32();
        await Assert.That(authority).IsEqualTo(1u);
        await Assert.That(authority & 4u).IsEqualTo(0u); // no editor-only creation gate
        await Assert.That(reader.Pos).IsEqualTo(reader.Count);
        await Assert.That(connection.GetAttribute("gmFlag") != null).IsEqualTo(gm);
    }
}
