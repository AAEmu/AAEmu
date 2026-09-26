using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// X2Team:RaidApplicantList: bool bSubRecruiter, u64 type. The binding
/// Sends the client's team owner id and raises the flag only for a siege raid's officer.
/// Answered with SCRaidApplicantList.
/// </summary>
public class CSRaidApplicantListPacket() : GamePacket(CSOffsets.CSRaidApplicantListPacket, 1)
{
    public bool SubRecruiter { get; private set; }
    public ulong OwnerId { get; private set; }

    public override void Read(PacketStream stream)
    {
        SubRecruiter = stream.ReadBoolean();
        OwnerId = stream.ReadUInt64();
        RaidRecruitmentManager.Instance.ListApplicants(Connection.ActiveChar, SubRecruiter, OwnerId);
    }
}
