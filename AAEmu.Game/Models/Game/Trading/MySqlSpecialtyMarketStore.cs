using AAEmu.Game.Core.Managers;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Trading;

public sealed class MySqlSpecialtyMarketStore(ISaveManager saveManager) : ISpecialtyMarketStore
{
    public SpecialtyMarketState Load() => saveManager.ExecuteOperation((connection, transaction) =>
    {
        var state = new SpecialtyMarketState();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // All writers lock this row first. Holding it makes the following reads a consistent
        // market snapshot even when the connection uses READ COMMITTED isolation.
        command.CommandText = "SELECT id, revision FROM specialty_market_revision FOR UPDATE";
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read() || reader.GetByte(0) != 1)
                throw new InvalidDataException("Missing or corrupt specialty market revision singleton.");
            state.Revision = reader.GetInt64(1);
            if (state.Revision < 0 || reader.Read())
                throw new InvalidDataException("Corrupt specialty market revision singleton.");
        }

        command.CommandText = "SELECT item_id, zone_group_id, ratio, demand_remainder FROM specialty_market_routes";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var itemId = reader.GetUInt32(0);
                var zoneGroupId = reader.GetUInt32(1);
                if (!state.PriceRatios.TryGetValue(itemId, out var ratios))
                {
                    ratios = [];
                    state.PriceRatios.Add(itemId, ratios);
                    state.DemandRemainders.Add(itemId, []);
                }
                ratios.Add(zoneGroupId, reader.GetInt32(2));
                state.DemandRemainders[itemId].Add(zoneGroupId, reader.GetInt32(3));
            }
        }

        command.CommandText = "SELECT zone_group_id, tag_id, `sequence`, item_id, amount " +
            "FROM specialty_market_contributions ORDER BY zone_group_id, tag_id, `sequence`";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var key = (reader.GetUInt32(0), reader.GetUInt32(1));
                if (!state.MaterialContributions.TryGetValue(key, out var contributions))
                {
                    contributions = [];
                    state.MaterialContributions.Add(key, contributions);
                }
                contributions.Add(new SpecialtyMaterialContribution(
                    reader.GetUInt64(2),
                    reader.GetUInt32(3),
                    reader.GetUInt32(4)));
            }
        }

        command.CommandText = "SELECT zone_group_id, trade_good_id, amount FROM specialty_market_cargo";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                state.CargoStock.Add((reader.GetUInt32(0), reader.GetUInt32(1)), reader.GetUInt32(2));
        }

        command.CommandText = "SELECT item_id, zone_group_id, `sequence`, ratio, recorded " +
            "FROM specialty_market_history ORDER BY item_id, zone_group_id, `sequence`";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var key = (reader.GetUInt32(0), reader.GetUInt32(1));
                if (!state.Records.TryGetValue(key, out var records))
                {
                    records = [];
                    state.Records.Add(key, records);
                }
                if (reader.GetInt32(2) != records.Count || records.Count >= 256)
                    throw new InvalidDataException("Corrupt specialty market history sequence.");
                records.Add(new SpecialtyMarketRecord(reader.GetInt32(3), reader.GetInt64(4)));
            }
        }

        try
        {
            ValidateState(state);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Corrupt specialty market state.", exception);
        }
        return state;
    });

    public void Commit(SpecialtyMarketWrite write)
    {
        ValidateWrite(write);
        saveManager.ExecuteOperation((connection, transaction) =>
        {
            Apply(connection, transaction, write);
            return true;
        });
    }

    public void Apply(MySqlConnection connection, MySqlTransaction transaction, SpecialtyMarketWrite write)
    {
        ValidateWrite(write);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.Connection != connection)
            throw new ArgumentException("The transaction must belong to the supplied connection.", nameof(transaction));

        if (Execute(connection, transaction,
            "UPDATE specialty_market_revision SET revision = @updated WHERE id = 1 AND revision = @expected",
            ("@updated", write.Updated.Revision), ("@expected", write.Expected.Revision)) != 1)
            throw new SpecialtyMarketConflictException();

        var expectedRoutes = NormalizeRoutes(write.Expected);
        var updatedRoutes = NormalizeRoutes(write.Updated);
        foreach (var key in expectedRoutes.Keys.Union(updatedRoutes.Keys))
        {
            var hadRow = expectedRoutes.TryGetValue(key, out var expected);
            var hasRow = updatedRoutes.TryGetValue(key, out var updated);
            if (hadRow && hasRow && expected == updated)
                continue;
            if (!hasRow)
            {
                Execute(connection, transaction,
                    "DELETE FROM specialty_market_routes WHERE item_id = @item AND zone_group_id = @zone",
                    ("@item", key.ItemId), ("@zone", key.ZoneGroupId));
            }
            else
            {
                Execute(connection, transaction,
                    "INSERT INTO specialty_market_routes (item_id, zone_group_id, ratio, demand_remainder) " +
                    "VALUES (@item, @zone, @ratio, @remainder) ON DUPLICATE KEY UPDATE ratio = @ratio, demand_remainder = @remainder",
                    ("@item", key.ItemId), ("@zone", key.ZoneGroupId), ("@ratio", updated.Ratio), ("@remainder", updated.Remainder));
            }
        }

        ApplyContributions(connection, transaction, write.Expected.MaterialContributions, write.Updated.MaterialContributions);
        ApplyStock(connection, transaction, "specialty_market_cargo", "trade_good_id", write.Expected.CargoStock, write.Updated.CargoStock);

        foreach (var key in write.Expected.Records.Keys.Union(write.Updated.Records.Keys))
        {
            var expected = write.Expected.Records.GetValueOrDefault(key) ?? [];
            var updated = write.Updated.Records.GetValueOrDefault(key) ?? [];
            if (expected.Select(record => (record.Ratio, record.Recorded))
                .SequenceEqual(updated.Select(record => (record.Ratio, record.Recorded))))
                continue;

            Execute(connection, transaction,
                "DELETE FROM specialty_market_history WHERE item_id = @item AND zone_group_id = @zone",
                ("@item", key.ItemId), ("@zone", key.ZoneGroupId));
            for (var sequence = 0; sequence < updated.Count; sequence++)
            {
                Execute(connection, transaction,
                    "INSERT INTO specialty_market_history (item_id, zone_group_id, `sequence`, ratio, recorded) " +
                    "VALUES (@item, @zone, @sequence, @ratio, @recorded)",
                    ("@item", key.ItemId), ("@zone", key.ZoneGroupId), ("@sequence", sequence),
                    ("@ratio", updated[sequence].Ratio), ("@recorded", updated[sequence].Recorded));
            }
        }
    }

    private static Dictionary<(uint ItemId, uint ZoneGroupId), (int Ratio, int Remainder)> NormalizeRoutes(SpecialtyMarketState state)
    {
        var routes = new Dictionary<(uint, uint), (int, int)>();
        foreach (var (itemId, ratios) in state.PriceRatios)
        {
            state.DemandRemainders.TryGetValue(itemId, out var remainders);
            foreach (var (zoneGroupId, ratio) in ratios)
                routes.Add((itemId, zoneGroupId), (ratio, remainders?.GetValueOrDefault(zoneGroupId) ?? 0));
        }
        return routes;
    }

    private static void ApplyStock(MySqlConnection connection, MySqlTransaction transaction, string table, string keyColumn,
        Dictionary<(uint ZoneGroupId, uint Id), uint> expected, Dictionary<(uint ZoneGroupId, uint Id), uint> updated)
    {
        foreach (var key in expected.Keys.Union(updated.Keys))
        {
            var hadRow = expected.TryGetValue(key, out var oldAmount);
            var hasRow = updated.TryGetValue(key, out var amount);
            if (hadRow && hasRow && oldAmount == amount)
                continue;
            if (!hasRow)
            {
                Execute(connection, transaction,
                    $"DELETE FROM {table} WHERE zone_group_id = @zone AND {keyColumn} = @id",
                    ("@zone", key.ZoneGroupId), ("@id", key.Id));
            }
            else
            {
                Execute(connection, transaction,
                    $"INSERT INTO {table} (zone_group_id, {keyColumn}, amount) VALUES (@zone, @id, @amount) " +
                    "ON DUPLICATE KEY UPDATE amount = @amount",
                    ("@zone", key.ZoneGroupId), ("@id", key.Id), ("@amount", amount));
            }
        }
    }

    private static void ApplyContributions(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Dictionary<(uint ZoneGroupId, uint TagId), List<SpecialtyMaterialContribution>> expectedQueues,
        Dictionary<(uint ZoneGroupId, uint TagId), List<SpecialtyMaterialContribution>> updatedQueues)
    {
        var expected = NormalizeContributions(expectedQueues);
        var updated = NormalizeContributions(updatedQueues);
        foreach (var key in expected.Keys.Union(updated.Keys))
        {
            var hadRow = expected.TryGetValue(key, out var oldContribution);
            var hasRow = updated.TryGetValue(key, out var contribution);
            if (hadRow && hasRow &&
                oldContribution.ItemId == contribution.ItemId &&
                oldContribution.Amount == contribution.Amount)
                continue;
            if (!hasRow)
            {
                Execute(connection, transaction,
                    "DELETE FROM specialty_market_contributions " +
                    "WHERE zone_group_id = @zone AND tag_id = @tag AND `sequence` = @sequence",
                    ("@zone", key.ZoneGroupId), ("@tag", key.TagId), ("@sequence", key.Sequence));
            }
            else
            {
                Execute(connection, transaction,
                    "INSERT INTO specialty_market_contributions (zone_group_id, tag_id, `sequence`, item_id, amount) " +
                    "VALUES (@zone, @tag, @sequence, @item, @amount) " +
                    "ON DUPLICATE KEY UPDATE item_id = @item, amount = @amount",
                    ("@zone", key.ZoneGroupId), ("@tag", key.TagId), ("@sequence", key.Sequence),
                    ("@item", contribution.ItemId), ("@amount", contribution.Amount));
            }
        }
    }

    private static Dictionary<(uint ZoneGroupId, uint TagId, ulong Sequence), SpecialtyMaterialContribution>
        NormalizeContributions(Dictionary<(uint ZoneGroupId, uint TagId), List<SpecialtyMaterialContribution>> queues)
    {
        var contributions = new Dictionary<(uint, uint, ulong), SpecialtyMaterialContribution>();
        foreach (var (key, queue) in queues)
        foreach (var contribution in queue)
            contributions.Add((key.ZoneGroupId, key.TagId, contribution.Sequence), contribution);
        return contributions;
    }

    private static int Execute(MySqlConnection connection, MySqlTransaction transaction, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.Prepare();
        return command.ExecuteNonQuery();
    }

    private static void ValidateWrite(SpecialtyMarketWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);
        ValidateState(write.Expected);
        ValidateState(write.Updated);
        if (write.Expected.Revision == long.MaxValue || write.Updated.Revision != write.Expected.Revision + 1)
            throw new ArgumentException("Updated revision must be exactly the expected revision plus one.", nameof(write));
    }

    private static void ValidateState(SpecialtyMarketState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Revision < 0)
            throw new ArgumentException("Market revision must be nonnegative.", nameof(state));
        if (state.PriceRatios == null || state.DemandRemainders == null || state.MaterialContributions == null ||
            state.CargoStock == null || state.Records == null)
            throw new ArgumentException("Market dictionaries must not be null.", nameof(state));

        foreach (var (itemId, ratios) in state.PriceRatios)
        {
            if (itemId == 0 || ratios == null)
                throw new ArgumentException("Market item keys must be positive and ratio dictionaries must not be null.", nameof(state));
            foreach (var (zoneGroupId, ratio) in ratios)
            {
                if (zoneGroupId == 0 || ratio < 0)
                    throw new ArgumentException("Market zone keys must be positive and ratios must be nonnegative.", nameof(state));
            }
        }

        foreach (var (itemId, remainders) in state.DemandRemainders)
        {
            if (itemId == 0 || remainders == null)
                throw new ArgumentException("Market item keys must be positive and remainder dictionaries must not be null.", nameof(state));
            foreach (var (zoneGroupId, remainder) in remainders)
            {
                if (zoneGroupId == 0 || remainder is < 0 or > 3 ||
                    !state.PriceRatios.TryGetValue(itemId, out var ratios) || !ratios.ContainsKey(zoneGroupId))
                    throw new ArgumentException("Demand remainders must be between zero and three and have a matching ratio route.", nameof(state));
            }
        }

        foreach (var (key, contributions) in state.MaterialContributions)
        {
            if (key.ZoneGroupId == 0 || key.TagId == 0 || contributions == null || contributions.Count == 0)
                throw new ArgumentException("Material contribution keys must be positive and queues must not be empty.", nameof(state));
            ulong total = 0;
            ulong previousSequence = 0;
            foreach (var contribution in contributions)
            {
                if (contribution == null || contribution.Sequence == 0 || contribution.Sequence <= previousSequence ||
                    contribution.ItemId == 0 || contribution.Amount == 0)
                    throw new ArgumentException("Material contributions must have positive values and strictly increasing sequences.", nameof(state));
                total += contribution.Amount;
                if (total > uint.MaxValue)
                    throw new ArgumentException("Material contribution totals must fit in an unsigned integer.", nameof(state));
                previousSequence = contribution.Sequence;
            }
        }

        foreach (var key in state.CargoStock.Keys)
        {
            if (key.Item1 == 0 || key.Item2 == 0)
                throw new ArgumentException("Stock keys must be positive.", nameof(state));
        }

        foreach (var (key, records) in state.Records)
        {
            if (key.ItemId == 0 || key.ZoneGroupId == 0 || records == null || records.Count > 256)
                throw new ArgumentException("History keys must be positive and history must contain at most 256 records.", nameof(state));
            long previousTime = 0;
            foreach (var record in records)
            {
                if (record == null || record.Ratio < 0 || record.Recorded < previousTime)
                    throw new ArgumentException("History ratios and times must be nonnegative, with times in nondecreasing order.", nameof(state));
                previousTime = record.Recorded;
            }
        }
    }
}
