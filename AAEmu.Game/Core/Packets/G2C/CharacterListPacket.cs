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
        {
            character.Write(stream);

            if (Logger.IsDebugEnabled)
                Logger.Debug($"CharacterList -> id={character.Id}, name='{character.Name}', race={character.Race}, gender={character.Gender}, modelParams hex: {Convert.ToHexString(character.ModelParams.Write(new PacketStream()).GetBytes())}");
        }

        return stream;
    }
}
