namespace AAEmu.Game.Models.Game.Sagas;

/// <summary>One saga_quest_groups row, before validation against membership.</summary>
public sealed record SagaGroupRow(
    uint Id,
    string Name,
    uint CurrencyId,
    uint CurrencyValue,
    uint ItemSetId,
    uint MilestoneId,
    uint BookId,
    uint CompletionCondId);

/// <summary>One saga_quests row in shipped table order.</summary>
public sealed record SagaMembershipRow(uint SagaQuestGroupId, uint QuestContextId);

/// <summary>
/// A membership row that could not be honored, kept so the loader can report it loudly instead of
/// dropping it silently. Shipped content is not clean: one membership points at a group row that
/// some database builds do not carry, and its quest_context row is missing everywhere.
/// </summary>
public sealed record SagaContentIssue(uint SagaQuestGroupId, uint QuestContextId, string Reason);

/// <summary>
/// Validated saga content: groups in shipped order, each with its quests in shipped membership
/// order. Built once by the game-data loader (or a test) from raw rows.
/// </summary>
public sealed class SagaQuestCatalog
{
    public static readonly SagaQuestCatalog Empty = new([], []);

    private readonly Dictionary<uint, SagaQuestGroup> _groupsById;
    private readonly Dictionary<uint, SagaQuestGroup> _groupsByQuestId;

    private SagaQuestCatalog(IReadOnlyList<SagaQuestGroup> groups, IReadOnlyList<SagaContentIssue> issues)
    {
        Groups = groups;
        Issues = issues;
        _groupsById = groups.ToDictionary(group => group.Id);
        _groupsByQuestId = [];
        foreach (var group in groups)
        {
            foreach (var questId in group.OrderedQuestIds)
                _groupsByQuestId.TryAdd(questId, group);
        }
    }

    /// <summary>All groups ordered by saga_quest_groups.id — the shipped row order.</summary>
    public IReadOnlyList<SagaQuestGroup> Groups { get; }

    /// <summary>Rows that failed validation; each is reported at load time.</summary>
    public IReadOnlyList<SagaContentIssue> Issues { get; }

    public bool TryGetGroup(uint groupId, out SagaQuestGroup group) =>
        _groupsById.TryGetValue(groupId, out group);

    /// <summary>Fail loud: an id the content does not carry is a bug, not an empty state.</summary>
    public SagaQuestGroup GetGroup(uint groupId) =>
        _groupsById.TryGetValue(groupId, out var group)
            ? group
            : throw new InvalidOperationException($"Saga quest group {groupId} does not exist in content");

    /// <summary>The group a quest belongs to, or null when the quest is not a saga-group member.</summary>
    public SagaQuestGroup FindGroupByQuest(uint questId) =>
        _groupsByQuestId.GetValueOrDefault(questId);

    /// <summary>
    /// Validates raw rows into a catalog. Groups keep their shipped id order; quest membership keeps
    /// the shipped saga_quests row order (the order the chapter/quest indices follow — id order is
    /// not contiguous: shipped ids skip 5..7). A membership whose group row or quest_context row is
    /// missing is skipped and recorded as an <see cref="SagaContentIssue"/>; a group left empty by
    /// that skipping is an issue too, since it can never complete.
    /// </summary>
    public static SagaQuestCatalog Build(
        IEnumerable<SagaGroupRow> groupRows,
        IEnumerable<SagaMembershipRow> membershipRows,
        Func<uint, bool> questExists)
    {
        var orderedGroups = groupRows.OrderBy(row => row.Id).ToList();
        var groupsById = new Dictionary<uint, SagaGroupBuilder>();
        foreach (var row in orderedGroups)
        {
            groupsById[row.Id] = new SagaGroupBuilder(row);
        }

        var issues = new List<SagaContentIssue>();
        foreach (var membership in membershipRows)
        {
            if (!groupsById.TryGetValue(membership.SagaQuestGroupId, out var builder))
            {
                issues.Add(new SagaContentIssue(
                    membership.SagaQuestGroupId,
                    membership.QuestContextId,
                    "saga_quest_groups row missing"));
                continue;
            }

            if (!questExists(membership.QuestContextId))
            {
                issues.Add(new SagaContentIssue(
                    membership.SagaQuestGroupId,
                    membership.QuestContextId,
                    "quest_contexts row missing"));
                continue;
            }

            builder.QuestIds.Add(membership.QuestContextId);
        }

        var groups = new List<SagaQuestGroup>(orderedGroups.Count);
        foreach (var row in orderedGroups)
        {
            var builder = groupsById[row.Id];
            if (builder.QuestIds.Count == 0)
            {
                issues.Add(new SagaContentIssue(
                    row.Id, 0, "group has no usable saga quest members"));
            }

            groups.Add(new SagaQuestGroup(
                row.Id,
                row.Name,
                row.CurrencyId,
                row.CurrencyValue,
                row.ItemSetId,
                row.MilestoneId,
                row.BookId,
                row.CompletionCondId,
                builder.QuestIds));
        }

        return new SagaQuestCatalog(groups, issues);
    }

    private sealed class SagaGroupBuilder(SagaGroupRow row)
    {
        public SagaGroupRow Row { get; } = row;
        public List<uint> QuestIds { get; } = [];
    }
}
