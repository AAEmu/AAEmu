using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCancelCharacterDeleteResponsePacket(uint characterId, byte deleteStatus)
    : GamePacket(SCOffsets.SCCancelCharacterDeleteResponsePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(characterId);
        stream.Write(deleteStatus);
        return stream;
    }
}
