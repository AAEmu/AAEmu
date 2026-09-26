using System.Globalization;

using AAEmu.Commons.Utils.DB;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.CrossServer;

/// <summary>
/// MySQL side of the cross-server transfer journal (<c>character_transfer_journals</c> plus the
/// <c>characters</c>/<c>items</c> rows they witness). Every mutator runs in one transaction with
/// the journal row locked first, so a departure, a settle, a rollback and a re-entry are each
/// all-or-nothing and each exactly once — the primary key on <c>character_id</c> is the
/// double-departure guard.
/// </summary>
public sealed class MySqlCrossServerTransferStore : ICrossServerTransferStore
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public CrossServerCharacterSnapshot CaptureSnapshot(ulong characterId)
    {
        using var connection = MySQL.CreateConnection();

        long money, money2, aaPoint;
        DateTime transferRequestTime;
        using (var command = new MySqlCommand(
                   "SELECT `money`, `money2`, `aa_point`, `transfer_request_time` FROM `characters` WHERE `id` = @id AND `deleted` = 0",
                   connection))
        {
            command.Parameters.AddWithValue("@id", characterId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return null;

            money = reader.GetInt64(0);
            money2 = reader.GetInt64(1);
            aaPoint = reader.GetInt64(2);
            transferRequestTime = reader.IsDBNull(3) ? DateTime.MinValue : reader.GetDateTime(3);
        }

        long itemCount, itemFingerprint;
        using (var command = new MySqlCommand(
                   "SELECT COUNT(*), COALESCE(SUM(`id`), 0) FROM `items` WHERE `owner` = @id",
                   connection))
        {
            command.Parameters.AddWithValue("@id", characterId);
            using var reader = command.ExecuteReader();
            reader.Read();
            itemCount = reader.GetInt64(0);
            itemFingerprint = Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture);
        }

        return new CrossServerCharacterSnapshot(
            characterId, money, money2, aaPoint, itemCount, itemFingerprint, transferRequestTime);
    }

    public bool TryParkAndJournal(CrossServerTransferJournal journal)
    {
        if (journal.State != CrossServerTransferState.Parked)
            return false;

        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            var existingState = ReadState(connection, transaction, journal.CharacterId, forUpdate: true);
            if (existingState is CrossServerTransferState.Parked or CrossServerTransferState.Transferred)
            {
                transaction.Rollback();
                return false; // live journal: the double-departure refusal
            }

            if (existingState != null)
                Execute(connection, transaction,
                    "DELETE FROM `character_transfer_journals` WHERE `character_id` = @id",
                    ("@id", journal.CharacterId));

            using (var insert = new MySqlCommand(
                       """
                       INSERT INTO `character_transfer_journals`
                           (`character_id`, `account_id`, `source_server_key`, `target_server_key`,
                            `state`, `snapshot_json`, `created_at`, `updated_at`)
                       VALUES
                           (@character_id, @account_id, @source_server_key, @target_server_key,
                            @state, @snapshot_json, @created_at, @updated_at)
                       """,
                       connection, transaction))
            {
                insert.Parameters.AddWithValue("@character_id", journal.CharacterId);
                insert.Parameters.AddWithValue("@account_id", journal.AccountId);
                insert.Parameters.AddWithValue("@source_server_key", journal.SourceServerKey);
                insert.Parameters.AddWithValue("@target_server_key", journal.TargetServerKey);
                insert.Parameters.AddWithValue("@state", (byte)journal.State);
                insert.Parameters.AddWithValue("@snapshot_json", journal.Snapshot.ToJson());
                insert.Parameters.AddWithValue("@created_at", journal.CreatedUtc);
                insert.Parameters.AddWithValue("@updated_at", journal.UpdatedUtc);
                insert.ExecuteNonQuery();
            }

            // Park marker on the characters row, in the same transaction as the journal insert.
            Execute(connection, transaction,
                "UPDATE `characters` SET `transfer_request_time` = @parked WHERE `id` = @id",
                ("@parked", journal.UpdatedUtc), ("@id", journal.CharacterId));

            transaction.Commit();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Cross-server departure journal write failed for character {0}", journal.CharacterId);
            try
            {
                transaction.Rollback();
            }
            catch (Exception rollbackEx)
            {
                Logger.Error(rollbackEx, "Rollback of departure journal write failed for character {0}", journal.CharacterId);
            }

            return false;
        }
    }

    public CrossServerTransferJournal Get(ulong characterId)
    {
        using var connection = MySQL.CreateConnection();
        using var command = new MySqlCommand(
            """
            SELECT `character_id`, `account_id`, `source_server_key`, `target_server_key`,
                   `state`, `snapshot_json`, `created_at`, `updated_at`
            FROM `character_transfer_journals`
            WHERE `character_id` = @id
            """,
            connection);
        command.Parameters.AddWithValue("@id", characterId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadJournal(reader) : null;
    }

    public bool TrySetState(ulong characterId, CrossServerTransferState expected, CrossServerTransferState next)
    {
        using var connection = MySQL.CreateConnection();
        using var command = new MySqlCommand(
            """
            UPDATE `character_transfer_journals`
            SET `state` = @next, `updated_at` = @updated
            WHERE `character_id` = @id AND `state` = @expected
            """,
            connection);
        command.Parameters.AddWithValue("@next", (byte)next);
        command.Parameters.AddWithValue("@updated", DateTime.UtcNow);
        command.Parameters.AddWithValue("@id", characterId);
        command.Parameters.AddWithValue("@expected", (byte)expected);
        return command.ExecuteNonQuery() == 1;
    }

    public bool TryRestore(ulong characterId, CrossServerCharacterSnapshot snapshot, CrossServerTransferState expected, CrossServerTransferState next)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            if (ReadState(connection, transaction, characterId, forUpdate: true) != expected)
            {
                transaction.Rollback();
                return false;
            }

            // Witness the inventory before writing anything: same items as the snapshot, or no restore.
            using (var probe = new MySqlCommand(
                       "SELECT COUNT(*), COALESCE(SUM(`id`), 0) FROM `items` WHERE `owner` = @id",
                       connection, transaction))
            {
                probe.Parameters.AddWithValue("@id", characterId);
                using var reader = probe.ExecuteReader();
                reader.Read();
                var itemCount = reader.GetInt64(0);
                var fingerprint = Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture);
                reader.Close();

                if (itemCount != snapshot.ItemCount || fingerprint != snapshot.ItemFingerprint)
                {
                    Logger.Error(
                        "Cross-server restore refused for character {0}: item witness changed (snapshot {1}/{2}, live {3}/{4}).",
                        characterId, snapshot.ItemCount, snapshot.ItemFingerprint, itemCount, fingerprint);
                    transaction.Rollback();
                    return false;
                }
            }

            using (var restore = new MySqlCommand(
                       """
                       UPDATE `characters`
                       SET `money` = @money, `money2` = @money2, `aa_point` = @aa_point,
                           `transfer_request_time` = @transfer_request_time
                       WHERE `id` = @id
                       """,
                       connection, transaction))
            {
                restore.Parameters.AddWithValue("@money", snapshot.Money);
                restore.Parameters.AddWithValue("@money2", snapshot.Money2);
                restore.Parameters.AddWithValue("@aa_point", snapshot.AaPoint);
                restore.Parameters.AddWithValue("@transfer_request_time", snapshot.TransferRequestUtc);
                restore.Parameters.AddWithValue("@id", characterId);
                restore.ExecuteNonQuery();
            }

            using (var journal = new MySqlCommand(
                       "UPDATE `character_transfer_journals` SET `state` = @next, `updated_at` = @updated WHERE `character_id` = @id",
                       connection, transaction))
            {
                journal.Parameters.AddWithValue("@next", (byte)next);
                journal.Parameters.AddWithValue("@updated", DateTime.UtcNow);
                journal.Parameters.AddWithValue("@id", characterId);
                journal.ExecuteNonQuery();
            }

            transaction.Commit();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Cross-server restore failed for character {0}", characterId);
            try
            {
                transaction.Rollback();
            }
            catch (Exception rollbackEx)
            {
                Logger.Error(rollbackEx, "Rollback of restore failed for character {0}", characterId);
            }

            return false;
        }
    }

    public bool TryAbandonParked(ulong characterId)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            if (ReadState(connection, transaction, characterId, forUpdate: true) != CrossServerTransferState.Parked)
            {
                transaction.Rollback();
                return false;
            }

            Execute(connection, transaction,
                "UPDATE `characters` SET `transfer_request_time` = @cleared WHERE `id` = @id",
                ("@cleared", DateTime.MinValue), ("@id", characterId));
            Execute(connection, transaction,
                "UPDATE `character_transfer_journals` SET `state` = @next, `updated_at` = @updated WHERE `character_id` = @id",
                ("@next", (byte)CrossServerTransferState.RolledBack),
                ("@updated", DateTime.UtcNow),
                ("@id", characterId));

            transaction.Commit();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Cross-server abandon of parked journal failed for character {0}", characterId);
            try
            {
                transaction.Rollback();
            }
            catch (Exception rollbackEx)
            {
                Logger.Error(rollbackEx, "Rollback of parked-journal abandon failed for character {0}", characterId);
            }

            return false;
        }
    }

    public IReadOnlyList<CrossServerTransferJournal> LoadAll()
    {
        var rows = new List<CrossServerTransferJournal>();
        using var connection = MySQL.CreateConnection();
        using var command = new MySqlCommand(
            """
            SELECT `character_id`, `account_id`, `source_server_key`, `target_server_key`,
                   `state`, `snapshot_json`, `created_at`, `updated_at`
            FROM `character_transfer_journals`
            ORDER BY `character_id`
            """,
            connection);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            rows.Add(ReadJournal(reader));

        return rows;
    }

    private static CrossServerTransferState? ReadState(MySqlConnection connection, MySqlTransaction transaction, ulong characterId, bool forUpdate)
    {
        var sql = "SELECT `state` FROM `character_transfer_journals` WHERE `character_id` = @id" +
                  (forUpdate ? " FOR UPDATE" : string.Empty);
        using var command = new MySqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@id", characterId);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : (CrossServerTransferState)Convert.ToByte(value, CultureInfo.InvariantCulture);
    }

    private static CrossServerTransferJournal ReadJournal(MySqlDataReader reader) => new(
        reader.GetUInt64(0),
        reader.GetUInt32(1),
        reader.GetString(2),
        reader.GetString(3),
        (CrossServerTransferState)reader.GetByte(4),
        // A malformed snapshot_json throws here on purpose: recovery must not run on guessed state.
        CrossServerCharacterSnapshot.FromJson(reader.GetString(5)),
        reader.GetDateTime(6),
        reader.GetDateTime(7));

    private static void Execute(MySqlConnection connection, MySqlTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }
}
