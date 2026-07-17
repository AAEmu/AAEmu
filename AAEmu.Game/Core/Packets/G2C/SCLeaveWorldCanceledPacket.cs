using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLeaveWorldCanceledPacket() : GamePacket(SCOffsets.SCLeaveWorldCanceledPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        return stream;
    }
}
