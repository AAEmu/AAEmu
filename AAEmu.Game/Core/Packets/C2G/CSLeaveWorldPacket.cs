using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSLeaveWorldPacket() : GamePacket(CSOffsets.CSLeaveWorldPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var leaveWorldTarget = (LeaveWorldTargetType)stream.ReadByte();
        EnterWorldManager.Leave(Connection, leaveWorldTarget);
    }
}
