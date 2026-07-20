using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnhungPacket(uint unitObjId, uint targetObjId, uint reason) : GamePacket(SCOffsets.SCUnhungPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitObjId);
        stream.WriteBc(targetObjId);
        stream.Write(reason);
        return stream;
    }
}
