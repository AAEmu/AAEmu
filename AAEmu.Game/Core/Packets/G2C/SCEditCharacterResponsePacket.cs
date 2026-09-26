using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The lobby's answer to a CSEditCharacter: the edited character's lobby record, the same struct the
/// character list and SCCreateCharacterResponse carry (serializer hands
/// the body straight to the lobby record writer).
/// </summary>
public class SCEditCharacterResponsePacket(Character character) : GamePacket(SCOffsets.SCEditCharacterResponsePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        return character.WriteLobby1013(stream);
    }
}
