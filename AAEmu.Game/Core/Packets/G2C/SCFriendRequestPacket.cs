using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// u64 characterId, u32 worldId, datetime requestTime, and string characterName.
/// </summary>
public class SCFriendRequestPacket(
    bool isRequest,
    bool success,
    ErrorMessageType errorMessage,
    ulong characterId,
    uint worldId,
    DateTime requestTime,
    string characterName)
    : GamePacket(SCOffsets.SCFriendRequestPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(isRequest);
        stream.Write(success);
        stream.Write((short)errorMessage);
        stream.Write(characterId);
        stream.Write(worldId);
        stream.Write(requestTime);
        stream.Write(characterName);
        return stream;
    }
}
