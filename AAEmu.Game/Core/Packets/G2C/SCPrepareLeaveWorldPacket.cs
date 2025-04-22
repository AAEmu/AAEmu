using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCPrepareLeaveWorldPacket(int time, LeaveWorldTargetType target, bool idleKick) : GamePacket(SCOffsets.SCPrepareLeaveWorldPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(time);
        stream.Write((byte)target);
        stream.Write(idleKick);
        return stream;
    }
}
