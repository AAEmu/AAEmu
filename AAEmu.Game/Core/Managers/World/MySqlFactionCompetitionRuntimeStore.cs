using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// MySQL-backed faction-competition score storage. A faction that has never scored has no row, so
/// the table holds only scores that were actually awarded.
/// </summary>
public sealed class MySqlFactionCompetitionRuntimeStore : IFactionCompetitionRuntimeStore
{
    public IReadOnlyDictionary<(uint, uint), long> LoadAll()
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT faction_competition_id,faction_id,score FROM faction_competition_runtime_states";
        using var reader = command.ExecuteReader();
        var scores = new Dictionary<(uint, uint), long>();
        while (reader.Read())
            scores[(reader.GetUInt32(0), reader.GetUInt32(1))] = reader.GetInt64(2);
        return scores;
    }

    public void Save(uint competitionId, uint factionId, long score)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO faction_competition_runtime_states (faction_competition_id,faction_id,score)
            VALUES (@competition,@faction,@score)
            ON DUPLICATE KEY UPDATE score=VALUES(score)
            """;
        command.Parameters.AddWithValue("@competition", competitionId);
        command.Parameters.AddWithValue("@faction", factionId);
        command.Parameters.AddWithValue("@score", score);
        command.ExecuteNonQuery();
    }

    public void Delete(uint competitionId, uint factionId)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM faction_competition_runtime_states WHERE faction_competition_id=@competition AND faction_id=@faction";
        command.Parameters.AddWithValue("@competition", competitionId);
        command.Parameters.AddWithValue("@faction", factionId);
        command.ExecuteNonQuery();
    }
}
