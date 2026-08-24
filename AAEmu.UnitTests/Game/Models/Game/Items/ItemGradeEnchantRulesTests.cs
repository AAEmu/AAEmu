using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

/// <summary>
/// Covers the prioritized regrade outcome ladder (success → break → disable → downgrade → fail)
/// driven by item_enchant_ratios, including charm adjustments and downgrade target handling.
/// </summary>
public class ItemGradeEnchantRulesTests
{
    private static GradeTemplate Grade(byte id, int order) => new() { Grade = id, GradeOrder = order };

    private static ItemEnchantRatio Ratio(
        int success = 0, int greatSuccess = 0, int breakRatio = 0,
        int downgrade = 0, int disable = 0,
        int downMin = -1, int downMax = -1, int cost = 10)
    {
        return new ItemEnchantRatio
        {
            GroupId = 1,
            Grade = 3,
            SuccessRatio = success,
            GreatSuccessRatio = greatSuccess,
            BreakRatio = breakRatio,
            DowngradeRatio = downgrade,
            DisableRatio = disable,
            DowngradeMin = downMin,
            DowngradeMax = downMax,
            Cost = cost
        };
    }

    private static int OrderOf(int grade) => grade + 1; // shipped table: order = grade + 1

    [Test]
    public async Task Resolve_RollBelowSuccess_Succeeds()
    {
        var ratio = Ratio(success: 7000);
        var current = Grade(3, 4);
        var (result, newOrder) = ItemGradeEnchantRules.Resolve(
            ratio, current, 6999, false, null, OrderOf);

        await Assert.That(result).IsEqualTo(GradeEnchantResult.Success);
        await Assert.That(newOrder).IsEqualTo(5); // next grade order
    }

    [Test]
    public async Task Resolve_RollAtSuccessBoundary_FallsThrough()
    {
        var ratio = Ratio(success: 7000);
        var current = Grade(3, 4);
        var (result, _) = ItemGradeEnchantRules.Resolve(
            ratio, current, 7000, false, null, OrderOf);

        await Assert.That(result).IsNotEqualTo(GradeEnchantResult.Success);
    }

    [Test]
    public async Task Resolve_SuccessThenBreakBand_Breaks()
    {
        var ratio = Ratio(success: 5000, breakRatio: 3000);
        var current = Grade(3, 4);
        var (result, newOrder) = ItemGradeEnchantRules.Resolve(
            ratio, current, 5000, false, null, OrderOf);

        await Assert.That(result).IsEqualTo(GradeEnchantResult.Break);
        await Assert.That(newOrder).IsEqualTo(current.GradeOrder);
    }

    [Test]
    public async Task Resolve_DowngradeBand_UsesAbsoluteTargetGrades()
    {
        // wonder-fail rows drop to absolute grades in the shipped range
        var ratio = Ratio(success: 2000, downgrade: 10000, downMin: 5, downMax: 7);
        var current = Grade(8, 9);

        // roll 9999 is below the full downgrade band (2000..12000 clamps at 10000)
        var (result, newOrder) = ItemGradeEnchantRules.Resolve(
            ratio, current, 9999, false, null, OrderOf);

        await Assert.That(result).IsEqualTo(GradeEnchantResult.Downgrade);
        await Assert.That(newOrder).IsGreaterThanOrEqualTo(OrderOf(5));
        await Assert.That(newOrder).IsLessThanOrEqualTo(OrderOf(7));
        await Assert.That(newOrder < current.GradeOrder).IsTrue();
    }

    [Test]
    public async Task Resolve_DowngradeImpossible_FallsBackToFail()
    {
        // -1 bounds are the shipped marker for "cannot downgrade".
        var ratio = Ratio(success: 1000, downgrade: 5000, downMin: -1, downMax: -1);
        var current = Grade(3, 4);
        var (result, newOrder) = ItemGradeEnchantRules.Resolve(
            ratio, current, 4999, false, null, OrderOf);

        await Assert.That(result).IsEqualTo(GradeEnchantResult.Fail);
        await Assert.That(newOrder).IsEqualTo(current.GradeOrder);
    }

