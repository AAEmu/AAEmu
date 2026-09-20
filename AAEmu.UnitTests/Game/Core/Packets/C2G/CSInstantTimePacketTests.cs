using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

public class CSInstantTimePacketTests
{
    [Test]
    public async Task Read_IsTimeTypeThenUseThenSaveDb()
    {
        var stream = new PacketStream();
        stream.Write((uint)InstantTimeKind.MobilizationOrderNotRecv);
        stream.Write(true);
        stream.Write(true);

        var packet = new CSInstantTimePacket();
        packet.Read(new PacketStream(stream.GetBytes()));

        await Assert.That(packet.TimeType).IsEqualTo((uint)InstantTimeKind.MobilizationOrderNotRecv);
        await Assert.That(packet.Use).IsTrue();
        await Assert.That(packet.SaveDb).IsTrue();
    }

    [Test]
    public async Task Read_ExpeditionSummonKind_IsNotMobilization()
    {
        var stream = new PacketStream();
        stream.Write((uint)InstantTimeKind.ExpeditionSummonNotRecv);
        stream.Write(true);
        stream.Write(false);

        var packet = new CSInstantTimePacket();
        packet.Read(new PacketStream(stream.GetBytes()));

        await Assert.That(packet.TimeType).IsEqualTo((uint)InstantTimeKind.ExpeditionSummonNotRecv);
        await Assert.That(packet.Use).IsTrue();
        await Assert.That(packet.SaveDb).IsFalse();
        await Assert.That(packet.TimeType).IsNotEqualTo((uint)InstantTimeKind.MobilizationOrderNotRecv);
    }
}
