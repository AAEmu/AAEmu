using AAEmu.Game.Models.Game.Auction;
using AAEmu.Game.Models.Game.Auction.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Auction;

public class AuctionHouseRulesTests
{
    [Test]
    public async Task HoursFor_MapsTheFourDurationBytes()
    {
        await Assert.That(AuctionHouseRules.HoursFor(AuctionDuration.AuctionDuration6Hours)).IsEqualTo(6);
        await Assert.That(AuctionHouseRules.HoursFor(AuctionDuration.AuctionDuration12Hours)).IsEqualTo(12);
        await Assert.That(AuctionHouseRules.HoursFor(AuctionDuration.AuctionDuration24Hours)).IsEqualTo(24);
        await Assert.That(AuctionHouseRules.HoursFor(AuctionDuration.AuctionDuration48Hours)).IsEqualTo(48);
        await Assert.That(AuctionHouseRules.IsValidDuration(AuctionDuration.AuctionDuration6Hours)).IsTrue();
        await Assert.That(AuctionHouseRules.IsValidDuration((AuctionDuration)4)).IsFalse();
    }

    [Test]
    public async Task PlayerHeldItem_ExcludesHouseAndMailEscrow()
    {
        var item = new Item(0) { Count = 1, SlotType = SlotType.Inventory };
        await Assert.That(AuctionHouseRules.IsPlayerHeldItem(item)).IsTrue();
        await Assert.That(AuctionHouseRules.IsEscrowSlot(SlotType.Inventory)).IsFalse();

        item.SlotType = SlotType.Auction;
        await Assert.That(AuctionHouseRules.IsPlayerHeldItem(item)).IsFalse();
        await Assert.That(AuctionHouseRules.IsEscrowSlot(SlotType.Auction)).IsTrue();

        item.SlotType = SlotType.Mail;
        await Assert.That(AuctionHouseRules.IsPlayerHeldItem(item)).IsFalse();
        await Assert.That(AuctionHouseRules.IsEscrowSlot(SlotType.Mail)).IsTrue();
    }

    [Test]
    public async Task PricesAreListable_RejectsEmptyInvertedAndOverflowBuyout()
    {
        await Assert.That(AuctionHouseRules.PricesAreListable(0, 0)).IsFalse();
        await Assert.That(AuctionHouseRules.PricesAreListable(-1, 100)).IsFalse();
        await Assert.That(AuctionHouseRules.PricesAreListable(200, 100)).IsFalse();
        await Assert.That(AuctionHouseRules.PricesAreListable(100, 0)).IsTrue();
        await Assert.That(AuctionHouseRules.PricesAreListable(0, 100)).IsTrue();
        await Assert.That(AuctionHouseRules.PricesAreListable(100, 100)).IsTrue();
        await Assert.That(AuctionHouseRules.PricesAreListable(1, AuctionHouseRules.MaxEscrowCopper + 1)).IsFalse();
    }

    [Test]
    public async Task BidCharge_TakesOnlyTheRaiseFromTheStandingBidder()
    {
        await Assert.That(AuctionHouseRules.TryGetBidCharge(150, 100, 100, 500, false, out var first, out var buyout)).IsTrue();
        await Assert.That(first).IsEqualTo(150L);
        await Assert.That(buyout).IsFalse();

        await Assert.That(AuctionHouseRules.TryGetBidCharge(200, 100, 150, 500, true, out var raise, out _)).IsTrue();
        await Assert.That(raise).IsEqualTo(50L);

        await Assert.That(AuctionHouseRules.TryGetBidCharge(500, 100, 150, 500, true, out var selfBuy, out var selfBuyout)).IsTrue();
        await Assert.That(selfBuy).IsEqualTo(350L);
        await Assert.That(selfBuyout).IsTrue();

        await Assert.That(AuctionHouseRules.TryGetBidCharge(500, 100, 150, 500, false, out var otherBuy, out var otherBuyout)).IsTrue();
        await Assert.That(otherBuy).IsEqualTo(500L);
        await Assert.That(otherBuyout).IsTrue();

        await Assert.That(AuctionHouseRules.TryGetBidCharge(150, 100, 150, 500, false, out _, out _)).IsFalse();
        await Assert.That(AuctionHouseRules.TryGetBidCharge(50, 100, 0, 500, false, out _, out _)).IsFalse();
        await Assert.That(AuctionHouseRules.TryGetBidCharge(AuctionHouseRules.MaxEscrowCopper + 1, 1, 0, 0, false, out _, out _)).IsFalse();
    }

