namespace AAEmu.Game.Models.Game.Sagas;

/// <summary>
/// One row of <c>saga_quest_groups</c> plus its ordered <c>saga_quests</c> membership.
/// </summary>
/// <param name="Id">saga_quest_groups.id — also the chronicle "type" / main key the client addresses this group by.</param>
/// <param name="Name">saga_quest_groups.name (content display name, never parsed).</param>
/// <param name="CurrencyId">saga_quest_groups.currency_id — purchase requirement, see the buy handler.</param>
/// <param name="CurrencyValue">saga_quest_groups.currency_value — purchase requirement amount.</param>
/// <param name="ItemSetId">saga_quest_groups.item_set_id — purchase requirement item set.</param>
/// <param name="MilestoneId">saga_quest_groups.milestone_id — the milestone this group's completion grant is keyed by.</param>
/// <param name="BookId">saga_quest_groups.book_id — book the story tab opens for a completed group.</param>
/// <param name="CompletionCondId">
/// saga_quest_groups.completion_cond_id: a member quest_context_id that completes the group on its
/// own. 0 means "every member quest must be completed".
/// </param>
/// <param name="OrderedQuestIds">Member quest_context_ids in the shipped saga_quests row order.</param>
public sealed class SagaQuestGroup(
    uint id,
    string name,
    uint currencyId,
    uint currencyValue,
    uint itemSetId,
    uint milestoneId,
    uint bookId,
    uint completionCondId,
    IReadOnlyList<uint> orderedQuestIds)
{
    public uint Id { get; } = id;
    public string Name { get; } = name;
    public uint CurrencyId { get; } = currencyId;
    public uint CurrencyValue { get; } = currencyValue;
    public uint ItemSetId { get; } = itemSetId;
    public uint MilestoneId { get; } = milestoneId;
    public uint BookId { get; } = bookId;
    public uint CompletionCondId { get; } = completionCondId;
    public IReadOnlyList<uint> OrderedQuestIds { get; } = orderedQuestIds;

    /// <summary>
    /// Grant key for this group's completion reward: its own milestone id from content. The reward
    /// ledger is keyed by (group, key), so GF-W14's per-milestone grants reuse the same table with
    /// their own content keys.
    /// </summary>
    public uint CompletionGrantKey => MilestoneId;

    public bool ContainsQuest(uint questId) => OrderedQuestIds.Contains(questId);

    /// <summary>
    /// Whether the group is complete under the supplied ground truth of finished quests.
    /// An explicit completion condition wins; otherwise every member quest must be done. A group
    /// left with no usable members (its memberships all failed content validation) can never
    /// complete — completing it out of thin air would grant a reward for content that is not there.
    /// </summary>
    public bool IsComplete(Func<uint, bool> isQuestCompleted)
    {
        if (CompletionCondId != 0)
            return ContainsQuest(CompletionCondId) && isQuestCompleted(CompletionCondId);

        if (OrderedQuestIds.Count == 0)
            return false;

        foreach (var questId in OrderedQuestIds)
        {
            if (!isQuestCompleted(questId))
                return false;
        }

        return true;
    }

    public int CompletedCount(Func<uint, bool> isQuestCompleted)
    {
        var count = 0;
        foreach (var questId in OrderedQuestIds)
        {
            if (isQuestCompleted(questId))
                count++;
        }

        return count;
    }
}
