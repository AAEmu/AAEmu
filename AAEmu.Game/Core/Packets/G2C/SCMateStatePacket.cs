using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCMateStatePacket(uint objId) : GamePacket(SCOffsets.SCMateStatePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(0); // skillCount
        stream.Write(0); // tagCount
        return stream;
    }
}
