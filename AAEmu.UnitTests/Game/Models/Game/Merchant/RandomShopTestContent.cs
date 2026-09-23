using AAEmu.Game.Models.Game.Merchant;

namespace AAEmu.UnitTests.Game.Models.Game.Merchant;

/// <summary>
/// Fixture rows for the random merchant suites: the same shape the content loader reads
/// (flags as 't'/'f', integer weights and budgets), but every number here is test data, not a
/// shipped row. Each pack isolates one behavior:
/// pack 1 refreshable (free 2 / paid 1, sale_cnt 3 over 3 groups),
/// pack 2 refresh_use='f' with sale_cnt == group count (the shipped distinct-draw shape),
/// pack 3 ships refresh_multiply_use='t' (unknown semantics, must be refused),
/// pack 4 ships an unknown kind_id (must be refused),
/// pack 5 two groups weighted 9:1 with sale_cnt 1 (weight honoring).
/// </summary>
internal static class RandomShopTestContent
{
    internal const uint RefreshablePack = 1;
    internal const uint NoRefreshPack = 2;
    internal const uint MultiplyRefusedPack = 3;
    internal const uint UnknownKindPack = 4;
    internal const uint WeightedPack = 5;

    internal static readonly DateTime AnyMoment = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

    internal static IReadOnlyDictionary<uint, RandomMerchantPack> Build() =>
        RandomMerchantContentBuilder.Build(PackRows(), GroupRows(), GoodRows());

    internal static List<RandomMerchantPackRow> PackRows() =>
    [
        new()
        {
            Id = RefreshablePack, KindId = 7, ItemPointId = 0, SaleCnt = 3,
            RefreshUse = "t", RefreshMultiplyUse = "f",
            RefreshFreeCnt = 2, RefreshChargeCnt = 1,
            RefreshCurrencyId = 6, RefreshPoint = 1, RefreshItemId = 70001
        },
        new()
        {
            Id = NoRefreshPack, KindId = 7, ItemPointId = 0, SaleCnt = 2,
            RefreshUse = "f", RefreshMultiplyUse = "f",
            RefreshFreeCnt = 0, RefreshChargeCnt = 0,
            RefreshCurrencyId = 0, RefreshPoint = 0, RefreshItemId = 0
        },
        new()
        {
            Id = MultiplyRefusedPack, KindId = 7, ItemPointId = 0, SaleCnt = 1,
            RefreshUse = "t", RefreshMultiplyUse = "t",
            RefreshFreeCnt = 1, RefreshChargeCnt = 0,
            RefreshCurrencyId = 0, RefreshPoint = 0, RefreshItemId = 0
        },
        new()
        {
            Id = UnknownKindPack, KindId = 99, ItemPointId = 0, SaleCnt = 1,
            RefreshUse = "f", RefreshMultiplyUse = "f",
            RefreshFreeCnt = 0, RefreshChargeCnt = 0,
            RefreshCurrencyId = 0, RefreshPoint = 0, RefreshItemId = 0
        },
        new()
        {
            Id = WeightedPack, KindId = 0, ItemPointId = 0, SaleCnt = 1,
            RefreshUse = "f", RefreshMultiplyUse = "f",
            RefreshFreeCnt = 0, RefreshChargeCnt = 0,
            RefreshCurrencyId = 0, RefreshPoint = 0, RefreshItemId = 0
        }
    ];

    internal static List<RandomMerchantGroupRow> GroupRows() =>
    [
        new() { PackId = RefreshablePack, Id = 101, GroupNo = 1, Weight = 700 },
        new() { PackId = RefreshablePack, Id = 102, GroupNo = 2, Weight = 200 },
        new() { PackId = RefreshablePack, Id = 103, GroupNo = 3, Weight = 100 },
        new() { PackId = NoRefreshPack, Id = 201, GroupNo = 1, Weight = 1 },
        new() { PackId = NoRefreshPack, Id = 202, GroupNo = 2, Weight = 1 },
        new() { PackId = MultiplyRefusedPack, Id = 301, GroupNo = 1, Weight = 10 },
        new() { PackId = UnknownKindPack, Id = 401, GroupNo = 1, Weight = 10 },
        new() { PackId = WeightedPack, Id = 501, GroupNo = 1, Weight = 900_000 },
        new() { PackId = WeightedPack, Id = 502, GroupNo = 2, Weight = 100_000 }
    ];

    internal static List<RandomMerchantGoodRow> GoodRows() =>
    [
        new() { GroupId = 101, Id = 2001, ItemId = 80001, GradeId = 0, Cost = 100, Weight = 10 },
        new() { GroupId = 102, Id = 2002, ItemId = 80002, GradeId = 1, Cost = 200, Weight = 10 },
        new() { GroupId = 103, Id = 2003, ItemId = 80003, GradeId = 0, Cost = 300, Weight = 10 },
        new() { GroupId = 201, Id = 2004, ItemId = 80004, GradeId = 0, Cost = 50, Weight = 10 },
        new() { GroupId = 202, Id = 2005, ItemId = 80005, GradeId = 0, Cost = 60, Weight = 10 },
        new() { GroupId = 301, Id = 2006, ItemId = 80006, GradeId = 0, Cost = 70, Weight = 10 },
        new() { GroupId = 401, Id = 2007, ItemId = 80007, GradeId = 0, Cost = 80, Weight = 10 },
        new() { GroupId = 501, Id = 2008, ItemId = 80008, GradeId = 0, Cost = 90, Weight = 10 },
        new() { GroupId = 502, Id = 2009, ItemId = 80009, GradeId = 0, Cost = 95, Weight = 10 }
    ];
}
