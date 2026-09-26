using System.Data;
using System.Data.Common;

using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models.Game.Sieges;

using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// The database side of a siege: the live score counters, the settled outcomes, and the dominion change a
/// settlement makes.
/// </summary>
/// <remarks>
/// A settlement is three writes - the outcome row, the zeroed counters, and the dominion's new owner - and
/// they land in one transaction. A siege that recorded its outcome but never moved the dominion (or the
/// reverse) would leave the database claiming a winner nobody was given, and the phase tick that triggered it
/// has already moved on, so there would be nothing to retry. <see cref="Settle"/> is the only place that
/// writes all three, and it reports the outcome it actually stored.
/// </remarks>
public interface ISiegeScoreStore
{
    /// <summary>The zone group's running counters; a zone group that has never been scored reads as zero.</summary>
    SiegeScoreState Read(ushort zoneGroupId);

    /// <summary>
    /// Adds one award and returns the counters as they now stand. The row is created on first use.
    /// </summary>
    SiegeScoreState Add(ushort zoneGroupId, SiegeScoreSide side, uint amount);

    /// <summary>Zeroes the counters for the next cycle.</summary>
    void Reset(ushort zoneGroupId);

    /// <summary>
    /// Records how the siege ended, zeroes the counters and applies the dominion change, in one transaction.
    /// </summary>
    /// <returns>
    /// The outcome that is on record afterwards. When this cycle was already recorded - a repeated tick, or a
    /// restart between the write and the phase update - the <em>stored</em> outcome comes back rather than the
    /// one passed in, so a retry converges on what was decided instead of deciding again from counters that
    /// have since been zeroed.
    /// </returns>
    SiegeSettlementRecord Settle(SiegeSettlementRecord record);
}

/// <summary>
/// The MySQL store. The statements are written in the SQL both MySQL and SQLite accept, so the tests run the
/// same text against an in-memory database rather than a copy of it.
/// </summary>
public sealed class MySqlSiegeScoreStore : ISiegeScoreStore
{
    /// <summary>MySQL's ER_DUP_ENTRY, the error a unique key raises on a second insert.</summary>
    private const int DuplicateKeyErrorNumber = 1062;

    /// <summary>SQLite's constraint errors, which is how its unique key answers a second insert.</summary>
    private static readonly int[] SqliteDuplicateCodes =
    [
        19,   // SQLITE_CONSTRAINT
        1555, // SQLITE_CONSTRAINT_PRIMARYKEY
        2067  // SQLITE_CONSTRAINT_UNIQUE
    ];

    private readonly Func<DbConnection> _connectionFactory;

    public MySqlSiegeScoreStore() : this(MySQL.CreateConnection)
    {
    }

    internal MySqlSiegeScoreStore(Func<DbConnection> connectionFactory) => _connectionFactory = connectionFactory;

    public SiegeScoreState Read(ushort zoneGroupId)
    {
        using var connection = _connectionFactory();
        return Read(connection, null, zoneGroupId);
    }

