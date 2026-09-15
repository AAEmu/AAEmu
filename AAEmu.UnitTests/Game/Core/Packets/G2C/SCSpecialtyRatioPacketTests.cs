using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Trading;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCSpecialtyRatioPacketTests
{
    [Test]
    public async Task SpecialtyPriceList_WritesKeyedQuotePage()
    {
        var quote = new SpecialtyQuote
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

        new SCSpecialtyRatioPacket(8, 17958, [quote], [], true, true).Write(stream);

        stream.Rollback();
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)8);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(17958u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.ReadUInt32()).IsEqualTo(43323u);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(260000ul);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(200000ul);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1300u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(5u);
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)ShopCurrencyType.Money);
        await Assert.That(stream.ReadSByte()).IsEqualTo((sbyte)0);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    public async Task CargoList_WritesUnkeyedQuotePage()
    {
        var quote = new SpecialtyQuote
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

        new SCSpecialtyGoodsPacket([quote], [], true, true).Write(stream);

        stream.Rollback();
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.ReadUInt32()).IsEqualTo(43323u);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(260000ul);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(200000ul);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1300u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(5u);
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)ShopCurrencyType.Money);
        await Assert.That(stream.ReadSByte()).IsEqualTo((sbyte)0);
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }

    [Test]
    public async Task SpecialtyLists_WriteActiveEventIds()
    {
        var ratioStream = new PacketStream();
        var goodsStream = new PacketStream();

        new SCSpecialtyRatioPacket(8, 17958, [], [14, 30], true, true).Write(ratioStream);
        new SCSpecialtyGoodsPacket([], [14, 30], true, true).Write(goodsStream);

        ratioStream.Rollback();
        await Assert.That(ratioStream.ReadUInt16()).IsEqualTo((ushort)8);
        await Assert.That(ratioStream.ReadUInt32()).IsEqualTo(17958u);
        await Assert.That(ratioStream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(ratioStream.ReadUInt32()).IsEqualTo(2u);
        await Assert.That(ratioStream.ReadBoolean()).IsTrue();
        await Assert.That(ratioStream.ReadBoolean()).IsTrue();
        await Assert.That(ratioStream.ReadUInt32()).IsEqualTo(14u);
        await Assert.That(ratioStream.ReadUInt32()).IsEqualTo(30u);
        await Assert.That(ratioStream.Pos).IsEqualTo(ratioStream.Count);

        goodsStream.Rollback();
        await Assert.That(goodsStream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(goodsStream.ReadUInt32()).IsEqualTo(2u);
        await Assert.That(goodsStream.ReadBoolean()).IsTrue();
        await Assert.That(goodsStream.ReadBoolean()).IsTrue();
        await Assert.That(goodsStream.ReadUInt32()).IsEqualTo(14u);
        await Assert.That(goodsStream.ReadUInt32()).IsEqualTo(30u);
        await Assert.That(goodsStream.Pos).IsEqualTo(goodsStream.Count);
    }

    [Test]
    public async Task SpecialtyEventMessage_WritesMessage()
    {
        var stream = new PacketStream();

        new SCSpecialtyEventMsgPacket("Event started").Write(stream);

        stream.Rollback();
        await Assert.That(stream.ReadString()).IsEqualTo("Event started");
        await Assert.That(stream.Pos).IsEqualTo(stream.Count);
    }
}
