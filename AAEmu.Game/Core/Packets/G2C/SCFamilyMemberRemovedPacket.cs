using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFamilyMemberRemovedPacket : GamePacket
    {
        private readonly uint _familyId;
        private readonly bool _kicked;
        private readonly uint _memberId;
        
        public SCFamilyMemberRemovedPacket(uint familyId, bool kicked, uint memberId) : base(0x02d, 1)
        {
            _familyId = familyId;
            _kicked = kicked;
            _memberId = memberId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_familyId);
            stream.Write(_memberId); // TODO may or may not be. =_+ WTF?!?!?
            stream.Write(_kicked);
            return stream;
        }

        // TODO of miss logic
        /* sub_3957BBF0
          a2->Reader->ReadUInt32("family", this + 8, 0);
          if ( !(a2->Reader->field_14)("member", 1) )
            return a2->Reader->ReadBool("kicked", v2 + 16, 0);
          a2->Reader->ReadUInt32("type", v2 + 12, 0);
          a2->Reader->field_18(a2);
          return a2->Reader->ReadBool("kicked", v2 + 16, 0);
         */
    }
}
