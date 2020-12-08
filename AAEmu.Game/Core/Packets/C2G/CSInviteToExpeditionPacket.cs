using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSInviteToExpeditionPacket : GamePacket
    {
        public CSInviteToExpeditionPacket() : base(CSOffsets.CSInviteToExpeditionPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var name = stream.ReadString();

            _log.Debug("InviteToExpedition, Name: {0}", name);
            ExpeditionManager.Instance.Invite(Connection, name);
        }
    }
}
