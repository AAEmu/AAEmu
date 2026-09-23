namespace AAEmu.Game.Models.Game.Sagas;

/// <summary>Persisted row of character_saga_groups (owner column is bound by the caller).</summary>
public sealed record SagaGroupProgressRow(uint GroupId, SagaGroupStatus Status, ushort CompletedCount);

/// <summary>Persisted row of character_saga_reward_grants (owner column is bound by the caller).</summary>
public sealed record SagaRewardGrantRow(uint GroupId, uint GrantKey);

/// <summary>
/// What one evaluation changed, so the caller knows which packets to push. Null-returning APIs mean
/// nothing changed. <see cref="RewardGranted"/> is true only on the call that took the grant — the
/// exactly-once edge GF-W14 hooks as well.
/// </summary>
public sealed record SagaProgressChange(
    uint GroupId,
    bool StatusChanged,
    SagaGroupStatus PreviousStatus,
    SagaGroupStatus CurrentStatus,
    ushort CompletedCount,
    bool RewardGranted,
    uint RewardGrantKey);

/// <summary>
/// Pure per-character saga-group progression: which groups are bought (eligible), how far each has
/// advanced, and which rewards were granted. No I/O — persistence rows and wire packets are the
/// owner's concern; the catalog is always passed in, so tests run against injected content.
/// The grant ledger is append-only: a status can move back if a member quest is reset, but a
/// reward that was granted is never granted again.
/// </summary>
public sealed class SagaProgressState
{
    private readonly Dictionary<uint, SagaGroupProgressRow> _groups = [];
    private readonly HashSet<(uint GroupId, uint GrantKey)> _grants = [];

    /// <summary>True when the character holds the group's chronicle info record (bought = eligible).</summary>
    public bool IsUnlocked(uint groupId) => _groups.ContainsKey(groupId);

    public bool TryGetRecord(uint groupId, out SagaGroupProgressRow record) =>
        _groups.TryGetValue(groupId, out record);

    public bool HasGrant(uint groupId, uint grantKey) => _grants.Contains((groupId, grantKey));

    /// <summary>
    /// Eligibility: a quest outside every saga group is always allowed; a saga member requires the
    /// group's record to exist, otherwise the accept must fail with ChronicleInfoNeed.
    /// </summary>
    public SagaStartGate EvaluateStartGate(SagaQuestCatalog catalog, uint questId)
    {
        var group = catalog.FindGroupByQuest(questId);
        if (group is null)
            return SagaStartGate.Allowed;

        return _groups.ContainsKey(group.Id)
            ? SagaStartGate.Allowed
            : SagaStartGate.ChronicleInfoNeed;
    }

    /// <summary>Creates the chronicle info record at status Active; idempotent.</summary>
    public SagaUnlockResult Unlock(SagaQuestCatalog catalog, uint groupId)
    {
        if (!catalog.TryGetGroup(groupId, out _))
            return SagaUnlockResult.UnknownGroup;

        if (!_groups.TryAdd(groupId, new SagaGroupProgressRow(groupId, SagaGroupStatus.Active, 0)))
            return SagaUnlockResult.AlreadyUnlocked;

        return SagaUnlockResult.Ok;
    }

    /// <summary>
    /// Re-evaluates the group a finished quest belongs to against the quest-completion ground truth.
    /// Counting from the predicate (not incrementing) keeps the call replay-safe: the completion
    /// event can fire again for a quest that is already flagged.
    /// </summary>
    public SagaProgressChange OnQuestCompleted(
        SagaQuestCatalog catalog, uint questId, Func<uint, bool> isQuestCompleted)
    {
        var group = catalog.FindGroupByQuest(questId);
        // Locked groups hold no record, so there is nothing to advance; non-members too.
        if (group is null || !_groups.ContainsKey(group.Id))
            return null;

        return Evaluate(group, isQuestCompleted);
    }

    /// <summary>
    /// Re-evaluates every unlocked group from the ground truth — the login catch-up path for
    /// characters whose quests were finished before this state existed, and the retry path for a
    /// grant whose payload could not be applied yet.
    /// </summary>
    public IReadOnlyList<SagaProgressChange> Reconcile(
        SagaQuestCatalog catalog, Func<uint, bool> isQuestCompleted)
    {
        List<SagaProgressChange> changes = null;
        foreach (var group in catalog.Groups)
        {
            if (!_groups.ContainsKey(group.Id))
                continue;

            var change = Evaluate(group, isQuestCompleted);
            if (change is null)
                continue;

            changes ??= [];
            changes.Add(change);
        }

        return changes ?? [];
    }

    /// <summary>
    /// The idempotent reward-grant core: takes the (group, key) grant once and never again. Returns
    /// true only for the call that took it.
    /// </summary>
    public bool TryBeginGrant(uint groupId, uint grantKey) => _grants.Add((groupId, grantKey));

    private SagaProgressChange Evaluate(SagaQuestGroup group, Func<uint, bool> isQuestCompleted)
    {
        var record = _groups[group.Id];

        var completedCount = (ushort)Math.Clamp(group.CompletedCount(isQuestCompleted), 0, ushort.MaxValue);
        var complete = group.IsComplete(isQuestCompleted);
        var status = complete ? SagaGroupStatus.Complete : SagaGroupStatus.Active;
        var statusChanged = status != record.Status;
        var countChanged = completedCount != record.CompletedCount;

        var rewardGranted = false;
        uint grantKey = 0;
        if (complete)
        {
            grantKey = group.CompletionGrantKey;
            rewardGranted = TryBeginGrant(group.Id, grantKey);
        }

        if (!statusChanged && !countChanged && !rewardGranted)
            return null;

        _groups[group.Id] = new SagaGroupProgressRow(group.Id, status, completedCount);
        return new SagaProgressChange(
            group.Id, statusChanged, record.Status, status, completedCount, rewardGranted, grantKey);
    }

    /// <summary>Snapshot rows for persistence, in group id order.</summary>
    public IReadOnlyList<SagaGroupProgressRow> ExportGroups() =>
        _groups.Values.OrderBy(row => row.GroupId).ToList();

    /// <summary>Snapshot grant rows for persistence, ordered for stable round-trips.</summary>
    public IReadOnlyList<SagaRewardGrantRow> ExportGrants() =>
        _grants
            .Select(grant => new SagaRewardGrantRow(grant.GroupId, grant.GrantKey))
            .OrderBy(row => row.GroupId)
            .ThenBy(row => row.GrantKey)
            .ToList();

    /// <summary>
    /// Restores persisted rows, replacing anything held now — load runs on a fresh instance, and
    /// replace semantics keep save → reload → save byte-stable.
    /// </summary>
    public void Import(
        IEnumerable<SagaGroupProgressRow> groups, IEnumerable<SagaRewardGrantRow> grants)
    {
        _groups.Clear();
        _grants.Clear();

        foreach (var row in groups)
            _groups[row.GroupId] = row;

        foreach (var row in grants)
            _grants.Add((row.GroupId, row.GrantKey));
    }
}
