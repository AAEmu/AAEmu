using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.UnitTests.Game.Models.Game.Auction;

public class AuctionSoldRecordRulesTests
{
    [Test]
    public async Task BuildDays_AlwaysReturnsFourteenRowsAndKeepsTheLatestUnitPrice()
    {
        var now = new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
        var sales = new[]
        {
            new AuctionSale(10, 2, now.AddHours(-2), 400, 2),
            new AuctionSale(10, 2, now.AddHours(-1), 900, 3),
            new AuctionSale(10, 2, now.AddDays(-3), 100, 1),
            new AuctionSale(11, 2, now, 50, 1)
        };

        var days = AuctionSoldRecordRules.BuildDays(sales, 10, 2, now);
        await Assert.That(days.Count).IsEqualTo(14);
        await Assert.That(days[0].Volume).IsEqualTo(5);
        await Assert.That(days[0].MinPrice).IsEqualTo(200);
        await Assert.That(days[0].MaxPrice).IsEqualTo(300);
        await Assert.That(days[0].LastPrice).IsEqualTo(300);
        await Assert.That(days[0].AveragePrice).IsEqualTo((200L * 2 + 300L * 3) / 5);
        await Assert.That(days[3].Volume).IsEqualTo(1);
        await Assert.That(days[3].LastPrice).IsEqualTo(100);
        await Assert.That(days[1].Volume).IsEqualTo(0);
    }
}
