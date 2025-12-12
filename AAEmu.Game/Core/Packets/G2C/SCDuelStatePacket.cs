using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDuelStatePacket(uint challengerObjId, uint flagObjId) : GamePacket(SCOffsets.SCDuelStatePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(challengerObjId);  // challengerObjId
        stream.WriteBc(flagObjId);       // flagObjId

        return stream;
    }
}
