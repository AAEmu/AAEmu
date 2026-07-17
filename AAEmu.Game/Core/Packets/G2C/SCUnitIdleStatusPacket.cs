using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitIdleStatusPacket(uint id, bool status) : GamePacket(SCOffsets.SCUnitIdleStatusPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(id);
        stream.Write(status);
        return stream;
    }
}
