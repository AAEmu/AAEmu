using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// X2Team:RaidRecruitList(). No body: the client's serializer for this type is the empty function
/// x2game-dev.dll FUN_395e5690. Answered with SCRaidRecruitList.
/// </summary>
public class CSRaidRecruitListPacket() : GamePacket(CSOffsets.CSRaidRecruitListPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        RaidRecruitmentManager.Instance.List(Connection.ActiveChar);
    }
}
