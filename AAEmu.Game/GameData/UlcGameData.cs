using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

/// <summary>
/// Valid 10.0.2.13 ULC identifiers. The native UnitReq evaluator rejects an unknown ULC id before
/// checking the account attribute; the content row's <c>active</c> flag is not that validity test.
/// </summary>
[GameData]
public class UlcGameData : Singleton<UlcGameData>, IGameDataLoader
{
    private HashSet<uint> _ids = [];

    public void Load(SqliteConnection connection)
    {
        _ids = [];

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM ulcs";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
            _ids.Add(reader.GetUInt32("id"));
    }

    public void PostLoad()
    {
    }

    public bool Exists(uint id) => _ids.Contains(id);
}
