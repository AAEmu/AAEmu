using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.Residents;

/// <summary>
/// MySQL store for resident settlement. Every read and write is wrapped: a missing
/// <c>SQL/updates/2026-09-23_aaemu_game_resident_state.sql</c> migration must not be able to block
/// login or a settlement — it degrades loudly (Error naming the file) and the in-memory state stays
/// authoritative for the session.
/// </summary>
public sealed class MySqlResidentStateStore : IResidentStateStore
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private const string MigrationFile = "SQL/updates/2026-09-23_aaemu_game_resident_state.sql";

    private const string SelectCharacters = """
        SELECT owner, zone_group_id, service_point, charge, hunting_charge, updated_at
        FROM character_resident_state
        """;

    private const string UpsertCharacterSql = """
        INSERT INTO character_resident_state (owner, zone_group_id, service_point, charge, hunting_charge, updated_at)
        VALUES (@owner, @zone_group_id, @service_point, @charge, @hunting_charge, @updated_at)
        ON DUPLICATE KEY UPDATE service_point = VALUES(service_point), charge = VALUES(charge), hunting_charge = VALUES(hunting_charge), updated_at = VALUES(updated_at)
        """;

    public IReadOnlyList<CharacterResidentState> LoadAll()
    {
        try
        {
            var rows = new List<CharacterResidentState>();
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = SelectCharacters;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new CharacterResidentState(
                    reader.GetUInt32("owner"),
                    (ushort)reader.GetUInt32("zone_group_id"),
                    reader.GetUInt32("service_point"),
                    reader.GetUInt64("charge"),
                    reader.GetUInt64("hunting_charge"),
                    ServerCalendar.AsUtc(reader.GetDateTime("updated_at"))));
            }
            return rows;
        }
        catch (MySqlException ex)
        {
            Logger.Error(ex, "Resident state: could not load character_resident_state; degrade to empty (see {0})", MigrationFile);
            return [];
        }
    }

    public bool UpsertCharacterState(CharacterResidentState row)
    {
        if (row == null)
            return false;
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = UpsertCharacterSql;
            command.Parameters.AddWithValue("@owner", row.OwnerId);
            command.Parameters.AddWithValue("@zone_group_id", row.ZoneGroupId);
            command.Parameters.AddWithValue("@service_point", row.ServicePoint);
            command.Parameters.AddWithValue("@charge", row.Charge);
            command.Parameters.AddWithValue("@hunting_charge", row.HuntingCharge);
            command.Parameters.AddWithValue("@updated_at", row.UpdatedAt);
            return command.ExecuteNonQuery() > 0;
        }
        catch (MySqlException ex)
        {
            Logger.Error(ex, "Resident state: could not persist character {0} zone group {1} (see {2})",
                row.OwnerId, row.ZoneGroupId, MigrationFile);
            return false;
        }
    }
}
