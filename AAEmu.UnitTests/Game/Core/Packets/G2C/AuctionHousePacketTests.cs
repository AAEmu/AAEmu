using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Auction;
using AAEmu.Game.Models.Game.Auction.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class AuctionHousePacketTests
{
    [Test]
    public async Task Listing_RoundTripsThe10_0_2_13FieldOrder()
    {
        var posted = new DateTime(2026, 9, 5, 9, 0, 0, DateTimeKind.Utc);
        var lot = new AuctionLot
        {
            Id = 99,
            Duration = AuctionDuration.AuctionDuration24Hours,
            Item = new Item(1) { TemplateId = 0 },
            WorldId = 1,
            ClientId = 7,
            ClientName = "Seller",
            StartMoney = 100,
            DirectMoney = 500,
            PostDate = posted,
            Asked = 1_725_000_000,
            ChargePercent = 200,
            DepositPercent = 15,
            ServiceKind = 0,
            BidWorldId = 255,
            BidderId = 8,
            BidderName = "Bidder",
            BidMoney = 150,
            ExtraMoney = 0,
            MinStack = 1,
            MaxStack = 1
        };

        var body = lot.Write(new PacketStream());
        var read = new AuctionLot();
        read.Read(new PacketStream(body.GetBytes()));

        await Assert.That(read.Id).IsEqualTo(99ul);
        await Assert.That(read.Duration).IsEqualTo(AuctionDuration.AuctionDuration24Hours);
        await Assert.That(read.WorldId).IsEqualTo((byte)1);
        await Assert.That(read.ClientId).IsEqualTo(7u);
        await Assert.That(read.ClientName).IsEqualTo("Seller");
        await Assert.That(read.StartMoney).IsEqualTo(100L);
        await Assert.That(read.DirectMoney).IsEqualTo(500L);
        await Assert.That(read.Asked).IsEqualTo(1_725_000_000ul);
        await Assert.That(read.ChargePercent).IsEqualTo(200);
        await Assert.That(read.DepositPercent).IsEqualTo(15);
        await Assert.That(read.BidderId).IsEqualTo(8u);
        await Assert.That(read.BidderName).IsEqualTo("Bidder");
        await Assert.That(read.BidMoney).IsEqualTo(150L);
        await Assert.That(read.MinStack).IsEqualTo(1);
        await Assert.That(read.MaxStack).IsEqualTo(1);
    }

    [Test]
    public async Task SearchCriteria_RoundTripsWireOrderIncludingPrices()
    {
        var search = new AuctionSearch
        {
            Keyword = "ore",
            ExactMatch = true,
            Grade = 2,
            CategoryA = 1,
            CategoryB = 4,
            CategoryC = 9,
            Page = 3,
            ClientId = 77,
            Filter = 0,
            ItemListCount = 0,
            WorldId = 1,
            MinItemLevel = 5,
            MaxItemLevel = 40,
            MinPrice = 10,
            MaxPrice = 99_999,
            SortKind = AuctionSearchSortKind.DirectPrice,
            SortOrder = AuctionSearchSortOrder.Desc
        };

        var body = search.Write(new PacketStream()).GetBytes();
        var read = new AuctionSearch();
        read.Read(new PacketStream(body));

        await Assert.That(read.Keyword).IsEqualTo("ore");
        await Assert.That(read.ExactMatch).IsTrue();
        await Assert.That(read.Page).IsEqualTo(3);
        await Assert.That(read.ClientId).IsEqualTo(77ul);
        await Assert.That(read.MinPrice).IsEqualTo(10L);
        await Assert.That(read.MaxPrice).IsEqualTo(99_999L);
        await Assert.That(read.SortKind).IsEqualTo(AuctionSearchSortKind.DirectPrice);
        await Assert.That(read.SortOrder).IsEqualTo(AuctionSearchSortOrder.Desc);
    }

    [Test]
    public async Task MultilingualSearch_ClampsTheTemplateIdArray()
    {
        var stream = new PacketStream();
        stream.Write(200);
        for (var i = 0; i < 200; i++)
            stream.Write((uint)i);

        var search = new AuctionSearch();
        search.ReadItemTemplateIds(new PacketStream(stream.GetBytes()));
        await Assert.That(search.ItemTemplateIds.Count).IsEqualTo(AuctionHouseRules.MultilingualItemIdLimit);
        await Assert.That(search.ItemListCount).IsEqualTo(AuctionHouseRules.MultilingualItemIdLimit);
    }

    [Test]
    public async Task Message_IsKindTemplateAndMoney()
    {
        var body = new SCAuctionMessagePacket(AuctionMessageKind.Outbid, 27501, 12_000)
            .Write(new PacketStream())
            .GetBytes();

        var expected = new PacketStream();
        expected.Write((byte)AuctionMessageKind.Outbid);
        expected.Write(27501u);
        expected.Write(12_000L);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
        await Assert.That(body.Length).IsEqualTo(13);
    }

    [Test]
    public async Task LowestPrice_WritesMoneyAsInt64()
    {
        var body = new SCAuctionLowestPricePacket(10, 2, 1_500_000_000)
            .Write(new PacketStream())
            .GetBytes();

        await Assert.That(BitConverter.ToUInt32(body, 0)).IsEqualTo(10u);
        await Assert.That(body[4]).IsEqualTo((byte)2);
        await Assert.That(BitConverter.ToInt64(body, 5)).IsEqualTo(1_500_000_000L);
        await Assert.That(body.Length).IsEqualTo(13);
    }

    [Test]
    public async Task Searched_ClampsThePageToNineLots()
    {
        var lots = Enumerable.Range(0, 12).Select(i => new AuctionLot
        {
            Id = (ulong)i,
            Item = new Item(0) { TemplateId = 0 },
            ClientName = string.Empty,
            BidderName = string.Empty
        }).ToList();

        var body = new SCAuctionSearchedPacket(1, lots, 0, new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc))
            .Write(new PacketStream())
            .GetBytes();

        await Assert.That(BitConverter.ToInt32(body, 0)).IsEqualTo(1);
        await Assert.That(BitConverter.ToInt32(body, 4)).IsEqualTo(9);
    }

    [Test]
    public async Task SoldRecord_AlwaysWritesFourteenDayRows()
    {
        var days = AuctionSoldRecordRules.BuildDays([], 10, 1, DateTime.UtcNow);
        var body = new SCAuctionSoldRecordSearchedPacket(10, 1, true, days)
            .Write(new PacketStream())
            .GetBytes();

        var expected = new PacketStream();
        expected.Write(10u);
        expected.Write((byte)1);
        expected.Write(true);
        foreach (var day in days)
            expected.Write(day);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task LimitedPrice_EmptyListIsCountZero()
    {
        var body = new SCAuctionLimitedPricePacket([])
            .Write(new PacketStream())
            .GetBytes();

        await Assert.That(BitConverter.ToInt32(body, 0)).IsEqualTo(0);
        await Assert.That(body.Length).IsEqualTo(4);
    }

    [Test]
    public async Task Bid_RoundTripsLotWorldBidderMoneyAndStack()
    {
        var bid = new AuctionBid
        {
            LotId = 44,
            WorldId = 1,
            BidderId = 9,
            BidderName = "Buyer",
            Money = 2500,
            StackSize = 3
        };
        var read = new AuctionBid();
        read.Read(new PacketStream(bid.Write(new PacketStream()).GetBytes()));

        await Assert.That(read.LotId).IsEqualTo(44ul);
        await Assert.That(read.Money).IsEqualTo(2500L);
        await Assert.That(read.StackSize).IsEqualTo(3);
        await Assert.That(read.BidderName).IsEqualTo("Buyer");
    }

    [Test]
    public async Task Offsets_MatchThe10_0_2_13AuctionBlock()
    {
        await Assert.That(CSOffsets.CSAuctionPostPacket).IsEqualTo((ushort)0x0F8);
        await Assert.That(CSOffsets.CSAuctionSearchForMultilingualPacket).IsEqualTo((ushort)0x20F);
        await Assert.That(CSOffsets.CSSearchAuctionSoldRecordPacket).IsEqualTo((ushort)0x0FE);
        await Assert.That(SCOffsets.SCAuctionSoldRecordSearchedPacket).IsEqualTo((ushort)0x177);
        await Assert.That(SCOffsets.SCAuctionLimitedPricePacket).IsEqualTo((ushort)0x178);
        await Assert.That(MailType.AucBidFail).IsEqualTo((MailType)17);
    }
}
