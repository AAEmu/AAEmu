using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.Char;

public sealed class CharacterFavoriteCrafts(Character owner)
{
    public const int MaximumEntries = 30;

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly object _sync = new();
    private SortedSet<uint> _craftTypes = [];

    public Character Owner { get; } = owner;

    public int[] GetWireCraftTypes()
    {
        lock (_sync)
            return _craftTypes.Select(craftType => checked((int)craftType)).ToArray();
    }

    public void Load(MySqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT craft_type FROM character_favorite_crafts WHERE owner = @owner ORDER BY craft_type";
        command.Parameters.AddWithValue("@owner", Owner.Id);
        command.Prepare();

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var craftType = reader.GetUInt32("craft_type");
            if (!CraftManager.Instance.HasCraft(craftType))
            {
                Logger.Warn(
                    "Ignoring unknown favorite craft type {0} persisted for character {1}",
                    craftType,
                    Owner.Id);
                continue;
            }

            if (craftType > int.MaxValue)
            {
                Logger.Warn(
                    "Ignoring favorite craft type {0} for character {1}; it cannot be represented by the native i32 field",
                    craftType,
                    Owner.Id);
                continue;
            }

            if (_craftTypes.Count >= MaximumEntries)
            {
                Logger.Warn(
                    "Ignoring favorite craft type {0} for character {1}; native capacity is {2}",
                    craftType,
                    Owner.Id,
                    MaximumEntries);
                continue;
            }

            _craftTypes.Add(craftType);
        }
    }

    public bool TryUpdate(IReadOnlyCollection<int> favorites, IReadOnlyCollection<int> unfavorites)
    {
        ArgumentNullException.ThrowIfNull(favorites);
        ArgumentNullException.ThrowIfNull(unfavorites);

        if (!TryResolveCraftTypes(favorites, out var additions) ||
            !TryResolveCraftTypes(unfavorites, out var removals))
            return false;
        if (additions.Overlaps(removals))
            return false;

        lock (_sync)
        {
            var updated = new SortedSet<uint>(_craftTypes);
            foreach (var craftType in removals)
                updated.Remove(craftType);
            foreach (var craftType in additions)
                updated.Add(craftType);

            if (updated.Count > MaximumEntries)
                return false;
            if (updated.SetEquals(_craftTypes))
                return true;

            try
            {
                Persist(updated);
                _craftTypes = updated;
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to update favorite crafts for character {0}", Owner.Id);
                return false;
            }
        }
    }

    private static bool TryResolveCraftTypes(IEnumerable<int> wireTypes, out SortedSet<uint> craftTypes)
    {
        craftTypes = [];
        foreach (var wireType in wireTypes)
        {
            if (wireType <= 0)
                return false;

            var craftType = checked((uint)wireType);
            if (!CraftManager.Instance.HasCraft(craftType))
                return false;
            craftTypes.Add(craftType);
        }

        return true;
    }

    private void Persist(IEnumerable<uint> craftTypes)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            using (var deleteCommand = connection.CreateCommand())
            {
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = "DELETE FROM character_favorite_crafts WHERE owner = @owner";
                deleteCommand.Parameters.AddWithValue("@owner", Owner.Id);
                deleteCommand.ExecuteNonQuery();
            }

            using (var insertCommand = connection.CreateCommand())
            {
                insertCommand.Transaction = transaction;
                insertCommand.CommandText =
                    "INSERT INTO character_favorite_crafts(owner, craft_type) VALUES (@owner, @craftType)";
                insertCommand.Parameters.AddWithValue("@owner", Owner.Id);
                var craftTypeParameter = insertCommand.Parameters.Add("@craftType", MySqlDbType.UInt32);
                insertCommand.Prepare();

                foreach (var craftType in craftTypes)
                {
                    craftTypeParameter.Value = craftType;
                    if (insertCommand.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("Favorite-craft insert did not affect exactly one row.");
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
