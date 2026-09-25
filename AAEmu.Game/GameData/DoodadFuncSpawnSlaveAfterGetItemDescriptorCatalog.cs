using System.Collections.ObjectModel;

using AAEmu.Game.Models.Game.DoodadObj.Details;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

/// <summary>
/// Content-only catalog for the Q09 <c>doodad_func_spawn_slave_after_get_items</c> table.
/// It deliberately does not resolve a slave, grant an item, schedule work, or create a unit.
/// </summary>
public sealed class DoodadFuncSpawnSlaveAfterGetItemDescriptorCatalog
{
    private const string TableName = "doodad_func_spawn_slave_after_get_items";

    private DoodadFuncSpawnSlaveAfterGetItemDescriptorCatalog(
        IDictionary<uint, DoodadFuncSpawnSlaveAfterGetItemDescriptor> descriptors)
    {
        Descriptors = new ReadOnlyDictionary<uint, DoodadFuncSpawnSlaveAfterGetItemDescriptor>(
            new Dictionary<uint, DoodadFuncSpawnSlaveAfterGetItemDescriptor>(descriptors));
    }

    public IReadOnlyDictionary<uint, DoodadFuncSpawnSlaveAfterGetItemDescriptor> Descriptors { get; }

    public int Count => Descriptors.Count;

    public bool TryGet(uint id, out DoodadFuncSpawnSlaveAfterGetItemDescriptor descriptor) =>
        Descriptors.TryGetValue(id, out descriptor);

    public static DoodadFuncSpawnSlaveAfterGetItemDescriptorCatalog Load(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var descriptors = new Dictionary<uint, DoodadFuncSpawnSlaveAfterGetItemDescriptor>();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT id, item_id, delay, offset_x, offset_z, angle FROM {TableName} ORDER BY id";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var id = ReadRequiredUInt32(reader, "id");
                var itemId = ReadRequiredUInt32(reader, "item_id");
                var delay = ReadDelay(reader);
                var offsetX = ReadFiniteSingle(reader, "offset_x");
                var offsetZ = ReadFiniteSingle(reader, "offset_z");
                var angle = ReadFiniteSingle(reader, "angle");

                var descriptor = new DoodadFuncSpawnSlaveAfterGetItemDescriptor
                {
                    Id = id,
                    ItemId = itemId,
                    Delay = delay,
                    OffsetX = offsetX,
                    OffsetZ = offsetZ,
                    Angle = angle
                };

                if (!descriptors.TryAdd(id, descriptor))
                    throw new InvalidDataException($"{TableName} contains duplicate descriptor id {id}.");
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex) when (ex is SqliteException or InvalidCastException or FormatException or OverflowException)
        {
            throw new InvalidDataException($"Q09 descriptor table '{TableName}' could not be loaded.", ex);
        }

        if (descriptors.Count == 0)
            throw new InvalidDataException($"Q09 descriptor table '{TableName}' contains no rows.");

        return new DoodadFuncSpawnSlaveAfterGetItemDescriptorCatalog(descriptors);
    }

    private static uint ReadRequiredUInt32(SQLiteWrapperReader reader, string column)
    {
        if (reader.IsDBNull(column))
            throw new InvalidDataException($"{TableName}.{column} must not be null.");

        var value = reader.GetInt64(column);
        if (value <= 0 || value > uint.MaxValue)
            throw new InvalidDataException($"{TableName}.{column} must be a positive uint.");

        return (uint)value;
    }

    private static int ReadDelay(SQLiteWrapperReader reader)
    {
        if (reader.IsDBNull("delay"))
            throw new InvalidDataException($"{TableName}.delay must not be null.");

        var value = reader.GetInt64("delay");
        if (value < int.MinValue || value > int.MaxValue)
            throw new InvalidDataException($"{TableName}.delay is outside Int32 range.");

        var delay = (int)value;
        if (delay < 0)
            throw new InvalidDataException($"{TableName}.delay must not be negative.");

        return delay;
    }

    private static float ReadFiniteSingle(SQLiteWrapperReader reader, string column)
    {
        if (reader.IsDBNull(column))
            throw new InvalidDataException($"{TableName}.{column} must not be null.");

        var value = reader.GetFloat(column);
        if (!float.IsFinite(value))
            throw new InvalidDataException($"{TableName}.{column} must be finite.");

        return value;
    }
}
