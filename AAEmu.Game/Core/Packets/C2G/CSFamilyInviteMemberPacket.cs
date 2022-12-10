using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFamilyInviteMemberPacket : GamePacket
    {
        public CSFamilyInviteMemberPacket() : base(CSOffsets.CSFamilyInviteMemberPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var name = stream.ReadString();
            var title = stream.ReadString();

            _log.Debug("FamilyInviteMember, Name: {0}, Title: {1}", name, title);

            FamilyManager.Instance.InviteToFamily(Connection.ActiveChar, name, title);
        }
    }
}
