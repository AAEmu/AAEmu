using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

/// <summary>One item a reopenable merchant pack can yield.</summary>
public class MerchantReopenGood
{
    public uint ItemId { get; init; }
    public byte GradeId { get; init; }
    public int Count { get; init; }
    public int Weight { get; init; }
}

/// <summary>A rank tier inside a pack, weighted against the pack's other tiers.</summary>
public class MerchantReopenGroup
{
    public uint Id { get; init; }
    public int Weight { get; init; }
    public List<MerchantReopenGood> Goods { get; } = [];
}

/// <summary>
/// The reopenable merchant packs ("재개봉 랜박 상자"), a two stage weighted draw: pick a rank tier from
/// <c>merchant_reopen_groups</c> by weight, then an item from <c>merchant_reopen_goods</c> by weight.
/// </summary>
[GameData]
public class MerchantReopenPackGameData : Singleton<MerchantReopenPackGameData>, IGameDataLoader
{
    private Dictionary<uint, List<MerchantReopenGroup>> _groupsByPack;

    public void Load(SqliteConnection connection)
    {
        _groupsByPack = [];
        var groupsById = new Dictionary<uint, MerchantReopenGroup>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM merchant_reopen_groups";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var group = new MerchantReopenGroup
                {
                    Id = reader.GetUInt32("id"),
                    Weight = reader.GetInt32("weight")
                };

                var packId = reader.GetUInt32("merchant_reopen_pack_id");
                if (!_groupsByPack.TryGetValue(packId, out var packGroups))
                {
                    packGroups = [];
                    _groupsByPack.Add(packId, packGroups);
                }

                packGroups.Add(group);
                groupsById.Add(group.Id, group);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM merchant_reopen_goods";
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
                    ItemId = reader.GetUInt32("item_id"),
                    GradeId = (byte)reader.GetInt32("grade_id"),
                    Count = reader.GetInt32("count"),
                    Weight = reader.GetInt32("weight")
                });
            }
        }
    }

    public void PostLoad()
    {
    }

    /// <summary>Draws one item from a pack, or null when the pack has no usable entries.</summary>
    public MerchantReopenGood Roll(uint packId)
    {
        if (_groupsByPack == null || !_groupsByPack.TryGetValue(packId, out var groups) || groups.Count == 0)
            return null;

        var group = PickWeighted(groups, g => g.Weight);
        if (group == null || group.Goods.Count == 0)
            return null;

        return PickWeighted(group.Goods, g => g.Weight);
    }

    /// <summary>
    /// Weighted pick. Weights run to 5000000 in this data and a pack has at most a handful of entries, so a
    /// long accumulator is enough to keep the running total from overflowing.
    /// </summary>
    private static T PickWeighted<T>(List<T> entries, Func<T, int> weightOf) where T : class
    {
        long total = 0;
        foreach (var entry in entries)
            total += Math.Max(0, weightOf(entry));

        // Every weight zero: fall back to a flat pick rather than returning nothing.
        if (total <= 0)
            return entries[Random.Shared.Next(entries.Count)];

        var roll = Random.Shared.NextInt64(total);
        long running = 0;
        foreach (var entry in entries)
        {
            running += Math.Max(0, weightOf(entry));
            if (roll < running)
                return entry;
        }

        return entries[^1];
    }
}
