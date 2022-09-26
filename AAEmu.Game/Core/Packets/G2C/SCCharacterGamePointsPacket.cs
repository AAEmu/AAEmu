using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCharacterGamePointsPacket : GamePacket
    {
        private readonly Character _character;

        public SCCharacterGamePointsPacket(Character character) : base(SCOffsets.SCCharacterGamePointsPacket, 5)
        {
            _character = character;
        }

        public override PacketStream Write(PacketStream stream)
        {
            // 10 in 1.2, 14 in 3+
            stream.Write(_character.HonorPoint);
            stream.Write(_character.VocationPoint);

            for (var i = 0; i < 12; i++)
                stream.Write(0); // point
            return stream;
        }
    }

    /*
       v2 = this;
-->    v3 = 14;
       do
       {
       result = (a2->Read->UInt32)("p", v2, 0);
       v2 += 4;
       --v3;
       }
       while ( v3 );
       return result;
    */
}
