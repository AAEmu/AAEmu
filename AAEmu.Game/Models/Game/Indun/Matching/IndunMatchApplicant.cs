namespace AAEmu.Game.Models.Game.Indun.Matching;

public sealed class IndunMatchApplicant(uint characterId, uint teamId, DateTime appliedAt)
{
    public uint CharacterId { get; } = characterId;
    public uint TeamId { get; } = teamId;
    public DateTime AppliedAt { get; } = appliedAt;
    public bool Accepted { get; set; }
    public bool Declined { get; set; }
}
