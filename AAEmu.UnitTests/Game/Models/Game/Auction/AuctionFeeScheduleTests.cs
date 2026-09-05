using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.UnitTests.Game.Models.Game.Auction;

public class AuctionFeeScheduleTests
{
    [Test]
    public async Task ListingDeposit_IsPerMilleOfBuyoutCappedAtAMillion()
    {
        var fees = new AuctionFeeSchedule();
        await Assert.That(fees.GetListingDeposit(100_000, AuctionDuration.AuctionDuration6Hours)).IsEqualTo(500);
        await Assert.That(fees.GetListingDeposit(100_000, AuctionDuration.AuctionDuration12Hours)).IsEqualTo(1_000);
        await Assert.That(fees.GetListingDeposit(100_000, AuctionDuration.AuctionDuration24Hours)).IsEqualTo(1_500);
        await Assert.That(fees.GetListingDeposit(100_000, AuctionDuration.AuctionDuration48Hours)).IsEqualTo(2_000);
        await Assert.That(fees.GetListingDeposit(1_000_000_000, AuctionDuration.AuctionDuration48Hours)).IsEqualTo(1_000_000);
        await Assert.That(fees.GetListingDeposit(0, AuctionDuration.AuctionDuration6Hours)).IsEqualTo(0);
    }

    [Test]
    public async Task SaleCharge_IsTwoPercentUnlessTheItemOverrides()
    {
        var fees = new AuctionFeeSchedule();
        await Assert.That(fees.GetSaleCharge(10_000)).IsEqualTo(200);
        await Assert.That(fees.GetSaleCharge(10_000, 1000)).IsEqualTo(1_000);
        await Assert.That(fees.GetSaleCharge(0)).IsEqualTo(0);
    }

    [Test]
    public async Task AccountBuffDiscount_CutsTheStoredRate()
    {
        await Assert.That(AuctionFeeSchedule.ApplyPercentDiscount(200, 25)).IsEqualTo(150);
        await Assert.That(AuctionFeeSchedule.ApplyPercentDiscount(20, 0)).IsEqualTo(20);

        var fees = new AuctionFeeSchedule();
        await Assert.That(fees.GetSaleCharge(10_000, 0, 25)).IsEqualTo(150);
        await Assert.That(fees.GetListingDeposit(100_000, AuctionDuration.AuctionDuration12Hours, 50)).IsEqualTo(500);
    }
}
