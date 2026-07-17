using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCKnockBackUnitPacket(uint objId, float x, float y, float z)
    : GamePacket(SCOffsets.SCKnockBackUnitPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(x);
        stream.Write(y);
        stream.Write(z);
        return stream;
    }
}
