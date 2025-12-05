using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterGamePointsPacket(Character character) : GamePacket(SCOffsets.SCCharacterGamePointsPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(character.HonorPoint);
        stream.Write(character.VocationPoint);

        for (var i = 0; i < 8; i++)
            stream.Write(0); // point
        return stream;
    }
}

/*
v3 = 10;
do
{
result = a2->Reader->ReadUInt32("p", v2, 0);
v2 += 4;
--v3;
}
while ( v3 );
*/
