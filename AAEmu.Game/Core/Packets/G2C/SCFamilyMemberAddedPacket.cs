using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFamilyMemberAddedPacket : GamePacket
    {
        private readonly Family _family;
        private readonly int _addedIndex;
        
        public SCFamilyMemberAddedPacket(Family family, int addedIndex) : base(SCOffsets.SCFamilyMemberAddedPacket, 5)
        {
            _family = family;
            _addedIndex = addedIndex;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_family);
            stream.Write(_addedIndex);
            return stream;
        }
    }
}
