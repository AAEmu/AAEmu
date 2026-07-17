using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnbondDoodadPacket(uint characterObjId, uint characterId, uint doodadObjId)
    : GamePacket(SCOffsets.SCUnbondDoodadPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(characterObjId);
        stream.Write(characterId);
        stream.WriteBc(doodadObjId);
        return stream;
    }
}
