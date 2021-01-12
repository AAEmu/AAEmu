using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFamilyMemberOnlinePacket : GamePacket
    {
        private readonly uint _familyId;
        private readonly uint _memberId;
        private readonly bool _online;

        public SCFamilyMemberOnlinePacket(uint familyId, uint memberId, bool online) : base(SCOffsets.SCFamilyMemberOnlinePacket, 5)
        {
            _familyId = familyId;
            _memberId = memberId;
            _online = online;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_familyId);
            stream.Write(_memberId);
            stream.Write(_online);
            return stream;
        }
    }
}
