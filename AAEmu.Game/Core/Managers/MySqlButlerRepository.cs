using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models.Game.Butlers;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public sealed class MySqlButlerRepository : IButlerRepository
{
    public IReadOnlyList<CharacterButlerRecord> LoadAll()
    {
        using var connection = MySQL.CreateConnection();
        return LoadButlerRecords(connection);
    }

    private static IReadOnlyList<CharacterButlerRecord> LoadButlerRecords(MySqlConnection connection)
    {
        var records = new List<CharacterButlerRecord>();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT character_id, house_id, name, labor_power, lp_charged_amount, " +
            "lp_charge_reset_time, remain_production_cost " +
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
                reader.GetUInt16("remain_production_cost"),
                reader.GetInt64("lp_charge_reset_time")));
        }

        return records;
    }

    public IReadOnlyList<CharacterButlerStateRecord> LoadAllStates()
    {
        using var connection = MySQL.CreateConnection();
        var records = LoadButlerRecords(connection);
        var permanentDatas = records.ToDictionary(record => record.CharacterId,
            _ => new Dictionary<sbyte, ulong>());
        var jobs = records.ToDictionary(record => record.CharacterId,
            _ => new List<ButlerHarvestJob>());
        var specialtyTradeJobs = records.ToDictionary(record => record.CharacterId,
            _ => new List<ButlerSpecialtyTradeJob>());
        var storedItems = records.ToDictionary(record => record.CharacterId,
            _ => new List<ButlerStoredItem>());

        LoadPermanentDatas(connection, permanentDatas);
        LoadHarvestJobs(connection, jobs);
        LoadSpecialtyTradeJobs(connection, specialtyTradeJobs);
        LoadStoredItems(connection, storedItems);

        return [.. records.Select(record => new CharacterButlerStateRecord(
            record,
            permanentDatas.GetValueOrDefault(record.CharacterId) ?? new Dictionary<sbyte, ulong>(),
            jobs.GetValueOrDefault(record.CharacterId) ?? [],
            storedItems.GetValueOrDefault(record.CharacterId) ?? [],
            specialtyTradeJobs.GetValueOrDefault(record.CharacterId) ?? []))];
    }

    public bool TryChangeHouse(CharacterButlerRecord record, uint expectedHouseId)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        if (!TryChangeHouse(record, expectedHouseId, connection, transaction))
        {
            transaction.Rollback();
            return false;
        }

        transaction.Commit();
        return true;
    }

    public bool TryChangeHouse(CharacterButlerRecord record, uint expectedHouseId,
        MySqlConnection connection, MySqlTransaction transaction)
    {
        var rows = LockCharacterAndHouse(connection, transaction, record.CharacterId, record.HouseId);
        var characterRow = rows.FirstOrDefault(row => row.CharacterId == record.CharacterId);
        var actualHouseId = characterRow.CharacterId == 0 ? 0 : characterRow.HouseId;
        var occupiedByAnotherCharacter = record.HouseId != 0 && rows.Any(row =>
            row.HouseId == record.HouseId && row.CharacterId != record.CharacterId);
        if (actualHouseId != expectedHouseId || occupiedByAnotherCharacter)
            return false;

        var affected = characterRow.CharacterId == 0
            ? Insert(record, connection, transaction)
            : Update(record, connection, transaction, includeHouse: true);
        if (affected != 1)
            return false;
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

    public void SavePermanentData(uint characterId, sbyte key, ulong value, MySqlConnection connection,
        MySqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO character_butler_permanent_data (character_id, data_key, data_value) " +
            "VALUES (@character_id, @data_key, @data_value) " +
            "ON DUPLICATE KEY UPDATE data_value=@data_value";
        command.Parameters.AddWithValue("@character_id", characterId);
        command.Parameters.AddWithValue("@data_key", key);
        command.Parameters.AddWithValue("@data_value", value);
        command.Prepare();
        command.ExecuteNonQuery();
    }

    public void DeletePermanentData(uint characterId, sbyte key, MySqlConnection connection,
        MySqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "DELETE FROM character_butler_permanent_data WHERE character_id=@character_id AND data_key=@data_key";
        command.Parameters.AddWithValue("@character_id", characterId);
        command.Parameters.AddWithValue("@data_key", key);
        command.Prepare();
        command.ExecuteNonQuery();
    }

    public long InsertHarvestJob(uint characterId, ButlerHarvestJobCandidate candidate,
        MySqlConnection connection, MySqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO character_butler_harvest_jobs " +
            "(character_id, static_harvest_id, requested_amount, remaining_repeat_count, " +
            "lp_for_calc_exp, update_time) VALUES " +
            "(@character_id, @static_harvest_id, @requested_amount, @remaining_repeat_count, " +
            "@lp_for_calc_exp, @update_time)";
        command.Parameters.AddWithValue("@character_id", characterId);
        command.Parameters.AddWithValue("@static_harvest_id", candidate.StaticHarvestId);
        command.Parameters.AddWithValue("@requested_amount", candidate.RequestedAmount);
        command.Parameters.AddWithValue("@remaining_repeat_count", candidate.RemainingRepeatCount);
        command.Parameters.AddWithValue("@lp_for_calc_exp", candidate.LaborPowerForExperience);
        command.Parameters.AddWithValue("@update_time", candidate.UpdateTime);
        command.Prepare();
        if (command.ExecuteNonQuery() != 1 || command.LastInsertedId <= 0)
            throw new InvalidOperationException("Could not assign a durable farmhand harvest job id.");
        return command.LastInsertedId;
    }

    public bool UpdateHarvestJob(uint characterId, ButlerHarvestJob job,
        ushort expectedRemainingRepeatCount, long expectedUpdateTime, MySqlConnection connection,
        MySqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "UPDATE character_butler_harvest_jobs SET static_harvest_id=@static_harvest_id, " +
            "requested_amount=@requested_amount, remaining_repeat_count=@remaining_repeat_count, " +
            "lp_for_calc_exp=@lp_for_calc_exp, update_time=@update_time " +
            "WHERE id=@id AND character_id=@character_id " +
            "AND remaining_repeat_count=@expected_repeat_count AND update_time=@expected_update_time";
        command.Parameters.AddWithValue("@id", job.JobId);
        command.Parameters.AddWithValue("@character_id", characterId);
        command.Parameters.AddWithValue("@static_harvest_id", job.StaticHarvestId);
        command.Parameters.AddWithValue("@requested_amount", job.RequestedAmount);
        command.Parameters.AddWithValue("@remaining_repeat_count", job.RemainingRepeatCount);
        command.Parameters.AddWithValue("@lp_for_calc_exp", job.LaborPowerForExperience);
        command.Parameters.AddWithValue("@update_time", job.UpdateTime);
        command.Parameters.AddWithValue("@expected_repeat_count", expectedRemainingRepeatCount);
        command.Parameters.AddWithValue("@expected_update_time", expectedUpdateTime);
        command.Prepare();
        return command.ExecuteNonQuery() == 1;
    }

    public bool DeleteHarvestJob(uint characterId, long jobId, MySqlConnection connection,
        MySqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "DELETE FROM character_butler_harvest_jobs WHERE id=@id AND character_id=@character_id";
        command.Parameters.AddWithValue("@id", jobId);
        command.Parameters.AddWithValue("@character_id", characterId);
        command.Prepare();
        return command.ExecuteNonQuery() == 1;
    }

    public int DeleteAllHarvestJobs(uint characterId, MySqlConnection connection,
        MySqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM character_butler_harvest_jobs WHERE character_id=@character_id";
        command.Parameters.AddWithValue("@character_id", characterId);
        command.Prepare();
        return command.ExecuteNonQuery();
    }

    public int DeleteAllSpecialtyTradeJobs(uint characterId, MySqlConnection connection,
        MySqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "DELETE FROM character_butler_specialty_trade_jobs WHERE character_id=@character_id";
        command.Parameters.AddWithValue("@character_id", characterId);
        command.Prepare();
        return command.ExecuteNonQuery();
    }

    public long InsertSpecialtyTradeJob(uint characterId, ButlerSpecialtyTradeJobCandidate candidate,
        MySqlConnection connection, MySqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO character_butler_specialty_trade_jobs " +
            "(character_id, npc_id, specialty_type, to_zone_group_type, product_item_id, created_time, delivery_time) " +
            "VALUES (@character_id, @npc_id, @specialty_type, @to_zone_group_type, @product_item_id, " +
            "@created_time, @delivery_time)";
        command.Parameters.AddWithValue("@character_id", characterId);
        command.Parameters.AddWithValue("@npc_id", candidate.NpcId);
        command.Parameters.AddWithValue("@specialty_type", candidate.SpecialtyType);
        command.Parameters.AddWithValue("@to_zone_group_type", candidate.ToZoneGroupType);
        command.Parameters.AddWithValue("@product_item_id", candidate.ProductItemId);
        command.Parameters.AddWithValue("@created_time", candidate.CreatedTime);
        command.Parameters.AddWithValue("@delivery_time", candidate.DeliveryTime);
        command.Prepare();
        if (command.ExecuteNonQuery() != 1 || command.LastInsertedId <= 0)
            throw new InvalidOperationException("Could not assign a durable farmhand specialty-trade job id.");
        return command.LastInsertedId;
    }

    public bool DeleteSpecialtyTradeJob(uint characterId, long jobId, MySqlConnection connection,
        MySqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "DELETE FROM character_butler_specialty_trade_jobs WHERE id=@id AND character_id=@character_id";
        command.Parameters.AddWithValue("@id", jobId);
        command.Parameters.AddWithValue("@character_id", characterId);
        command.Prepare();
        return command.ExecuteNonQuery() == 1;
    }

    public bool TryInsertHarvestCompletion(long jobId, ushort cycleNumber, long completedAt,
        MySqlConnection connection, MySqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT IGNORE INTO character_butler_harvest_completions " +
            "(job_id, cycle_number, completed_at) VALUES (@job_id, @cycle_number, @completed_at)";
        command.Parameters.AddWithValue("@job_id", jobId);
        command.Parameters.AddWithValue("@cycle_number", cycleNumber);
        command.Parameters.AddWithValue("@completed_at", completedAt);
        command.Prepare();
        return command.ExecuteNonQuery() == 1;
    }

    public void SaveStoredItem(uint characterId, ButlerStoredItem item, MySqlConnection connection,
        MySqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO character_butler_items (character_id, item_type, item_id) " +
            "VALUES (@character_id, @item_type, @item_id)";
        command.Parameters.AddWithValue("@character_id", characterId);
        command.Parameters.AddWithValue("@item_type", item.Type);
        command.Parameters.AddWithValue("@item_id", item.ItemId);
        command.Prepare();
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException($"Could not persist farmhand item {item.ItemId}.");
    }

    public bool DeleteStoredItem(uint characterId, ulong itemId,
        MySqlConnection connection, MySqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "DELETE FROM character_butler_items WHERE character_id=@character_id AND item_id=@item_id";
        command.Parameters.AddWithValue("@character_id", characterId);
        command.Parameters.AddWithValue("@item_id", itemId);
        command.Prepare();
        return command.ExecuteNonQuery() == 1;
    }

    public int DeleteAllStoredItems(uint characterId, MySqlConnection connection,
        MySqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM character_butler_items WHERE character_id=@character_id";
        command.Parameters.AddWithValue("@character_id", characterId);
        command.Prepare();
        return command.ExecuteNonQuery();
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
            "(character_id, house_id, name, labor_power, lp_charged_amount, lp_charge_reset_time, " +
            "remain_production_cost) VALUES (@character_id, @house_id, @name, @labor_power, " +
            "@lp_charged_amount, @lp_charge_reset_time, @remain_production_cost)";
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
            "lp_charge_reset_time=@lp_charge_reset_time, " +
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
        command.Parameters.AddWithValue("@lp_charge_reset_time", record.LpChargeResetTime);
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

    private static void LoadPermanentDatas(MySqlConnection connection,
        IDictionary<uint, Dictionary<sbyte, ulong>> target)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT character_id, data_key, data_value FROM character_butler_permanent_data";
        command.Prepare();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var characterId = reader.GetUInt32("character_id");
            if (target.TryGetValue(characterId, out var values))
                values.Add(reader.GetSByte("data_key"), reader.GetUInt64("data_value"));
        }
    }

    private static void LoadHarvestJobs(MySqlConnection connection,
        IDictionary<uint, List<ButlerHarvestJob>> target)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, character_id, static_harvest_id, requested_amount, remaining_repeat_count, " +
            "lp_for_calc_exp, update_time FROM character_butler_harvest_jobs";
        command.Prepare();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var characterId = reader.GetUInt32("character_id");
            if (!target.TryGetValue(characterId, out var characterJobs))
                continue;
            characterJobs.Add(new ButlerHarvestJob(
                reader.GetInt64("id"),
                reader.GetUInt32("static_harvest_id"),
                reader.GetUInt16("requested_amount"),
                reader.GetUInt16("remaining_repeat_count"),
                reader.GetUInt32("lp_for_calc_exp"),
                reader.GetInt64("update_time")));
        }
    }

    public bool TryLoadSpecialtyTradeJob(uint characterId, long jobId, out ButlerSpecialtyTradeJob job)
    {
        job = null;
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, npc_id, specialty_type, to_zone_group_type, product_item_id, created_time, delivery_time " +
            "FROM character_butler_specialty_trade_jobs WHERE id=@id AND character_id=@character_id";
        command.Parameters.AddWithValue("@id", jobId);
        command.Parameters.AddWithValue("@character_id", characterId);
        command.Prepare();
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return false;
        job = ReadSpecialtyTradeJob(reader);
        return true;
    }

    private static ButlerSpecialtyTradeJob ReadSpecialtyTradeJob(MySqlDataReader reader)
    {
        var zoneGroup = reader.GetInt32("to_zone_group_type");
        if (zoneGroup is < 0 or > ushort.MaxValue)
            throw new InvalidDataException("Specialty trade job has an invalid zone group.");
        var job = new ButlerSpecialtyTradeJob(
            reader.GetInt64("id"),
            reader.GetUInt32("npc_id"),
            reader.GetUInt32("specialty_type"),
            checked((ushort)zoneGroup),
            reader.GetUInt32("product_item_id"),
            reader.GetInt64("created_time"),
            reader.GetUInt32("delivery_time"));
        if (job.JobId <= 0 || job.NpcId == 0 || job.SpecialtyType == 0 || job.ProductItemId == 0 ||
            job.CreatedTime < 0 || job.DeliveryTime == 0)
            throw new InvalidDataException("Specialty trade job contains invalid durable state.");
        return job;
    }

    private static void LoadSpecialtyTradeJobs(MySqlConnection connection,
        IDictionary<uint, List<ButlerSpecialtyTradeJob>> target)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, character_id, npc_id, specialty_type, to_zone_group_type, product_item_id, " +
            "created_time, delivery_time FROM character_butler_specialty_trade_jobs";
        command.Prepare();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var characterId = reader.GetUInt32("character_id");
            if (!target.TryGetValue(characterId, out var characterJobs))
                continue;
            characterJobs.Add(ReadSpecialtyTradeJob(reader));
        }
    }

    private static void LoadStoredItems(MySqlConnection connection,
        IDictionary<uint, List<ButlerStoredItem>> target)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT character_id, item_type, item_id FROM character_butler_items";
        command.Prepare();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var characterId = reader.GetUInt32("character_id");
            if (target.TryGetValue(characterId, out var characterItems))
                characterItems.Add(new ButlerStoredItem(
                    reader.GetByte("item_type"), reader.GetUInt64("item_id")));
        }
    }
}
