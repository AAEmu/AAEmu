using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFactionIndependencePacket(uint id, uint id2, string name, DateTime cTime)
    : GamePacket(SCOffsets.SCFactionIndependencePacket, 5)
{
    // TODO createTime?

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(id2);
        stream.Write(name);
        stream.Write(cTime);
        return stream;
    }
}
