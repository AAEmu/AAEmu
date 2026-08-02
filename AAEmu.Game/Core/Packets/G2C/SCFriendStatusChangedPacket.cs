using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// </summary>
public class SCFriendStatusChangedPacket(bool isWaitFriend, Friend friend)
    : GamePacket(SCOffsets.SCFriendStatusChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(isWaitFriend);
        stream.Write(friend);
        return stream;
    }
}
