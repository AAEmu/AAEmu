using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFactionKickToOriginResultPacket(string tgtName, uint id, uint id2, short errorMessage)
    : GamePacket(SCOffsets.SCFactionKickToOriginResultPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(tgtName);
        stream.Write(id);
        stream.Write(id2);
        stream.Write(errorMessage);
        return stream;
    }
}
