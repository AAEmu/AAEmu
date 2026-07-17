using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class CharacterListPacket(bool last, Character[] characters) : GamePacket(SCOffsets.CharacterListPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(last);
        stream.Write((byte)characters.Length);
        foreach (var character in characters)
            character.Write(stream);

        return stream;
    }
}
