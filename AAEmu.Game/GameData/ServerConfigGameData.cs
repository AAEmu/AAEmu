using System.Globalization;

using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.CrossServer;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// Game servers named by the content database table <c>server_configs</c>.
/// A departure with no requested target uses the single other row. Zero peers or more than one
/// is not a destination, and the caller refuses instead of guessing.
/// </summary>
[GameData]
public class ServerConfigGameData : Singleton<ServerConfigGameData>, IGameDataLoader, ICrossServerDirectory
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private List<uint> _serverIds = [];

    public void Load(SqliteConnection connection)
    {
        var ids = new List<uint>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM server_configs ORDER BY id";
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
            ids.Add(reader.GetUInt32("id"));

        _serverIds = ids;
        if (ids.Count == 0)
            Logger.Error("server_configs is empty; a cross-server departure with no named target will be refused");
    }

    public void PostLoad()
    {
    }

    public string ResolvePeerKey(byte ownServerId)
    {
        var peers = _serverIds.Where(id => id != ownServerId).ToList();
        if (peers.Count == 1)
            return peers[0].ToString(CultureInfo.InvariantCulture);

        Logger.Error(
            "Cross-server departure target unresolved: server_configs named {0} peer(s) for server id {1}; exactly one is required.",
            peers.Count, ownServerId);
        return null;
    }
}
