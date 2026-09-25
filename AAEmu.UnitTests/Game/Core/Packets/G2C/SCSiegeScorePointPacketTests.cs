using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCSiegeScorePointPacketTests
{
    [Test]
    public async Task Write_CarriesTheZoneGroupAndTheThreeTotals()
    {
        var body = new SCSiegeScorePointPacket(33, 12u, 345u, 67u).Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write((ushort)33);
        expected.Write(12u);
        expected.Write(345u);
        expected.Write(67u);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task TypeId_IsTheClientSiegeScorePointOpcode()
    {
        await Assert.That(new SCSiegeScorePointPacket(0, 0, 0, 0).TypeId).IsEqualTo((ushort)0x33C);
    }

    [Test]
    public async Task Write_KeepsTheFieldOrderTheClientReadsOutlawDefenseOffense()
    {
        // The client writes the three counters into its dominion record in this order; a packet that swaps two
        // of them would show one side's score on another.
        var body = new SCSiegeScorePointPacket(1, 1u, 2u, 3u).Write(new PacketStream()).GetBytes();

        await Assert.That(body.Length).IsEqualTo(14);
        await Assert.That((int)body[2]).IsEqualTo(1);
        await Assert.That((int)body[6]).IsEqualTo(2);
        await Assert.That((int)body[10]).IsEqualTo(3);
    }
}
