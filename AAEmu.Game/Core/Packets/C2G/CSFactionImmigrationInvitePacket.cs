using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFactionImmigrationInvitePacket : GamePacket
    {
        public CSFactionImmigrationInvitePacket() : base(CSOffsets.CSFactionImmigrationInvitePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var invitee = stream.ReadString();

            _log.Debug("FactionImmigrationInvite, {0}", invitee);
        }
    }
}
