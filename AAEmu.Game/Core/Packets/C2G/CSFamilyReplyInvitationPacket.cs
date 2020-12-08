using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFamilyReplyInvitationPacket : GamePacket
    {
        public CSFamilyReplyInvitationPacket() : base(CSOffsets.CSFamilyReplyInvitationPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var invitorId = stream.ReadUInt32();
            var join = stream.ReadBoolean();
            var role = stream.ReadString();

            _log.Debug("FamilyReplyInvitation, invitorId: {0}, join: {1}, role: {2}", invitorId, join, role);

            FamilyManager.Instance.ReplyToInvite(invitorId, Connection.ActiveChar, join, role);
        }
    }
}
