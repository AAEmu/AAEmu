using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFamilyInviteMemberPacket : GamePacket
    {
        public CSFamilyInviteMemberPacket() : base(0x19, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var invitedCharacterName = stream.ReadString();
            var roleName = stream.ReadString();

            _log.Debug("CSFamilyInviteMember : InvitedCharacterName = {0}, RoleName = {1}", invitedCharacterName, roleName);
        }
    }
}