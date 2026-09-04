using AAEmu.Game.Models.Game.FishSchools;

namespace AAEmu.UnitTests.Game.Models.Game.FishSchools;

public class FishSalePriceTests
{
    // Small tuna pack 27501 (소형 참다랑어 꾸러미). Catalog: item_prices.refund 91000,
    // fish_details 284–356, item_grades 0→100 / 2→150. The two live stand sales on 2026-09-04
    // were this template: a caught pack (grade 0) and `/item add self 27501 1 2`.
    private const int SmallTunaRefund = 91000;
    private const int SmallTunaMaxWeight = 356;
    private const int CaughtWeight = 320;
    private const int Grade0Multiplier = 100;
    private const int Grade2Multiplier = 150;

    [Test]
    public async Task CaughtSmallTunaPack_Grade0_PaysWeightShareOfRefund()
    {
        var ok = FishSalePrice.TryCalculate(
            SmallTunaRefund, Grade0Multiplier, CaughtWeight, SmallTunaMaxWeight, out var price);

        await Assert.That(ok).IsTrue();
        await Assert.That(price).IsEqualTo(81798);
    }

    [Test]
    public async Task GmSmallTunaPack_Grade2_PaysGradeAdjustedRefund()
    {
        var ok = FishSalePrice.TryCalculate(
            SmallTunaRefund, Grade2Multiplier, CaughtWeight, SmallTunaMaxWeight, out var price);

        await Assert.That(ok).IsTrue();
        await Assert.That(price).IsEqualTo(122697);
    }

    [Test]
    public async Task ZeroWeight_DoesNotPriceASale()
    {
        var ok = FishSalePrice.TryCalculate(
            SmallTunaRefund, Grade0Multiplier, 0f, SmallTunaMaxWeight, out var price);

        await Assert.That(ok).IsFalse();
        await Assert.That(price).IsEqualTo(0);
    }
}
