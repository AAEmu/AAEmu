using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// X2Team:RaidApplicantDel(ownerId): u64 type, the post's owner id (x2game-dev.dll FUN_39c688d0). The
/// applicant withdraws; answered with SCRaidApplicantDel.
/// </summary>
public class CSRaidApplicantDelPacket() : GamePacket(CSOffsets.CSRaidApplicantDelPacket, 1)
{
    public ulong OwnerId { get; private set; }

    public override void Read(PacketStream stream)
    {
        OwnerId = stream.ReadUInt64();
        RaidRecruitmentManager.Instance.Withdraw(Connection.ActiveChar, OwnerId);
    }
}
