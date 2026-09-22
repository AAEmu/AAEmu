using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// The collection/encyclopedia entries a character has discovered.
/// </summary>
/// <remarks>
/// <para>
/// An entry is one item type the shipped collection content keys on: a member of an encyclopedia
/// guide or the item type a collection/progress record watches. Discovery is a first-seen set — the
/// first time an event for an entry arrives it is recorded here, and later arrivals are replays that
/// must change nothing. Progress itself (the achievement counters and record values a discovery
/// reports into) lives with the rest of that state; this is the ledger that makes discovery itself
/// durable and idempotent across sessions.
/// </para>
/// <para>
/// Rows are rewritten inside <see cref="Character.Save"/>'s transaction. A feature-table failure
/// degrades instead of failing the whole save: the in-memory set stays the source of truth for the
/// session and the next save retries, which is better than letting one missing optional table roll
/// back every other character row.
/// </para>
/// </remarks>
public sealed class CharacterCollections
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>The migration that creates the table this class reads and writes.</summary>
    public const string SqlUpdateFile = "SQL/updates/2026-09-22_aaemu_game_character_collections.sql";

    private readonly object _sync = new();
    private readonly HashSet<uint> _discovered = [];

    public CharacterCollections(Character owner)
    {
        Owner = owner;
    }

    public Character Owner { get; }

    /// <summary>
    /// True until the first sync after world entry. The client's collection view is server-pushed, so a
    /// character loaded from the database queues that sync and only the world-entry replay flushes it.
    /// </summary>
    public bool InitialSyncPending { get; private set; } = true;

    public int Count
    {
        get
        {
            lock (_sync)
                return _discovered.Count;
        }
    }

    public bool IsDiscovered(uint itemTypeId)
    {
        lock (_sync)
            return _discovered.Contains(itemTypeId);
    }

    /// <summary>
    /// Records a discovery.
    /// </summary>
    /// <returns>True only the first time the entry is seen; a replayed discovery returns false.</returns>
    public bool TryDiscover(uint itemTypeId)
    {
        if (itemTypeId == 0)
            return false;

        lock (_sync)
            return _discovered.Add(itemTypeId);
    }

    /// <summary>A copy of every discovered entry, for the sync path and the GM surface.</summary>
    public IReadOnlyCollection<uint> Snapshot()
    {
        lock (_sync)
            return new HashSet<uint>(_discovered);
    }

    /// <summary>Marks the post-world-entry sync as delivered.</summary>
    public void MarkSynced() => InitialSyncPending = false;

    /// <summary>
    /// Merges restored rows into the set, asking the supplied resolver whether each entry still exists
    /// in the shipped content.
    /// </summary>
    /// <remarks>
    /// The resolver is injected rather than read from the content singleton here: persistence must be
    /// exercisable without the game-data singletons being configured, and a row whose content has since
    /// disappeared is dropped loudly rather than silently kept or silently lost.
    /// </remarks>
    /// <param name="itemTypes">The item type ids read back from storage.</param>
    /// <param name="entryKnown">
    /// Whether an entry still exists in content, or null to accept every restored row unasked.
    /// </param>
    /// <returns>How many entries were kept.</returns>
    public int Restore(IEnumerable<uint> itemTypes, Func<uint, bool> entryKnown)
    {
        if (itemTypes == null)
            return 0;

        var kept = 0;
        foreach (var itemTypeId in itemTypes)
        {
            if (itemTypeId == 0)
                continue;

            if (entryKnown != null && !entryKnown(itemTypeId))
            {
                Logger.Error(
                    "CharacterCollections: dropping restored entry {0} for {1}: no such row in the shipped collection content",
                    itemTypeId, Owner?.Name ?? "<unknown>");
                continue;
            }

            lock (_sync)
            {
                if (_discovered.Add(itemTypeId))
                    kept++;
            }
        }

        return kept;
    }

    public void Load(MySqlConnection connection, Func<uint, bool> entryKnown = null)
    {
        if (Owner == null)
            return;

        try
        {
            var rows = new List<uint>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT `item_type_id` FROM character_collections WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    rows.Add(reader.GetUInt32("item_type_id"));
            }

            Restore(rows, entryKnown);
        }
        catch (MySqlException exception)
        {
            Logger.Error(exception,
                "CharacterCollections: could not load the discovery ledger for {0}; apply {1} ({2})",
                Owner.Name, SqlUpdateFile, exception.Message);
        }
    }

    /// <summary>
    /// Rewrites the character's discovery rows inside the caller's transaction, degrading to an error
    /// log if the feature table cannot be written.
    /// </summary>
    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        if (Owner == null)
            return;

        try
        {
            List<uint> snapshot;
            lock (_sync)
                snapshot = [.. _discovered];

            using (var delete = connection.CreateCommand())
            {
                delete.Connection = connection;
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM character_collections WHERE `owner` = @owner";
                delete.Parameters.AddWithValue("@owner", Owner.Id);
                delete.ExecuteNonQuery();
            }

            foreach (var itemTypeId in snapshot)
            {
                using var insert = connection.CreateCommand();
                insert.Connection = connection;
                insert.Transaction = transaction;
                insert.CommandText =
                    "INSERT INTO character_collections (`owner`, `item_type_id`) VALUES (@owner, @itemType)";
                insert.Parameters.AddWithValue("@owner", Owner.Id);
                insert.Parameters.AddWithValue("@itemType", itemTypeId);
                insert.ExecuteNonQuery();
            }
        }
        catch (MySqlException exception)
        {
            Logger.Error(exception,
                "CharacterCollections: could not save the discovery ledger for {0}; apply {1} ({2})",
                Owner.Name, SqlUpdateFile, exception.Message);
        }
    }
}
