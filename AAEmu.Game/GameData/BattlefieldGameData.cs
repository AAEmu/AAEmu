using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.InstantGame;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class BattlefieldGameData : Singleton<BattlefieldGameData>, IGameDataLoader
{
    private Dictionary<uint, Battlefield> _battlefields;

    public Battlefield GetBattlefield(uint id)
    {
        return _battlefields.GetValueOrDefault(id);
    }

    public void Load(SqliteConnection connection, SqliteConnection connection2)
    {
        _battlefields = new Dictionary<uint, Battlefield>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM battle_fields";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var bf = new Battlefield
                    {
                        Id = reader.GetUInt32("id"),
                        ZoneKey = reader.GetUInt32("zone_key")
                    };

                    _battlefields.Add(bf.Id, bf);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM game_rule_sets";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var gsr = new GameRuleSet
                    {
                        Id = reader.GetUInt32("id"),
                        BattlefieldId = reader.GetUInt32("battle_field_id"),
                        CorpsSize = reader.GetUInt32("corps_size"),
                        Corps1FactionId = reader.GetUInt32("corps1_id"),
                        Corps2FactionId = reader.GetUInt32("corps2_id"),
                        TimeOpening = reader.GetInt32("time_opening"),
                        TimeEnding = reader.GetInt32("time_ending"),
                        TimePlaying = reader.GetInt32("time_playing"),
                        VictoryScore = reader.GetInt32("victory_score")
                    };

                    if (_battlefields.TryGetValue(gsr.BattlefieldId, out var battlefield))
                        battlefield.RuleSet = gsr;
                }
            }
        }

        var pathFile = Path.Combine(FileManager.AppPath, "Data", "battlefields.json");
        var contents = FileManager.GetFileContents(pathFile);
        if (string.IsNullOrWhiteSpace(contents))
            throw new IOException($"File {pathFile} doesn't exists or is empty.");

        if (JsonHelper.TryDeserializeObject(contents, out List<BattlefieldSpawns> bfSpawns, out _))
        {
            foreach (var bfSpawn in bfSpawns)
            {
                if (!_battlefields.TryGetValue(bfSpawn.BattlefieldId, out var battlefield))
                    return;

                battlefield.Spawns = bfSpawn;
            }
        }
    }

    public void PostLoad()
    {
    }
}
