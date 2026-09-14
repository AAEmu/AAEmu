using AAEmu.Game.Core.Managers;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// The recipes a character has learned by using recipe items (<c>item_recipes</c>).
///
/// The 10.0.2.13 client links a recipe item to its craft out of its own copy of the world database, so it
/// knows what the item teaches; what it cannot do is remember it. The crafts a recipe item reaches are only
/// usable once learned, so the set has to survive a logout.
///
/// Learning is deliberately two steps. <see cref="PersistLearned"/> writes the rows on a caller-owned
/// transaction that also carries the item deduction, and <see cref="ApplyLearned"/> updates the live set only
/// after that transaction commits: writing the row on its own connection first left a window where a
/// character reloaded holding both the unlock and the recipe item.
/// </summary>
public sealed class CharacterRecipeBook(Character owner)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly object _sync = new();
    private HashSet<uint> _crafts = [];

    public Character Owner { get; } = owner;

    public int Count
    {
        get
        {
            lock (_sync)
                return _crafts.Count;
        }
    }

    public bool IsLearned(uint craftId)
    {
        lock (_sync)
            return _crafts.Contains(craftId);
    }

    /// <summary>Every learned craft, for diagnostics and admin tooling.</summary>
    public uint[] GetLearnedCrafts()
    {
        lock (_sync)
            return [.. _crafts.Order()];
    }

    public void Load(MySqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT craft_id FROM character_recipes WHERE owner = @owner ORDER BY craft_id";
        command.Parameters.AddWithValue("@owner", Owner.Id);
        command.Prepare();

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var craftId = reader.GetUInt32("craft_id");
            if (!CraftManager.Instance.HasCraft(craftId))
            {
                Logger.Warn("Ignoring unknown recipe craft {0} persisted for character {1}", craftId, Owner.Id);
                continue;
            }

            _crafts.Add(craftId);
        }
    }

    /// <summary>
    /// Writes the learned rows on the caller's transaction. The caller must commit before calling
    /// <see cref="ApplyLearned"/>, so the unlock and whatever the cast consumed become durable together.
    /// </summary>
    public void PersistLearned(IEnumerable<uint> craftIds, MySqlConnection connection, MySqlTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // INSERT IGNORE: the row is the whole state, so a duplicate from a retried cast is not an error.
        command.CommandText = "INSERT IGNORE INTO character_recipes(owner, craft_id) VALUES (@owner, @craftId)";
        command.Parameters.AddWithValue("@owner", Owner.Id);
        var craftParameter = command.Parameters.Add("@craftId", MySqlDbType.UInt32);
        command.Prepare();

        foreach (var craftId in craftIds)
        {
            craftParameter.Value = craftId;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Applies a recipe write that has already committed. Never fails.</summary>
    public void ApplyLearned(IEnumerable<uint> craftIds)
    {
        lock (_sync)
        {
            foreach (var craftId in craftIds)
                _crafts.Add(craftId);
        }
    }
}
