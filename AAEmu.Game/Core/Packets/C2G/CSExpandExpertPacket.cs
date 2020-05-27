using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpandExpertPacket : GamePacket
    {
        public CSExpandExpertPacket() : base(0x101, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            _log.Debug("ExpandExpert");

            DbLoggerCategory.Database.Connection.ActiveChar.Actability.ExpandExpert();
        }
    }
}
