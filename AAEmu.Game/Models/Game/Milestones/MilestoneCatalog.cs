namespace AAEmu.Game.Models.Game.Milestones;

/// <summary>
/// One <c>milestones</c> row: the release-window columns that decide whether the milestone may
/// advance. <c>name</c>, <c>memo</c> and <c>merge_date</c> are authoring/display metadata and are
/// deliberately not loaded — no condition here is decided from display text.
/// </summary>
/// <param name="Id">milestones.id.</param>
/// <param name="StartDate">milestones.start_date, normalized to UTC at load (window opens).</param>
/// <param name="EndDate">milestones.end_date, normalized to UTC at load (window closes).</param>
/// <param name="Release">milestones.release — 't' means the batch shipped; unreleased rows never advance.</param>
public sealed record MilestoneRow(uint Id, DateTime StartDate, DateTime EndDate, bool Release);

/// <summary>
/// A raw (quest → milestone) trigger read from content. The direction is "reversed": content rows
/// point at their milestone, so a quest completion event is walked back to the milestone it
/// advances instead of the milestone pushing onto quests.
/// </summary>
public sealed record MilestoneTriggerRow(uint QuestId, uint MilestoneId);

/// <summary>
/// A trigger or row the loader could not honor, kept so load reports it loudly instead of
/// dropping it silently (same contract as <c>SagaContentIssue</c>).
/// </summary>
public sealed record MilestoneContentIssue(uint QuestId, uint MilestoneId, string Reason);

/// <summary>
/// Validated milestone content: every shipped milestone row plus the reversed
/// quest → milestone trigger index. Built once by <c>MilestoneGameData</c> (or a test) from raw
/// rows; pure afterwards.
/// </summary>
public sealed class MilestoneCatalog
{
    public static readonly MilestoneCatalog Empty = Build([], []);

    private readonly Dictionary<uint, MilestoneRow> _rowsById;
    private readonly Dictionary<uint, uint> _milestoneByQuestId;
    private readonly Dictionary<uint, List<uint>> _questIdsByMilestoneId;

    private MilestoneCatalog(
        IReadOnlyList<MilestoneRow> rows,
        IReadOnlyList<MilestoneContentIssue> issues,
        Dictionary<uint, uint> milestoneByQuestId,
        Dictionary<uint, List<uint>> questIdsByMilestoneId)
    {
        Rows = rows;
        Issues = issues;
        _milestoneByQuestId = milestoneByQuestId;
        _questIdsByMilestoneId = questIdsByMilestoneId;
        _rowsById = rows.ToDictionary(row => row.Id);
    }

    /// <summary>All milestone rows ordered by id — the shipped row order.</summary>
    public IReadOnlyList<MilestoneRow> Rows { get; }

    /// <summary>Rows or triggers that failed validation; each is reported at load time.</summary>
    public IReadOnlyList<MilestoneContentIssue> Issues { get; }

    public bool TryGetRow(uint milestoneId, out MilestoneRow row) => _rowsById.TryGetValue(milestoneId, out row);

    /// <summary>Fail loud: an id the content does not carry is a bug, not an empty state.</summary>
    public MilestoneRow GetRow(uint milestoneId) =>
        _rowsById.TryGetValue(milestoneId, out var row)
            ? row
            : throw new InvalidOperationException($"Milestone {milestoneId} does not exist in content");

    /// <summary>Whether the content carries a milestone under this id — the grant-ledger check.</summary>
    public bool Contains(uint milestoneId) => _rowsById.ContainsKey(milestoneId);

    /// <summary>The milestone a completed quest advances through its reversed trigger, or 0 when
    /// the quest is not milestone-tagged (the common case: most quests carry no milestone).</summary>
    public uint FindMilestoneByQuest(uint questId) => _milestoneByQuestId.GetValueOrDefault(questId);

    /// <summary>The trigger chain for a milestone: every quest whose completion advances it,
    /// in ascending quest id order (shipped table order).</summary>
    public IReadOnlyList<uint> GetQuestChain(uint milestoneId) =>
        _questIdsByMilestoneId.TryGetValue(milestoneId, out var chain) ? chain : [];

    /// <summary>
    /// Eligibility straight from the milestone row, evaluated against the caller's UTC clock:
    /// released content only, and only inside the row's own [start_date, end_date] window.
    /// A missing row is not eligible — callers that need the loud half use <see cref="GetRow"/>.
    /// </summary>
    public bool IsEligible(uint milestoneId, DateTime nowUtc)
    {
        if (!_rowsById.TryGetValue(milestoneId, out var row))
            return false;

        var now = ServerCalendar.AsUtc(nowUtc);
        return row.Release && now >= row.StartDate && now <= row.EndDate;
    }

    /// <summary>
    /// Validates raw rows into a catalog. Duplicate milestone ids, triggers pointing at missing
    /// milestone rows, and rows with unusable dates are rejected and recorded as issues;
    /// a trigger whose quest is re-tagged keeps the first occurrence so the index stays total.
    /// </summary>
    public static MilestoneCatalog Build(
        IEnumerable<MilestoneRow> rows,
        IEnumerable<MilestoneTriggerRow> triggers)
    {
        var issues = new List<MilestoneContentIssue>();
        var orderedRows = new List<MilestoneRow>();
        var seenIds = new HashSet<uint>();
        foreach (var row in rows.OrderBy(row => row.Id))
        {
            if (!seenIds.Add(row.Id))
            {
                issues.Add(new MilestoneContentIssue(0, row.Id, "duplicate milestone id"));
                continue;
            }

            if (row.EndDate < row.StartDate)
            {
                issues.Add(new MilestoneContentIssue(
                    0, row.Id, "end_date precedes start_date — window unusable, row dropped"));
                continue;
            }

            orderedRows.Add(row);
        }

        var milestoneByQuestId = new Dictionary<uint, uint>();
        var questIdsByMilestoneId = new Dictionary<uint, List<uint>>();
        var knownIds = orderedRows.Select(row => row.Id).ToHashSet();
        foreach (var trigger in triggers)
        {
            if (!knownIds.Contains(trigger.MilestoneId))
            {
                issues.Add(new MilestoneContentIssue(
                    trigger.QuestId, trigger.MilestoneId, "milestones row missing"));
                continue;
            }

            if (milestoneByQuestId.TryAdd(trigger.QuestId, trigger.MilestoneId))
            {
                if (!questIdsByMilestoneId.TryGetValue(trigger.MilestoneId, out var chain))
                    questIdsByMilestoneId[trigger.MilestoneId] = chain = [];
                chain.Add(trigger.QuestId);
            }
            else
            {
                issues.Add(new MilestoneContentIssue(
                    trigger.QuestId, trigger.MilestoneId, "quest already tagged by an earlier trigger"));
            }
        }

        foreach (var chain in questIdsByMilestoneId.Values)
            chain.Sort();

        return new MilestoneCatalog(orderedRows, issues, milestoneByQuestId, questIdsByMilestoneId);
    }
}
