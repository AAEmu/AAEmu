using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models.Game.Butlers;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public sealed class MySqlButlerRepository : IButlerRepository
{
    public IReadOnlyList<CharacterButlerRecord> LoadAll()
    {
        var records = new List<CharacterButlerRecord>();
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT character_id, house_id, name, labor_power, lp_charged_amount, remain_production_cost " +
            "FROM character_butlers";
        command.Prepare();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            records.Add(new CharacterButlerRecord(
                reader.GetUInt32("character_id"),
                reader.IsDBNull(reader.GetOrdinal("house_id")) ? 0 : reader.GetUInt32("house_id"),
                reader.GetString("name"),
                reader.GetUInt32("labor_power"),
                reader.GetUInt16("lp_charged_amount"),
                reader.GetUInt16("remain_production_cost")));
        }

        return records;
    }

    public bool TryChangeHouse(CharacterButlerRecord record, uint expectedHouseId)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        var rows = LockCharacterAndHouse(connection, transaction, record.CharacterId, record.HouseId);
        var characterRow = rows.FirstOrDefault(row => row.CharacterId == record.CharacterId);
        var actualHouseId = characterRow.CharacterId == 0 ? 0 : characterRow.HouseId;
        var occupiedByAnotherCharacter = record.HouseId != 0 && rows.Any(row =>
            row.HouseId == record.HouseId && row.CharacterId != record.CharacterId);
        if (actualHouseId != expectedHouseId || occupiedByAnotherCharacter)
        {
            transaction.Rollback();
            return false;
        }

        var affected = characterRow.CharacterId == 0
            ? Insert(record, connection, transaction)
            : Update(record, connection, transaction, includeHouse: true);
        if (affected != 1)
        {
            transaction.Rollback();
            return false;
        }

        transaction.Commit();
        return true;
    }

    public void Save(CharacterButlerRecord record, MySqlConnection connection, MySqlTransaction transaction)
    {
        var rows = LockCharacterAndHouse(connection, transaction, record.CharacterId, 0);
        var characterExists = rows.Any(row => row.CharacterId == record.CharacterId);
        var affected = characterExists
            ? Update(record, connection, transaction, includeHouse: false)
            : Insert(record, connection, transaction);
        if ((!characterExists && affected != 1) || (characterExists && affected is < 0 or > 1))
            throw new InvalidOperationException($"Unexpected character_butlers affected row count: {affected}");
    }

    private static IReadOnlyList<(uint CharacterId, uint HouseId)> LockCharacterAndHouse(
        MySqlConnection connection, MySqlTransaction transaction, uint characterId, uint houseId)
    {
        var rows = new List<(uint CharacterId, uint HouseId)>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT character_id, house_id FROM character_butlers " +
            "WHERE character_id=@character_id OR (@house_id IS NOT NULL AND house_id=@house_id) FOR UPDATE";
        command.Parameters.AddWithValue("@character_id", characterId);
        command.Parameters.Add("@house_id", MySqlDbType.UInt32).Value = houseId == 0 ? DBNull.Value : houseId;
        command.Prepare();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            rows.Add((reader.GetUInt32("character_id"),
                reader.IsDBNull(reader.GetOrdinal("house_id")) ? 0 : reader.GetUInt32("house_id")));
        return rows;
    }

    private static int Insert(CharacterButlerRecord record, MySqlConnection connection,
        MySqlTransaction transaction)
    {
        using var command = CreateWriteCommand(record, connection, transaction);
        command.CommandText =
            "INSERT INTO character_butlers " +
            "(character_id, house_id, name, labor_power, lp_charged_amount, remain_production_cost) " +
            "VALUES (@character_id, @house_id, @name, @labor_power, @lp_charged_amount, @remain_production_cost)";
        command.Prepare();
        return command.ExecuteNonQuery();
    }

    private static int Update(CharacterButlerRecord record, MySqlConnection connection,
        MySqlTransaction transaction, bool includeHouse)
    {
        using var command = CreateWriteCommand(record, connection, transaction);
        command.CommandText = "UPDATE character_butlers SET " +
            (includeHouse ? "house_id=@house_id, " : string.Empty) +
            "name=@name, labor_power=@labor_power, lp_charged_amount=@lp_charged_amount, " +
            "remain_production_cost=@remain_production_cost WHERE character_id=@character_id";
        command.Prepare();
        return command.ExecuteNonQuery();
    }

    private static MySqlCommand CreateWriteCommand(CharacterButlerRecord record, MySqlConnection connection,
        MySqlTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("@character_id", record.CharacterId);
        command.Parameters.Add("@house_id", MySqlDbType.UInt32).Value =
            record.HouseId == 0 ? DBNull.Value : record.HouseId;
        command.Parameters.AddWithValue("@name", record.Name ?? string.Empty);
        command.Parameters.AddWithValue("@labor_power", record.LaborPower);
        command.Parameters.AddWithValue("@lp_charged_amount", record.LpChargedAmount);
        command.Parameters.AddWithValue("@remain_production_cost", record.RemainProductionCost);
        return command;
    }

    public void Delete(uint characterId)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM character_butlers WHERE character_id=@character_id";
        command.Parameters.AddWithValue("@character_id", characterId);
        command.Prepare();
        command.ExecuteNonQuery();
    }
}
