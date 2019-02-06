using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFamilyOwnerChangedPacket : GamePacket
    {
        private readonly uint _familyId;
        private readonly uint _memberId;
        
        public SCFamilyOwnerChangedPacket(uint familyId, uint memberId) : base(0x02e, 1)
        {
            _familyId = familyId;
            _memberId = memberId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_familyId);
            stream.Write(_memberId); // TODO may or may not be. =_+ WTF?!?!?
            return stream;
        }
        
        // TODO if i miss logic
        /* sub_3957BC60
         *
          a2->Reader->ReadUInt32("family", this + 8, 0);
          result = (a2->Reader->field_14)("newOwner", 1);
          if ( !result )
            return result;
          a2->Reader->ReadUInt32("type", v2 + 12, 0);
          result = (a2->Reader->field_18)(a2);
         */
    }
}
