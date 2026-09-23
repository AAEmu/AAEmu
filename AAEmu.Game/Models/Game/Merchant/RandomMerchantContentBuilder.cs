using AAEmu.Game.Models.Game.Items;

using NLog;

namespace AAEmu.Game.Models.Game.Merchant;

/// <summary>
/// Loud refusal raised when random-merchant content cannot be trusted: a missing pack row, an
/// unknown pack kind, an unparsable content flag, or the shipped-but-undecoded
/// <c>refresh_multiply_use</c> semantics. Callers fail and say so; they never fall back to a
/// default number.
/// </summary>
public sealed class RandomMerchantContentException(string message) : Exception(message);

/// <summary>One raw <c>merchant_random_packs</c> row as the loader read it. Boolean content columns arrive as 't'/'f'.</summary>
public sealed class RandomMerchantPackRow
{
    public uint Id { get; set; }
    public byte KindId { get; set; }
    public uint ItemPointId { get; set; }
    public int SaleCnt { get; set; }
    public string RefreshUse { get; set; }
    public string RefreshMultiplyUse { get; set; }
    public int RefreshFreeCnt { get; set; }
    public int RefreshChargeCnt { get; set; }
    public uint RefreshCurrencyId { get; set; }
    public int RefreshPoint { get; set; }
    public uint RefreshItemId { get; set; }
}

/// <summary>One raw <c>merchant_random_groups</c> row.</summary>
public sealed class RandomMerchantGroupRow
{
    public uint PackId { get; set; }
    public uint Id { get; set; }
    public int GroupNo { get; set; }
    public long Weight { get; set; }
}

/// <summary>One raw <c>merchant_random_goods</c> row.</summary>
public sealed class RandomMerchantGoodRow
{
    public uint GroupId { get; set; }
    public uint Id { get; set; }
    public uint ItemId { get; set; }
    public byte GradeId { get; set; }
    public int Cost { get; set; }
    public long Weight { get; set; }
}

