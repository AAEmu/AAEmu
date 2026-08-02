using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// bool success, and i16 errorMessage.
/// </summary>
public class SCDeleteFriendPacket(
    bool isRequester,
    uint characterId,
    string friendName,
    bool success,
    ErrorMessageType errorMessage)
    : GamePacket(SCOffsets.SCDeleteFriendPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(isRequester);
        stream.Write((ulong)characterId);
        stream.Write(friendName);
        stream.Write(success);
        stream.Write((short)errorMessage);
        return stream;
    }
}
