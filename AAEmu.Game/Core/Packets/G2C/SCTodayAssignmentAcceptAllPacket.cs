using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTodayAssignmentAcceptAllPacket(ushort errorMessage)
    : GamePacket(SCOffsets.SCTodayAssignmentAcceptAllPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(errorMessage);
        return stream;
    }
}
