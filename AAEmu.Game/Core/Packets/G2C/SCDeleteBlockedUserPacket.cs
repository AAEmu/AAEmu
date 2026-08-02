using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// </summary>
public class SCDeleteBlockedUserPacket(
    ulong characterId,
    bool success,
    ErrorMessageType errorMessage)
    : GamePacket(SCOffsets.SCDeleteBlockedUserPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(characterId);
        stream.Write(success);
        stream.Write((short)errorMessage);
        return stream;
    }
}
