using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSNotifySubZonePacket : GamePacket
    {
        public CSNotifySubZonePacket() : base(CSOffsets.CSNotifySubZonePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var subZoneId = stream.ReadUInt32();
            if (subZoneId == 0) return;

            _log.Debug("Enter RegionId: {0} ", subZoneId);
            Connection.ActiveChar.Portals.NotifySubZone(subZoneId);
        }
    }
}
