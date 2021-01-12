using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFamilyTitleChangedPacket : GamePacket
    {
        private readonly uint _familyId;
        private readonly uint _memberId;
        private readonly string _title;
        
        public SCFamilyTitleChangedPacket(uint familyId, uint memberId, string title) : base(SCOffsets.SCFamilyTitleChangedPacket, 5)
        {
            _familyId = familyId;
            _memberId = memberId;
            _title = title;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_familyId);
            stream.Write(_memberId);
            stream.Write(_title);
            return stream;
        }
    }
}
