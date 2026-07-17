using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDeleteCharacterResponsePacket(
    uint characterId,
    byte status,
    DateTime? deleteRequestedTime = null,
    DateTime? deleteDelay = null)
    : GamePacket(SCOffsets.SCDeleteCharacterResponsePacket, 5)
{
    private readonly DateTime _deleteRequestedTime = deleteRequestedTime ?? DateTime.MinValue;
    private readonly DateTime _deleteDelay = deleteDelay ?? DateTime.MinValue;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(characterId);
        stream.Write(status);
        stream.Write(_deleteRequestedTime);
        stream.Write(_deleteDelay);
        return stream;
    }
}
