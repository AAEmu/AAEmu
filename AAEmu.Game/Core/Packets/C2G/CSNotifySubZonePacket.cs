using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSNotifySubZonePacket : GamePacket
    {
        public CSNotifySubZonePacket() : base(0x112, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var subZoneId = stream.ReadUInt32();
            if (subZoneId == 0) return;

            _log.Debug("Enter RegionId: {0} ", subZoneId);
            DbLoggerCategory.Database.Connection.ActiveChar.Portals.NotifySubZone(subZoneId);
        }
    }
}
