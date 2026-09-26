using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.PlotAuctions;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// Wire order for the plot-auction SC family, pinned from the 10.0.2.13 client's serializers:
/// BidResponse = activityId, auctionConfigId, errorCode, bidAmount (four s32);
/// BidUpdate = activityId, auctionConfigId, newBidAmount (three s32);
/// Info = activityId, then the bidInfoMap: u32 count, and per pair a u32 key plus the six-s32
/// value struct (/ in the client's deserializer).
/// </summary>
public class PlotAuctionPacketTests
{
    [Test]
    public async Task BidResponse_CarriesItsFourFieldsInOrder()
    {
        var body = new SCPlotAuctionBidResponsePacket(1001, 7, PlotAuctionErrorCodes.BidTooLow, 105)
            .Write(new PacketStream());
        await Assert.That(body.GetBytes().Length).IsEqualTo(16);

        var stream = new PacketStream(body.GetBytes());
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1001u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(7u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(PlotAuctionErrorCodes.BidTooLow);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(105u);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task BidUpdate_CarriesItsThreeFieldsInOrder()
    {
        var body = new SCPlotAuctionBidUpdatePacket(1001, 7, 110)
            .Write(new PacketStream());
        await Assert.That(body.GetBytes().Length).IsEqualTo(12);

        var stream = new PacketStream(body.GetBytes());
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1001u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(7u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(110u);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Info_WritesTheActivityThenTheMapCountAndPairs()
    {
        var rows = new List<PlotAuctionBidInfoRow>
        {
            new()
            {
                PlotId = 1, MyBidAmount = 105, MyRanking = 1,
                CurrentWinningBid = 105, TotalBidders = 2, BasePrice = 105,
            },
            new()
            {
                PlotId = 2, MyBidAmount = 0, MyRanking = 0,
                CurrentWinningBid = 0, TotalBidders = 0, BasePrice = 0,
            },
        };

        var body = new SCPlotAuctionInfoPacket(1001, rows).Write(new PacketStream());
        // activityId + count, then two pairs of (u32 key + six s32).
        await Assert.That(body.GetBytes().Length).IsEqualTo(4 + 4 + 2 * (4 + 6 * 4));

        var stream = new PacketStream(body.GetBytes());
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1001u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(2u);

        await Assert.That(stream.ReadUInt32()).IsEqualTo(1u); // pair key
        await Assert.That(stream.ReadInt32()).IsEqualTo(1); // value.plotId (repeats the key)
        await Assert.That(stream.ReadInt32()).IsEqualTo(105); // myBidAmount
        await Assert.That(stream.ReadInt32()).IsEqualTo(1); // myRanking
        await Assert.That(stream.ReadInt32()).IsEqualTo(105); // currentWinningBid
        await Assert.That(stream.ReadInt32()).IsEqualTo(2); // totalBidders
        await Assert.That(stream.ReadInt32()).IsEqualTo(105); // basePrice

        await Assert.That(stream.ReadUInt32()).IsEqualTo(2u); // pair key
        await Assert.That(stream.ReadInt32()).IsEqualTo(2); // value.plotId
        await Assert.That(stream.ReadInt32()).IsEqualTo(0);
        await Assert.That(stream.ReadInt32()).IsEqualTo(0);
        await Assert.That(stream.ReadInt32()).IsEqualTo(0);
        await Assert.That(stream.ReadInt32()).IsEqualTo(0);
        await Assert.That(stream.ReadInt32()).IsEqualTo(0);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Info_EmptyMapIsJustActivityAndZeroCount()
    {
        var body = new SCPlotAuctionInfoPacket(1001, []).Write(new PacketStream());
        await Assert.That(body.GetBytes().Length).IsEqualTo(8);
        var stream = new PacketStream(body.GetBytes());
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1001u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }
}