    [Test]
    public async Task Resolve_DowngradeToPoor_IsLegalTarget()
    {
        // Grade id 0 (poor) is a real grade, so 0 bounds are valid targets, not "impossible".
        var ratio = Ratio(success: 1000, downgrade: 10000, downMin: 0, downMax: 0);
        var current = Grade(3, 4);
        var (result, newOrder) = ItemGradeEnchantRules.Resolve(
            ratio, current, 9999, false, null, OrderOf);

        await Assert.That(result).IsEqualTo(GradeEnchantResult.Downgrade);
        await Assert.That(newOrder).IsEqualTo(OrderOf(0));
    }

    [Test]
    public async Task Resolve_DisableBand_DisablesWithoutBreaking()
    {
        // Crafted-style row: no break column, disable instead.
        var ratio = Ratio(success: 6000, disable: 5000);
        var current = Grade(7, 8);
        var (result, newOrder) = ItemGradeEnchantRules.Resolve(
            ratio, current, 6999, false, null, OrderOf);

        await Assert.That(result).IsEqualTo(GradeEnchantResult.Disable);
        await Assert.That(newOrder).IsEqualTo(current.GradeOrder);
    }

    [Test]
    public async Task Resolve_LuckyRoll_TopSliceOfSuccess_GreatSuccess()
    {
        var ratio = Ratio(success: 6000, greatSuccess: 1000);
        var current = Grade(3, 4);
        // top slice of the success band: 6000-1000 .. 6000
        var (result, newOrder) = ItemGradeEnchantRules.Resolve(
            ratio, current, 5500, true, null, OrderOf);

        await Assert.That(result).IsEqualTo(GradeEnchantResult.GreatSuccess);
        await Assert.That(newOrder).IsEqualTo(current.GradeOrder + 2);
    }

    [Test]
    public async Task Resolve_LuckyRoll_LowerSuccessSlice_PlainSuccess()
    {
        var ratio = Ratio(success: 6000, greatSuccess: 1000);
        var current = Grade(3, 4);
        var (result, newOrder) = ItemGradeEnchantRules.Resolve(
            ratio, current, 4000, true, null, OrderOf);

        await Assert.That(result).IsEqualTo(GradeEnchantResult.Success);
        await Assert.That(newOrder).IsEqualTo(current.GradeOrder + 1);
    }

    [Test]
    public async Task Resolve_GreatSuccessRequiresLucky()
    {
        var ratio = Ratio(success: 6000, greatSuccess: 1000);
        var current = Grade(3, 4);
        var (result, newOrder) = ItemGradeEnchantRules.Resolve(
            ratio, current, 5500, false, null, OrderOf);

        await Assert.That(result).IsEqualTo(GradeEnchantResult.Success);
        await Assert.That(newOrder).IsEqualTo(current.GradeOrder + 1);
    }

    [Test]
    public async Task Resolve_CharmAdjustments_BroadenBandsWithinCap()
    {
        var ratio = Ratio(success: 5000);
        var current = Grade(3, 4);
        var charm = new ItemGradeEnchantRules.CharmAdjustment(
            addSuccessRatio: 1000, addSuccessMul: 20, // 5000 + 1000 + (5000*20%) = 7000
            addGreatSuccessRatio: 0, addGreatSuccessMul: 0,
            addBreakRatio: 0, addBreakMul: 0,
            addDowngradeRatio: 0, addDowngradeMul: 0);

        // 6999 fails un-charmed (success band is only 5000); charmed it succeeds.
        var (result, _) = ItemGradeEnchantRules.Resolve(ratio, current, 6999, false, charm, OrderOf);
        await Assert.That(result).IsEqualTo(GradeEnchantResult.Success);
    }

    [Test]
    public async Task Adjust_ClampsToMaxAndFloor()
    {
        var max = ItemGradeEnchantRules.Adjust(9000, 5000, 50);
        await Assert.That(max).IsEqualTo(ItemGradeEnchantRules.MaxRatio);

        var floor = ItemGradeEnchantRules.Adjust(1000, -2000, 0);
        await Assert.That(floor).IsEqualTo(0);
    }

    [Test]
    public async Task DeadEndRows_AreDetected()
    {
        var dead = Ratio(); // all zero (mythic/arche rows)
        await Assert.That(dead.IsDeadEnd).IsTrue();

        var alive = Ratio(success: 130);
        await Assert.That(alive.IsDeadEnd).IsFalse();
    }
}