    [Test]
    public async Task IsOwnedInBag_RejectsOwnerZeroEvenWhenTheSlotLooksLikeInventory()
    {
        var item = new Item(0) { Count = 1, SlotType = SlotType.Inventory, OwnerId = 0 };
        await Assert.That(AuctionHouseRules.IsOwnedInBag(item, 39)).IsFalse();

        item.OwnerId = 39;
        await Assert.That(AuctionHouseRules.IsOwnedInBag(item, 39)).IsTrue();
        await Assert.That(AuctionHouseRules.IsOwnedInBag(item, 38)).IsFalse();

        item.SlotType = SlotType.Bank;
        await Assert.That(AuctionHouseRules.IsOwnedInBag(item, 39)).IsFalse();
    }

    [Test]
    public async Task IsListableItem_RejectsBoundUccUnsellableAndEmptyStacks()
    {
        var template = new ItemTemplate { Sellable = true, BindType = ItemBindType.Normal };
        var item = new Item(0) { Template = template, Count = 1 };
        await Assert.That(AuctionHouseRules.IsListableItem(item)).IsTrue();

        item.SetFlag(ItemFlag.SoulBound);
        await Assert.That(AuctionHouseRules.IsListableItem(item)).IsFalse();
        item.RemoveFlag(ItemFlag.SoulBound);

        item.SetFlag(ItemFlag.HasUCC);
        await Assert.That(AuctionHouseRules.IsListableItem(item)).IsFalse();
        item.RemoveFlag(ItemFlag.HasUCC);

        template.Sellable = false;
        await Assert.That(AuctionHouseRules.IsListableItem(item)).IsFalse();
        template.Sellable = true;

        template.BindType = ItemBindType.BindOnPickup;
        await Assert.That(AuctionHouseRules.IsListableItem(item)).IsFalse();
        template.BindType = ItemBindType.BindOnAuctionWin;
        await Assert.That(AuctionHouseRules.IsListableItem(item)).IsTrue();

        item.Count = 0;
        await Assert.That(AuctionHouseRules.IsListableItem(item)).IsFalse();
        await Assert.That(AuctionHouseRules.IsHeldByHouse(item)).IsFalse();
        item.Count = 1;
        item.SlotType = SlotType.Auction;
        await Assert.That(AuctionHouseRules.IsHeldByHouse(item)).IsTrue();
    }

    [Test]
    public async Task ClampStacks_ForcesTheFullStackWhenPartialBuyIsOff()
    {
        await Assert.That(AuctionHouseRules.ClampStacks(20, 1, 5, false)).IsEqualTo((20, 20));
        await Assert.That(AuctionHouseRules.ClampStacks(20, 3, 8, true)).IsEqualTo((3, 8));
        await Assert.That(AuctionHouseRules.ClampStacks(5, 8, 1, true)).IsEqualTo((5, 5));
    }

    [Test]
    public async Task Matches_FiltersSellerWorldKeywordGradeLevelAndPrice()
    {
        var lot = Lot("iron ore", 10, 2, 1, 2, 3, 500);
        lot.ClientId = 77;
        lot.WorldId = 1;

        var search = new AuctionSearch
        {
            Keyword = "iron",
            Grade = 2,
            CategoryA = 1,
            MinItemLevel = 5,
            MaxItemLevel = 15,
            MinPrice = 100,
            MaxPrice = 1000,
            ClientId = 77,
            WorldId = 1
        };

        await Assert.That(AuctionHouseRules.Matches(lot, search, [], "iron ore")).IsTrue();

        search.Keyword = "copper";
        await Assert.That(AuctionHouseRules.Matches(lot, search, [], "iron ore")).IsFalse();

        search.Keyword = "iron ore";
        search.ExactMatch = true;
        await Assert.That(AuctionHouseRules.Matches(lot, search, [], "Iron Ore")).IsTrue();

        search.ClientId = 9;
        await Assert.That(AuctionHouseRules.Matches(lot, search, [], "Iron Ore")).IsFalse();
    }

    [Test]
    public async Task Matches_UsesTheMultilingualTemplateListWhenPresent()
    {
        var lot = Lot("iron ore", 10, 0, 0, 0, 0, 100);
        lot.Item.TemplateId = 42;

        await Assert.That(AuctionHouseRules.Matches(lot, new AuctionSearch(), [42, 7], "iron ore")).IsTrue();
        await Assert.That(AuctionHouseRules.Matches(lot, new AuctionSearch(), [7], "iron ore")).IsFalse();
    }

    [Test]
    public async Task Matches_KeepsIdHitsWhenTheTypedKeywordIsAnotherLanguage()
    {
        var lot = Lot("전문화 확장의 인장", 1, 0, 6, 18, 0, 100);
        lot.Item.TemplateId = 29656;
        lot.ClientId = 39;

        var search = new AuctionSearch
        {
            Keyword = "博学之章",
            ClientId = 39UL | (1UL << 32)
        };

        await Assert.That(AuctionHouseRules.Matches(lot, search, [29656], string.Empty)).IsTrue();
        await Assert.That(AuctionHouseRules.Matches(lot, search, [7769], string.Empty)).IsFalse();
    }

