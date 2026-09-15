using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Trading;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

public class CSBuySpecialtyItemPacketTests
{
    [Test]
    public async Task ReadBody_ConsumesQuoteBeforeCompactObjectIds()
    {
        var expectedQuote = new SpecialtyQuote
        {
            ItemId = 43323,
            Refund = 260000,
            NoEventRefund = 200000,
            Ratio = 1300,
            Stock = 5,
            CanProduce = true,
            Currency = ShopCurrencyType.Money,
            Type = 0
        };
        var stream = new PacketStream();
        stream.Write(expectedQuote);
        stream.WriteBc(0x010203);
        stream.WriteBc(0x0A0B0C);
        stream.Rollback();

        var (quote, npcObjId, auxiliary) = CSBuySpecialtyItemPacket.ReadBody(stream);

        await Assert.That(quote).IsEqualTo(expectedQuote);
        await Assert.That(npcObjId).IsEqualTo(0x010203u);
        await Assert.That(auxiliary).IsEqualTo(0x0A0B0Cu);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }
}
