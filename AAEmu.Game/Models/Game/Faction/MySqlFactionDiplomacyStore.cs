using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;

using NLog;

namespace AAEmu.Game.Models.Game.Faction;

/// <summary>MySQL store for hero diplomacy. Each mutate is its own connection, the craft order board pattern.</summary>
public sealed class MySqlFactionDiplomacyStore : IFactionDiplomacyStore
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private const string Columns =
        "faction1_id, faction2_id, state, next_state, update_unix, change_unix, updater_id, updater_name, confirmer_id, confirmer_name";

    private const string Values =
        "@faction1_id, @faction2_id, @state, @next_state, @update_unix, @change_unix, @updater_id, @updater_name, @confirmer_id, @confirmer_name";

    public IReadOnlyList<FactionDiplomacyAgreement> LoadAgreements()
    {
        var rows = new List<FactionDiplomacyAgreement>();
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {Columns} FROM faction_relations";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                rows.Add(Read(reader));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Faction diplomacy: failed to load agreements");
        }

        return rows;
    }

    public bool UpsertAgreement(FactionDiplomacyAgreement agreement)
    {
        if (agreement == null || agreement.Faction1 == 0 || agreement.Faction2 == 0)
            return false;

        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO faction_relations ({Columns}) VALUES ({Values})
                ON DUPLICATE KEY UPDATE
                    state = @state, next_state = @next_state, update_unix = @update_unix, change_unix = @change_unix,
                    updater_id = @updater_id, updater_name = @updater_name, confirmer_id = @confirmer_id, confirmer_name = @confirmer_name
                """;
            Bind(command, agreement);
            command.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Faction diplomacy: failed to persist agreement {0}/{1}", agreement.Faction1, agreement.Faction2);
            return false;
        }
    }

    public bool DeleteAgreement(uint faction1, uint faction2)
    {
        var (low, high) = FactionDiplomacyRules.NormalizePair(faction1, faction2);
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM faction_relations WHERE faction1_id = @faction1_id AND faction2_id = @faction2_id";
            command.Parameters.AddWithValue("@faction1_id", low);
            command.Parameters.AddWithValue("@faction2_id", high);
            return command.ExecuteNonQuery() == 1;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Faction diplomacy: failed to delete agreement {0}/{1}", low, high);
            return false;
        }
    }

    public IReadOnlyList<FactionDiplomacyAgreement> LoadHistory(int limit)
    {
        var rows = new List<FactionDiplomacyAgreement>();
        if (limit <= 0)
            return rows;

        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            // Newest rows by insertion id, then flipped so the client receives them oldest first.
            command.CommandText = $"SELECT {Columns} FROM faction_relation_histories ORDER BY id DESC LIMIT @limit";
            command.Parameters.AddWithValue("@limit", limit);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                rows.Add(Read(reader));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Faction diplomacy: failed to load history");
        }

        rows.Reverse();
        return rows;
    }

    public bool InsertHistory(FactionDiplomacyAgreement entry)
    {
        if (entry == null || entry.Faction1 == 0 || entry.Faction2 == 0)
            return false;

        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"INSERT INTO faction_relation_histories ({Columns}) VALUES ({Values})";
            Bind(command, entry);
            return command.ExecuteNonQuery() == 1;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Faction diplomacy: failed to record history for {0}/{1}", entry.Faction1, entry.Faction2);
            return false;
        }
    }

    public IReadOnlyList<FactionDiplomacyCount> LoadCounts()
    {
        var rows = new List<FactionDiplomacyCount>();
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT character_id, other_id, `count`, updated_unix FROM faction_relation_counts";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new FactionDiplomacyCount(
                    reader.GetUInt32("character_id"),
                    reader.GetUInt32("other_id"),
                    reader.GetUInt32("count"),
                    Helpers.UnixTime(reader.GetInt64("updated_unix"))));
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Faction diplomacy: failed to load counts");
        }

        return rows;
    }

    public bool UpsertCount(FactionDiplomacyCount count)
    {
        if (count.CharacterId == 0)
            return false;

        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO faction_relation_counts (character_id, other_id, `count`, updated_unix)
                VALUES (@character_id, @other_id, @count, @updated_unix)
                ON DUPLICATE KEY UPDATE `count` = @count, updated_unix = @updated_unix
                """;
            command.Parameters.AddWithValue("@character_id", count.CharacterId);
            command.Parameters.AddWithValue("@other_id", count.OtherId);
            command.Parameters.AddWithValue("@count", count.Count);
            command.Parameters.AddWithValue("@updated_unix", Helpers.UnixTime(count.UpdatedAt));
            command.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Faction diplomacy: failed to persist count {0}/{1}", count.CharacterId, count.OtherId);
            return false;
        }
    }

    private static FactionDiplomacyAgreement Read(MySql.Data.MySqlClient.MySqlDataReader reader) => new()
    {
        Faction1 = reader.GetUInt32("faction1_id"),
        Faction2 = reader.GetUInt32("faction2_id"),
        State = (RelationState)reader.GetByte("state"),
        NextState = (RelationState)reader.GetByte("next_state"),
        UpdateTime = Helpers.UnixTime(reader.GetInt64("update_unix")),
        ChangeTime = Helpers.UnixTime(reader.GetInt64("change_unix")),
        UpdaterId = reader.GetUInt32("updater_id"),
        UpdaterName = reader.GetString("updater_name"),
        ConfirmerId = reader.GetUInt32("confirmer_id"),
        ConfirmerName = reader.GetString("confirmer_name")
    };

    private static void Bind(MySql.Data.MySqlClient.MySqlCommand command, FactionDiplomacyAgreement agreement)
    {
        var (low, high) = FactionDiplomacyRules.NormalizePair(agreement.Faction1, agreement.Faction2);
        command.Parameters.AddWithValue("@faction1_id", low);
        command.Parameters.AddWithValue("@faction2_id", high);
        command.Parameters.AddWithValue("@state", (byte)agreement.State);
        command.Parameters.AddWithValue("@next_state", (byte)agreement.NextState);
        command.Parameters.AddWithValue("@update_unix", Helpers.UnixTime(agreement.UpdateTime));
        command.Parameters.AddWithValue("@change_unix", Helpers.UnixTime(agreement.ChangeTime));
        command.Parameters.AddWithValue("@updater_id", agreement.UpdaterId);
        command.Parameters.AddWithValue("@updater_name", agreement.UpdaterName ?? string.Empty);
        command.Parameters.AddWithValue("@confirmer_id", agreement.ConfirmerId);
        command.Parameters.AddWithValue("@confirmer_name", agreement.ConfirmerName ?? string.Empty);
    }
}
