using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

/// <summary>
/// Loads the 10.0.2.13 regrade data family:
/// <list type="bullet">
/// <item><c>item_enchant_ratio_groups</c> — named ratio groups, each optionally bound to an item impl.</item>
/// <item><c>item_enchant_ratios</c> — odds/cost per (group, current grade).</item>
/// <item><c>item_enchant_ratio_items</c> — per-item overrides of the default group.</item>
/// </list>
/// Group resolution for an item: per-item override → impl-bound group → group 1 (default).
/// </summary>
[GameData]
public class ItemEnchantRatioGameData : Singleton<ItemEnchantRatioGameData>, IGameDataLoader
{
    private Dictionary<uint, ItemEnchantRatio[]> _ratiosByGroup;
    private Dictionary<uint, uint> _groupForImpl;
    private Dictionary<uint, uint> _groupForItem;

    public void Load(SqliteConnection connection)
    {
        _ratiosByGroup = [];
        _groupForImpl = [];
        _groupForItem = [];

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_enchant_ratio_groups";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var groupId = reader.GetUInt32("id");
                    var implId = reader.GetUInt32("item_impl_id", 0);
                    if (implId != 0 && Enum.IsDefined(typeof(ItemImplEnum), (int)implId))
                        _groupForImpl.TryAdd(implId, groupId);
                }
            }
        }

        var ratiosByGroupRows = new Dictionary<uint, Dictionary<byte, ItemEnchantRatio>>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_enchant_ratios";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var row = new ItemEnchantRatio
                    {
                        GroupId = reader.GetUInt32("item_enchant_ratio_group_id"),
                        Grade = reader.GetByte("grade"),
                        SuccessRatio = reader.GetInt32("grade_enchant_success_ratio", 0),
                        GreatSuccessRatio = reader.GetInt32("grade_enchant_great_success_ratio", 0),
                        BreakRatio = reader.GetInt32("grade_enchant_break_ratio", 0),
                        DowngradeRatio = reader.GetInt32("grade_enchant_downgrade_ratio", 0),
                        DisableRatio = reader.GetInt32("grade_enchant_disable_ratio", 0),
                        Cost = reader.GetInt32("grade_enchant_cost", 0),
                        DowngradeMin = reader.GetInt32("grade_enchant_downgrade_min", -1),
                        DowngradeMax = reader.GetInt32("grade_enchant_downgrade_max", -1),
                        CurrencyId = reader.GetUInt32("currency_id", 0)
                    };

                    if (!ratiosByGroupRows.TryGetValue(row.GroupId, out var byGrade))
                    {
                        byGrade = [];
                        ratiosByGroupRows.Add(row.GroupId, byGrade);
                    }

                    // Later duplicate rows for the same grade win, matching the dedicate loader.
                    byGrade[row.Grade] = row;
                }
            }
        }

        foreach (var (groupId, byGrade) in ratiosByGroupRows)
        {
            var max = byGrade.Keys.DefaultIfEmpty((byte)0).Max();
            var flat = new ItemEnchantRatio[max + 1];
            foreach (var (grade, row) in byGrade)
                flat[grade] = row;
            _ratiosByGroup.Add(groupId, flat);
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_enchant_ratio_items";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var itemId = reader.GetUInt32("item_id");
                    var groupId = reader.GetUInt32("item_enchant_ratio_group_id");
                    _groupForItem[itemId] = groupId;
                }
            }
        }
    }

    public void PostLoad()
    {
        // ItemManager (a Manager, loaded before GameData) has already computed the grade tables.
        ItemGradeEnchantRules.MaxGradeOrder = ItemManager.MaxGradeValue;
    }

    /// <summary>Ratio row for regrading an item that is currently at <paramref name="grade"/>, or null.</summary>
    public ItemEnchantRatio GetRatio(uint itemId, ItemTemplate template, byte grade)
    {
        if (_ratiosByGroup.Count == 0 || grade > byte.MaxValue)
            return null;

        var groupId = ResolveGroupId(itemId, template);
        if (!_ratiosByGroup.TryGetValue(groupId, out var byGrade))
            return null;

        return grade < byGrade.Length ? byGrade[grade] : null;
    }

    /// <summary>Currency/multiplier input of the grade_enchant_cost formula for this attempt.</summary>
    public int GetCostInput(uint itemId, ItemTemplate template, byte grade)
    {
        return GetRatio(itemId, template, grade)?.Cost ?? 0;
    }

    private uint ResolveGroupId(uint itemId, ItemTemplate template)
    {
        if (_groupForItem.TryGetValue(itemId, out var byItem))
            return byItem;

        if (_groupForImpl.TryGetValue((uint)template.ImplId, out var byImpl))
            return byImpl;

        // Group 1 is the shipped default group ("default").
        const uint DefaultGroupId = 1;
        _ratiosByGroup.TryGetValue(DefaultGroupId, out _);
        return DefaultGroupId;
    }
}
