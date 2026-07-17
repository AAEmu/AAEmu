using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterGamePointsPacket(Character character) : GamePacket(SCOffsets.SCCharacterGamePointsPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(character.HonorPoint);
        stream.Write(character.VocationPoint);

        for (var i = 0; i < 12; i++)
            stream.Write(0); // point
        return stream;
    }
}
