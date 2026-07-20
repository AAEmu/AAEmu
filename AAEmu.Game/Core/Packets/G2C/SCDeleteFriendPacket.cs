using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDeleteFriendPacket(uint characterId, bool success, string friendName, short errorMessage)
    : GamePacket(SCOffsets.SCDeleteFriendPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(characterId);
        stream.Write(success);
        stream.Write(friendName);
        stream.Write(errorMessage);
        return stream;
    }
}
