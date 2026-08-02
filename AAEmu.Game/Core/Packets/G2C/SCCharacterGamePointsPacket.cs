using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterGamePointsPacket(Character character) : GamePacket(SCOffsets.SCCharacterGamePointsPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // of 14 u32 "moneyAmount" slots. Slots 0/1 are honor/vocation points; the remaining 12 are unused
        // point currencies and go out as 0.
        stream.Write(character.HonorPoint);
        stream.Write(character.VocationPoint);

        for (var i = 0; i < 12; i++)
            stream.Write(0);
        return stream;
    }
}
