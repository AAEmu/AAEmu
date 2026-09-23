using System.Globalization;
using System.Text.RegularExpressions;

namespace AAEmu.Game.Models.Game.Residents;

/// <summary>One <c>local_development_boards</c> row.</summary>
/// <remarks>
/// <paramref name="Threshold"/> is the single ASCII digit-run parsed out of <c>show_text</c> at
/// load — the text around it is locale prose and never classified on. <c>null</c> means the row
/// carried no digit-run and was skipped loudly at load; it never becomes a number.
/// </remarks>
public sealed record LocalDevelopmentBoardRow(uint RowId, uint BoardTypeId, uint ShowPhase, uint? Threshold);

/// <summary>One <c>local_developments</c> row joined with its board rows.</summary>
public sealed class LocalDevelopmentDefinition
{
    public uint Id { get; init; }
    public ushort ZoneGroupId { get; init; }
    public uint DoodadAlmightyId { get; init; }
    public uint BoardDoodadId { get; init; }

    /// <summary>Raw <c>doodad_phase_0..3</c> func-group ids; a value &lt;= 0 means the content did not define it.</summary>
    public int[] DoodadPhases { get; init; } = [];

    public List<LocalDevelopmentBoardRow> BoardRows { get; } = [];
}

/// <summary>The phase plan for one contribution total: what the world should be showing.</summary>
/// <remarks>
/// <see cref="DoodadPhase"/>/<see cref="BoardPhase"/> are func-group ids; null means the content
/// has nothing to apply there (undefined phase, or no board threshold crossed yet).
/// </remarks>
public sealed record LocalDevelopmentPlan(uint Contribution, uint Level, uint? DoodadPhase, uint? BoardPhase);

/// <summary>
/// The local-development state machine, pure: contribution -&gt; distinct board thresholds crossed
/// -&gt; development level -&gt; doodad/board func-group phases. Every number comes from the content
/// rows handed in; this class holds no gameplay values of its own.
/// </summary>
public static class LocalDevelopmentRules
{
    /// <summary>
    /// The threshold digit-run in <c>show_text</c>. Explicitly ASCII <c>[0-9]</c>: .NET's
    /// <c>\d</c> matches any Unicode digit and the surrounding locale prose must never leak in.
    /// </summary>
    private static readonly Regex ThresholdRun = new("[0-9]+", RegexOptions.Compiled);

    /// <summary>The threshold a board row announces, or null when the text carries no ASCII digit-run.</summary>
    public static uint? ParseThreshold(string showText)
    {
        if (string.IsNullOrEmpty(showText))
            return null;
        var match = ThresholdRun.Match(showText);
        if (!match.Success)
            return null;
        return uint.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var threshold)
            ? threshold
            : null;
    }

    /// <summary>The distinct thresholds of a development, ascending. Duplicated thresholds (two board types announcing the same count) count once.</summary>
    public static List<uint> DistinctSortedThresholds(LocalDevelopmentDefinition definition)
    {
        return definition.BoardRows
            .Where(row => row.Threshold != null)
            .Select(row => row.Threshold!.Value)
            .Distinct()
            .OrderBy(threshold => threshold)
            .ToList();
    }

    /// <summary>Development level = how many distinct thresholds the contribution has crossed.</summary>
    public static uint LevelFor(uint contribution, IReadOnlyList<uint> sortedThresholds)
    {
        uint level = 0;
        foreach (var threshold in sortedThresholds)
        {
            if (contribution < threshold)
                break;
            level++;
        }
        return level;
    }

    /// <summary>The <c>doodad_phase_N</c> func group for a level, or null when the content left it undefined (-1 / out of range).</summary>
    public static uint? PhaseForLevel(LocalDevelopmentDefinition definition, uint level)
    {
        if (level >= definition.DoodadPhases.Length)
            return null;
        var phase = definition.DoodadPhases[level];
        return phase > 0 ? (uint)phase : null;
    }

    /// <summary>
    /// The board <c>show_phase</c> to display at a contribution total: the row of the highest
    /// crossed threshold (ties broken by board row id — table order). Level 0 touches no board.
    /// </summary>
    public static uint? BoardPhaseFor(LocalDevelopmentDefinition definition, uint contribution, uint level)
    {
        if (level == 0)
            return null;
        LocalDevelopmentBoardRow best = null;
        foreach (var row in definition.BoardRows)
        {
            if (row.Threshold == null || contribution < row.Threshold.Value)
                continue;
            if (best == null ||
                row.Threshold.Value > best.Threshold!.Value ||
                (row.Threshold.Value == best.Threshold.Value && row.RowId > best.RowId))
            {
                best = row;
            }
        }
        return best?.ShowPhase;
    }

    /// <summary>Evaluate the whole ladder for one development and contribution total.</summary>
    public static LocalDevelopmentPlan Evaluate(LocalDevelopmentDefinition definition, uint contribution)
    {
        var level = LevelFor(contribution, DistinctSortedThresholds(definition));
        return new LocalDevelopmentPlan(
            contribution,
            level,
            PhaseForLevel(definition, level),
            BoardPhaseFor(definition, contribution, level));
    }

    /// <summary>True when the spawned doodad is not already in the target phase — the only time DoChangePhase (and its broadcast) may run.</summary>
    public static bool ShouldChangePhase(uint currentFuncGroupId, uint targetPhase) =>
        currentFuncGroupId != targetPhase;
}
