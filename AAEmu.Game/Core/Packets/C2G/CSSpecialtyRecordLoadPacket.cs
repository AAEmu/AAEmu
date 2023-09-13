using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSpecialtyRecordLoadPacket : GamePacket
    {
        public CSSpecialtyRecordLoadPacket() : base(CSOffsets.CSSpecialtyRecordLoadPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var zoneId = stream.ReadInt32();
            var id = stream.ReadUInt32();

            _log.Warn("CSSpecialtyRecordLoadPacket, ZoneId: {0}, Id: {1}", zoneId, id);
        }
    }
}
