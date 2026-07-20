using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDoodadOriginatorPacket(uint objId, uint newOwnerId, uint faction)
    : GamePacket(SCOffsets.SCDoodadOriginatorPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(newOwnerId);
        stream.Write(faction);

        return stream;
    }
}
