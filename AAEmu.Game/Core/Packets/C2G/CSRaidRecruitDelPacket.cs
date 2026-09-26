using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// X2Team:RaidRecruitDel. No body (empty serializer): the poster deletes
/// their own post; the applicants listed on it go with it (ui_texts 8853).
/// </summary>
public class CSRaidRecruitDelPacket() : GamePacket(CSOffsets.CSRaidRecruitDelPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        RaidRecruitmentManager.Instance.Delete(Connection.ActiveChar);
    }
}
