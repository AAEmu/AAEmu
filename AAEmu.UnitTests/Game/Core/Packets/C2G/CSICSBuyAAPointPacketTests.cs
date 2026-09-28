using System.Collections.Concurrent;
using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.C2G;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

/// <summary>
/// Pins the AA-point checkout request: the body is a single unsigned 32-bit cash amount, and the
/// packet has to be routed rather than parsed and dropped.
/// </summary>
public class CSICSBuyAAPointPacketTests
{
    [Test]
    public async Task Body_IsASingleUnsignedCashAmount()
    {
        var stream = new PacketStream();
        stream.Write(uint.MaxValue);
        stream.Rollback();

        var cashMoney = CSICSBuyAAPointPacket.ReadBody(stream);

        await Assert.That(cashMoney).IsEqualTo(uint.MaxValue);
        // The request is exactly one u32; a reader that left bytes behind would be reading a
        // different body than the client sends.
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    public async Task AZeroCashRequestIsReadAsZeroRatherThanSkipped()
    {
        var stream = new PacketStream();
        stream.Write(0u);
        stream.Rollback();

        // Refusing a zero request belongs to the checkout rules, not to the reader: the reader
        // must not quietly turn a zero body into some other value on the way past.
        await Assert.That(CSICSBuyAAPointPacket.ReadBody(stream)).IsEqualTo(0u);
    }

    [Test]
    public async Task TheRequestIsRegisteredAtTheClientToWorldLevel()
    {
        var handler = (GameProtocolHandler)typeof(GameNetwork)
            .GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(GameNetwork.Instance)!;
        var packets = (ConcurrentDictionary<byte, ConcurrentDictionary<uint, Type>>)typeof(GameProtocolHandler)
            .GetField("_packets", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(handler)!;

        await Assert.That(packets[1].TryGetValue(CSOffsets.CSICSBuyAAPointPacket, out var registered))
            .IsTrue();
        await Assert.That(registered).IsEqualTo(typeof(CSICSBuyAAPointPacket));
    }
}
