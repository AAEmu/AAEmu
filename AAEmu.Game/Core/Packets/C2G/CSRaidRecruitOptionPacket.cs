using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// X2Team:RaidRecruitOption(autoJoin): bool autoJoin, the applicant window's
/// Auto-Invite / Manual Invite radio. Answered with SCRaidRecruitOption.
/// </summary>
public class CSRaidRecruitOptionPacket() : GamePacket(CSOffsets.CSRaidRecruitOptionPacket, 1)
{
    public bool AutoJoin { get; private set; }

    public override void Read(PacketStream stream)
    {
        AutoJoin = stream.ReadBoolean();
        RaidRecruitmentManager.Instance.SetOption(Connection.ActiveChar, AutoJoin);
    }
}
