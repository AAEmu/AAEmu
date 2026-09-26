namespace AAEmu.Game.Models.Game.SailingActivity;

/// <summary>One <c>game_activities</c> row: the id the packets carry as <c>activityId</c>.</summary>
public sealed class SailingActivityRow
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; init; }

    /// <summary>The authored mode, kept verbatim. Its per-value meaning is not recovered.</summary>
    public int TimeMode { get; init; }

    public string StartTimeRaw { get; init; } = string.Empty;
    public string EndTimeRaw { get; init; } = string.Empty;
    public string ServerGroupsRaw { get; init; } = string.Empty;

    /// <summary>The task group the activity selects its tasks through, or 0 when it has none.</summary>
    public int TaskGroupId { get; init; }

    /// <summary>Start instant, set only when the authored window could be resolved.</summary>
    public DateTime? StartUtc { get; init; }

    /// <summary>End instant, set only when the authored window could be resolved.</summary>
    public DateTime? EndUtc { get; init; }
}

/// <summary>One <c>game_activity_stages</c> row.</summary>
public sealed class SailingActivityStageRow
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int StageNumber { get; init; }
    public int UnlockMode { get; init; }
    public int TimeMode { get; init; }

    /// <summary>
    /// The authored unlock value, verbatim. It is text in the shipped table and a bare number in
    /// every row; what it counts is not recovered, so nothing here converts it to an instant.
    /// </summary>
    public string UnlockTimeRaw { get; init; } = string.Empty;

    /// <summary>
    /// <see cref="UnlockTimeRaw"/> as a non-negative integer. The loader refuses a value that is
    /// not a number or that is negative, so this is always populated: quietly dropping an authored
    /// value would make a stage unlock silently never fire.
    /// </summary>
    public int UnlockAmount { get; init; }

    /// <summary>0 means "no prerequisite"; any other value must name a loaded stage.</summary>
    public int PrerequisiteStageId { get; init; }
}

/// <summary>One <c>game_activity_tasks</c> row.</summary>
public sealed class SailingActivityTaskRow
{
    public int Id { get; init; }

    /// <summary>
    /// The stage this task belongs to. 0 is the table's own "unbound" value — the column defaults
    /// to 0 and every stage uses 0 for "no prerequisite" — so it is a legal state, not an error.
    /// Any other value must name a loaded stage.
    /// </summary>
    public int StageId { get; init; }

    public int TaskGroupId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// The authored task kind, verbatim. No whitelist is applied: the shipped set is wider than any
    /// documented one and the meaning of each value is not recovered.
    /// </summary>
    public int TaskType { get; init; }

    /// <summary>The authored condition kind, verbatim. Its meaning is not recovered.</summary>
    public int ConditionTypeId { get; init; }

    /// <summary>Points the task awards, as authored. Zero is a shipped value, not a missing one.</summary>
    public int RewardPoints { get; init; }

    /// <summary>True when the task pays points. Paired with <see cref="RewardPoints"/> in content.</summary>
    public bool IsPointReward { get; init; }

    public int SortOrder { get; init; }

    /// <summary>Parsed "type|item|count" rewards. Empty is legal shipped content.</summary>
    public List<(uint RewardType, uint ItemId, uint Count)> Rewards { get; init; } = [];
}

/// <summary>A shipped row that references something the catalog does not contain.</summary>
/// <param name="Table">Which table the offending row came from.</param>
/// <param name="RowId">That row's own id.</param>
/// <param name="MissingTable">The table the reference should have resolved into.</param>
/// <param name="MissingId">The value that resolved to nothing.</param>
public readonly record struct SailingActivityOrphan(string Table, int RowId, string MissingTable, int MissingId);

/// <summary>A shipped row this loader could not make sense of, with the reason.</summary>
public readonly record struct SailingActivityContentProblem(string Table, int RowId, string Reason);

/// <summary>
/// Everything the loader chose not to fail on, so the reasons stay inspectable after boot rather
/// than scrolling past in a log.
/// </summary>
public sealed class SailingActivityDiagnostics
{
    /// <summary>Rows whose reference names a table entry that is not loaded.</summary>
    public List<SailingActivityOrphan> Orphans { get; } = [];

    /// <summary>Rows that were rejected, with the reason each was rejected.</summary>
    public List<SailingActivityContentProblem> Problems { get; } = [];

    /// <summary>Activities whose authored window could not be turned into instants.</summary>
    public List<SailingActivityContentProblem> UnresolvedWindows { get; } = [];

    /// <summary>Records an orphan reference. Never throws: shipped content contains these.</summary>
    public void AddOrphan(string table, int rowId, string missingTable, int missingId)
    {
        Orphans.Add(new SailingActivityOrphan(table, rowId, missingTable, missingId));
    }

    /// <summary>Records a rejected row.</summary>
    public void AddProblem(string table, int rowId, string reason)
    {
        Problems.Add(new SailingActivityContentProblem(table, rowId, reason));
    }

    /// <summary>Records an activity whose window stays unresolved.</summary>
    public void AddUnresolvedWindow(int activityId, string reason)
    {
        UnresolvedWindows.Add(new SailingActivityContentProblem("game_activities", activityId, reason));
    }

    /// <summary>True when nothing was reported.</summary>
    public bool IsClean => Orphans.Count == 0 && Problems.Count == 0 && UnresolvedWindows.Count == 0;
}
