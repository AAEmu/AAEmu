using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

/// <summary>
/// One combat resource — the combo-point style pools an ability accumulates (광란, 착취, 근성 and the rest).
/// </summary>
public class CombatResource
{
    public int Id { get; init; }
    public string Name { get; init; }

    /// <summary>Cap on the accumulated amount; 5 for most, 5000 for 근성.</summary>
    public int Max { get; init; }

    /// <summary>Amount a unit starts with, non-zero only for 죽음의 낙인.</summary>
    public int DefaultPoint { get; init; }

    /// <summary>enum_combat_resource_send_types: 1 Self, 2 Broadcast.</summary>
    public int SendTypeId { get; init; }
}

/// <summary>
/// The combat resource definitions from <c>combat_resources</c>. Needed by CombatResourceEffect to clamp a
/// grant against the resource's own ceiling rather than letting a pool run past what the UI can show.
/// </summary>
[GameData]
public class CombatResourceGameData : Singleton<CombatResourceGameData>, IGameDataLoader
{
    private Dictionary<int, CombatResource> _resources;

    public void Load(SqliteConnection connection)
    {
        _resources = [];

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM combat_resources";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var resource = new CombatResource
            {
                Id = reader.GetInt32("id"),
                Name = reader.GetString("name"),
                Max = reader.GetInt32("max"),
                DefaultPoint = reader.GetInt32("default_point"),
                // Column is spelled "resouece_send_type_id" in the shipped schema.
                SendTypeId = reader.GetInt32("resouece_send_type_id")
            };

            _resources[resource.Id] = resource;
        }
    }

    public void PostLoad()
    {
    }

    public CombatResource Get(int id) => _resources?.GetValueOrDefault(id);

    /// <summary>Ceiling for a resource, 0 when the id is unknown.</summary>
    public int GetMax(int id) => Get(id)?.Max ?? 0;
}
