using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// X2Team:RaidApplicantAcceptReply(ownerId, join, role): u64 type, bool join, u32 role (x2game-dev.dll
/// FUN_39c6bc80). The applicant's answer to SCRaidApplicantAccept; the popup sends join=false by itself
/// when it closes unanswered after 60 s (raid_recruit_applicant_accept.lua).
/// </summary>
public class CSRaidApplicantAcceptReplyPacket() : GamePacket(CSOffsets.CSRaidApplicantAcceptReplyPacket, 1)
{
    public ulong OwnerId { get; private set; }
    public bool Join { get; private set; }
    public uint Role { get; private set; }

    public override void Read(PacketStream stream)
    {
        OwnerId = stream.ReadUInt64();
        Join = stream.ReadBoolean();
        Role = stream.ReadUInt32();
        RaidRecruitmentManager.Instance.AcceptReply(Connection.ActiveChar, OwnerId, Join, Role);
    }
}
