using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Merchant;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

/// <summary>One row of <c>merchant_reopen_goods</c>.</summary>
public class MerchantReopenGood
{
    public uint Id { get; init; }
    public uint ItemId { get; init; }
    public byte GradeId { get; init; }
    public int Count { get; init; }
    public int Weight { get; init; }
}

/// <summary>A rank tier inside a pack, weighted against the pack's other tiers.</summary>
public class MerchantReopenGroup
{
    public uint Id { get; init; }
    public int Rank { get; init; }
    public int Weight { get; init; }
    public int DistributionWeight { get; init; }
    public List<MerchantReopenGood> Goods { get; } = [];
}

/// <summary>
/// One <c>merchant_reopen_packs</c> row with its tiers: the open limits, the paid-open price and
/// the reopen cooldown, all straight from content.
/// </summary>
public class MerchantReopenPack
{
    public uint Id { get; init; }

    /// <summary><c>merchant_reopen_packs.free_count</c> - free opens a box state gets.</summary>
    public int FreeCount { get; init; }

    /// <summary><c>merchant_reopen_packs.charge_count</c> - paid opens a box state gets.</summary>
    public int ChargeCount { get; init; }

    /// <summary><c>merchant_reopen_packs.currency_id</c> as a <see cref="ContentCurrencyType"/>.</summary>
    public ContentCurrencyType Currency { get; set; }

    /// <summary><c>merchant_reopen_packs.charge_point</c> - price of one paid open.</summary>
    public int ChargePoint { get; init; }

    /// <summary><c>merchant_reopen_packs.charge_item_id</c> - item consumed when the currency is item points.</summary>
    public uint ChargeItemId { get; init; }

    /// <summary>
    /// <c>gain_merchant_reopen_pack_item_effects.life_time</c> - minutes before the box may be
    /// rolled again (the record struct's lifeTime).
    /// </summary>
    public int LifeTime { get; init; }

    /// <summary>False when content validation refused the pack; every flow refuses it loudly.</summary>
    public bool Usable { get; set; } = true;

    /// <summary>Rank tiers of this pack; the draw picks one by weight, then a good inside it.</summary>
    public List<MerchantReopenGroup> Groups { get; } = [];
}

/// <summary>
/// The reopenable merchant packs (the "재개봉 랜박 상자" boxes): a two-stage weighted draw -
/// pick a rank tier from <c>merchant_reopen_groups</c> by weight, then an item from
/// <c>merchant_reopen_goods</c> by weight - plus each pack's open limits and paid-open price.
/// </summary>
[GameData]
public class MerchantReopenPackGameData : Singleton<MerchantReopenPackGameData>, IGameDataLoader
{
    private Dictionary<uint, MerchantReopenPack> _packs = [];

    public MerchantReopenPack GetPack(uint packId) =>
        _packs.TryGetValue(packId, out var pack) ? pack : null;

    public IReadOnlyDictionary<uint, MerchantReopenPack> Packs => _packs;

