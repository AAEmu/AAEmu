using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.World;

public class ZoneInstanceId(uint zoneId, uint instanceId) : PacketMarshaler
{
    public uint ZoneId { get; set; } = zoneId;
    public uint InstanceId { get; set; } = instanceId;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(ZoneId);
        stream.Write(InstanceId);
        return stream;
    }
}
