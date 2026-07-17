using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCBuffLearnedPacket(uint objId, uint buffId) : GamePacket(SCOffsets.SCBuffLearnedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(buffId);
        return stream;
    }
}
