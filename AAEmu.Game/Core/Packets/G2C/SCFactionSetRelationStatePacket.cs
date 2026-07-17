using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFactionSetRelationStatePacket(uint id, uint id2, byte state, DateTime expireTime, byte nextState)
    : GamePacket(SCOffsets.SCFactionSetRelationStatePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(id2);
        stream.Write(state);
        stream.Write(expireTime);
        stream.Write(nextState);
        return stream;
    }
}