    [Test]
    public async Task Matches_KeywordOnlyFallsBackToTheTemplateName()
    {
        var lot = Lot("전문화 확장의 인장", 1, 0, 0, 0, 0, 100);
        var search = new AuctionSearch { Keyword = "전문화" };

        await Assert.That(AuctionHouseRules.Matches(lot, search, [], string.Empty)).IsTrue();
        search.Keyword = "snow flake";
        await Assert.That(AuctionHouseRules.Matches(lot, search, [], string.Empty)).IsFalse();
    }

    [Test]
    public async Task Matches_KeywordOnlyHitsAnyLocalizedName()
    {
        var lot = Lot("전문화 확장의 인장", 1, 0, 0, 0, 0, 100);
        lot.Item.TemplateId = 29656;
        var names = new[] { "전문화 확장의 인장", "Specialization Snowflake", "博学之章" };

        await Assert.That(AuctionHouseRules.Matches(lot, new AuctionSearch { Keyword = "博学之章" }, [], names)).IsTrue();
        await Assert.That(AuctionHouseRules.Matches(lot, new AuctionSearch { Keyword = "Specialization Snowflake" }, [], names)).IsTrue();
        await Assert.That(AuctionHouseRules.Matches(lot, new AuctionSearch { Keyword = "전문화" }, [], names)).IsTrue();
        await Assert.That(AuctionHouseRules.Matches(lot, new AuctionSearch { Keyword = "snow flake" }, [], names)).IsFalse();
        await Assert.That(AuctionHouseRules.Matches(lot, new AuctionSearch { Keyword = "Specialization" }, [], ["", "Specialization Snowflake"])).IsTrue();
    }

    [Test]
    public async Task Sort_ExpireDateUsesEndTimeNotPostDate()
    {
        var early = Lot("a", 1, 0, 0, 0, 0, 1);
        early.Id = 2;
        early.EndTime = DateTime.UtcNow.AddHours(1);
        var late = Lot("b", 1, 0, 0, 0, 0, 1);
        late.Id = 1;
        late.EndTime = DateTime.UtcNow.AddHours(5);

        var ordered = AuctionHouseRules.Sort([late, early], AuctionSearchSortKind.ExpireDate, AuctionSearchSortOrder.Asc).ToList();
        await Assert.That(ordered[0].Id).IsEqualTo(2ul);
        await Assert.That(ordered[1].Id).IsEqualTo(1ul);
    }

    [Test]
    public async Task Page_ReturnsNineLotsAndAnEmptyOutOfRangePage()
    {
        var lots = Enumerable.Range(0, 20).Select(i =>
        {
            var lot = Lot("x", 1, 0, 0, 0, 0, 1);
            lot.Id = (ulong)i;
            return lot;
        }).ToList();

        var page0 = AuctionHouseRules.Page(lots, 0);
        var page1 = AuctionHouseRules.Page(lots, 1);
        var page2 = AuctionHouseRules.Page(lots, 2);

        await Assert.That(page0.Count).IsEqualTo(9);
        await Assert.That(page0[0].Id).IsEqualTo(0ul);
        await Assert.That(page1.Count).IsEqualTo(9);
        await Assert.That(page1[0].Id).IsEqualTo(9ul);
        await Assert.That(page2.Count).IsEqualTo(2);
        await Assert.That(AuctionHouseRules.Page(lots, 3)).IsEmpty();
    }

    [Test]
    public async Task SoulBoundAndUcc_FollowItemFlags()
    {
        var item = new Item(0);
        await Assert.That(AuctionHouseRules.IsSoulBound(item)).IsFalse();
        item.SetFlag(ItemFlag.SoulBound);
        await Assert.That(AuctionHouseRules.IsSoulBound(item)).IsTrue();
        item.SetFlag(ItemFlag.HasUCC);
        await Assert.That(AuctionHouseRules.HasUcc(item)).IsTrue();
    }

    private static AuctionLot Lot(string name, int level, byte grade, int a, int b, int c, long buyout)
    {
        var template = new ItemTemplate
        {
            Id = 1,
            Name = name,
            Level = level,
            AuctionSettings = new AuctionSettings(a, b, c, 0, true)
        };
        return new AuctionLot
        {
            Item = new Item(0)
            {
                TemplateId = template.Id,
                Template = template,
                Grade = grade,
                Count = 1
            },
            DirectMoney = buyout
        };
    }
}
