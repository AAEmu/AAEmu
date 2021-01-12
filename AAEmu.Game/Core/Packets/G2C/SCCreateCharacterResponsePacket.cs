using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCreateCharacterResponsePacket : GamePacket
    {
        private readonly Character _character;

        public SCCreateCharacterResponsePacket(Character character) : base(SCOffsets.SCCreateCharacterResponsePacket, 5)
        {
            _character = character;
        }

        public override PacketStream Write(PacketStream stream)
        {
            return _character.Write(stream);
        }
    }
}
