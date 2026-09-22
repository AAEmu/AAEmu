using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Achievement.Enums;
using AAEmu.Game.Models.Game.Collections;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// The shipped content behind the collections/encyclopedia domain: which achievements form the
/// collection set, which records watch which item type, and which item types the encyclopedia guides
/// catalogue.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is read from the shipped content database — the collection kind comes from the
/// achievement-kind table, membership from the categories that kind points at, watch records from the
/// record definitions, and encyclopedia entries from the item-guide tables. Nothing is derived from
/// display text and no id is invented: a row that references something the database does not hold is
/// skipped and logged as an error, and a missing collection kind row fails the load outright.
/// </para>
/// <para>
/// The encyclopedia side is loaded and validated even though the client renders it from its own local
/// copy: the guides are what make an item type a known entry, and validating them here is what lets a
/// discovery tell an encyclopedia member from an item no content row cares about.
/// </para>
/// </remarks>
[GameData]
public class CollectionGameData : Singleton<CollectionGameData>, IGameDataLoader
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>The catalog key that names the collection achievement kind.</summary>
    public const string CollectionKindName = "collection";

    /// <summary>A watch record's <c>value2</c> when it accepts the item at any grade.</summary>
    public const int AnyGrade = -1;

    /// <summary>One watch record and the item grade it asks for (<see cref="AnyGrade"/> for any).</summary>
    private readonly record struct WatchRecord(uint Id, int RequiredGrade);

    private HashSet<uint> _collectionAchievementIds = [];
    private Dictionary<(CharRecordKind Kind, uint ItemType), List<WatchRecord>> _recordsByItem = [];
    private Dictionary<int, int> _gradeOrder = [];
    private HashSet<uint> _itemEntries = [];
    private Dictionary<uint, List<uint>> _guidesByItem = [];
    private int _encyclopediaGuideCount;

    /// <summary>Whether an achievement belongs to the collection set the client's tab draws.</summary>
    public bool IsCollectionAchievement(uint achievementId) => _collectionAchievementIds.Contains(achievementId);

    public IReadOnlyCollection<uint> CollectionAchievementIds => _collectionAchievementIds;

    /// <summary>
    /// Whether an item type is a collection/encyclopedia entry: a member of an encyclopedia guide, or
    /// the item type an obtain/unpack/equip watch record targets.
    /// </summary>
    public bool IsKnownEntry(uint itemTypeId) => _itemEntries.Contains(itemTypeId);

    /// <summary>The guide ids an item type appears in, empty when it is not in any guide.</summary>
    public IReadOnlyList<uint> GetEncyclopediaEntries(uint itemTypeId) =>
        _guidesByItem.TryGetValue(itemTypeId, out var guides) ? guides : [];

    /// <summary>How many guide rows survived loading (diagnostics and tests).</summary>
    public int EncyclopediaGuideCount => _encyclopediaGuideCount;

    /// <summary>
    /// The record ids a discovery of this item type reports into, for the event it arrived from. A record
    /// that asks for a grade is only included when the item's grade meets it.
    /// </summary>
    public IReadOnlyList<uint> GetRecordsToReport(uint itemTypeId, byte itemGrade, CollectionDiscoverySource source)
    {
        var kind = source switch
        {
            CollectionDiscoverySource.Acquired => CharRecordKind.GetItemType,
            CollectionDiscoverySource.Unpacked => CharRecordKind.UnpackItemType,
            CollectionDiscoverySource.Equipped => CharRecordKind.EquipItemType,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown discovery source"),
        };

        if (!_recordsByItem.TryGetValue((kind, itemTypeId), out var records))
            return [];

        var reported = new List<uint>(records.Count);
        foreach (var record in records)
        {
            if (GradeMeets(itemGrade, record.RequiredGrade))
                reported.Add(record.Id);
        }

        return reported;
    }

    /// <summary>
    /// Whether an item of <paramref name="itemGrade"/> satisfies a record that asks for
    /// <paramref name="requiredGrade"/>: any grade, or that grade or better by <c>item_grades.grade_order</c>.
    /// </summary>
    /// <remarks>
    /// The order column is what ranks grades; the ids do not (grade 1 ranks below grade 0), so the ids are never
    /// compared directly.
    /// </remarks>
    public bool GradeMeets(int itemGrade, int requiredGrade)
    {
        if (requiredGrade < 0)
            return true;

        if (!_gradeOrder.TryGetValue(requiredGrade, out var needed))
            return false; // refused at load; unreachable for a loaded record

        if (!_gradeOrder.TryGetValue(itemGrade, out var held))
        {
            Logger.Error("CollectionGameData: item grade {0} has no item_grades row; a record asking for grade {1} is not reported",
                itemGrade, requiredGrade);
            return false;
        }

        return held >= needed;
    }

    public void Load(SqliteConnection connection)
    {
        _collectionAchievementIds = [];
        _recordsByItem = [];
        _gradeOrder = [];
        _itemEntries = [];
        _guidesByItem = [];
        _encyclopediaGuideCount = 0;

        var achievementKindIds = LoadAchievementKinds(connection, out var collectionKindId);
        var knownCategories = new HashSet<uint>();
        var collectionCategoryIds = LoadCategories(connection, achievementKindIds, collectionKindId, knownCategories);
        var subCategoryCategory = LoadSubCategories(connection, knownCategories, collectionCategoryIds,
            out var collectionSubCategories);
        LoadAchievementMembership(connection, subCategoryCategory, collectionSubCategories);
        LoadItemGrades(connection);
        LoadWatchRecords(connection);
        LoadEncyclopedia(connection);
    }

    /// <summary>The rank of every item grade, which a watch record's grade requirement is compared by.</summary>
    private void LoadItemGrades(SqliteConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, grade_order FROM item_grades";
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var id = reader.GetInt32("id");
                if (!_gradeOrder.TryAdd(id, reader.GetInt32("grade_order")))
                    Logger.Error("CollectionGameData: duplicate item_grades row {0} — keeping the first", id);
            }
        }

        if (_gradeOrder.Count == 0)
            Logger.Error("CollectionGameData: item_grades is empty; every watch record that asks for a grade is skipped");
    }

    public void PostLoad()
    {
        // Everything is resolved in Load; there is no cross-loader dependency.
    }

    /// <summary>Reads the achievement-kind ids; fails loudly when the collection kind is absent.</summary>
    private static HashSet<uint> LoadAchievementKinds(SqliteConnection connection, out uint collectionKindId)
    {
        var kinds = new HashSet<uint>();
        collectionKindId = 0;
        var found = false;

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, name FROM enum_achievement_kinds";
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var id = reader.GetUInt32("id");
                if (!kinds.Add(id))
                {
                    Logger.Error("CollectionGameData: duplicate enum_achievement_kinds row {0} — keeping the first", id);
                    continue;
                }

                if (reader.GetString("name").Equals(CollectionKindName, StringComparison.OrdinalIgnoreCase))
                {
                    collectionKindId = id;
                    found = true;
                }
            }
        }

        if (!found)
            throw new InvalidOperationException(
                $"enum_achievement_kinds has no '{CollectionKindName}' row; the collection content cannot load");

        return kinds;
    }

    /// <summary>Every category id that exists, plus the subset that carries the collection kind.</summary>
    private static HashSet<uint> LoadCategories(SqliteConnection connection, HashSet<uint> achievementKindIds,
        uint collectionKindId, HashSet<uint> knownCategories)
    {
        var collectionCategories = new HashSet<uint>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, achievement_kind_id FROM achievement_categories";
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var id = reader.GetUInt32("id");
                if (!knownCategories.Add(id))
                {
                    Logger.Error("CollectionGameData: duplicate achievement_categories row {0} — keeping the first", id);
                    continue;
                }

                var kindId = reader.GetUInt32("achievement_kind_id");
                if (kindId == collectionKindId)
                {
                    collectionCategories.Add(id);
                }
                else if (!achievementKindIds.Contains(kindId))
                {
                    Logger.Error(
                        "CollectionGameData: achievement_categories row {0} references unknown achievement kind {1} — not treating it as a collection category",
                        id, kindId);
                }
            }
        }

        if (collectionCategories.Count == 0)
            Logger.Error("CollectionGameData: no achievement category carries the '{0}' kind; nothing to collect",
                CollectionKindName);

        return collectionCategories;
    }

    /// <summary>Which sub-category each row belongs to, plus the subset under the collection categories.</summary>
    private static Dictionary<uint, uint> LoadSubCategories(SqliteConnection connection,
        HashSet<uint> knownCategories, HashSet<uint> collectionCategoryIds,
        out HashSet<uint> collectionSubCategories)
    {
        var subCategoryCategory = new Dictionary<uint, uint>();
        collectionSubCategories = [];
        var skipped = 0;

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, achievement_category_id FROM achievement_sub_categories";
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var id = reader.GetUInt32("id");
                if (subCategoryCategory.ContainsKey(id))
                {
                    Logger.Error("CollectionGameData: duplicate achievement_sub_categories row {0} — keeping the first",
                        id);
                    continue;
                }

                var categoryId = reader.GetUInt32("achievement_category_id");
                if (!knownCategories.Contains(categoryId))
                {
                    Logger.Error(
                        "CollectionGameData: achievement_sub_categories row {0} references unknown category {1} — skipping",
                        id, categoryId);
                    skipped++;
                    continue;
                }

                subCategoryCategory[id] = categoryId;
                if (collectionCategoryIds.Contains(categoryId))
                    collectionSubCategories.Add(id);
            }
        }

        if (skipped > 0)
            Logger.Error("CollectionGameData: skipped {0} sub-category rows with dangling category references", skipped);

        return subCategoryCategory;
    }

    /// <summary>The achievement ids filed under a collection sub-category.</summary>
    private void LoadAchievementMembership(SqliteConnection connection,
        Dictionary<uint, uint> subCategoryCategory, HashSet<uint> collectionSubCategories)
    {
        var seen = new HashSet<uint>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, achievement_sub_category_id FROM achievements";
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var id = reader.GetUInt32("id");
                if (!seen.Add(id))
                {
                    Logger.Error("CollectionGameData: duplicate achievements row {0} — keeping the first", id);
                    continue;
                }

                var subCategoryId = reader.GetUInt32("achievement_sub_category_id");
                if (!subCategoryCategory.ContainsKey(subCategoryId))
                    continue; // not filed under any loaded sub-category; the achievement loader owns that gap

                if (collectionSubCategories.Contains(subCategoryId))
                    _collectionAchievementIds.Add(id);
            }
        }
    }

    /// <summary>The obtain/unpack/equip watch records, keyed by the item type they name.</summary>
    private void LoadWatchRecords(SqliteConnection connection)
    {
        var getItemKind = (uint)CharRecordKind.GetItemType;
        var unpackItemKind = (uint)CharRecordKind.UnpackItemType;
        var equipItemKind = (uint)CharRecordKind.EquipItemType;

        var itemless = 0;
        var outOfRange = 0;
        var unknownGrade = 0;

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT id, kind_id, value1, value2 FROM char_records WHERE kind_id IN (@getKind, @unpackKind, @equipKind)";
            command.Parameters.AddWithValue("@getKind", getItemKind);
            command.Parameters.AddWithValue("@unpackKind", unpackItemKind);
            command.Parameters.AddWithValue("@equipKind", equipItemKind);
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var id = reader.GetUInt32("id");
                var value1 = reader.GetInt32("value1");
                if (value1 <= 0)
                {
                    // A watch row with no item target counts something other than one item type; nothing
                    // discovers into it.
                    itemless++;
                    continue;
                }

                if ((ulong)value1 > uint.MaxValue)
                {
                    Logger.Error("CollectionGameData: char_records row {0} has out-of-range item target {1} — skipping",
                        id, value1);
                    outOfRange++;
                    continue;
                }

                // value2 is the item grade the record asks for, or AnyGrade.
                var requiredGrade = reader.IsDBNull("value2") ? AnyGrade : reader.GetInt32("value2");
                if (requiredGrade < 0)
                {
                    requiredGrade = AnyGrade;
                }
                else if (!_gradeOrder.ContainsKey(requiredGrade))
                {
                    Logger.Error("CollectionGameData: char_records row {0} asks for item grade {1}, which item_grades does not hold — skipping",
                        id, requiredGrade);
                    unknownGrade++;
                    continue;
                }

                var key = ((CharRecordKind)reader.GetUInt32("kind_id"), (uint)value1);
                if (!_recordsByItem.TryGetValue(key, out var records))
                    _recordsByItem[key] = records = [];

                records.Add(new WatchRecord(id, requiredGrade));
                _itemEntries.Add((uint)value1);
            }
        }

        if (itemless > 0)
            Logger.Trace("CollectionGameData: {0} item watch records carry no item target", itemless);
        if (outOfRange > 0)
            Logger.Error("CollectionGameData: skipped {0} item watch records with out-of-range targets", outOfRange);
        if (unknownGrade > 0)
            Logger.Error("CollectionGameData: skipped {0} item watch records that ask for an unknown item grade", unknownGrade);
    }

    /// <summary>The encyclopedia guides and their item members, with dangling references skipped loudly.</summary>
    private void LoadEncyclopedia(SqliteConnection connection)
    {
        var items = LoadIdSet(connection, "SELECT id FROM items", "items");

        var impls = LoadIdSet(connection, "SELECT id FROM item_guide_impls", "item_guide_impls");

        var aCategories = new HashSet<uint>();
        var skippedACategories = 0;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, item_guide_impl_id FROM item_guide_a_categories";
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var id = reader.GetUInt32("id");
                if (!aCategories.Add(id))
                {
                    Logger.Error("CollectionGameData: duplicate item_guide_a_categories row {0} — keeping the first",
                        id);
                    continue;
                }

                if (!impls.Contains(reader.GetUInt32("item_guide_impl_id")))
                {
                    Logger.Error(
                        "CollectionGameData: item_guide_a_categories row {0} references an unknown impl — skipping", id);
                    aCategories.Remove(id);
                    skippedACategories++;
                }
            }
        }

        var bCategories = new HashSet<uint>();
        var skippedBCategories = 0;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, item_guide_a_category_id FROM item_guide_b_categories";
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var id = reader.GetUInt32("id");
                if (!bCategories.Add(id))
                {
                    Logger.Error("CollectionGameData: duplicate item_guide_b_categories row {0} — keeping the first",
                        id);
                    continue;
                }

                if (!aCategories.Contains(reader.GetUInt32("item_guide_a_category_id")))
                {
                    Logger.Error(
                        "CollectionGameData: item_guide_b_categories row {0} references an unknown category — skipping",
                        id);
                    bCategories.Remove(id);
                    skippedBCategories++;
                }
            }
        }

        var guides = new HashSet<uint>();
        var skippedGuides = 0;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, item_guide_impl_id FROM item_guides";
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var id = reader.GetUInt32("id");
                if (!guides.Add(id))
                {
                    Logger.Error("CollectionGameData: duplicate item_guides row {0} — keeping the first", id);
                    continue;
                }

                if (!impls.Contains(reader.GetUInt32("item_guide_impl_id")))
                {
                    Logger.Error("CollectionGameData: item_guides row {0} references an unknown impl — skipping", id);
                    guides.Remove(id);
                    skippedGuides++;
                }
            }
        }

        var skippedElems = 0;
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT item_id, item_guide_id, item_guide_a_category_id, item_guide_b_category_id FROM item_guide_elems";
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var itemId = reader.GetUInt32("item_id");
                var guideId = reader.GetUInt32("item_guide_id");
                var aId = reader.GetUInt32("item_guide_a_category_id");
                var bId = reader.IsDBNull("item_guide_b_category_id")
                    ? 0u
                    : reader.GetUInt32("item_guide_b_category_id");

                var reason = !guides.Contains(guideId) ? "unknown guide"
                    : !items.Contains(itemId) ? "unknown item"
                    : aId != 0 && !aCategories.Contains(aId) ? "unknown category"
                    : bId != 0 && !bCategories.Contains(bId) ? "unknown subcategory"
                    : null;

                if (reason != null)
                {
                    Logger.Error(
                        "CollectionGameData: item_guide_elems row for item {0} in guide {1} has an {2} — skipping",
                        itemId, guideId, reason);
                    skippedElems++;
                    continue;
                }

                if (!_guidesByItem.TryGetValue(itemId, out var guidesForItem))
                    _guidesByItem[itemId] = guidesForItem = [];

                if (!guidesForItem.Contains(guideId))
                    guidesForItem.Add(guideId);

                _itemEntries.Add(itemId);
            }
        }

        _encyclopediaGuideCount = guides.Count;

        var skipped = skippedACategories + skippedBCategories + skippedGuides + skippedElems;
        if (skipped > 0)
            Logger.Error(
                "CollectionGameData: skipped {0} encyclopedia rows with dangling references ({1} categories, {2} subcategories, {3} guides, {4} members)",
                skipped, skippedACategories, skippedBCategories, skippedGuides, skippedElems);
    }

    /// <summary>Loads a single id column into a set, keeping the first row of a duplicate id.</summary>
    private static HashSet<uint> LoadIdSet(SqliteConnection connection, string sql, string tableName)
    {
        var ids = new HashSet<uint>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = sql;
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var id = reader.GetUInt32("id");
                if (!ids.Add(id))
                    Logger.Error("CollectionGameData: duplicate {0} row {1} — keeping the first", tableName, id);
            }
        }

        return ids;
    }
}
