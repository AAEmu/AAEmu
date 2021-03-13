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
            stream.Write(_character.HonorPoint);
            stream.Write(_character.VocationPoint);

            for (var i = 0; i < 11; i++) // in 1.2 march 2015 = 2 + 8 = 10, in 1.8 = 2 + 10 = 12, in 2.0 = 2 + 11 = 13
            {
                stream.Write(0u); // point
            }

            return stream;
        }
    }
}
