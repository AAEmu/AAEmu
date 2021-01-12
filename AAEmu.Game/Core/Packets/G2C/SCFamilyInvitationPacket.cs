using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFamilyInvitationPacket : GamePacket
    {
        private readonly uint _invitorId;
        private readonly string _invitorName;
        private readonly uint _familyId;
        private readonly string _role;

        public SCFamilyInvitationPacket(uint invitorId, string invitorName, uint familyId, string role) 
            : base(SCOffsets.SCFamilyInvitationPacket, 5)
        {
            _invitorId = invitorId;
            _invitorName = invitorName;
            _familyId = familyId;
            _role = role;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_invitorId);
            stream.Write(_invitorName);
            stream.Write(_familyId);
            stream.Write(_role);
            return stream;
        }
    }
}
