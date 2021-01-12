using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFamilyMemberRemovedPacket : GamePacket
    {
        private readonly uint _familyId;
        private readonly bool _kicked;
        private readonly uint _memberId;
        
        public SCFamilyMemberRemovedPacket(uint familyId, bool kicked, uint memberId) : base(SCOffsets.SCFamilyMemberRemovedPacket, 5)
        {
            _familyId = familyId;
            _kicked = kicked;
            _memberId = memberId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_familyId);
            stream.Write(_memberId);
            stream.Write(_kicked);
            return stream;
        }
    }
}
