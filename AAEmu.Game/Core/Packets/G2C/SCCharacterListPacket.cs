using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterListPacket(bool last, Character[] characters) : GamePacket(SCOffsets.SCCharacterListPacket, 1)
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
