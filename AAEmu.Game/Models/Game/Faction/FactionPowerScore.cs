using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Faction;

/// <summary>One faction's power points for one week, in the client's score-entry field order.</summary>
public sealed class FactionPowerScore : PacketMarshaler
{
    public uint WeekNum { get; set; }
    public uint FactionId { get; set; }
    public uint SiegePoint { get; set; }
    public uint SiegeMaintainPoint { get; set; }
    public uint CompetitionPoint { get; set; }
    public float QuestPoint { get; set; }
    public uint RaidPoint { get; set; }
    public uint RaidMaintainPoint { get; set; }
    public uint RaidJointPoint { get; set; }
    public uint ImmigratePoint { get; set; }

    public FactionPowerScore Clone() => (FactionPowerScore)MemberwiseClone();

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(WeekNum);
        stream.Write((int)FactionId);
        stream.Write(SiegePoint);
        stream.Write(SiegeMaintainPoint);
        stream.Write(CompetitionPoint);
        stream.Write(QuestPoint);
        stream.Write(RaidPoint);
        stream.Write(RaidMaintainPoint);
        stream.Write(RaidJointPoint);
        stream.Write(ImmigratePoint);
        return stream;
    }
}
