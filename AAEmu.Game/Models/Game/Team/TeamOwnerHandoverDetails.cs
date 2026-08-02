using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Team;

/// <remarks>
/// </remarks>
public enum TeamOwnerHandoverReason : sbyte
{
    HigherHeroGrade = 0,
    HigherHero = 1,
    LeadershipPeriodPoint = 2,
    LeadershipPoint = 3,
    Level = 4,
    GearScore = 5,
    None = 6
}

public readonly record struct TeamOwnerHandoverDetails(
    TeamOwnerHandoverReason Reason,
    int LeadershipPeriodPoint,
    int LeadershipPoint,
    sbyte Level,
    uint GearScore)
{
    public void Write(PacketStream stream)
    {
        stream.Write((sbyte)Reason);
        stream.Write(LeadershipPeriodPoint);
        stream.Write(LeadershipPoint);
        stream.Write(Level);
        stream.Write(GearScore);
    }
}
