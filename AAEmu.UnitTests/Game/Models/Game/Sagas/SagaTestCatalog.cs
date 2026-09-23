using AAEmu.Game.Models.Game.Sagas;

namespace AAEmu.UnitTests.Game.Models.Game.Sagas;

/// <summary>Synthetic saga rows for tests — never the shipped ids.</summary>
internal static class SagaTestCatalog
{
    /// <summary>
    /// Builds a catalog from (groupId, completionCondId, milestoneId) groups and raw memberships.
    /// </summary>
    public static SagaQuestCatalog Build(
        IEnumerable<(uint Id, uint CompletionCondId, uint MilestoneId)> groups,
        IEnumerable<(uint GroupId, uint QuestId)> members,
        Func<uint, bool> questExists = null)
    {
        var groupRows = groups.Select(group => new SagaGroupRow(
            group.Id,
            $"group-{group.Id}",
            0, // currency id
            0, // currency value
            0, // item set id
            group.MilestoneId,
            0, // book id
            group.CompletionCondId));

        var memberRows = members.Select(member => new SagaMembershipRow(member.GroupId, member.QuestId));

        return SagaQuestCatalog.Build(groupRows, memberRows, questExists ?? (_ => true));
    }
}
