using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.World
{
    public class ZoneInstanceId : PacketMarshaler
    {
        public uint ZoneId { get; set; }
        public uint InstanceId { get; set; }

        public ZoneInstanceId(uint zoneId, uint instanceId)
        {
            ZoneId = zoneId;
            InstanceId = instanceId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(ZoneId);
            stream.Write(InstanceId);
            return stream;
        }
    }
}
