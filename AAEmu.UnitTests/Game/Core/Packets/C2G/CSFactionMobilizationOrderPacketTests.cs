using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

public class CSFactionMobilizationOrderPacketTests
{
    [Test]
    public async Task Read_IsResultThenHeroIdThenZoneGroupU16()
    {
        var stream = new PacketStream();
        stream.Write(1u);
        stream.Write(8UL);
        stream.Write((ushort)2);

        var packet = new CSFactionMobilizationOrderPacket();
        packet.Read(new PacketStream(stream.GetBytes()));

        await Assert.That(packet.Result).IsEqualTo((uint)MobilizationOrderResultType.Accept);
        await Assert.That(packet.HeroId).IsEqualTo(8UL);
        await Assert.That(packet.ZoneGroupType).IsEqualTo((ushort)2);
    }
}
