using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDuelStartedPacket(uint challengerObjId, uint challengedObjId)
    : GamePacket(SCOffsets.SCDuelStartedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(challengerObjId);  // challengerObjId
        stream.WriteBc(challengedObjId); // challengedObjId

        return stream;
    }
}
