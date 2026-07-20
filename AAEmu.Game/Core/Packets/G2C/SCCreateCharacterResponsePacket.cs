using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCreateCharacterResponsePacket(Character character)
    : GamePacket(SCOffsets.SCCreateCharacterResponsePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        return character.Write(stream);
    }
}
