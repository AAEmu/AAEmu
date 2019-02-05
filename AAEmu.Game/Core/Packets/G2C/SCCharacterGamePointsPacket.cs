using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCharacterGamePointsPacket : GamePacket
    {
        private readonly Character _character;

        public SCCharacterGamePointsPacket(Character character) : base(0x180, 1)
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
}
