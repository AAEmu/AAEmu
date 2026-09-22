using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// Which item templates can carry a crest: the <c>ucc_applicables</c> rows of the applicable kind named
/// <see cref="ItemKindName"/>. A row pointing at no item is skipped loudly, and without the kind row no item
/// can take a crest at all.
/// </summary>
[GameData]
public class UccGameData : Singleton<UccGameData>, IGameDataLoader
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>The catalog key of the applicable kind whose rows name item templates.</summary>
    public const string ItemKindName = "item";

    private HashSet<uint> _crestItems = [];

    /// <summary>Whether an item template can carry a crest.</summary>
    public bool TakesCrest(uint itemTemplateId) => _crestItems.Contains(itemTemplateId);

    /// <summary>How many item templates can carry a crest (diagnostics and tests).</summary>
    public int CrestItemCount => _crestItems.Count;

    public void Load(SqliteConnection connection)
    {
        _crestItems = [];

        uint? itemKindId = null;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, name FROM enum_ucc_applicable_kinds";
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                if (reader.GetString("name").Equals(ItemKindName, StringComparison.OrdinalIgnoreCase))
                    itemKindId = reader.GetUInt32("id");
            }
        }

        if (itemKindId == null)
        {
            Logger.Error("UccGameData: enum_ucc_applicable_kinds has no '{0}' row; no item can take a crest", ItemKindName);
            return;
        }

        var dangling = 0;
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT a.actual_id, i.id AS item_id FROM ucc_applicables a " +
                "LEFT JOIN items i ON i.id = a.actual_id WHERE a.kind_id = @kind";
            command.Parameters.AddWithValue("@kind", itemKindId.Value);
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                if (reader.IsDBNull("item_id"))
                {
                    dangling++;
                    continue;
                }

                _crestItems.Add(reader.GetUInt32("actual_id"));
            }
        }

        if (dangling > 0)
            Logger.Error("UccGameData: skipped {0} ucc_applicables item rows that name no item", dangling);
        if (_crestItems.Count == 0)
            Logger.Error("UccGameData: no ucc_applicables item rows loaded; no item can take a crest");
    }

    public void PostLoad()
    {
    }
}
