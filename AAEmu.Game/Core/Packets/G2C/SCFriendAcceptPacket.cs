using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// and one Friend record.
/// </summary>
public class SCFriendAcceptPacket(
    bool success,
    bool isAccept,
    ErrorMessageType errorMessage,
    Friend friend)
    : GamePacket(SCOffsets.SCFriendAcceptPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(success);
        stream.Write(isAccept);
        stream.Write((short)errorMessage);
        stream.Write(friend);
        return stream;
    }
}