    public void Load(SqliteConnection connection)
    {
        var packs = new Dictionary<uint, MerchantReopenPack>();
        var groupsById = new Dictionary<uint, MerchantReopenGroup>();
        var groupPack = new Dictionary<uint, uint>();

        // life_time rows first: a pack without one has no reopen cooldown record, which the wire
        // record struct requires, so such a pack is refused rather than defaulted.
        var lifeTimeByPack = new Dictionary<uint, int>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT merchant_reopen_pack_id, life_time FROM gain_merchant_reopen_pack_item_effects";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
                lifeTimeByPack[reader.GetUInt32("merchant_reopen_pack_id")] = reader.GetInt32("life_time");
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT id, free_count, charge_count, currency_id, charge_point, charge_item_id FROM merchant_reopen_packs";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var pack = new MerchantReopenPack
                {
                    Id = reader.GetUInt32("id"),
                    FreeCount = reader.GetInt32("free_count"),
                    ChargeCount = reader.GetInt32("charge_count"),
                    ChargePoint = reader.GetInt32("charge_point"),
                    ChargeItemId = reader.GetUInt32("charge_item_id"),
                    LifeTime = lifeTimeByPack.TryGetValue(reader.GetUInt32("id"), out var lifeTime) ? lifeTime : 0
                };

                var currencyId = reader.GetUInt32("currency_id");
                var usable = true;
                if (!lifeTimeByPack.ContainsKey(pack.Id))
                {
                    Logger.Error(
                        "Reopen box: merchant_reopen_packs row {0} has no gain_merchant_reopen_pack_item_effects life_time row - refusing the pack",
                        pack.Id);
                    usable = false;
                }
                if (Enum.IsDefined(typeof(ContentCurrencyType), currencyId))
                    pack.Currency = (ContentCurrencyType)currencyId;
                else
                {
                    Logger.Error(
                        "Reopen box: merchant_reopen_packs row {0} has unknown currency_id {1} - refusing the pack",
                        pack.Id, currencyId);
                    usable = false;
                }
                if (pack.FreeCount < 0 || pack.ChargeCount < 0 || pack.ChargePoint < 0)
                {
                    Logger.Error(
                        "Reopen box: merchant_reopen_packs row {0} has negative limits (free {1}, charge {2}, point {3}) - refusing the pack",
                        pack.Id, pack.FreeCount, pack.ChargeCount, pack.ChargePoint);
                    usable = false;
                }

                pack.Usable = usable;
                packs[pack.Id] = pack;
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, merchant_reopen_pack_id, rank, weight, distribution_weight FROM merchant_reopen_groups";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var group = new MerchantReopenGroup
                {
                    Id = reader.GetUInt32("id"),
                    Rank = reader.GetInt32("rank"),
                    Weight = reader.GetInt32("weight"),
                    DistributionWeight = reader.GetInt32("distribution_weight")
                };
                var packId = reader.GetUInt32("merchant_reopen_pack_id");
                if (!packs.TryGetValue(packId, out var pack))
                    continue;
                pack.Groups.Add(group);
                groupsById[group.Id] = group;
                groupPack[group.Id] = packId;
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, merchant_reopen_group_id, item_id, grade_id, count, weight FROM merchant_reopen_goods";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var groupId = reader.GetUInt32("merchant_reopen_group_id");
                if (!groupsById.TryGetValue(groupId, out var group))
                    continue;
                group.Goods.Add(new MerchantReopenGood
                {
                    Id = reader.GetUInt32("id"),
                    ItemId = reader.GetUInt32("item_id"),
                    GradeId = (byte)reader.GetInt32("grade_id"),
                    Count = reader.GetInt32("count"),
                    Weight = reader.GetInt32("weight")
                });
            }
        }

        foreach (var pack in packs.Values)
        {
            if (pack.Usable && pack.Groups.Count == 0)
            {
                Logger.Error("Reopen box: merchant_reopen_packs row {0} has no groups - refusing the pack", pack.Id);
                pack.Usable = false;
            }
            if (pack.Usable && pack.Groups.Any(group => group.Goods.Count == 0))
            {
                Logger.Error("Reopen box: merchant_reopen_packs row {0} has an empty group - refusing the pack", pack.Id);
                pack.Usable = false;
            }
        }

        _packs = packs;
    }

    public void PostLoad()
    {
    }

    /// <summary>
    /// Two-stage draw over a pack's content: a tier by group weight, then a good inside it by
    /// good weight, both through <see cref="WeightedSelection"/>. Shipped good rows all carry
    /// weight 0, and a die with no positive weight draws its rows uniformly - the same flat
    /// reading the shipped roll has always taken; a draw that cannot be made is a loud miss
    /// (null), never a fallback to other content.
    /// </summary>
    public static MerchantReopenGood Roll(MerchantReopenPack pack, Random rng)
    {
        if (pack == null || !pack.Usable)
            return null;

        var groups = pack.Groups;
        var groupWeights = new long[groups.Count];
        for (var i = 0; i < groups.Count; i++)
            groupWeights[i] = groups[i].Weight;

        var groupIndex = WeightedSelection.PickIndex(groupWeights, rng);
        if (groupIndex < 0)
            return null;

        var group = groups[groupIndex];
        var goodWeights = new long[group.Goods.Count];
        for (var i = 0; i < group.Goods.Count; i++)
            goodWeights[i] = group.Goods[i].Weight;

        var goodIndex = WeightedSelection.PickIndex(goodWeights, rng);
        if (goodIndex < 0)
            return group.Goods.Count == 0 ? null : group.Goods[rng.Next(group.Goods.Count)];

        return group.Goods[goodIndex];
    }

    /// <summary>Draws one item from the loaded pack, or null when the pack is unusable.</summary>
    public MerchantReopenGood Roll(uint packId, Random rng = null) =>
        Roll(GetPack(packId), rng ?? Random.Shared);

    private static NLog.Logger Logger { get; } = NLog.LogManager.GetCurrentClassLogger();
}
