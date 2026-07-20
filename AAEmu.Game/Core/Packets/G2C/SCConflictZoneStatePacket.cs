using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCConflictZoneStatePacket(ushort zoneId, ZoneConflictType hpws, DateTime endTime)
    : GamePacket(SCOffsets.SCConflictZoneStatePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(zoneId);
        stream.Write((byte)hpws);
        stream.Write(endTime);
        return stream;
    }
}
