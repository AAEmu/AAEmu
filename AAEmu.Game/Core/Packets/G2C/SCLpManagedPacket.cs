using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCLpManagedPacket : GamePacket
    {
        private readonly uint _characterId;

        public SCLpManagedPacket(uint characterId) : base(SCOffsets.SCLpManagedPacket, 5)
        {
            _characterId = characterId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_characterId);
            return stream;
        }
    }
}
