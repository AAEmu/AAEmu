using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitInvisiblePacket(uint objId, bool invisible) : GamePacket(SCOffsets.SCUnitInvisiblePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(invisible);
        return stream;
    }
}
