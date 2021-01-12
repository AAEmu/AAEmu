using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFamilyCreatedPacket : GamePacket
    {
        private readonly Family _family;
        
        public SCFamilyCreatedPacket(Family family) : base(SCOffsets.SCFamilyCreatedPacket, 5)
        {
            _family = family;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_family);
            return stream;
        }
    }
}
