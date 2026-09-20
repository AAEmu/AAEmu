using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCFactionMobilizationOrderPacketTests
{
    [Test]
    public async Task Body_IsZoneGroupU16ThenHeroIdThenName()
    {
        const ushort zoneGroup = 2;
        const ulong heroId = 8;
        const string heroName = "Tester";

        var body = new SCFactionMobilizationOrderPacket(zoneGroup, heroId, heroName)
            .Write(new PacketStream())
            .GetBytes();

        var expected = new PacketStream();
        expected.Write(zoneGroup);
        expected.Write(heroId);
        expected.Write(heroName);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
        await Assert.That(BitConverter.ToUInt16(body, 0)).IsEqualTo(zoneGroup);
        await Assert.That(BitConverter.ToUInt64(body, 2)).IsEqualTo(heroId);
    }

    [Test]
    public async Task Body_IsShorterThanAnEightByteZoneGroup()
    {
        var body = new SCFactionMobilizationOrderPacket(2, 8, "Tester")
            .Write(new PacketStream())
            .GetBytes();

        var wideZone = new PacketStream();
        wideZone.Write(2UL);
        wideZone.Write(8UL);
        wideZone.Write("Tester");

        await Assert.That(body.Length).IsEqualTo(wideZone.GetBytes().Length - 6);
    }
}
