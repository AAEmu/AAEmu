using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFamilyDescPacket : GamePacket
    {
        private readonly Family _family;
        
        public SCFamilyDescPacket(Family family) : base(SCOffsets.SCFamilyDescPacket, 5)
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
