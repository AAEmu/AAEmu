using AAEmu.Game.Models.Game.Sagas;

namespace AAEmu.Game.Models.Game.Milestones;

/// <summary>Persisted row of character_milestones (owner column is bound by the caller).</summary>
public sealed record MilestoneProgressRow(uint MilestoneId, MilestoneStatus Status, ushort CompletedCount);

/// <summary>
/// What one evaluation changed, so the caller knows whether to sync.
/// <see cref="Granted"/> is true only for the call that took the grant — the exactly-once edge,
/// same shape as <c>SagaProgressChange</c>.
/// </summary>
public sealed record MilestoneProgressChange(
    uint MilestoneId,
    bool StatusChanged,
    MilestoneStatus PreviousStatus,
    MilestoneStatus CurrentStatus,
    ushort CompletedCount,
    bool Granted);

/// <summary>
/// Pure per-character milestone progression, keyed onto GF-W13's grant-once ledger: advancement
/// comes from the reversed quest → milestone trigger index in <see cref="MilestoneCatalog"/>,
/// eligibility from the milestone row's own release/window columns, and the grant is taken
/// through <c>character_saga_reward_grants</c> keyed by milestone id under
/// <see cref="GrantScope"/> — never a private second ledger. No I/O: persistence rows and wire
/// packets are the owner's concern; the catalog and the clock are always passed in, so tests run
/// against injected content and time. Counting comes from the completed-quest ground truth, never
/// from the event itself, so a replayed trigger cannot double-count.
/// </summary>
public sealed class MilestoneProgressState
{
    /// <summary>
    /// saga_quest_group_id scope for milestone grants in character_saga_reward_grants: milestone
    /// grants are not tied to a saga group, and shipped saga group ids start at 1, so 0 is free.
    /// A named sentinel, never a content value.
    /// </summary>
    public const uint GrantScope = 0;

    private readonly Dictionary<uint, MilestoneProgressRow> _records = [];
    private readonly SagaProgressState _ledger;

    public MilestoneProgressState(SagaProgressState ledger)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
    }

    public bool TryGetRecord(uint milestoneId, out MilestoneProgressRow record) =>
        _records.TryGetValue(milestoneId, out record);

    /// <summary>Whether this milestone's completion grant was already taken on the shared ledger.</summary>
    public bool HasGrant(uint milestoneId) => _ledger.HasGrant(GrantScope, milestoneId);

    /// <summary>
    /// Reversed trigger: a finished quest walks back to the milestone its content row names and
    /// re-evaluates that milestone against the quest-completion ground truth. Null means nothing
    /// changed — the quest is not milestone-tagged, the milestone is not eligible right now
    /// (unreleased content or outside the row's own window), no member of its chain has finished
    /// yet, or the state has already converged. Replays are therefore free.
    /// </summary>
    public MilestoneProgressChange OnQuestCompleted(
        MilestoneCatalog catalog,
        uint questId,
        Func<uint, bool> isQuestCompleted,
        DateTime nowUtc)
    {
        var milestoneId = catalog.FindMilestoneByQuest(questId);
        if (milestoneId == 0)
            return null;

        return Evaluate(catalog, milestoneId, isQuestCompleted, nowUtc);
    }

    /// <summary>
    /// Re-evaluates every milestone the content carries — the login catch-up path for characters
    /// whose quests were finished before this state existed (no record is created without
    /// progress, and a fully-finished chain found here takes its grant exactly once). A recorded
    /// milestone whose window has since closed is left exactly as it is: records never rewind and
    /// grants never reopen, the milestone simply stops advancing until its row is eligible again.
    /// </summary>
    public IReadOnlyList<MilestoneProgressChange> Reconcile(
        MilestoneCatalog catalog,
        Func<uint, bool> isQuestCompleted,
        DateTime nowUtc)
    {
        // Content rows first, then any record the content no longer carries: Evaluate refuses
        // that combination loudly instead of letting a ghost milestone converge silently.
        var ids = catalog.Rows
            .Select(row => row.Id)
            .Concat(_records.Keys)
            .Distinct()
            .OrderBy(id => id);

        List<MilestoneProgressChange> changes = null;
        foreach (var milestoneId in ids)
        {
            var change = Evaluate(catalog, milestoneId, isQuestCompleted, nowUtc);
            if (change is null)
                continue;

            changes ??= [];
            changes.Add(change);
        }

        return changes ?? [];
    }

    private MilestoneProgressChange Evaluate(
        MilestoneCatalog catalog,
        uint milestoneId,
        Func<uint, bool> isQuestCompleted,
        DateTime nowUtc)
    {
        _records.TryGetValue(milestoneId, out var record);

        if (!catalog.TryGetRow(milestoneId, out _))
        {
            // A record for content that no longer exists refuses to converge: fail loud instead of
            // silently keeping a ghost milestone. A trigger for such content cannot reach here —
            // the loader already rejected the trigger and reported it — so no record means the
            // milestone simply is not there.
            if (record is null)
                return null;
            throw new InvalidOperationException(
                $"Milestone {milestoneId} has a character record but no content row");
        }

        // Eligibility is the milestone row's own condition: unreleased content or a closed window
        // means this milestone does not advance. An existing record is frozen, never rewound.
        if (!catalog.IsEligible(milestoneId, nowUtc))
            return null;

        var chain = catalog.GetQuestChain(milestoneId);
        var completedCount = (ushort)Math.Clamp(chain.Count(isQuestCompleted), 0, ushort.MaxValue);
        // A chainless milestone can never complete — finishing it out of thin air would grant a
        // reward for content that is not there (same rule as GF-W13's empty saga groups).
        var complete = chain.Count > 0 && completedCount == chain.Count;
        var status = complete
            ? MilestoneStatus.Complete
            : record?.Status ?? MilestoneStatus.Active;

        var previousStatus = record?.Status ?? MilestoneStatus.Active;
        var statusChanged = record is null ? complete : status != record.Status;
        var countChanged = record is null ? completedCount > 0 : completedCount != record.CompletedCount;

        var granted = false;
        if (status == MilestoneStatus.Complete)
            granted = _ledger.TryBeginGrant(GrantScope, milestoneId);

        if (!statusChanged && !countChanged && !granted)
            return null;

        _records[milestoneId] = new MilestoneProgressRow(milestoneId, status, completedCount);
        return new MilestoneProgressChange(
            milestoneId, statusChanged, previousStatus, status, completedCount, granted);
    }

    /// <summary>Snapshot rows for persistence, in milestone id order.</summary>
    public IReadOnlyList<MilestoneProgressRow> ExportRows() =>
        _records.Values.OrderBy(row => row.MilestoneId).ToList();

    /// <summary>
    /// Restores persisted rows, replacing anything held now — load runs on a fresh instance, and
    /// replace semantics keep save → reload → save byte-stable.
    /// </summary>
    public void Import(IEnumerable<MilestoneProgressRow> rows)
    {
        _records.Clear();
        foreach (var row in rows)
            _records[row.MilestoneId] = row;
    }
}
