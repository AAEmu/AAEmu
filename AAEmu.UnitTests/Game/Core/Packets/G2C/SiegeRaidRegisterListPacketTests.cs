using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SiegeRaidRegisterListPacketTests
{
    [Test]
    public async Task Write_WritesTheFlagsThenOneBlockPerZoneWithItsRows()
    {
        var zones = new List<SiegeRaidRegisterZone>
        {
            new(33, new List<SiegeRaidRegisterRow>
            {
                new(1, "First", 1001),
                new(2, "Second", 1002)
            })
        };

        var body = new SCSiegeRaidRegisterListPacket(true, true, 33, zones).Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write(true);
        expected.Write(true);
        expected.Write((ushort)33);
        expected.Write(1); // zone count
        expected.Write(33); // zone group type
        expected.Write(2); // row count
        expected.Write(1u);
        expected.Write("First");
        expected.Write(1001L);
        expected.Write(0u);
        expected.Write((byte)0);
        expected.Write(2u);
        expected.Write("Second");
        expected.Write(1002L);
        expected.Write(0u);
        expected.Write((byte)0);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task Write_SendsAnEmptyRosterWithoutZones()
    {
        var body = new SCSiegeRaidRegisterListPacket(false, true, 34, [])
            .Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write(false);
        expected.Write(true);
        expected.Write((ushort)34);
        expected.Write(0);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }
}
