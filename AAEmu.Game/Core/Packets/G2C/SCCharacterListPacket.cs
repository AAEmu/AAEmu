using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterListPacket(bool last, Character[] characters) : GamePacket(SCOffsets.SCCharacterListPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(last);
        stream.Write((byte)characters.Length);
        foreach (var character in characters)
        {
            var p0 = stream.Count;
            character.WriteLobby1013(stream); // 10.0.2.13 lobby char struct
            Logger.Warn("WriteLobby1013 {0}: {1} bytes", character.Name, stream.Count - p0);
        }

        return stream;
    }
}
