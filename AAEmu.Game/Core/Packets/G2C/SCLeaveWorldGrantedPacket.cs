using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLeaveWorldGrantedPacket(LeaveWorldTargetType target) : GamePacket(SCOffsets.SCLeaveWorldGrantedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)target);
        return stream;
    }
}
