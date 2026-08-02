using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// from the selected pending-request map.
/// </remarks>
public class SCFriendCancelPacket(
    bool success,
    bool isReceive,
    ulong ownerCharacterId,
    ulong counterpartCharacterId)
    : GamePacket(SCOffsets.SCFriendCancelPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(success);
        stream.Write(isReceive);
        stream.Write(ownerCharacterId);
        stream.Write(counterpartCharacterId);
        return stream;
    }
}