    public SiegeScoreState Add(ushort zoneGroupId, SiegeScoreSide side, uint amount)
    {
        var column = ColumnFor(side);
        using var connection = _connectionFactory();
        using var transaction = connection.BeginTransaction();

        // The next value is worked out here rather than in SQL, so it uses the same saturating arithmetic the
        // rest of the feature does and the write itself is plain SQL both providers understand.
        var next = Read(connection, transaction, zoneGroupId).Plus(side, amount);

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            // The column name is one of three named constants, never a request value.
            command.CommandText = $"UPDATE siege_scores SET {column} = @value WHERE zone_id = @z";
            Add(command, "@value", next.For(side));
            Add(command, "@z", zoneGroupId);
            if (command.ExecuteNonQuery() == 0)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = $"INSERT INTO siege_scores (zone_id, {column}) VALUES (@z, @value)";
                Add(insert, "@value", next.For(side));
                Add(insert, "@z", zoneGroupId);
                insert.ExecuteNonQuery();
            }
        }

        // Read back rather than trusting what was written: the totals a caller publishes are the totals the
        // database holds.
        var stored = Read(connection, transaction, zoneGroupId);
        transaction.Commit();
        return stored;
    }

    public void Reset(ushort zoneGroupId)
    {
        using var connection = _connectionFactory();
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                "UPDATE siege_scores SET outlaw_point = 0, defense_point = 0, offense_point = 0 WHERE zone_id = @z";
            Add(command, "@z", zoneGroupId);
            if (command.ExecuteNonQuery() == 0)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText =
                    "INSERT INTO siege_scores (zone_id, outlaw_point, defense_point, offense_point) VALUES (@z, 0, 0, 0)";
                Add(insert, "@z", zoneGroupId);
                insert.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    public SiegeSettlementRecord Settle(SiegeSettlementRecord record)
    {
        using var connection = _connectionFactory();
        using var transaction = connection.BeginTransaction();

        var onRecord = ReadSettlement(connection, transaction, record.ZoneGroupId, record.CycleWeekStart);
        if (onRecord != null)
        {
            transaction.Rollback();
            return onRecord;
        }

        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO siege_settlements
                    (zone_id, cycle_week_start, settled_at, outlaw_point, defense_point, offense_point,
                     outcome, defender_faction_id, winner_faction_id, reason)
                VALUES (@z, @cycle, @settled, @outlaw, @defense, @offense, @outcome, @defender, @winner, @reason)
                """;
            Add(command, "@z", record.ZoneGroupId);
            Add(command, "@cycle", record.CycleWeekStart);
            Add(command, "@settled", record.SettledAtUtc);
            Add(command, "@outlaw", record.Score.OutlawPoint);
            Add(command, "@defense", record.Score.DefensePoint);
            Add(command, "@offense", record.Score.OffensePoint);
            Add(command, "@outcome", (byte)record.Outcome);
            Add(command, "@defender", record.DefenderFactionId);
            Add(command, "@winner", record.WinnerFactionId);
            Add(command, "@reason", record.Reason);
            command.ExecuteNonQuery();
        }
        catch (DbException ex) when (IsDuplicate(ex))
        {
            // Another attempt at this cycle won the race, and its outcome is the one that stands.
            transaction.Rollback();
            return ReadSettlement(record.ZoneGroupId, record.CycleWeekStart)
                   ?? throw new InvalidOperationException(
                       $"Siege settlement for zone group {record.ZoneGroupId} cycle {record.CycleWeekStart:yyyy-MM-dd} " +
                       "already exists but cannot be read back.", ex);
        }

        using (var reset = connection.CreateCommand())
        {
            reset.Transaction = transaction;
            reset.CommandText =
                "UPDATE siege_scores SET outlaw_point = 0, defense_point = 0, offense_point = 0 WHERE zone_id = @z";
            Add(reset, "@z", record.ZoneGroupId);
            reset.ExecuteNonQuery();
        }

        using (var dominion = connection.CreateCommand())
        {
            dominion.Transaction = transaction;
            // The siege ended either way, so the siege-end clock moves on. The reign start clock does
            // NOT: it records when the current owner took the claim, and a defended siege leaves that
            // owner in place. Moving it here would make the database drift away from the in-memory
            // dominion, and the moved date would come back for the client (X2Dominion:GetReignStartDate)
            // on the next restart. An alliance that broke through DOES take the claim, and a settled
            // dominion belongs to a nation rather than to the guild that declared it last cycle, so
            // the guild link goes with it -- and that branch is the only one that starts a new reign.
            if (record.WinnerFactionId == 0)
            {
                dominion.CommandText =
                    "UPDATE dominions SET last_siege_end_time = @settled WHERE zone_id = @z";
            }
            else
            {
                dominion.CommandText =
                    """
                    UPDATE dominions
                    SET expedition_id = 0, faction_id = @winner,
                        last_siege_end_time = @settled, reign_start_time = @settled
                    WHERE zone_id = @z
                    """;
                Add(dominion, "@winner", record.WinnerFactionId);
            }

            Add(dominion, "@settled", record.SettledAtUtc);
            Add(dominion, "@z", record.ZoneGroupId);
            if (dominion.ExecuteNonQuery() == 0)
                throw new InvalidOperationException(
                    $"Zone group {record.ZoneGroupId} has no dominions row to settle; the siege outcome was not recorded.");
        }

        transaction.Commit();
        return record;
    }

    private SiegeSettlementRecord? ReadSettlement(ushort zoneGroupId, DateTime cycleWeekStart)
    {
        using var connection = _connectionFactory();
        return ReadSettlement(connection, null, zoneGroupId, cycleWeekStart);
    }

    private static SiegeScoreState Read(DbConnection connection, DbTransaction? transaction, ushort zoneGroupId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT outlaw_point, defense_point, offense_point FROM siege_scores WHERE zone_id=@z";
        Add(command, "@z", zoneGroupId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return SiegeScoreState.Empty(zoneGroupId);

        return new SiegeScoreState
        {
            ZoneGroupId = zoneGroupId,
            OutlawPoint = Convert.ToUInt32(reader.GetValue(0)),
            DefensePoint = Convert.ToUInt32(reader.GetValue(1)),
            OffensePoint = Convert.ToUInt32(reader.GetValue(2)),
        };
    }

    private static SiegeSettlementRecord? ReadSettlement(
        DbConnection connection, DbTransaction? transaction, ushort zoneGroupId, DateTime cycleWeekStart)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT zone_id, cycle_week_start, settled_at, outlaw_point, defense_point, offense_point,
                   outcome, defender_faction_id, winner_faction_id, reason
            FROM siege_settlements WHERE zone_id=@z AND cycle_week_start=@cycle
            """;
        Add(command, "@z", zoneGroupId);
        Add(command, "@cycle", cycleWeekStart);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        var zoneGroup = (ushort)Convert.ToUInt32(reader.GetValue(0));
        return new SiegeSettlementRecord
        {
            ZoneGroupId = zoneGroup,
            CycleWeekStart = Convert.ToDateTime(reader.GetValue(1)),
            SettledAtUtc = Convert.ToDateTime(reader.GetValue(2)),
            Score = new SiegeScoreState
            {
                ZoneGroupId = zoneGroup,
                OutlawPoint = Convert.ToUInt32(reader.GetValue(3)),
                DefensePoint = Convert.ToUInt32(reader.GetValue(4)),
                OffensePoint = Convert.ToUInt32(reader.GetValue(5)),
            },
            Outcome = (SiegeOutcome)Convert.ToByte(reader.GetValue(6)),
            DefenderFactionId = Convert.ToUInt32(reader.GetValue(7)),
            WinnerFactionId = Convert.ToUInt32(reader.GetValue(8)),
            Reason = Convert.ToString(reader.GetValue(9)) ?? string.Empty,
        };
    }

    /// <summary>
    /// A parameter added by name through the provider-neutral API, so the same statement text works on MySQL
    /// and on the SQLite the tests run it against.
    /// </summary>
    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static bool IsDuplicate(DbException ex) => ex switch
    {
        MySqlException mySql => mySql.Number == DuplicateKeyErrorNumber,
        SqliteException sqlite => SqliteDuplicateCodes.Contains(sqlite.SqliteErrorCode),
        _ => false,
    };

    private static string ColumnFor(SiegeScoreSide side) => side switch
    {
        SiegeScoreSide.Defense => "defense_point",
        SiegeScoreSide.Offense => "offense_point",
        SiegeScoreSide.Outlaw => "outlaw_point",
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown siege score side."),
    };
}
