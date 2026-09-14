using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// The recipes a character has learned by using recipe items (<c>item_recipes</c>).
///
/// The 10.0.2.13 client links a recipe item to its craft out of its own copy of the world database, so it
/// knows what the item teaches; what it cannot do is remember it. 2811 crafts are only reachable through a
/// recipe item, so without this the "recipe book" resets on every logout and any craft the character has
/// not learned in this session is unusable.
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

    /// <summary>
    /// Zero-based position of a learned craft in the character's recipe list, or -1 when it is not learned.
    /// Sent to the client alongside the craft id when a recipe is unlocked.
    /// </summary>
    public int GetLearnedIndex(uint craftId)
    {
        lock (_sync)
        {
            var index = 0;
            foreach (var learned in _crafts.Order())
            {
                if (learned == craftId)
                    return index;
                index++;
            }

            return -1;
        }
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
    /// Records a learned recipe. Returns false when it was already known or could not be stored, so the
    /// caller does not announce a recipe twice.
    /// </summary>
    public bool Learn(uint craftId)
    {
        if (craftId == 0 || !CraftManager.Instance.HasCraft(craftId))
        {
            Logger.Warn("Refusing to learn unknown recipe craft {0} for character {1}", craftId, Owner.Id);
            return false;
        }

        lock (_sync)
        {
            if (_crafts.Contains(craftId))
                return false;

            try
            {
                Persist(craftId);
                _crafts.Add(craftId);
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to store learned recipe {0} for character {1}", craftId, Owner.Id);
                return false;
            }
        }
    }

    private void Persist(uint craftId)
    {
        // INSERT IGNORE: the row is the whole state, so a duplicate from a retried cast is not an error.
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT IGNORE INTO character_recipes(owner, craft_id) VALUES (@owner, @craftId)";
        command.Parameters.AddWithValue("@owner", Owner.Id);
        command.Parameters.AddWithValue("@craftId", craftId);
        command.ExecuteNonQuery();
    }
}
