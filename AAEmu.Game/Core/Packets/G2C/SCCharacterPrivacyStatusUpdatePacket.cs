using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// </remarks>
public class SCCharacterPrivacyStatusUpdatePacket(bool result, CharacterPrivacyStatus status)
    : GamePacket(SCOffsets.SCCharacterPrivacyStatusUpdatePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(result);
        stream.Write((sbyte)status);

        return stream;
    }
}
