using AAEmu.Game.Models.Game.TodayAssignment;
using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.Game.Models.Game.Expeditions.PublicAssignments;

public sealed class ExpeditionPublicAssignmentState
{
    public uint ExpeditionId { get; init; }
    public DateTime PeriodStart { get; init; }
    public uint RealStep { get; init; }
    public uint GroupId { get; set; }
    public uint QuestContextId { get; set; }
    public TodayAssignmentStatus Status { get; set; } = TodayAssignmentStatus.Progress;
    public int[] Objectives { get; } = new int[10];
    public uint Version { get; set; }
    public uint SelectionGeneration { get; set; } = 1;
    public DateTime? CompletedAt { get; set; }
    public bool GuildRewarded { get; set; }
    public Dictionary<uint, PublicAssignmentContributor> Contributors { get; } = [];

    public ExpeditionPublicAssignmentState Copy()
    {
        var copy = new ExpeditionPublicAssignmentState
        {
            ExpeditionId = ExpeditionId, PeriodStart = PeriodStart, RealStep = RealStep, GroupId = GroupId,
            QuestContextId = QuestContextId, Status = Status, Version = Version, CompletedAt = CompletedAt,
            GuildRewarded = GuildRewarded, SelectionGeneration = SelectionGeneration
        };
        Array.Copy(Objectives, copy.Objectives, Objectives.Length);
        foreach (var contributor in Contributors) copy.Contributors[contributor.Key] = contributor.Value;
        return copy;
    }
}

public sealed record PublicAssignmentContributor(uint CharacterId, string CharacterName, ulong Contribution);

public sealed record QueuedPublicAssignmentProgress(
    PublicQuestProgressEvent Captured,
    string ContributorName,
    uint RealStep,
    DateTime PeriodStart,
    uint SelectionGeneration)
{
    public bool Matches(ExpeditionPublicAssignmentState state) =>
        state != null && state.ExpeditionId == Captured.ExpeditionId && state.RealStep == RealStep &&
        state.PeriodStart == PeriodStart && state.SelectionGeneration == SelectionGeneration;
}
