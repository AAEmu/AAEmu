using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHungPacket(uint unitObjId, uint targetObjId) : GamePacket(SCOffsets.SCHungPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitObjId);
        stream.WriteBc(targetObjId);
        return stream;
    }
}
