using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAddBlockedUserPacket(uint characterId, string characterName, bool success, short errorMessage)
    : GamePacket(SCOffsets.SCAddBlockedUserPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(characterId);
        stream.Write(characterName);
        stream.Write(success);
        stream.Write(errorMessage);
        return stream;
    }
}
