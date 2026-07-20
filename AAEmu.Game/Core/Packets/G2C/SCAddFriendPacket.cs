using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAddFriendPacket(Friend friend, bool success, short errorMessage)
    : GamePacket(SCOffsets.SCAddFriendPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(friend);
        stream.Write(success);
        stream.Write(errorMessage);
        return stream;
    }
}
