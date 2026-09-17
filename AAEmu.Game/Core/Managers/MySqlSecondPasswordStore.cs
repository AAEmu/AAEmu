using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models.Game.SecondPassword;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

/// <summary>The account second passwords in MySQL: one row per account, replaced when the password changes.</summary>
public sealed class MySqlSecondPasswordStore : ISecondPasswordStore
{
    public SecondPasswordRecord Load(uint accountId)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT salt, hash, failed_count FROM account_second_passwords WHERE account_id=@account";
        command.Parameters.AddWithValue("@account", accountId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return new SecondPasswordRecord
        {
            AccountId = accountId,
            Salt = Convert.FromBase64String(reader.GetString(0)),
            Hash = reader.GetString(1),
            FailedCount = reader.GetInt32(2)
        };
    }

    public void Save(SecondPasswordRecord record)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO account_second_passwords (account_id, salt, hash, failed_count, updated_at) " +
            "VALUES (@account, @salt, @hash, @failed, @updated) " +
            "ON DUPLICATE KEY UPDATE salt=VALUES(salt), hash=VALUES(hash), failed_count=VALUES(failed_count), " +
            "updated_at=VALUES(updated_at)";
        command.Parameters.AddWithValue("@account", record.AccountId);
        command.Parameters.AddWithValue("@salt", Convert.ToBase64String(record.Salt));
        command.Parameters.AddWithValue("@hash", record.Hash);
        command.Parameters.AddWithValue("@failed", record.FailedCount);
        command.Parameters.AddWithValue("@updated", DateTime.UtcNow);
        command.ExecuteNonQuery();
    }

    public void Delete(uint accountId)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM account_second_passwords WHERE account_id=@account";
        command.Parameters.AddWithValue("@account", accountId);
        command.ExecuteNonQuery();
    }
}
