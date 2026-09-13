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
    // Current native IsExpeditionContents compares this content field with 5; configured
    // instance_ui_kinds row 5 is the Expedition category.
    public const uint ExpeditionInstanceUiKindId = 5;

    private Dictionary<uint, Battlefield> _battlefields;

    public Battlefield GetBattlefield(uint id)
    {
        return _battlefields.GetValueOrDefault(id);
    }

    public void Load(SqliteConnection connection)
    {
        _battlefields = new Dictionary<uint, Battlefield>();

        // 10.0.2.13: the battle_field<->game_rule_set link is reversed. In 1.2 the rule set
        // carried a battle_field_id; in 10.0.2.13 each battle_fields row carries a
        // game_rule_set_id. Build a reverse map (ruleSetId -> battlefieldId) so we can still
        // attach rule sets to their battlefields below.
        var ruleSetToBattlefield = new Dictionary<uint, uint>();

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

                    // game_rule_set_id (0 = none) links this battlefield to its rule set.
                    var ruleSetId = reader.GetUInt32("game_rule_set_id", 0u);
                    if (ruleSetId != 0u)
                        ruleSetToBattlefield[ruleSetId] = bf.Id;
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id,target_id,instance_ui_kind_id,squad_not_use FROM instances WHERE target_type='BattleField'";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var battlefieldId = reader.GetUInt32("target_id");
                if (!_battlefields.TryGetValue(battlefieldId, out var battlefield))
                    continue;
                battlefield.InstanceId = reader.GetUInt32("id");
                battlefield.InstanceUiKindId = reader.GetUInt32("instance_ui_kind_id");
                battlefield.SquadNotUse = reader.GetBoolean("squad_not_use", true);
            }
        }

        var rankDetailIds = LoadInstanceRankDetailIds(connection);
        foreach (var battlefield in _battlefields.Values)
            if (rankDetailIds.TryGetValue(battlefield.InstanceId, out var rankDetailId))
                battlefield.InstanceRankDetailId = rankDetailId;

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM game_rule_sets";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    // 10.0.2.13: battle_field_id, corps_size, corps1_id, corps2_id and
                    // time_opening were removed from game_rule_sets; only the columns below
                    // remain. Removed model fields are left at their defaults.
                    var ruleSetId = reader.GetUInt32("id");
                    ruleSetToBattlefield.TryGetValue(ruleSetId, out var battlefieldId);

                    var gsr = new GameRuleSet
                    {
                        Id = ruleSetId,
                        BattlefieldId = battlefieldId,
                        TimeEnding = reader.GetInt32("time_ending"),
                        TimePlaying = reader.GetInt32("time_playing"),
                        TimeReady = reader.GetInt32("time_ready"),
                        VictoryScore = reader.GetInt32("victory_score")
                    };

                    if (battlefieldId != 0u && _battlefields.TryGetValue(battlefieldId, out var battlefield))
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

    /// <summary>
    /// Maps the client history/rating <c>type</c> to its authored instance. Current native loads
    /// these three columns into InstanceRankDetailDesc; the client independently resolves the
    /// history's following type through instances.id for its display name.
    /// </summary>
    internal static IReadOnlyDictionary<uint, uint> LoadInstanceRankDetailIds(SqliteConnection connection)
    {
        var result = new Dictionary<uint, uint>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,instance_id,rating_only FROM instance_rank_details";
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var id = reader.GetUInt32("id");
            var instanceId = reader.GetUInt32("instance_id");
            if (id == 0 || instanceId == 0 || !result.TryAdd(instanceId, id))
                throw new InvalidDataException($"Ambiguous instance rank detail mapping for instance {instanceId}.");
        }
        return result;
    }
}
