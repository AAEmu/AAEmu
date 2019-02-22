using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCharacterGamePointsPacket : GamePacket
    {
        private readonly Character _character;

        public SCCharacterGamePointsPacket(Character character) : base(SCOffsets.SCCharacterGamePointsPacket, 1)
        {
            _character = character;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_character.HonorPoint);
            stream.Write(_character.VocationPoint);

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
}
