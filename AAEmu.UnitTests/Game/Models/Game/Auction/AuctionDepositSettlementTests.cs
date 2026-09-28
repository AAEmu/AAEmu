using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.UnitTests.Game.Models.Game.Auction;

public class AuctionDepositSettlementTests
{
    [Test]
    public async Task DepositAtSettlement_UsesTheStoredPostDiscountRate()
    {
        var fees = new AuctionFeeSchedule();

        await Assert.That(fees.GetListingDepositForRate(400_000, 10)).IsEqualTo(4_000L);
        await Assert.That(fees.GetListingDepositForRate(400_000, 0)).IsEqualTo(0L);
    }

    [Test]
    public async Task SuccessfulSalePayout_ReturnsTheListingDeposit()
    {
        await Assert.That(AuctionHouseRules.SellerPayoutAfterSale(360_000, 4_000)).IsEqualTo(364_000L);
        await Assert.That(AuctionHouseRules.SellerPayoutAfterSale(360_000, 0)).IsEqualTo(360_000L);
    }
}
