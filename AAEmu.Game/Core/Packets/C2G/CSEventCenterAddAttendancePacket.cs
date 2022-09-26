using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSEventCenterAddAttendancePacket : GamePacket
    {
        public CSEventCenterAddAttendancePacket() : base(CSOffsets.CSEventCenterAddAttendancePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var itemTemplateId = stream.ReadUInt32();

            _log.Debug($"CSEventCenterAddAttendancePacket, itemTemplateId: {itemTemplateId}");
            
            //HousingManager.Instance.HouseTaxInfo(Connection, tl);
        }
    }
}
