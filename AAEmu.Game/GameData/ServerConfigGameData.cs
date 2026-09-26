using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.CrossServer;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// Rows of <c>server_configs</c>. Each row is a group of logic ids
/// (<c>logic_ids</c>), not a destination server. A departure with no explicit target
/// therefore has no peer to resolve here.
/// </summary>
[GameData]
public class ServerConfigGameData : Singleton<ServerConfigGameData>, IGameDataLoader, ICrossServerDirectory
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private int _groupCount;

    public void Load(SqliteConnection connection)
    {
        var count = 0;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, logic_ids, server_name FROM server_configs ORDER BY id";
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            count++;
            if (reader.IsDBNull("logic_ids") || string.IsNullOrWhiteSpace(reader.GetString("logic_ids")))
                Logger.Error("server_configs id {0} has no logic_ids", reader.GetUInt32("id"));
        }

        _groupCount = count;
        if (count == 0)
            Logger.Error("server_configs is empty; a cross-server departure with no named target will be refused");
        else
            Logger.Info("server_configs loaded {0} group(s); none of them names a departure destination", count);
    }

    public void PostLoad()
    {
    }

    /// <summary>
    /// Always null. <c>server_configs</c> does not name a peer server, so an unnamed
    /// departure is refused. An explicit target key is resolved by the caller before this.
    /// </summary>
    public string ResolvePeerKey(byte ownServerId)
    {
        Logger.Error(
            "Cross-server departure target unresolved: server_configs has {0} group(s) and names no peer for server id {1}.",
            _groupCount, ownServerId);
        return null;
    }
}
