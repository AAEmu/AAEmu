using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCVegetationCutdowningPacket(uint unitObjId, uint doodadObjId)
    : GamePacket(SCOffsets.SCVegetationCutdowningPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitObjId);
        stream.WriteBc(doodadObjId);
        return stream;
    }
}
