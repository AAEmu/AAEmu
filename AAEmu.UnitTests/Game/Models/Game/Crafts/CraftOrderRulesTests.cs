using AAEmu.Game.Models.Game.Crafts;

namespace AAEmu.UnitTests.Game.Models.Game.Crafts;

/// <summary>
/// The board's decisions: who may post, what a fee has to clear, which orders a search page covers.
/// </summary>
public class CraftOrderRulesTests
{
    private static Craft Craft(bool orderable = true, int cost = 1_000, uint group = 3, int actability = 5_000)
    {
        var craft = new Craft
        {
            Id = 77,
            Orderable = orderable,
            Cost = cost,
            ActabilityGroupId = group,
            ActabilityLimit = actability
        };
        craft.CraftProducts.Add(new CraftProduct { Id = 1, CraftId = 77, ItemId = 500, Amount = 2 });
        return craft;
    }

    private static CraftOrder Order(uint id = 1, uint group = 3, uint actability = 5_000, ulong fee = 2_000) => new()
    {
        Id = id,
        OwnerId = 9,
        CraftId = 77,
        ItemId = 500,
        Count = 1,
        Fee = fee,
        ActabilityGroupId = group,
        ActabilityPoint = actability
    };

    [Test]
    public async Task Post_StopsAtTheClientEntryCap()
    {
        await Assert.That(CraftOrderRules.CanPost(0)).IsTrue();
        await Assert.That(CraftOrderRules.CanPost(CraftOrderRules.EntriesPerCharacter - 1)).IsTrue();
        await Assert.That(CraftOrderRules.CanPost(CraftOrderRules.EntriesPerCharacter)).IsFalse();
    }

    [Test]
    public async Task MinimumFee_ComesFromTheCraftCostColumn()
    {
        await Assert.That(CraftOrderRules.MinimumFee(Craft(cost: 260_000))).IsEqualTo(260_000ul);
        await Assert.That(CraftOrderRules.MinimumFee(Craft(cost: -5))).IsEqualTo(0ul);
        await Assert.That(CraftOrderRules.MinimumFee(null)).IsEqualTo(0ul);
    }

    [Test]
    public async Task Fee_BelowTheMinimumIsRefusedAtTheBoundary()
    {
        var craft = Craft(cost: 1_000);

        await Assert.That(CraftOrderRules.IsFeeAcceptable(craft, 999)).IsFalse();
        await Assert.That(CraftOrderRules.IsFeeAcceptable(craft, 1_000)).IsTrue();
        await Assert.That(CraftOrderRules.IsFeeAcceptable(null, 1_000)).IsFalse();
    }

    [Test]
    public async Task Orderable_NeedsTheContentFlagAndAProduct()
    {
        await Assert.That(CraftOrderRules.IsOrderable(Craft())).IsTrue();
        await Assert.That(CraftOrderRules.IsOrderable(Craft(orderable: false))).IsFalse();

        var productless = Craft();
        productless.CraftProducts.Clear();
        await Assert.That(CraftOrderRules.IsOrderable(productless)).IsFalse();
    }

    [Test]
    public async Task Fill_NeedsTheActabilityTheOrderAsksFor()
    {
        var order = Order(actability: 5_000);

        await Assert.That(CraftOrderRules.CanFill(order, 4_999)).IsFalse();
        await Assert.That(CraftOrderRules.CanFill(order, 5_000)).IsTrue();
    }

    [Test]
    public async Task Filter_KeepsTheRequestedGroupAndOptionallyOnlyFillableOrders()
    {
        var mine = Order(id: 1, group: 3, actability: 5_000);
        var otherGroup = Order(id: 2, group: 4, actability: 5_000);
        var tooHigh = Order(id: 3, group: 3, actability: 90_000);

        var everyGroup = new CraftOrderQuery(0, 0, 0, 0, false);
        await Assert.That(CraftOrderRules.MatchesFilter(mine, everyGroup, 0)).IsTrue();
        await Assert.That(CraftOrderRules.MatchesFilter(otherGroup, everyGroup, 0)).IsTrue();

        var group3 = new CraftOrderQuery(3, 0, 0, 0, false);
        await Assert.That(CraftOrderRules.MatchesFilter(mine, group3, 0)).IsTrue();
        await Assert.That(CraftOrderRules.MatchesFilter(otherGroup, group3, 0)).IsFalse();

        var fillableOnly = new CraftOrderQuery(3, 0, 0, 0, true);
        await Assert.That(CraftOrderRules.MatchesFilter(mine, fillableOnly, 5_000)).IsTrue();
        await Assert.That(CraftOrderRules.MatchesFilter(tooHigh, fillableOnly, 5_000)).IsFalse();
    }

    [Test]
    public async Task Page_IsOneBasedAndStopsPastTheEnd()
    {
        var all = Enumerable.Range(0, 20).Select(i => Order((uint)i)).ToList();

        // The client's page counter starts at 1 (captured live), so page 1 is the first page and
        // page 0 is read as the first page as well.
        var first = CraftOrderRules.PageOf(all, 1);
        await Assert.That(first.Count).IsEqualTo(CraftOrderWire.SearchEntryLimit);
        await Assert.That(first[0].Id).IsEqualTo(0ul);

        var fromZero = CraftOrderRules.PageOf(all, 0);
        await Assert.That(fromZero[0].Id).IsEqualTo(0ul);

        var third = CraftOrderRules.PageOf(all, 3);
        await Assert.That(third[0].Id).IsEqualTo((ulong)(2 * CraftOrderWire.SearchEntryLimit));

        var past = CraftOrderRules.PageOf(all, 99);
        await Assert.That(past.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Sorted_UsesTheAskedColumnAndKeepsIdAsTheTie()
    {
        var cheapLate = Order(id: 3, group: 4, fee: 100);
        var richEarly = Order(id: 1, group: 4, fee: 500);
        var mid = Order(id: 2, group: 1, fee: 300);
        var rows = new[] { cheapLate, richEarly, mid };

        var byFeeDesc = CraftOrderRules.Sorted(rows, new CraftOrderQuery(0, CraftOrderRules.SortKindFee, CraftOrderRules.SortDescending, 1, false));
        await Assert.That(byFeeDesc[0].Id).IsEqualTo(1ul);
        await Assert.That(byFeeDesc[1].Id).IsEqualTo(2ul);
        await Assert.That(byFeeDesc[2].Id).IsEqualTo(3ul);

        var byGroupAsc = CraftOrderRules.Sorted(rows, new CraftOrderQuery(0, CraftOrderRules.SortKindActabilityGroup, CraftOrderRules.SortAscending, 1, false));
        await Assert.That(byGroupAsc[0].Id).IsEqualTo(2ul);
        await Assert.That(byGroupAsc[1].Id).IsEqualTo(1ul);
        await Assert.That(byGroupAsc[2].Id).IsEqualTo(3ul);

        var byIdDesc = CraftOrderRules.Sorted(rows, new CraftOrderQuery(0, CraftOrderRules.SortKindDefault, CraftOrderRules.SortDescending, 1, false));
        await Assert.That(byIdDesc[0].Id).IsEqualTo(3ul);
        await Assert.That(byIdDesc[2].Id).IsEqualTo(1ul);
    }
}
