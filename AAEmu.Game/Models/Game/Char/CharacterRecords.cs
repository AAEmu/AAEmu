using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// The character's progress against the record definitions in <c>char_records</c>.
/// </summary>
/// <remarks>
/// <para>
/// A record definition names a kind and its parameters (<c>char_records.kind_id</c>,
/// <c>value1</c>, <c>value2</c>); the value a character has reached for it is the only thing stored here.
/// Gameplay events report record values and the achievement side asks what a record is worth now, which is
/// what keeps the two apart: nothing in this class knows an achievement exists.
/// </para>
/// <para>
/// The store keeps the highest value ever reported for a record. Records describe things that can go down as
/// well as up — ability levels fall on a respec — and an achievement that has been earned is not taken back,
/// so the high-water mark is what an achievement is allowed to look at. An explicit reset is a separate call
/// (<see cref="Clear"/>), which is what the achievement reset path uses.
/// </para>
/// </remarks>
public sealed class CharacterRecords
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly object _sync = new();
    private readonly Dictionary<uint, int> _values = [];

    public CharacterRecords(Character owner)
    {
        Owner = owner;
    }

    public Character Owner { get; }

    /// <summary>The value this character has reached for a record, or zero when it never reported one.</summary>
    public int Get(uint recordId)
    {
        lock (_sync)
            return _values.GetValueOrDefault(recordId);
    }

    /// <summary>
    /// Reports a record's current value, keeping whichever of the two is higher.
    /// </summary>
    /// <returns>The value the record now holds.</returns>
    public int Report(uint recordId, int value)
    {
        lock (_sync)
        {
            var kept = _values.GetValueOrDefault(recordId);
            if (value <= kept)
                return kept;

            _values[recordId] = value;
            return value;
        }
    }

    /// <summary>Sets a record outright, lower values included — the reset path, not the gameplay path.</summary>
    public void Set(uint recordId, int value)
    {
        lock (_sync)
            _values[recordId] = value;
    }

    /// <summary>Forgets a record entirely, so the next report starts from zero.</summary>
    public void Clear(uint recordId)
    {
        lock (_sync)
            _values.Remove(recordId);
    }

    /// <summary>A copy of every record the character has a value for, for save and for the GM surface.</summary>
    public IReadOnlyDictionary<uint, int> Snapshot()
    {
        lock (_sync)
            return new Dictionary<uint, int>(_values);
    }

    public void Load(MySqlConnection connection)
    {
        if (Owner == null)
            return;

        try
        {
            var loaded = new Dictionary<uint, int>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT `record_id`, `value` FROM character_records WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    loaded[reader.GetUInt32("record_id")] = reader.GetInt32("value");
            }

            lock (_sync)
            {
                _values.Clear();
                foreach (var (recordId, value) in loaded)
                    _values[recordId] = value;
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to load records for {0}", Owner.Name);
        }
    }

    /// <summary>
    /// Rewrites the character's record rows inside the caller's transaction.
    /// </summary>
    /// <remarks>
    /// Nothing here catches: the rows are written in <see cref="Character.Save"/>'s transaction, after the
    /// rows have been deleted, so a failure that stayed quiet would let the save report success and commit
    /// with the character's progress gone. The exception has to reach the save so it can roll back.
    /// </remarks>
    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        if (Owner == null)
            return;

        Dictionary<uint, int> snapshot;
        lock (_sync)
            snapshot = new Dictionary<uint, int>(_values);

        using var delete = connection.CreateCommand();
        delete.Connection = connection;
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM character_records WHERE `owner` = @owner";
        delete.Parameters.AddWithValue("@owner", Owner.Id);
        delete.ExecuteNonQuery();

        foreach (var (recordId, value) in snapshot)
        {
            using var insert = connection.CreateCommand();
            insert.Connection = connection;
            insert.Transaction = transaction;
            insert.CommandText =
                "INSERT INTO character_records (`owner`, `record_id`, `value`) VALUES (@owner, @record, @value)";
            insert.Parameters.AddWithValue("@owner", Owner.Id);
            insert.Parameters.AddWithValue("@record", recordId);
            insert.Parameters.AddWithValue("@value", value);
            insert.ExecuteNonQuery();
        }
    }
}
