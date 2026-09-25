namespace AAEmu.Game.Models.Game.World.Zones;

/// <summary>
/// Typed, content-loaded values for the five <c>conflict_zones.no_kill_min_N</c> columns.
/// </summary>
/// <remarks>
/// <para>
/// The values are retained for diagnostics only. The shipped 10.0.2.13 compact databases contain
/// zero for every column, and the available native, client, and research material does not establish
/// the decay trigger, target state, or reset boundary. Applying these values to the state machine
/// would therefore be an invented mechanic.
/// </para>
/// <para>
/// The five entries correspond to the trouble states before <see cref="ZoneConflictType.Conflict"/>;
/// the count is derived from the state enum rather than duplicated as a content literal.
/// </para>
/// </remarks>
public sealed class ConflictZoneNoKillDecayMetadata
{
    public const int TroubleStateCount = (int)ZoneConflictType.Conflict;

    private readonly int[] _noKillMinutesByTroubleState;

    public ConflictZoneNoKillDecayMetadata(ushort zoneGroupId, IEnumerable<int> noKillMinutesByTroubleState)
    {
        if (noKillMinutesByTroubleState == null)
            throw new ArgumentNullException(nameof(noKillMinutesByTroubleState));

        var values = noKillMinutesByTroubleState.ToArray();
        if (values.Length != TroubleStateCount)
        {
            throw new InvalidOperationException(
                $"Conflict zone {zoneGroupId} requires exactly {TroubleStateCount} no-kill metadata values; found {values.Length}.");
        }

        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] < 0)
            {
                throw new InvalidOperationException(
                    $"Conflict zone {zoneGroupId} has a negative no-kill metadata value at trouble state {i}: {values[i]}.");
            }
        }

        ZoneGroupId = zoneGroupId;
        _noKillMinutesByTroubleState = values;
    }

    public ushort ZoneGroupId { get; }

    /// <summary>Authored values, indexed by the trouble state's enum value.</summary>
    public IReadOnlyList<int> NoKillMinutesByTroubleState => _noKillMinutesByTroubleState;

    /// <summary>
    /// True when at least one authored value is non-zero. This does not imply that decay is applied;
    /// it is a diagnostic signal for content that would need further behavioral evidence.
    /// </summary>
    public bool IsConfigured => _noKillMinutesByTroubleState.Any(value => value != 0);

    public int GetMinutes(ZoneConflictType troubleState)
    {
        if (troubleState is < ZoneConflictType.Tension or >= ZoneConflictType.Conflict)
        {
            throw new ArgumentOutOfRangeException(
                nameof(troubleState),
                troubleState,
                "No-kill metadata is defined only for trouble states before Conflict.");
        }

        return _noKillMinutesByTroubleState[(int)troubleState];
    }

    public string ToDiagnostic() =>
        $"zone={ZoneGroupId} no_kill_min={string.Join(',', _noKillMinutesByTroubleState)} configured={IsConfigured} application=deferred";
}
