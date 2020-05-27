using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSInviteToExpeditionPacket : GamePacket
    {
        public CSInviteToExpeditionPacket() : base(0x00c, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var name = stream.ReadString();

            _log.Debug("InviteToExpedition, Name: {0}", name);
            ExpeditionManager.Instance.Invite(DbLoggerCategory.Database.Connection, name);
        }
    }
}
