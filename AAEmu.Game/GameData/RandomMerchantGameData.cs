using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Merchant;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// The random merchant catalog: <c>merchant_random_packs</c> (7 rows), <c>merchant_random_groups</c>
/// (414) and <c>merchant_random_goods</c> (818), assembled by
/// <see cref="RandomMerchantContentBuilder"/> so every validation rule is shared with tests.
/// Grades resolve in <see cref="PostLoad"/> the same way vendor stock resolves them
/// (NpcManager's merchant load: items.fixed_grade when set, else gradable row grade, else 0).
/// </summary>
[GameData]
public class RandomMerchantGameData : Singleton<RandomMerchantGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private IReadOnlyDictionary<uint, RandomMerchantPack> _packs = new Dictionary<uint, RandomMerchantPack>();

    public IReadOnlyDictionary<uint, RandomMerchantPack> Packs => _packs;

    /// <summary>One pack by <c>merchant_random_packs.id</c>, or null when no such row was loaded.</summary>
    public RandomMerchantPack GetPack(uint packId) =>
        _packs.TryGetValue(packId, out var pack) ? pack : null;

    public void Load(SqliteConnection connection)
    {
        var packRows = new List<RandomMerchantPackRow>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT id, kind_id, item_point_id, sale_cnt, refresh_use, refresh_multiply_use, " +
                "refresh_free_cnt, refresh_charge_cnt, refresh_currency_id, refresh_point, refresh_item_id " +
                "FROM merchant_random_packs ORDER BY id";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                packRows.Add(new RandomMerchantPackRow
                {
                    Id = reader.GetUInt32("id"),
                    KindId = reader.GetByte("kind_id"),
                    ItemPointId = reader.GetUInt32("item_point_id"),
                    SaleCnt = reader.GetInt32("sale_cnt"),
                    RefreshUse = reader.GetString("refresh_use"),
                    RefreshMultiplyUse = reader.GetString("refresh_multiply_use"),
                    RefreshFreeCnt = reader.GetInt32("refresh_free_cnt"),
                    RefreshChargeCnt = reader.GetInt32("refresh_charge_cnt"),
                    RefreshCurrencyId = reader.GetUInt32("refresh_currency_id"),
                    RefreshPoint = reader.GetInt32("refresh_point"),
                    RefreshItemId = reader.GetUInt32("refresh_item_id")
                });
            }
        }

        var groupRows = new List<RandomMerchantGroupRow>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT id, merchant_random_pack_id, group_no, weight FROM merchant_random_groups ORDER BY id";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                groupRows.Add(new RandomMerchantGroupRow
                {
                    Id = reader.GetUInt32("id"),
                    PackId = reader.GetUInt32("merchant_random_pack_id"),
                    GroupNo = reader.GetInt32("group_no"),
                    Weight = reader.GetInt64("weight")
                });
            }
        }

        var goodRows = new List<RandomMerchantGoodRow>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT id, merchant_random_group_id, item_id, grade_id, cost, weight FROM merchant_random_goods ORDER BY id";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                goodRows.Add(new RandomMerchantGoodRow
                {
                    Id = reader.GetUInt32("id"),
                    GroupId = reader.GetUInt32("merchant_random_group_id"),
                    ItemId = reader.GetUInt32("item_id"),
                    GradeId = reader.GetByte("grade_id"),
                    Cost = reader.GetInt32("cost"),
                    Weight = reader.GetInt64("weight")
                });
            }
        }

        _packs = RandomMerchantContentBuilder.Build(packRows, groupRows, goodRows);
        Logger.Info(
            "Loaded {0} random merchant packs ({1} usable), {2} group rows, {3} good rows",
            _packs.Count, _packs.Values.Count(pack => pack.Usable), groupRows.Count, goodRows.Count);
    }

    public void PostLoad()
    {
        // ItemManager completes its Load() before GameDataManager runs loaders' PostLoad, so the
        // templates are there. A template that does not resolve keeps its content grade_id - the
        // same fallback NpcManager's merchant load takes for an unknown item.
        var itemManager = ItemManager.Instance;
        foreach (var pack in _packs.Values)
        {
            foreach (var group in pack.EligibleGroups)
            {
                foreach (var good in group.Goods)
                {
                    if (itemManager.GetTemplate(good.ItemId) is not { } template)
                        continue;
                    good.Grade = template.FixedGrade >= 0
                        ? (byte)template.FixedGrade
                        : template.Gradable
                            ? good.Grade
                            : (byte)0;
                }
            }
        }
    }
}