/// <summary>
/// Pure row-to-model builder for the random merchant catalog. Validation is loud: a pack that
/// fails validation is kept in the dictionary marked <see cref="RandomMerchantPack.Usable"/> false
/// (with an error log), and the manager refuses to open a window for it by raising
/// <see cref="RandomMerchantContentException"/>. Every gameplay number (weights, sale counts,
/// refresh budgets, prices) comes from these content rows - nothing is defaulted in C#.
/// </summary>
public static class RandomMerchantContentBuilder
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static IReadOnlyDictionary<uint, RandomMerchantPack> Build(
        IEnumerable<RandomMerchantPackRow> packRows,
        IEnumerable<RandomMerchantGroupRow> groupRows,
        IEnumerable<RandomMerchantGoodRow> goodRows)
    {
        var packs = new Dictionary<uint, RandomMerchantPack>();
        foreach (var row in packRows)
        {
            if (packs.ContainsKey(row.Id))
            {
                Logger.Error("Random merchant: duplicate merchant_random_packs row {0} - keeping the first, refusing the duplicate", row.Id);
                continue;
            }
            packs[row.Id] = BuildPackHeader(row);
        }

        // Groups per pack, kept local until eligibility is decided (an orphan or refused pack's
        // groups are never exposed, so a bad join cannot leak into a window roll).
        var groupsByPack = new Dictionary<uint, List<RandomMerchantGroup>>();
        foreach (var row in groupRows)
        {
            if (!packs.TryGetValue(row.PackId, out var pack) || !pack.Usable)
            {
                Logger.Error("Random merchant: merchant_random_groups row {0} points at missing or refused pack {1} - skipped", row.Id, row.PackId);
                continue;
            }
            if (!groupsByPack.TryGetValue(row.PackId, out var list))
            {
                list = [];
                groupsByPack[row.PackId] = list;
            }
            list.Add(new RandomMerchantGroup { Id = row.Id, GroupNo = row.GroupNo, Weight = row.Weight });
        }

        var groupsById = groupsByPack
            .SelectMany(pair => pair.Value.Select(group => (PackId: pair.Key, Group: group)))
            .GroupBy(entry => entry.Group.Id)
            .ToDictionary(entries => entries.Key, entries => entries.First());

        foreach (var row in goodRows)
        {
            if (!groupsById.TryGetValue(row.GroupId, out var entry))
            {
                Logger.Error("Random merchant: merchant_random_goods row {0} points at missing group {1} - skipped", row.Id, row.GroupId);
                continue;
            }
            if (row.Cost < 0)
            {
                Logger.Error("Random merchant: merchant_random_goods row {0} has negative cost {1} - skipped, no price fallback is invented", row.Id, row.Cost);
                continue;
            }
            entry.Group.Goods.Add(new RandomMerchantGood
            {
                Id = row.Id,
                ItemId = row.ItemId,
                Grade = row.GradeId,
                Cost = row.Cost,
                Weight = row.Weight
            });
        }

        foreach (var (packId, groups) in groupsByPack)
        {
            var pack = packs[packId];
            foreach (var group in groups.OrderBy(group => group.GroupNo))
            {
                if (group.Weight <= 0)
                    continue; // zero-weight rows are content-legal and simply never drawn
                if (!group.Goods.Any(good => good.Weight > 0))
                {
                    Logger.Error("Random merchant: group {0} of pack {1} has no positive-weight good - excluded from the window draw", group.Id, packId);
                    continue;
                }
                pack.EligibleGroups.Add(group);
            }
        }

        // A pack that still has nothing to draw - no group rows at all, or none drawable - is
        // refused loudly rather than shipping an empty window.
        foreach (var pack in packs.Values)
        {
            if (!pack.Usable || pack.EligibleGroups.Count > 0)
                continue;
            pack.Usable = false;
            Logger.Error("Random merchant: pack {0} has no group that can yield an offer - refused", pack.Id);
        }

        return packs;
    }

    /// <summary><c>merchant_random_packs.kind_id</c> -&gt; the currency its offers are paid in. Null for kinds that ship no payable currency.</summary>
    public static ShopCurrencyType? ResolveShopCurrency(byte kindId) => kindId switch
    {
        (byte)MerchantPackKind.Money => ShopCurrencyType.Money,
        (byte)MerchantPackKind.Honor => ShopCurrencyType.Honor,
        (byte)MerchantPackKind.Vocation => ShopCurrencyType.VocationBadges,
        (byte)MerchantPackKind.ItemPoint => ShopCurrencyType.ItemPoint,
        (byte)MerchantPackKind.CustomItemPoint => ShopCurrencyType.ItemPoint,
        // MerchantPackKind.Empty and every gap in the 10.0.2.13 kind ids: no payable currency.
        _ => null
    };

    private static RandomMerchantPack BuildPackHeader(RandomMerchantPackRow row)
    {
        var refreshUse = ParseFlag(row.RefreshUse);
        var refreshMultiplyUse = ParseFlag(row.RefreshMultiplyUse);
        var currency = ResolveShopCurrency(row.KindId);

        var pack = new RandomMerchantPack
        {
            Id = row.Id,
            Kind = (MerchantPackKind)row.KindId,
            ItemPointId = row.ItemPointId,
            SaleCnt = row.SaleCnt,
            RefreshUse = refreshUse ?? false,
            RefreshMultiplyUse = refreshMultiplyUse ?? false,
            RefreshFreeCnt = row.RefreshFreeCnt,
            RefreshChargeCnt = row.RefreshChargeCnt,
            RefreshCurrencyId = row.RefreshCurrencyId,
            RefreshPoint = row.RefreshPoint,
            RefreshItemId = row.RefreshItemId,
            Currency = currency,
            RefreshCurrency = Enum.IsDefined(typeof(ContentCurrencyType), row.RefreshCurrencyId)
                ? (ContentCurrencyType?)row.RefreshCurrencyId
                : null
        };

        if (refreshUse == null || refreshMultiplyUse == null)
        {
            pack.Usable = false;
            Logger.Error(
                "Random merchant: pack {0} ships an unparsable refresh flag (refresh_use='{1}', refresh_multiply_use='{2}') - refused",
                row.Id, row.RefreshUse, row.RefreshMultiplyUse);
            return pack;
        }

        if (pack.RefreshMultiplyUse)
        {
            // Every shipped row is 'f'. Semantics of 't' are unknown, so a pack that ships it is
            // refused rather than silently treated as 'f'.
            pack.Usable = false;
            Logger.Error("Random merchant: pack {0} ships refresh_multiply_use='t' (unknown semantics) - refused", row.Id);
            return pack;
        }

        if (currency == null)
        {
            pack.Usable = false;
            Logger.Error("Random merchant: pack {0} has kind_id {1}, which maps to no payable shop currency - refused", row.Id, row.KindId);
            return pack;
        }

        if (pack.RefreshCurrency == null)
            Logger.Error(
                "Random merchant: pack {0} has refresh_currency_id {1}, which is no known content currency - free refreshes still work, a paid refresh will be refused loudly",
                row.Id, row.RefreshCurrencyId);

        if (pack.SaleCnt <= 0)
        {
            pack.Usable = false;
            Logger.Error("Random merchant: pack {0} has sale_cnt {1} - refused (a window must draw at least one offer)", row.Id, pack.SaleCnt);
            return pack;
        }

        return pack;
    }

    /// <summary>Exact content boolean: only 't' and 'f' parse. Anything else is null (loud refusal upstream).</summary>
    private static bool? ParseFlag(string raw) => raw switch
    {
        "t" => true,
        "f" => false,
        _ => null
    };
}
