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

    /// <summary>
    /// Raw wire bytes, little-endian, as the client serializer emits them: an unsigned 32-bit
    /// <c>type</c> at offset 0 followed by a signed 32-bit <c>itemCount</c> at offset 4. The bytes
    /// are emitted one at a time so this never depends on write-side signedness, and the shape is
    /// pinned at runtime rather than only by the C# property types failing to compile.
    /// </summary>
    private static byte[] Wire(uint type, int itemCount)
    {
        var bytes = new byte[8];
        for (var i = 0; i < 4; i++)
        {
            bytes[i] = (byte)(type >> (8 * i));
            bytes[4 + i] = (byte)(itemCount >> (8 * i));
        }

        return bytes;
    }

    [Test]
    public async Task FakeBuyBody_ReadsUnsignedTypeThenSignedCountFromRawBytes()
    {
        // type 0x0000FF7F, itemCount 0xFFFFFFFF == -1
        var (type, count) = CSFakeBuySpecialtyItemPacket.ReadBody(new PacketStream(Wire(0x0000FF7Fu, -1)));

        await Assert.That(type).IsEqualTo(0x0000FF7Fu);
        await Assert.That(count).IsEqualTo(-1);
    }

    [Test]
    public async Task FakeSellBody_ReadsUnsignedTypeThenSignedCountFromRawBytes()
    {
        var (type, count) = CSFakeSellSpecialtyItemPacket.ReadBody(new PacketStream(Wire(0xFFFFFFFFu, -7)));

        await Assert.That(type).IsEqualTo(0xFFFFFFFFu);
        await Assert.That(count).IsEqualTo(-7);
    }

    [Test]
    public async Task FakeBodies_ConsumeExactlyEightBytes()
    {
        var buy = new PacketStream(Wire(1u, 1));
        CSFakeBuySpecialtyItemPacket.ReadBody(buy);
        await Assert.That(buy.Pos).IsEqualTo(8);

        var sell = new PacketStream(Wire(1u, 1));
        CSFakeSellSpecialtyItemPacket.ReadBody(sell);
        await Assert.That(sell.Pos).IsEqualTo(8);
    }
}
