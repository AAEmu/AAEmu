using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

public class CSUpdateFavoritePortalPacketTests
{
    [Test]
    public async Task Read_UsesCountAndIdTypeFavoriteEntryOrder()
    {
        var body = new PacketStream()
            .Write((byte)2)
            .Write(17)
            .Write((byte)PortalBookType.Private)
            .Write(true)
            .Write(42)
            .Write((byte)PortalBookType.Return)
            .Write(false)
            .GetBytes();
        var packet = new CSUpdateFavoritePortalPacket();

        packet.Read(new PacketStream(body));

        await Assert.That(packet.Changes).IsEquivalentTo(new[]
        {
            new FavoritePortalChange((byte)PortalBookType.Private, 17, true),
            new FavoritePortalChange((byte)PortalBookType.Return, 42, false)
        });
    }

    [Test]
    public async Task Read_RejectsNegativeIdsAsUnownedWithoutThrowing()
    {
        var body = new PacketStream()
            .Write((byte)1)
            .Write(-1)
            .Write((byte)PortalBookType.Private)
            .Write(true)
            .GetBytes();
        var packet = new CSUpdateFavoritePortalPacket();

        packet.Read(new PacketStream(body));

        await Assert.That(packet.Changes[0].PortalId).IsEqualTo(uint.MaxValue);
    }
}
