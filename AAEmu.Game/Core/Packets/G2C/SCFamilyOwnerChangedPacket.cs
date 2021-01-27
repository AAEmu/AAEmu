using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFamilyOwnerChangedPacket : GamePacket
    {
        private readonly uint _familyId;
        private readonly uint _memberId;

        public SCFamilyOwnerChangedPacket(uint familyId, uint memberId) : base(SCOffsets.SCFamilyOwnerChangedPacket, 5)
        {
            _familyId = familyId;
            _memberId = memberId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_familyId);
            stream.Write(_memberId); // newOwner
            return stream;
        }
    }
}
