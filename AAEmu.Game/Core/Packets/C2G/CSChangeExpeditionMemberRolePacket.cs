using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeExpeditionMemberRolePacket : GamePacket
    {
        public CSChangeExpeditionMemberRolePacket() : base(0x007, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var role = stream.ReadByte();
            var id = stream.ReadUInt32(); // type(id)

            _log.Debug("ChangeExpeditionMemberRole, Id: {0}, Role: {1}", id, role);
        }
    }
}
