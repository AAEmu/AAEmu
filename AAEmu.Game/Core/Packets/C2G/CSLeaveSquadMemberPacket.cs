using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSLeaveSquadMemberPacket() : GamePacket(CSOffsets.CSLeaveSquadMemberPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        SquadManager.Instance.Leave(Connection.ActiveChar);
    }
}
