using AAEmu.Game.Models.Game.Milestones;

namespace AAEmu.UnitTests.Game.Models.Game.Milestones;

/// <summary>Synthetic milestone rows for tests — never the shipped ids or dates.</summary>
internal static class MilestoneTestCatalog
{
    /// <summary>A clock every fixture derives its windows from, so no literal dates are pinned.</summary>
    public static readonly DateTime Now = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Builds a catalog from (milestoneId, release) rows inside the default window and
    /// (questId, milestoneId) triggers. Rows and triggers arrive shuffled relative to output
    /// order on purpose: shipped tables have no insertion-order contract.</summary>
    public static MilestoneCatalog Build(
        IEnumerable<(uint Id, bool Release)> rows,
        IEnumerable<(uint QuestId, uint MilestoneId)> triggers,
        int windowDays = 1)
    {
        var rowRows = rows.Select(row => new MilestoneRow(
            row.Id,
            Now.AddDays(-windowDays),
            Now.AddDays(windowDays),
            row.Release));

        var triggerRows = triggers.Select(trigger =>
            new MilestoneTriggerRow(trigger.QuestId, trigger.MilestoneId));

        return MilestoneCatalog.Build(rowRows, triggerRows);
    }

    /// <summary>A released milestone whose window contains <see cref="Now"/>, chain quests 9001..9001+chain.</summary>
    public static MilestoneCatalog Chain(
        uint milestoneId,
        uint chainLength,
        bool release = true) =>
        Build(
            [(milestoneId, release)],
            Enumerable.Range(0, (int)chainLength)
                .Select(i => (QuestId: (uint)(9001 + i), MilestoneId: milestoneId)));
}
