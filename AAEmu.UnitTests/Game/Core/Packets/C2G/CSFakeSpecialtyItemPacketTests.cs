using System.Collections.Concurrent;
using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.C2G;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

public class CSFakeSpecialtyItemPacketTests
{
    [Test]
    public async Task FakeBuyBody_PreservesUnsignedTypeAndSignedCount()
    {
        var stream = new PacketStream();
        stream.Write(uint.MaxValue);
        stream.Write(-7);
        stream.Rollback();

        var (type, count) = CSFakeBuySpecialtyItemPacket.ReadBody(stream);

        await Assert.That(type).IsEqualTo(uint.MaxValue);
        await Assert.That(count).IsEqualTo(-7);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    public async Task FakeSellBody_PreservesUnsignedTypeAndSignedCount()
    {
        var stream = new PacketStream();
        stream.Write(31832u);
        stream.Write(3);
        stream.Rollback();

        var (type, count) = CSFakeSellSpecialtyItemPacket.ReadBody(stream);

        await Assert.That(type).IsEqualTo(31832u);
        await Assert.That(count).IsEqualTo(3);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    public async Task FakeSpecialtyPackets_AreRegisteredAtTheirProtocolLevels()
    {
        var handler = (GameProtocolHandler)typeof(GameNetwork)
            .GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(GameNetwork.Instance)!;
        var packets = (ConcurrentDictionary<byte, ConcurrentDictionary<uint, Type>>)typeof(GameProtocolHandler)
            .GetField("_packets", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(handler)!;

        await Assert.That(packets[1][CSOffsets.CSFakeBuySpecialtyItemPacket])
            .IsEqualTo(typeof(CSFakeBuySpecialtyItemPacket));
        await Assert.That(packets[1][CSOffsets.CSFakeSellSpecialtyItemPacket])
            .IsEqualTo(typeof(CSFakeSellSpecialtyItemPacket));
    }
}
