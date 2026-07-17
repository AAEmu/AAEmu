using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterDeletedPacket(uint characterId, string characterName)
    : GamePacket(SCOffsets.SCCharacterDeletedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(characterId);
        stream.Write(characterName);
        return stream;
    }
}
